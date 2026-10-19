using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Core;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// RapidDev 控制台兼容认证端点（/api/auth/*）：
///   - 协议：{code,message,data} 包装；登录 {userName,password} → {accessToken,refreshToken,expiresAt,sessionId}
///   - site-info / captcha / userinfo / menus 供控制台框架启动
///   - 与 AdminEndpoints 的原生 /api/auth/login 并存（{username,password} → {token,user}）
/// 控制台（RapidDev.AdminConsole / UserConsole）经反代把 /api 指向主控后，登录即用，无二次配置。
/// </summary>
public static class RapidDevAuthEndpoints
{
    public static void MapRapidDevAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        // 站点信息（控制台登录页展示）
        auth.MapGet("/site-info", () => Results.Json(new
        {
            code = 200,
            message = "操作成功",
            data = new
            {
                name = "TnorvCDN",
                subtitle = "自研 CDN 加速与管理平台",
                icp = "",
                psb = "",
                copyright = "Copyright © 2026 TnorvCDN",
            },
        }));

        // 验证码（未启用 → enabled=false，控制台跳过验证码 UI）
        auth.MapGet("/captcha", () => Results.Json(new
        {
            code = 200,
            message = "操作成功",
            data = new { enabled = false, captchaId = "" },
        }));

        auth.MapPost("/logout", async (HttpContext ctx, CdnDbContext db) =>
        {
            // 移除当前登录会话（个人中心设备管理）；sessionId 由前端 body/query 携带
            var raw = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(raw, out var uid))
            {
                var sid = ctx.Request.Query["sessionId"].FirstOrDefault()
                    ?? ctx.Request.Headers["X-Session-Id"].FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(sid))
                    await SessionEndpoints.UnregisterAsync(db, uid, sid);
            }
            return Results.Json(new { code = 200, message = "操作成功", data = (object?)null });
        }).AllowAnonymous();

        auth.MapPost("/refresh", () => Results.Json(new { code = 400, message = "请重新登录", data = (object?)null }))
            .AllowAnonymous();

        // 当前用户信息
        auth.MapGet("/userinfo", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var (userId, isAdmin) = await CurrentUserAsync(db, cfg);
            if (userId == null) return Results.Json(new { code = 401, message = "未登录", data = (object?)null });
            var user = await db.Users.FindAsync(userId.Value);
            if (user == null) return Results.Json(new { code = 401, message = "用户不存在", data = (object?)null });
            return Results.Json(new
            {
                code = 200,
                message = "操作成功",
                data = new
                {
                    id = user.Id,
                    userName = user.Username,
                    nickName = user.Username,
                    roles = new[] { user.IsAdmin ? "admin" : "user" },
                    permissions = user.IsAdmin ? new[] { "*:*:*" } : Array.Empty<string>(),
                },
            });
        }).AllowAnonymous();

        // 动态菜单：管理员=完整 CDN 管理菜单；普通用户=用户端菜单
        auth.MapGet("/menus", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var (userId, isAdmin) = await CurrentUserAsync(db, cfg);
            if (userId == null) return Results.Json(new { code = 401, message = "未登录", data = (object?)null });
            var user = await db.Users.FindAsync(userId.Value);
            var menus = user?.IsAdmin == true ? AdminMenus() : UserMenus();
            return Results.Json(new { code = 200, message = "操作成功", data = menus });
        }).AllowAnonymous();

        auth.MapGet("/permissions", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var (userId, _) = await CurrentUserAsync(db, cfg);
            if (userId == null) return Results.Json(new { code = 401, message = "未登录", data = (object?)null });
            var user = await db.Users.FindAsync(userId.Value);
            return Results.Json(new
            {
                code = 200,
                message = "操作成功",
                data = user?.IsAdmin == true ? new[] { "*:*:*" } : Array.Empty<string>(),
            });
        }).AllowAnonymous();
    }

    private static async Task<(long?, bool)> CurrentUserAsync(CdnDbContext db, IConfiguration cfg)
    {
        // 从 HTTP 上下文解析 JWT（AllowAnonymous + 手动校验，供框架统一封装）
        var http = new HttpContextAccessor().HttpContext;
        var header = http?.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ")) return (null, false);
        var token = header["Bearer ".Length..].Trim();
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"] ?? "dev-only-secret-change-me-32bytes!!"));
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "tnorv",
                ValidateAudience = true, ValidAudience = "tnorv",
                IssuerSigningKey = key,
                ValidateLifetime = true,
            }, out _);
            var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value;
            return (long.TryParse(idClaim, out var id) ? id : null, roleClaim == "admin");
        }
        catch
        {
            return (null, false);
        }
    }

    private static (string token, long expiresAt, string sessionId) IssueToken(User user, IConfiguration cfg)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"] ?? "dev-only-secret-change-me-32bytes!!"));
        var expires = DateTime.UtcNow.AddHours(8);
        var sessionId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.IsAdmin ? "admin" : "user"),
        };
        var token = new JwtSecurityToken(
            issuer: "tnorv", audience: "tnorv",
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token),
            new DateTimeOffset(expires).ToUnixTimeSeconds(), sessionId);
    }

    // ---- 菜单结构（与控制台路由一一对应） ----

    private static List<object> AdminMenus() => new()
    {
        Menu("数据看板", "dashboard", children: new[]
        {
            Item("/dashboard", "概况", "home"),
        }),
        Menu("网站列表", "world", children: new[]
        {
            Item("/cdn/servers", "网站列表", "world", "cdn:server:list"),
            Item("/cdn/servers/logs", "访问日志", "file-text", "cdn:server:log"),
            Item("/cdn/servers/certs", "证书管理", "certificate", "cdn:cert:list"),
            Item("/cdn/servers/traffic-stats", "用量统计", "chart-area", "cdn:stat:list"),
            Item("/cdn/servers/user-scripts", "边缘脚本", "script", "cdn:script:list"),
        }),
        Menu("边缘节点", "server-2", children: new[]
        {
            Item("/cdn/clusters", "集群列表", "server-2", "cdn:cluster:list"),
            Item("/cdn/clusters/ip-addrs", "节点IP", "ip", "cdn:cluster:list"),
            Item("/cdn/clusters/monitors", "区域监控", "activity", "cdn:monitor:list"),
            Item("/cdn/clusters/anti-ddos", "高防IP", "shield-lock", "cdn:antiddos:list"),
        }),
        Menu("域名解析", "cloud", children: new[]
        {
            Item("/cdn/dns", "集群列表", "cloud", "cdn:dns:list"),
            Item("/cdn/dns/icp", "备案审核", "clipboard-check", "cdn:dns:list"),
        }),
        Menu("日志审计", "history", children: new[]
        {
            Item("/system/operlog", "操作日志", "history", "system:operlog:list"),
            Item("/system/loginlog", "登录日志", "login", "system:loginlog:list"),
            Item("/system/sms", "短信设置", "message", "system:sms:settings"),
        }),
        Menu("系统设置", "settings", children: new[]
        {
            Item("/cdn/settings", "基础设置", "settings", "cdn:setting:list"),
            Item("/cdn/settings/advanced", "高级设置", "settings-2", "cdn:setting:list"),
            Item("/cdn/users/invitations", "邀请码管理", "mail-forward", "cdn:user:list"),
        }),
        Item("/profile", "个人中心", "user", hidden: true),
    };

    private static List<object> UserMenus() => new()
    {
        Menu("概览", "chart-line", children: new[]
        {
            Item("/dashboard", "概览", "chart-line"),
        }),
        Menu("CDN加速", "world", children: new[]
        {
            Item("/servers", "我的网站", "world", "user:server:list"),
            Item("/servers/cache", "刷新预热", "refresh", "user:server:cache"),
            Item("/servers/certs", "证书管理", "certificate", "user:cert:list"),
            Item("/servers/traffic-stats", "用量统计", "chart-area", "user:stat:list"),
            Item("/servers/icp", "备案管理", "clipboard-check", "user:server:list"),
            Item("/servers/cert-events", "证书签发日志", "scroll-text", "user:cert:list"),
        }),
        Menu("负载均衡", "arrows-split-2", children: new[]
        {
            Item("/lb", "负载均衡", "arrows-split-2", "user:lb:list"),
        }),
        Menu("WAF安全", "shield-half", children: new[]
        {
            Item("/waf", "概览", "shield-half", "user:waf:list"),
            Item("/waf/logs", "拦截日志", "file-text", "user:waf:list"),
            Item("/waf/iplists", "拦截IP", "list-details", "user:waf:list"),
            Item("/waf/settings", "全局规则", "settings", "user:waf:list"),
        }),
        Menu("域名解析", "cloud", children: new[]
        {
            Item("/ns/access", "NS托管接入", "cloud", "user:ns:list"),
            Item("/ns/domains", "我的域名", "world", "user:ns:list"),
            Item("/ns/domains/groups", "域名分组", "folder", "user:ns:list"),
            Item("/ns/domains/batch", "批量操作", "stack", "user:ns:list"),
            Item("/ns/routes", "线路管理", "route", "user:ns:list"),
            Item("/ns/plans", "套餐", "ticket", "user:ns:list"),
        }),
        Menu("套餐管理", "gift", children: new[]
        {
            Item("/plans", "套餐管理", "gift", "user:plan:list"),
        }),
        Menu("DDoS高防", "shield-lock", children: new[]
        {
            Item("/anti-ddos/instances", "实例列表", "shield-lock", "user:antiddos:list"),
            Item("/anti-ddos/packages", "购买实例", "shopping-cart", "user:antiddos:list"),
        }),
        Menu("财务管理", "wallet", children: new[]
        {
            Item("/finance", "概览", "wallet", "user:finance:list"),
            Item("/finance/bills", "费用账单", "receipt", "user:finance:list"),
            Item("/finance/logs", "收支明细", "arrows-left-right", "user:finance:list"),
            Item("/finance/charge", "充值", "coin", "user:finance:list"),
            Item("/finance/coupons", "优惠券", "discount", "user:finance:list"),
        }),
        Menu("工单系统", "messages", children: new[]
        {
            Item("/tickets", "工单系统", "messages", "user:ticket:list"),
        }),
        Menu("访问控制", "lock", children: new[]
        {
            Item("/acl", "访问控制", "lock", "user:acl:list"),
        }),
        Menu("消息", "bell", children: new[]
        {
            Item("/messages", "消息列表", "bell", "user:message:list"),
            Item("/notify", "消息提醒设置", "bell-ringing", "user:message:list"),
        }),
        Item("/profile", "个人中心", "user", hidden: true),
    };

    private static object Menu(string title, string icon, object[] children) => new
    {
        title, icon,
        type = "catalog",
        children,
    };

    private static object Item(string path, string title, string icon, string? perm = null, bool hidden = false) => new
    {
        url = path,
        title,
        icon,
        perm,
        hidden,
        type = "link",
    };

    public record RapidLoginRequest(string UserName, string Password, string? Uuid, string? Code, int? LoginType);
}
