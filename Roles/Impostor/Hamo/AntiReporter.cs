using AmongUs.GameOptions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Hazel;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

public sealed class AntiReporter : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(AntiReporter),
            player => new AntiReporter(player),
            CustomRoles.AntiReporter,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 2300,
            SetupOptionItem,
            "anr"
        );
    public AntiReporter(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Brokens = [];
        ReportCrashTimers.Clear();
        Cooldown = OptionColldown.GetFloat();
        Use = OptionMax.GetInt();
        AntiReporterResetMeeting = OptionAntiReporterResetMeeting.GetBool();
        AntiReporterResetse = OptionAntiReporterResetse.GetFloat();
    }
    Dictionary<byte, float> ReportCrashTimers = new(14);
    static OptionItem OptionColldown;
    static OptionItem OptionMax;
    static OptionItem OptionAntiReporterResetMeeting;
    static OptionItem OptionAntiReporterResetse;
    enum OptionName
    {
        Cooldown,
        AntiReporterMaximum,
        AntiReporterResetMeeting,
        AntiReporterResetse
    }
    static float Cooldown;
    static int Use;
    static bool AntiReporterResetMeeting;
    static float AntiReporterResetse;
    private static void SetupOptionItem()
    {
        OptionColldown = FloatOptionItem.Create(RoleInfo, OptionName.Cooldown, new(1f, 1000f, 1f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMax = IntegerOptionItem.Create(RoleInfo, OptionName.AntiReporterMaximum, new(1, 1000, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionAntiReporterResetMeeting = BooleanOptionItem.Create(RoleInfo, OptionName.AntiReporterResetMeeting, true, false);
        OptionAntiReporterResetse = IntegerOptionItem.Create(RoleInfo, OptionName.AntiReporterResetse, new(0, 999, 1), 20, false)
            .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity);
    }
    private void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(Use);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        Use = reader.ReadInt32();
    }
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;
        var target = Player.GetKillTarget(true);
        if (target == null) return;
        if (!CanUseAbilityButton() || ReportCrashTimers.ContainsKey(target.PlayerId)) return;
        ResetCooldown = true;
        ReportCrashTimers.Add(target.PlayerId, 0f);
        Use--;
        Player.SetKillCooldown(ForceProtect: true, target: target);
        Logger.Info($"{target.Data?.PlayerName}のメガホンワンクリックだから間違えて壊しちゃった☆ ﾃﾍｯ", "AntiReporter");
        SendRPC();
        Brokens.Add(target.PlayerId);
        Utils.NotifyRoles(SpecifySeer: Player);
    }
    public override string GetProgressText(bool comms = false) => Utils.ColorString(Use > 0 ? Color.red : Color.gray, $"({Use})");
    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportReason reportreson)
    {
        if (ReportCrashTimers.ContainsKey(reporter.PlayerId))
        {
            reportreson = DontReportReason.Impostor;
            return true;
        }
        return false;
    }
    public override string GetAbilityButtonText()
    {
        return AntiReporterResetse == 0 ? GetString("DestroyButtonText") : GetString("DisableButtonText");
    }

    public override void OnStartMeeting()
    {
        if (AntiReporterResetMeeting == true) ReportCrashTimers.Clear();
    }
    public override bool CanUseAbilityButton() => Use > 0;
    bool IUsePhantomButton.IsPhantomRole => Use > 0;
    bool IUsePhantomButton.IsresetAfterKill => false;
    public override void OnFixedUpdate(PlayerControl _)
    {
        if (!AmongUsClient.Instance.AmHost || AntiReporterResetse == 0) return;

        foreach (var (targetId, timer) in ReportCrashTimers.ToArray())
        {
            if (timer >= AntiReporterResetse)
            {
                ReportCrashTimers.Remove(targetId);
            }
            else
            {
                ReportCrashTimers[targetId] += Time.fixedDeltaTime;
            }
        }
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = Use > 0 ? Cooldown : 200f;
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !(Use > 0) || !Player.IsAlive()) return "";

        if (isForHud) return GetString("PhantomButtonKilltargetLowertext");
        return $"<size=50%>{GetString("PhantomButtonKilltargetLowertext")}</size>";
    }
    ICollection<byte> Brokens = [];
    public override void CheckWinner(GameOverReason reason)
    {
        var count = OptionMax.GetInt() - Use;
        if (0 < count)
        {
        }
        if (Brokens.Any(x => 3 <= Brokens.Count(b => b == x)))
        {
        }
    }

}