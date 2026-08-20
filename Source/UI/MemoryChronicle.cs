using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Memory.Utils;
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
    // 常量配置
    private const float TitleHeight = 18f;
    private const float ChapterGap = 3f;


    // 时间轴底栏的固定像素高度（含刻度线与日期文本）。
    private const float AxisHeight = 28f;
    // 篇章视窗允许的最小时间跨度（2 天），防止缩放过细导致刻度拥挤。
    private const float MinimumSpan = 2f * GenDate.TicksPerDay;
    // 篇章视窗允许的最大时间跨度（10 年），防止缩放过广导致篇章挤压。
    private const float MaximumSpan = 10f * GenDate.TicksPerYear;


    private static readonly Color ChapterColor = MemoryArchivePalette.Background(MemoryLayer.Archive);
    private static readonly Color ChapterHoverColor = new(0.36f, 0.24f, 0.44f, 1f);


    // 共享状态
    private readonly MemoryTabContext _context = new();



    // 篇章视窗的时间跨度（Tick 数），默认一年，受 MinimumSpan/MaximumSpan 约束。
    private float _chronicleSpanTicks = GenDate.TicksPerYear;

    private int _chronicleStartTick;
    private int _chronicleEndTick;



    // 当前可见的篇章列表，每帧由 Refresh() 从 MemoryComp 重建并按标签过滤
    // 非空，不会有空元素。enjoy
    private readonly List<MemoryEntry> _chapters = new();


    // 篇章视窗中心对应的游戏 Tick，与阅读光标 CursorTick 相互独立。
    private float _chronicleCenterTick;

    // 是否正在拖动时间轴上的阅读光标。
    private bool _draggingCursor;
    // 是否已按下鼠标但尚未达到拖拽阈值，用于区分单击与框选。
    private bool _dragArmed;


    // 框选
    private bool _dragSelecting; // 正在框选
    private Vector2 _dragStart; // 框选起点
    private Vector2 _dragCurrent; // 框选终点
    private HashSet<MemoryEntry> _dragBaseSelection = new(); // 已选中集合，用于在 Ctrl 模式下保留原选择并叠加新选区。
    private MemoryEntry _selectionAnchor; // Shift 范围选择的锚点


    private ChronicleAxis _axis = new();



    public MemoryChronicle(MemoryTabContext context)
    {
        _context = context;
        _context.ResetState += ResetMutiSelect;
        _context.RePositionCursor += KeepCursorVisible;
    }

    // 主入口：布局标题、篇章区、时间轴，再处理输入、绘制光标与篇章选择。
    public void Draw(Rect rect)
    {
        // 绘制背景
        Widgets.DrawMenuSection(rect);

        // 没有有效记忆档案时只画一个占位提示，不进入后续绘制流程。
        if (!_context.HasMemory)
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, "RimTalk.Memory.UI.InvalidChronicle".Translate());
            return;
        }

        // 每帧刷新自身状态
        RefreshChapter();

        // 三段布局：标题条 → 篇章区 → 时间轴底栏，竖向自上而下排列。
        Rect inner = rect.ContractedBy(8f);
        float width = inner.width;
        float x = inner.x;
        float y = inner.y;

        // 标题条绘制：篇章数 + 当前视窗跨度（换算成天）
        Rect titleRect = new(x, y, width, TitleHeight);
        using (new TextBlock(GameFont.Tiny, new Color(0.72f, 0.78f, 0.82f)))
            Widgets.Label(titleRect, "RimTalk_Archive_ChronicleTitle".Translate((_chronicleEndTick - _chronicleStartTick / GenDate.TicksPerDay).Named("DAYS")));
        y += TitleHeight;

        // 内部派发时间轴绘制：刻度线 + 日期文本
        float bottomY = inner.yMax;
        Rect axisRect = new(x, bottomY - AxisHeight, width, AxisHeight);
        _axis.DrawAxis(axisRect, _chronicleStartTick, _chronicleEndTick);







        // 篇章栏绘制：背景色 + 篇章卡片 + 重叠徽标
        y += ChapterGap;
        Rect chapterRect = new(x, y, width, inner.height - TitleHeight - AxisHeight - 5f);




        // 篇章栏与时间轴的底色。
        Widgets.DrawBoxSolid(chapterRect, new Color(0.07f, 0.08f, 0.09f, 0.82f));

        // 把视窗跨度/中心约束到生命起止范围内。
        int minimumTick = _context.LifeStartTick;
        int maximumTick = _context.LifeCurrentTick;
        NormalizeViewport(minimumTick, maximumTick);

        // 篇章视窗和阅读光标是独立状态，光标可以出现在时间轴上的任意横向位置。
        float halfSpan = _chronicleSpanTicks * 0.5f;
        float startTick = _chronicleCenterTick - halfSpan;
        float endTick = _chronicleCenterTick + halfSpan;
        HashSet<MemoryEntry> selection = _context.Selection;
        DrawChapters(chapterRect, _chapters, startTick, endTick, selection);

        // 输入处理可能改动 CursorTick 并返回“时间流需要跟随定位”的请求。
        bool requestedTimelinePosition = HandleInput(
            chapterRect,
            axisRect,
            startTick,
            endTick,
            minimumTick,
            maximumTick);
        DrawCursor(chapterRect, axisRect, _context.CursorTick, startTick, endTick);
        HandleChapterSelection(chapterRect, _chapters, startTick, endTick, selection);
        // 把“需要时间流定位”的请求上交给上下文，由它驱动时间流组件。
        _context.RaiseRePositionTimeline();
    }

    // 每帧从 MemoryComp 的 CLPA 档案重建篇章列表，只应用标签过滤。
    public void RefreshChapter()
    {
        _chapters.Clear();
        string query = _context.TagFilter?.Trim();
        _chapters.AddRange(_context.MemoryComp.ArchiveMemories
            .Where(memory =>
            memory is not null
            && (
            string.IsNullOrWhiteSpace(query)
            || (memory.Tags?.Any(tag => tag?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ?? false)
            )));
    }


    // 绘制篇章卡片（选中框、置顶按钮、内容摘要），并对相互重叠的篇章生成可点击的折叠入口。
    private void DrawChapters(
        Rect rect,
        IReadOnlyList<MemoryEntry> chapters,
        float startTick,
        float endTick,
        HashSet<MemoryEntry> selection)
    {
        // hits 同时用于本帧的重叠检测与点击命中，按时间顺序遍历。
        List<ChapterHit> hits = new();
        foreach (MemoryEntry chapter in chapters.OrderBy(memory => memory.GameTick))
        {
            // 篇章有结束 Tick 时取之，否则视作瞬时点（起止相同）。
            int chapterEnd = chapter.EndGameTick > chapter.GameTick ? chapter.EndGameTick : chapter.GameTick;
            // 整个篇章都在视窗外则跳过，节省绘制。
            if (chapterEnd < startTick || chapter.GameTick > endTick) continue;

            // 把篇章起止 Tick 映射到屏幕横向坐标，并裁剪到视窗边界内。
            float xMin = TickToX(Math.Max(chapter.GameTick, startTick), startTick, endTick, rect);
            float xMax = TickToX(Math.Min(chapterEnd, endTick), startTick, endTick, rect);
            float width = Math.Max(8f, xMax - xMin);
            // 重叠篇章轻微错位，保持“档案叠放”视觉，同时避免无限增加泳道高度。
            int overlapDepth = hits.Count(hit => hit.Rect.Overlaps(new Rect(xMin, rect.y + 5f, width, rect.height - 10f)));
            float offset = Math.Min(12f, overlapDepth * 3f);
            Rect chapterCard = new(xMin, rect.y + 5f + offset, width, Math.Max(18f, rect.height - 10f - offset));
            hits.Add(new ChapterHit(chapter, chapterCard));

            // 卡片底色：悬停时换高亮色，否则用篇章默认色。
            Widgets.DrawBoxSolid(chapterCard, Mouse.IsOver(chapterCard) ? ChapterHoverColor : ChapterColor);
            GUI.color = MemoryArchivePalette.Archive;
            Widgets.DrawBox(chapterCard, 1);
            GUI.color = Color.white;
            // 被选中的篇章加粗描边。
            if (selection.Contains(chapter))
            {
                GUI.color = new Color(0.84f, 0.74f, 0.46f);
                Widgets.DrawBox(chapterCard, 2);
                GUI.color = Color.white;
            }
            // 卡片足够宽时才显示内容摘要，按宽度估算可容纳的字数。
            if (chapterCard.width >= 54f)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(chapterCard.ContractedBy(4f), Truncate(chapter.Content, Mathf.FloorToInt(chapterCard.width / 7f)));
                Text.Font = GameFont.Small;
            }

            // 卡片足够宽时才显示置顶按钮（◆ 已置顶 / ◇ 未置顶），点击切换置顶状态。
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
            // 把整簇卡片的并集范围作为徽标定位参考，徽标贴在右上方但不超过区域右边界。
            Rect bounds = cluster.Select(hit => hit.Rect).Aggregate(Union);
            Rect badge = new(Math.Min(rect.xMax - 34f, bounds.xMax - 30f), rect.y + 2f, 32f, 18f);
            Widgets.DrawBoxSolid(badge, new Color(0.18f, 0.14f, 0.21f, 0.98f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(badge, $"+{cluster.Count() - 1}");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            // 点击徽标弹出该簇内全部篇章的列表（按时间倒序），供用户精确选择。
            if (Widgets.ButtonInvisible(badge))
            {
                OpenOverlapMenu(cluster.Select(hit => hit.Memory).OrderByDescending(memory => memory.GameTick).ToList());
            }
        }

    }

    // 篇章区鼠标交互：单击选中、Ctrl 切换、Shift 范围选中，按住拖拽则框选（叠加在拖拽前的既有选区之上）。
    private void HandleChapterSelection(
        Rect rect,
        IReadOnlyList<MemoryEntry> chapters,
        float startTick,
        float endTick,
        HashSet<MemoryEntry> selection)
    {
        Event current = Event.current;
        Vector2 mouse = current.mousePosition;
        // 重新计算每个篇章在本帧几何下的命中矩形；与 DrawChapters 的几何一致但相互独立。
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

        // 阶段一：左键按下 → 武装拖拽，并快照当前选区（Ctrl 模式下保留以便叠加，否则从空开始）。
        if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(mouse))
        {
            _dragArmed = true;
            _dragSelecting = false;
            _dragStart = mouse;
            _dragCurrent = mouse;
            _dragBaseSelection = current.control ? new HashSet<MemoryEntry>(selection) : new HashSet<MemoryEntry>();
            return;
        }

        // 阶段二：武装后若拖拽超过 5px 阈值，进入框选模式；选区 = 基底选区 ∪ 与选框相交的篇章。
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

        // 阶段三：松开鼠标。若自始至终没进入框选 → 当作单击处理。
        if (current.rawType == EventType.MouseUp)
        {
            _dragArmed = false;
            if (!_dragSelecting)
            {
                // 多个卡片重叠时，选最窄的那张（最上层/最新），更符合“点谁选谁”的直觉。
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
            // 框选结束，收尾。
            _dragSelecting = false;
            current.Use();
        }

        // 阶段四：框选进行中，叠加绘制半透明选框（每帧绘制，IMGUI 风格）。
        if (_dragSelecting)
        {
            Rect box = SelectionBox();
            Widgets.DrawBoxSolid(box, new Color(0.72f, 0.58f, 0.28f, 0.18f));
            GUI.color = new Color(0.9f, 0.72f, 0.34f);
            Widgets.DrawBox(box, 1);
            GUI.color = Color.white;
        }
    }

    // 应用修饰键语义选中篇章并上报焦点。Shift：以锚点为起点范围选中；Ctrl：切换；默认：单选。
    private void SelectChapter(
        MemoryEntry memory,
        IReadOnlyList<MemoryEntry> chapters,
        HashSet<MemoryEntry> selection)
    {
        // 按时间排序的可见列表，用于 Shift 范围选择的区间索引。
        List<MemoryEntry> visible = chapters.OrderBy(chapter => chapter.GameTick).ToList();
        // Shift：以上次单击的锚点为起点，选中锚点→当前篇章之间的连续区间。
        if (Event.current.shift && _selectionAnchor is not null)
        {
            int from = visible.IndexOf(_selectionAnchor);
            int to = visible.IndexOf(memory);
            if (from >= 0 && to >= 0)
                selection.UnionWith(visible.Skip(Math.Min(from, to)).Take(Math.Abs(from - to) + 1));
            else
            {
                // 锚点已不在可见列表（被过滤掉等），退化为单选。
                selection.Clear();
                selection.Add(memory);
            }
        }
        // Ctrl：切换选中——已选则取消，未选则加入。
        else if (Event.current.control)
        {
            if (!selection.Add(memory)) selection.Remove(memory);
        }
        // 默认：清空后单选。
        else
        {
            selection.Clear();
            selection.Add(memory);
        }
        // 记下本次选中作为下一次 Shift 的锚点，并通知上下文聚焦此篇章。
        _selectionAnchor = memory;
        _context.Focus(memory);
    }

    private Rect SelectionBox() => new(
        Math.Min(_dragStart.x, _dragCurrent.x),
        Math.Min(_dragStart.y, _dragCurrent.y),
        Math.Abs(_dragStart.x - _dragCurrent.x),
        Math.Abs(_dragStart.y - _dragCurrent.y));

    // 传递式邻接聚类：与已有组任一条相交的篇章归入同一重叠入口，便于为完全重合的篇章群生成单个折叠入口。
    private static IEnumerable<IGrouping<string, ChapterHit>> BuildOverlapClusters(List<ChapterHit> hits)
    {
        // 每个篇章映射到一个簇键；同簇篇章互相（传递式）相交。
        Dictionary<ChapterHit, string> keys = new();
        int clusterIndex = 0;
        // 从左到右处理，保证邻接关系按位置推进。
        foreach (ChapterHit hit in hits.OrderBy(item => item.Rect.x))
        {
            // 找一个已登记且与当前篇章相交的簇，加入它；否则开新簇。
            string existing = keys
                .Where(pair => pair.Key.Rect.Overlaps(hit.Rect))
                .Select(pair => pair.Value)
                .FirstOrDefault();
            keys[hit] = existing ?? $"cluster:{clusterIndex++}";
        }
        // 按簇键分组返回，簇内只有 1 个的会在调用处被过滤掉。
        return keys.Keys.GroupBy(hit => keys[hit]);
    }

    private void OpenOverlapMenu(List<MemoryEntry> memories)
    {
        // 每个篇章做成一个菜单项：显示“年龄·内容预览”，点击后聚焦该篇章。
        Find.WindowStack.Add(new FloatMenu(memories.Select(memory => new FloatMenuOption(
            $"{memory.AgeString} · {Truncate(memory.Content, 80)}",
            () => _context.Focus(memory))).ToList()));
    }

    // Pawn/存档切换时清空上一上下文的残留状态（Shift 锚点、拖拽中状态、篇章缓存），避免新上下文误用旧选择或旧拖拽轨迹。
    public void ResetMutiSelect()
    {
        _selectionAnchor = null;
        _dragArmed = false;
        _dragSelecting = false;
    }

    /// <summary>
    /// 时间流主动更新 CursorTick 后调用。光标仍在视窗内时不移动篇章；越界时才让视窗追到光标边缘。
    /// </summary>
    public void KeepCursorVisible()
    {
        int minimumTick = _context.LifeStartTick;
        int maximumTick = _context.LifeCurrentTick;
        // 先把视窗约束到合法范围，拿到本帧可见窗口。
        NormalizeViewport(minimumTick, maximumTick);
        float halfSpan = _chronicleSpanTicks * 0.5f;
        float startTick = _chronicleCenterTick - halfSpan;
        float endTick = _chronicleCenterTick + halfSpan;
        // 光标还在视窗内 → 不动视窗（用户可能在自由浏览其他时段）。
        // 光标越过边界 → 把视窗边缘对齐到光标，让光标回到可见范围。
        if (_context.CursorTick < startTick)
            _chronicleCenterTick = _context.CursorTick + halfSpan;
        else if (_context.CursorTick > endTick)
            _chronicleCenterTick = _context.CursorTick - halfSpan;
        ClampCenter(minimumTick, maximumTick);
    }

    // 输入：篇章区滚轮平移视窗、时间轴滚轮缩放视窗；在时间轴上按下/拖拽则移动光标并请求时间流定位。
    private bool HandleInput(
        Rect chapterRect,
        Rect axisRect,
        float startTick,
        float endTick,
        int minimumTick,
        int maximumTick)
    {
        Event current = Event.current;
        // requested 表示本次输入是否改动过 CursorTick，调用方据此决定是否要让时间流重新定位。
        bool requested = false;
        // ① 篇章区滚轮 → 仅平移视窗中心，不动光标、不动时间流阅读位置。
        if (current.type == EventType.ScrollWheel && chapterRect.Contains(current.mousePosition))
        {
            // 篇章滚动只平移可见时间范围；CursorTick 和时间流阅读位置保持不变。
            _chronicleCenterTick += current.delta.y * _chronicleSpanTicks * 0.06f;
            ClampCenter(minimumTick, maximumTick);
            current.Use();
        }
        // ② 时间轴滚轮 → 缩放视窗跨度。
        else if (current.type == EventType.ScrollWheel && axisRect.Contains(current.mousePosition))
        {
            // 未触边时保持光标的屏幕横向比例；触边时边界对齐优先。
            // oldFraction：缩放前光标在旧视窗中的横向比例（0=最左，1=最右）。
            float oldFraction = Mathf.InverseLerp(startTick, endTick, _context.CursorTick);
            // factor>1 放大跨度，<1 缩小；用 Pow 让滚轮手感呈指数变化。
            float factor = Mathf.Pow(1.18f, current.delta.y);
            float availableSpan = Math.Max(MinimumSpan, maximumTick - minimumTick);
            _chronicleSpanTicks = Mathf.Clamp(
                _chronicleSpanTicks * factor,
                MinimumSpan,
                Math.Min(MaximumSpan, availableSpan));
            // 调整中心，使光标在新视窗中保持同样的横向比例（视觉上光标“钉”在原处）。
            _chronicleCenterTick = _context.CursorTick + (0.5f - oldFraction) * _chronicleSpanTicks;
            ClampCenter(minimumTick, maximumTick);
            current.Use();
        }

        // ③ 时间轴上左键按下 → 开始拖拽光标，并把点击位置映射成 CursorTick。
        if (current.type == EventType.MouseDown && current.button == 0 && axisRect.Contains(current.mousePosition))
        {
            _draggingCursor = true;
            _context.CursorTick = XToTick(current.mousePosition.x, startTick, endTick, axisRect);
            requested = true;
            current.Use();
        }
        // ④ 拖拽中 → 持续把鼠标 x（限定在轴范围内）映射成 CursorTick。
        else if (_draggingCursor && current.type == EventType.MouseDrag && current.button == 0)
        {
            _context.CursorTick = XToTick(Mathf.Clamp(current.mousePosition.x, axisRect.x, axisRect.xMax), startTick, endTick, axisRect);
            requested = true;
            current.Use();
        }
        // ⑤ 松开 → 结束光标拖拽。
        if (_draggingCursor && current.rawType == EventType.MouseUp)
            _draggingCursor = false;
        return requested;
    }

    // 把视窗跨度/中心约束到合法范围；中心未初始化（==0）时跟随阅读光标，再 clamp 到生命起止之间。
    private void NormalizeViewport(int minimumTick, int maximumTick)
    {
        // 生命跨度可能比 MaximumSpan 还小，取两者较小值作为跨度上限。
        float availableSpan = Math.Max(MinimumSpan, maximumTick - minimumTick);
        _chronicleSpanTicks = Mathf.Clamp(
            _chronicleSpanTicks,
            MinimumSpan,
            Math.Min(MaximumSpan, availableSpan));
        // 首帧（中心还是 0）时把视窗对齐到阅读光标，作为初始位置。
        if (_chronicleCenterTick == 0f)
            _chronicleCenterTick = _context.CursorTick;
        ClampCenter(minimumTick, maximumTick);
    }

    // 视窗中心 clamp 到 [生命起点+半跨度, 生命终点-半跨度]；生命跨度放不下时直接居中。
    private void ClampCenter(int minimumTick, int maximumTick)
    {
        float halfSpan = _chronicleSpanTicks * 0.5f;
        // 视窗中心可移动范围：保证左右各 halfSpan 不超出生命起止。
        float minimumCenter = minimumTick + halfSpan;
        float maximumCenter = maximumTick - halfSpan;
        _chronicleCenterTick = minimumCenter <= maximumCenter
            ? Mathf.Clamp(_chronicleCenterTick, minimumCenter, maximumCenter)
            // 生命跨度放不下整个视窗 → 直接居中于生命区间，接受两侧溢出。
            : (minimumTick + maximumTick) * 0.5f;
    }

    // 视窗内绘制阅读光标竖线与时间轴上的三角顶帽；光标越出视窗则不绘制。
    private static void DrawCursor(Rect chapterRect, Rect axisRect, int tick, float startTick, float endTick)
    {
        // 光标在视窗外不绘制，避免出现“悬空竖线”。
        if (tick < startTick || tick > endTick) return;
        float x = TickToX(tick, startTick, endTick, axisRect);
        Color color = new(0.93f, 0.72f, 0.34f, 0.95f);
        // 一根贯穿篇章区+时间轴的竖线。
        Widgets.DrawBoxSolid(new Rect(x, chapterRect.y, 2f, chapterRect.height + axisRect.height), color);
        // 时间轴顶部的小三角顶帽，让光标位置更醒目。
        Widgets.DrawBoxSolid(new Rect(x - 5f, axisRect.y, 12f, 5f), color);
    }

    private static float TickToX(float tick, float startTick, float endTick, Rect rect) =>
        rect.x + (tick - startTick) / (endTick - startTick) * rect.width; // tick 在视窗内的相对比例 × 宽度

    private static int XToTick(float x, float startTick, float endTick, Rect rect) =>
        Mathf.RoundToInt(Mathf.Lerp(startTick, endTick, Mathf.InverseLerp(rect.x, rect.xMax, x))); // 屏幕像素 → 视窗内比例 → Tick，四舍五入

    private static Rect Union(Rect left, Rect right)
    {
        float x = Math.Min(left.x, right.x);
        float y = Math.Min(left.y, right.y);
        // 取两矩形左上角最小、右下角最大，合成最小外接矩形。
        return new Rect(x, y, Math.Max(left.xMax, right.xMax) - x, Math.Max(left.yMax, right.yMax) - y);
    }

    private static string DateLabel(int tick) =>
        GenDate.DateMonthYearStringAt(GenDate.TickGameToAbs(tick), Vector2.zero); // 游戏 Tick → 绝对 Tick → 带年月日期

    private static string Truncate(string text, int max)
    {
        // 空或未超长直接返回原值（空值兜底成 string.Empty，避免返回 null）。
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
        // 截到 max-1 字符再加省略号，保证总长度恰好为 max。
        return text.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    // 命中测试记录：把篇章与其屏幕卡片矩形绑在一起，绘制/点击/框选三处共用。
    private sealed class ChapterHit
    {
        public MemoryEntry Memory { get; }
        public Rect Rect { get; }
        public ChapterHit(MemoryEntry memory, Rect rect) { Memory = memory; Rect = rect; }
    }
}
