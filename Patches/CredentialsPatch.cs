using System;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Templates;
using static TownOfHostL.Translator;
using TownOfHostL.Roles.Crewmate;

using TownOfHostL.Core.Modes;

namespace TownOfHostL
{
    [HarmonyPatch]
    public static class CredentialsPatch
    {
        public static SpriteRenderer TohLogo { get; private set; }

        // 自分の画面に描くだけなので、本編のかくれんぼで休んでいる間も出す
        [PatchBundle(PatchBundles.Core)]
        [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
        class PingTrackerUpdatePatch
        {
            static StringBuilder sb = new();
            /// <summary>ロビーの警告を宣言した役職。役職の登録は起動時に揃うので、最初に使う時に 1 回だけ組む</summary>
            static SimpleRoleInfo[] rolesWithLobbyWarning;
            static PingTracker aspectOwner;
            static AspectPosition aspect;
            static bool aspectSaved;
            static AspectPosition.EdgeAlignments baseAlign;
            static void Postfix(PingTracker __instance)
            {
                bool inMenu = !AmongUsClient.Instance || AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.NotJoined;
                __instance.text.alignment = inMenu ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.TopRight;

                // 位置調整
                // 毎フレーム通るので、部品は PingTracker が替わった時だけ引き直す
                if (aspectOwner != __instance) { aspectOwner = __instance; aspect = __instance.GetComponent<AspectPosition>(); nextBuildAt = 0f; }
                if (aspect)
                {
                    if (!aspectSaved) { aspectSaved = true; baseAlign = aspect.Alignment; }
                    var align = inMenu ? AspectPosition.EdgeAlignments.RightBottom : baseAlign;
                    var dist = inMenu ? new Vector3(0.35f, 0.12f, 0f) : new Vector3(1.8f, 6.0f, 0f);
                    if (aspect.Alignment != align || aspect.DistanceFromEdge != dist)
                    {
                        aspect.Alignment = align;
                        aspect.DistanceFromEdge = dist;
                        aspect.AdjustPosition();
                    }
                    // 右上の報告・辞典のボタンに字が重なる時は、字をボタンの下へずらす
                    if (!inMenu) AvoidAddedButtons(__instance);
                }

                // 本編は毎フレーム Ping の字を書き直すので、足す字も毎フレーム付ける。足す字の中身は設定や状態が変わった時にしか変わらないので、組み直すのは 0.25 秒ごと
                float now = Time.unscaledTime;
                if (now >= nextBuildAt)
                {
                    nextBuildAt = now + 0.25f;
                    suffix = BuildSuffix();
                }
                __instance.text.text += suffix;
            }
            static bool shiftedForButtons;
            static void AvoidAddedButtons(PingTracker ping)
            {
                try
                {
                    bool has = TownOfHostL.Modules.RoleDictionaryHudButton.TryGetAddedButtonsArea(out var left, out var bottom);
                    if (!has && !shiftedForButtons) return;
                    var t = ping.transform;
                    // TMP_Text.renderer は IL2CPP では古い Component.renderer に当たって例外になるので、部品から直接取る
                    var mr = ping.text.GetComponent<MeshRenderer>();
                    if (!mr) return;
                    var rb = mr.bounds;
                    // 字の右上の角の、字の位置からのずれ (字の中身が変わっても位置と一緒に動くので、毎フレーム測り直す)
                    float topOff = rb.max.y - t.position.y, rightOff = rb.max.x - t.position.x;
                    // 本来の位置へ戻してから決める (ずらしを積み重ねない)
                    aspect.AdjustPosition();
                    shiftedForButtons = false;
                    if (!has) return;
                    var p = t.position;
                    if (p.x + rightOff <= left || p.y + topOff <= bottom) return;
                    t.position = new Vector3(p.x, bottom - 0.05f - topOff, p.z);
                    shiftedForButtons = true;
                }
                catch (System.Exception ex)
                {
                    if (!avoidWarned) { avoidWarned = true; Logger.Warn($"右上の字をボタンからよけられない: {ex.Message}", "Credentials"); }
                }
            }
            static bool avoidWarned;
            static string suffix = "";
            static float nextBuildAt;
            static string BuildSuffix()
            {
                sb.Clear();

                sb.Append("\r\n").Append(Main.credentialsText);

                // 客の間は自分の設定が部屋に効かないので、設定から出す表示は出さない
                bool asClient = TownOfHostL.Core.VanillaHideAndSeek.IsClientMode;
                if (!asClient && Options.NoGameEnd.GetBool()) sb.Append($"\r\n").Append(Utils.ColorString(Color.red, GetString("NoGameEnd")));
                if (!asClient && ModeCatalog.Current.Label is { } label) sb.Append($"\r\n").Append(Utils.ColorString(label.Color, GetString(label.Key)));
                //if (Options.IsONMode) sb.Append($"\r\n").Append(Utils.ColorString(Utils.GetRoleColor(CustomRoles.ONVillager), GetString("OneNight")));
                // 自分がホストなら MOD は入っている (本編のかくれんぼで立てた部屋では版の登録が走らないので、登録は見ない)
                if (!GameStates.IsModHost && !(AmongUsClient.Instance && AmongUsClient.Instance.AmHost)) sb.Append($"\r\n").Append(Utils.ColorString(Color.red, GetString("Warning.NoModHost")));
                if (DebugModeManager.IsDebugMode) sb.Append("\r\n").Append(Utils.ColorString(Color.green, "デバッグモード"));

                if (GameStates.IsLobby) sb.Append("\r\n").Append(LobbyClock.Text);

                if (GameStates.IsLobby && !asClient)
                {
                    // 役職が宣言したロビーの警告 (SimpleRoleInfo.LobbyWarning)
                    rolesWithLobbyWarning ??= CustomRoleManager.AllRolesInfo.Values.Where(info => info.LobbyWarning != null).ToArray();
                    foreach (var info in rolesWithLobbyWarning)
                    {
                        if (!info.RoleName.IsEnable() || info.LobbyWarning() is not { } key) continue;
                        sb.Append("\r\n").Append(Utils.ColorString(Color.red, GetString(key)));
                    }
                }

                return sb.ToString();
            }
        }
        [HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start))]
        class VersionShowerStartPatch
        {
            static string[] mainManuText = {
                "11/20がLのリリース日らしい",
            };

