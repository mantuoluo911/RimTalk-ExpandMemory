using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace RimTalk.Memory.Utils;

internal static class XmlTextSanitizer
{
    // XML 1.0 cannot represent some control characters or unpaired surrogates.
    // Keep word boundaries when replacing them, and preserve valid Unicode pairs.
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        StringBuilder sanitized = null;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (char.IsHighSurrogate(character) && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1]))
            {
                sanitized?.Append(character).Append(text[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(character))
            {
                sanitized?.Append(character);
            }
            else
            {
                sanitized ??= new StringBuilder(text.Length).Append(text, 0, i);
                sanitized.Append(' ');
            }
        }

        return sanitized?.ToString() ?? text;
    }

    public static void SanitizeInPlace(List<string> values)
    {
        if (values == null) return;
        for (int i = 0; i < values.Count; i++)
            values[i] = Sanitize(values[i]);
    }
}
