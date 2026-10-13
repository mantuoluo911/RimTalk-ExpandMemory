using HarmonyLib;

namespace RimTalk.Memory.Patches
{
    // RimTalk 本体的 API 设置保存后重置记忆拓展的 client 池。
    [HarmonyPatch(typeof(global::RimTalk.Settings), nameof(global::RimTalk.Settings.WriteSettings))]
    public static class RimTalk_Settings_WriteSettings_Patch
    {
        [HarmonyPostfix]
        static void Postfix() => AI.AIService.ResetClientPool();
    }

    // RimTalk 内部发生多配置轮换（TryNextConfig）后，同步重置记忆拓展的 client 池。
    [HarmonyPatch(typeof(global::RimTalk.RimTalkSettings), nameof(global::RimTalk.RimTalkSettings.TryNextConfig))]
    public static class RimTalk_Settings_TryNextConfig_Patch
    {
        [HarmonyPostfix]
        static void Postfix() => AI.AIService.ResetClientPool();
    }
}

