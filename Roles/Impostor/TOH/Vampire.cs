using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor
{
    public sealed class Vampire : RoleBase, IImpostor
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(Vampire),
                player => new Vampire(player),
                CustomRoles.Vampire,
                () => RoleTypes.Impostor,
                CustomRoleTypes.Impostor,
                (int)Options.offsetId.ImpTOH + 400,
                SetupOptionItem,
                "ヴァンパイア",
                introSound: () => GetIntroSound(RoleTypes.Shapeshifter)
            );
        public Vampire(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
            KillDelay = OptionKillDelay.GetFloat();

            BittenPlayers.Clear();
        }

        static OptionItem OptionKillDelay;
        enum OptionName
        {
            VampireKillDelay
        }

        static float KillDelay;

        Dictionary<byte, float> BittenPlayers = new(14);

        private static void SetupOptionItem()
        {
            OptionKillDelay = FloatOptionItem.Create(RoleInfo, OptionName.VampireKillDelay, new(1f, 1000f, 1f), 10f, false)
                .SetValueFormat(OptionFormat.Seconds);
        }
        public void OnCheckMurderAsKiller(MurderInfo info)
        {
            // キル出来ない相手・守りで止まるキルは噛まない (噛んだ後の死は守りを通らないので、守りが働いても相手が死んでしまう)
            if (!info.CanKill || info.CheckHasGuard()) return;
            var (killer, target) = info.AttemptTuple;

            if (target.Is(CustomRoles.Bait)) return;
            if (info.IsFakeSuicide) return;

            //誰かに噛まれていなければ登録
            if (!BittenPlayers.ContainsKey(target.PlayerId))
            {
                killer.SetKillCooldown(Main.LastKillCooldown.Value);
                BittenPlayers.Add(target.PlayerId, 0f);
                // プレイヤーがプラットフォームに乗ることを防ぐ
                PlayerState.GetByPlayerId(target.PlayerId).CanUseMovingPlatform = false;
            }
            info.DoKill = false;
        }
        public override void OnFixedUpdate(PlayerControl _)
        {
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask) return;

            foreach (var (targetId, timer) in BittenPlayers.ToArray())
            {
                if (timer >= KillDelay)
                {
                    var target = Utils.GetPlayerById(targetId);
                    KillBitten(target);
                    BittenPlayers.Remove(targetId);
                }
                else
                {
                    BittenPlayers[targetId] += Time.fixedDeltaTime;
                }
            }
        }
        public override void OnDestroy()
        {
            // 噛んだ人が死ぬ前に役職が替わった時は、ヴァンパイアの死後と同じく噛まれた本人の死として起こす
            // (残すと、噛まれた人が死なないまま動く床にも乗れなくなる)。試合の終わりの後始末では起こさない
            if (BittenPlayers.Count == 0 || !GameStates.IsInTask || GameEndChecker.EndingStarted) return;
            var vampireId = Player.PlayerId;
            var bitten = new List<byte>(BittenPlayers.Keys);
            BittenPlayers.Clear();
            // 役職の替わりの途中でキルを送らないよう、次のフレームで起こす
            _ = new LateTask(() => KillBittenAfterRoleChange(vampireId, bitten), 0f, "Vampire.BittenAfterRoleChange");
        }
        private static void KillBittenAfterRoleChange(byte vampireId, List<byte> bitten)
        {
            if (!GameStates.IsInGame || GameEndChecker.EndingStarted) return;
            // 会議の窓の間は客へ死を送らないので、明けるまで待つ (試合が閉じれば遅らせた処理ごと捨てられる)
            if (GameStates.InMeetingWindow)
            {
                _ = new LateTask(() => KillBittenAfterRoleChange(vampireId, bitten), 1f, "Vampire.BittenAfterRoleChange");
                return;
            }
            var vampire = Utils.GetPlayerById(vampireId);
            foreach (var targetId in bitten)
            {
                var target = Utils.GetPlayerById(targetId);
                if (target == null) continue;
                PlayerState.GetByPlayerId(targetId).CanUseMovingPlatform = true;
                if (!target.IsAlive()) continue;
                PlayerState.GetByPlayerId(targetId).DeathReason = CustomDeathReason.Bite;
                if (vampire != null) target.SetRealKiller(vampire);
                Kill.Suicide(target);
            }
        }
        public override void OnReportDeadBody(PlayerControl _, NetworkedPlayerInfo __)
        {
            foreach (var targetId in BittenPlayers.Keys)
            {
                var target = Utils.GetPlayerById(targetId);
                KillBitten(target, true);
            }
            BittenPlayers.Clear();
        }
        public bool OverrideKillButtonText(out string text)
        {
            text = GetString("VampireBiteButtonText");
            return true;
        }

        private void KillBitten(PlayerControl target, bool isButton = false)
        {
            var vampire = Player;
            // 噛まれた人が抜けた時は何もしない (null のまま下へ進むと、毎フレーム例外になる)
            if (target == null) return;
            if (target.IsAlive())
            {

                // 梯子の上の人と、ヴァンパイアがもう死んでいる時 (殺し手が死んでいるキルの判定は取り消される) は、噛まれた本人の死として起こす
                if (target.MyPhysics.Animations.IsPlayingAnyLadderAnimation() || !vampire.IsAlive())
                {
                    PlayerState.GetByPlayerId(target.PlayerId).DeathReason = CustomDeathReason.Bite;
                    target.SetRealKiller(vampire);
                    Kill.Suicide(target);
                    Logger.Info($"Vampireに噛まれている{target.name}を梯子の上かヴァンパイアの死後にキルしました。", "Vampire.KillBitten");
                }
                else
                {

                    if (CustomRoleManager.OnCheckMurder(
                        vampire, target,
                        target, target, false, deathReason: CustomDeathReason.Bite))
                    {
                        target.SetRealKiller(vampire);
                        Logger.Info($"Vampireに噛まれている{target.name}を自爆させました。", "Vampire.KillBitten");
                        if (!isButton && vampire.IsAlive())
                        {
                            RPC.PlaySound(vampire.PlayerId, Sounds.KillSound);
                        }
                    }
                    else
                    {
                        Logger.Info($"Vampireに噛まれている{target.name}はキルされませんでした。", "Vampire.KillBitten");
                    }
                }
            }
            else
            {
                Logger.Info($"Vampireに噛まれている{target.name}はすでに死んでいました。", "Vampire.KillBitten");
            }
        }
    }
}