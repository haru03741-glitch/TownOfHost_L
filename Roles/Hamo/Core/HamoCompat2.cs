using System.Collections.Generic;
using Hazel;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;

namespace TownOfHostL
{
    /// <summary>いつも同じ値を返す設定 (TOH_L に無い hamo の機能の設定)</summary>
    public sealed class HamoConstOption
    {
        readonly bool value;
        public HamoConstOption(bool value) => this.value = value;
        public bool GetBool() => value;
    }

    public static partial class Options
    {
        // ===== hamo の共通設定 =====
        public static OptionItem DoubleTriggerThreshold;
        public static OptionItem AutoGrantPet;
        public static OptionItem ErasedBodyRemovePet;
        public static OptionItem ErasedBodyCover;
        public static OptionItem ClientEffects;
        public static OptionItem ClientEffectsNova;
        public static OptionItem ClientEffectsSlugger;
        public static OptionItem ClientEffectsSound;
        public static OptionItem ClientEffectsVolume;
        public static OptionItem ClientEffectsMeeting;
        public static OptionItem ClientEffectsDeath;
        public static OptionItem ClientEffectsShake;
        public static OptionItem ClientEffectsSkipDummy;
        public static OptionItem ClientEffectsTeleport;
        public static OptionItem ClientEffectsGuessUi;
        public static OptionItem ClientEffectsGuessUiDelay;
        public static OptionItem MadmateTell;
        static readonly string[] Tellopt = { "NoProcessing", "Crewmate", "Madmate", "Impostor" };

        // ===== TOH_L に無い hamo の機能 (いつもオフ) =====
        /// <summary>チャットコマンドを隠す (TOH_L に無い)</summary>
        public static readonly HamoConstOption ExHideChatCommand = new(false);
        public static readonly HamoConstOption ExRpcWeightR = new(false);
        /// <summary>1 ターン目の会議 (TOH_L に無い)</summary>
        public static bool firstturnmeeting => false;
        public static readonly HamoConstOption FirstTurnMeetingCantability = new(false);

        /// <summary>マッドメイトを占った時の結果</summary>
        public static CustomRoles MadTellOpt()
        {
            switch (Tellopt[MadmateTell?.GetValue() ?? 0])
            {
                case "Crewmate": return CustomRoles.Crewmate;
                case "Madmate": return CustomRoles.Madmate;
                case "Impostor": return CustomRoles.Impostor;
            }
            return CustomRoles.NotAssigned;
        }

