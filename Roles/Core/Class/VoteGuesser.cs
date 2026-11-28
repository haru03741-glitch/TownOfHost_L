using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

using static TownOfHostL.Translator;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL.Roles.Core.Class;

public abstract class VoteGuesser : RoleBase
{
    public VoteGuesser(
        SimpleRoleInfo roleInfo,
        PlayerControl player,
        Func<HasTask> hasTasks = null,
        bool? hasAbility = null
    )
    : base(
        roleInfo,
        player,
        hasTasks,
        hasAbility)
    {
        NumOfGuess = 1;
        MultipleInMeeting = false;

        guesserInfo = null;

        selecting = false;
        voted = false;
        guessed = false;
        targetGuess = null;
        targetForRole = null;
    }

    protected int NumOfGuess = 1;
    protected bool MultipleInMeeting = false;
    protected bool HideMisfire = false;
    protected bool GuessAfterVote = false;
    // マッドに使用
    protected bool AlwaysSuicide = false;
    /// <summary>撃ち方 (0: 自分に投票・1: チャットの /bt・2: どちらでも)</summary>
    protected int GuessMethod = 0;
    public static readonly string[] GuessMethodSelections = { "GuessMethodVote", "GuessMethodCommand", "GuessMethodBoth" };
    public bool UsesVote => GuessMethod != 1;
    public bool UsesCommand => GuessMethod != 0;
    /// <summary>今 /bt で撃てるか</summary>
    public bool CanCommandGuess => UsesCommand && Player != null && Player.IsAlive() && NumOfGuess > 0 && (!guessed || MultipleInMeeting);

    private GuesserInfo guesserInfo;

    private bool selecting = false;
    private bool guessed = false;
    private bool voted = false;
    private PlayerControl targetGuess = null;
    private PlayerControl targetForRole = null;

