using System;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;
using TMPro;
using TownOfHostL.Templates;
using TownOfHostL.Modules;

using TownOfHostL.Core.Modes;

namespace TownOfHostL;

[HarmonyPatch(typeof(HudManager))]
class HudManagerPatch
{
    static TextMeshPro optionShowerText;
    static string lastOptionText;
    // 「キル」の文字は言語が変わらない限り同じなので、毎フレーム本編の翻訳を引かない
    static string killLabelCache;
    static SupportedLangs killLabelLang;
    static string KillLabel()
    {
        var lang = TranslationController.Instance.currentLanguage.languageID;
        if (killLabelCache == null || lang != killLabelLang)
        {
            killLabelCache = GetString(StringNames.KillLabel);
            killLabelLang = lang;
        }
        return killLabelCache;
    }

    public static bool ShowDebugText = false;
    public static int LastCallNotifyRolesPerSecond = 0;
    public static int NowCallNotifyRolesCount = 0;
    public static int LastSetNameDesyncCount = 0;
    public static int LastFPS = 0;
    public static int NowFrameCount = 0;
    public static float FrameRateTimer = 0.0f;
    public static TextMeshPro LowerInfoText;

    [HarmonyPatch(nameof(HudManager.Start)), HarmonyPostfix]
    public static void StartPostfix(HudManager __instance)
    {
        CreateOptionShower(__instance);
        optionShowerText.transform.position = new Vector3(-3.3f, 2.85f, 1f);
    }

    /// <summary>TOHL が休んでいる間に HudManager が作られた時に、戻ってから作る。</summary>
    public static void EnsureOptionShower(HudManager hud)
    {
        if (optionShowerText != null) return;
        CreateOptionShower(hud);
        // HUD はカメラと一緒に動き、この時のカメラは原点にいない。Start の時と同じ見た目の位置になるよう HUD からの相対で置く
        var hudPos = hud.transform.position;
        optionShowerText.transform.position = new Vector3(hudPos.x - 3.3f, hudPos.y + 2.85f, 1f);
    }

    private static void CreateOptionShower(HudManager __instance)
    {
        optionShowerText = TMPTemplate.Create(
            "OptionShowerText",
            "",
            fontSize: 0.5f,
            alignment: TextAlignmentOptions.TopLeft,
            setActive: false,
            parent: __instance.transform);
        lastOptionText = null;
    }
    [HarmonyPatch(nameof(HudManager.Update)), HarmonyPostfix]
    public static void UpdatePostfix(HudManager __instance)
    {
        if (!GameStates.IsModHost) return;
        LobbyPreJit.Tick();
        var player = PlayerControl.LocalPlayer;
        if (player == null) return;
        var TaskTextPrefix = "";
        //壁抜け
        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            if ((!AmongUsClient.Instance.IsGameStarted || !GameStates.IsOnlineGame)
                && player.CanMove)
            {
                player.Collider.offset = new Vector2(0f, 127f);
            }
        }
        //壁抜け解除
        if (player.Collider.offset.y == 127f)
        {
            if (!Input.GetKey(KeyCode.LeftControl) || (AmongUsClient.Instance.IsGameStarted && GameStates.IsOnlineGame))
            {
                player.Collider.offset = new Vector2(0f, -0.3636f);
            }
        }
        if (GameStates.IsLobby)
        {
            optionShowerText.gameObject.SetActive(true);

            string optionText = OptionShower.GetText();
            if (AmongUsClient.Instance.AmHost && Main.NormalOptions != null)
            {
                bool crewZero = Main.NormalOptions.CrewLightMod <= 0f;
                bool impZero = Main.NormalOptions.ImpostorLightMod <= 0f;
                if (crewZero || impZero)
                {
                    var warnParts = new System.Collections.Generic.List<string>();
                    if (crewZero) warnParts.Add("クルービジョン");
                    if (impZero) warnParts.Add("インポスタービジョン");
                    string warn = Utils.ColorString(Color.red, $" {string.Join(" / ", warnParts)} が 0 に設定されています");
                    optionText = warn + "\n" + optionText;
                }
            }
            // 中身が変わった時だけ書き込む (毎フレーム同じ長文をゲーム側へ写していた)
            if (!ReferenceEquals(optionText, lastOptionText) && !string.Equals(optionText, lastOptionText, StringComparison.Ordinal))
            {
                lastOptionText = optionText;
                optionShowerText.text = optionText;
                optionShowerText.fontSizeMin =
                optionShowerText.fontSizeMax = (TranslationController.Instance.currentLanguage.languageID == SupportedLangs.Japanese || Main.ForceJapanese.Value) ? 1.05f : 1.2f;
            }
        }
        else
        { optionShowerText.gameObject.SetActive(false); }
        //ゲーム中でなければ以下は実行されない
        if (!AmongUsClient.Instance.IsGameStarted) return;

        Utils.CountAlivePlayers();

