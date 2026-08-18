using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// 导入导出的文件系统适配层。
/// 只负责 Scribe 文件读写和文件夹操作；身份重建、容量检查等业务规则交给 MemoryArchiveCommands。
/// </summary>
internal static class MemoryArchiveTransferService
{
    // 静态入口
    public static void ExportMemories(FourLayerMemoryComp memoryComp)
    {
        if (memoryComp?.parent is not Pawn pawn) return;
        try
        {
            string path = Export(pawn, memoryComp);
            Messages.Message(MemoryArchiveText.Get("RimTalk_Archive_Exported", path), MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception exception)
        {
            Log.Error($"[RimTalk.Memory.UI] Export failed: {exception}");
            Messages.Message(MemoryArchiveText.Get("RimTalk_Archive_ExportFailed", exception.Message), MessageTypeDefOf.RejectInput, false);
        }
    }

    public static void OpenImportMenu(FourLayerMemoryComp memoryComp)
    {
        if (memoryComp is null) return;
        List<FloatMenuOption> options = new() { new FloatMenuOption(MemoryArchiveText.Get("RimTalk_Archive_OpenFolder"), OpenFolder) };
        foreach (string path in GetImportFiles())
        {
            string captured = path;
            options.Add(new FloatMenuOption(Path.GetFileName(path), () => ConfirmImport(captured, memoryComp)));
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static void ConfirmImport(string path, FourLayerMemoryComp memoryComp)
    {
        if (memoryComp?.parent is not Pawn pawn) return;
        try
        {
            (string pawnName, List<MemoryEntry> memories) = Read(path);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(MemoryArchiveText.Get("RimTalk_Archive_ConfirmImport", pawnName, memories.Count, pawn.LabelShort), () =>
            {
                int imported = Import(memoryComp, memories);
                Messages.Message(MemoryArchiveText.Get("RimTalk_Archive_Imported", imported, memories.Count), MessageTypeDefOf.TaskCompletion, false);
            }));
        }
        catch (Exception exception)
        {
            Log.Error($"[RimTalk.Memory.UI] Import failed: {exception}");
            Messages.Message(MemoryArchiveText.Get("RimTalk_Archive_ImportFailed", exception.Message), MessageTypeDefOf.RejectInput, false);
        }
    }

    /// <summary>
    /// 将当前 Pawn 的四层记忆导出到 SaveData/MemoryExports。
    /// </summary>
    public static string Export(Pawn pawn, FourLayerMemoryComp comp)
    {
        string folder = ExportFolder;
        Directory.CreateDirectory(folder);
        string fileName = $"{pawn.Name.ToStringShort}_Memories_{Find.TickManager.TicksGame}.xml";
        string path = Path.Combine(folder, fileName);
        string pawnId = pawn.ThingID;
        string pawnName = pawn.Name.ToStringShort;
        List<MemoryEntry> memories = comp.ActiveMemories.Concat(comp.SituationalMemories)
            .Concat(comp.EventLogMemories).Concat(comp.ArchiveMemories).Where(memory => memory is not null).ToList();
        Scribe.saver.InitSaving(path, "MemoryExport");
        Scribe_Values.Look(ref pawnId, "pawnId");
        Scribe_Values.Look(ref pawnName, "pawnName");
        Scribe_Collections.Look(ref memories, "memories", LookMode.Deep);
        Scribe.saver.FinalizeSaving();
        return path;
    }

    /// <summary>
    /// 获取可导入 XML，按最后修改时间从新到旧排列。
    /// </summary>
    public static List<string> GetImportFiles() => Directory.Exists(ExportFolder)
        ? Directory.GetFiles(ExportFolder, "*.xml").OrderByDescending(File.GetLastWriteTime).ToList()
        : new List<string>();

    /// <summary>
    /// 读取导出文件，但不立即写入目标组件，以便 UI 先向玩家确认。
    /// </summary>
    public static (string PawnName, List<MemoryEntry> Memories) Read(string path)
    {
        string pawnId = string.Empty;
        string pawnName = string.Empty;
        List<MemoryEntry> memories = new();
        Scribe.loader.InitLoading(path);
        Scribe_Values.Look(ref pawnId, "pawnId");
        Scribe_Values.Look(ref pawnName, "pawnName");
        Scribe_Collections.Look(ref memories, "memories", LookMode.Deep);
        Scribe.loader.FinalizeLoading();
        return (pawnName, memories ?? new List<MemoryEntry>());
    }

    /// <summary>
    /// 将已读取的条目作为命令提交给业务层，并返回实际导入数量。
    /// </summary>
    public static int Import(FourLayerMemoryComp comp, IEnumerable<MemoryEntry> memories)
        => MemoryArchiveCommands.Import(comp, memories);

    public static void OpenFolder()
    {
        Directory.CreateDirectory(ExportFolder);
        Process.Start(ExportFolder);
    }

    private static string ExportFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, "MemoryExports");
}
