using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 短信功能端点：
/// - POST /api/sms-code/send（匿名验证码：手机号登录 / 绑定手机号，前端 smsApi.send 已对接）
/// - 管理端 /admin/sms/*：全局设置（启用/密钥/签名/提醒项/限额）、发送记录、测试发送、批次状态查询
/// - 用户端 /user/notify/*：绑定手机号（验证码校验）、订阅提醒项（与全局启用项取交集生效）
/// 引导文案：用户需到 https://sms.tnorv.com/ 完成实名认证→添加资质→报备签名→报备模板。
/// 风控：短信内容禁止携带域名/URL；单手机日限额；验证码 60s/条。
/// </summary>
public static class SmsEndpoints
{
    public static void MapSmsEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ---- 匿名验证码（前端 /sms-code/send）----
        api.MapPost("/sms-code/send", async (HttpRequest http, CdnDbContext db, SmsService sms) =>
        {
            string mobile = "", scene = "phone-login", countryCode = "0086";
            try
            {
                using var doc = await JsonDocument.ParseAsync(http.Body);
                if (doc.RootElement.TryGetProperty("mobile", out var m)) mobile = m.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("phone", out var ph2) && string.IsNullOrEmpty(mobile)) mobile = ph2.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("scene", out var s)) scene = s.GetString() ?? "phone-login";
                if (doc.RootElement.TryGetProperty("countryCode", out var cc) && cc.ValueKind == JsonValueKind.String)
                    countryCode = cc.GetString() ?? "0086";
            }
            catch { }
            if (scene is not ("phone-login" or "bind-phone")) scene = "phone-login";
            var ip = http.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var (ok, msg) = await sms.SendCodeAsync(Services.PhoneUtil.Normalize(mobile, countryCode), scene, ip);
            return Results.Json(new { code = ok ? 200 : 400, success = ok, message = msg, data = new { success = ok, message = msg } });
        }).AllowAnonymous();

        // ---- 管理端 ----
        var admin = app.MapGroup("/api/admin").RequireAuthorization();
        admin.MapGet("/sms/settings", async (CdnDbContext db) =>
        {
            var st = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1) ?? new SmsSettings();
            return Results.Json(new
            {
                code = 200, data = new
                {
                    st.Enabled, st.ApiBase, apiKey = Mask(st.ApiKey), hasSecret = !string.IsNullOrEmpty(st.ApiSecret),
                    st.Signature, st.VerifySign, st.EnabledEvents, st.DailyLimitPerMobile, st.CodeExpireSeconds, st.UpdatedAt,
                }
            });
        });
        admin.MapPut("/sms/settings", async (HttpRequest http, CdnDbContext db, SmsService sms, AuditService audit) =>
        {
            var st = await db.SmsSettings.FirstOrDefaultAsync(x => x.Id == 1);
            if (st == null) { st = new SmsSettings { Id = 1 }; db.SmsSettings.Add(st); }
            using var doc = await JsonDocument.ParseAsync(http.Body);
            var root = doc.RootElement;
            if (root.TryGetProperty("enabled", out var en)) st.Enabled = en.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("apiBase", out var ab)) st.ApiBase = ab.GetString() ?? st.ApiBase;
            if (root.TryGetProperty("apiKey", out var ak) && !string.IsNullOrEmpty(ak.GetString()) && ak.GetString()!.Contains('*') == false) st.ApiKey = ak.GetString()!;
            if (root.TryGetProperty("apiSecret", out var asc) && !string.IsNullOrEmpty(asc.GetString())) st.ApiSecret = asc.GetString()!; // 空值=不修改
            if (root.TryGetProperty("signature", out var sg)) st.Signature = sg.GetString() ?? st.Signature;
            if (root.TryGetProperty("verifySign", out var vs)) st.VerifySign = vs.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("enabledEvents", out var ev) && ev.ValueKind == JsonValueKind.Array)
                st.EnabledEvents = JsonSerializer.Serialize(ev.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => SmsEvents.Keys.Contains(x)).Distinct().ToList());
            if (root.TryGetProperty("dailyLimitPerMobile", out var dl) && dl.TryGetInt32(out var dli)) st.DailyLimitPerMobile = Math.Clamp(dli, 1, 100);
            if (root.TryGetProperty("codeExpireSeconds", out var ce) && ce.TryGetInt32(out var cei)) st.CodeExpireSeconds = Math.Clamp(cei, 60, 900);
            st.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            sms.Invalidate();
            _ = audit.LogAsync("sms.settings", $"enabled={st.Enabled} events={st.EnabledEvents}");
            return Results.Json(new { code = 200, message = "已保存" });
        });
        // 发送记录（分页 + 手机号/事件筛选）
        admin.MapGet("/sms/logs", async (CdnDbContext db, string? mobile, string? evt, int page = 1, int pageSize = 20) =>
        {
            var q = db.SmsLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(mobile)) q = q.Where(l => l.Mobile.Contains(mobile));
            if (!string.IsNullOrWhiteSpace(evt)) q = q.Where(l => l.Event == evt);
            var p = Math.Max(1, page); var ps = Math.Clamp(pageSize, 1, 200);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(l => l.Id).Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Json(new { code = 200, data = new { total, page = p, pageSize = ps, items } });
        });
        // 测试发送
        admin.MapPost("/sms/test", async (HttpRequest http, CdnDbContext db, SmsService sms, AuditService audit) =>
        {
            using var doc = await JsonDocument.ParseAsync(http.Body);
            var mobile = doc.RootElement.TryGetProperty("mobile", out var m) ? m.GetString() ?? "" : "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(mobile, "^1[3-9]\\d{9}$"))
                return Results.Json(new { code = 400, message = "手机号格式不正确" });
            var (ok, msg) = await sms.SendPlainAsync(null, mobile, "test", "TnorvCDN 短信通道测试，收到即代表签名与密钥配置正常。");
            _ = audit.LogAsync("sms.test", $"{mobile} ok={ok}");
            return Results.Json(new { code = ok ? 200 : 400, message = msg });
        });
        // 批次状态查询
        admin.MapGet("/sms/status", async (CdnDbContext db, SmsService sms, string batchId) =>
        {
            if (string.IsNullOrWhiteSpace(batchId)) return Results.Json(new { code = 400, message = "batchId 必填" });
            var json = await sms.QueryStatusAsync(batchId);
            return Results.Content(json, "application/json");
        });

        // ---- 用户端提醒设置 ----
        var user = app.MapGroup("/api/user").RequireAuthorization();
        user.MapGet("/notify/settings", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid.Value);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });
            var st = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1) ?? new SmsSettings();
            return Results.Json(new
            {
                code = 200, data = new
                {
                    smsEnabled = st.Enabled,
                    // 手机号脱敏展示：173****8880
                    phone = string.IsNullOrEmpty(u.SmsPhone) ? "" : MaskPhone(u.SmsPhone),
                    hasPhone = !string.IsNullOrEmpty(u.SmsPhone),
                    globalEvents = ParseList(st.EnabledEvents),
                    allEvents = SmsEvents.All.Select(x => new { key = x.Key, label = x.Label }),
                    subscribed = ParseList(u.SmsEvents),
                }
            });
        });
        // 发送绑定手机号验证码（需登录）
        user.MapPost("/notify/phone/send-code", async (HttpRequest http, CdnDbContext db, IConfiguration cfg, SmsService sms) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            using var doc = await JsonDocument.ParseAsync(http.Body);
            var mobile = doc.RootElement.TryGetProperty("phone", out var p) ? p.GetString() ?? "" : "";
            var ip = http.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var (ok, msg) = await sms.SendCodeAsync(mobile, "bind-phone", ip);
            return Results.Json(new { code = ok ? 200 : 400, message = msg });
        });
        // 保存提醒设置：绑定手机（携验证码时）+ 订阅项
        user.MapPut("/notify/settings", async (HttpRequest http, CdnDbContext db, IConfiguration cfg, SmsService sms) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == uid.Value);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });
            using var doc = await JsonDocument.ParseAsync(http.Body);
            var root = doc.RootElement;
            // 换绑手机号：须携带该新手机号收到的验证码
            if (root.TryGetProperty("phone", out var ph) && !string.IsNullOrEmpty(ph.GetString()))
            {
                var mobile = Services.PhoneUtil.Normalize(ph.GetString());
                if (!Services.PhoneUtil.IsValid(mobile))
                    return Results.Json(new { code = 400, message = "手机号格式不正确" });
                if (mobile != u.SmsPhone)
                {
                    var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                    if (!sms.VerifyCode(mobile, "bind-phone", code))
                        return Results.Json(new { code = 400, message = "验证码错误或已过期" });
                    // 手机号唯一性：已被其他账号绑定时拒绝（手机号登录凭据）
                    if (await db.Users.AnyAsync(x => x.Id != u.Id && x.SmsPhone == mobile))
                        return Results.Json(new { code = 400, message = "该手机号已被其他账号绑定" });
                    u.SmsPhone = mobile;
                }
            }
            // 订阅项：与事件目录取交集
            if (root.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Array)
                u.SmsEvents = JsonSerializer.Serialize(ev.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => SmsEvents.Keys.Contains(x)).Distinct().ToList());
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = "已保存" });
        });
    }

    private static string[] ParseList(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? []; } catch { return []; }
    }

    private static string Mask(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= 8 ? new string('*', s.Length) : s[..6] + "****" + s[^4..];

    private static string MaskPhone(string p) => Services.PhoneUtil.Mask(p);
}
