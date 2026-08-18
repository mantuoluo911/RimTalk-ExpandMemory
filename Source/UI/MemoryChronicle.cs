using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// CLPA 横向人生篇章导航器。
/// 篇章跨度按真实时间映射；CursorTick 表示阅读时间，ChronicleCenterTick 表示独立的篇章视窗中心。
/// </summary>
public sealed class MemoryChronicle
{
    private const float AxisHeight = 28f;
    private const float MinimumSpan = 2f * GenDate.TicksPerDay;
    private const float MaximumSpan = 10f * GenDate.TicksPerYear;
    private static readonly Color ChapterColor = MemoryArchivePalette.Background(MemoryLayer.Archive);
    private static readonly Color ChapterHoverColor = new(0.36f, 0.24f, 0.44f, 1f);
    private readonly MemoryTabContext _context;
    private readonly Cursor _cursor;
    private readonly List<MemoryEntry> _chapters = new();
    private float _chronicleCenterTick;
    private float _chronicleSpanTicks = GenDate.TicksPerYear;
    private bool _draggingCursor;
    private bool _dragArmed;
    private bool _dragSelecting;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private HashSet<MemoryEntry> _dragBaseSelection = new();
    private MemoryEntry _selectionAnchor;

    public MemoryChronicle(MemoryTabContext context, Cursor cursor)
    {
        _context = context;
        _cursor = cursor;
    }

    // 每帧从 MemoryComp 的 CLPA 档案重建篇章列表，应用标签过滤。
    public void Refresh()
    {
        _chapters.Clear();
        if (_context.MemoryComp is null) return;
        string query = _context.TagFilter?.Trim();
        _chapters.AddRange(_context.MemoryComp.ArchiveMemories
            .Where(memory => memory is not null)
            .Where(memory => string.IsNullOrWhiteSpace(query)
                             || memory.Tags?.Any(tag => tag.Contains(query, System.StringComparison.OrdinalIgnoreCase)) == true));
    }

    public void Draw(Rect rect)
    {
        if (_context.ContextReseting) ResetTransientState();
        if (!_context.HasMemory)
        {
            Widgets.DrawMenuSection(rect);
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, MemoryArchiveText.Get("RimTalk.Memory.UI.InvalidChronicle"));
            return;
        }