            static TextMeshPro SpecialEventText;
            static void Postfix(VersionShower __instance)
            {
                TMPTemplate.SetBase(__instance.text);
                Main.credentialsText = $"<color={Main.ModColor}>{Main.ModName}</color> v{Main.PluginVersion}";
                if (Main.IsPrerelease)
                {
                    Main.credentialsText += $"{Main.PluginSubVersion}";

                    if (!Main.PluginVersionName.IsNullOrWhiteSpace())
                    {
                        Main.credentialsText += $"\r\n<#F39C12>{Main.PluginVersionName}</color>";
                    }
                }
#if DEBUG
                Main.credentialsText += $"\r\n<color={Main.ModColor}>{ThisAssembly.Git.Branch}({ThisAssembly.Git.Commit})</color>";
#endif
                var credentials = TMPTemplate.Create(
                    "TOHCredentialsText",
                    Main.credentialsText.Replace("\r\n", "  "),
                    fontSize: 1.6f,
                    alignment: TextAlignmentOptions.Left,
                    setActive: true);
                credentials.rectTransform.pivot = new Vector2(0f, 0.5f);
                credentials.rectTransform.sizeDelta = new Vector2(8f, 0.3f);
                credentials.transform.position = new Vector3(-1.45f, -2.87f, -2f);

                ErrorText.Create(__instance.text);
                if (Main.hasArgumentException && ErrorText.Instance != null)
                {
                    ErrorText.Instance.AddError(ErrorCode.Main_DictionaryError);
                }

                VersionChecker.Check();
                if (OptionItem.KeyDuplicated)
                {
                    ErrorText.Instance.AddError(ErrorCode.OptionKeyDuplicate);
                }

                if (SpecialEventText == null && TohLogo != null)
                {
                    SpecialEventText = TMPTemplate.Create(
                        "SpecialEventText",
                        "",
                        Color.white,
                        alignment: TextAlignmentOptions.Center,
                        parent: TohLogo.transform);
                    SpecialEventText.name = "SpecialEventText";
                    SpecialEventText.fontSizeMin = 3f;
                    SpecialEventText.alignment = TextAlignmentOptions.Center;
                    SpecialEventText.transform.localPosition = new Vector3(0f, -1.2f, 0f);
                }
                if (SpecialEventText != null)
                {
                    SpecialEventText.enabled = TitleLogoPatch.amongUsLogo != null;
                    SpecialEventText.gameObject.SetActive(true);
                }
                if (Main.IsValentine)
                {
                    SpecialEventText.text = "♥happy Valentine♥";
                    if (CultureInfo.CurrentCulture.Name == "ja-JP")
                        SpecialEventText.text += "<size=60%>\n<color=#b58428>チョコレート屋が\n一年ぶりに帰ってきた！</size></color>";
                    SpecialEventText.color = Utils.GetRoleColor(CustomRoles.Lovers);
                }
                else if (Main.IsWhiteDay)
                {
                    SpecialEventText.text = "♥happy WhiteDay♥";
                    if (CultureInfo.CurrentCulture.Name == "ja-JP")
                        SpecialEventText.text += "<size=60%>\n<color=#b58428>チョコレート屋でお返しを。</size></color>";
                    SpecialEventText.color = Utils.GetRoleColor(CustomRoles.Lovers);
                }
                else if (Main.IsAprilFool)
                {
                    SpecialEventText.text = "★happy Halloween★<size=60%>\n本日限定：ジャック・オー・ランタン復刻登場！</size>";
                    SpecialEventText.color = Utils.GetRoleColor(CustomRoles.JackOLantern);
                }
                else if (Main.IsInitialRelease)
                {
                    SpecialEventText.color = Color.yellow;
                    SpecialEventText.text = $"Happy Birthday to {Main.ModName}!";
                }
                else if (Main.IsHalloween)
                {
                    SpecialEventText.text = "★happy Halloween★";
                    if (CultureInfo.CurrentCulture.Name == "ja-JP")
                        SpecialEventText.text += "<size=60%>\n期間限定：ジャック・オー・ランタン登場！</size>";
                    SpecialEventText.color = Utils.GetRoleColor(CustomRoles.JackOLantern);
                }
                else if (Main.IsChristmas)
                {
                    SpecialEventText.color = Color.yellow;
                    SpecialEventText.text = "★Merry Christmas★";
                    if (CultureInfo.CurrentCulture.Name == "ja-JP")
                        SpecialEventText.text += "<size=60%>\n新インポスター、アドミニスター登場！</size>";
                }
                else if (CultureInfo.CurrentCulture.Name == "ja-JP")
                {
                    var num = IRandom.Instance.Next(mainManuText.Length);
                    SpecialEventText.text = $"★TOH-Lへようこそ！★\n<size=55%>{mainManuText[num]}</size>";
                    SpecialEventText.color = Color.yellow;
                }
            }
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
        class TitleLogoPatch
        {
            public static GameObject amongUsLogo;

