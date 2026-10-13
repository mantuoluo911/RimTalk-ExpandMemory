using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Memory.Utils;
using Verse;

namespace RimTalk.Memory.AI.Client;

/// <summary>
/// 客户端调度池：负责按优先级调度与轮换 AI 客户端，提供瞬态容错与基于指纹的即时配置同步。
/// 彻底解决配置跟随模式下由于游标累加造成的永久死锁问题。
/// </summary>
public class ClientPool : IAIClient
{
    // 基于配置指纹缓存的 Client 实例，保证配置未变时高效复用，配置变更时自动热重建
    private readonly Dictionary<string, IAIClient> _clientCache = new();
    private readonly object _lock = new();

    /// <summary>
    /// 获取 AI 响应。每次请求均动态获取最新候选配置链，支持动态轮换与故障转移。
    /// </summary>
    public async Task<Payload> GetChatCompletionAsync(string prompt)
    {
        var candidates = AIClientFactory.GetCurrentCandidateConfigs();
        if (candidates.Count == 0)
        {
            Log.Warning("[RimTalk.Memory.AI.Client] 没有检测到任何可用的 AI 配置（跟随 RimTalk 或独立配置均未就绪）。");
            return new Payload
            {
                URL = "None",
                Model = "None",
                Request = prompt,
                ErrorMessage = "No available AI configuration found."
            };
        }

        Payload lastResult = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            var config = candidates[i];
            var client = GetOrCreateClient(config);
            if (client == null) continue;

            try
            {
                var result = await client.GetChatCompletionAsync(prompt);
                if (result?.IsValid ?? false)
                {
                    return result;
                }

                lastResult = result;
                Log.Warning($"[RimTalk.Memory.AI.Client] 配置 [{i + 1}/{candidates.Count}] ({config.Provider}/{config.EffectiveModelName}) 响应失败。");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.Memory.AI.Client] 配置 [{i + 1}/{candidates.Count}] ({config.Provider}/{config.EffectiveModelName}) 抛出异常: {ex.Message}");
                lastResult = new Payload
                {
                    URL = config.URL,
                    Model = config.EffectiveModelName,
                    Request = prompt,
                    ErrorMessage = ex.Message
                };
            }
        }

        Log.Error($"[RimTalk.Memory.AI.Client] 所有已尝试的 {candidates.Count} 个 AI 配置均执行失败。");
        return lastResult;
    }

    /// <summary>
    /// 验证当前配置的有效性。只要有一个候选配置通过，即判定为有效。
    /// </summary>
    public async Task<bool> ValidateAsync()
    {
        var candidates = AIClientFactory.GetCurrentCandidateConfigs();
        if (candidates.Count == 0) return false;

        for (int i = 0; i < candidates.Count; i++)
        {
            var config = candidates[i];
            var client = GetOrCreateClient(config);
            if (client == null) continue;

            try
            {
                if (await client.ValidateAsync())
                    return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.Memory.AI.Client] 验证配置 [{i + 1}/{candidates.Count}] 时发生异常: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 重置客户端缓存，释放旧连接
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _clientCache.Clear();
        }
    }

    private IAIClient GetOrCreateClient(ApiConfig config)
    {
        string fingerprint = AIClientFactory.GetConfigFingerprint(config);
        lock (_lock)
        {
            if (_clientCache.TryGetValue(fingerprint, out var cached) && cached != null)
                return cached;

            var client = AIClientFactory.BuildClient(config);
            if (client != null)
            {
                _clientCache[fingerprint] = client;
            }
            return client;
        }
    }
}

