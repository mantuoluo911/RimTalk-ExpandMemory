using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// 常驻详情栏，负责只读展示和原位编辑草稿。
/// 保存时提交 MemoryEditCommand；固定与所有权变化不在编辑路径中处理。
/// </summary>
internal sealed class MemoryDetails
{
    private readonly MemoryTabContext _context;
    private Vector2 _scroll;
    private MemoryEntry _editingMemory;
    private string _content = string.Empty;
    private string _notes = string.Empty;
    private string _tags = string.Empty;
    private float _importance;

    public MemoryDetails(MemoryTabContext context) => _context = context;

    public void Draw(Rect rect)
    {
        MemoryEntry memory = _context.FocusedMemory;
        FourLayerMemoryComp comp = _context.MemoryComp;
        Widgets.DrawMenuSection(rect);
        Rect inner = rect.ContractedBy(12f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 34f), MemoryArchiveText.Get("RimTalk_Archive_Details"));
        Text.Font = GameFont.Small;
        Rect body = new(inner.x, inner.y + 42f, inner.width, inner.height - 42f);

        if (memory is null)
        {
            CancelEdit();
            Widgets.Label(body, MemoryArchiveText.Get("RimTalk_Archive_Guide"));
            return;
        }

        // 焦点切换时丢弃旧草稿，避免返回原记忆后意外恢复过期编辑状态。
        if (_editingMemory is not null && _editingMemory != memory) CancelEdit();
        if (_editingMemory != memory) DrawReadOnly(body, memory, comp);
        else DrawEditor(body, memory, comp);
    }

    private void DrawReadOnly(Rect rect, MemoryEntry memory, FourLayerMemoryComp comp)
    {
        float notesExtra = string.IsNullOrWhiteSpace(memory.Note)
            ? 0f
            : Math.Max(44f, Text.CalcHeight(memory.Note, rect.width - 20f)) + 32f;
        float contentHeight = Math.Max(rect.height, Text.CalcHeight(memory.Content ?? string.Empty, rect.width - 20f) + 360f + notesExtra);
        Rect view = new(0f, 0f, rect.width - 16f, contentHeight);
        Widgets.BeginScrollView(rect, ref _scroll, view);
        float y = 0f;
        Widgets.Label(new Rect(0f, y, view.width, 24f), $"{MemoryArchiveText.Layer(memory.Layer)} · {MemoryArchiveText.Type(memory.Type)}");
        y += 26f;
        GUI.color = new Color(0.68f, 0.72f, 0.75f);
        Widgets.Label(new Rect(0f, y, view.width, 42f), TimeLabel(memory));
        GUI.color = Color.white;
        y += 48f;

        float textHeight = Math.Max(72f, Text.CalcHeight(memory.Content ?? string.Empty, view.width));
        Widgets.Label(new Rect(0f, y, view.width, textHeight), memory.Content ?? string.Empty);
        y += textHeight + 14f;
        Widgets.DrawLineHorizontal(0f, y, view.width);
        y += 12f;
        Widgets.Label(new Rect(0f, y, view.width, 26f), MemoryArchiveText.Get("RimTalk_Archive_Importance", memory.Importance.ToString("F2")));
        y += 26f;
        Widgets.Label(new Rect(0f, y, view.width, 26f), MemoryArchiveText.Get("RimTalk_Archive_Activity", memory.Activity.ToString("F2")));
        y += 26f;
        Widgets.Label(new Rect(0f, y, view.width, 42f), MemoryArchiveText.Get("RimTalk_Archive_TagsValue", string.Join(", ", memory.Tags ?? Enumerable.Empty<string>())));
        y += 44f;
        if (!string.IsNullOrWhiteSpace(memory.relatedPawnName))
        {
            Widgets.Label(new Rect(0f, y, view.width, 26f), MemoryArchiveText.Get("RimTalk_Archive_RelatedPawn", memory.relatedPawnName));
            y += 28f;
        }
        if (!string.IsNullOrWhiteSpace(memory.location))
        {
            Widgets.Label(new Rect(0f, y, view.width, 26f), MemoryArchiveText.Get("RimTalk_Archive_Location", memory.location));
            y += 28f;
        }
        if (!string.IsNullOrWhiteSpace(memory.Note))
        {
            Widgets.Label(new Rect(0f, y, view.width, 24f), MemoryArchiveText.Get("RimTalk_Archive_Notes"));
            y += 24f;
            float notesHeight = Math.Max(44f, Text.CalcHeight(memory.Note, view.width));
            GUI.color = new Color(0.78f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, y, view.width, notesHeight), memory.Note);
            GUI.color = Color.white;
            y += notesHeight + 8f;
        }
        string states = $"{MemoryArchiveText.Get(memory.IsPinned ? "RimTalk_Archive_Pinned" : "RimTalk_Archive_Unpinned")} · " +
                        MemoryArchiveText.Get(comp.Summarizer.CheckSummarizing(memory) ? "RimTalk_Archive_Summarizing" : comp.Summarizer.CheckSummarized(memory) ? "RimTalk_Archive_Summarized" : "RimTalk_Archive_NotSummarized");
        Widgets.Label(new Rect(0f, y, view.width, 26f), states);
        y += 34f;
        GUI.color = new Color(0.55f, 0.58f, 0.61f);
        Widgets.Label(new Rect(0f, y, view.width, 46f), MemoryArchiveText.Get("RimTalk_Archive_Technical", memory.Layer, memory.Id, memory.OriginId));
        GUI.color = Color.white;
        Widgets.EndScrollView();

        Rect edit = new(rect.xMax - 100f, rect.yMax - 36f, 100f, 36f);
        if (Widgets.ButtonText(edit, MemoryArchiveText.Get("RimTalk_Knowledge_Edit"))) BeginEdit(memory);
    }

    private void DrawEditor(Rect rect, MemoryEntry memory, FourLayerMemoryComp comp)
    {
        // 编辑态持续强制暂停；玩家必须保存或取消后才能恢复游戏时间。
        Find.TickManager?.Pause();
        Rect view = new(0f, 0f, rect.width - 16f, 520f);
        Widgets.BeginScrollView(rect, ref _scroll, view);
        float y = 0f;
        Widgets.Label(new Rect(0f, y, view.width, 24f), $"{MemoryArchiveText.Layer(memory.Layer)} · {MemoryArchiveText.Type(memory.Type)} · {MemoryArchiveText.Get("RimTalk_Archive_ReadOnlyClassification")}");
        y += 30f;
        Widgets.Label(new Rect(0f, y, view.width, 24f), MemoryArchiveText.Get("RimTalk_Archive_Content"));
        y += 24f;
        _content = Widgets.TextArea(new Rect(0f, y, view.width, 180f), _content);
        y += 190f;
        Widgets.Label(new Rect(0f, y, view.width, 24f), MemoryArchiveText.Get("RimTalk_Archive_Tags"));
        y += 24f;
        _tags = Widgets.TextField(new Rect(0f, y, view.width, 28f), _tags);
        y += 38f;
        Widgets.Label(new Rect(0f, y, view.width, 24f), MemoryArchiveText.Get("RimTalk_Archive_Notes"));
        y += 24f;
        _notes = Widgets.TextArea(new Rect(0f, y, view.width, 70f), _notes);
        y += 82f;
        Widgets.Label(new Rect(0f, y, view.width, 24f), MemoryArchiveText.Get("RimTalk_Archive_Importance", _importance.ToString("F2")));
        y += 22f;
        _importance = Widgets.HorizontalSlider(new Rect(0f, y, view.width, 24f), _importance, 0f, 1f, true);
        y += 34f;
        Widgets.EndScrollView();

        Rect cancel = new(rect.xMax - 100f, rect.yMax - 36f, 100f, 36f);
        Rect save = new(cancel.x - 108f, cancel.y, 100f, 36f);
        if (Widgets.ButtonText(save, MemoryArchiveText.Get("RimTalk_Knowledge_Save")))
        {
            MemoryEntry updated = MemoryArchiveCommands.Edit(comp, memory, new MemoryEditCommand
            {
                Content = _content,
                Notes = _notes,
                Tags = _tags.Split(',').ToList(),
                Importance = _importance
            });
            CancelEdit();
        }
        if (Widgets.ButtonText(cancel, MemoryArchiveText.Get("RimTalk_Knowledge_Cancel"))) CancelEdit();
    }

    private void BeginEdit(MemoryEntry memory)
    {
        // 编辑器操作独立草稿，保存前不修改业务对象。
        Find.TickManager?.Pause();
        _editingMemory = memory;
        _content = memory.Content ?? string.Empty;
        _notes = memory.Note ?? string.Empty;
        _tags = string.Join(", ", memory.Tags ?? Enumerable.Empty<string>());
        _importance = memory.Importance;
        _scroll = Vector2.zero;
    }

    private void CancelEdit()
    {
        _editingMemory = null;
    }

    private static string TimeLabel(MemoryEntry memory)
    {
        if (memory.Layer is MemoryLayer.Archive && memory.EndGameTick >= memory.GameTick)
            return $"{DateLabel(memory.GameTick)} - {DateLabel(memory.EndGameTick)}";
        return $"{DateLabel(memory.GameTick)} · {memory.AgeString}";
    }

    private static string DateLabel(int tick) =>
        GenDate.DateFullStringAt(GenDate.TickGameToAbs(tick), Vector2.zero);
}
