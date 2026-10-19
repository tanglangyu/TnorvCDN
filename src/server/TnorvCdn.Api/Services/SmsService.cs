using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 短信服务（Tnorv SMS 网关）：
/// - 验证码：内存存取、IP/手机号流控、尝试次数限制；默认模板「您的验证码是{code}，请不要告诉其他人哟！」
/// - 提醒派发 NotifyAsync：全局启用项 ∩ 用户订阅项；内容禁止携带域名（短信风控严格）
/// - 网关调用：POST {ApiBase}/api/sms-gateway/send-api；密钥开启签名校验时附 timestamp + SHA256(apiKey+timestamp+apiSecret)
/// 用户引导：需到 https://sms.tnorv.com/ 完成实名认证→添加资质→报备签名→报备模板。
/// </summary>
public class SmsService(IServiceScopeFactory scopes, ILogger<SmsService> logger)
{
    private const string CodeTemplate = "您的验证码是{code}，请不要告诉其他人哟！";
    private (SmsSettings? Value, DateTime LoadedAt) _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    // ---- 验证码存储：mobile:scene → (code, expiresAt, attempts) ----
    private sealed record CodeEntry(string Code, DateTime ExpiresAt, int Attempts);
    private readonly ConcurrentDictionary<string, CodeEntry> _codes = new();

    // ---- 流控计数：key → (count, windowStart) ----
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rate = new();

    // ---- 已提醒去重（事件:用户:日）——账单/到期类提醒每天最多一条 ----
    private static readonly HashSet<string> _notifiedToday = new();

