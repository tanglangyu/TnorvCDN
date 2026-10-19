using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Data;

public class CdnDbContext(DbContextOptions<CdnDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public DbSet<Cluster> Clusters => Set<Cluster>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<AccessLogRecord> AccessLogs => Set<AccessLogRecord>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<ProbeRecord> ProbeRecords => Set<ProbeRecord>();
    public DbSet<ProtectionInstance> ProtectionInstances => Set<ProtectionInstance>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<NsDomain> NsDomains => Set<NsDomain>();
    public DbSet<SshGrant> SshGrants => Set<SshGrant>();
    public DbSet<FlowPackage> FlowPackages => Set<FlowPackage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SiteConfigTemplate> SiteConfigTemplates => Set<SiteConfigTemplate>();
    public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();
    public DbSet<CertEvent> CertEvents => Set<CertEvent>();
    public DbSet<DnsSettings> DnsSettings => Set<DnsSettings>();
    public DbSet<SmsSettings> SmsSettings => Set<SmsSettings>();
    public DbSet<SmsLog> SmsLogs => Set<SmsLog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<AccessKey> AccessKeys => Set<AccessKey>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<IpLibraryItem> IpLibrary => Set<IpLibraryItem>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Cluster>().HasIndex(c => c.Name).IsUnique();
        // Ddos 持久化为 Clusters."Ddos" jsonb 列（经 Cluster.DdosJson 属性；text→jsonb 显式转换）
        mb.Entity<Cluster>().Property(c => c.DdosJson).HasColumnName("Ddos")
          .HasColumnType("jsonb");
        mb.Entity<Cluster>().Property(c => c.OpsJson).HasColumnName("Ops")
          .HasColumnType("jsonb");
        mb.Entity<Cluster>().Property(c => c.DefaultsJson).HasColumnName("Defaults")
          .HasColumnType("jsonb");
        mb.Entity<Node>().HasIndex(n => n.Token).IsUnique().HasFilter("\"Token\" <> ''"); // 空串（未注册节点）不参与唯一约束
        mb.Entity<Node>().HasIndex(n => n.InstallToken);
        mb.Entity<User>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<Server>().HasIndex(s => s.ClusterId);
        mb.Entity<Server>().HasIndex(s => s.UserId);
        mb.Entity<Server>().HasIndex(s => s.DomainsJson);
        mb.Entity<AccessLogRecord>().HasIndex(l => new { l.ServerId, l.Timestamp });
        mb.Entity<ProbeRecord>().HasIndex(pr => new { pr.ServerId, pr.Timestamp });
        mb.Entity<AuditLog>().HasIndex(a => new { a.UserId, a.Timestamp });
        mb.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });
        mb.Entity<Subscription>().HasIndex(s => new { s.UserId, s.Enabled });

        // Ticket 对话时间线以 JSON 文本列存储（TicketMessage 无主键 POCO，不走实体映射）
        mb.Entity<Ticket>().Property(t => t.Messages)
          .HasConversion(
              v => JsonSerializer.Serialize(v ?? new List<TicketMessage>(), JsonOpts),
              v => (JsonSerializer.Deserialize<List<TicketMessage>>(v, JsonOpts) ?? new List<TicketMessage>()))
          .HasMaxLength(-1);
        mb.Entity<UsageRecord>().HasIndex(u => new { u.UserId, u.Year, u.Month }).IsUnique();

        // 复合配置对象以 JSON 列存储（EF Core 不映射嵌套类型，避免 owned type 主键问题）
        mb.Entity<Server>()
            .Property(s => s.Domains)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<string[]>(v, JsonOpts) ?? new string[0]);
        // DomainsJson 仅作 EF 列名占位，运行时不使用
        mb.Entity<Server>().Property(s => s.DomainsJson).HasColumnName("DomainsJson");

        mb.Entity<Server>()
            .Property(s => s.HttpPorts)
            .HasConversion(v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<int[]>(v, JsonOpts) ?? new int[0]);
        mb.Entity<Server>()
            .Property(s => s.HttpsPorts)
            .HasConversion(v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<int[]>(v, JsonOpts) ?? new int[0]);
        mb.Entity<Server>()
            .Property(s => s.L4Ports)
            .HasConversion(v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<int[]>(v, JsonOpts) ?? new int[0]);

        mb.Entity<Server>()
            .Property(s => s.Origin)
            .HasConversion(v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<OriginConfig>(v, JsonOpts) ?? new OriginConfig());
        mb.Entity<Server>()
            .Property(s => s.Cache)
            .HasConversion(v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<CacheConfig>(v, JsonOpts) ?? new CacheConfig());
        mb.Entity<Server>()
            .Property(s => s.Security)
            .HasConversion(v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<SecurityConfig>(v, JsonOpts) ?? new SecurityConfig());
        mb.Entity<Server>()
            .Property(s => s.AdvancedJson)
            .HasColumnName("AdvancedJson");
        mb.Entity<Server>().Ignore(s => s.Advanced);
    }
}
