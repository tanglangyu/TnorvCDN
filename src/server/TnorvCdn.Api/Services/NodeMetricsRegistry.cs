using System.Collections.Concurrent;

namespace TnorvCdn.Api.Services;

/// <summary>节点实时指标注册表（心跳上报写入内存，看板聚合读取；重启即空，仅作实时展示）。</summary>
public static class NodeMetricsRegistry
{
    public record Entry(double CpuUsage, double MemUsage, double Load, uint Connections, long ConfigVersion, DateTime At);

    private static readonly ConcurrentDictionary<long, Entry> _byNode = new();

    public static void Update(long nodeId, double cpu, double mem, double load, uint conns, long configVersion)
        => _byNode[nodeId] = new Entry(cpu, mem, load, conns, configVersion, DateTime.UtcNow);

    /// <summary>近 2 分钟内上报过的节点指标。</summary>
    public static List<(long NodeId, Entry E)> Fresh()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        return _byNode.Where(kv => kv.Value.At >= cutoff).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
