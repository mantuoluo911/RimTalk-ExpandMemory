using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// 主标签各绘制区域共享的会话上下文
/// </summary>
public sealed class MemoryTabContext
{
    // 公开数据，供外部消费
    // 核心记忆组件
    public FourLayerMemoryComp MemoryComp;

    // 核心锚点指针
    public Cursor Cursor;
    public int CursorTick;

    // 主 UI 窗口 rect
    public Rect InRect;

    // 全量记忆总时间跨度缓存，每帧刷新
    public int LifeStartTick;
    public int LifeCurrentTick;

    // 过滤器
    public string TagFilter = string.Empty;
    public MemoryLayer? LayerFilter;
    public MemoryType? TypeFilter;

    public readonly HashSet<string> CollapsedGroups = new();
    public readonly HashSet<string> SeenGroups = new();

    // 关注点，会展示在记忆详情区
    public MemoryEntry FocusedMemory;

    // 选中记忆集合
    public readonly HashSet<MemoryEntry> Selection = new();

    public bool TimelineNeedsPositioning;

    public bool ContextReseting = true;

    public bool HasMemory => MemoryComp is not null && _allMemories.Count > 0;


    // 私有数据，供内部检查数据时效
    // 全量记忆缓存，每帧刷新
    private readonly HashSet<MemoryEntry> _allMemories = new();


    // 每帧刷新
    public void Update(Rect inRect)
    {
        InRect = inRect;
        CheckReset();
        Refresh();
    }

    private void CheckReset()
    {
        ContextReseting = false;

        // 当前 context 目标出现有效变化时，更新 MemoryComp 并重置 context 状态
        if (Find.Selector.SingleSelectedThing is Pawn pawn
            && pawn != MemoryComp?.parent
            && pawn.TryGetComp<FourLayerMemoryComp>() is { } memoryComp)
        {
            MemoryComp = memoryComp;
            _allMemories?.Clear();
            FocusedMemory = null;
            Selection.Clear();

            TimelineNeedsPositioning = true;
            ContextReseting = true;
        }
    }

    private void Refresh()
    {
        // 没有有效的记忆组件时，没有多余操作的必要
        if (MemoryComp is null) return;

        // 刷新全量记忆缓存
        var aBMs = MemoryComp.ActiveMemories;
        var sCMs = MemoryComp.SituationalMemories;
        var eLSs = MemoryComp.EventLogMemories;
        var cLPAs = MemoryComp.ArchiveMemories;

        _allMemories.Clear();
        _allMemories.EnsureCapacity(aBMs.Count + sCMs.Count + eLSs.Count + cLPAs.Count);
        _allMemories.UnionWith(aBMs);
        _allMemories.UnionWith(sCMs);
        _allMemories.UnionWith(eLSs);
        _allMemories.UnionWith(cLPAs);

        // 刷新全量记忆时间跨度
        if (_allMemories.Count > 0)
        {
            LifeStartTick = _allMemories.Min(memory => memory.GameTick);
            LifeCurrentTick = _allMemories.Max(memory => memory.EndGameTick);
        }

        // 刷新关注点和选中集合
        if (!_allMemories.Contains(FocusedMemory))
            FocusedMemory = null;
        Selection.IntersectWith(_allMemories);
    }

    public void Focus(MemoryEntry memory) => FocusedMemory = memory;

    public void SetPinned(MemoryEntry memory, bool isPinned)
    {
        MemoryEntry updated = MemoryArchiveCommands.SetPinned(MemoryComp, memory, isPinned);
        if (updated is null) return;
        if (Selection.Remove(memory)) Selection.Add(updated);
        if (FocusedMemory == memory) FocusedMemory = updated;
    }

    public void SummarizeSelected()
    {
        MemoryComp?.Summarizer?.ManualSummarize(Selection);
        Selection.Clear();
    }

    public void ArchiveSelected()
    {
        MemoryComp?.Summarizer?.Archive(Selection);
        Selection.Clear();
    }

    public void DeleteSelected()
    {
        if (MemoryComp is null) return;
        foreach (MemoryEntry memory in Selection)
            MemoryComp.Interactor.RemoveMemory(memory);
        Selection.Clear();
        FocusedMemory = null;
    }
}
