using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class CursedWolf : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(CursedWolf),
            player => new CursedWolf(player),
            CustomRoles.CursedWolf,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 200,
            SetupOptionItem,
            "呪狼"
        );
    public CursedWolf(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        GuardSpellTimes = OptionGuardSpellTimes.GetInt();
        nowKillMotion = (KillMotionOption)OptionKillMotion.GetValue();
    }
    public static OptionItem OptionKillMotion;
    private static OptionItem OptionGuardSpellTimes;
    enum OptionName
    {
        CursedWolfGuardSpellMotion,
        CursedWolfGuardSpellTimes,
    }
    enum KillMotionOption
    {
        MotionKill,
        MotionSuicide,
    };
    KillMotionOption nowKillMotion;

    private static int GuardSpellTimes;
    int SpellCount;

    public static void SetupOptionItem()
    {
        OptionKillMotion = StringOptionItem.Create(RoleInfo, OptionName.CursedWolfGuardSpellMotion, EnumHelper.GetAllNames<KillMotionOption>(), 0, false);
        OptionGuardSpellTimes = IntegerOptionItem.Create(RoleInfo, OptionName.CursedWolfGuardSpellTimes, new(1, 15, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
    }
    public override void Add()
    {
        var playerId = Player.PlayerId;

        SpellCount = GuardSpellTimes;
        Logger.Info($"{Utils.GetPlayerById(playerId)?.GetNameWithRole()} : 残り{SpellCount}回", "CursedWolf");
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;
        // 直接キル出来る役職チェック
        if (killer.GetCustomRole().IsDirectKillRole()) return true;
        if (SpellCount <= 0) return true;

        // ガード
        killer.RpcProtectedMurderPlayer(target);
        target.RpcProtectedMurderPlayer(target);
        SpellCount--;
        Logger.Info($"{target.GetNameWithRole()} : 残り{SpellCount}回", "CursedWolf");

        //切り返す
        PlayerState.GetByPlayerId(killer.PlayerId).DeathReason = CustomDeathReason.Spell;
        switch (nowKillMotion)
        {
            case KillMotionOption.MotionKill://自身がキル
                Kill.Force(target, killer);
                break;
            case KillMotionOption.MotionSuicide://相手が自爆
                Kill.Suicide(killer);
                break;
        }
        // 呪殺した後のキルクールは通常の半分
        target.ResetKillCooldown();
        target.SetKillCooldown(Main.AllPlayerKillCooldown[target.PlayerId] / 2f);
        // 自身は斬られない
        info.CanKill = false;
        return true;
    }
    public override string GetProgressText(bool comms = false) => Utils.ColorString(Palette.ImpostorRed, $"〔{SpellCount}〕");
}