        public const int HamoGeneralOptionId = 4_800_000;
        /// <summary>hamo の共通設定を作る (Options.Load から呼ぶ)</summary>
        public static void LoadHamoOptions()
        {
            var id = HamoGeneralOptionId;
            DoubleTriggerThreshold = FloatOptionItem.Create(id++, "DoubleTriggerThreashould", new(0.3f, 1f, 0.1f), 0.5f, TabGroup.ImpostorRoles, false)
                .SetHeader(true)
                .SetValueFormat(OptionFormat.Seconds);
            MadmateTell = StringOptionItem.Create(id++, "MadmateTellOption", Tellopt, 0, TabGroup.MadmateRoles, false)
                .SetHeader(true);

            AutoGrantPet = BooleanOptionItem.Create(id++, "AutoGrantPet", true, TabGroup.ModMainSettings, false)
                .SetHeader(true)
                .SetColorcode("#f0d36a");
            ErasedBodyRemovePet = BooleanOptionItem.Create(id++, "ErasedBodyRemovePet", true, TabGroup.ModMainSettings, false)
                .SetColorcode("#f0d36a");
            ErasedBodyCover = BooleanOptionItem.Create(id++, "ErasedBodyCover", true, TabGroup.ModMainSettings, false)
                .SetColorcode("#f0d36a");

            ClientEffects = BooleanOptionItem.Create(id++, "ClientEffects", true, TabGroup.ModMainSettings, false)
                .SetColorcode("#7fefff")
                .SetHeader(true);
            ClientEffectsNova = BooleanOptionItem.Create(id++, "ClientEffectsNova", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsSlugger = BooleanOptionItem.Create(id++, "ClientEffectsSlugger", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsSound = BooleanOptionItem.Create(id++, "ClientEffectsSound", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsVolume = IntegerOptionItem.Create(id++, "ClientEffectsVolume", new(10, 100, 10), 80, TabGroup.ModMainSettings, false)
                .SetValueFormat(OptionFormat.Percent).SetParent(ClientEffectsSound);
            ClientEffectsMeeting = BooleanOptionItem.Create(id++, "ClientEffectsMeeting", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsDeath = BooleanOptionItem.Create(id++, "ClientEffectsDeath", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsShake = BooleanOptionItem.Create(id++, "ClientEffectsShake", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsSkipDummy = BooleanOptionItem.Create(id++, "ClientEffectsSkipDummy", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsTeleport = BooleanOptionItem.Create(id++, "ClientEffectsTeleport", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsGuessUi = BooleanOptionItem.Create(id++, "ClientEffectsGuessUi", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
            ClientEffectsGuessUiDelay = FloatOptionItem.Create(id++, "ClientEffectsGuessUiDelay", new(0f, 10f, 0.5f), 3f, TabGroup.ModMainSettings, false)
                .SetParent(ClientEffectsGuessUi).SetValueFormat(OptionFormat.Seconds);
        }
    }

    public partial class Main
    {
        /// <summary>hamo の「CS 版」判定。TOH_L はいつも違う</summary>
        public static bool IsCs() => false;
        // hamo のサボタージュの記録 (イビルブレンダーの偽サボタージュ用)
        public static bool IsActiveSabotage;
        public static SystemTypes SabotageType;
        public static byte LastSab = byte.MaxValue;
        public static float SabotageActivetimer;
    }

    public static partial class AntiBlackout
    {
        /// <summary>暗転対策が動いている途中か (hamo)</summary>
        public static bool IsSet => IsCached;
    }

    static partial class CustomRolesHelper
    {
        /// <summary>ゲッサーを持つ役職がいるか。TOH_L にゲッサーは無い</summary>
        public static bool CheckGuesser() => false;
    }

    public partial class TaskState
    {
        public bool HasCompletedEnoughCountOfTasks(float count) => HasCompletedEnoughCountOfTasks((int)count);
    }

    partial class LateTask
    {
        /// <summary>hamo の形 (4 つ目はログを出すか)</summary>
        public LateTask(System.Action action, float time, string name, bool? log)
            : this(action, time, name ?? "No Name Task", false) { }
    }

    static partial class RPC
    {
        public static void RpcSyncAllNetworkedPlayer(int targetClientId = -1) => ExtendedPlayerControl.RpcSyncAllNetworkedPlayer(targetClientId);
        /// <summary>役職の公開情報を送る (hamo)。書いた後 FinishRpcImmediately する</summary>
        public static MessageWriter RpcPublicRoleSync(byte playerid, CustomRoles role)
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.PublicRoleSync, SendOption.None, -1);
            writer.Write(playerid);
            writer.WritePacked((int)role);
            return writer;
        }
    }

    public static partial class NameColorManager
    {
        /// <summary>会議中に名前の色を送り直す (hamo)</summary>
        public static void RpcMeetingColorName(PlayerControl pc = null)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (pc == null) Utils.NotifyRoles(isForMeeting: true, NoCache: true);
            else Utils.NotifyRoles(isForMeeting: true, SpecifySeer: pc, NoCache: true);
        }
    }

    public static class UtilsTask
    {
        public static bool HasTasks(NetworkedPlayerInfo p, bool ForRecompute = true) => Utils.HasTasks(p, ForRecompute);
    }

    /// <summary>hamo のゲームモード。TOH_L ではいつも普通のモードとして扱う</summary>
    public static class GameModeManager
    {
        public static bool IsStandardClass() => Options.CurrentGameMode == CustomGameMode.Standard;
    }
    /// <summary>hamo の 1 ターン目の会議 (TOH_L に無い)</summary>
    public static class FirstTurnMeeting
    {
        public static bool BeforeFirstMeeting => false;
    }
    /// <summary>hamo のゲスト向け通信制限 (TOH_L に無い)</summary>
    public static class NonHostRpcGuard
    {
        public static bool IsHidden(byte playerId) => false;
    }
    public static class NetworkSafety
    {
        public static bool AllowTeleport() => true;
    }
    /// <summary>hamo の最初のキルクール (TOH_L は TOH_L の設定を使う)</summary>
    public static class InitialKillCooldownOption
    {
        public static bool TryGet(PlayerControl player, out float cooldown)
        {
            cooldown = 0f;
            return false;
        }
    }
    /// <summary>hamo のゲッサー (TOH_L に無い)</summary>
    public static class GuessManager
    {
        public static bool CanUseGuess(PlayerControl pc) => false;
    }
    public static class GuesserUi
    {
        public static void Enable<T>(T list) { }
    }

    /// <summary>今いるベントの番号</summary>
    public static class VentilationSystemUpdateSystemPatch
    {
        public static Dictionary<byte, byte> NowVentId = new();
        [Attributes.GameModuleInitializer]
        public static void Init() => NowVentId.Clear();
    }
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoExitVent))]
    class HamoExitVentPatch
    {
        public static void Prefix(PlayerPhysics __instance)
        {
            if (__instance?.myPlayer != null) VentilationSystemUpdateSystemPatch.NowVentId.Remove(__instance.myPlayer.PlayerId);
        }
    }

    public static partial class MeetingHudPatch
    {
        /// <summary>会議で送った文の記録 (hamo)。(送り先, 文, 題)。送るのは役職の側</summary>
        public static List<(byte sentto, string text, string title)> HamoMeetingSends = new();

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
        class HamoMeetingSendsPatch
        {
            public static void Postfix()
            {
                if (!AmongUsClient.Instance.AmHost) return;
                var sends = HamoMeetingSends.ToArray();
                HamoMeetingSends.Clear();
                // hamo の役職の会議の始めの文
                var addMessages = new List<string>();
                foreach (var role in HamoHooks.AllHamoRoles)
                {
                    var m = role.MeetingAddMessage();
                    if (!string.IsNullOrEmpty(m)) addMessages.Add(m);
                }
                if (!string.IsNullOrEmpty(MeetingVoteManager.Voteresult))
                {
                    addMessages.Add(MeetingVoteManager.Voteresult);
                    MeetingVoteManager.Voteresult = "";
                }
                if (addMessages.Count == 0) return;
                _ = new LateTask(() =>
                {
                    foreach (var m in addMessages)
                        Utils.SendMessage(m);
                }, 3.5f, "HamoMeetingSends", true);
            }
        }
    }
}

namespace TownOfHostL.Modules
{
    public partial class MeetingTimeManager
    {
        /// <summary>天秤の会議: 話し合い無し、投票の時間だけ</summary>
        public static void Balancer(int time)
        {
            DiscussionTime = 0;
            VotingTime = time;
        }
    }
}

namespace TownOfHostL.Roles.Core
{
    public static partial class CustomRoleManager
    {
        /// <summary>誰かがベントに入った時に呼ばれる (hamo)</summary>
        public static HashSet<System.Func<PlayerPhysics, int, bool>> OnEnterVentOthers = new();
    }
}
