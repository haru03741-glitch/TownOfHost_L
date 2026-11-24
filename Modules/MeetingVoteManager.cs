using System;
using System.Linq;
using System.Collections.Generic;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Madmate;
using TownOfHostL.Roles.Crewmate;
using System.Diagnostics.Metrics;
using UnityEngine;

using TownOfHostL.Core.Modes;

namespace TownOfHostL.Modules;

public class MeetingVoteManager
{
    public IReadOnlyDictionary<byte, VoteData> AllVotes => allVotes;
    private Dictionary<byte, VoteData> allVotes = new(15);
    private readonly MeetingHud meetingHud;

    public static MeetingVoteManager Instance => _instance;
    private static MeetingVoteManager _instance;
    private static LogHandler logger = Logger.Handler(nameof(MeetingVoteManager));

    private MeetingVoteManager()
    {
        meetingHud = MeetingHud.Instance;
        ClearVotes();
    }

    public static void Start()
    {
        _instance = new();
        TieBreaker.OnStartMeeting();
    }

    /// 投票を初期状態にします
    public void ClearVotes()
    {
        foreach (var voteArea in meetingHud.playerStates)
        {
            byte playerId = voteArea.PlayerId;
            allVotes[playerId] = new(playerId);
        }
    }
    /// 今までに行われた投票をすべて削除し，特定の投票先に1票投じられた状態で会議を強制終了します
    /// <param name="voter">投票を行う人</param>
    /// <param name="exiled">追放先</param>
    public void ClearAndExile(byte voter, byte exiled, bool isAntiComp = false)
    {
        logger.Info($"{Utils.GetPlayerById(voter).GetNameWithRole()} によって {GetVoteName(exiled)} が追放されます");
        ClearVotes();
        var vote = new VoteData(voter);
        vote.DoVote(exiled, 1);
        allVotes[voter] = vote;
        byte antiCompId = isAntiComp ? voter : byte.MaxValue;
        RoleHooks.InClearAndExile = true;
        try { EndMeeting(false, antiCompId); }
        finally { RoleHooks.InClearAndExile = false; }
    }
    /// 今までの投票をすべて消し，複数人が同じ相手へ 1 票ずつ投じた状態で会議を終える
    public void ClearAndExiles(byte[] voters, byte exiled)
    {
        logger.Info($"ClearAndExilesにより、{GetVoteName(exiled)} が追放されます");
        ClearVotes();
        foreach (var voter in voters)
        {
            var vote = new VoteData(voter);
            vote.DoVote(exiled, 1);
            allVotes[voter] = vote;
        }
        RoleHooks.InClearAndExile = true;
        try { EndMeeting(false); }
        finally { RoleHooks.InClearAndExile = false; }
    }
    /// <summary>投票を全部消して会議を終える</summary>
    public void ClearAndEndMeeting()
    {
        logger.Info("削除して会議を終了します");
        ClearVotes();
        RoleHooks.InClearAndExile = true;
        try { EndMeeting(false); }
        finally { RoleHooks.InClearAndExile = false; }
    }
    /// <summary>会議の結果として出す文 (役職が書き換える)</summary>
    public static string Voteresult = "";
    /// 投票を行います．投票者が既に投票している場合は票を上書きします
    /// <param name="voter">投票者</param>
    /// <param name="voteFor">投票先</param>
    /// <param name="numVotes">票数</param>
    /// <param name="isIntentional">投票者自身の投票操作による自発的な投票かどうか</param>
    /// <param name="judgeExiledId">ジャッジの裁決で実際に追放される人。byte.MaxValueなら裁決ではない</param>
    /// <param name="judgeNonce">ジャッジの裁決の Nonce</param>
    public void SetVote(byte voter, byte voteFor, int numVotes = 1, bool isIntentional = true,
                        byte judgeExiledId = byte.MaxValue, ushort judgeNonce = 0, bool isoverride = true)
    {
        // 締めを待たせている間 (役職がその場で決めた締め) の票は受けない
        if (PendingEnd != null)
        {
            logger.Info($"ID: {voter}の票は、締めを待っている間なので数えません");
            return;
        }
        if (!allVotes.TryGetValue(voter, out var vote))
        {
            logger.Warn($"ID: {voter}の投票データがありません。新規作成します");
            vote = new(voter);
        }
        if (vote.HasVoted)
        {
            if (!isoverride && vote.VotedFor != NoVote) return;
            logger.Info($"ID: {voter}の投票を上書きします");
        }
        var voterPc = Utils.GetPlayerById(voter);

        if (vote.IsJudgeOverrule && judgeExiledId == byte.MaxValue)
        {
            return;
        }

        if (judgeExiledId != byte.MaxValue)
        {
            vote.DoVote(voteFor, numVotes, judgeExiledId, judgeNonce);
            return;
        }

        bool doVote = true;
        foreach (var role in CustomRoleManager.AllActiveRoles.Values.ToArray())
        {
            byte? roleVoteFor; int? roleNumVotes; bool roleDoVote;
            try { (roleVoteFor, roleNumVotes, roleDoVote) = role.ModifyVote(voter, voteFor, isIntentional); }
            catch (Exception ex) { Logger.Exception(ex, "ModifyVote"); continue; }
            if (roleVoteFor.HasValue)
            {
                logger.Info($"{role.Player.GetNameWithRole()} が {voterPc.GetNameWithRole()} の投票先を {GetVoteName(roleVoteFor.Value)} に変更します");
                voteFor = roleVoteFor.Value;
            }
            if (roleNumVotes.HasValue)
            {
                logger.Info($"{role.Player.GetNameWithRole()} が {voterPc.GetNameWithRole()} の投票数を {roleNumVotes.Value} に変更します");
                numVotes = roleNumVotes.Value;
            }
            if (!roleDoVote)
            {
                logger.Info($"{role.Player.GetNameWithRole()} によって投票は取り消されます");
                doVote = roleDoVote;
            }
        }
        numVotes = PlusVote.OnVote(voter, numVotes);
        TieBreaker.OnVote(voter, voteFor);

        if (doVote)
        {
            vote.DoVote(voteFor, numVotes);
        }
    }
    /// 議論時間が終わってる or 全員が投票を終えていれば会議を終了します
    public void CheckAndEndMeeting()
    {
        // 会議の出だしの演出と話し合いの間は締めない。この間に締めの電文を送ると、本編の演出が後から投票前の段階へ戻し、
        // 会議が閉じたのに画面が投票前のまま進まなくなる。票がそろっていれば、投票の段階に入った時に締める (MeetingHud.Update)
        if (IsEarlyPhase)
        {
            PendingCheck = true;
            return;
        }
        PendingCheck = false;
        // 役職がその場で決めた締めを待たせていれば、そちらで締める
        if (PendingEnd is { } end) { end(); return; }
        // 投票時間 0 は無制限 (時間切れで締めない)
        bool timeExpired = Main.NormalOptions.VotingTime > 0
            && meetingHud.discussionTimer - (float)Main.NormalOptions.DiscussionTime >= Main.NormalOptions.VotingTime;
        bool allVoted = AllVotes.Values.All(vote => vote.HasVoted);
        if (timeExpired || allVoted)
        {
            // 天秤会議では、スキップや同数の扱いの設定を使わない (天秤が自分で決める)
            EndMeeting(Roles.Crewmate.Balancer.Id == byte.MaxValue);
        }
    }
    /// <summary>出だしの演出や話し合いの間に締めの判定を見送った</summary>
    public bool PendingCheck { get; private set; }
    /// <summary>出だしの演出や話し合いの間に待たせた締め</summary>
    public Action PendingEnd { get; private set; }
    private bool IsEarlyPhase => meetingHud != null && meetingHud.state is MeetingHud.MeetingStates.Animating or MeetingHud.MeetingStates.Discussion;

