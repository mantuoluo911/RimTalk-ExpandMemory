using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// 纵向时间流的即时模式绘制与选择交互。
/// 展示模型由 MemoryTimelineModel 构建，本类只维护短暂的鼠标和选择锚点状态。
/// </summary>
public sealed class MemoryTimelineView
{
    private static readonly Color SpineColor = new(0.34f, 0.39f, 0.43f, 0.9f);
    private readonly MemoryTabContext _context;
    private Vector2 _timelineScroll;
    private readonly MemoryTimelineModel _model = new();
    private List<TimelineNode> _nodes = new();
    private MemoryEntry _selectionAnchor;
    private bool _dragSelecting;
    private bool _dragArmed;
    private float _verticalPadding;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private HashSet<MemoryEntry> _dragBaseSelection = new();

    public int CenterTick { get; private set; }
    public bool UserScrolled { get; private set; }

    public MemoryTimelineView(MemoryTabContext context)
    {
        _context = context;
        _context.ResetState += ResetTransientState;
        _context.RePositionTimeline += CenterOnTick;
    }
    public void Draw(Rect rect)
    {
        if (!_context.HasMemory)
        {
            Widgets.DrawMenuSection(rect);
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, MemoryArchiveText.Get("RimTalk.Memory.UI.NoMemoryComponent"));
            return;
        }

        FourLayerMemoryComp comp = _context.MemoryComp;
        HashSet<MemoryEntry> selection = _context.Selection;
        Widgets.DrawMenuSection(rect);
        Rect viewport = rect.ContractedBy(8f);
        Vector2 scrollBeforeDraw = _timelineScroll;
        _nodes = _model.Build(comp, _context, Find.TickManager?.TicksGame ?? 0);
        if (_selectionAnchor is not null && _nodes.All(node => node.Memory != _selectionAnchor))
            _selectionAnchor = null;
        // 上下留出半个视窗，使时间轴首尾节点也能被程序化定位到视窗中央。
        _verticalPadding = viewport.height * 0.5f;
        float totalHeight = _nodes.Count == 0
            ? viewport.height
            : _nodes[^1].Y + _nodes[^1].Height + _verticalPadding * 2f;
        Rect viewRect = new(0f, 0f, viewport.width - 16f, Math.Max(viewport.height, totalHeight));

        Widgets.BeginScrollView(viewport, ref _timelineScroll, viewRect);

        // 仅绘制视窗附近节点；Y 坐标仍保留完整内容位置，滚动和跳转不受虚拟化影响。
        float visibleTop = _timelineScroll.y - 100f;
        float visibleBottom = _timelineScroll.y + viewport.height + 100f;
        float spineX = 24f;
        Widgets.DrawBoxSolid(new Rect(spineX, _verticalPadding, 2f, totalHeight - _verticalPadding * 2f), SpineColor);

        foreach (TimelineNode node in _nodes)
        {
            float nodeY = node.Y + _verticalPadding;
            if (nodeY + node.Height < visibleTop || nodeY > visibleBottom) continue;
            Rect nodeRect = new(0f, node.Y + _verticalPadding, viewRect.width, node.Height);
            DrawNode(nodeRect, node, comp, selection);
        }
        HandleDragSelection(viewport, viewRect, selection);
        if (_dragSelecting)
        {
            Rect box = SelectionBox();
            Widgets.DrawBoxSolid(box, new Color(0.72f, 0.58f, 0.28f, 0.18f));
            GUI.color = new Color(0.9f, 0.72f, 0.34f);
            Widgets.DrawBox(box, 1);
            GUI.color = Color.white;
        }
        Widgets.EndScrollView();

        // BeginScrollView 只在用户滚轮或拖动滚动条时改变该值；程序化 CenterOnTick 发生在 Draw 之后。
        UserScrolled = _timelineScroll != scrollBeforeDraw;
        UpdateCenterTick(_timelineScroll.y + viewport.height * 0.5f);

