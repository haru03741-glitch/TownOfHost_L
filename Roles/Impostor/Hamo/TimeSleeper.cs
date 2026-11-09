using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;
using TownOfHostL.Roles.Crewmate;

namespace TownOfHostL.Roles.Impostor;

public sealed class TimeSleeper : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: TownOfHost_Pko (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(TimeSleeper),
            player => new TimeSleeper(player),
            CustomRoles.TimeSleeper,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 7700,
            SetUpOptionItem,
            "ts"
        );

    public TimeSleeper(PlayerControl player)
        : base(RoleInfo, player)
    {
        RecordDuration = OptionRecordDuration.GetFloat();
        RewindSpeed = OptionRewindSpeed.GetFloat();
        PhantomCooldown = OptionPhantomCooldown.GetFloat();

        isRecording = false;
        isRewinding = false;
        recordTimer = 0f;
        rewindTimer = 0f;
        rewindIndex = 0;
        positionHistory = new();
        rewindSkipPlayers = new();
        movingPlatformUsedDuringRec = new();
    }

    static OptionItem OptionPhantomCooldown;
    static float PhantomCooldown;
    static OptionItem OptionRecordDuration;
    static float RecordDuration;
    static OptionItem OptionRewindSpeed;
    static float RewindSpeed;

    const float RewindMinGap = 0.5f;
    // 途中の歩のために使わず残しておく SnapTo の本数 (仕上げの全員分と他の役職の TP)
    const float RewindReserve = 30f;
    bool isRecording;
    bool isRewinding;
    float recordTimer;
    float rewindTimer;
    int rewindIndex;

    Dictionary<byte, List<Vector2>> positionHistory;
    HashSet<byte> rewindSkipPlayers;
    HashSet<byte> movingPlatformUsedDuringRec;

    enum OptionName
    {
        TimeSleeperRecordDuration,
        TimeSleeperRewindSpeed,
    }

    static void SetUpOptionItem()
    {
        OptionPhantomCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0, 180, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRecordDuration = FloatOptionItem.Create(RoleInfo, OptionName.TimeSleeperRecordDuration, new(1f, 20f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRewindSpeed = FloatOptionItem.Create(RoleInfo, OptionName.TimeSleeperRewindSpeed, new(0.5f, 5f, 0.5f), 1f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }

    public float CalculateKillCooldown() => 30f;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;
    bool IUsePhantomButton.IsresetAfterKill => false;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = PhantomCooldown;
    }

    bool IUsePhantomButton.IsPhantomRole => true;

    static bool IsUsingMovingPlatform(PlayerControl pc)
    {
        if (pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) return true;
        if (pc.onLadder) return true;
        if ((MapNames)Main.NormalOptions.MapId == MapNames.Airship
            && Vector2.Distance(pc.GetTruePosition(), new Vector2(7.76f, 8.56f)) <= 1.9f) return true;
        if (pc.inMovingPlat) return true;
        return false;
    }

    static bool IsBeamingOrCharging(PlayerControl pc)
    {
        if (pc.GetRoleClass() is HadouHo hh)
            return hh.IsCharging || hh.ShowBeamMark;
        if (pc.GetRoleClass() is JackalHadouHo jhh)
            return jhh.IsCharging || jhh.IsSuperCharging || jhh.ShowBeamMark;
        if (pc.GetRoleClass() is SheriffHadouHo shh)
            return shh.IsCharging || shh.ShowBeamMark;
        return false;
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        if (isRecording || isRewinding) return;
        if (!Player.IsAlive()) return;

        isRecording = true;
        recordTimer = 0f;
        positionHistory.Clear();
        rewindSkipPlayers.Clear();
        movingPlatformUsedDuringRec.Clear();

        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (IsUsingMovingPlatform(pc))
            {
                rewindSkipPlayers.Add(pc.PlayerId);
                continue;
            }
            positionHistory[pc.PlayerId] = new List<Vector2>();
        }

        Utils.NotifyRoles();
        SendRpc();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;

        if (isRecording)
        {
            recordTimer += Time.fixedDeltaTime;

            if (recordTimer % 0.1f < Time.fixedDeltaTime)
            {
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    if (IsUsingMovingPlatform(pc))
                    {
                        movingPlatformUsedDuringRec.Add(pc.PlayerId);
                        continue;
                    }

                    if (rewindSkipPlayers.Contains(pc.PlayerId)) continue;

                    if (!positionHistory.ContainsKey(pc.PlayerId))
                        positionHistory[pc.PlayerId] = new List<Vector2>();

                    var pos = pc.transform.position;
                    positionHistory[pc.PlayerId].Add(pos);
                }
            }

            if (recordTimer >= RecordDuration)
            {
                isRecording = false;
                isRewinding = true;
                rewindTimer = 0f;
                rewindIndex = positionHistory.ContainsKey(Player.PlayerId)
                    ? positionHistory[Player.PlayerId].Count - 1
                    : 0;
                Utils.NotifyRoles();
                SendRpc();
            }
            return;
        }

        if (isRewinding)
        {
            rewindTimer += Time.fixedDeltaTime * RewindSpeed;

            if (rewindTimer >= 0.1f)
            {
                rewindTimer = 0f;

                if (rewindIndex < 0)
                {
                    isRewinding = false;
                    positionHistory.Clear();
                    rewindSkipPlayers.Clear();
                    movingPlatformUsedDuringRec.Clear();
                    Player.RpcResetAbilityCooldown();
                    Utils.NotifyRoles();
                    SendRpc();
                    return;
                }

                foreach (var kvp in positionHistory)
                {
                    var pc = Utils.GetPlayerById(kvp.Key);
                    if (pc == null || !pc.IsAlive()) continue;
                    if (rewindIndex >= kvp.Value.Count) continue;

                    if (rewindSkipPlayers.Contains(pc.PlayerId)) continue;
                    if (movingPlatformUsedDuringRec.Contains(pc.PlayerId)) continue;
                    if (IsUsingMovingPlatform(pc)) continue;
                    if (pc.inVent) continue;
                    if (IsBeamingOrCharging(pc)) continue;

                    var targetPos = kvp.Value[rewindIndex];
                    // 0.1 秒ごとに全員へ送ると SnapTo の予算がすぐ尽き、巻き戻しが途中で崩れて他の役職の TP も落ちる。
                    // 途中の歩は、今の位置から離れていて予算に余裕がある時だけ送る。最後の 1 歩は記録した位置へ必ず合わせる
                    float gap = Vector2.Distance(pc.transform.position, targetPos);
                    if (rewindIndex == 0)
                    {
                        if (gap >= 0.05f) pc.RpcSnapTo(targetPos, force: true);
                        continue;
                    }
                    if (gap < RewindMinGap || !TownOfHostL.Modules.Net.SnapToBudget.HasHeadroom(RewindReserve)) continue;
                    pc.RpcSnapTo(targetPos);
                }

                rewindIndex--;
            }
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        isRecording = false;
        isRewinding = false;
        recordTimer = 0f;
        rewindTimer = 0f;
        rewindIndex = 0;
        positionHistory.Clear();
        rewindSkipPlayers.Clear();
        movingPlatformUsedDuringRec.Clear();
        SendRpc();
    }

    public override void OnStartMeeting()
    {
        isRecording = false;
        isRewinding = false;
        recordTimer = 0f;
        rewindTimer = 0f;
        rewindIndex = 0;
        positionHistory.Clear();
        rewindSkipPlayers.Clear();
        movingPlatformUsedDuringRec.Clear();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;
        AURoleOptions.PhantomCooldown = PhantomCooldown;
        Player.RpcResetAbilityCooldown();
    }

    void SendRpc()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(isRecording);
        sender.Writer.Write(isRewinding);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        isRecording = reader.ReadBoolean();
        isRewinding = reader.ReadBoolean();
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";
        if (isRecording)
        {
            var remaining = RecordDuration - recordTimer;
            return $"{(isForHud ? "" : "<size=60%>")}<color=#a0c4ff>記録中... {remaining:F1}s</color>";
        }
        if (isRewinding)
            return $"{(isForHud ? "" : "<size=60%>")}<color=#a0c4ff>巻き戻し中...</color>";
        return $"{(isForHud ? "" : "<size=60%>")}<color=#a0c4ff>ファントムボタン → タイムスリープ</color>";
    }

    public override string GetProgressText(bool comms = false)
    {
        if (isRecording) return $"<color=#a0c4ff>●REC</color>";
        if (isRewinding) return $"<color=#a0c4ff>◀◀</color>";
        return "";
    }

    public override string GetAbilityButtonText() => "タイムスリープ";
}
