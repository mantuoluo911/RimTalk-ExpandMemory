using System;
using System.Collections.Generic;
using System.Linq;

namespace RimTalk.Memory.AI.Integration;

/// <summary>
/// 负责将 RimTalk 的当前配置精准无缝映射为记忆拓展可用的 ApiConfig。
/// 全面覆盖 Simple 模式（Google / Player2）、Cloud 多配置轮换模式与 Local 本地推理模式。
/// </summary>
public static class RimTalkApiConfigGetter
{
    /// <summary>
    /// 获取 RimTalk 当前真正处于激活生效状态的那一个配置
    /// 与 RimTalk 本体的 GetActiveConfig 逻辑严格对齐
    /// </summary>
    public static ApiConfig GetActiveConfig()
    {
        if (Settings.Get() is not { } settings)
            return null;

        var active = settings.GetActiveConfig();
        return active != null ? Convert(active) : null;
    }

    /// <summary>
    /// 获取当前可用的全部 RimTalk 配置列表。
    /// 首位永远是当前激活的配置，后续元素按轮换顺序排列作为 Fallback。
    /// </summary>
    public static List<ApiConfig> GetRimTalkApiConfigs()
    {
        if (Settings.Get() is not { } settings)
            return [];

        // 1. 简单配置模式：直接对齐 GetActiveConfig
        if (settings.UseSimpleConfig)
        {
            var active = settings.GetActiveConfig();
            return active != null ? [Convert(active)] : [];
        }

        // 2. 本地模式：只使用 LocalConfig
        if (!settings.UseCloudProviders)
        {
            return settings.LocalConfig is { } localConfig && localConfig.IsValid()
                ? [Convert(localConfig)]
                : [];
        }

        // 3. 云端多配置列表：以当前激活索引为起点进行轮换重排
        if (settings.CloudConfigs is not { Count: > 0 } configs)
            return [];

        int total = configs.Count;
        int startIndex = Math.Max(0, settings.CurrentCloudConfigIndex % total);

        var result = new List<ApiConfig>(total);
        for (int i = 0; i < total; i++)
        {
            var c = configs[(startIndex + i) % total];
            if (c != null && c.IsValid())
            {
                result.Add(Convert(c));
            }
        }

        return result;
    }

    /// <summary>
    /// 将 RimTalk 的 ApiConfig 转换为记忆拓展的 ApiConfig
    /// </summary>
    private static ApiConfig Convert(global::RimTalk.ApiConfig rimTalkConfig)
    {
        var provider = rimTalkConfig.Provider.Convert();
        string model = rimTalkConfig.GetEffectiveModelName();

        // 兜底处理：避免某些情况下返回 null 或空串导致 IsValid 误判
        if (string.IsNullOrWhiteSpace(model))
        {
            model = provider switch
            {
                AIProvider.Google => global::RimTalk.Data.Constant.DefaultCloudModel,
                AIProvider.Player2 => "Default",
                _ => "default"
            };
        }

        return new ApiConfig
        {
            IsEnabled = rimTalkConfig.IsEnabled,
            Provider = provider,
            ApiKey = rimTalkConfig.ApiKey,
            // 对齐 RimTalk 语义：BaseUrl 仅对 Custom/Local 生效，预设 provider 一律走注册表端点
            CustomUrl = rimTalkConfig.Provider is global::RimTalk.AIProvider.Custom or global::RimTalk.AIProvider.Local
                ? rimTalkConfig.BaseUrl
                : null,
            CustomModelName = model
        };
    }

    private static AIProvider Convert(this global::RimTalk.AIProvider provider) => provider switch
    {
        global::RimTalk.AIProvider.Google => AIProvider.Google,
        global::RimTalk.AIProvider.OpenAI => AIProvider.OpenAI,
        global::RimTalk.AIProvider.DeepSeek => AIProvider.DeepSeek,
        global::RimTalk.AIProvider.Grok => AIProvider.Grok,
        global::RimTalk.AIProvider.GLM => AIProvider.GLM,
        global::RimTalk.AIProvider.GLMCoding => AIProvider.GLMCoding,
        global::RimTalk.AIProvider.AlibabaIntl => AIProvider.AlibabaIntl,
        global::RimTalk.AIProvider.AlibabaCN => AIProvider.AlibabaCN,
        global::RimTalk.AIProvider.OpenRouter => AIProvider.OpenRouter,
        global::RimTalk.AIProvider.Claude => AIProvider.Claude,
        global::RimTalk.AIProvider.Moonshot => AIProvider.Moonshot,
        global::RimTalk.AIProvider.Player2 => AIProvider.Player2,
        // Local 与 Custom 都通过 BaseUrl 直连，在记忆拓展统一走兼容的 Custom 客户端处理
        _ => AIProvider.Custom
    };
}

