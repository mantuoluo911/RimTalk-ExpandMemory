using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimTalk.MemoryPatch;

namespace RimTalk.Memory.API
{
    /// <summary>
    /// 为 {{pawn.memory}} Mustache 变量提供内容
    /// 
    /// 当 RimTalk 解析模板时遇到 {{pawn1.memory}}，
    /// 会调用此 Provider 获取 pawn1 的记忆内容
    /// </summary>
    public static class MemoryVariableProvider
    {
        /// <summary>
        /// 获取 Pawn 的记忆内容
        /// 由 RimTalk Mustache Parser 在解析 {{pawnN.memory}} 时调用
        /// </summary>
        /// <param name="pawn">目标 Pawn（由 RimTalk 传入）</param>
        /// <returns>格式化的记忆文本</returns>
        public static string GetPawnMemory(Pawn pawn)
        {
            if (pawn == null) 
            {
                return "";
            }
            
            try
            {
                var settings = RimTalkMemoryPatchMod.Settings;
                
                // 优先使用四层记忆系统
                var fourLayerComp = pawn.TryGetComp<FourLayerMemoryComp>();
                if (fourLayerComp != null)
                {
                    return GetFourLayerMemories(pawn, fourLayerComp, settings);
                }
                
                // 回退到旧的记忆组件
                var memoryComp = pawn.TryGetComp<PawnMemoryComp>();
                if (memoryComp != null)
                {
                    return GetLegacyMemories(memoryComp, settings);
                }
                
                return "(No memory component)";
            }
            catch (Exception ex)
            {
                Log.Warning($"[MemoryPatch] Error getting pawn memory for {pawn?.LabelShort}: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// ⭐ v4.2: 缓存每个 Pawn 的记忆结果，避免重复计算
        /// Key: Pawn.ThingID, Value: 记忆文本
        /// </summary>
        [ThreadStatic]
        private static Dictionary<string, string> _pawnMemoryCache;
        
        /// <summary>
        /// ⭐ v4.2: 上次缓存的时间戳
        /// </summary>
        [ThreadStatic]
        private static int _memoryCacheTick;
        
        /// <summary>
        /// ⭐ v4.2: 缓存有效期（2秒 = 120 ticks）
        /// </summary>
        private const int MEMORY_CACHE_EXPIRE_TICKS = 120;
        
        /// <summary>
        /// ⭐ v4.2: 获取四层记忆系统的记忆
        /// 结构：ABM（最近记忆）+ ELS/CLPA（总结后的记忆）
        /// 格式统一：序号. [类型] 内容 (时间)
        /// </summary>
        private static string GetFourLayerMemories(Pawn pawn, FourLayerMemoryComp comp, RimTalkMemoryPatchSettings settings)
        {
            string pawnId = pawn.ThingID;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            
            // ⭐ v4.2: 检查缓存是否有效
            if (_pawnMemoryCache == null || currentTick - _memoryCacheTick > MEMORY_CACHE_EXPIRE_TICKS)
            {
                _pawnMemoryCache = new Dictionary<string, string>();
                _memoryCacheTick = currentTick;
            }
            
            // ⭐ v4.2: 如果缓存中有这个 Pawn 的结果，直接返回
            if (_pawnMemoryCache.TryGetValue(pawnId, out string cachedResult))
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[Memory] Using cached result for {pawn.LabelShort}");
                }
                return cachedResult;
            }
            
            var sb = new StringBuilder();
            
            // ⭐ v4.2: 第一部分 - ABM（最近记忆，支持跨 Pawn 去重）
            string abmContent = RoundMemoryManager.InjectABM(pawn);
            
            if (!string.IsNullOrEmpty(abmContent))
            {
                sb.AppendLine(abmContent);
            }
            
            // ⭐ v4.0: 第二部分 - ELS/CLPA（总结后的记忆，通过关键词匹配）
            string dialogueContext = GetCurrentDialogueContext();
            string elsMemories = DynamicMemoryInjection.InjectMemories(
                pawn,
                dialogueContext,
                settings.maxInjectedMemories
            );
            
            if (!string.IsNullOrEmpty(elsMemories))
            {
                // 如果 ABM 有内容，加空行分隔
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.AppendLine(elsMemories);
            }
            
            // 如果都为空，返回最近记忆
            string result;
            if (sb.Length == 0)
            {
                result = GetRecentMemories(comp, settings.maxInjectedMemories);
            }
            else
            {
                result = sb.ToString().TrimEnd();
            }
            
            // ⭐ v4.2: 缓存结果
            _pawnMemoryCache[pawnId] = result;
            
            return result;
        }
        
        /// <summary>
        /// 获取最近的记忆（无匹配时的回退）
        /// </summary>
        private static string GetRecentMemories(FourLayerMemoryComp comp, int maxCount)
        {
            var recentMemories = new List<MemoryEntry>();
            
            // 从各层收集最近的记忆
            recentMemories.AddRange(comp.SituationalMemories.Take(maxCount / 2));
            recentMemories.AddRange(comp.EventLogMemories.Take(maxCount / 2));
            
            if (recentMemories.Count == 0)
            {
                return "(No memories yet)";
            }
            
            // 按时间排序
            var sortedMemories = recentMemories
                .OrderByDescending(m => m.timestamp)
                .Take(maxCount);
            
            return FormatMemories(sortedMemories);
        }
        
        /// <summary>
        /// 获取旧版记忆组件的记忆
        /// </summary>
        private static string GetLegacyMemories(PawnMemoryComp comp, RimTalkMemoryPatchSettings settings)
        {
            var memories = comp.GetRelevantMemories(settings.maxInjectedMemories);
            
            if (memories == null || memories.Count == 0)
            {
                return "(No memories yet)";
            }
            
            var sb = new StringBuilder();
            int index = 1;
            
            foreach (var memory in memories)
            {
                sb.AppendLine($"{index}. {memory.content} ({memory.TimeAgoString})");
                index++;
            }
            
            return sb.ToString().TrimEnd();
        }
        
        /// <summary>
        /// 格式化记忆列表
        /// </summary>
        private static string FormatMemories(IEnumerable<MemoryEntry> memories)
        {
            var sb = new StringBuilder();
            int index = 1;
            
            foreach (var memory in memories)
            {
                string typeTag = GetMemoryTypeTag(memory.type);
                sb.AppendLine($"{index}. [{typeTag}] {memory.content} ({memory.TimeAgoString})");
                index++;
            }
            
            return sb.ToString().TrimEnd();
        }
        
        /// <summary>
        /// 获取记忆类型标签
        /// </summary>
        private static string GetMemoryTypeTag(MemoryType type)
        {
            switch (type)
            {
                case MemoryType.Conversation:
                    return "Conversation";
                case MemoryType.Action:
                    return "Action";
                case MemoryType.Observation:
                    return "Observation";
                case MemoryType.Event:
                    return "Event";
                case MemoryType.Emotion:
                    return "Emotion";
                case MemoryType.Relationship:
                    return "Relationship";
                default:
                    return "Memory";
            }
        }
        
        /// <summary>
        /// 获取当前对话上下文（用于关键词匹配）
        /// 从 RimTalkMemoryAPI 获取缓存的上下文
        /// </summary>
        private static string GetCurrentDialogueContext()
        {
            try
            {
                // 从 RimTalkMemoryAPI 获取缓存的上下文
                var context = Patches.RimTalkMemoryAPI.GetLastRimTalkContext(out _, out int tick);
                
                // 检查缓存是否过期（60 ticks 内有效）
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (currentTick - tick > 60)
                {
                    return "";
                }
                
                return context ?? "";
            }
            catch
            {
                return "";
            }
        }
        
        // 注意：固定记忆(isPinned)不需要单独处理
        // DynamicMemoryInjection 已经给 isPinned 的记忆加了 0.5 的评分加成
        // 它们会自然地排在 {{memory}} 输出的前面
        
        /// <summary>
        /// 获取 Pawn 的 ABM 层记忆（超短期记忆）
        /// 由 RimTalk Mustache Parser 在解析 {{pawnN.ABM}} 时调用
        /// </summary>
        public static string GetPawnABM(Pawn pawn)
        {
            if (pawn == null) return "";
            
            try
            {
                // 直接复用现有的 RoundMemoryManager.InjectABM
                string abmContent = RoundMemoryManager.InjectABM(pawn);
                
                if (string.IsNullOrEmpty(abmContent))
                {
                    return "(No ABM memories)";
                }
                
                return abmContent;
            }
            catch (Exception ex)
            {
                Log.Warning($"[MemoryPatch] Error getting ABM for {pawn?.LabelShort}: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 获取 Pawn 的 ELS 层记忆（中期记忆 - Event Log Summary）
        /// 由 RimTalk Mustache Parser 在解析 {{pawnN.ELS}} 时调用
        /// </summary>
        public static string GetPawnELS(Pawn pawn)
        {
            if (pawn == null) return "";
            
            try
            {
                var comp = pawn.TryGetComp<FourLayerMemoryComp>();
                if (comp == null || comp.EventLogMemories == null || comp.EventLogMemories.Count == 0)
                {
                    return "(No ELS memories)";
                }
                
                return FormatMemoryList(comp.EventLogMemories, MemoryLayer.EventLog);
            }
            catch (Exception ex)
            {
                Log.Warning($"[MemoryPatch] Error getting ELS for {pawn?.LabelShort}: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 获取 Pawn 的 CLPA 层记忆（长期记忆 - Colony Lore & Persona Archive）
        /// 由 RimTalk Mustache Parser 在解析 {{pawnN.CLPA}} 时调用
        /// </summary>
        public static string GetPawnCLPA(Pawn pawn)
        {
            if (pawn == null) return "";
            
            try
            {
                var comp = pawn.TryGetComp<FourLayerMemoryComp>();
                if (comp == null || comp.ArchiveMemories == null || comp.ArchiveMemories.Count == 0)
                {
                    return "(No CLPA memories)";
                }
                
                return FormatMemoryList(comp.ArchiveMemories, MemoryLayer.Archive);
            }
            catch (Exception ex)
            {
                Log.Warning($"[MemoryPatch] Error getting CLPA for {pawn?.LabelShort}: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 格式化记忆列表（用于 ELS/CLPA 输出）
        /// 格式: "序号. [类型] 内容 (游戏日期)"
        /// </summary>
        private static string FormatMemoryList(List<MemoryEntry> memories, MemoryLayer layer)
        {
            if (memories == null || memories.Count == 0)
            {
                return "";
            }
            
            var settings = RimTalkMemoryPatchMod.Settings;
            int maxCount = settings?.maxInjectedMemories ?? 10;
            
            var sb = new StringBuilder();
            int index = 1;
            
            // 按时间降序排序（最新的在前）
            var sortedMemories = memories
                .OrderByDescending(m => m.timestamp)
                .Take(maxCount);
            
            foreach (var memory in sortedMemories)
            {
                string typeTag = GetMemoryTypeTag(memory.type);
                // ELS/CLPA 使用游戏日期时间戳
                string timeStr = memory.GameDateString;
                sb.AppendLine($"{index}. [{typeTag}] {memory.content} ({timeStr})");
                index++;
            }
            
            return sb.ToString().TrimEnd();
        }
        
        /// <summary>
        /// 获取 Pawn 的 ELS 层匹配记忆（经过上下文匹配）
        /// 由 RimTalk Mustache Parser 在解析 {{pawnN.matchELS}} 时调用
        /// 使用 DynamicMemoryInjection 的匹配逻辑，只返回 ELS 层
        /// </summary>
        public static string GetPawnMatchELS(Pawn pawn)
        {
            if (pawn == null) return "";
            
            try
            {
                var comp = pawn.TryGetComp<FourLayerMemoryComp>();
                if (comp == null || comp.EventLogMemories == null || comp.EventLogMemories.Count == 0)
                {
                    return "(No ELS memories)";
                }
                
                var settings = RimTalkMemoryPatchMod.Settings;
                string dialogueContext = GetCurrentDialogueContext();
                
                // 使用匹配逻辑获取 ELS 记忆
                string result = GetMatchedMemoriesForLayer(
                    pawn,
                    comp,
                    comp.EventLogMemories,
                    MemoryLayer.EventLog,
                    dialogueContext,
                    settings?.maxInjectedMemories ?? 10
                );
                
                if (string.IsNullOrEmpty(result))
                {
                    return "(No matched ELS memories)";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[MemoryPatch] Error getting matchELS for {pawn?.LabelShort}: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 获取 Pawn 的 CLPA 层匹配记忆（经过上下文匹配）
        /// 由 RimTalk Mustache Parser 在解析 {{pawnN.matchCLPA}} 时调用
        /// 使用 DynamicMemoryInjection 的匹配逻辑，只返回 CLPA 层
        /// </summary>
        public static string GetPawnMatchCLPA(Pawn pawn)
        {
            if (pawn == null) return "";
            
            try
            {
                var comp = pawn.TryGetComp<FourLayerMemoryComp>();
                if (comp == null || comp.ArchiveMemories == null || comp.ArchiveMemories.Count == 0)
                {
                    return "(No CLPA memories)";
                }
                
                var settings = RimTalkMemoryPatchMod.Settings;
                string dialogueContext = GetCurrentDialogueContext();
                
                // 使用匹配逻辑获取 CLPA 记忆
                string result = GetMatchedMemoriesForLayer(
                    pawn,
                    comp,
                    comp.ArchiveMemories,
                    MemoryLayer.Archive,
                    dialogueContext,
                    settings?.maxInjectedMemories ?? 10
                );
                
                if (string.IsNullOrEmpty(result))
                {
                    return "(No matched CLPA memories)";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[MemoryPatch] Error getting matchCLPA for {pawn?.LabelShort}: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 获取特定层级的匹配记忆
        /// 复用 DynamicMemoryInjection 的评分逻辑，但只针对指定层级
        /// </summary>
        private static string GetMatchedMemoriesForLayer(
            Pawn pawn,
            FourLayerMemoryComp comp,
            List<MemoryEntry> memories,
            MemoryLayer layer,
            string context,
            int maxCount)
        {
            if (memories == null || memories.Count == 0)
            {
                return null;
            }
            
            // 使用 DynamicMemoryInjection 的匹配逻辑
            string result = DynamicMemoryInjection.InjectMemoriesWithDetails(
                comp,
                context,
                maxCount,
                out var scores
            );
            
            // 从结果中过滤只保留指定层级的记忆
            if (scores == null || scores.Count == 0)
            {
                return null;
            }
            
            var layerScores = scores.Where(s => s.Memory.layer == layer).ToList();
            
            if (layerScores.Count == 0)
            {
                return null;
            }
            
            // 格式化输出
            var sb = new StringBuilder();
            int index = 1;
            
            foreach (var score in layerScores.OrderByDescending(s => s.TotalScore).Take(maxCount))
            {
                var memory = score.Memory;
                string typeTag = GetMemoryTypeTag(memory.type);
                string timeStr = memory.GameDateString;
                sb.AppendLine($"{index}. [{typeTag}] {memory.content} ({timeStr})");
                index++;
            }
            
            return sb.ToString().TrimEnd();
        }
    }
}