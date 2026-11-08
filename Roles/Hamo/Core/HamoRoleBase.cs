using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;

namespace TownOfHostL.Roles.Core;

/// <summary>
/// TownOfHost-hamo から移植した役職の土台。
/// hamo の RoleBase と同じ名前・引数の仮想関数を持ち、TOH_L 側の仮想関数から呼び分ける。
/// TOH_L に無いフックは HamoHooks から呼ぶ。
/// </summary>
public abstract class HamoRoleBase : RoleBase
{
    public HamoRoleBase(SimpleRoleInfo roleInfo, PlayerControl player, Func<HasTask> hasTasks = null, bool? hasAbility = null)
        : base(roleInfo, player, hasTasks, hasAbility)
    { }

    /// <summary>ゲーム開始後にインスタンス作成された時に呼ばれる</summary>
    public virtual void ChengeRoleAdd() { }

    /// <summary>勝者がほぼ決まった後 (確定敗北の後) に呼ばれる。勝者を足したり外したりできる</summary>
    public virtual void CheckWinner(GameOverReason reason) { }

    // ===== RPC =====
    protected new class RoleRPCSender : IDisposable
    {
        public MessageWriter Writer;
        public RoleRPCSender(RoleBase role, SendOption sendOption = SendOption.None)
        {
            Writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.CustomRoleSync, SendOption.Reliable, -1);
            Writer.Write(role.Player.PlayerId);
        }
        public void Dispose()
        {
            AmongUsClient.Instance.FinishRpcImmediately(Writer);
        }
    }
    protected RoleRPCSender CreateSender(SendOption sendOption = SendOption.None) => new(this, sendOption);
    public virtual void ReceiveRPC(MessageReader reader) { }
    public sealed override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType == CustomRPC.CustomRoleSync) ReceiveRPC(reader);
    }

    // ===== 死亡・スポーン・開始 =====
    public virtual void OnDead(PlayerControl player) { }
    public virtual void OnSpawn(bool initialState = false) { }
    public virtual void StartGameTasks() { }
    public virtual void ChangeColor() { }
    public virtual void OnLeftPlayer(PlayerControl player) { }

    // ===== 変身・透明化 =====
    public virtual bool CheckShapeshift(PlayerControl target, ref bool shouldAnimate) => true;
    public sealed override bool OnCheckShapeshift(PlayerControl target, ref bool animate) => CheckShapeshift(target, ref animate);
    public virtual bool CheckVanish() => true;
    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        if (CheckVanish()) return true;
        killCooldown = -1f;
        return false;
    }

    // ===== ベント =====
    public virtual bool CanClickUseVentButton => true;
    public virtual bool CanVentMoving(PlayerPhysics physics, int ventId) => true;
    public virtual void OnVentilationSystemUpdate(PlayerControl user, VentilationSystem.Operation Operation, int ventId) { }

    // ===== 会議 =====
    public virtual string MeetingAddMessage() => "";
    public virtual bool CheckVoteAsVoter(byte votedForId, PlayerControl voter) => true;
    /// <summary>hamo の役職の投票の確認は HamoHooks.CheckVoteAsVoter から全員分呼ぶ</summary>
    public sealed override bool CheckVoteAsVoter(PlayerControl votedFor) => true;
    public virtual bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile) => false;
    public virtual bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportreson reason) => false;
    public virtual bool? CheckGuess(PlayerControl killer) => true;

    // ===== タスク =====
    public virtual bool OnCompleteTask(uint taskid) => true;
    public sealed override bool OnCompleteTask() => OnCompleteTask(HamoHooks.CurrentTaskId);
    public virtual bool CanTask() => Utils.HasTasks(PlayerControl.LocalPlayer.Data, false);

    // ===== サボタージュ =====
    public virtual void OnFixSabotage(PlayerControl player, SystemTypes systemTypes, byte amount) { }
    public virtual void AfterSabotage(SystemTypes systemType) { }

    // ===== 役職の見え方 =====
    public virtual RoleTypes? AfterMeetingRole => null;
    public virtual CustomRoles TellResults(PlayerControl player) => CustomRoles.NotAssigned;
    public virtual CustomRoles HaveAddRole() => CustomRoles.NotAssigned;
    public virtual CustomRoles Misidentify() => CustomRoles.NotAssigned;

    // ===== 名前の表示 =====
    public virtual bool NotifyRolesCheckOtherName => false;
    public virtual bool AllEnabledColor => false;
    public virtual bool GetTemporaryName(ref string name, ref bool NoMarker, bool isForMeeting, PlayerControl seer, PlayerControl seen = null) => false;

    public virtual void OverrideTrueRoleName(ref Color roleColor, ref string roleText) { }
    public sealed override void OverrideShowMainRoleText(ref Color roleColor, ref string roleText) => OverrideTrueRoleName(ref roleColor, ref roleText);

    public virtual void OverrideProgressTextAsSeen(PlayerControl seer, ref bool enabled, ref string text) { }

    public virtual string GetProgressText(bool comms = false, bool GameLog = false) => "";
    public sealed override string GetProgressText(bool comms = false) => GetProgressText(comms, false);

    // ===== ボタン =====
    public virtual bool OverrideAbilityButton(out string text)
    {
        text = default;
        return false;
    }

    // ===== hamo の共通設定・道具 =====
    public static FloatValueRule OptionBaseCoolTime => new(0, 180, 0.5f);
    public static AudioClip GetIntrosound(RoleTypes roleType) => GetIntroSound(roleType);
    public static string GetString(StringNames stringName)
        => DestroyableSingleton<TranslationController>.Instance.GetString(stringName, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(0));
    public static string GetString(string str, Dictionary<string, string> replacementDic = null) => Translator.GetString(str, replacementDic);

    protected new enum GeneralOption
    {
        Cooldown,
        KillCooldown,
        CanVent,
        ImpostorVision,
        CanUseSabotage,
        CanCreateSideKick,
        Duration,
        cantaskcount,
        MeetingMaxTime,
        PlayShapeAnimate,
        TaskAwakening,
        AwakeningTaskcount,
        AbilityAwakening,
        OptionCount,
        EngineerInVentCooldown,
        TaskTrigger,
        CanUseActiveComms
    }
    public enum DontReportreson
    {
        None,
        wait,
        NonReport,
        Transparent,
        CantUseButton,
        Eat,
        Other,
        Impostor,
        Jiangshi,
    }
}
