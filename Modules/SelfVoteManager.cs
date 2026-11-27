using System.Collections.Generic;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Madmate;
using static TownOfHostL.Modules.MeetingVoteManager;

namespace TownOfHostL.Modules
{
    public static class SelfVoteManager
    {
        ///<summary>
        ///MeetingVoteManagerのSkip
        ///</summary>
        public static byte SkipId = Skip;
        public static Dictionary<byte, bool> CheckVote = new();
        [Attributes.GameModuleInitializer]
        public static void Init()
        {
            CheckVote.Clear();
            RoomTaskAssign.AllRoomTasker.Clear();
        }
        public static void AddSelfVotes(PlayerControl player)
        {
            CheckVote.TryAdd(player.PlayerId, false);
        }

        public enum VoteStatus
        {
            Skip,
            Self,
            Vote,
        }
        ///<summary>
        /// 自投票モードのチェック
        ///</summary>
        ///<returns>自投票モードならtrueを返す</returns>
        /// <param name="status">投票のステータスを返す</param>
        public static bool CheckSelfVoteMode(PlayerControl player, byte id, out VoteStatus status)
        {
            Check(player);
            var mode = CheckVote[player.PlayerId];
            if (player.PlayerId == id)
            {
                status = VoteStatus.Self;
                CheckVote[player.PlayerId] = !mode;
                mode = !mode;
            }
            else if (Skip == id)
                status = VoteStatus.Skip;
            else
                status = VoteStatus.Vote;
            Logger.Info($"player: {Main.AllPlayerNames[player.PlayerId]} mode: {mode} status: {status}", "SelfVoteManager");
            return mode;
        }

        private static void Check(PlayerControl player)
        {
            if (!CheckVote.ContainsKey(player.PlayerId))
            {
                AddSelfVotes(player);
                Logger.Info($"×チェックに失敗 {player.PlayerId}を追加しました", "SelfVoteManager");
            }
        }

        public static void SetMode(PlayerControl player, bool mode)
            => CheckVote[player.PlayerId] = mode;

        public static bool Canuseability()
        {
            if (FirstTurnMeeting.CantAbilityNow) return false;
            if (MadAvenger.Skill) return false;
            if (Assassin.NowUse) return false;

            return true;
        }

        public enum PickResult
        {
            /// <summary>能力と関係ない (ふつうの投票として通す)</summary>
            Pass,
            /// <summary>モードの切り替えだけ (この投票は取り消す)</summary>
            Cancel,
            /// <summary>能力の相手を選んだ (能力を使い、この投票は取り消す)</summary>
            Pick,
        }

        /// <summary>
        /// 自投票で能力のモードに入り、次に誰かへ投票すると相手を選ぶ。CheckAnyVote で、投票したのが本人の時に呼ぶ。
        /// モード中に飛ばすと投票のモードへ戻る。モードでない時の投票はふつうの投票
        /// </summary>
        public static PickResult SelfVotePick(PlayerControl player, byte votedForId, string modeKey, string voteKey)
        {
            if (!CheckSelfVoteMode(player, votedForId, out var status)) return PickResult.Pass;
            switch (status)
            {
                case VoteStatus.Self:
                    Utils.SendMessage(string.Format(Translator.GetString("SkillMode"), Translator.GetString(modeKey), Translator.GetString(voteKey)) + Translator.GetString("VoteSkillMode"), player.PlayerId);
                    SetMode(player, true);
                    return PickResult.Cancel;
                case VoteStatus.Skip:
                    Utils.SendMessage(Translator.GetString("VoteSkillFin"), player.PlayerId);
                    SetMode(player, false);
                    return PickResult.Cancel;
                default:
                    SetMode(player, false);
                    return PickResult.Pick;
            }
        }

        // 前の会議で能力のモードに入ったまま終わっても、次の会議はふつうの投票から始める
        [HarmonyLib.HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
        static class ResetModePatch
        {
            public static void Prefix()
            {
                foreach (var id in new List<byte>(CheckVote.Keys)) CheckVote[id] = false;
            }
        }

        public enum AbilityVoteMode
        {
            NomalVote,
            SelfVote,
        }
    }
}