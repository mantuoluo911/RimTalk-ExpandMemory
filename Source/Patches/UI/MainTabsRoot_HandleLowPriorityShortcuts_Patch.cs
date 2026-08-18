using HarmonyLib;
using RimWorld;
using RimTalk.Memory.UI;
using UnityEngine;
using Verse;

namespace RimTalk.MemoryPatch;

/// <summary>
/// 原版会在地图鼠标按下时关闭所有非 Inspect 主标签并清空选择。
/// 记忆档案需要保留窗口，让 Selector 完成 Pawn 选择后由窗口切换所有者。
/// </summary>
[HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.HandleLowPriorityShortcuts))]
internal static class MainTabsRoot_HandleLowPriorityShortcuts_Patch
{
    private static bool Prefix()
    {
        bool memoryArchiveOpen = Find.MainTabsRoot.OpenTab?.TabWindow is MemoryTabWindow;
        bool leftMouseDown = Event.current.type is EventType.MouseDown && Event.current.button == 0;
        return !memoryArchiveOpen || !leftMouseDown;
    }
}
