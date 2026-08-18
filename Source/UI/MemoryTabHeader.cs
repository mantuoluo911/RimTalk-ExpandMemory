using RimTalk.Memory.Debug;
using RimTalk.Memory.Maintenance;
using RimTalk.Memory.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

internal sealed class MemoryTabHeader
{
    // 尺寸常量
    private const float Gap = MemoryTabWindow.Gap;
    private const float PawnWidgetWidth = 250f;
    private const float FilterWidgetWidth = 170f;
    private const float DefaultWidgetWidth = MemoryTabWindow.DefaultWidgetWidth;


    private const float PawnSelectorWidth = 300f;

    // 共享状态
    private readonly MemoryTabContext _context;

    // 预计算的枚举值列表，供下拉菜单使用
    private static readonly List<MemoryLayer?> _allLayers = EnumUtil.AllEnumValues<MemoryLayer>()?
        .Select(layer => (MemoryLayer?)layer).Prepend(null).ToList() ?? new();
    private static readonly List<MemoryType?> _allTypes = EnumUtil.AllEnumValues<MemoryType>()?
        .Select(type => (MemoryType?)type).Prepend(null).ToList() ?? new();

    public MemoryTabHeader(MemoryTabContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        _context = context;
    }

    public void Draw(Rect rect)
    {
        // 绘制背景，控制缩进，初始化 xy 游标
        Widgets.DrawMenuSection(rect);
        Rect inRect = rect.ContractedBy(6f);
        float height = inRect.height;
        float x = inRect.x;
        float y = inRect.y;

        // 绘制角色选择控件
        Rect pawnRect = new(x, y, PawnWidgetWidth, height);
        if (Widgets.ButtonText(pawnRect, _context.MemoryComp?.parent?.LabelShort ?? "RimTalk.Memory.UI.Need_SelectPawn".Translate()))
        {
            Rect totalRect = _context.InRect;
            // 计算选择器的**绝对**矩形
            float selectorY = pawnRect.y + totalRect.y;
            Rect pawnSelectorRect = new(
                pawnRect.xMax + totalRect.x,
                selectorY,
                PawnSelectorWidth,
                Math.Max(totalRect.yMax - selectorY, 0f)
                );
            Find.WindowStack.Add(new MemoryTabPawnSelector(pawnSelectorRect));
        }
        x += PawnWidgetWidth + Gap;

        // 绘制过滤器控件
        Rect filterRect = new(x, y, FilterWidgetWidth, height);
        _context.TagFilter = Widgets.TextField(filterRect, _context.TagFilter);
        x += FilterWidgetWidth + Gap;

        TooltipHandler.TipRegion(filterRect, "RimTalk.Memory.UI.TagFilter".Translate());

        // 绘制层级过滤控件
        Rect layerRect = new(x, y, DefaultWidgetWidth, height);
        if (Widgets.ButtonText(layerRect, _context.LayerFilter.Translate()))
            OpenLayerFilterMenu();
        x += DefaultWidgetWidth + Gap;

        TooltipHandler.TipRegion(layerRect, "RimTalk.Memory.UI.LayerFilter".Translate());

        // 绘制类型过滤控件
        Rect typeRect = new(x, y, DefaultWidgetWidth, height);
        if (Widgets.ButtonText(typeRect, _context.TypeFilter.Translate()))
            OpenTypeFilterMenu();
        x += DefaultWidgetWidth + Gap;

        TooltipHandler.TipRegion(typeRect, "RimTalk.Memory.UI.TypeFilter".Translate());

        // 绘制工具按钮
        float xRight = inRect.xMax;
        Rect toolsRect = new(xRight - DefaultWidgetWidth, y, DefaultWidgetWidth, height);
        if (Widgets.ButtonText(toolsRect, "RimTalk.Memory.UI.Tools".Translate()))
            OpenToolsMenu();
        xRight -= DefaultWidgetWidth + Gap;

        // 绘制记忆统计信息
        if (_context.MemoryComp is { } memoryComp)
        {
            Rect statsRect = new(x, y, Math.Max(0f, xRight - x), height);
            using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleLeft))
                Widgets.Label(
                    statsRect,
                    $"ABM {memoryComp.ActiveMemories?.Count ?? 0} · SCM {memoryComp.SituationalMemories?.Count ?? 0} · " +
                    $"ELS {memoryComp.EventLogMemories?.Count ?? 0} · CLPA {memoryComp.ArchiveMemories?.Count ?? 0}"
                    );
        }
    }

    private void OpenLayerFilterMenu() =>
        Find.WindowStack.Add(new FloatMenu(_allLayers
            .Select(layer => new FloatMenuOption(layer.Translate(), () => _context.LayerFilter = layer))
            .ToList()));

    private void OpenTypeFilterMenu() =>
        Find.WindowStack.Add(new FloatMenu(_allTypes
            .Select(type => new FloatMenuOption(type.Translate(), () => _context.TypeFilter = type))
            .ToList()));

    private void OpenToolsMenu()
    {
        var windowStack = Find.WindowStack;
        var memoryComp = _context.MemoryComp;
        windowStack.Add(new FloatMenu([
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.Knowledge"), () => windowStack.Add(new Dialog_CommonKnowledge())),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.CreateMemory"), () => windowStack.Add(new MemoryCreateDialog(memoryComp))),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.Preview"), () => windowStack.Add(new Dialog_InjectionPreview())),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.SummaryPrompt"), () => windowStack.Add(new Dialog_PromptEditor())),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.Export"), () => CustomScribe.Export(memoryComp)),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.Import"), OpenImportMenu),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.SummarizeAll"), MemorySummarizer.SummarizeAll),
            new(MemoryArchiveText.Get("RimTalk.Memory.UI.OperationGuide"), () => windowStack.Add(new Dialog_MessageBox("RimTalk.Memory.UI.Guide".Translate())))
        ]));
    }

    private void OpenImportMenu()
    {
        var targetComp = _context.MemoryComp;
        var windowStack = Find.WindowStack;
        windowStack.Add(new FloatMenu((CustomScribe.GetImportFiles() ?? [])
                .Select(path => new FloatMenuOption(Path.GetFileName(path), () => windowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "RimTalk.Memory.UI.ConfirmImport".Translate(),
                    () => CustomScribe.Import(path, targetComp)
                    ))))
                .Prepend(new FloatMenuOption("RimTalk.Memory.UI.OpenImportFolder".Translate(), CustomScribe.OpenExportFolder))
                .ToList()));
    }
}
