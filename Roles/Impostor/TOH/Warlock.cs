using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

public sealed class Warlock : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Warlock),
            player => new Warlock(player),
            CustomRoles.Warlock,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpTOH + 500,
            null,
            "ウォーロック"
        );
    public Warlock(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
    public override void OnDestroy()
    {
        CursedPlayer = null;
    }

    PlayerControl CursedPlayer;
    bool IsCursed;
    bool Shapeshifting;
    public override void Add()
    {
        CursedPlayer = null;
        IsCursed = false;
        Shapeshifting = false;
    }
    public bool OverrideKillButtonText(out string text)
    {
        if (!Shapeshifting)
        {
            text = GetString("WarlockCurseButtonText");
            return true;
        }
        else
        {
            text = default;
            return false;
        }
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = IsCursed ? 1f : Options.DefaultKillCooldown;
        AURoleOptions.ShapeshifterDuration = 1f;
    }
    // 呪いはキルでないので、守りより前に振り分ける (呪った人の守りを使わせない。守りは呪いで殺される人で通す)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        //自殺なら関係ない・変身中は通常キル
        if (info.IsSuicide || Shapeshifting) return true;

        var (killer, target) = info.AttemptTuple;
        if (!IsCursed)
        {//まだ呪っていない
            IsCursed = true;
            CursedPlayer = target;
            //呪える相手は一人だけなのでキルボタン無効化
            killer.SetKillCooldown(255f);
            // 変身のクールを 1 秒にした設定が届いてから数え直す
            killer.RpcResetAbilityCooldown(sync: true);
        }
        //どちらにしてもキルは無効
        return false;
    }
    public override void OnShapeshift(PlayerControl target)
    {
        Shapeshifting = !Is(target);

        if (!AmongUsClient.Instance.AmHost) return;

        if (Shapeshifting)
        {///変身時
            if (CursedPlayer != null && CursedPlayer.IsAlive())
            {//呪っていて対象がまだ生きていたら
                Vector2 cpPos = CursedPlayer.transform.position;
                Dictionary<PlayerControl, float> candidateList = new();
                float distance;
                foreach (PlayerControl candidatePC in Main.AllAlivePlayerControls)
                {
                    if (candidatePC != CursedPlayer)
                    {
                        distance = Vector2.Distance(cpPos, candidatePC.transform.position);
                        candidateList.Add(candidatePC, distance);
                        Logger.Info($"{candidatePC?.Data?.PlayerName}の位置{distance}", "Warlock");
                    }
                }
                var nearest = candidateList.OrderBy(c => c.Value).FirstOrDefault();
                var killTarget = nearest.Key;
                // 呪いのキルも、殺される人の守りを通す
                if (killTarget != null && Kill.Attempt(CursedPlayer, killTarget, realKiller: Player))
                    Logger.Info($"{killTarget.GetNameWithRole()}was killed", "Warlock");
                Player.ResetKillCooldown();
                CursedPlayer = null;
            }
            else if (CursedPlayer != null)
            {
                // 呪った相手が先に死んだ・抜けた時も、呪いで止めたキルボタンを戻す (戻さないと会議まで 255 秒のまま)
                Player.ResetKillCooldown();
                CursedPlayer = null;
            }
        }
        else
        {
            if (IsCursed)
            {
                //ShapeshifterCooldownを通常に戻す
                IsCursed = false;
                Player.RpcResetAbilityCooldown(sync: true);
            }
        }
    }
    public override void AfterMeetingTasks()
    {
        CursedPlayer = null;
        IsCursed = false;
    }
}