    public SmsSettings? GetSettings()
    {
        var (cached, at) = _cache;
        if (cached != null && DateTime.UtcNow - at < CacheTtl) return cached;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
            var row = db.SmsSettings.AsNoTracking().FirstOrDefault(s => s.Id == 1);
            _cache = (row, DateTime.UtcNow);
            return row;
        }
        catch { return null; }
    }

    public void Invalidate() => _cache = default;

    // ================= 验证码 =================

    /// <summary>发送验证码（匿名场景：手机号登录 / 绑定手机号）。返回 (ok, message)。</summary>
    public async Task<(bool Ok, string Message)> SendCodeAsync(string mobile, string scene, string? clientIp)
    {
        var st = GetSettings();
        if (st == null || !st.Enabled) return (false, "短信功能未启用");
        // 国别码归一：系统内统一存 00 前缀格式；暂只开放 0086 发码
        mobile = PhoneUtil.Normalize(mobile);
        if (!PhoneUtil.IsValid(mobile)) return (false, "手机号格式不正确");
        if (!PhoneUtil.IsSupported(mobile)) return (false, "该国家/地区暂未开放短信服务");

        // 流控：同号 60s 一条 / 同号日上限 / 同 IP 每小时 10 条
        if (_rate.TryGetValue($"m:{mobile}", out var last) && (DateTime.UtcNow - last.WindowStart).TotalSeconds < 60)
            return (false, "发送过于频繁，请 1 分钟后再试");
        var dayKey = $"d:{mobile}:{DateTime.UtcNow:yyyyMMdd}";
        if (GetRate(dayKey) >= Math.Max(1, st.DailyLimitPerMobile)) return (false, "该手机号今日发送次数已达上限");
        if (!string.IsNullOrEmpty(clientIp))
        {
            var ipKey = $"ip:{clientIp}:{DateTime.UtcNow:yyyyMMddHH}";
            if (GetRate(ipKey) >= 10) return (false, "操作过于频繁，请稍后再试");
        }

        var code = Random.Shared.Next(100000, 999999).ToString();
        var text = $"【{st.Signature}】" + CodeTemplate.Replace("{code}", code);
        var (ok, msg, batchId) = await GatewaySendAsync(st, [mobile], text);
        IncRate(dayKey); IncRate($"m:{mobile}");
        if (!string.IsNullOrEmpty(clientIp)) IncRate($"ip:{clientIp}:{DateTime.UtcNow:yyyyMMddHH}");
        await LogAsync(null, mobile, "verify", text, batchId, ok ? "queued" : "failed", msg);
        if (!ok) return (false, msg);

        _codes[$"{mobile}:{scene}"] = new CodeEntry(code, DateTime.UtcNow.AddSeconds(Math.Clamp(st.CodeExpireSeconds, 60, 900)), 0);
        return (true, "验证码已发送");
    }

    /// <summary>校验验证码（成功即消费；5 次失败作废）。</summary>
    public bool VerifyCode(string mobile, string scene, string code)
    {
        var key = $"{mobile}:{scene}";
        if (!_codes.TryGetValue(key, out var e)) return false;
        if (DateTime.UtcNow > e.ExpiresAt) { _codes.TryRemove(key, out _); return false; }
        if (e.Attempts >= 5) { _codes.TryRemove(key, out _); return false; }
        if (e.Code != code)
        {
            _codes[key] = e with { Attempts = e.Attempts + 1 };
            return false;
        }
        _codes.TryRemove(key, out _);
        return true;
    }

    // ================= 提醒派发 =================

    /// <summary>
    /// 发送事件提醒短信：全局启用 ∩ 用户订阅（管理员另需手机号已绑定）。
    /// dedupKey 相同的事件当日只发一条（如 sub_expiring:uid 每日一次）。
    /// 内容禁止包含域名/URL（短信风控），违反直接拒发并记日志。
    /// </summary>
    public async Task<bool> NotifyAsync(long userId, string eventKey, string content, string? dedupKey = null)
    {
        var st = GetSettings();
        if (st == null || !st.Enabled) return false;
        if (!SmsEvents.Keys.Contains(eventKey)) return false;
        var enabled = ParseList(st.EnabledEvents);
        if (!enabled.Contains(eventKey)) return false;

        if (!string.IsNullOrEmpty(dedupKey))
        {
            var today = $"nd:{dedupKey}:{DateTime.UtcNow:yyyyMMdd}";
            lock (_notifiedToday) { if (!_notifiedToday.Add(today)) return false; }
        }

        long? uid = userId;
        string mobile;
        bool subscribed;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
            if (u == null || string.IsNullOrEmpty(u.SmsPhone)) return false;
            mobile = PhoneUtil.Normalize(u.SmsPhone);   // 兼容存量裸号码
            subscribed = ParseList(u.SmsEvents).Contains(eventKey);
        }
        if (!subscribed) return false;
        var (ok, _) = await SendPlainAsync(uid, mobile, eventKey, content);
        return ok;
    }

    /// <summary>直接发送（管理端测试发送；已含风控检查）。content 不带签名前缀，自动补【签名】。</summary>
    public async Task<(bool Ok, string Message)> SendPlainAsync(long? userId, string mobile, string eventKey, string content)
    {
        var st = GetSettings();
        if (st == null || !st.Enabled) return (false, "短信功能未启用");
        // 风控：内容不得包含域名/URL（监管严格，链接类内容极易被拦截并影响签名信誉）
        if (ContainsUrlOrDomain(content))
        {
            await LogAsync(userId, mobile, eventKey, content, "", "blocked", "内容包含域名/链接（风控禁止）");
            return (false, "内容包含域名或链接，短信风控禁止发送");
        }
        var text = content.StartsWith('【') ? content : $"【{st.Signature}】{content}";
        var (ok, msg, batchId) = await GatewaySendAsync(st, [mobile], text);
        await LogAsync(userId, mobile, eventKey, text, batchId, ok ? "queued" : "failed", msg);
        return (ok, msg);
    }

    /// <summary>查询网关批次状态（管理端）。</summary>
    public async Task<string> QueryStatusAsync(string batchId)
    {
        var st = GetSettings();
        if (st == null) return "{\"code\":400,\"message\":\"短信未配置\"}";
        var body = new Dictionary<string, object> { ["apiKey"] = st.ApiKey, ["batchId"] = batchId };
        if (st.VerifySign) Sign(st, body);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var resp = await http.PostAsync($"{st.ApiBase}/api/sms-gateway/send-api/status",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { code = 500, message = ex.Message }); }
    }

    // ================= 网关 =================

    private static void Sign(SmsSettings st, Dictionary<string, object> body)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var raw = $"{st.ApiKey}{ts}{st.ApiSecret}";
        var sig = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        body["timestamp"] = ts.ToString();  // 网关要求字符串（数字会触发"请求参数错误"）
        body["signature"] = sig;
    }

    private async Task<(bool Ok, string Message, string BatchId)> GatewaySendAsync(SmsSettings st, string[] mobiles, string content)
    {
        if (string.IsNullOrWhiteSpace(st.ApiKey) || string.IsNullOrWhiteSpace(st.ApiSecret))
            return (false, "短信网关未配置 ApiKey/ApiSecret", "");
        var body = new Dictionary<string, object>
        {
            ["apiKey"] = st.ApiKey,
            ["mobiles"] = mobiles,
            ["content"] = content,
        };
        if (st.VerifySign) Sign(st, body);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var resp = await http.PostAsync($"{st.ApiBase}/api/sms-gateway/send-api",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : (int?)null;
            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var status = "";
            var batchId = "";
            if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                if (d.TryGetProperty("status", out var ds)) status = ds.GetString() ?? "";
                if (d.TryGetProperty("batchId", out var db2)) batchId = db2.GetString() ?? "";
                if (status == "blocked") return (false, "已被网关拦截: " + message, batchId);
            }
            if (code == 200 && success) return (true, "已受理", batchId);
            return (false, $"{message}（code={code}）", batchId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "短信网关调用失败");
            return (false, "网关调用失败: " + ex.Message, "");
        }
    }

    private async Task LogAsync(long? userId, string mobile, string evt, string content, string batchId, string status, string error)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
            db.SmsLogs.Add(new SmsLog
            {
                UserId = userId, Mobile = mobile, Event = evt, Content = content,
                BatchId = batchId, Status = status, Error = error ?? "", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "短信日志写入失败"); }
    }

    // ---- 工具 ----

    private static readonly System.Text.RegularExpressions.Regex DomainRegex =
        new(@"([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>风控：短信内容不允许携带域名/URL（允许【签名】中的中文，纯数字/手机号不影响）。</summary>
    public static bool ContainsUrlOrDomain(string content)
    {
        if (content.Contains("http://") || content.Contains("https://") || content.Contains("www.")) return true;
        return DomainRegex.IsMatch(content);
    }

    private static string[] ParseList(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? []; } catch { return []; }
    }

    private int GetRate(string key)
    {
        if (!_rate.TryGetValue(key, out var v)) return 0;
        return v.WindowStart.Date == DateTime.UtcNow.Date && (key.Contains(":H") == false || v.WindowStart.Hour == DateTime.UtcNow.Hour) ? v.Count : 0;
    }

    private void IncRate(string key)
    {
        _rate.AddOrUpdate(key, (1, DateTime.UtcNow), (_, v) => (v.Count + 1, v.WindowStart));
        // 简易清理：条目过多时清掉过期窗口
        if (_rate.Count > 10000)
        {
            foreach (var k in _rate.Keys)
                if (_rate.TryGetValue(k, out var v) && (DateTime.UtcNow - v.WindowStart).TotalHours > 26)
                    _rate.TryRemove(k, out _);
        }
    }
}
