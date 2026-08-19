using RimTalk.MemoryPatch;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// “角色人生档案工作台”主标签窗口，负责生命周期、布局和区域派发
/// </summary>
public sealed class MemoryTabWindow : MainTabWindow
{
    // 尺寸常量
    // 共用
    public const float Gap = 8f;
    public const float DefaultWidgetWidth = 114f;
    public const float ScrollbarWidth = 16f;
    // 私有
    private const float HeaderHeight = 46f;
    private const float ChronicleHeight = 104f;
    private const float SelectionBarHeight = 44f;
    private const float SplitterWidth = 6f;
    private const float MinDetailsWidth = 300f;
    private const float MinTimelineWidth = 520f;

    // 窗口共享状态
    private readonly MemoryTabContext _context;

    // 窗口子实例
    // 窗口头
    private readonly MemoryTabHeader _header;
    // 篇章区
    private readonly MemoryChronicle _chronicle;
    // 时间轴
    private readonly MemoryTimelineView _timeline;
    // 记忆详情
    private readonly MemoryDetails _details;
    // 动作栏（选中记忆后展示）
    private readonly MemorySelectionBar _selectionBar;

    // 时间轴-记忆详情区宽度调整锚点
    private float _timelineResizeAnchor = -1;

    // 期望的标签页尺寸，实际绘制区会有缩进
    public override Vector2 RequestedTabSize => new(1280f, 760f);

    // 构造函数，会同时以 context 注册各个子实例
    // 并建立 _chronicle、_timeline 和 cursor 的交叉引用
    public MemoryTabWindow()
    {
        _context = new MemoryTabContext();
        _header = new MemoryTabHeader(_context);
        _chronicle = new MemoryChronicle(_context);
        _timeline = new MemoryTimelineView(_context);
        _details = new MemoryDetails(_context);
        _selectionBar = new MemorySelectionBar(_context);

        doCloseX = true;
    }

    /// <summary>
    /// 主标签窗口绘制入口，负责布局和派发
    /// 因为是派发而不是绘制，所以不使用 xy 游标
    /// </summary>
    public override void DoWindowContents(Rect inRect)
    {
        _context.Update(inRect);

        float totalWidth = inRect.width;
        float totalHeight = inRect.height;

        // 派发 header 绘制
        Rect headerRect = new(0f, 0f, totalWidth, HeaderHeight);
        _header.Draw(headerRect);

        // 准备派发内容区绘制
        float contentTop = headerRect.yMax + Gap;
        float contentHeight = totalHeight - contentTop;

        bool showSelectionBar = _context.Selection?.Count > 0;
        if (showSelectionBar) contentHeight -= SelectionBarHeight;

        // 派发篇章区和时间轴区绘制
        float chronicleWidth = Math.Clamp(
            RimTalkMemoryPatchMod.Settings.MemoryTabTimeLineWidth,
            MinTimelineWidth,
            totalWidth - MinDetailsWidth - Gap - SplitterWidth);

        Rect chronicleRect = new(0f, contentTop, chronicleWidth, ChronicleHeight);
        _chronicle.Draw(chronicleRect);

        Rect timelineRect = new(0f, chronicleRect.yMax + Gap, chronicleWidth, contentHeight - ChronicleHeight - Gap);
        _timeline.Draw(timelineRect);

        // 派发时间轴-记忆详情区分割条绘制和拖拽调整
        Rect splitterRect = new(chronicleRect.xMax + Gap, contentTop, SplitterWidth, contentHeight);
        HandleDetailsResize(splitterRect, totalWidth);

        // 派发记忆详情区绘制
        Rect detailsRect = new(splitterRect.xMax, contentTop, totalWidth - chronicleWidth - Gap - SplitterWidth, contentHeight);
        _details.Draw(detailsRect);

        if (showSelectionBar)
            _selectionBar.Draw(new Rect(0f, totalHeight - SelectionBarHeight, totalWidth, SelectionBarHeight));
    }

    private void HandleDetailsResize(Rect rect, float totalWidth)
    {
        Widgets.DrawBoxSolid(rect, Mouse.IsOver(rect)
            ? new Color(0.55f, 0.6f, 0.66f, 0.8f)
            : new Color(0.25f, 0.28f, 0.32f, 0.8f));

        Event current = Event.current;
        if (current is { type: EventType.MouseDown, button: 0 } && rect.Contains(current.mousePosition))
        {
            _timelineResizeAnchor = current.mousePosition.x;
            current.Use();
        }
        if (_timelineResizeAnchor != -1 && current.type is EventType.MouseDrag)
        {
            var settings = RimTalkMemoryPatchMod.Settings;
            settings.MemoryTabTimeLineWidth = Math.Clamp(
                settings.MemoryTabTimeLineWidth + (current.mousePosition.x - _timelineResizeAnchor),
                MinTimelineWidth,
                totalWidth - MinDetailsWidth - Gap - SplitterWidth);
            current.Use();
        }
        if (_timelineResizeAnchor != -1 && current.rawType is EventType.MouseUp)
        {
            _timelineResizeAnchor = -1;
            RimTalkMemoryPatchMod.Settings.Write();
        }
    }
}
