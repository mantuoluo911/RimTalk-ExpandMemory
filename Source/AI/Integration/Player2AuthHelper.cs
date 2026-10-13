using System;
using System.Collections.Generic;
using RimTalk.MemoryPatch;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.AI.Integration;

/// <summary>
/// 对接 RimTalk 本体 Player2AuthService 的辅助类，
/// 提供统一的网页授权状态感知、授权流启动、重开授权页与取消操作。
/// </summary>
public static class Player2AuthHelper
{
    private const string FallbackPlayer2Website = "https://player2.game";

    public static bool IsAuthenticating
    {
        get
        {
            try
            {
                return global::RimTalk.Client.Player2.Player2AuthService.IsAuthenticating;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string ApprovalUrl
    {
        get
        {
            try
            {
                return global::RimTalk.Client.Player2.Player2AuthService.ApprovalUrl;
            }
            catch
            {
                return null;
            }
        }
    }

    public static void StartAuth(Action<string> onKeyReceived = null)
    {
        try
        {
            global::RimTalk.Client.Player2.Player2AuthService.StartAuth(key =>
            {
                onKeyReceived?.Invoke(key);
            });
        }
        catch (Exception ex)
        {
            Log.Warning($"[RimTalk.Memory.AI] 调用 RimTalk Player2AuthService 失败: {ex.Message}，回退至打开官网。");
            Application.OpenURL(FallbackPlayer2Website);
        }
    }

    public static void Cancel()
    {
        try
        {
            global::RimTalk.Client.Player2.Player2AuthService.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warning($"[RimTalk.Memory.AI] 取消 Player2 授权失败: {ex.Message}");
        }
    }

    public static void ReopenApprovalPage()
    {
        try
        {
            string url = ApprovalUrl;
            if (!string.IsNullOrEmpty(url))
            {
                Application.OpenURL(url);
                return;
            }
        }
        catch
        {
            // ignore
        }
        Application.OpenURL(FallbackPlayer2Website);
    }

    public static void OpenWebsite()
    {
        Application.OpenURL(FallbackPlayer2Website);
    }
}
