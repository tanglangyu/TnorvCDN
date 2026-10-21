using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services.Dns;
using TnorvCdn.Api.Endpoints;
using TnorvCdn.Api.Grpc;
using TnorvCdn.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// 数据库：默认 SQLite（开发/演示），生产切 PostgreSQL 只需换连接串与 Provider
// CentOS 8 的 glibc 过旧无法加载 SQLitePCLraw 原生库时，可切换 Npgsql：
//   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
builder.Services.AddDbContext<CdnDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default") ?? "Data Source=tnorv.db";
    if (cs.StartsWith("Host="))
        o.UseNpgsql(cs);
    else
        o.UseSqlite(cs);
});

// gRPC
builder.Services.AddGrpc();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddHostedService<BillingSchedulerService>();

// 配置发布：队列化（所有变更入队，后台单线程串行发布，防大量修改打爆节点）
builder.Services.AddSingleton<ConfigPublishQueue>();
builder.Services.AddSingleton<ChallengeRegistry>();
builder.Services.AddScoped<ConfigPublisher>();
builder.Services.AddHostedService<ConfigPublishWorker>();
builder.Services.AddSingleton<PurgeRegistry>();
builder.Services.AddSingleton<SmsService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<IDnsProvider, AggregatedDnsProvider>();
builder.Services.AddSingleton<IDnsProvider, AliyunDnsProvider>();
builder.Services.AddSingleton<IDnsProvider, DnspodProvider>();
builder.Services.AddSingleton<IDnsProvider, HuaweiDnsProvider>();
builder.Services.AddSingleton<IDnsProvider, CloudflareDnsProvider>();
builder.Services.AddSingleton<DnsSettingsService>();
builder.Services.AddSingleton<DnsService>();
builder.Services.AddHttpClient("dns");
builder.Services.AddSingleton<GeoIpService>();
builder.Services.AddHostedService<GeoIpBackgroundService>();

// 管理端缓存刷新指令入口（挂在 PurgeRegistry，节点经配置快照消费）
// POST /api/admin/cache/purge  body: { matchType: url|directory|host|all, host, value }