        if (SetHudActivePatch.IsActive)
        {
            if (player.IsAlive())
            {
                var roleClass = player.GetRoleClass();
                if (roleClass != null)
                {
                    var killLabel = (roleClass as IKiller)?.OverrideKillButtonText(out string text) == true ? text : KillLabel();
                    __instance.KillButton.OverrideText(killLabel);
                    if (roleClass.HasAbility)
                    {
                        __instance.AbilityButton.OverrideText(roleClass.GetAbilityButtonText());
                        __instance.AbilityButton.ToggleVisible(roleClass.CanUseAbilityButton() && GameStates.IsInTask);
                    }
                }
                if (player.GetCustomRole().IsCCCatRoles())
                {
                    __instance.AbilityButton.ToggleVisible(CatchCat.CatPlayer.CanUseVent(player) && GameStates.IsInTask);
                }

                //バウンティハンターのターゲットテキスト
                if (LowerInfoText == null)
                {
                    LowerInfoText = UnityEngine.Object.Instantiate(__instance.KillButton.buttonLabelText);
                    LowerInfoText.transform.parent = __instance.transform;
                    LowerInfoText.transform.localPosition = new Vector3(0, -2f, 0);
                    LowerInfoText.alignment = TMPro.TextAlignmentOptions.Center;
                    LowerInfoText.overflowMode = TMPro.TextOverflowModes.Overflow;
                    LowerInfoText.enableWordWrapping = false;
                    LowerInfoText.color = Palette.EnabledColor;
                    LowerInfoText.fontSizeMin = 2.0f;
                    LowerInfoText.fontSizeMax = 2.0f;
                }

                // 組み立ててから 1 回だけ書く (以前は書いては読み戻すのを毎フレーム 3 回していた)
                string lowerText = VentEnterTask.GetLowerText(player, isForMeeting: GameStates.IsMeeting, isForHud: true) ?? "";
                if (lowerText != "") lowerText += "\n";
                lowerText += roleClass?.GetLowerText(player, isForMeeting: GameStates.IsMeeting, isForHud: true) ?? "";
                TextWriteCache.SetText(LowerInfoText, lowerText);
                LowerInfoText.enabled = lowerText != "";

                if (!AmongUsClient.Instance.IsGameStarted && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay)
                {
                    LowerInfoText.enabled = false;
                }

                if (player.CanUseKillButton())
                {
                    __instance.KillButton.ToggleVisible(player.IsAlive() && GameStates.IsInTask);
                    player.Data.Role.CanUseKillButton = true;
                }
                else
                {
                    __instance.KillButton.SetDisabled();
                    __instance.KillButton.ToggleVisible(false);
                }
                if (player.GetCustomRole().HasRoleFlag(RoleFlags.FakeTasksLabel))
                    TaskTextPrefix += GetString(StringNames.FakeTasks);

                bool CanUseVent = player.CanUseImpostorVentButton();
                __instance.ImpostorVentButton.ToggleVisible(CanUseVent);
                player.Data.Role.CanVent = CanUseVent;
            }
            else
            {
                __instance.ReportButton.Hide();
                __instance.ImpostorVentButton.Hide();
                __instance.KillButton.Hide();
                __instance.AbilityButton.Show();
                __instance.AbilityButton.OverrideText(GetString(StringNames.HauntAbilityName));
            }
        }

        if (Input.GetKeyDown(KeyCode.Y) && AmongUsClient.Instance.NetworkMode == NetworkModes.FreePlay)
        {
            __instance.ToggleMapVisible(new MapOptions()
            {
                Mode = MapOptions.Modes.Sabotage,
                AllowMovementWhileMapOpen = true
            });
            if (player.AmOwner)
            {
                player.MyPhysics.inputHandler.enabled = true;
                ConsoleJoystick.SetMode_Task();
            }
        }