        if (UserScrolled && CenterTick != _context.CursorTick)
        {
            _context.CursorTick = CenterTick;
        }
    }

    public void CenterOnTick()
    {
        if (_nodes.Count == 0) return;

        int tick = _context.CursorTick;

        // 时间流节点离散，篇章光标落在空时段时按绝对时间选择最近节点，同距偏向较新节点。
        TimelineNode nearest = _nodes
            .OrderBy(node => Math.Abs((long)node.Tick - tick))
            .ThenByDescending(node => node.Tick)
            .First();
        float contentHeight = _nodes[^1].Y + _nodes[^1].Height + _verticalPadding * 2f;
        float maximumScroll = Math.Max(0f, contentHeight);
        _timelineScroll.y = Mathf.Clamp(
            nearest.Y + _verticalPadding + nearest.Height * 0.5f,
            0f,
            maximumScroll);
        CenterTick = nearest.Tick;
    }

    public void ResetTransientState()
    {
        // Pawn/存档切换时不能保留上一上下文的 Shift 锚点或未完成拖拽。
        _selectionAnchor = null;
        _dragArmed = false;
        _dragSelecting = false;
    }

    private void UpdateCenterTick(float centerY)
    {
        if (_nodes.Count == 0) return;
        TimelineNode center = _nodes
            .OrderBy(node => Math.Abs(node.Y + _verticalPadding + node.Height * 0.5f - centerY))
            .First();
        CenterTick = center.Tick;
    }

    private void DrawNode(
        Rect rect,
        TimelineNode node,
        FourLayerMemoryComp comp,
        HashSet<MemoryEntry> selection)
    {
        float indent = node.Depth * 18f;
        Rect marker = new(18f + indent, rect.y + rect.height * 0.5f - 5f, 12f, 12f);
        bool collapsed = _context.CollapsedGroups.Contains(node.Key);

        if (node.Kind is TimelineNodeKind.Year or TimelineNodeKind.Quadrum)
        {
            DrawGroupHeader(rect, node, selection, marker, collapsed);
            return;
        }

        if (node.Kind is TimelineNodeKind.Summary)
        {
            DrawSummary(rect, node, comp, selection, marker, collapsed);
            return;
        }

        DrawMemory(rect, node.Memory, comp, selection, marker);
    }

    private void DrawGroupHeader(Rect rect, TimelineNode node, HashSet<MemoryEntry> selection, Rect marker, bool collapsed)
    {
        Rect content = new(marker.xMax + 8f, rect.y, rect.width - marker.xMax - 12f, rect.height);
        Widgets.DrawBoxSolid(marker, node.Kind is TimelineNodeKind.Year
            ? new Color(0.58f, 0.48f, 0.3f)
            : new Color(0.36f, 0.48f, 0.54f));
        Text.Font = node.Kind is TimelineNodeKind.Year ? GameFont.Medium : GameFont.Small;
        Widgets.Label(content, $"{(collapsed ? "▶" : "▼")}  {node.Label}   {node.GroupMemories.Count} 条");
        Text.Font = GameFont.Small;

        Rect checkRect = new(rect.xMax - 28f, rect.y + 4f, 24f, 24f);
        bool allSelected = node.GroupMemories.Count > 0 && node.GroupMemories.All(selection.Contains);
        bool wasAllSelected = allSelected;
        Widgets.Checkbox(checkRect.position, ref allSelected, 24f);
        if (allSelected != wasAllSelected)
        {
            if (allSelected) selection.UnionWith(node.GroupMemories);
            else selection.ExceptWith(node.GroupMemories);
        }

        Rect toggleRect = new(content.x, rect.y, content.width - 34f, rect.height);
        if (Widgets.ButtonInvisible(toggleRect)) Toggle(node.Key);
    }

    private void DrawSummary(
        Rect rect,
        TimelineNode node,
        FourLayerMemoryComp comp,
        HashSet<MemoryEntry> selection,
        Rect marker,
        bool collapsed)
    {
        DrawActivityNode(marker, node.Memory.Activity, node.Memory.Layer);
        Rect card = new(marker.xMax + 10f, rect.y, rect.width - marker.xMax - 14f, rect.height);
        Widgets.DrawBoxSolid(card, MemoryArchivePalette.Background(MemoryLayer.EventLog));
        GUI.color = _context.FocusedMemory == node.Memory ? Color.white : MemoryArchivePalette.EventLog;
        Widgets.DrawBox(card, _context.FocusedMemory == node.Memory ? 2 : 1);
        GUI.color = Color.white;
        DrawSummaryStatus(card, node.Memory, comp);

        Rect checkRect = new(card.x + 8f, card.y + 8f, 24f, 24f);
        bool allSelected = node.GroupMemories.Count > 0 && node.GroupMemories.All(selection.Contains);
        bool wasAllSelected = allSelected;
        Widgets.Checkbox(checkRect.position, ref allSelected, 24f);
        if (allSelected != wasAllSelected)
        {
            if (allSelected) selection.UnionWith(node.GroupMemories);
            else selection.ExceptWith(node.GroupMemories);
        }

        Rect pinRect = new(card.xMax - 30f, card.y + 6f, 24f, 24f);
        DrawPinButton(pinRect, node.Memory);
        Rect header = new(checkRect.xMax + 8f, card.y + 7f, card.width - 78f, 24f);
        Text.Font = GameFont.Small;
        Widgets.Label(header, $"{(collapsed ? "▶" : "▼")}  {MemoryArchiveText.Layer(MemoryLayer.EventLog)} · {node.Memory.AgeString} · {MemoryArchiveText.Get("RimTalk_Archive_ConcurrentCount", node.GroupMemories.Count - 1)}");
        Rect textRect = new(card.x + 12f, card.y + 35f, card.width - 24f, card.height - 48f);
        Text.Font = GameFont.Tiny;
        Widgets.Label(textRect, Truncate(node.Memory.Content, 220));
        Text.Font = GameFont.Small;
        DrawActivityBar(new Rect(card.x + 1f, card.yMax - 4f, card.width - 2f, 3f), node.Memory.Activity, node.Memory.Layer);

        Rect toggleRect = new(header.x, header.y, header.width, header.height);
        if (Widgets.ButtonInvisible(toggleRect)) Toggle(node.Key);
        Rect focusRect = new(card.x, card.y + 32f, card.width, card.height - 32f);
        if (Widgets.ButtonInvisible(focusRect)) SelectMemory(node.Memory, selection);
    }

    private void DrawMemory(
        Rect rect,
        MemoryEntry memory,
        FourLayerMemoryComp comp,
        HashSet<MemoryEntry> selection,
        Rect marker)
    {
        DrawActivityNode(marker, memory.Activity, memory.Layer);
        Rect card = new(marker.xMax + 10f, rect.y, rect.width - marker.xMax - 14f, rect.height);
        Widgets.DrawBoxSolid(card, MemoryArchivePalette.Background(memory.Layer));
        GUI.color = memory.IsPinned ? new Color(0.94f, 0.74f, 0.3f) : MemoryArchivePalette.Layer(memory.Layer);
        Widgets.DrawBox(card, memory.IsPinned ? 2 : 1);
        GUI.color = Color.white;
        DrawSummaryStatus(card, memory, comp);
        if (_context.FocusedMemory == memory || selection.Contains(memory))
        {
            GUI.color = _context.FocusedMemory == memory ? new Color(0.84f, 0.74f, 0.46f) : new Color(0.52f, 0.62f, 0.68f);
            Widgets.DrawBox(card, _context.FocusedMemory == memory ? 2 : 1);
            GUI.color = Color.white;
        }

        Rect checkRect = new(card.x + 8f, card.y + 8f, 24f, 24f);
        bool selected = selection.Contains(memory);
        Widgets.Checkbox(checkRect.position, ref selected, 24f);
        if (selected) selection.Add(memory); else selection.Remove(memory);

        Rect pinRect = new(card.xMax - 30f, card.y + 6f, 24f, 24f);
        DrawPinButton(pinRect, memory);
        Rect header = new(checkRect.xMax + 8f, card.y + 7f, card.width - 76f, 22f);
        string importance = memory.Importance >= 0.8f ? "  ★" : string.Empty;
        Widgets.Label(header, $"{MemoryArchiveText.Type(memory.Type)} · {memory.AgeString}{importance}");
        Rect content = new(card.x + 12f, card.y + 32f, card.width - 24f, card.height - 40f);
        Text.Font = memory.Type is MemoryType.Action ? GameFont.Small : GameFont.Tiny;
        Widgets.Label(content, Truncate(memory.Content, memory.Type is MemoryType.Action ? 150 : 280));
        Text.Font = GameFont.Small;
        DrawActivityBar(new Rect(card.x + 1f, card.yMax - 4f, card.width - 2f, 3f), memory.Activity, memory.Layer);
        if (Widgets.ButtonInvisible(new Rect(card.x + 38f, card.y, card.width - 72f, card.height)))
            SelectMemory(memory, selection);
    }

    private void DrawPinButton(Rect rect, MemoryEntry memory)
    {
        GUI.color = memory.IsPinned ? new Color(1f, 0.78f, 0.32f) : new Color(0.64f, 0.66f, 0.68f);
        if (Widgets.ButtonText(rect, memory.IsPinned ? "◆" : "◇"))
            _context.SetPinned(memory, !memory.IsPinned);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(rect, MemoryArchiveText.Get(memory.IsPinned
            ? "RimTalk_MindStream_Unpin"
            : "RimTalk_MindStream_Pin"));
    }

    private static void DrawSummaryStatus(Rect card, MemoryEntry memory, FourLayerMemoryComp comp)
    {
        if (comp?.Summarizer?.CheckSummarizing(memory) == true)
        {
            float pulse = (Mathf.Sin(Time.realtimeSinceStartup * 4f) + 1f) * 0.5f;
            GUI.color = Color.Lerp(
                new Color(0.48f, 0.2f, 0.68f, 0.65f),
                new Color(0.86f, 0.5f, 1f),
                pulse);
            Widgets.DrawBox(card.ContractedBy(4f), 2);
            GUI.color = Color.white;
        }
        else if (comp?.Summarizer?.CheckSummarized(memory) == true)
        {
            GUI.color = new Color(0.68f, 0.4f, 0.88f, 0.55f);
            Widgets.DrawBox(card.ContractedBy(4f), 1);
            GUI.color = Color.white;
        }
    }

    private void SelectMemory(MemoryEntry memory, HashSet<MemoryEntry> selection)
    {
        // Shift 范围以当前可见节点序列为准；筛选后锚点不可见时退化为普通单选。
        if (Event.current.shift && _selectionAnchor is not null)
        {
            List<MemoryEntry> visible = _nodes.Where(node => node.Memory is not null).Select(node => node.Memory).ToList();
            int from = visible.IndexOf(_selectionAnchor);
            int to = visible.IndexOf(memory);
            if (from >= 0 && to >= 0)
            {
                for (int index = Math.Min(from, to); index <= Math.Max(from, to); index++) selection.Add(visible[index]);
            }
            else
            {
                selection.Clear();
                selection.Add(memory);
            }
        }
        else if (Event.current.control)
        {
            if (!selection.Add(memory)) selection.Remove(memory);
        }
        else
        {
            selection.Clear();
            selection.Add(memory);
        }
        _selectionAnchor = memory;
        _context.Focus(memory);
    }

    private void HandleDragSelection(Rect viewport, Rect viewRect, HashSet<MemoryEntry> selection)
    {
        Event current = Event.current;
        Vector2 mouse = current.mousePosition;

        // 必须先在列表内按下并超过阈值才进入框选，避免从窗口外拖入时误清空选择。
        if (current.type == EventType.MouseDown && current.button == 0 && viewRect.Contains(mouse))
        {
            _dragArmed = true;
            _dragStart = mouse;
            _dragCurrent = mouse;
            _dragSelecting = false;
            // Ctrl 框选以按下时选择集为基线；普通框选则从空集开始。
            _dragBaseSelection = current.control ? new HashSet<MemoryEntry>(selection) : new HashSet<MemoryEntry>();
        }
        else if (_dragArmed && current.type == EventType.MouseDrag && current.button == 0 && Vector2.Distance(_dragStart, mouse) >= 5f)
        {
            _dragSelecting = true;
            _dragCurrent = mouse;
            Rect box = SelectionBox();
            selection.Clear();
            selection.UnionWith(_dragBaseSelection);
            foreach (TimelineNode node in _nodes.Where(node => node.Memory is not null))
            {
                Rect nodeRect = new(0f, node.Y + _verticalPadding, viewRect.width, node.Height);
                if (box.Overlaps(nodeRect)) selection.Add(node.Memory);
            }
            current.Use();
        }
        if (current.rawType == EventType.MouseUp)
        {
            _dragArmed = false;
            if (!_dragSelecting) return;
            _dragSelecting = false;
            current.Use();
        }
    }

    private Rect SelectionBox()
    {
        float x = Math.Min(_dragStart.x, _dragCurrent.x);
        float y = Math.Min(_dragStart.y, _dragCurrent.y);
        return new Rect(x, y, Math.Abs(_dragStart.x - _dragCurrent.x), Math.Abs(_dragStart.y - _dragCurrent.y));
    }

    private static void DrawActivityNode(Rect rect, float activity, MemoryLayer layer)
    {
        float value = Mathf.Clamp01(activity);
        Color color = MemoryArchivePalette.Layer(layer);
        color.a = Mathf.Lerp(0.35f, 1f, value);
        GUI.color = color;
        Widgets.DrawBox(rect, value < 0.2f ? 2 : 1);
        if (value >= 0.2f) Widgets.DrawBoxSolid(rect.ContractedBy(3f), color);
        GUI.color = Color.white;
    }

    private static void DrawActivityBar(Rect rect, float activity, MemoryLayer layer)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.18f, 0.19f));
        Widgets.DrawBoxSolid(
            new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(activity), rect.height),
            MemoryArchivePalette.Layer(layer));
    }

    private void Toggle(string key)
    {
        if (!_context.CollapsedGroups.Add(key)) _context.CollapsedGroups.Remove(key);
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
        return text.Substring(0, max - 1) + "…";
    }

}
