using System.Globalization;
using Verse;

namespace RimTalk.Memory.UI;

/// <summary>
/// 新记忆 UI 的翻译入口，并统一内部层级/类型到玩家可见名称的映射。
/// </summary>
public static class MemoryArchiveText
{
    public static string Get(string key) => key?.Translate() ?? string.Empty;
    public static string Get(string key, params object[] args) =>
        key is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, key.Translate(), args);

    public static string Layer(MemoryLayer? layer) => layer switch
    {
        null => Get("RimTalk.Memory.UI.All_Layers"),
        _ => Layer((MemoryLayer)layer)
    };
    public static string Layer(MemoryLayer layer) => Get(layer switch
    {
        MemoryLayer.Active => "RimTalk_Archive_LayerRecent",
        MemoryLayer.Situational => "RimTalk_Archive_LayerShort",
        MemoryLayer.EventLog => "RimTalk_Archive_LayerSummary",
        MemoryLayer.Archive => "RimTalk_Archive_LayerChapter",
        _ => "RimTalk_Archive_Memory"
    });

    public static string Type(MemoryType? type) => type switch
    {
        null => Get("RimTalk.Memory.UI.All_Types"),
        _ => Type((MemoryType)type)
    };
    public static string Type(MemoryType type) => Get(type switch
    {
        MemoryType.Conversation => "RimTalk_MindStream_Conversation",
        MemoryType.Action => "RimTalk_MindStream_Action",
        MemoryType.Summarization => "RimTalk_Archive_TypeSummary",
        MemoryType.Event => "RimTalk_Archive_TypeEvent",
        MemoryType.Emotion => "RimTalk_Archive_TypeEmotion",
        MemoryType.Relationship => "RimTalk_Archive_TypeRelationship",
        _ => "RimTalk_Archive_Memory"
    });
}
