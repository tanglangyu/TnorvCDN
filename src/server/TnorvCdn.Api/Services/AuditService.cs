using System.Security.Claims;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>审计日志：管理端敏感写操作落库（谁/何时/做了什么）。</summary>
public class AuditService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IServiceScopeFactory scopes, ILogger<AuditService> logger)
    { _scopes = scopes; _logger = logger; }

    public async Task LogAsync(string action, string detail)
    {
        try
        {
            var http = new HttpContextAccessor().HttpContext;
            var userId = 0L;
            var ip = "";
            if (http != null)
            {
                var idClaim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                long.TryParse(idClaim, out userId);
                ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
            }
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = action,
                Detail = detail.Length > 2000 ? detail[..2000] : detail,
                Ip = ip,
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[audit] 记录失败: {Message}", ex.Message);
        }
    }
}

/// <summary>审计日志记录。</summary>
public class AuditLog
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Action { get; set; } = "";    // server.create / cluster.update / user.delete ...
    public string Detail { get; set; } = "";
    public string Ip { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
