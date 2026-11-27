using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Core.Modes;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem SuddenSharingRoles;
    public static OptionItem SuddenCantSeeOtherName;
    public static OptionItem SuddenDeathTimeLimit;
    public static OptionItem SuddenDeathReactortime;
    public static OptionItem SuddenPlayerArrow;
    public static OptionItem SuddenArrowSendTime;
    public static OptionItem SuddenArrowSenddis;
    public static OptionItem SuddenRemainingPlayerCount;
    public static OptionItem SuddenCanSeeKillflash;
    public static OptionItem SuddenKillcooltime;
    public static OptionItem SuddenfinishTaskWin;
    public static OptionItem SuddenTeam;
    public static OptionItem SuddenAddTeamYellow;
    public static OptionItem SuddenAddTeamGreen;
    public static OptionItem SuddenAddTeamPurple;
    public static OptionItem SuddenTeamOption;
    public static OptionItem SuddenTeamMaxPlayers;
    public static OptionItem SuddenTeamRole;
    public static OptionItem[] SuddenTeamRoles;

    static void SetupSuddenDeathOptions()
    {
        const CustomGameMode mode = CustomGameMode.SuddenDeath;
        var color = SuddenDeath.ModeColor;
        SuddenSharingRoles = BooleanOptionItem.Create("SuddenSharingRoles", false, TabGroup.ModMainSettings, false).SetHeader(true).SetColor(color).SetGameMode(mode);
        SuddenCantSeeOtherName = BooleanOptionItem.Create("SuddenCannotSeeName", false, TabGroup.ModMainSettings, false).SetColor(color).SetGameMode(mode);
        SuddenRemainingPlayerCount = BooleanOptionItem.Create("SuddenRemainingPlayerCount", true, TabGroup.ModMainSettings, false).SetColor(color).SetGameMode(mode);
        SuddenCanSeeKillflash = BooleanOptionItem.Create("SuddenCanSeeKillflash", true, TabGroup.ModMainSettings, false).SetColor(color).SetGameMode(mode);
        SuddenKillcooltime = FloatOptionItem.Create("SuddenKillcooltime", new(0f, 180f, 0.5f), 15f, TabGroup.ModMainSettings, false)
            .SetZeroNotation(OptionZeroNotation.Off).SetValueFormat(OptionFormat.Seconds).SetColor(color).SetGameMode(mode);
        SuddenfinishTaskWin = BooleanOptionItem.Create("SuddenfinishTaskWin", true, TabGroup.ModMainSettings, false).SetColor(color).SetGameMode(mode);
        SuddenDeathTimeLimit = FloatOptionItem.Create("SuddenDeathTimeLimit", new(0f, 300f, 1f), 120f, TabGroup.ModMainSettings, false)
            .SetZeroNotation(OptionZeroNotation.Infinity).SetValueFormat(OptionFormat.Seconds).SetColor(color).SetGameMode(mode);
        SuddenDeathReactortime = FloatOptionItem.Create("SuddenDeathReactortime", new(1f, 300f, 1f), 15f, TabGroup.ModMainSettings, false)
            .SetValueFormat(OptionFormat.Seconds).SetColor(color).SetGameMode(mode);
        SuddenPlayerArrow = BooleanOptionItem.Create("SuddenPlayerArrow", true, TabGroup.ModMainSettings, false).SetColor(color).SetGameMode(mode);
        SuddenArrowSendTime = FloatOptionItem.Create("SuddenArrowSendTime", new(0f, 300f, 0.5f), 90f, TabGroup.ModMainSettings, false).SetParent(SuddenPlayerArrow)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(mode);
        SuddenArrowSenddis = FloatOptionItem.Create("SuddenArrowSenddis", new(0f, 180f, 0.5f), 5f, TabGroup.ModMainSettings, false).SetParent(SuddenPlayerArrow)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(mode);
        SuddenTeam = BooleanOptionItem.Create("SuddenTeam", false, TabGroup.ModMainSettings, false).SetColor(color).SetGameMode(mode);
        SuddenAddTeamYellow = BooleanOptionItem.Create("SuddenTeamYellow", true, TabGroup.ModMainSettings, false).SetParent(SuddenTeam).SetColorcode(SuddenDeath.TeamColors[2]).SetGameMode(mode);
        SuddenAddTeamGreen = BooleanOptionItem.Create("SuddenTeamGreen", true, TabGroup.ModMainSettings, false).SetParent(SuddenTeam).SetColorcode(SuddenDeath.TeamColors[3]).SetGameMode(mode);
        SuddenAddTeamPurple = BooleanOptionItem.Create("SuddenTeamPurple", true, TabGroup.ModMainSettings, false).SetParent(SuddenTeam).SetColorcode(SuddenDeath.TeamColors[4]).SetGameMode(mode);
        SuddenTeamOption = BooleanOptionItem.Create("SuddenTeamOption", false, TabGroup.ModMainSettings, false).SetParent(SuddenTeam).SetGameMode(mode);
        SuddenTeamMaxPlayers = IntegerOptionItem.Create("SuddenTeamMax", new(1, 15, 1), 2, TabGroup.ModMainSettings, false).SetParent(SuddenTeam).SetGameMode(mode);
        SuddenTeamRole = BooleanOptionItem.Create("SuddenTeamRole", false, TabGroup.ModMainSettings, false).SetParent(SuddenTeam).SetGameMode(mode);

        var names = SuddenDeath.TeamRoleList.Select(role => role.ToString()).ToArray();
        string[] keys = { "SuddenRedTeamRole", "SuddenBlueTeamRole", "SuddenYellowTeamRole", "SuddenGreenTeamRole", "SuddenPurpleTeamRole" };
        SuddenTeamRoles = new OptionItem[keys.Length];
        for (var i = 0; i < keys.Length; i++)
            SuddenTeamRoles[i] = StringOptionItem.Create(keys[i], names, 0, TabGroup.ModMainSettings, false).SetParent(SuddenTeamRole)
                .SetColorcode(SuddenDeath.TeamColors[i]).SetGameMode(mode);
    }
}

