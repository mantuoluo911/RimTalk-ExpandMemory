using System;
using System.Collections.Generic;
using RimTalk.Memory.AI.Client;
using RimTalk.Memory.AI.Integration;
using RimTalk.MemoryPatch;

namespace RimTalk.Memory.AI;

/// <summary>
/// 客户端工厂：提供客户端构建与可用配置快照提取。
/// 无状态设计，彻底杜绝游标越界造成的死锁。
/// </summary>
public static class AIClientFactory
{
    /// <summary>
    /// 获取当前模式下应当尝试的所有有效配置候选列表。
    /// 无论跟随模式还是独立模式，首位元素始终是当前优先级最高的首选配置。
    /// </summary>
    public static List<ApiConfig> GetCurrentCandidateConfigs()
    {
        var settings = RimTalkMemoryPatchMod.Settings;
        if (settings == null) return [];

        if (settings.UseRimTalkAIConfig)
        {
            return RimTalkApiConfigGetter.GetRimTalkApiConfigs();
        }

        var list = new List<ApiConfig>();
        if (settings.ApiConfigs != null)
        {
            foreach (var cfg in settings.ApiConfigs)
            {
                if (cfg != null && cfg.IsEnabled && cfg.IsValid)
                {
                    list.Add(cfg);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// 根据配置项构建对应的客户端实例
    /// </summary>
    public static IAIClient BuildClient(ApiConfig config)
    {
        if (config == null) return null;
        return config.Provider is AIProvider.Player2
            ? new Player2Client(config)
            : new OpenAIClient(config);
    }

    /// <summary>
    /// 计算配置项的唯一指纹，用于识别配置是否发生变更
    /// </summary>
    public static string GetConfigFingerprint(ApiConfig config)
    {
        if (config == null) return "null";
        return $"{config.Provider}|{config.ApiKey}|{config.EffectiveModelName}|{config.URL}";
    }
}

