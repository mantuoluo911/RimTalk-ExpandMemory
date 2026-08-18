using System.Linq;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

internal sealed class MemorySelectionBar
{
    private readonly MemoryTabContext _context;

    public MemorySelectionBar(MemoryTabContext context) => _context = context;

    public void Draw(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        Rect inner = rect.ContractedBy(6f);
        float x = inner.xMax;
        const float width = 90f;
        const float gap = 6f;

        Widgets.Label(new Rect(inner.x, inner.y, 180f, inner.height),
            MemoryArchiveText.Get("RimTalk.Memory.UI.Selected", _context.Selection.Count));

        if (Widgets.ButtonText(new Rect(x - width, inner.y, width, inner.height),
                MemoryArchiveText.Get("RimTalk_Archive_ClearSelection")))
            _context.Selection.Clear();
        x -= width + gap;

        using (new GUIBlock(new Color(0.92f, 0.55f, 0.52f)))
            if (Widgets.ButtonText(new Rect(x - width, inner.y, width, inner.height),
                    MemoryArchiveText.Get("RimTalk.Memory.UI.Delete")))
                ConfirmDelete();
        x -= width + gap;

        using (new GUIBlock(_context.Selection.Any(memory =>
                   memory?.Layer is MemoryLayer.EventLog or MemoryLayer.Archive)))
            if (Widgets.ButtonText(new Rect(x - width, inner.y, width, inner.height),
                    MemoryArchiveText.Get("RimTalk_Archive_ActionArchive")))
                ConfirmArchive();
        x -= width + gap;

        using (new GUIBlock(_context.Selection.Any(memory =>
                   memory?.Layer is MemoryLayer.Active or MemoryLayer.Situational)))
            if (Widgets.ButtonText(new Rect(x - width, inner.y, width, inner.height),
                    MemoryArchiveText.Get("RimTalk_Archive_ActionSummarize")))
                ConfirmSummarize();
    }

    private void ConfirmSummarize() => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
        MemoryArchiveText.Get("RimTalk_Archive_ConfirmSummarize"), _context.SummarizeSelected));

    private void ConfirmArchive() => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
        MemoryArchiveText.Get("RimTalk_Archive_ConfirmArchive"), _context.ArchiveSelected));

    private void ConfirmDelete() => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
        MemoryArchiveText.Get("RimTalk_Archive_ConfirmDelete", _context.Selection.Count),
        _context.DeleteSelected));
}
