using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Roles.Unit.JackOLantern;

namespace TownOfHostL.Roles.Impostor;
public sealed class Jack : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Jack),
            player => new Jack(player),
            CustomRoles.Jack,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.UnitMix + 100,//使用しない
            null,
            "ジャック",
            flags: RoleFlags.DontShowOption
        );
    public Jack(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        killCooldown = OptionJackKillCooldown.GetFloat();
        roleRemoveKillMaxCount = OptionJackRoleRemoveKillCount.GetInt();

        removeKillLimit = roleRemoveKillMaxCount;
        SentPlayer = new(15);

        jack = Player;
    }

    static float killCooldown;
    static int roleRemoveKillMaxCount;

    int removeKillLimit;

    // 試合を跨いで前の試合のプレイヤーを指さないようにする
    public override void OnDestroy()
    {
        jack = null;
    }
    public override void Add()
    {
        Player.AddDoubleTrigger();
    }
    public float CalculateKillCooldown() => killCooldown;

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        // 使用回数がない時は通常キル
        if (!info.CanKill || removeKillLimit == 0) return;

        var (killer, target) = info.AttemptTuple;
        info.DoKill = killer.CheckDoubleTrigger(target, () =>
        {
            target.SetRealKiller(killer);
            killer.RpcMurderPlayer(target, true);

            removeKillLimit--;
            Logger.Info($"{killer.GetNameWithRole()}：役職消去キル残り{removeKillLimit}回", "Jack");
            // 表示更新
            Utils.NotifyRoles(SpecifySeer: killer);

            // クルー陣営なら役職消去
            if (target.Is(CustomRoleTypes.Crewmate))
            {
                Logger.Info($"{killer.GetNameWithRole()}：役職消去→{target.GetNameWithRole()}", "Jack");
                target.RpcSetCustomRole(CustomRoles.Crewmate);

                // 表示更新
                Utils.NotifyRoles(SpecifySeer: target);
            }
        });
    }

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
    {
        if (SentPlayer.Contains(seen.PlayerId))
        {
            view.Enabled = true;
            view.Prefix = Utils.ColorString(Utils.GetRoleColor(CustomRoles.JackOLantern),"◆") + view.Prefix;
        }
    }
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(removeKillLimit > 0 ? RoleInfo.RoleColor : Color.gray, $"[{removeKillLimit}]");
}