// JWT 认证
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            builder.Configuration["Jwt:Key"] ?? "dev-only-secret-change-me-32bytes!!"));
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "tnorv", ValidAudience = "tnorv",
            IssuerSigningKey = key, ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
        // SignalR query token + API 密钥（ak_ 前缀）在 OnMessageReceived 统一拦截
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = async context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                var header = context.Request.Headers.Authorization.FirstOrDefault();
                if (header != null && header.StartsWith("Bearer ak_", StringComparison.Ordinal))
                {
                    // AK 认证：查库比对 SHA256 哈希，命中则注入身份（Bearer ak_xxx.sk_xxx）
                    var (ok, userId) = await AccessKeyEndpoints.VerifyAsync(
                        context.HttpContext.RequestServices.GetRequiredService<CdnDbContext>(), header);
                    if (!ok) return; // 交给 JWT 流程 → 401
                    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, (userId ?? 0).ToString()) };
                    if (userId == null) claims.Add(new Claim(ClaimTypes.Role, "admin"));
                    var identity = new ClaimsIdentity(claims, "AccessToken");
                    context.Principal = new ClaimsPrincipal(identity);
                    context.Success();
                }
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// 自动建库 + 种子管理员（首次启动）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
    db.Database.EnsureCreated();
    // EnsureCreated 不会为已有库补建新表：手动补齐后续迭代新增的计费表（幂等）
    var conn = db.Database.GetConnectionString() ?? "";
    if (conn.StartsWith("Host="))
    {
        // 手动补齐后续迭代新增的表（幂等）。用 Npgsql 原生执行，绕开 ExecuteSqlRaw 的
        // composite format 解析（SQL 内 { } 会被误当格式占位符）。
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(conn);
        await using var npg = new Npgsql.NpgsqlConnection(csb.ConnectionString);
        await npg.OpenAsync();
        await using var cmd = npg.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "Plans" (
                "Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL,
                "MaxServers" INT NOT NULL DEFAULT 10, "MaxDomains" INT NOT NULL DEFAULT 20,
                "MonthlyTrafficMB" BIGINT NOT NULL DEFAULT 51200, "PriceCents" NUMERIC NOT NULL DEFAULT 0,
                "Enabled" BOOL NOT NULL DEFAULT TRUE, "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "Subscriptions" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NOT NULL, "PlanId" BIGINT NOT NULL,
                "StartsAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(), "ExpiresAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "Enabled" BOOL NOT NULL DEFAULT TRUE);
            CREATE TABLE IF NOT EXISTS "UsageRecords" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NOT NULL,
                "Year" INT NOT NULL, "Month" INT NOT NULL,
                "Requests" BIGINT NOT NULL DEFAULT 0, "TrafficMB" BIGINT NOT NULL DEFAULT 0,
                "PeakBandwidthKbps" BIGINT NOT NULL DEFAULT 0, "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "Certificates" (
                "Id" BIGSERIAL PRIMARY KEY, "ClusterId" BIGINT NOT NULL DEFAULT 1,
                "Domains" TEXT NOT NULL DEFAULT '', "CertPem" TEXT NOT NULL DEFAULT '', "KeyPem" TEXT NOT NULL DEFAULT '',
                "ExpiresAt" TIMESTAMPTZ NULL, "Source" TEXT NOT NULL DEFAULT 'manual', "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "Notifications" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NULL,
                "Title" TEXT NOT NULL DEFAULT '', "Content" TEXT NOT NULL DEFAULT '',
                "Type" INT NOT NULL DEFAULT 0, "IsRead" BOOL NOT NULL DEFAULT FALSE,
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE INDEX IF NOT EXISTS ix_notifications_user_read ON "Notifications" ("UserId","IsRead");
            CREATE TABLE IF NOT EXISTS "SiteConfigTemplates" (
                "Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL DEFAULT '',
                "Description" TEXT NOT NULL DEFAULT '', "BuiltIn" BOOL NOT NULL DEFAULT FALSE,
                "ConfigJson" TEXT NOT NULL DEFAULT '', "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE INDEX IF NOT EXISTS ix_site_templates_name ON "SiteConfigTemplates" ("Name");
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslMode" TEXT NOT NULL DEFAULT 'auto';
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "Group" TEXT NULL;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslState" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslIssuedDomains" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslExpiresAt" TIMESTAMPTZ NULL;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslLastAttemptAt" TIMESTAMPTZ NULL;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslLastError" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "Clusters" ADD COLUMN IF NOT EXISTS "ConfigVersion" BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE "Clusters" ADD COLUMN IF NOT EXISTS "Ops" JSONB NULL;
            ALTER TABLE "Clusters" ADD COLUMN IF NOT EXISTS "Defaults" JSONB NULL;
            CREATE TABLE IF NOT EXISTS "AcmeAccounts" (
                "Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL DEFAULT '',
                "EmailTemplate" TEXT NOT NULL DEFAULT '', "DefaultEmail" TEXT NOT NULL DEFAULT '',
                "Enabled" BOOL NOT NULL DEFAULT TRUE, "Note" TEXT NOT NULL DEFAULT '',
                "Provider" TEXT NOT NULL DEFAULT 'letsencrypt', "DirectoryUrl" TEXT NOT NULL DEFAULT '',
                "BackupDirectoryUrl" TEXT NOT NULL DEFAULT '', "EabKid" TEXT NOT NULL DEFAULT '', "EabHmacKey" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            ALTER TABLE "AcmeAccounts" ADD COLUMN IF NOT EXISTS "Provider" TEXT NOT NULL DEFAULT 'letsencrypt';
            ALTER TABLE "AcmeAccounts" ADD COLUMN IF NOT EXISTS "DirectoryUrl" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "AcmeAccounts" ADD COLUMN IF NOT EXISTS "BackupDirectoryUrl" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "AcmeAccounts" ADD COLUMN IF NOT EXISTS "EabKid" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "AcmeAccounts" ADD COLUMN IF NOT EXISTS "EabHmacKey" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "SslRenewRequestedAt" TIMESTAMPTZ NULL;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "AdvancedJson" JSONB NULL;
            ALTER TABLE "Servers" ALTER COLUMN "AdvancedJson" TYPE TEXT;
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "UserId" BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "Type" TEXT NOT NULL DEFAULT 'A';
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "Value" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "Ttl" INT NOT NULL DEFAULT 600;
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "SyncStatus" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "Kind" TEXT NOT NULL DEFAULT 'record';
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "ServerId" BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "Delegated" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "NsDomains" ADD COLUMN IF NOT EXISTS "DelegatedCheckedAt" TIMESTAMPTZ NULL;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "AccessMode" TEXT NOT NULL DEFAULT 'cname';
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "AccessModeChangedAt" TIMESTAMPTZ NULL;
            CREATE TABLE IF NOT EXISTS "CertEvents" (
                "Id" BIGSERIAL PRIMARY KEY, "ServerId" BIGINT NOT NULL DEFAULT 0, "NodeId" BIGINT NOT NULL DEFAULT 0,
                "Domain" TEXT NOT NULL DEFAULT '', "Status" TEXT NOT NULL DEFAULT '', "Error" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE INDEX IF NOT EXISTS ix_cert_events_server ON "CertEvents" ("ServerId","CreatedAt" DESC);
            CREATE INDEX IF NOT EXISTS ix_cert_events_created ON "CertEvents" ("CreatedAt" DESC);
            CREATE TABLE IF NOT EXISTS "DnsSettings" (
                "Id" BIGSERIAL PRIMARY KEY, "Enabled" BOOL NOT NULL DEFAULT TRUE,
                "Provider" TEXT NOT NULL DEFAULT 'aggregated', "DefaultDomainId" TEXT NOT NULL DEFAULT '',
                "ProvidersJson" TEXT NOT NULL DEFAULT '', "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "Orders" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NOT NULL, "OrderNo" TEXT NOT NULL DEFAULT '',
                "Kind" TEXT NOT NULL DEFAULT 'recharge', "PlanId" BIGINT NULL, "FlowPackageId" BIGINT NULL,
                "Months" INT NOT NULL DEFAULT 1, "AmountCents" NUMERIC NOT NULL DEFAULT 0,
                "Status" TEXT NOT NULL DEFAULT 'pending', "PayMethod" TEXT NOT NULL DEFAULT 'balance',
                "CouponCode" TEXT NULL, "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(), "PaidAt" TIMESTAMPTZ NULL);
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "LocationsJson" JSONB NULL;
            ALTER TABLE "Servers" ALTER COLUMN "LocationsJson" TYPE TEXT;
            CREATE TABLE IF NOT EXISTS "Posts" (
                "Id" BIGSERIAL PRIMARY KEY, "Title" TEXT NOT NULL DEFAULT '', "Content" TEXT NOT NULL DEFAULT '',
                "Enabled" BOOL NOT NULL DEFAULT TRUE, "Popup" BOOL NOT NULL DEFAULT FALSE,
                "Audience" TEXT NOT NULL DEFAULT 'user', "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "AccessKeys" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NOT NULL DEFAULT 0, "Name" TEXT NOT NULL DEFAULT '',
                "KeyId" TEXT NOT NULL DEFAULT '', "KeySecret" TEXT NOT NULL DEFAULT '',
                "Enabled" BOOL NOT NULL DEFAULT TRUE, "LastUsedAt" TIMESTAMPTZ NULL,
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE INDEX IF NOT EXISTS ix_access_keys_user ON "AccessKeys" ("UserId");
            CREATE TABLE IF NOT EXISTS "UserIdentities" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NOT NULL DEFAULT 0,
                "Type" TEXT NOT NULL DEFAULT 'personal', "RealName" TEXT NOT NULL DEFAULT '',
                "IdNumber" TEXT NOT NULL DEFAULT '', "EnterpriseName" TEXT NOT NULL DEFAULT '',
                "Status" TEXT NOT NULL DEFAULT 'pending', "Note" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(), "ReviewedAt" TIMESTAMPTZ NULL);
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Features" TEXT NULL;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "L4ProxyProtocol" BOOL NOT NULL DEFAULT FALSE;
            ALTER TABLE "Tickets" ADD COLUMN IF NOT EXISTS "Category" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "Tickets" ADD COLUMN IF NOT EXISTS "Messages" JSONB NOT NULL DEFAULT '[]';
            ALTER TABLE "Tickets" ALTER COLUMN "Messages" TYPE TEXT;
            ALTER TABLE "Servers" ADD COLUMN IF NOT EXISTS "RealtimePublish" BOOL NOT NULL DEFAULT FALSE;
            ALTER TABLE "Plans" ADD COLUMN IF NOT EXISTS "Features" TEXT NULL;
            ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "AutoRenew" BOOL NOT NULL DEFAULT FALSE;
            ALTER TABLE "ProtectionInstances" ADD COLUMN IF NOT EXISTS "UserId" BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SmsPhone" TEXT NOT NULL DEFAULT '';
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SmsEvents" TEXT NOT NULL DEFAULT '[]';
            -- 存量手机号补国别码（裸 11 位 → 0086 前缀），幂等
            UPDATE "Users" SET "SmsPhone" = '0086' || "SmsPhone" WHERE "SmsPhone" ~ '^1[3-9]\d{9}$';
            UPDATE "SmsLogs" SET "Mobile" = '0086' || "Mobile" WHERE "Mobile" ~ '^1[3-9]\d{9}$';
            CREATE TABLE IF NOT EXISTS "SmsSettings" (
                "Id" BIGSERIAL PRIMARY KEY, "Enabled" BOOL NOT NULL DEFAULT FALSE,
                "ApiBase" TEXT NOT NULL DEFAULT '',
                "ApiKey" TEXT NOT NULL DEFAULT '', "ApiSecret" TEXT NOT NULL DEFAULT '',
                "Signature" TEXT NOT NULL DEFAULT '', "VerifySign" BOOL NOT NULL DEFAULT TRUE,
                "EnabledEvents" TEXT NOT NULL DEFAULT '[]', "DailyLimitPerMobile" INT NOT NULL DEFAULT 10,
                "CodeExpireSeconds" INT NOT NULL DEFAULT 300, "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "UserSessions" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NOT NULL, "SessionId" TEXT NOT NULL DEFAULT '',
                "Ip" TEXT NOT NULL DEFAULT '', "UserAgent" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(), "LastSeenAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "SmsLogs" (
                "Id" BIGSERIAL PRIMARY KEY, "UserId" BIGINT NULL, "Mobile" TEXT NOT NULL DEFAULT '',
                "Event" TEXT NOT NULL DEFAULT '', "Content" TEXT NOT NULL DEFAULT '',
                "BatchId" TEXT NOT NULL DEFAULT '', "Status" TEXT NOT NULL DEFAULT '',
                "Error" TEXT NOT NULL DEFAULT '', "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            CREATE TABLE IF NOT EXISTS "IpLibrary" (
                "Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL DEFAULT '',
                "Kind" TEXT NOT NULL DEFAULT 'deny', "Cidrs" TEXT NOT NULL DEFAULT '',
                "Note" TEXT NOT NULL DEFAULT '', "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW());
            """;
        await cmd.ExecuteNonQueryAsync();
    }
    // DNS 配置只存数据库：首次启动把 appsettings.json 的 Dns 节一次性迁移入 DnsSettings 表
    await scope.ServiceProvider.GetRequiredService<DnsSettingsService>().SeedFromConfigIfMissingAsync();
    // 内置站点配置模板（一键应用：HTTPS/缓存/安全预设），幂等
    if (!db.SiteConfigTemplates.Any(t => t.BuiltIn))
    {
        db.SiteConfigTemplates.AddRange(
            new SiteConfigTemplate
            {
                Name = "标准网站加速", BuiltIn = true,
                Description = "自动HTTPS + 智能缓存，适合博客/企业站",
                ConfigJson = """{"sslMode":"auto","forceHttps":true,"cache":{"enabled":true,"rules":[{"ext":["jpg","jpeg","png","gif","webp","css","js"],"ttl":86400},{"ext":["html","htm"],"ttl":600}]}}""",
            },
            new SiteConfigTemplate
            {
                Name = "电商防护", BuiltIn = true,
                Description = "HTTPS + CC防护 + 5秒盾 + UA过滤，适合电商/活动页",
                ConfigJson = """{"sslMode":"auto","forceHttps":true,"cache":{"enabled":true,"rules":[{"ext":["jpg","png","css","js"],"ttl":3600}]},"security":{"cc":{"enabled":true,"threshold":120,"windowSeconds":60,"banSeconds":300,"excludePaths":[]},"jsShield":{"enabled":false,"mode":"js","cookieSeconds":1800,"maxFails":3,"banSeconds":600,"excludePaths":["/api/"]},"uaRules":[],"ipWhitelist":[],"ipBlacklist":[],"urlAuth":{"enabled":false}}}""",
            },
            new SiteConfigTemplate
            {
                Name = "高安全模式", BuiltIn = true,
                Description = "强制HTTPS + 5秒盾 + URL防盗链 + CC高防，适合被攻击站点",
                ConfigJson = """{"sslMode":"auto","forceHttps":true,"cache":{"enabled":true,"rules":[{"ext":["jpg","png","css","js"],"ttl":86400}]},"security":{"cc":{"enabled":true,"threshold":60,"windowSeconds":60,"banSeconds":600,"excludePaths":[]},"jsShield":{"enabled":true,"mode":"js","cookieSeconds":1800,"maxFails":2,"banSeconds":3600,"excludePaths":["/api/"]},"urlAuth":{"enabled":true,"type":"a","key":"tnorv-urlauth-key","expirySeconds":3600},"uaRules":[],"ipWhitelist":[],"ipBlacklist":[],"deniedCountries":[],"deniedCidrs":[]}}""",
            });
        db.SaveChanges();
        app.Logger.LogInformation("已初始化内置站点配置模板");
    }

    if (!db.Clusters.Any())
    {
        db.Clusters.Add(new Cluster { Name = "默认集群" });
        db.SaveChanges();
        app.Logger.LogInformation("已初始化默认集群");
    }
    // 管理员账号不再自动创建：全新系统首次打开控制台会进入安装引导（/setup）设置管理员账号
}

app.MapSetupEndpoints();
app.MapAdminEndpoints();
app.MapNodeApiEndpoints();
app.MapRapidDevAuth();
app.MapUserCdnEndpoints();
app.MapAdminExtraEndpoints();
app.MapUserCdnExtraEndpoints();
app.MapUserPayEndpoints();
app.MapUserIcpEndpoints();
app.MapUserServerCrudEndpoints();
app.MapUserServerSettingsEndpoints();
app.MapAccessKeyEndpoints();
app.MapNotificationEndpoints();
app.MapSmsEndpoints();
app.MapSessionEndpoints();
app.MapHub<NotificationHub>("/hubs/notification");
app.MapGrpcService<NodeGrpcService>();
app.MapGrpcService<LogGrpcService>();

// 管理后台前端（Vue3 SPA）
app.UseDefaultFiles();
app.UseStaticFiles();
// 节点二进制下载目录（自升级指令 url 指向 /downloads/*）：
// 显式端点返回 octet-stream（静态中间件在此环境不稳定，且可防 SPA fallback 返回 HTML 误装）
app.MapGet("/downloads/{file}", (string file) =>
{
    // Path.GetFileName 防路径穿越；只允许读 /opt/downloads 下的文件
    var safe = Path.GetFileName(file);
    var path = Path.Combine("/opt/downloads", safe);
    if (!File.Exists(path)) return Results.NotFound();
    return Results.File(path, "application/octet-stream", safe);
});
// SPA fallback：仅对非 API/gRPC 路径返回 index.html
app.MapFallback(async ctx =>
{
    var path = ctx.Request.Path;
    if (path.StartsWithSegments("/api") || path.StartsWithSegments("/grpc") || path.StartsWithSegments("/health"))
    {
        ctx.Response.StatusCode = 404;
        return;
    }
    ctx.Response.ContentType = "text/html";
    // /user/* → 用户控制台 SPA，其余 → 管理控制台
    if (path.StartsWithSegments("/user"))
        await ctx.Response.SendFileAsync("wwwroot/user/index.html");
    else
        await ctx.Response.SendFileAsync("wwwroot/index.html");
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// 节点离线告警后台任务（60s 周期；心跳超 2 分钟视为离线并发送通知）
var notifyScopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
_ = Task.Run(async () =>
{
    var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
    var notify = app.Services.GetRequiredService<NotificationService>();
    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            using var scope = notifyScopeFactory.CreateScope();
            await notify.CheckNodeOfflineAsync(scope);
        }
        catch { /* 告警检查失败不影响主服务 */ }
    }
});

app.Run();