/// <summary>サドンデス。通常の役職で配り、全員が自分以外を敵として戦う (チーム戦もある)。会議とサボタージュは無い</summary>
/// <remarks>TownOfHost-K (KYMario/TownOfHost-K@b7c85c84) のサドンデスを元にした (TownOfHost-hamo 経由)。</remarks>
public sealed class SuddenDeathMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.SuddenDeath;
    public override bool AllowsDoorSabotage => false;
    public override bool AllowsSabotage => false;
    public override bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target) => false;
    public override bool HasMeetings => false;
    public override bool ShowsOption(OptionItem option) => option.GameMode == CustomGameMode.Standard || base.ShowsOption(option);
    public override (string Key, Color Color)? Label => ("SuddenDeathMode", SuddenDeath.ModeColor);
}

public static class SuddenDeath
{
    public static readonly Color ModeColor = new Color32(255, 153, 102, 255);
    public static readonly string[] TeamColors = { "#ff1919", "#1e90ff", "#ffd700", "#00c853", "#a64dff" };
    static readonly CustomWinner[] teamWinners =
    {
        CustomWinner.SuddenDeathRed, CustomWinner.SuddenDeathBlue, CustomWinner.SuddenDeathYellow,
        CustomWinner.SuddenDeathGreen, CustomWinner.SuddenDeathPurple,
    };
    static readonly string[] teamWinKeys = { "SuddenDeathRed", "SuddenDeathBlue", "SuddenDeathYellow", "SuddenDeathGreen", "SuddenDeathPurple" };

    public static readonly CustomRoles[] InvalidRoles =
    {
        CustomRoles.Crewmate,
        CustomRoles.GuardianAngel,
        CustomRoles.HASFox,
        CustomRoles.HASTroll,
        CustomRoles.GM,
        CustomRoles.TaskPlayerB,
    };

