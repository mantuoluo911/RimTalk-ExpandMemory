using System.Collections.Generic;
using System.Linq;
using RimTalk.MemoryPatch;

namespace RimTalk.Memory;

/// <summary>
/// 详情编辑器提交给业务层的完整编辑快照。
/// UI 不直接决定共享记忆如何私有化，也不直接处理固定记忆的层级迁移。
/// </summary>
public sealed class MemoryEditCommand
{
    public string Content;
    public string Notes;
    public List<string> Tags = new();
    public float Importance;
}

/// <summary>
/// 记忆档案 UI 的业务命令接收方。
/// 集中处理创建、编辑和导入涉及的容量治理、RoundMemory 私有化和列表顺序约束。
/// </summary>
public static class MemoryArchiveCommands
{
    /// <summary>
    /// 应用编辑快照，必要时先将多人共享的 RoundMemory 替换为当前 Pawn 的私有副本。
    /// 返回值可能不是传入的 source，调用方应使用返回对象更新焦点和选择集合。
    /// </summary>
    public static MemoryEntry Edit(FourLayerMemoryComp comp, MemoryEntry source, MemoryEditCommand command)
    {
        if (source is null || command is null) return source;
        // Editing never changes ownership. Only the explicit pin action may
        // privatize a shared RoundMemory.
        MemoryEntry target = source;
        target.Content = command.Content?.Trim();
        target.Note = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        target.Tags = command.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct().ToList();
        target.Importance = command.Importance;
        target.IsUserEdited = true;
        target.Tags.Add(MemoryTags.用户编辑);
        return target;
    }

    /// <summary>
    /// 切换固定状态，并返回可能经过 RoundMemory 私有化或 ABM 迁移后的实际对象。
    /// </summary>
    public static MemoryEntry SetPinned(FourLayerMemoryComp comp, MemoryEntry source, bool isPinned)
    {
        if (source is null) return null;
        MemoryEntry target = EnsurePrivate(comp, source);
        comp.Interactor.PinMemory(target, isPinned);
        return target;
    }

    private static MemoryEntry EnsurePrivate(FourLayerMemoryComp comp, MemoryEntry source)
    {
        if (source is not RoundMemory) return source;

        // RoundMemory 可能被多个 Pawn 共同引用；直接编辑会修改所有参与者看到的内容。
        MemoryEntry privateCopy = source.Privatize();
        ReplaceReference(comp, source, privateCopy);
        return privateCopy;
    }

    private static void ReplaceReference(FourLayerMemoryComp comp, MemoryEntry source, MemoryEntry replacement)
    {
        Replace(comp.ActiveMemories, source, replacement);
        Replace(comp.SituationalMemories, source, replacement);
        Replace(comp.EventLogMemories, source, replacement);
        Replace(comp.ArchiveMemories, source, replacement);
    }

    private static void Replace(List<MemoryEntry> list, MemoryEntry source, MemoryEntry replacement)
    {
        int index = list.IndexOf(source);
        if (index >= 0) list[index] = replacement;
    }
}
