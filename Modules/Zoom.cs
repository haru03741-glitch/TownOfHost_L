using System;
using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL
{
    // https://github.com/tugaru1975/TownOfPlus/TOPmods/Zoom.cs
    // 自分のカメラだけの操作なので、TOHL 入りの客の間と本編のかくれんぼでも当てる
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class Zoom
    {
        private static bool ResetButtons = false;
        public static void Postfix()
        {
            if (GameStates.IsShip && !GameStates.IsMeeting && GameStates.IsCanMove)
            {
                if (PlayerControl.LocalPlayer.Is(CustomRoleTypes.Impostor) && Options.ImpostorOperateVisibility.GetBool()) return;

                // カメラの大きさを変えるのはホイールを回した時だけ。回していないフレームはカメラを引かない
                float scroll = TownOfHostL.Modules.RoleDictionary.IsOpen || TownOfHostL.Modules.ReportWindow.IsOpen || !Application.isFocused ? 0f : Input.mouseScrollDelta.y;
                if (scroll != 0f)
                {
                    float size = Camera.main.orthographicSize;
                    if (size > 3.0f)
                        ResetButtons = true;

                    if (scroll > 0)
                    {
                        if (size > 3.0f)
                        {
                            SetZoomSize(times: false);
                        }
                    }
                    else if (GameStates.IsDead || GameStates.IsFreePlay)
                    {
                        if (size < 18.0f)
                        {
                            SetZoomSize(times: true);
                        }
                    }
                    // 引いたままなら、戻す時にボタンの配置を直させる (大きさが変わるのはここだけ)
                    ResetButtons = Camera.main.orthographicSize > 3.0f;
                }
                Flag.NewFlag("Zoom");
            }
            else
            {
                Flag.Run(() =>
                {
                    // ホイール以外でカメラが引かれていた時も、戻す時にボタンの配置を直させる
                    if (Camera.main.orthographicSize > 3.0f) ResetButtons = true;
                    SetZoomSize(reset: true);
                }, "Zoom");
            }
        }

        static void SetZoomSize(bool times = false, bool reset = false)
        {
            var size = 1.5f;
            if (!times) size = 1 / size;
            if (reset)
            {
                Camera.main.orthographicSize = 3.0f;
                HudManager.Instance.UICamera.orthographicSize = 3.0f;
                HudManager.Instance.Chat.transform.localScale = Vector3.one;
                if (GameStates.IsMeeting) MeetingHud.Instance.transform.localScale = Vector3.one;
            }
            else
            {
                Camera.main.orthographicSize *= size;
                HudManager.Instance.UICamera.orthographicSize *= size;
            }

            if (ResetButtons)
            {
                ResolutionManager.ResolutionChanged.Invoke((float)Screen.width / Screen.height, Screen.width, Screen.height, Screen.fullScreen);
                ResetButtons = false;
            }
        }
    }
}

public static class Flag
{
    private static List<string> OneTimeList = new();
    private static List<string> FirstRunList = new();
    public static void Run(Action action, string type, bool firstrun = false)
    {
        if (OneTimeList.Contains(type) || (firstrun && !FirstRunList.Contains(type)))
        {
            if (!FirstRunList.Contains(type)) FirstRunList.Add(type);
            OneTimeList.Remove(type);
            action();
        }

    }
    public static void NewFlag(string type)
    {
        if (!OneTimeList.Contains(type)) OneTimeList.Add(type);
    }

    public static void DeleteFlag(string type)
    {
        if (OneTimeList.Contains(type)) OneTimeList.Remove(type);
    }
}
