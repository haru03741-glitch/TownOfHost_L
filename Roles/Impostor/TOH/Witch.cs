using System.Collections.Generic;
using System.Text;
using UnityEngine;

using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor
{
    public sealed class Witch : RoleBase, IImpostor
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(Witch),
                player => new Witch(player),
                CustomRoles.Witch,
                () => RoleTypes.Impostor,
                CustomRoleTypes.Impostor,
                (int)Options.offsetId.ImpTOH + 600,
                SetupOptionItem,
                "ウィッチ"
            );
        public Witch(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
            Holdings.Subscribe(CustomRoleManager.EveryoneNeedsSeenNamesOthers, () => IsSpelled());
            Holdings.SubscribeShared(CustomRoleManager.MarkOthers, GetMarkOthers);
        }
        // 同じ役職が複数いる時に、1 人の作成や破棄で他の持ち主の分まで消さないよう、共有の一覧は試合の開始で空にし、破棄では自分の分だけ外す
        [TownOfHostL.Attributes.GameModuleInitializer]
        public static void Init() => Witches.Clear();
        public override void OnDestroy()
        {
            Witches.Remove(this);
            SpelledPlayer.Clear();
        }
        public static OptionItem OptionModeSwitchAction;
        enum OptionName
        {
            WitchModeSwitchAction,
        }
        public enum SwitchTrigger
        {
            TriggerKill,
            TriggerVent,
            TriggerDouble,
        };

        public bool IsSpellMode;
        private int LastVentSwitchFrame = -1;
        private int LastVentSwitchId = -1;
        public List<byte> SpelledPlayer = new();
        public SwitchTrigger NowSwitchTrigger;

        public static List<Witch> Witches = new();
        public static void SetupOptionItem()
        {
            OptionModeSwitchAction = StringOptionItem.Create(RoleInfo, OptionName.WitchModeSwitchAction, EnumHelper.GetAllNames<SwitchTrigger>(), 0, false);
        }
        public override void Add()
        {
            IsSpellMode = false;
            SpelledPlayer.Clear();
            NowSwitchTrigger = (SwitchTrigger)OptionModeSwitchAction.GetValue();
            Witches.Add(this);
            Player.AddDoubleTrigger();

        }
        public void SwitchSpellMode(bool kill)
        {
            bool needSwitch = false;
            switch (NowSwitchTrigger)
            {
                case SwitchTrigger.TriggerKill:
                    needSwitch = kill;
                    break;
                case SwitchTrigger.TriggerVent:
                    needSwitch = !kill;
                    break;
            }
            if (needSwitch)
            {
                IsSpellMode = !IsSpellMode;
                Utils.NotifyRoles(SpecifySeer: Player);
            }
        }
        public static bool IsSpelled(byte target = 255)
        {
            foreach (var witch in Witches)
            {
                if (target == 255 && witch.SpelledPlayer.Count != 0) return true;

                if (witch.SpelledPlayer.Contains(target))
                {
                    return true;
                }
            }
            return false;
        }
        public void SetSpelled(PlayerControl target)
        {
            if (!IsSpelled(target.PlayerId))
            {
                SpelledPlayer.Add(target.PlayerId);
                //キルクールの適正化
                Player.SetKillCooldown();
            }
        }
        public void OnCheckMurderAsKiller(MurderInfo info)
        {
            if (!info.CanKill) return;

            var (killer, target) = info.AttemptTuple;
            if (NowSwitchTrigger == SwitchTrigger.TriggerDouble)
            {
                info.DoKill = killer.CheckDoubleTrigger(target, () => { SetSpelled(target); });
            }
            else
            {
                if (IsSpellMode)
                {//呪いならキルしない
                    info.DoKill = false;
                    SetSpelled(target);
                }
                SwitchSpellMode(true);
            }
            //切れない相手ならキルキャンセル
            info.DoKill &= info.CanKill;
        }
        public override void AfterMeetingTasks()
        {
            if (Player.IsAlive() || MyState.DeathReason != CustomDeathReason.Vote)
            {//吊られなかった時呪いキル発動
                var spelledIdList = new List<byte>();
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    if (SpelledPlayer.Contains(pc.PlayerId) && !Main.AfterMeetingDeathPlayers.ContainsKey(pc.PlayerId))
                    {
                        pc.SetRealKiller(Player);
                        spelledIdList.Add(pc.PlayerId);
                    }
                }
                MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Spell, spelledIdList.ToArray());
            }
            //実行してもしなくても呪いはすべて解除
            SpelledPlayer.Clear();
        }
        public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
        {
            seen ??= seer;
            if (isForMeeting && IsSpelled(seen.PlayerId))
            {
                return Utils.ColorString(Palette.ImpostorRed, "†");
            }
            return "";
        }
        public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
        {
            seen ??= seer;
            if (!Is(seen) || isForMeeting) return "";

            var sb = new StringBuilder();
            sb.Append(isForHud ? GetString("WitchCurrentMode") : "Mode:");
            if (NowSwitchTrigger == SwitchTrigger.TriggerDouble)
            {
                sb.Append(GetString("WitchModeDouble"));
            }
            else
            {
                sb.Append(IsSpellMode ? GetString("WitchModeSpell") : GetString("WitchModeKill"));
            }
            return sb.ToString();
        }
        public bool OverrideKillButtonText(out string text)
        {
            if (NowSwitchTrigger != SwitchTrigger.TriggerDouble && IsSpellMode)
            {
                text = GetString("WitchSpellButtonText");
                return true;
            }
            text = default;
            return false;
        }
        public override bool OnEnterVent(PlayerPhysics physics, int ventId)
        {
            if (NowSwitchTrigger is SwitchTrigger.TriggerVent)
            {                
                if (LastVentSwitchFrame == Time.frameCount && LastVentSwitchId == ventId)
                    return true;

                LastVentSwitchFrame = Time.frameCount;
                LastVentSwitchId = ventId;
                SwitchSpellMode(false);
            }
            return true;
        }
    }
}