        if (AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame) RepairSender.enabled = false;
        if (Input.GetKeyDown(KeyCode.RightShift) && AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame)
        {
            RepairSender.enabled = !RepairSender.enabled;
            RepairSender.Reset();
        }
        if (RepairSender.enabled && AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0)) RepairSender.Input(0);
            if (Input.GetKeyDown(KeyCode.Alpha1)) RepairSender.Input(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) RepairSender.Input(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) RepairSender.Input(3);
            if (Input.GetKeyDown(KeyCode.Alpha4)) RepairSender.Input(4);
            if (Input.GetKeyDown(KeyCode.Alpha5)) RepairSender.Input(5);
            if (Input.GetKeyDown(KeyCode.Alpha6)) RepairSender.Input(6);
            if (Input.GetKeyDown(KeyCode.Alpha7)) RepairSender.Input(7);
            if (Input.GetKeyDown(KeyCode.Alpha8)) RepairSender.Input(8);
            if (Input.GetKeyDown(KeyCode.Alpha9)) RepairSender.Input(9);
            if (Input.GetKeyDown(KeyCode.Return)) RepairSender.InputEnter();
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ToggleHighlight))]
    class ToggleHighlightPatch
    {
        public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] bool active, [HarmonyArgument(1)] RoleTeamTypes team)
        {
            var player = PlayerControl.LocalPlayer;
            if (!GameStates.IsInTask) return;

            if (player.CanUseKillButton())
            {
                ((Renderer)__instance.cosmetics.currentBodySprite.BodySprite).material.SetColor("_OutlineColor", Utils.GetRoleColor(player.GetCustomRole()));
            }
        }
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.SetOutline))]
class SetVentOutlinePatch
{
    public static void Postfix(Vent __instance, [HarmonyArgument(1)] ref bool mainTarget)
    {
        var player = PlayerControl.LocalPlayer;
        Color color = PlayerControl.LocalPlayer.GetRoleColor(true);
        ((Renderer)__instance.myRend).material.SetColor("_OutlineColor", color);
        ((Renderer)__instance.myRend).material.SetColor("_AddColor", mainTarget ? color : Color.clear);
    }
}
[HarmonyPatch(typeof(HudManager), nameof(HudManager.SetHudActive), new Type[] { typeof(PlayerControl), typeof(RoleBehaviour), typeof(bool) })]
class SetHudActivePatch
{
    public static bool IsActive = false;
    public static void Postfix(HudManager __instance, [HarmonyArgument(2)] bool isActive)
    {
        __instance.ReportButton.ToggleVisible(!GameStates.IsLobby && isActive);
        if (!GameStates.IsModHost) return;
        IsActive = isActive;
        if (GameStates.IsLobby) return;
        if (!isActive) return;

        var player = PlayerControl.LocalPlayer;
        __instance.KillButton.ToggleVisible(player.CanUseKillButton());
        __instance.ImpostorVentButton.ToggleVisible(player.CanUseImpostorVentButton());
        __instance.SabotageButton.ToggleVisible(player.CanUseSabotageButton());
    }
}
[HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.Show))]
class MapBehaviourShowPatch
{
    public static void Prefix(MapBehaviour __instance, ref MapOptions opts)
    {
        if (GameStates.IsMeeting) return;

        if (opts.Mode is MapOptions.Modes.Normal or MapOptions.Modes.Sabotage)
        {
            var player = PlayerControl.LocalPlayer;
            if (player.GetRoleClass() is IKiller killer && killer.CanUseSabotageButton())
                opts.Mode = MapOptions.Modes.Sabotage;
            else
                opts.Mode = MapOptions.Modes.Normal;
        }
    }
}
[HarmonyPatch(typeof(TaskPanelBehaviour), nameof(TaskPanelBehaviour.SetTaskText))]
class TaskPanelBehaviourPatch
{
    // タスク表示の文章が更新・適用された後に実行される
    public static void Postfix(TaskPanelBehaviour __instance)
    {
        if (GameStates.IsLobby) return; 
        if (!GameStates.IsModHost) return;
        PlayerControl player = PlayerControl.LocalPlayer;

        // 役職説明表示
        if (!player.GetCustomRole().IsVanilla())
        {
            var RoleWithInfo = $"{RoleText.GetRoleNameText(player.PlayerId, showSubRole: false)}:\r\n";
            RoleWithInfo += player.GetRoleInfo();
            __instance.taskText.text = Utils.ColorString(player.GetRoleColor(true), RoleWithInfo) + "\n" + __instance.taskText.text;
        }

        // RepairSenderの表示
        if (RepairSender.enabled && AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame)
        {
            __instance.taskText.text = RepairSender.GetText();
        }
    }
}
[HarmonyPatch(typeof(HudManager), nameof(HudManager.CoShowIntro))]
class HudManagerCoShowIntroPatch
{
    public static bool Cancel = true;
    public static bool Prefix(HudManager __instance, ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (AmongUsClient.Instance.AmHost
            && ModeCatalog.Current.UsesStandardRoles
            && Cancel)
        {
            Logger.Warn("イントロの表示をキャンセルしました", "CoShowIntro");
            // 本編の CoSetRole は戻り値をそのまま StartCoroutine に渡すので、null だと例外でその先が止まる
            __result = Empty().WrapToIl2Cpp();
            return false;
        }

        Cancel = true;
        return true;
    }
    private static System.Collections.IEnumerator Empty()
    {
        yield break;
    }
}
class RepairSender
{
    public static bool enabled = false;
    public static bool TypingAmount = false;

    public static int SystemType;
    public static byte amount;

    public static void Input(byte num)
    {
        if (!TypingAmount)
        {
            //SystemType入力中
            SystemType *= 10;
            SystemType += num;
        }
        else
        {
            //Amount入力中
            amount *= 10;
            amount += num;
        }
    }
    public static void InputEnter()
    {
        if (!TypingAmount)
        {
            //SystemType入力中
            TypingAmount = true;
        }
        else
        {
            //Amount入力中
            Send();
        }
    }
    public static void Send()
    {
        ShipStatus.Instance.RpcUpdateSystem((SystemTypes)SystemType, amount);
        Reset();
    }
    public static void Reset()
    {
        TypingAmount = false;
        SystemType = 0;
        amount = 0;
    }
    public static string GetText()
    {
        return SystemType.ToString() + "(" + ((SystemTypes)SystemType).ToString() + ")\r\n" + amount;
    }
}