    /// <summary>
    /// 会議中キル (ゲッサー・処刑) の後: 死んだ人へ入れた票を取り消し (本編の画面の票の取り消しと合わせる)、
    /// 入れ直す間を置いてから締めの判定をやり直す (死んだ人が最後の未投票だった時に締まらないままにならないように)
    /// </summary>
    public static void OnMeetingKill(byte targetId)
    {
        if (!AmongUsClient.Instance.AmHost || Instance is not { } manager) return;
        foreach (var vote in manager.allVotes.Values)
            if (vote.VotedFor == targetId) vote.DoVote(NoVote, 1);
        _ = new LateTask(() => Instance?.CheckAndEndMeeting(), 3f, "CheckForEndVoting.AfterMeetingKill");
    }

    /// 無条件で会議を終了します
    /// <param name="applyVoteMode">スキップと同数投票の設定を適用するかどうか</param>
    public void EndMeeting(bool applyVoteMode = true, byte antiCompId = byte.MaxValue)
    {
        // 出だしの演出と話し合いの間に締めると、本編の演出が投票前の段階へ戻して会議が止まる。
        // 役職がその場で決めた締め (ディクテーターなど) も、投票の段階に入るまで待ってから同じ形で締める (待つ間の票は受けない)
        if (IsEarlyPhase)
        {
            bool inClear = RoleHooks.InClearAndExile;
            PendingEnd = () =>
            {
                RoleHooks.InClearAndExile = inClear;
                try { EndMeeting(applyVoteMode, antiCompId); }
                finally { RoleHooks.InClearAndExile = false; }
            };
            logger.Info("会議の出だしの演出の間なので、締めを投票の段階まで待ちます");
            return;
        }
        PendingEnd = null;
        var result = CountVotes(applyVoteMode, antiCompId != byte.MaxValue);
        //Y-anti
        var exiled = (antiCompId == byte.MaxValue) ? result.Exiled : Utils.GetPlayerInfoById(antiCompId);

        var logName = exiled == null ? (result.IsTie ? "同数" : "スキップ") : exiled.Object?.GetNameWithRole() ?? exiled.PlayerName;
        logger.Info($"追放者: {logName} で会議を終了します");
        // 記録や演出の例外で RpcVotingComplete まで届かないと、次のフレームでも同じ所で止まり会議が終わらない
        try { GameLog.OnVoteResult(exiled, result.IsTie); }
        catch (Exception ex) { Logger.Exception(ex, "GameLog.OnVoteResult"); }
        try { HamoMeeting.OnVoteResult(exiled, result.IsTie); }
        catch (Exception ex) { Logger.Exception(ex, "HamoMeeting.OnVoteResult"); }

        var states = new List<MeetingHud.VoterState>();
        foreach (var voteArea in meetingHud.playerStates)
        {
            byte playerId = voteArea.PlayerId;
            var voteData = AllVotes.TryGetValue(playerId, out var value) ? value : null;
            if (voteData == null)
            {
                logger.Warn($"{Utils.GetPlayerById(playerId).GetNameWithRole()} の投票データがありません");
                continue;
            }
            if (voteData.VotedFor == NoVote || !CanBeVotedFor(voteData.VotedFor)) continue;
            for (var i = 0; i < voteData.NumVotes; i++)
            {
                states.Add(new()
                {
                    VoterId = playerId,
                    VotedForId = voteData.VotedFor,
                });
            }
        }

        try { MeetingExtras.OnVotes(states); }
        catch (Exception ex) { Logger.Exception(ex, "MeetingExtras.OnVotes"); }
        if (exiled != null) AntiBlackout.ExiledPlayerId = exiled.PlayerId;
        Logger.Info($"ExiledPlayerIdSet exiled: {exiled?.name}", "AntiBlackout");
        // SetIsDead は MeetingHud.OnDestroy (会議終了ログの直後) で呼ばれるため、
        // ここでは OverrideExiledPlayer の場合のみ即時呼び出しを維持する。

        bool isOverruled = result.OverrideExiled != byte.MaxValue;

        if (AntiBlackout.OverrideExiledPlayer)
        {
            ExileControllerWrapUpPatch.AntiBlackout_LastExiled = exiled;
            AntiBlackout.SetIsDead();

            meetingHud.RpcVotingComplete(states.ToArray(), null, true, false, 0);
            if (isOverruled) MeetingHudPatch.SetJudgeOverrulePatch.ShowOverruleLocally(result.OverruleNonce);
        }
        else
        {
            meetingHud.RpcVotingComplete(states.ToArray(), exiled, result.IsTie,
                                         isOverruled && exiled != null, result.OverruleNonce);
        }
        try
        {
            if (exiled != null)
            {
                // 暗転対策で追放者を伏せて送った時も、本当の追放者で属性の処理を通す
                try { Revealer.OnExiled(exiled.Object, showOnExileScreen: !AntiBlackout.OverrideExiledPlayer); }
                catch (Exception ex) { Logger.Exception(ex, "Revealer.OnExiled"); }
                MeetingHudPatch.CheckForDeathOnExile(CustomDeathReason.Vote, exiled.PlayerId);
            }
        }
        finally
        {
            // 締めの電文はもう出ている。例外でも残さず、同じ会議でもう一度締めない
            Destroy();
        }
    }
    /// <see cref="AllVotes"/>から投票をカウントします
    /// <param name="applyVoteMode">スキップと同数投票の設定を適用するかどうか</param>
    /// <returns>([Key: 投票先,Value: 票数]の辞書, 追放される人, 同数投票かどうか)</returns>
    public VoteResult CountVotes(bool applyVoteMode, bool isAntiComp = false)
    {
        // 投票モードに従って投票を変更
        if (applyVoteMode && Options.VoteMode.GetBool())
        {
            ApplySkipAndNoVoteMode();
        }

        // GameMode
        if (ModeCatalog.IsCatchCat/* || (Options.IsONMode && !MeetingStates.FirstMeeting/*狩人投票ターン)*/)
        {
            ChangeSkipByGameMode();
        }

        // Key: 投票された人
        // Value: 票数
        Dictionary<byte, int> votes = new();
        foreach (var voteArea in meetingHud.playerStates)
        {
            votes[(byte)voteArea.PlayerId] = 0;
        }
        votes[Skip] = 0;

        // ジャッジの裁決
        byte overrideExiled = byte.MaxValue;
        ushort overruleNonce = 0;

        foreach (var vote in AllVotes.Values)
        {
            if (vote.VotedFor == NoVote)
            {
                continue;
            }
            var voter = Utils.GetPlayerById(vote.Voter);
            if (voter == null) continue;
            if (PlayerState.GetByPlayerId(vote.Voter).IsDead) continue;

            if (vote.IsJudgeOverrule)
            {
                overrideExiled = vote.JudgeExiledId;
                overruleNonce = vote.JudgeNonce;
            }

            // 会議中に死んだ・抜けた人への票は数えない。数えると、もういない人が追放者に選ばれる
            if (!CanBeVotedFor(vote.VotedFor)) continue;

            votes[vote.VotedFor] += vote.NumVotes;
        }

        return new VoteResult(votes, isAntiComp, overrideExiled, overruleNonce);
    }
    /// スキップモードと無投票モードに応じて，投票を上書きしたりプレイヤーを死亡させたりします
    private void ApplySkipAndNoVoteMode()
    {
        var ignoreSkipModeDueToFirstMeeting = MeetingStates.FirstMeeting && Options.WhenSkipVoteIgnoreFirstMeeting.GetBool();
        var ignoreSkipModeDueToNoDeadBody = !MeetingStates.IsExistDeadBody && Options.WhenSkipVoteIgnoreNoDeadBody.GetBool();
        var ignoreSkipModeDueToEmergency = MeetingStates.IsEmergencyMeeting && Options.WhenSkipVoteIgnoreEmergency.GetBool();
        var ignoreSkipMode = ignoreSkipModeDueToFirstMeeting || ignoreSkipModeDueToNoDeadBody || ignoreSkipModeDueToEmergency;

        var skipMode = Options.GetWhenSkipVote();
        var noVoteMode = Options.GetWhenNonVote();
        foreach (var voteData in AllVotes)
        {
            var vote = voteData.Value;
            if (!vote.HasVoted)
            {
                var voterName = Utils.GetPlayerById(vote.Voter).GetNameWithRole();
                switch (noVoteMode)
                {
                    case VoteMode.Suicide:
                        MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, vote.Voter);
                        logger.Info($"無投票のため {voterName} を自殺させます");
                        break;
                    case VoteMode.SelfVote:
                        SetVote(vote.Voter, vote.Voter, isIntentional: false);
                        logger.Info($"無投票のため {voterName} に自投票させます");
                        break;
                    case VoteMode.Skip:
                        SetVote(vote.Voter, Skip, isIntentional: false);
                        logger.Info($"無投票のため {voterName} にスキップさせます");
                        break;
                }
            }
            else if (!ignoreSkipMode && vote.IsSkip)
            {
                var voterName = Utils.GetPlayerById(vote.Voter).GetNameWithRole();
                switch (skipMode)
                {
                    case VoteMode.Suicide:
                        MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, vote.Voter);
                        logger.Info($"スキップしたため {voterName} を自殺させます");
                        break;
                    case VoteMode.SelfVote:
                        SetVote(vote.Voter, vote.Voter, isIntentional: false);
                        logger.Info($"スキップしたため {voterName} に自投票させます");
                        break;
                }
            }
        }
    }
    /// GameModeで投票を強制的にスキップ
    private void ChangeSkipByGameMode()
    {
        foreach (var voteData in AllVotes)
        {
            var vote = voteData.Value;
            var voter = Utils.GetPlayerById(vote.Voter);
            if (voter == null) continue;
            if (!vote.IsSkip && !voter.Data.IsDead)
            {
                SetVote(vote.Voter, Skip, isIntentional: false);
                logger.Info($"スキップ以外のため{voter.GetNameWithRole()}にスキップさせました");
            }
        }
    }
    public void Destroy()
    {
        _instance = null;
    }

    private static bool CanBeVotedFor(byte target)
    {
        if (target == Skip) return true;
        var info = Utils.GetPlayerInfoById(target);
        return info != null && !info.Disconnected && !PlayerState.GetByPlayerId(target).IsDead;
    }

    public static string GetVoteName(byte num)
    {
        string name = "invalid";
        var player = Utils.GetPlayerById(num);
        if (num < 15 && player != null) name = player?.GetNameWithRole();
        else if (num == Skip) name = "Skip";
        else if (num == NoVote) name = "None";
        else if (num == 255) name = "Dead";
        return name;
    }

    public class VoteData
    {
        public byte Voter { get; private set; } = byte.MaxValue;
        public byte VotedFor { get; private set; } = NoVote;
        public int NumVotes { get; private set; } = 1;
        public bool IsSkip => VotedFor == Skip && !PlayerState.GetByPlayerId(Voter).IsDead;
        public bool HasVoted => VotedFor != NoVote || PlayerState.GetByPlayerId(Voter).IsDead;
        public bool IsJudgeOverrule => JudgeExiledId != byte.MaxValue;
        public byte JudgeExiledId { get; private set; } = byte.MaxValue;
        public ushort JudgeNonce { get; private set; }

        public VoteData(byte voter) => Voter = voter;

        public void DoVote(byte voteTo, int numVotes)
        {
            logger.Info($"投票: {Utils.GetPlayerById(Voter).GetNameWithRole()} => {GetVoteName(voteTo)} x {numVotes}");
            VotedFor = voteTo;
            NumVotes = numVotes;
        }
        public void DoVote(byte voteTo, int numVotes, byte judgeExiledId, ushort judgeNonce)
        {
            VotedFor = voteTo;
            NumVotes = numVotes;
            JudgeExiledId = judgeExiledId;
            JudgeNonce = judgeNonce;
        }
    }

    public readonly struct VoteResult
    {
        /// <summary>
        /// Key: 投票された人<br/>
        /// Value: 得票数
        /// </summary>
        public IReadOnlyDictionary<byte, int> VotedCounts => votedCounts;
        private readonly Dictionary<byte, int> votedCounts;
        /// <summary>
        /// 追放されるプレイヤー
        /// </summary>
        public readonly NetworkedPlayerInfo Exiled;
        /// <summary>
        /// 同数投票かどうか
        /// </summary>
        public readonly bool IsTie;
        /// <summary>
        /// ジャッジの裁決で追放されるプレイヤー (byte.MaxValue なら裁決なし)
        /// </summary>
        public readonly byte OverrideExiled;
        /// <summary>
        /// ジャッジの裁決の Nonce 
        /// </summary>
        public readonly ushort OverruleNonce;

        public VoteResult(Dictionary<byte, int> votedCounts, bool isAntiComp = false,
                          byte overrideExiled = byte.MaxValue, ushort overruleNonce = 0)
        {
            this.votedCounts = votedCounts;
            OverrideExiled = overrideExiled;
            OverruleNonce = overruleNonce;

            // 票数順に整列された投票
            var orderedVotes = votedCounts.OrderByDescending(vote => vote.Value);
            // 最も票を得た人の票数
            var maxVoteNum = orderedVotes.FirstOrDefault().Value;
            // 最多票数のプレイヤー全員
            var mostVotedPlayers = votedCounts.Where(vote => vote.Value == maxVoteNum).Select(vote => vote.Key).ToArray();

            // 最多票数のプレイヤーが複数人いる場合
            if (mostVotedPlayers.Length > 1)
            {
                IsTie = true;
                Exiled = null;
                logger.Info($"{string.Join(',', mostVotedPlayers.Select(id => GetVoteName(id)))} が同数");
            }
            else
            {
                IsTie = false;
                Exiled = GameData.Instance.GetPlayerById(mostVotedPlayers[0]);
                logger.Info($"最多得票者: {GetVoteName(mostVotedPlayers[0])}");
            }

            if (overrideExiled != byte.MaxValue)
            {
                Exiled = GameData.Instance.GetPlayerById(overrideExiled);
                IsTie = false;
                return;
            }

            if (!isAntiComp)
            {
                (IsTie, Exiled) = TieBreaker.BreakingVote(IsTie, Exiled, votedCounts, maxVoteNum);
                Exiled = Refusing.VoteChange(Exiled);
                Exiled = Nimrod.VoteChange(Exiled);
                Exiled = MadNimrod.VoteChange(Exiled);
            }

            // 役職による投票結果の書き換え (どれかが true なら同数時の特殊モードを使わない)
            var skipVoteMode = RoleHooks.VotingResults(ref Exiled, ref IsTie, votedCounts, mostVotedPlayers);

            // 同数投票時の特殊モード (誰にも票が入っていない時は、全員が 0 票で並ぶだけなので当てない)
            if (IsTie && Options.VoteMode.GetBool() && !skipVoteMode && maxVoteNum > 0)
            {
                var tieMode = (TieMode)Options.WhenTie.GetValue();
                switch (tieMode)
                {
                    case TieMode.All:
                        var toExile = mostVotedPlayers.Where(id => id != Skip).ToArray();
                        foreach (var playerId in toExile)
                        {
                            Utils.GetPlayerById(playerId)?.SetRealKiller(null);
                        }
                        MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Vote, toExile);
                        Exiled = null;
                        logger.Info("全員追放します");
                        break;
                    case TieMode.Random:
                        var exileId = mostVotedPlayers.OrderBy(_ => Guid.NewGuid()).FirstOrDefault();
                        Exiled = GameData.Instance.GetPlayerById(exileId);
                        IsTie = false;
                        logger.Info($"ランダム追放: {GetVoteName(exileId)}");
                        break;
                }
            }
        }
    }

    public const byte Skip = 253;
    public const byte NoVote = 254;

    /// 会議中に killtargetid を死なせ，その人への投票を取り消す (会議中キル)
    public static void ResetVoteManager(byte killtargetid)
    {
        PlayerControl pc = Utils.GetPlayerById(killtargetid);
        if (pc == null) return;
        bool amOwner = pc.AmOwner;

        if (AmongUsClient.Instance.AmHost)
        {
            pc.Data.IsDead = true;
            pc.RpcExileV3(CustomDeathReason.etc, keepExisting: true);
        }
        // 死因は呼び元が先に書いている
        PlayerState.GetByPlayerId(killtargetid).SetDead(CustomDeathReason.etc, keepExisting: true);

        MeetingHud meetingHud = MeetingHud.Instance;
        if (meetingHud == null) return;
        HudManager hudManager = DestroyableSingleton<HudManager>.Instance;
        bool resetVote = (meetingHud.discussionTimer - (float)Main.NormalOptions.DiscussionTime - (float)Main.NormalOptions.VotingTime) < 3;

        foreach (var x in meetingHud.playerStates) x.MaskArea.gameObject.SetActive(false);
        hudManager.KillOverlay.ShowKillAnimation(pc.Data, pc.Data);
        _ = new LateTask(() =>
        {
            if (meetingHud == null || meetingHud.playerStates == null) return;
            foreach (var x in meetingHud.playerStates)
            {
                if (x == null || x.MaskArea == null || x.MaskArea.gameObject == null) continue;
                x.MaskArea.gameObject.SetActive(true);
            }
        }, 2f, "ResetMask");
        if (AmongUsClient.Instance.AmHost) foreach (var p in Main.AllPlayerControls) p.KillFlash();

        if (amOwner)
        {
            hudManager.ShadowQuad.gameObject.SetActive(false);
            pc.cosmetics.nameText.GetComponent<MeshRenderer>().material.SetInt("_Mask", 0);
            ImportantTextTask importantTextTask = new GameObject("_Player").AddComponent<ImportantTextTask>();
            importantTextTask.transform.SetParent(AmongUsClient.Instance.transform, false);
            meetingHud.SetForegroundForDead();
        }
        PlayerVoteArea voteArea = meetingHud.playerStates.FirstOrDefault(x => x.PlayerId == pc.PlayerId);
        if (voteArea != null)
        {
            if (resetVote)
            {
                if (voteArea.DidVote) voteArea.UnsetVote();
                voteArea.AmDead = true;
                voteArea.Overlay.gameObject.SetActive(true);
                voteArea.Overlay.color = Color.white;
                voteArea.XMark.gameObject.SetActive(true);
                voteArea.XMark.transform.localScale = Vector3.one;
                if (AmongUsClient.Instance.AmHost)
                {
                    meetingHud.RpcClearVote(pc.PlayerId);
                    meetingHud.ClearVote(pc.PlayerId, pc == PlayerControl.LocalPlayer);
                    voteArea.UnsetVote();
                }
            }
            else
            {
                Instance?.SetVote(pc.PlayerId, NoVote);
            }
        }

        foreach (var playerVoteArea in meetingHud.playerStates)
        {
            if (playerVoteArea.VotedForId != pc.PlayerId) continue;
            var voteAreaPlayer = Utils.GetPlayerById(playerVoteArea.PlayerId);
            if (voteAreaPlayer == null) continue;
            if (AmongUsClient.Instance.AmHost)
            {
                if (resetVote)
                {
                    meetingHud.RpcClearVote(voteAreaPlayer.PlayerId);
                    meetingHud.ClearVote(voteAreaPlayer.PlayerId, voteAreaPlayer == PlayerControl.LocalPlayer);
                    playerVoteArea.UnsetVote();
                }
                else
                {
                    Instance?.SetVote(voteAreaPlayer.PlayerId, NoVote);
                }
            }
        }
        if (AmongUsClient.Instance.AmHost)
        {
            _ = new LateTask(() => Instance?.CheckAndEndMeeting(), 3f, "CheckForEndVoteing");
        }
    }
}
