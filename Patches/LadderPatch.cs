using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL
{
    public class FallFromLadder
    {
        public static Dictionary<byte, Vector3> TargetLadderData;
        private static int Chance => (Options.LadderDeathChance as StringOptionItem).GetChance();
        [GameModuleInitializer]
        public static void Reset()
        {
            TargetLadderData = new();
        }
        public static void OnClimbLadder(PlayerPhysics player, Ladder source)
        {
            if (!Options.LadderDeath.GetBool()) return;
            var sourcePos = source.transform.position;
            var targetPos = source.Destination.transform.position;
            //降りているのかを検知
            if (sourcePos.y > targetPos.y)
            {
                int chance = IRandom.Instance.Next(1, 101);
                if (chance <= Chance)
                {
                    TargetLadderData[player.myPlayer.PlayerId] = targetPos;
                }
            }
        }
        public static void FixedUpdate(PlayerControl player)
        {
            if (player.Data.Disconnected) return;
            if (TargetLadderData.ContainsKey(player.PlayerId))
            {
                if (Vector2.Distance(TargetLadderData[player.PlayerId], player.transform.position) < 0.5f)
                {
                    if (player.Data.IsDead) return;
                    // ホストの追放の締めから暗転対策の戻しまでは死を送らない (本編の客の追放の締めの頭数の判定より先に届くと暗転しうる)
                    if (AntiBlackout.InRevertWindow) return;
                    //LateTaskを入れるため、先に死亡判定を入れておく
                    player.Data.IsDead = true;
                    _ = new LateTask(() =>
                    {
                        Vector2 targetPos = (Vector2)TargetLadderData[player.PlayerId] + new Vector2(0.1f, 0f);
                        ushort num = (ushort)(NetHelpers.XRange.ReverseLerp(targetPos.x) * 65535f);
                        ushort num2 = (ushort)(NetHelpers.YRange.ReverseLerp(targetPos.y) * 65535f);
                        CustomRpcSender sender = CustomRpcSender.Create("LadderFallRpc", sendOption: Hazel.SendOption.None);
                        sender.AutoStartRpc(player.NetTransform.NetId, (byte)RpcCalls.SnapTo)
                                .Write(num)
                                .Write(num2)
                                .Write((ushort)(player.NetTransform.lastSequenceId + 8)) // 本編の SnapTo は位置の後に sequenceId (ushort) を読む
                        .EndRpc();
                        sender.AutoStartRpc(player.NetId, (byte)RpcCalls.MurderPlayer)
                                .WriteNetObject(player)
                                .Write((int)ExtendedPlayerControl.SucceededFlags)
                        .EndRpc();
                        sender.SendMessage();
                        player.NetTransform.SnapTo(targetPos);
                        var state = PlayerState.GetByPlayerId(player.PlayerId);
                        state.DeathReason = CustomDeathReason.Fall;
                        player.MurderPlayer(player);
                        state.SetDead(CustomDeathReason.Fall);
                    }, 0.30f, "LadderFallTask");
                }
            }
        }
    }
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.ClimbLadder))]
    class LadderPatch
    {
        public static void Postfix(PlayerPhysics __instance, Ladder source, byte climbLadderSid)
        {
            FallFromLadder.OnClimbLadder(__instance, source);
            Roles.Neutral.Spelunker.OnLadderClimbed(__instance.myPlayer);
        }
    }
}