    public override string GetProgressText(bool comms = false) => Utils.ColorString(NumOfGuess > 0 ? Color.yellow : Color.gray, $"({NumOfGuess})");
    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
    {
        if (!isMeeting) return;
        if (Player == null || !Player.IsAlive()) return;
        if (seen == null || !seen.IsAlive() || seen.Data.Disconnected) return;
        if (NumOfGuess <= 0) return;

        if (guesserInfo == null) guesserInfo = new();

        if (!guesserInfo.PlayerNumbers.TryGetValue(seen.PlayerId, out int number)) return;

        if (!view.Enabled)
        {
            view.Replace("");
            view.Enabled = true;
        }
        view.Prefix = $"<color=#ffff00><size=110%>{number}</size></color>{view.Prefix}";
    }
    public override bool CheckVoteAsVoter(PlayerControl votedFor)
    {
        if (Player == null) return true;
        // Ensure meeting context exists to avoid nulls when accessed later
        if (MeetingHud.Instance == null) return true;

        if (NumOfGuess <= 0) return true;
        if (guessed && !MultipleInMeeting) return true;
        // 撃ち方がコマンドだけの時は、自分への投票も普通の投票
        if (!UsesVote) return true;

        // Ensure helper object exists when needed
        if (guesserInfo == null) guesserInfo = new();

        string formatTag = $"<size=70%><line-height=1.6pic>";

        if (selecting)
        {
            if (votedFor == null)
            {
                //スキップでモード解除
                selecting = false;
                targetGuess = null;
                targetForRole = null;

                Utils.SendMessageAutoSplit($"{formatTag}{GetString("Message.GuesserSelectionCancel")}", true, "", Player.PlayerId, formatTag);
                SendMessageGuide();
                return false;
            }

            if (targetGuess == null)
            {
                if (votedFor.PlayerId == Player.PlayerId)
                {
                    //対象選択での自投票は通常投票
                    selecting = false;
                    targetGuess = null;
                    targetForRole = null;
                    Utils.SendMessageAutoSplit($"{formatTag}{GetString("Message.GuesserSelectionSelfSelect")}", true, "", Player.PlayerId, formatTag);
                    return DoVote();
                }
                targetGuess = votedFor;
                guesserInfo.ResetList();
                Logger.Info($"GuesserSetTarget1 guesser: {Player?.name}, target: {targetGuess?.name}", "Guesser.CheckVoteAsVoter");
            }
            else
            {
                targetForRole = votedFor;
                Logger.Info($"GuesserSetTarget2 guesser: {Player?.name}, target: {targetForRole?.name}", "Guesser.CheckVoteAsVoter");
            }

            if (targetGuess == null)
            {
                Utils.SendMessageAutoSplit($"{formatTag}{GetString("Message.GuesserSelectionTarget")}", true, "", Player.PlayerId, formatTag);
                return false;
            }
            if (targetForRole == null)
            {
                Utils.SendMessageAutoSplit($"{formatTag}{string.Format(GetString("Message.GuesserSelectionRole"), targetGuess.name, guesserInfo.PageNo, guesserInfo.GetRoleGuide())}", true, "", Player.PlayerId, formatTag);
                return false;
            }

            Logger.Info($"GuesserSelectMeetingEnd guesser: {Player?.name}, target: {targetGuess?.name}, {targetForRole?.name}", "Guesser.CheckVoteAsVoter");

            var role = guesserInfo.GetRole(targetForRole);

            if (role == CustomRoles.NotAssigned)
            {
                Logger.Info($"InvalidSelection playerId: {targetForRole?.PlayerId}", "Guesser.CheckVoteAsVoter");
                targetForRole = null;
                return false; //無効選択
            }
            if (role == CustomRoles.DummyNext)
            {
                //ページ切り替え
                Logger.Info($"NextPage pageNo: {guesserInfo.PageNo}", "Guesser.CheckVoteAsVoter");
                guesserInfo.NextPage();
                Utils.SendMessageAutoSplit($"{formatTag}{string.Format(GetString("Message.GuesserSelectionRole"), targetGuess.name, guesserInfo.PageNo, guesserInfo.GetRoleGuide())}", true, "", Player.PlayerId, formatTag);
                return false;
            }

            if (targetGuess == null || !targetGuess.IsAlive() || targetGuess.Data == null || targetGuess.Data.Disconnected)
            {
                //ターゲット存在なしは強制スキップ
                selecting = false;
                targetGuess = null;
                targetForRole = null;

                Utils.SendMessageAutoSplit($"{formatTag}{GetString("Message.GuesserSelectionCancel")}", true, "", Player.PlayerId, formatTag);
                SendMessageGuide();
                return false;
            }

            UseGuesserAbility(role);

            selecting = false;
            targetGuess = null;
            targetForRole = null;
            SendMessageGuide();

            return false;
        }
        if (votedFor == null) return DoVote();
        if (Player.PlayerId == votedFor.PlayerId && NumOfGuess > 0)
        {
            Logger.Info($"GuesserSelectStart guesser: {Player?.name}", "Guesser.CheckVoteAsVoter");

            selecting = true;
            if (guesserInfo == null) guesserInfo = new();
            else guesserInfo.ResetList();
            Utils.SendMessageAutoSplit($"{formatTag}{GetString("Message.GuesserSelectionTarget")}", true, "", Player.PlayerId, formatTag);

            return false;
        }

        return DoVote();
    }
    private bool DoVote()
    {
        if (!GuessAfterVote) return true;

        var vote = !voted;
        voted = true;
        if (MeetingHud.Instance != null)
        {
            _ = After(() =>
            {
                if (MeetingHud.Instance != null)
                {
                    MeetingHud.Instance.RpcClearVote(Player.PlayerId);
                }
            }, 0.5f, "GuesserClearVote");
        }

        return vote;
    }
    private void UseGuesserAbility(CustomRoles role)
    {
        guessed = true;
        NumOfGuess--;

        PlayerControl target;
        if (GuessCommand.Hits(targetGuess, role) && !AlwaysSuicide)
        {
            target = targetGuess;
            RpcGuesserMurderPlayer(target, CustomDeathReason.Shot);
        }
        else
        {
            target = Player;
            var reason = CustomDeathReason.Misfire;
            if (HideMisfire) reason = CustomDeathReason.Shot;
            RpcGuesserMurderPlayer(target, reason);
        }
        SendGuessedMessage(target);
    }
    /// <summary>
    /// チャットの /bt で撃つ。撃てない時は理由を返す
    /// </summary>
    public bool TryCommandGuess(PlayerControl target, CustomRoles role, out string error)
    {
        error = "";
        if (!CanCommandGuess)
        {
            error = GetString(UsesCommand ? "GuessercountError" : "GuessErrorVoteOnly");
            return false;
        }
        if (target == null || !target.IsAlive() || target.Data == null || target.Data.Disconnected)
        {
            error = GetString("GuessError2");
            return false;
        }
        if (role == CustomRoles.GM || target.Is(CustomRoles.GM) || role.IsAddOn())
        {
            error = GetString("GuessErrorCannotGuess");
            return false;
        }
        // 自分への投票で選んでいる途中なら止める
        selecting = false;
        targetForRole = null;
        targetGuess = target;
        Logger.Info($"GuesserCommand guesser: {Player?.name}, target: {target?.name}, role: {role}", "Guesser.TryCommandGuess");
        UseGuesserAbility(role);
        targetGuess = null;
        if (Player.IsAlive()) SendMessageGuide();
        return true;
    }
    private void SendMessageGuide()
    {
        string formatTag = $"<size=70%><line-height=1.6pic>";

        if (NumOfGuess > 0 && (!guessed || MultipleInMeeting))
        {
            var key = GuessMethod switch { 1 => "Message.GuesserCommandGuide", 2 => "Message.GuesserBothGuide", _ => "Message.SelfVoteForActivate" };
            Utils.SendMessageAutoSplit($"{formatTag}{GetString(key)}", true, "", Player.PlayerId, formatTag);
        }
        else
        {
            Utils.SendMessageAutoSplit($"{formatTag}{GetString("Message.SelfVoteUsed")}", true, "", Player.PlayerId, formatTag);
        }
    }
    private void SendGuessedMessage(PlayerControl target)
    {
        string decoration = $"<color=#ffff00><size=100%>{GetString("Message.GuesserAbilityUse")}</size></color>\n";

        string targetText = $"{Palette.GetColorName(target.Data.DefaultOutfit.ColorId)} {target.name}";
        targetText = Utils.ColorString(Palette.PlayerColors[target.Data.DefaultOutfit.ColorId], targetText);

        targetText = $"<size=100%>{targetText}\n<color=#ffffff> {GetString("Message.GuesserDead")}</color></size>";

        string dispText = $"{decoration}\n{targetText}\n";
        string chatText = "\n\n";

        PlayerControl sender = TownOfHostL.Core.ChatSenderName.Sender();
        string name = sender.Data.PlayerName;
        ChatCommands.SendCustomChat(dispText, chatText, name, sender);
    }
    public override void OnStartMeeting()
    {
        SendMessageGuide();
    }
    public override void AfterMeetingTasks()
    {
        selecting = false;
        guessed = false;
        voted = false;
        targetGuess = null;
        targetForRole = null;

        guesserInfo = null;
    }
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!isForMeeting) return "";
        if (seer == null || seen != seer) return "";
        if (!seer.IsAlive()) return "";
        if (NumOfGuess <= 0) return "";

        if (guesserInfo == null) guesserInfo = new();
        return $"<color=#ff8000>{GetString(UsesVote ? "Message.SelfVoteSuffix" : "Message.GuesserCommandSuffix")}</color>";
    }
    public void RpcGuesserMurderPlayer(PlayerControl target, CustomDeathReason reason)
        => GuessCommand.Kill(Player, target, reason);
    private class GuesserInfo
    {
        public Dictionary<byte, int> PlayerNumbers;
        private List<CustomRoles> roleList;
        private List<(PlayerControl target, int number, CustomRoles role)> dispList = new();

        private int indexDisplayed = -1;
        public int PageNo = 0;

        public GuesserInfo()
        {
            SetPlayerNumbers();
            SetRoleList();
            SetDispList();
        }
        public void ResetList()
        {
            indexDisplayed = -1;
            PageNo = 0;
            SetDispList();
        }
        public void NextPage()
        {
            SetDispList();
        }
        private void SetPlayerNumbers()
        {
            // /bt の番号と同じ物 (会議ごとに 1 から)
            PlayerNumbers = new(GuessCommand.Numbers);
        }
        private void SetRoleList()
        {
            roleList = new();
            // 設定画面と同じ順 (区分ごと) に並べる。組の役職から出る役職は、元の役職の所に並ぶ
            foreach (CustomRoles enabled in RoleOrder.Sort(CustomRolesHelper.AllStandardRoles.Where(r => r.IsEnable())))
            {
                // 別の役職の姿は元の役職として並べる (同じ名前が 2 つ並ばないように)
                var alias = enabled.AliasOf();
                var role = alias != CustomRoles.NotAssigned ? alias : enabled;
                var assign = role.GetRoleAssignInfo()?.AssignUnitRoles;
                if (assign != null)
                {
                    foreach (var targetRole in assign)
                    {
                        if (targetRole != CustomRoles.Crewmate && targetRole != CustomRoles.Impostor && !roleList.Contains(targetRole))
                            roleList.Add(targetRole);
                    }
                }
                foreach (var targetRole in GetAdditionalRole(role))
                {
                    if (targetRole != CustomRoles.Crewmate && targetRole != CustomRoles.Impostor && !roleList.Contains(targetRole))
                        roleList.Add(targetRole);
                }
            }
        }
        public CustomRoles[] GetAdditionalRole(CustomRoles role)
        {
            switch (role)
            {
                case CustomRoles.ChainShifter: return ChainShifter.AdditionalRoles;
                case CustomRoles.Jackal: return Jackal.AdditionalRoles;
                case CustomRoles.Executioner: return Executioner.AdditionalRoles;
                case CustomRoles.Lawyer: return Lawyer.AdditionalRoles;
            }
            return Array.Empty<CustomRoles>();
        }
        private void SetDispList()
        {
            CustomRoles role;
            int index;
            int indexRole;

            dispList = new();

            var allAlive = Main.AllAlivePlayerControls;
            if (allAlive == null) return;
            var targetList = allAlive.Where(x => x != null && x.Data != null && !x.Data.Disconnected).OrderBy(x => x.PlayerId).ToList();

            if (roleList == null) roleList = new();

            if (indexDisplayed >= roleList.Count - 1)
            {
                indexDisplayed = -1;
                PageNo = 0;
            }
            PageNo++;
            index = 0;
            indexRole = indexDisplayed + index + 1;
            foreach (var target in targetList)
            {
                if (!PlayerNumbers.TryGetValue(target.PlayerId, out int number)) continue;

                if (index >= targetList.Count - 1 && (PageNo > 1 || indexRole < roleList.Count - 1))
                {
                    //次ページ表示
                    role = CustomRoles.DummyNext;
                }
                else if (indexRole >= roleList.Count)
                {
                    //無効分
                    role = CustomRoles.NotAssigned;
                }
                else
                {
                    role = roleList[indexRole];
                    indexDisplayed = indexRole;
                }
                dispList.Add((target, number, role));

                index++;
                indexRole++;
            }
        }
        public string GetRoleGuide()
        {
            if (dispList == null || dispList.Count == 0) return string.Empty;
            string text;

            StringBuilder sb = new();
            foreach (var info in dispList)
            {
                if (info.role == CustomRoles.NotAssigned) continue;

                if (info.role == CustomRoles.DummyNext)
                {
                    text = $"\n{info.number} {GetString("Message.NextPage")}";
                }
                else
                {
                    text = $"\n{info.number} {Utils.GetRoleName(info.role)}";
                }
                sb.Append(text);
                Logger.Info($"dispRole {text}", "Guesser.RoleText");
            }
            return sb.ToString();
        }
        public CustomRoles GetRole(PlayerControl target)
        {
            if (dispList == null) return CustomRoles.NotAssigned;
            var info = dispList.FirstOrDefault(x => x.target == target);
            if (info.target == null) return CustomRoles.NotAssigned;
            return info.role;
        }
    }
}
