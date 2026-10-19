using System.Collections.Concurrent;

namespace TnorvCdn.Api.Services;

/// <summary>缓存刷新指令注册表：管理端下发后随节点配置快照传递，节点执行后汇报。</summary>
public class PurgeRegistry
{
    private long _seq;
    // clusterId -> 待执行指令（节点拉取后按版本消费）
    private readonly ConcurrentDictionary<long, ConcurrentQueue<PurgeCommand>> _pending = new();

    public long Enqueue(long clusterId, string matchType, string host, string value)
    {
        var id = Interlocked.Increment(ref _seq);
        var q = _pending.GetOrAdd(clusterId, _ => new());
        q.Enqueue(new PurgeCommand(id, matchType, host ?? "", value ?? ""));
        return id;
    }

    /// <summary>取走某集群全部待执行指令（节点 config 拉取时调用）。</summary>
    public PurgeCommand[] Drain(long clusterId)
    {
        if (!_pending.TryGetValue(clusterId, out var q)) return [];
        var list = new List<PurgeCommand>();
        while (q.TryDequeue(out var cmd)) list.Add(cmd);
        return list.ToArray();
    }
}

public record PurgeCommand(long Id, string MatchType, string Host, string Value);
