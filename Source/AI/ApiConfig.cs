using System;
using Verse;

namespace RimTalk.Memory.AI;

public class ApiConfig : IExposable
{
    public bool IsEnabled = true;
    public AIProvider Provider = AIProvider.Player2;
    public string ApiKey;
    public string CustomUrl;
    public string CustomModelName;

    /// <summary>
    /// 获取规范化且经过去除首尾空白的最终端点 URL
    /// </summary>
    public string URL
    {
        get
        {
            if (Provider is AIProvider.Custom)
            {
                if (string.IsNullOrWhiteSpace(CustomUrl)) return string.Empty;
                string trimmed = CustomUrl.Trim();
                if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = "http://" + trimmed;
                }
                return trimmed;
            }
            return Provider.GetEndpointUrl() ?? string.Empty;
        }
    }

    /// <summary>
    /// 获取有效模型名称（若为空且为本地/自定端点，则安全回退为 default）
    /// </summary>
    public string EffectiveModelName =>
        !string.IsNullOrWhiteSpace(CustomModelName)
            ? CustomModelName.Trim()
            : (Provider is AIProvider.Custom ? "default" : string.Empty);

    public bool IsValid
    {
        get
        {
            if (!IsEnabled) return false;

            if (Provider is AIProvider.Player2)
                return true;

            string url = URL;
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return false;

            if (string.IsNullOrWhiteSpace(EffectiveModelName))
                return false;

            // 预设云端服务必须有 API Key；Custom 端点（含从 RimTalk Local 映射的端点）允许空 Key
            if (Provider is not AIProvider.Custom && string.IsNullOrWhiteSpace(ApiKey))
                return false;

            return true;
        }
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref IsEnabled, "IsEnabled", true);
        Scribe_Values.Look(ref Provider, "Provider");
        Scribe_Values.Look(ref ApiKey, "ApiKey");
        Scribe_Values.Look(ref CustomUrl, "CustomUrl");
        Scribe_Values.Look(ref CustomModelName, "CustomModelName");
    }
}