            [HarmonyPriority(Priority.VeryHigh)]
            static void Postfix(MainMenuManager __instance)
            {
                amongUsLogo = GameObject.Find("LOGO-AU");

                var rightpanel = __instance.gameModeButtons.transform.parent;
                var logoObject = new GameObject("titleLogo_TOH");
                var logoTransform = logoObject.transform;
                TohLogo = logoObject.AddComponent<SpriteRenderer>();
                logoTransform.parent = rightpanel;
                logoTransform.localPosition = new(0f, 0.18f, 1f);
                //logoTransform.localScale *= 1f;
                TohLogo.sprite = Utils.LoadSprite("TownOfHost_L.Resources.TownOfHostL-Logo.png", 300f);
            }
        }
        [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
        class ModManagerLateUpdatePatch
        {
            public static void Prefix(ModManager __instance)
            {
                CheckMurderPatch.Update();
                // 1 つが投げても残りは止めない (切断の直後は送信が投げることがある)
                try { TownOfHostL.Core.NotifyRolesQueue.Flush(); }
                catch (System.Exception ex) { Logger.Exception(ex, "ModManagerLateUpdate"); }
                try { NameRpcBatcher.Flush(); }
                catch (System.Exception ex) { Logger.Exception(ex, "ModManagerLateUpdate"); }
                try { Modules.Net.OutfitSync.Tick(); }
                catch (System.Exception ex) { Logger.Exception(ex, "ModManagerLateUpdate"); }
                try { Modules.DeathBurst.Tick(); }
                catch (System.Exception ex) { Logger.Exception(ex, "ModManagerLateUpdate"); }
                try { OptionSaver.Tick(); }
                catch (System.Exception ex) { Logger.Exception(ex, "ModManagerLateUpdate"); }
            }
        }
        // MOD の印も自分の画面に描くだけなので、本編のかくれんぼで休んでいる間も出す
        [PatchBundle(PatchBundles.Core)]
        [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
        class ModStampPatch
        {
            public static void Prefix(ModManager __instance) => __instance.ShowModStamp();
            public static void Postfix(ModManager __instance)
            {
                __instance.ModStamp.transform.position = AspectPosition.ComputeWorldPosition(
                    __instance.localCamera, AspectPosition.EdgeAlignments.RightTop,
                    new Vector3(0.4f, 1.6f, __instance.localCamera.nearClipPlane + 0.1f));
            }
        }
    }
}