using System;
using System.Collections.Generic;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHostL
{
    /// <summary>
    /// キルボタンの 1 回押しと 2 回押しで能力を分ける (ホストのみ)。
    /// 使い方は 2 つ: 役職のキルチェックの中で <see cref="CheckDoubleTrigger"/> を呼ぶ形と、
    /// <see cref="IDoubleTrigger"/> を持たせてキルチェックの前に <see cref="OnCheckMurderAsKiller"/> で振り分ける形。
    /// どちらも AddDoubleTrigger で登録し、2 回目を待つ長さは設定 DoubleTriggerThreshold
    /// </summary>
    static partial class DoubleTrigger
    {
        sealed class Pending
        {
            public byte Target;
            public float Timer;
            public Action SingleAction;
        }

        static readonly HashSet<byte> players = new();
        static readonly Dictionary<byte, Pending> pending = new();

        static float Window => Options.DoubleTriggerThreshold?.GetFloat() ?? 1f;

        [GameModuleInitializer]
        public static void Init()
        {
            players.Clear();
            pending.Clear();
        }
        public static void AddDoubleTrigger(this PlayerControl killer) => players.Add(killer.PlayerId);
        public static bool CanDoubleTrigger(this PlayerControl killer) => players.Contains(killer.PlayerId);

        /// <summary>1 回目の押しで false (firstAction は待ち時間の後に動く)、待ち時間の内に同じ相手へ 2 回目を押すと true</summary>
        public static bool CheckDoubleTrigger(this PlayerControl killer, PlayerControl target, Action firstAction)
        {
            if (pending.TryGetValue(killer.PlayerId, out var p))
            {
                //2回目がターゲットずれてたら最初の相手にシングルアクション
                if (p.Target != target.PlayerId) return false;
                Logger.Info($"{killer.name} DoDoubleAction", "DoubleTrigger");
                pending.Remove(killer.PlayerId);
                return true;
            }
            Begin(killer, target, firstAction);
            return false;
        }

        /// <summary>
        /// IDoubleTrigger の役職のキルチェックを振り分ける。振り分けた時 true (役職の OnCheckMurderAsKiller は呼ばない)。
        /// 2 回目は最初に押した相手への 2 回押しとして扱う
        /// </summary>
        public static bool OnCheckMurderAsKiller(MurderInfo info)
        {
            var (killer, target) = info.AttemptTuple;
            if (!killer.CanDoubleTrigger()) return false;
            if (killer.GetRoleClass() is not IDoubleTrigger role || !role.CheckAction) return false;

            if (pending.Remove(killer.PlayerId, out var p))
            {
                var first = Utils.GetPlayerById(p.Target) ?? target;
                Logger.Info($"{killer.name} DoDoubleAction to {first.name} [{Window - p.Timer}s]", "DoubleTrigger");
                info.DoKill = role.DoubleAction(killer, first);
            }
            else
            {
                Begin(killer, target, () => role.SingleAction(killer, target));
                info.DoKill = false;
            }
            return true;
        }

        static void Begin(PlayerControl killer, PlayerControl target, Action singleAction)
        {
            //シングルアクション時はキル間隔を無視
            CheckMurderPatch.TimeSinceLastKill.Remove(killer.PlayerId);
            pending[killer.PlayerId] = new Pending { Target = target.PlayerId, Timer = Window, SingleAction = singleAction };
        }

        public static void OnFixedUpdate(PlayerControl player)
        {
            if (pending.Count == 0) return;
            if (!GameStates.IsInTask)
            {
                pending.Clear();
                return;
            }

            if (!pending.TryGetValue(player.PlayerId, out var p)) return;
            p.Timer -= Time.fixedDeltaTime;
            if (p.Timer > 0) return;
            Logger.Info($"{player.name} DoSingleAction", "DoubleTrigger");
            pending.Remove(player.PlayerId);
            p.SingleAction();
        }
    }
}
