using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// 纵向时间流中可绘制节点的类别。
/// </summary>
internal enum TimelineNodeKind
{
    Year,
    Quadrum,
    Summary,
    Memory
}

/// <summary>
/// 已完成布局的时间流节点。Y/Height 使用滚动内容坐标，而非屏幕坐标。
/// </summary>
internal sealed class TimelineNode
{
    public TimelineNodeKind Kind;
    public string Key;
    public string Label;
    public int Tick;
    public MemoryEntry Memory;
    public List<MemoryEntry> GroupMemories = new();
    public int Depth;
    public float Height;
    public float Y;
}

/// <summary>
/// 将四层组件中的 ABM、SCM 和 ELS 转换为年 → 象 → ELS → 具体经历的展示树。
/// CLPA 由横向篇章导航器单独展示，不进入本模型。
/// </summary>
internal sealed class MemoryTimelineModel
{
    /// <summary>
    /// 根据当前筛选和折叠状态生成一帧所需的扁平可见节点，并计算内容高度位置。
    /// </summary>
    public List<TimelineNode> Build(
        FourLayerMemoryComp comp,
        MemoryTabContext context,
        int currentTick)
    {
        List<MemoryEntry> concrete = comp.ActiveMemories
            .Concat(comp.SituationalMemories)
            .Where(memory => IsVisible(memory, context))
            .OrderBy(memory => memory.GameTick)
            .ToList();
        List<MemoryEntry> summaries = comp.EventLogMemories
            .Where(memory => IsVisible(memory, context))
            .OrderBy(memory => memory.GameTick)
            .ToList();

        // ELS 与具体经历先建立推断时段，再应用标签筛选，保证命中子项时仍保留总结节点路径。
        List<TimelineItem> items = BuildItems(concrete, summaries);
        if (!string.IsNullOrWhiteSpace(context.TagFilter))
        {
            string query = context.TagFilter.Trim();
            items = items
                .Select(item => item.Filter(query))
                .Where(item => item is not null)
                .ToList();
        }

        List<TimelineNode> nodes = new();
        foreach (IGrouping<int, TimelineItem> yearGroup in items
                     .OrderByDescending(item => item.Tick)
                     .GroupBy(item => GenDate.Year(GenDate.TickGameToAbs(item.Tick), 0f))
                     .OrderByDescending(group => group.Key))
        {
            int year = yearGroup.Key;
            string yearKey = $"year:{year}";
            List<MemoryEntry> yearMemories = yearGroup.SelectMany(item => item.AllMemories).Distinct().ToList();
            nodes.Add(Header(TimelineNodeKind.Year, yearKey, $"{year}年", yearGroup.Max(item => item.Tick), 0, yearMemories));
            if (context.CollapsedGroups.Contains(yearKey)) continue;

            foreach (IGrouping<Quadrum, TimelineItem> quadrumGroup in yearGroup
                         .GroupBy(item => GenDate.Quadrum(GenDate.TickGameToAbs(item.Tick), 0f))
                         .OrderByDescending(group => group.Key))
            {
                string quadrumKey = $"quadrum:{year}:{quadrumGroup.Key}";
                List<MemoryEntry> quadrumMemories = quadrumGroup.SelectMany(item => item.AllMemories).Distinct().ToList();
                nodes.Add(Header(TimelineNodeKind.Quadrum, quadrumKey, quadrumGroup.Key.Label(), quadrumGroup.Max(item => item.Tick), 1, quadrumMemories));
                if (context.CollapsedGroups.Contains(quadrumKey)) continue;

                foreach (TimelineItem item in quadrumGroup.OrderByDescending(entry => entry.Tick))
                {
                    if (item.Summary is not null)
                    {
                        string summaryKey = $"summary:{item.Summary.Id}";
                        // 每个 ELS 只在首次出现时应用默认折叠；用户之后的手动展开不会被下一帧覆盖。
                        if (context.SeenGroups.Add(summaryKey) && item.Tick < currentTick - 3 * GenDate.TicksPerDay)
                            context.CollapsedGroups.Add(summaryKey);

                        nodes.Add(new TimelineNode
                        {
                            Kind = TimelineNodeKind.Summary,
                            Key = summaryKey,
                            Label = MemoryArchiveText.Layer(MemoryLayer.EventLog),
                            Tick = item.Tick,
                            Memory = item.Summary,
                            GroupMemories = item.AllMemories.Distinct().ToList(),
                            Depth = 2,
                            Height = 108f
                        });
                        if (context.CollapsedGroups.Contains(summaryKey)) continue;
                    }

                    foreach (MemoryEntry memory in item.Concrete.OrderByDescending(entry => entry.GameTick))
                        nodes.Add(MemoryNode(memory, item.Summary is null ? 2 : 3));
                }
            }
        }

        float y = 0f;
        TimelineNode previous = null;
        foreach (TimelineNode node in nodes)
        {
            // 时间流按内容密度布局。长时间无记录只增加少量留白，不严格映射真实时长。
            if (previous is not null && previous.Kind is TimelineNodeKind.Memory && node.Kind is TimelineNodeKind.Memory)
            {
                int gap = Math.Abs(previous.Tick - node.Tick);
                if (gap > GenDate.TicksPerDay)
                    y += Math.Min(18f, gap / (float)GenDate.TicksPerDay * 2f);
            }
            node.Y = y;
            y += node.Height + 6f;
            previous = node;
        }

        return nodes;
    }

