using System.Text;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 通知服务：WebHook/邮件媒介、节点离线告警、证书到期提醒。
/// 采用"通知媒介 + 告警策略"模式；通知发送异步不阻塞业务。
/// </summary>
public class NotificationService(
    IConfiguration cfg,
    ILogger<NotificationService> logger,
    IHttpClientFactory httpFactory)
{
    private static readonly HashSet<string> LastOffline = new(); // 已告警节点（防重复）

    /// <summary>检查节点在线状态，对刚离线的节点发送告警（由后台任务周期调用）。</summary>
    public async Task CheckNodeOfflineAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<Data.CdnDbContext>();
        var now = DateTime.UtcNow;
        var offline = await db.Nodes.Where(n => n.IsOnline && n.LastHeartbeat != null &&
            (now - n.LastHeartbeat.Value).TotalMinutes > 2).ToListAsync();
        foreach (var node in offline)
        {
            node.IsOnline = false;
            await SendAsync("node_offline", $"节点离线告警：{node.Name}（{node.Hostname ?? node.Id.ToString()}）已超过 2 分钟无心跳", new
            {
                type = "node_offline",
                nodeId = node.Id, nodeName = node.Name, hostname = node.Hostname,
                lastHeartbeat = node.LastHeartbeat
            });
        }
        if (offline.Count > 0) await db.SaveChangesAsync();
    }

    /// <summary>发送通知：WebHook（企业微信/钉钉/自定义 JSON）与 SMTP 邮件。</summary>
    public async Task SendAsync(string eventType, string title, object payload)
    {
        var hookUrl = cfg["Notifications:WebHookUrl"];
        if (!string.IsNullOrEmpty(hookUrl))
        {
            try
            {
                var http = httpFactory.CreateClient("notify");
                http.Timeout = TimeSpan.FromSeconds(5);
                var body = JsonSerializer.Serialize(new
                {
                    msgtype = "text",
                    text = new { content = $"[TnorvCDN {eventType}] {title}" },
                    @event = eventType,
                    data = payload
                });
                await http.PostAsync(hookUrl, new StringContent(body, Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                logger.LogWarning("WebHook 通知失败: {Message}", ex.Message);
            }
        }
        // SMTP 邮件（可选配置 Notifications:Smtp:Host/Port/From/User/Pass/To）
        await SendEmailAsync(title, JsonSerializer.Serialize(payload));

        logger.LogWarning("[notify:{Event}] {Title} {@Payload}", eventType, title, payload);
    }

    /// <summary>SMTP 邮件通知（未配置 Host 则跳过）。</summary>
    public async Task SendEmailAsync(string subject, string body)
    {
        var host = cfg["Notifications:Smtp:Host"];
        if (string.IsNullOrEmpty(host)) return;
        try
        {
            var port = cfg.GetValue("Notifications:Smtp:Port", 587);
            var from = cfg["Notifications:Smtp:From"] ?? "";
            var user = cfg["Notifications:Smtp:User"] ?? "";
            var pass = cfg["Notifications:Smtp:Pass"] ?? "";
            var to = (cfg["Notifications:Smtp:To"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, pass),
            };
            var msg = new MailMessage(from, string.Join(",", to), $"[TnorvCDN] {subject}", body)
            { IsBodyHtml = false };
            await client.SendMailAsync(msg);
        }
        catch (Exception ex)
        {
            logger.LogWarning("SMTP 邮件发送失败: {Message}", ex.Message);
        }
    }
}