        Refresh();
        IReadOnlyList<MemoryEntry> chapters = _chapters;
        int minimumTick = _context.LifeStartTick;
        int maximumTick = _context.LifeCurrentTick;
        HashSet<MemoryEntry> selection = _context.Selection;
        Widgets.DrawMenuSection(rect);
        Rect inner = rect.ContractedBy(8f);
        Rect titleRect = new(inner.x, inner.y, inner.width, 18f);
        Rect chapterRect = new(inner.x, titleRect.yMax + 3f, inner.width, inner.height - titleRect.height - AxisHeight - 5f);
        Rect axisRect = new(inner.x, chapterRect.yMax, inner.width, AxisHeight);

        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.72f, 0.78f, 0.82f);
        Widgets.Label(titleRect, MemoryArchiveText.Get("RimTalk_Archive_ChronicleTitle", chapters.Count, Mathf.RoundToInt(_chronicleSpanTicks / GenDate.TicksPerDay)));
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        Widgets.DrawBoxSolid(chapterRect, new Color(0.07f, 0.08f, 0.09f, 0.82f));
        Widgets.DrawBoxSolid(axisRect, new Color(0.095f, 0.105f, 0.115f, 0.96f));

        NormalizeViewport(minimumTick, maximumTick);

        // 篇章视窗和阅读光标是独立状态，光标可以出现在时间轴上的任意横向位置。
        float halfSpan = _chronicleSpanTicks * 0.5f;
        float startTick = _chronicleCenterTick - halfSpan;
        float endTick = _chronicleCenterTick + halfSpan;
        DrawChapters(chapterRect, chapters, startTick, endTick, selection);
        DrawAxis(axisRect, startTick, endTick, _context.CursorTick);

        bool requestedTimelinePosition = HandleInput(
            chapterRect,
            axisRect,
            startTick,
            endTick,
            minimumTick,
            maximumTick);
        DrawCursor(chapterRect, axisRect, _context.CursorTick, startTick, endTick);
        HandleChapterSelection(chapterRect, chapters, startTick, endTick, selection);
        _context.TimelineNeedsPositioning = requestedTimelinePosition;
    }

    private void DrawChapters(
        Rect rect,
        IReadOnlyList<MemoryEntry> chapters,
        float startTick,
        float endTick,
        HashSet<MemoryEntry> selection)
    {
        List<ChapterHit> hits = new();
        foreach (MemoryEntry chapter in chapters.OrderBy(memory => memory.GameTick))
        {
            int chapterEnd = chapter.EndGameTick > chapter.GameTick ? chapter.EndGameTick : chapter.GameTick;
            if (chapterEnd < startTick || chapter.GameTick > endTick) continue;

            float xMin = TickToX(Math.Max(chapter.GameTick, startTick), startTick, endTick, rect);
            float xMax = TickToX(Math.Min(chapterEnd, endTick), startTick, endTick, rect);
            float width = Math.Max(8f, xMax - xMin);
            // 重叠篇章轻微错位，保持“档案叠放”视觉，同时避免无限增加泳道高度。
            int overlapDepth = hits.Count(hit => hit.Rect.Overlaps(new Rect(xMin, rect.y + 5f, width, rect.height - 10f)));
            float offset = Math.Min(12f, overlapDepth * 3f);
            Rect chapterCard = new(xMin, rect.y + 5f + offset, width, Math.Max(18f, rect.height - 10f - offset));
            hits.Add(new ChapterHit(chapter, chapterCard));

            Widgets.DrawBoxSolid(chapterCard, Mouse.IsOver(chapterCard) ? ChapterHoverColor : ChapterColor);
            GUI.color = MemoryArchivePalette.Archive;
            Widgets.DrawBox(chapterCard, 1);
            GUI.color = Color.white;
            if (selection.Contains(chapter))
            {
                GUI.color = new Color(0.84f, 0.74f, 0.46f);
                Widgets.DrawBox(chapterCard, 2);
                GUI.color = Color.white;
            }
            if (chapterCard.width >= 54f)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(chapterCard.ContractedBy(4f), Truncate(chapter.Content, Mathf.FloorToInt(chapterCard.width / 7f)));
                Text.Font = GameFont.Small;
            }

            if (chapterCard.width >= 30f)
            {
                Rect pinRect = new(chapterCard.xMax - 24f, chapterCard.y + 2f, 22f, 22f);
                GUI.color = chapter.IsPinned ? new Color(1f, 0.78f, 0.32f) : new Color(0.72f, 0.7f, 0.76f);
                if (Widgets.ButtonText(pinRect, chapter.IsPinned ? "◆" : "◇"))
                    _context.SetPinned(chapter, !chapter.IsPinned);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(pinRect, MemoryArchiveText.Get(chapter.IsPinned
                    ? "RimTalk_MindStream_Unpin"
                    : "RimTalk_MindStream_Pin"));
            }
        }

        // 重叠入口优先于普通卡片点击，确保完全重合时旧篇章仍可访问。
        foreach (IGrouping<string, ChapterHit> cluster in BuildOverlapClusters(hits).Where(group => group.Count() > 1))
        {
            Rect bounds = cluster.Select(hit => hit.Rect).Aggregate(Union);
            Rect badge = new(Math.Min(rect.xMax - 34f, bounds.xMax - 30f), rect.y + 2f, 32f, 18f);
            Widgets.DrawBoxSolid(badge, new Color(0.18f, 0.14f, 0.21f, 0.98f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(badge, $"+{cluster.Count() - 1}");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(badge))
            {
                OpenOverlapMenu(cluster.Select(hit => hit.Memory).OrderByDescending(memory => memory.GameTick).ToList());
            }
        }

    }

    private void HandleChapterSelection(
        Rect rect,
        IReadOnlyList<MemoryEntry> chapters,
        float startTick,
        float endTick,
        HashSet<MemoryEntry> selection)
    {
        Event current = Event.current;
        Vector2 mouse = current.mousePosition;
        List<ChapterHit> hits = chapters
            .OrderBy(memory => memory.GameTick)
            .Select(memory =>
            {
                int chapterEnd = memory.EndGameTick > memory.GameTick ? memory.EndGameTick : memory.GameTick;
                float xMin = TickToX(Math.Max(memory.GameTick, startTick), startTick, endTick, rect);
                float xMax = TickToX(Math.Min(chapterEnd, endTick), startTick, endTick, rect);
                return new ChapterHit(memory, new Rect(xMin, rect.y + 5f, Math.Max(8f, xMax - xMin), rect.height - 10f));
            })
            .ToList();

        if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(mouse))
        {
            _dragArmed = true;
            _dragSelecting = false;
            _dragStart = mouse;
            _dragCurrent = mouse;
            _dragBaseSelection = current.control ? new HashSet<MemoryEntry>(selection) : new HashSet<MemoryEntry>();
            return;
        }

        if (_dragArmed && current.type == EventType.MouseDrag && current.button == 0
            && Vector2.Distance(_dragStart, mouse) >= 5f)
        {
            _dragSelecting = true;
            _dragCurrent = mouse;
            Rect box = SelectionBox();
            selection.Clear();
            selection.UnionWith(_dragBaseSelection);
            foreach (ChapterHit hit in hits.Where(hit => box.Overlaps(hit.Rect)))
                selection.Add(hit.Memory);
            current.Use();
        }

        if (current.rawType == EventType.MouseUp)
        {
            _dragArmed = false;
            if (!_dragSelecting)
            {
                ChapterHit hit = hits
                    .Where(item => item.Rect.Contains(mouse))
                    .OrderByDescending(item => item.Rect.width)
                    .FirstOrDefault();
                if (hit is not null)
                {
                    SelectChapter(hit.Memory, chapters, selection);
                    current.Use();
                }
                return;
            }
            _dragSelecting = false;
            current.Use();
        }

        if (_dragSelecting)
        {
            Rect box = SelectionBox();
            Widgets.DrawBoxSolid(box, new Color(0.72f, 0.58f, 0.28f, 0.18f));
            GUI.color = new Color(0.9f, 0.72f, 0.34f);
            Widgets.DrawBox(box, 1);
            GUI.color = Color.white;
        }
    }

    private void SelectChapter(
        MemoryEntry memory,
        IReadOnlyList<MemoryEntry> chapters,
        HashSet<MemoryEntry> selection)
    {
        List<MemoryEntry> visible = chapters.OrderBy(chapter => chapter.GameTick).ToList();
        if (Event.current.shift && _selectionAnchor is not null)
        {
            int from = visible.IndexOf(_selectionAnchor);
            int to = visible.IndexOf(memory);
            if (from >= 0 && to >= 0)
                selection.UnionWith(visible.Skip(Math.Min(from, to)).Take(Math.Abs(from - to) + 1));
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

    private Rect SelectionBox() => new(
        Math.Min(_dragStart.x, _dragCurrent.x),
        Math.Min(_dragStart.y, _dragCurrent.y),
        Math.Abs(_dragStart.x - _dragCurrent.x),
        Math.Abs(_dragStart.y - _dragCurrent.y));

    private static IEnumerable<IGrouping<string, ChapterHit>> BuildOverlapClusters(List<ChapterHit> hits)
    {
        // 使用传递式邻接聚类：与已有组任一条相交的篇章归入同一重叠入口。
        Dictionary<ChapterHit, string> keys = new();
        int clusterIndex = 0;
        foreach (ChapterHit hit in hits.OrderBy(item => item.Rect.x))
        {
            string existing = keys
                .Where(pair => pair.Key.Rect.Overlaps(hit.Rect))
                .Select(pair => pair.Value)
                .FirstOrDefault();
            keys[hit] = existing ?? $"cluster:{clusterIndex++}";
        }
        return keys.Keys.GroupBy(hit => keys[hit]);
    }

    private void OpenOverlapMenu(List<MemoryEntry> memories)
    {
        Find.WindowStack.Add(new FloatMenu(memories.Select(memory => new FloatMenuOption(
            $"{memory.AgeString} · {Truncate(memory.Content, 80)}",
            () => _context.Focus(memory))).ToList()));
    }

    private static void DrawAxis(Rect rect, float startTick, float endTick, int cursorTick)
    {
        int firstDay = Mathf.FloorToInt(startTick / GenDate.TicksPerDay);
        int lastDay = Mathf.CeilToInt(endTick / GenDate.TicksPerDay);
        int visibleDays = Math.Max(1, lastDay - firstDay);
        int step = visibleDays > 240 ? 60 : visibleDays > 80 ? 15 : visibleDays > 25 ? 5 : 1;
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.62f, 0.66f, 0.69f);
        for (int day = firstDay - firstDay % step; day <= lastDay; day += step)
        {
            int tick = day * GenDate.TicksPerDay;
            float x = TickToX(tick, startTick, endTick, rect);
            Widgets.DrawBoxSolid(new Rect(x, rect.y, 1f, 7f), new Color(0.38f, 0.42f, 0.45f));
            Widgets.Label(new Rect(x + 3f, rect.y + 7f, 82f, 18f), DateLabel(tick));
        }
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    /// <summary>
    /// 时间流主动更新 CursorTick 后调用。光标仍在视窗内时不移动篇章；越界时才让视窗追到光标边缘。
    /// </summary>
    public void KeepCursorVisible()
    {
        int minimumTick = _context.LifeStartTick;
        int maximumTick = _context.LifeCurrentTick;
        NormalizeViewport(minimumTick, maximumTick);
        float halfSpan = _chronicleSpanTicks * 0.5f;
        float startTick = _chronicleCenterTick - halfSpan;
        float endTick = _chronicleCenterTick + halfSpan;
        if (_context.CursorTick < startTick)
            _chronicleCenterTick = _context.CursorTick + halfSpan;
        else if (_context.CursorTick > endTick)
            _chronicleCenterTick = _context.CursorTick - halfSpan;
        ClampCenter(minimumTick, maximumTick);
    }

    private bool HandleInput(
        Rect chapterRect,
        Rect axisRect,
        float startTick,
        float endTick,
        int minimumTick,
        int maximumTick)
    {
        Event current = Event.current;
        bool requested = false;
        if (current.type == EventType.ScrollWheel && chapterRect.Contains(current.mousePosition))
        {
            // 篇章滚动只平移可见时间范围；CursorTick 和时间流阅读位置保持不变。
            _chronicleCenterTick += current.delta.y * _chronicleSpanTicks * 0.06f;
            ClampCenter(minimumTick, maximumTick);
            current.Use();
        }
        else if (current.type == EventType.ScrollWheel && axisRect.Contains(current.mousePosition))
        {
            // 未触边时保持光标的屏幕横向比例；触边时边界对齐优先。
            float oldFraction = Mathf.InverseLerp(startTick, endTick, _context.CursorTick);
            float factor = Mathf.Pow(1.18f, current.delta.y);
            float availableSpan = Math.Max(MinimumSpan, maximumTick - minimumTick);
            _chronicleSpanTicks = Mathf.Clamp(
                _chronicleSpanTicks * factor,
                MinimumSpan,
                Math.Min(MaximumSpan, availableSpan));
            _chronicleCenterTick = _context.CursorTick + (0.5f - oldFraction) * _chronicleSpanTicks;
            ClampCenter(minimumTick, maximumTick);
            current.Use();
        }

        if (current.type == EventType.MouseDown && current.button == 0 && axisRect.Contains(current.mousePosition))
        {
            _draggingCursor = true;
            _context.CursorTick = XToTick(current.mousePosition.x, startTick, endTick, axisRect);
            requested = true;
            current.Use();
        }
        else if (_draggingCursor && current.type == EventType.MouseDrag && current.button == 0)
        {
            _context.CursorTick = XToTick(Mathf.Clamp(current.mousePosition.x, axisRect.x, axisRect.xMax), startTick, endTick, axisRect);
            requested = true;
            current.Use();
        }
        if (_draggingCursor && current.rawType == EventType.MouseUp)
            _draggingCursor = false;
        return requested;
    }

    private void NormalizeViewport(int minimumTick, int maximumTick)
    {
        float availableSpan = Math.Max(MinimumSpan, maximumTick - minimumTick);
        _chronicleSpanTicks = Mathf.Clamp(
            _chronicleSpanTicks,
            MinimumSpan,
            Math.Min(MaximumSpan, availableSpan));
        if (_chronicleCenterTick == 0f)
            _chronicleCenterTick = _context.CursorTick;
        ClampCenter(minimumTick, maximumTick);
    }

    private void ClampCenter(int minimumTick, int maximumTick)
    {
        float halfSpan = _chronicleSpanTicks * 0.5f;
        float minimumCenter = minimumTick + halfSpan;
        float maximumCenter = maximumTick - halfSpan;
        _chronicleCenterTick = minimumCenter <= maximumCenter
            ? Mathf.Clamp(_chronicleCenterTick, minimumCenter, maximumCenter)
            : (minimumTick + maximumTick) * 0.5f;
    }

    private static void DrawCursor(Rect chapterRect, Rect axisRect, int tick, float startTick, float endTick)
    {
        if (tick < startTick || tick > endTick) return;
        float x = TickToX(tick, startTick, endTick, axisRect);
        Color color = new(0.93f, 0.72f, 0.34f, 0.95f);
        Widgets.DrawBoxSolid(new Rect(x, chapterRect.y, 2f, chapterRect.height + axisRect.height), color);
        Widgets.DrawBoxSolid(new Rect(x - 5f, axisRect.y, 12f, 5f), color);
    }

    private static float TickToX(float tick, float startTick, float endTick, Rect rect) =>
        rect.x + (tick - startTick) / (endTick - startTick) * rect.width;

    private static int XToTick(float x, float startTick, float endTick, Rect rect) =>
        Mathf.RoundToInt(Mathf.Lerp(startTick, endTick, Mathf.InverseLerp(rect.x, rect.xMax, x)));

    private static Rect Union(Rect left, Rect right)
    {
        float x = Math.Min(left.x, right.x);
        float y = Math.Min(left.y, right.y);
        return new Rect(x, y, Math.Max(left.xMax, right.xMax) - x, Math.Max(left.yMax, right.yMax) - y);
    }

    private static string DateLabel(int tick) =>
        GenDate.DateMonthYearStringAt(GenDate.TickGameToAbs(tick), Vector2.zero);

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
        return text.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    private sealed class ChapterHit
    {
        public MemoryEntry Memory { get; }
        public Rect Rect { get; }
        public ChapterHit(MemoryEntry memory, Rect rect) { Memory = memory; Rect = rect; }
    }

    public void ResetTransientState()
    {
        // Pawn/存档切换时不能保留上一上下文的 Shift 锚点或未完成拖拽。
        _selectionAnchor = null;
        _dragArmed = false;
        _dragSelecting = false;
        _chapters.Clear();
    }
}
