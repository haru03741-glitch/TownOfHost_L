using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Crewmate;

public sealed class Psychic : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Psychic),
            player => new Psychic(player),
            CustomRoles.Psychic,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 1200,
            SetupOptionItem,
            "霊媒師",
            "#883fd1",
            introSound: () => GetIntroSound(RoleTypes.Crewmate)
        );
    public Psychic(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Cooldown = OptionCooldown.GetFloat();
        TaskTrigger = OptionTaskTrigger.GetInt();
    }
    private static OptionItem OptionCooldown;
    public static OptionItem MaxCheckRole;
    public static OptionItem ConfirmCamp;
    public static OptionItem KillerOnly;
    private static OptionItem OptionTaskTrigger;
    /// <summary>会議中に /psy (ID) で死んだ人を霊媒できる</summary>
    public static OptionItem OptionMeetingCommand;
    enum OptionName
    {
        PsychicMaxCheckRole,
        FortuneTellerConfirmCamp,
        FortuneTellerKillerOnly,
        TaskTrigger,
        PsychicMeetingCommand,
    }

    public static float Cooldown = 30;
    public static int TaskTrigger;
    int DivinationLeftCount = 0;
    bool IsCoolTimeOn = true;

    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionTaskTrigger = IntegerOptionItem.Create(RoleInfo, OptionName.TaskTrigger, new(0, 20, 1), 3, false)
            .SetValueFormat(OptionFormat.Pieces);
        MaxCheckRole = IntegerOptionItem.Create(RoleInfo, OptionName.PsychicMaxCheckRole, new(1, 15, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        ConfirmCamp = BooleanOptionItem.Create(RoleInfo, OptionName.FortuneTellerConfirmCamp, false, false);
        KillerOnly = BooleanOptionItem.Create(RoleInfo, OptionName.FortuneTellerKillerOnly, false, false);
        OptionMeetingCommand = BooleanOptionItem.Create(RoleInfo, OptionName.PsychicMeetingCommand, false, false);
    }

    /// <summary>
    /// /psy (ID): 会議中に死んだ人を 1 人霊媒する (設定で許した時だけ)。ベントで選ぶのと同じ回数を使い、結果は本人にだけ送る。
    /// 霊媒した人は、ベントで選んだ時と同じように会議の名前にも役職 (陣営) が出る
    /// </summary>
    public static void MeetingCommand(ChatCommandContext c)
    {
        var pc = c.Player;
        if (pc == null || pc.GetRoleClass() is not Psychic psychic) return;
        byte to = pc.PlayerId;
        if (!OptionMeetingCommand.GetBool()) { Utils.SendMessage(GetString("PsychicCmd.Disabled"), to); return; }
        if (!GameStates.IsMeeting || !pc.IsAlive()) { Utils.SendMessage(GetString("PsychicCmd.NotNow"), to); return; }
        if (!psychic.CanUseVent()) { Utils.SendMessage(GetString("PsychicCmd.CantUse"), to); return; }
        if (!byte.TryParse(c.Arg(1), out var id) || Utils.GetPlayerById(id) is not PlayerControl target)
        {
            Utils.SendMessage(GetString("PsychicCmd.Usage"), to);
            return;
        }
        if (target.IsAlive() || target == pc) { Utils.SendMessage(GetString("PsychicCmd.NotDead"), to); return; }
        if (!VentSelect.CheckRolePlayer.TryGetValue(to, out var checkedList)) VentSelect.CheckRolePlayer[to] = checkedList = new();
        if (checkedList.Contains(id)) { Utils.SendMessage(GetString("PsychicCmd.Already"), to); return; }

        checkedList.Add(id);
        psychic.DivinationLeftCount--;
        string result;
        if (KillerOnly.GetBool() && !TownOfHostL.Core.PlayerTraits.IsKillerForDivination(target))
            result = GetString("PsychicCmd.NotKiller");
        else if (ConfirmCamp.GetBool())
            result = TownOfHostL.Core.NameParts.TeamText(target);
        else
            result = Utils.ColorString(Utils.GetRoleColor(target.GetCustomRole()), Utils.GetRoleName(target.GetCustomRole()));
        Utils.SendMessage(string.Format(GetString("PsychicCmd.Result"), target.GetRealName(true), result, psychic.DivinationLeftCount),
            to, Utils.ColorString(RoleInfo.RoleColor, GetString("Psychic")));
        Utils.NotifyRoles(isForMeeting: true, SpecifySeer: pc);
    }
    public override void Add()
    {
        IsCoolTimeOn = true;
        DivinationLeftCount = MaxCheckRole.GetInt();
    }
    public override void OnStartMeeting()
    {
        IsCoolTimeOn = true;
        VentSelect.ClearSelect();
    }

    // 通報時にも IsCoolTimeOn をリセットして SyncAllSettings 時に
    // クールダウン0が送信されないようにする
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        IsCoolTimeOn = true;
        VentSelect.ClearSelect();
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = CanUseVent() ? (IsCoolTimeOn ? Cooldown : 0f) : 255f;
        AURoleOptions.EngineerInVentMaxTime = 1f;
    }
    public bool CanUseVent()
        => Player.IsAlive()
        && DivinationLeftCount > 0
        && MyTaskState.HasCompletedEnoughCountOfTasks(TaskTrigger);

    public override bool OnCompleteTask()
    {
        // 使えない間はベントの待ち時間を 255 秒にしてあり、客の手元ではそれを数え続けている。
        // タスクの数が届いて使えるようになったら、その設定が届いてから数え直す
        if (Player.IsAlive() && (MyTaskState.CompletedTasksCount == TaskTrigger || IsTaskFinished))
            Player.RpcResetAbilityCooldown(sync: true);
        return true;
    }

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!CanUseVent()) return false;

        IsCoolTimeOn = false;
        Player.MarkDirtySettings();

        VentSelect.PlayerSelect(Player);

        Utils.NotifyRoles(SpecifySeer: Player);
        return true;
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!GameStates.IsInTask) return;

        if (VentSelect.OnFixedUpdate(Player, out var selectedId))
        {
            // 確認できる回数 (PsychicMaxCheckRole) を 1 つ使う
            if (DivinationLeftCount > 0) DivinationLeftCount--;
            player.MarkDirtySettings();
            Utils.NotifyRoles(SpecifySeer: Player);

        }
    }

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
    {
        if (!VentSelect.IsShowTargetRole(Player, seen)) return;
        if (KillerOnly.GetBool() &&
            !TownOfHostL.Core.PlayerTraits.IsKillerForDivination(seen)) return;

        view.Enabled = true;

        if (!ConfirmCamp.GetBool()) return;   //役職表示

        view.Replace(TownOfHostL.Core.NameParts.TeamText(seen));   //陣営表示
    }
    public override bool KnowsRoleColorOf(PlayerControl target, bool isMeeting) => KnowTargetRoleColor(target) && isMeeting;
    private bool KnowTargetRoleColor(PlayerControl target)
    {
        if (!VentSelect.IsShowTargetRole(Player, target)) return false;
        if (!ConfirmCamp.GetBool()) return false;   //役職表示
        if (KillerOnly.GetBool() &&
        !TownOfHostL.Core.PlayerTraits.IsKillerForDivination(target)) return false;
        return true;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || !Is(seen) || !CanUseVent() || isForMeeting) return string.Empty;

        return VentSelect.GetCheckPlayerText(seer, isForHud);
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(RoleInfo.RoleColor, $"[{DivinationLeftCount}]");
    public override string GetAbilityButtonText() => GetString("ChangeButtonText");


    //死人がターゲットの特別なセレクト
    public static class VentSelect
    {
        public static Dictionary<byte, PlayerControl> SelectPlayer = new();
        public static Dictionary<byte, bool> SelectFix = new();
        public static Dictionary<byte, float> TergetFixTimer = new();
        public static Dictionary<byte, List<byte>> CheckRolePlayer = new();
        // 試合の開始でだけ空にする (Psychic を作るたびに空にすると、試合中に増えた Psychic が他の Psychic の確認済み一覧を消す)
        [TownOfHostL.Attributes.GameModuleInitializer]
        public static void Init()
        {
            SelectPlayer = new();
            SelectFix = new();
            TergetFixTimer = new();
            CheckRolePlayer = new();
        }
        public static void ClearSelect()
        {
            SelectPlayer.Clear();
            SelectFix.Clear();
            TergetFixTimer.Clear();
        }
        public static IEnumerable<PlayerControl> SelectList(byte playerId)
        {
            if (!CheckRolePlayer.ContainsKey(playerId)) CheckRolePlayer.Add(playerId, new());
            return Main.AllDeadPlayerControls.Where(x => !CheckRolePlayer[playerId].Contains(x.PlayerId));
        }
        public static void PlayerSelect(PlayerControl player)
        {
            if (player == null) return;

            var playerId = player.PlayerId;
            if (TergetFixTimer.ContainsKey(playerId)) //タイマーリセット
                TergetFixTimer.Remove(playerId);

            if (SelectFix.TryGetValue(playerId, out var fix) && fix) return;

            PlayerControl first = null;
            SelectPlayer.TryGetValue(playerId, out var selectedPlayer);
            var preSelected = false;
            var selected = false;
            if (!CheckRolePlayer.ContainsKey(playerId)) CheckRolePlayer.Add(playerId, new());
            foreach (var target in SelectList(playerId))
            {
                if (target == player) continue;

                if (first == null) first = target;

                if (preSelected)
                {
                    SelectPlayer[playerId] = target;
                    selected = true;
                    Logger.Info($"{player.name} PlayerSelectNow:{target.name}, nextTarget", "player");
                    break;
                }

                if (target == selectedPlayer) preSelected = true;
            }
            if (first == null)
            {
                SelectPlayer[playerId] = null;
                Logger.Info($"{player.name} PlayerSelectNow:null, ターゲットなし", "player");
                return;
            }
            if (!selected)
            {
                SelectPlayer[playerId] = first;
                Logger.Info($"{player.name} PlayerSelectNow:{first?.name}, firstTarget", "player");
            }

            TergetFixTimer.Add(player.PlayerId, 3f);
        }
        public static bool OnFixedUpdate(PlayerControl player, out int selectedId)
        {
            selectedId = -1;

            if (player == null) return false;
            if (!GameStates.IsInTask) return false;

            var playerId = player.PlayerId;
            if (!TergetFixTimer.ContainsKey(playerId)) return false;

            TergetFixTimer[playerId] -= Time.fixedDeltaTime;
            if (TergetFixTimer[playerId] > 0) return false;

            //以下ターゲット確定
            SelectFix[playerId] = true;
            selectedId = SelectPlayer[playerId].PlayerId;

            if (!CheckRolePlayer.ContainsKey(playerId)) CheckRolePlayer.Add(playerId, new());
            CheckRolePlayer[playerId].Add(SelectPlayer[playerId].PlayerId);
            TergetFixTimer.Remove(playerId);

            Logger.Info($"{player.name} PlayerDecision:{SelectPlayer[playerId].name}", "player");

            player.RpcProtectedMurderPlayer();   //設定完了のパリン
            player.RpcResetAbilityCooldown();

            return true;
        }

        public static bool IsShowTargetRole(PlayerControl seer, PlayerControl target)
        {
            var IsWatch = false;
            CheckRolePlayer.Do(x =>
            {
                if (x.Value != null && seer.PlayerId == x.Key && x.Value.Contains(target.PlayerId) && Utils.GetPlayerById(x.Key).IsAlive())
                    IsWatch = true;
            });
            return IsWatch;
        }
        public static string GetCheckPlayerText(PlayerControl psychic, bool isForHud)
        {
            if (psychic == null) return "";
            var psychicId = psychic.PlayerId;

            var str = new StringBuilder();
            SelectPlayer.TryGetValue(psychicId, out var target);
            if (target == null)
                str.Append(GetString(isForHud ? "SelectPlayerTagBefore" : "SelectPlayerTagMiniBefore"));
            else
            {
                str.Append(GetString(isForHud ? "SelectPlayerTag" : "SelectPlayerTagMini"));
                str.Append(target.GetRealName(Options.GetNameChangeModes() == NameChange.Crew));
            }
            return str.ToString();
        }
    }
}