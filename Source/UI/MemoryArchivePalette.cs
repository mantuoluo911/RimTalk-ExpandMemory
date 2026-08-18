using UnityEngine;

namespace RimTalk.Memory.UI;

/// <summary>
/// 四层记忆的统一低饱和配色。颜色主要用于节点、边框和轻微背景染色，避免压过正文。
/// </summary>
internal static class MemoryArchivePalette
{
    public static readonly Color Active = new(0.28f, 0.68f, 0.84f);
    public static readonly Color Situational = new(0.34f, 0.72f, 0.52f);
    public static readonly Color EventLog = new(0.86f, 0.64f, 0.28f);
    public static readonly Color Archive = new(0.64f, 0.42f, 0.78f);

    public static Color Layer(MemoryLayer layer) => layer switch
    {
        MemoryLayer.Active => Active,
        MemoryLayer.Situational => Situational,
        MemoryLayer.EventLog => EventLog,
        MemoryLayer.Archive => Archive,
        _ => Color.gray
    };

    public static Color Background(MemoryLayer layer)
    {
        Color color = Layer(layer);
        return new Color(color.r * 0.16f, color.g * 0.16f, color.b * 0.16f, 0.98f);
    }
}