    private static List<TimelineItem> BuildItems(List<MemoryEntry> concrete, List<MemoryEntry> summaries)
    {
        // ELS 没有保存来源 ID/起始 Tick，只能用“上一 ELS 之后至当前 ELS”推断同期经历。
        List<TimelineItem> items = new();
        HashSet<MemoryEntry> assigned = new();
        int previousSummaryTick = int.MinValue;
        foreach (MemoryEntry summary in summaries)
        {
            List<MemoryEntry> children = concrete
                .Where(memory => memory.GameTick > previousSummaryTick && memory.GameTick <= summary.GameTick)
                .ToList();
            assigned.UnionWith(children);
            items.Add(new TimelineItem(summary, children));
            previousSummaryTick = summary.GameTick;
        }
        items.AddRange(concrete.Where(memory => !assigned.Contains(memory)).Select(memory => new TimelineItem(null, new List<MemoryEntry> { memory })));
        return items;
    }

    private static TimelineNode Header(TimelineNodeKind kind, string key, string label, int tick, int depth, List<MemoryEntry> memories) => new()
    {
        Kind = kind,
        Key = key,
        Label = label,
        Tick = tick,
        Depth = depth,
        GroupMemories = memories,
        Height = kind is TimelineNodeKind.Year ? 34f : 30f
    };

    private static TimelineNode MemoryNode(MemoryEntry memory, int depth) => new()
    {
        Kind = TimelineNodeKind.Memory,
        Key = $"memory:{memory.Id}",
        Tick = memory.GameTick,
        Memory = memory,
        GroupMemories = new List<MemoryEntry> { memory },
        Depth = depth,
        Height = memory.Type is MemoryType.Conversation ? 96f : 68f
    };

    private static bool IsVisible(MemoryEntry memory, MemoryTabContext context) =>
        memory is not null
        && memory.Type is not MemoryType.Internal
        && (!context.LayerFilter.HasValue || memory.Layer == context.LayerFilter.Value)
        && (!context.TypeFilter.HasValue || memory.Type == context.TypeFilter.Value);

    /// <summary>
    /// 一条 ELS 及其推断同期经历；没有 ELS 覆盖的具体记忆也用同一结构表示。
    /// </summary>
    private sealed class TimelineItem
    {
        public MemoryEntry Summary { get; }
        public List<MemoryEntry> Concrete { get; }
        public int Tick => Summary?.GameTick ?? Concrete[0].GameTick;
        public IEnumerable<MemoryEntry> AllMemories => Summary is null ? Concrete : new[] { Summary }.Concat(Concrete);

        public TimelineItem(MemoryEntry summary, List<MemoryEntry> concrete)
        {
            Summary = summary;
            Concrete = concrete;
        }

        public TimelineItem Filter(string query)
        {
            bool summaryMatches = Matches(Summary, query);
            List<MemoryEntry> matches = Concrete.Where(memory => Matches(memory, query)).ToList();
            return summaryMatches || matches.Count > 0 ? new TimelineItem(Summary, matches) : null;
        }

        private static bool Matches(MemoryEntry memory, string query) =>
            memory?.Tags?.Any(tag => tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) == true;
    }
}
