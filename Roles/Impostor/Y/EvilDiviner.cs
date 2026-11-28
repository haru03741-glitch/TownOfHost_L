using AmongUs.GameOptions;
using System.Collections.Generic;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class EvilDiviner : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(EvilDiviner),
            player => new EvilDiviner(player),
            CustomRoles.EvilDiviner,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 600,
            SetupOptionItem,
            "イビルディバイナー"
        );
    public EvilDiviner(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        DivinationMaxCount = OptionDivinationMaxCount.GetInt();
    }
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionDivinationMaxCount;
    enum OptionName
    {
        EvilDivinerDivinationMaxCount,
    }
    private static float KillCooldown;
    private static int DivinationMaxCount;

    // 1 人ごとに持つ (複数人いる時に回数と占った相手が混ざらないように)
    int divinationCount;
    readonly HashSet<byte> divinationTargets = new();

    public static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDivinationMaxCount = IntegerOptionItem.Create(RoleInfo, OptionName.EvilDivinerDivinationMaxCount, new(1, 15, 1), 5, false)
            .SetValueFormat(OptionFormat.Times);
    }
    public override void Add()
    {
        divinationCount = DivinationMaxCount;
        Player.AddDoubleTrigger();
    }

    public float CalculateKillCooldown() => KillCooldown;
    public override string GetProgressText(bool comms = false) => Utils.ColorString(divinationCount > 0 ? Palette.ImpostorRed : Color.gray, $"[{divinationCount}]");

    public override bool KnowsRoleOf(PlayerControl seen, bool isMeeting) => divinationTargets.Contains(seen.PlayerId) && Player.IsAlive();

    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;
        if (divinationCount > 0)
        {
            return killer.CheckDoubleTrigger(target, () => { SetDivination(killer, target); });
        }
        else return true;
    }
    void SetDivination(PlayerControl killer, PlayerControl target)
    {
        // 1 回押しの待ちの間にどちらかが死んだ・抜けた時は占わない (回数を使わない)
        if (!killer.IsAlive() || !target.IsAlive()) return;
        if (divinationTargets.Add(target.PlayerId))
        {
            divinationCount--;
            Logger.Info($"{killer.GetNameWithRole()}：占った 占い先→{target.GetNameWithRole()} || 残り{divinationCount}回", "EvilDiviner");
            Utils.NotifyRoles(SpecifySeer: killer);

            //キルクールの適正化
            killer.SetKillCooldown();
        }
    }
}