    static CustomRoles[] teamRoleList;
    public static CustomRoles[] TeamRoleList => teamRoleList ??= CustomRolesHelper.AllStandardRoles.Where(role => !InvalidRoles.Contains(role) && !role.IsVanilla()).ToArray();

    public static bool Active => Options.CurrentGameMode == CustomGameMode.SuddenDeath;
    public static bool TeamMode => Active && Options.SuddenTeam.GetBool();

    /// <summary>プレイヤー → チーム (0 赤・1 青・2 黄・3 緑・4 紫)</summary>
    public static readonly Dictionary<byte, int> Teams = new();
    static readonly HashSet<byte> killedOnce = new();

    static float elapsed;
    static float roundTime;
    static bool sabotageStarted;
    static readonly bool?[] remaining = new bool?[4];
    static readonly float[] remainingAt = { 60f, 30f, 15f, 10f };
    static readonly string[] remainingColors = { "#8cff66", "#ffff00", "#ff9900", "#ff1919" };

    static float arrowTimer;
    static float gpsTimer;
    static bool showArrow;
    static int arrowColorIndex;
    static readonly string[] arrowColors = { "#ff9900", "#8cc8ff", "#00ff00", "#ffff00" };
    static readonly Dictionary<byte, Vector3> arrowPositions = new();

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        elapsed = 0f;
        sabotageStarted = false;
        arrowTimer = 0f;
        gpsTimer = 0f;
        showArrow = false;
        arrowColorIndex = -1;
        arrowPositions.Clear();
        killedOnce.Clear();
        if (!Active) return;
        roundTime = Options.SuddenDeathTimeLimit.GetFloat();
        for (var i = 0; i < remaining.Length; i++)
            remaining[i] = roundTime <= remainingAt[i] ? null : false;
    }

    public static bool SameTeam(byte a, byte b)
        => TeamMode && Teams.TryGetValue(a, out var ta) && Teams.TryGetValue(b, out var tb) && ta == tb;

    static bool IsPlayer(PlayerControl pc) => !(Options.EnableGM.GetBool() && pc.PlayerId == PlayerControl.LocalPlayer.PlayerId);

    static bool TeamEnabled(int team) => team switch
    {
        2 => Options.SuddenAddTeamYellow.GetBool(),
        3 => Options.SuddenAddTeamGreen.GetBool(),
        4 => Options.SuddenAddTeamPurple.GetBool(),
        _ => true,
    };

    // ---- 割り当て ----

    /// <summary>全員の役職を共通にする時は、抽選で出た役職から 1 つを選び、全員分に並べる。そうでない時は足りない分をインポスターで埋める</summary>
    public static void AdjustAssignList(List<CustomRoles> list)
    {
        if (!Active) return;
        var playerCount = Main.AllPlayerControls.Count();
        var mains = list.Where(role => role != CustomRoles.Impostor && !role.IsAddOn() && role != CustomRoles.Lovers).ToArray();
        if (Options.SuddenSharingRoles.GetBool())
        {
            var addons = list.Where(role => role.IsAddOn() || role == CustomRoles.Lovers).Distinct().ToArray();
            var role = mains.Length != 0 ? mains[IRandom.Instance.Next(mains.Length)] : CustomRoles.Impostor;
            list.Clear();
            for (var i = 0; i <= playerCount + 1; i++) list.Add(role);
            list.AddRange(addons);
            return;
        }
        for (var i = mains.Length; i < playerCount; i++) list.Add(CustomRoles.Impostor);
    }

    /// <summary>本編の割り当てで残った人のインポスターの数 (全員インポスターにする)</summary>
    public static int AdjustImpostorCount(int count, int players) => Active ? players : count;

    /// <summary>役職の実体を作る前に、チームを決め、チームの役職で上書きする</summary>
    public static void BeforeCreateInstance()
    {
        if (!TeamMode) return;
        if (!Options.SuddenTeamOption.GetBool())
        {
            Teams.Clear();
            var pool = Main.AllAlivePlayerControls.Where(IsPlayer).ToList();
            var max = Options.SuddenTeamMaxPlayers.GetInt();
            for (var team = 0; team < TeamColors.Length; team++)
            {
                if (!TeamEnabled(team)) continue;
                for (var i = 0; i < max && pool.Count > 0; i++)
                {
                    var pick = pool[IRandom.Instance.Next(pool.Count)];
                    pool.Remove(pick);
                    Teams[pick.PlayerId] = team;
                    Logger.Info($"{pick.Data?.PlayerName} => {team}", "SuddenDeathTeam");
                }
            }
        }
        if (!Options.SuddenTeamRole.GetBool()) return;
        foreach (var (id, team) in Teams)
        {
            var index = Options.SuddenTeamRoles[team].GetValue();
            if (index < 0 || index >= TeamRoleList.Length) continue;
            PlayerState.GetByPlayerId(id)?.SetMainRole(TeamRoleList[index]);
        }
    }

    public static void AfterAssign()
    {
        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
        CustomRoleManager.MarkOthers.Add(GetMarkOthers);
        GameEndChecker.SetPredicateTo(Rules);
        if (!TeamMode) return;
        foreach (var (id, team) in Teams)
            foreach (var seer in Main.AllPlayerControls)
                NameColorManager.Add(seer.PlayerId, id, TeamColors[team]);
    }

    /// <summary>全員の画面の全員の役職を、サドンデスの規則で送る</summary>
    public static void SendAllViews()
    {
        foreach (var target in Main.AllPlayerControls)
            foreach (var seer in Main.AllPlayerControls)
            {
                try { RoleView.Send(target, seer, RoleView.Default(target, seer)); }
                catch (System.Exception ex) { Logger.Exception(ex, "SuddenDeath"); }
            }
    }

    public static void OnIntroEnd()
    {
        if (!Active || !TeamMode) return;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (!IsPlayer(pc) || Teams.ContainsKey(pc.PlayerId)) continue;
            Logger.Warn($"{pc.Data?.PlayerName} => チームに入っていない", "SuddenDeath");
            PlayerState.GetByPlayerId(pc.PlayerId).DeathReason = CustomDeathReason.etc;
            Kill.Suicide(pc);
        }
    }

    // ---- キル ----

    /// <summary>まだキルしていない人の、スポーン後のキルクール</summary>
    public static void OverrideKillCooldown(PlayerControl player)
    {
        if (!Active || killedOnce.Contains(player.PlayerId)) return;
        var cool = Options.SuddenKillcooltime.GetFloat();
        if (cool <= 0) return;
        var own = (player.GetRoleClass() as Roles.Core.Interfaces.IKiller)?.CalculateKillCooldown();
        if (own.HasValue && own.Value != Options.DefaultKillCooldown) cool = own.Value;
        Main.AllPlayerKillCooldown[player.PlayerId] = cool;
    }

    public static void OnMurder(PlayerControl killer)
    {
        if (!Active || killer == null || !killedOnce.Add(killer.PlayerId)) return;
        killer.ResetKillCooldown();
    }

    public static bool BlocksKill(PlayerControl killer, PlayerControl target)
        => Active && killer != target && SameTeam(killer.PlayerId, target.PlayerId);

    public static bool SeesKillFlash(PlayerControl seer) => Active && Options.SuddenCanSeeKillflash.GetBool() && seer.IsAlive();

    public static bool HidesNames => Active && Options.SuddenCantSeeOtherName.GetBool();

    // ---- 時間 ----

    public static void OnFixedUpdate()
    {
        if (!Active || !HamoModes.IntroDone) return;
        if (Options.SuddenDeathTimeLimit.GetFloat() > 0) ReactorTick();
        if (Options.SuddenPlayerArrow.GetBool()) ArrowTick();
    }

    static void ReactorTick()
    {
        if (sabotageStarted) return;
        elapsed += Time.fixedDeltaTime;
        if (elapsed > roundTime)
        {
            sabotageStarted = true;
            HamoSabotageTrigger.Critical(Options.SuddenDeathReactortime.GetFloat());
            Utils.NotifyRoles();
            return;
        }
        for (var i = remaining.Length - 1; i >= 0; i--)
        {
            if (remaining[i] is not false || roundTime - elapsed >= remainingAt[i]) continue;
            for (var j = 0; j <= i; j++) if (remaining[j] is false) remaining[j] = true;
            Utils.NotifyRoles();
            return;
        }
    }

    static string RemainingText()
    {
        if (sabotageStarted) return "";
        for (var i = remaining.Length - 1; i >= 0; i--)
            if (remaining[i] is true) return Utils.ColorString(ColorOf(remainingColors[i]), $"{remainingAt[i]}s");
        return "";
    }

    static Color ColorOf(string code) => ColorUtility.TryParseHtmlString(code, out var c) ? c : Color.white;

    static void ArrowTick()
    {
        if (showArrow) arrowTimer += Time.fixedDeltaTime;
        else gpsTimer += Time.fixedDeltaTime;
        if (gpsTimer > Options.SuddenArrowSendTime.GetFloat()) showArrow = true;
        var interval = Options.SuddenArrowSenddis.GetFloat();
        if (!showArrow || arrowTimer <= interval) return;

        var alive = Main.AllAlivePlayerControls.ToArray();
        if (interval == 0)
        {
            foreach (var pc in alive)
                foreach (var pl in alive)
                    if (pc.PlayerId != pl.PlayerId && !SameTeam(pc.PlayerId, pl.PlayerId))
                        TargetArrow.Add(pc.PlayerId, pl.PlayerId);
            return;
        }
        arrowTimer = 0;
        foreach (var pc in alive)
            foreach (var pos in arrowPositions.Values) GetArrow.Remove(pc.PlayerId, pos);
        arrowPositions.Clear();
        foreach (var pc in alive) arrowPositions[pc.PlayerId] = pc.transform.position;
        foreach (var pc in alive)
        {
            var own = pc.transform.position;
            foreach (var (id, pos) in arrowPositions)
            {
                if (SameTeam(id, pc.PlayerId) || pos == own) continue;
                GetArrow.Add(pc.PlayerId, pos);
            }
        }
        arrowColorIndex = arrowColorIndex < 0 ? 0 : (arrowColorIndex + 1) % arrowColors.Length;
        Utils.NotifyRoles();
    }

    static string GetLowerTextOthers(PlayerControl seer, PlayerControl seen, bool isForMeeting, bool isForHud)
    {
        seen ??= seer;
        if (!Active || seer != seen || !Options.SuddenPlayerArrow.GetBool() || !showArrow) return "";
        string arrows;
        if (Options.SuddenArrowSenddis.GetFloat() == 0)
            arrows = TargetArrow.GetArrows(seer, Main.AllAlivePlayerControls.Select(pc => pc.PlayerId).ToArray());
        else
            arrows = GetArrow.GetArrows(seer, arrowPositions.Values.ToArray());
        if (string.IsNullOrEmpty(arrows)) return "";
        return Utils.ColorString(ColorOf(arrowColors[Mathf.Max(arrowColorIndex, 0)]), arrows);
    }

    static string GetMarkOthers(PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        seen ??= seer;
        if (!Active || seen != seer) return "";
        var text = RemainingText();
        if (!Options.SuddenRemainingPlayerCount.GetBool()) return text;
        if (TeamMode)
        {
            var counts = "";
            for (var team = 0; team < TeamColors.Length; team++)
            {
                var n = Main.AllAlivePlayerControls.Count(pc => Teams.TryGetValue(pc.PlayerId, out var t) && t == team);
                if (n > 0) counts += $"<color={TeamColors[team]}>({n})</color>";
            }
            return $"{text} <size=70%>{counts}</size>";
        }
        return $"{text}<color=#03fcb6> ({Main.AllAlivePlayerControls.Count()})</color>";
    }

    // ---- 勝敗 ----

    public static readonly GameEndRuleSet Rules = new(CheckPlayers, CheckFinishTask, CheckSabotage);

    static bool CheckPlayers(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        var alive = Main.AllAlivePlayerControls.ToArray();
        if (alive.Length == 0)
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
            return true;
        }
        if (CheckTeamWin(alive)) return true;
        if (alive.Length == 1)
        {
            var winner = alive[0];
            CustomWinnerHolder.ResetAndSetWinner((CustomWinner)winner.GetCustomRole());
            CustomWinnerHolder.WinnerIds.Add(winner.PlayerId);
            TeamAllWin();
            return true;
        }
        if (alive.All(pc => pc.Is(CustomRoles.Lovers)))
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Lovers);
            foreach (var pc in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.Lovers)))
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            TeamAllWin();
            return true;
        }
        return false;
    }

    static bool CheckTeamWin(PlayerControl[] alive)
    {
        if (!TeamMode) return false;
        for (var team = 0; team < TeamColors.Length; team++)
        {
            if (!alive.All(pc => Teams.TryGetValue(pc.PlayerId, out var t) && t == team)) continue;
            SetTeamWinner(team);
            return true;
        }
        return false;
    }

    static void SetTeamWinner(int team)
    {
        CustomWinnerHolder.ResetAndSetWinner(teamWinners[team]);
        foreach (var (id, t) in Teams)
            if (t == team) CustomWinnerHolder.WinnerIds.Add(id);
    }

    /// <summary>チーム戦で 1 人 (や恋人) が勝った時は、その人のチームの勝ちにする</summary>
    static void TeamAllWin()
    {
        if (!TeamMode) return;
        foreach (var id in CustomWinnerHolder.WinnerIds.ToArray())
        {
            if (!Teams.TryGetValue(id, out var team)) continue;
            SetTeamWinner(team);
            return;
        }
    }

    static bool CheckFinishTask(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (!Options.SuddenfinishTaskWin.GetBool()) return false;
        foreach (var player in Main.AllAlivePlayerControls)
        {
            var state = PlayerState.GetByPlayerId(player.PlayerId);
            if (state == null || !state.taskState.hasTasks) continue;
            if (!state.taskState.IsTaskFinished || !player.GetCustomRole().IsCrewmate()) continue;
            CustomWinnerHolder.ResetAndSetWinner((CustomWinner)player.GetCustomRole());
            CustomWinnerHolder.WinnerIds.Add(player.PlayerId);
            TeamAllWin();
            foreach (var other in Main.AllPlayerControls.Where(x => x.PlayerId != player.PlayerId))
            {
                if (!other.IsAlive()) continue;
                if (SameTeam(other.PlayerId, player.PlayerId)) continue;
                var otherState = PlayerState.GetByPlayerId(other.PlayerId);
                if (otherState != null) otherState.DeathReason = CustomDeathReason.Vote;
                other.SetRealKiller(player);
                Kill.Suicide(other);
            }
            return true;
        }
        return false;
    }

    /// <summary>時間切れの妨害が間に合わなかった時は、全員が死んで勝者なし</summary>
    static bool CheckSabotage(out GameOverReason reason)
    {
        if (!GameEndRules.BySabotage(out reason)) return false;
        foreach (var pc in Main.AllAlivePlayerControls.ToArray()) Kill.Suicide(pc);
        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
        return true;
    }

    /// <summary>結果の画面のチームの勝ちの文言</summary>
    public static bool TryGetTeamWinText(out string text)
    {
        var index = System.Array.IndexOf(teamWinners, CustomWinnerHolder.WinnerTeam);
        text = index < 0 ? null : $"<color={TeamColors[index]}>{GetString(teamWinKeys[index])}</color>";
        return index >= 0;
    }

    public static bool IsTeamWinner(CustomWinner winner) => System.Array.IndexOf(teamWinners, winner) >= 0;

    // ---- ロビーの位置でチーム分け ----

    static float lobbyTimer;
    static readonly Dictionary<byte, int> sentLobbyTeam = new();

    static int? TeamAt(Vector2 pos)
    {
        if (-3f <= pos.x && pos.x <= -1.1f && -1f <= pos.y && pos.y <= 0.4f) return 0;
        if (-0.2f <= pos.x && pos.x <= 0.7f && -1.1f <= pos.y && pos.y <= 0.4f) return 1;
        if (1.7f <= pos.x && pos.x <= 3f && -1.1f <= pos.y && pos.y <= 0.7f && TeamEnabled(2)) return 2;
        if (0.5f <= pos.x && pos.x <= 2.1f && 2.1f <= pos.y && pos.y <= 3.2f && TeamEnabled(3)) return 3;
        if (-2.9f <= pos.x && pos.x <= -1.1f && 2.2f <= pos.y && pos.y <= 3.0f && TeamEnabled(4)) return 4;
        return null;
    }

    static void LobbyTick()
    {
        lobbyTimer += Time.fixedDeltaTime;
        if (lobbyTimer < 0.25f) return;
        lobbyTimer = 0;
        if (!Active || !Options.SuddenTeam.GetBool() || !Options.SuddenTeamOption.GetBool())
        {
            if (Teams.Count > 0) Teams.Clear();
            sentLobbyTeam.Clear();
            return;
        }
        foreach (var pc in Main.AllPlayerControls)
        {
            var team = TeamAt(pc.GetTruePosition());
            if (team.HasValue) Teams[pc.PlayerId] = team.Value;
            else if (!GameStates.IsCountDown) Teams.Remove(pc.PlayerId);

            var shown = Teams.TryGetValue(pc.PlayerId, out var now) ? now : -1;
            if (sentLobbyTeam.TryGetValue(pc.PlayerId, out var last) && last == shown) continue;
            sentLobbyTeam[pc.PlayerId] = shown;
            var color = shown < 0 ? "#ffffff" : TeamColors[shown];
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.AmOwner || seer.IsModClient()) continue;
                pc.RpcSetNamePrivate($"<color={color}>{pc.Data.PlayerName}</color>", true, seer, true);
            }
        }
        if (GameStates.IsCountDown && Main.AllPlayerControls.Any(pc => IsPlayer(pc) && !Teams.ContainsKey(pc.PlayerId)))
        {
            GameStartManager.Instance.ResetStartState();
            Utils.SendMessage(GetString("SuddendeathLobbyError"));
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class LobbyPatch
    {
        static void Postfix(PlayerControl __instance)
        {
            if (!GameStates.IsLobby || __instance == null || !__instance.AmOwner || !AmongUsClient.Instance.AmHost) return;
            LobbyTick();
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    static class MurderPatch
    {
        static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!AmongUsClient.Instance.AmHost || target == null || target.IsAlive() || __instance == target) return;
            OnMurder(__instance);
        }
    }

    static void SetIntroText(IntroCutscene intro)
    {
        if (!Active) return;
        intro.TeamTitle.text = GetString("SuddenDeath");
        intro.TeamTitle.color = ModeColor;
        intro.ImpostorText.gameObject.SetActive(true);
        intro.ImpostorText.text = GetString("SuddenDeathIntro");
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate))]
    static class IntroCrew
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(IntroCutscene __instance) => SetIntroText(__instance);
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor))]
    static class IntroImpostor
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(IntroCutscene __instance) => SetIntroText(__instance);
    }
}
