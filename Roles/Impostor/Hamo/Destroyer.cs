using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Patches;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

public sealed class Destroyer : RoleBase, IImpostor
{
    // 役職の元: Nebula on the Ship (Dolly1016/Nebula-Public, GPL-3.0・TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Destroyer),
            player => new Destroyer(player),
            CustomRoles.Destroyer,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 97300,
            SetupOptionItem,
            "dsy",
            "#FF1919"
        );

    public Destroyer(PlayerControl player) : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        crushTime = OptionCrushTime.GetFloat();
        leaveTrace = OptionLeaveTrace.GetBool();
        traceTurns = OptionTraceTurns.GetInt();
        allKillFlash = OptionAllKillFlash.GetBool();
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionCrushTime;
    static OptionItem OptionLeaveTrace;
    static OptionItem OptionTraceTurns;
    static OptionItem OptionAllKillFlash;
    enum OptionName { DestroyerCrushTime, DestroyerLeaveTrace, DestroyerTraceTurns, DestroyerAllKillFlash }
    static bool allKillFlash;
    static bool leaveTrace;
    static int traceTurns;
    readonly List<DestroyerTrace> traces = new();

    static float killCooldown;
    static float crushTime;

    PlayerControl victim;
    float crushUntil;
    bool slowed;
    int lastShownSec = -1;

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCrushTime = FloatOptionItem.Create(RoleInfo, OptionName.DestroyerCrushTime, new(1f, 10f, 0.5f), 3f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionLeaveTrace = BooleanOptionItem.Create(RoleInfo, OptionName.DestroyerLeaveTrace, true, false);
        OptionTraceTurns = IntegerOptionItem.Create(RoleInfo, OptionName.DestroyerTraceTurns, new(1, 15, 1), 2, false, OptionLeaveTrace)
            .SetValueFormat(OptionFormat.Turns);
        OptionAllKillFlash = BooleanOptionItem.Create(RoleInfo, OptionName.DestroyerAllKillFlash, true, false);
    }

    public float CalculateKillCooldown() => killCooldown;

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsFakeSuicide) return;
        if (victim != null) { info.DoKill = false; return; }
        if (info.CheckHasGuard()) { info.IsGuard = false; return; }
        info.DoKill = false;
        Grab(info.AttemptTarget);
    }

    void Grab(PlayerControl target)
    {
        victim = target;
        crushUntil = Time.time + crushTime;
        lastShownSec = -1;
        CheckMurderPatch.TimeSinceLastKill[Player.PlayerId] = 0f;
        TownOfHostL.Modules.PlayerSpeed.Freeze(target.PlayerId, "DestroyerVictim");
        target.MarkDirtySettings();
        slowed = true;
        TownOfHostL.Modules.PlayerSpeed.Freeze(Player.PlayerId, nameof(Destroyer));
        Player.MarkDirtySettings();
        Player.SetKillCooldown(ForceProtect: true);
        StartPet();
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が {target.GetNameWithRole().RemoveHtmlTags()} をつぶし始めました", "Destroyer");
        SendRPC();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    void Release()
    {
        var v = victim;
        victim = null;
        StopPet();
        if (v != null)
        {
            TownOfHostL.Modules.PlayerSpeed.Remove(v.PlayerId, "DestroyerVictim");
            v.MarkDirtySettings();
        }
        if (slowed)
        {
            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(Destroyer));
            slowed = false;
            Player.MarkDirtySettings();
        }
        SendRPC();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    void StartPet()
    {
        try
        {
            PetActionManager.EnsureDefaultPet(Player.PlayerId);
            var pet = Player.cosmetics?.CurrentPet;
            var pos = Player.GetTruePosition();
            var petPos = pet != null ? (Vector2)pet.transform.position : pos + (Player.cosmetics.FlipX ? Vector2.left : Vector2.right) * 0.5f;
            Player.MyPhysics.RpcPet(Player.transform.position, petPos);
        }
        catch (System.Exception e) { Logger.Warn($"撫でる動きを出せませんでした: {e.Message}", "Destroyer"); }
    }

    void StopPet()
    {
        try { Player.MyPhysics.RpcCancelPet(); }
        catch (System.Exception e) { Logger.Warn($"撫でる動きを止められませんでした: {e.Message}", "Destroyer"); }
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player != Player || victim == null) return;
        if (!Player.IsAlive() || !victim.IsAlive() || GameStates.IsMeeting || GameStates.CalledMeeting) { Release(); return; }
        if (Time.time >= crushUntil)
        {
            var v = victim;
            var pos = v.GetTruePosition();
            Release();
            DeadBodyEraser.KillWithoutBody(Player, v, CustomDeathReason.Compression);
            // つぶされて死んだことを、全員の画面を光らせて知らせる
            if (allKillFlash && !v.IsAlive()) Utils.AllPlayerKillFlash();
            // 死体は残らないので、つぶした場所に痕跡を残す(設定したターン数だけ)
            if (leaveTrace && !v.IsAlive())
            {
                try { traces.Add(new DestroyerTrace(pos, traceTurns)); }
                catch (System.Exception e) { Logger.Warn($"痕跡を出せませんでした: {e.Message}", "Destroyer"); }
            }
            Player.SetKillCooldown(ForceProtect: true);
            return;
        }
        var sec = Mathf.CeilToInt(crushUntil - Time.time);
        if (sec != lastShownSec)
        {
            lastShownSec = sec;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (victim != null) Release();
    }

    // 会議が終わるたびに 1 ターン減らし、0 になった痕跡を消す(会議中に消すと暗転の原因になるので、会議の後に消す)
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        for (int i = traces.Count - 1; i >= 0; i--)
        {
            var t = traces[i];
            if (!t.IsAlive || t.PassTurn()) traces.RemoveAt(i);
        }
    }

    public override void OnDestroy()
    {
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(Destroyer));
        foreach (var t in traces) t.Remove();
        traces.Clear();
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || victim == null || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId) return "";
        return Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("DestroyerCrushing"), Mathf.Max(0, Mathf.CeilToInt(crushUntil - Time.time))));
    }

    void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(victim?.PlayerId ?? byte.MaxValue);
        sender.Writer.Write(Mathf.Max(0f, crushUntil - Time.time));
    }
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        var id = reader.ReadByte();
        victim = id == byte.MaxValue ? null : Utils.GetPlayerById(id);
        crushUntil = Time.time + reader.ReadSingle();
    }
}
