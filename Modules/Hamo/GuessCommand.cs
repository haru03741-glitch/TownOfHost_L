using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Class;
using TownOfHostL.Roles.AddOns.Common;
using static TownOfHostL.Translator;

namespace TownOfHostL;

/// <summary>
/// 会議中のチャットの /bt (番号か色) (役職名) で役職を当てる。属性のゲッサーと、撃ち方を「コマンド」にした役職のゲッサー
/// (ナイスゲッサー・イビルゲッサー・マッドゲッサー) が使う。番号は会議の名前の前に出る数 (生きている人を PlayerId の順に 1 から)。
/// </summary>
/// <remarks>
/// TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/GuessManager.cs) から移した。
/// hamo は属性のゲッサーの 1 会議の上限にも全体の回数 (CanGuessTime) を見ていたので、ここでは 1 会議の回数 (OwnCanGuessTime) を見る。
/// </remarks>
public static class GuessCommand
{
    // 属性のゲッサーが撃った回数 (試合全体・この会議)
    static readonly Dictionary<byte, int> guessed = new();
    static readonly Dictionary<byte, int> meetingGuessed = new();

    // 会議ごとの番号。会議を開くたびに作り直す (途中で人が死んでも番号はずれない)
    static Dictionary<byte, int> numbers = new();
    static int numbersSerial = -1;

    public static void Reset()
    {
        guessed.Clear();
        meetingGuessed.Clear();
        numbers = new();
        numbersSerial = -1;
    }
    /// <summary>会議が終わった時。1 会議の回数を戻す</summary>
    public static void AfterMeeting() => meetingGuessed.Clear();

    /// <summary>この会議の番号 (PlayerId → 1 からの数)</summary>
    public static Dictionary<byte, int> Numbers
    {
        get
        {
            if (numbersSerial != MeetingStates.Serial || numbers.Count == 0)
            {
                numbers = new();
                var n = 1;
                foreach (var pc in Main.AllAlivePlayerControls.OrderBy(x => x.PlayerId))
                    numbers[pc.PlayerId] = n++;
                numbersSerial = MeetingStates.Serial;
            }
            return numbers;
        }
    }

    /// <summary>属性のゲッサーで、まだ撃てるか (番号を見せるか)</summary>
    static bool AddonCanStillGuess(PlayerControl pc)
        => pc != null && pc.IsAlive() && pc.Is(CustomRoles.Guesser) && !Guesser.IsBlockedTeller(pc.GetCustomRole())
            && Count(guessed, pc.PlayerId) < Guesser.CanGuessTime.GetInt();

    /// <summary>番号を名前の前に付けて見せる必要がある人か (役職のゲッサーは役職の表示の所で出すので除く)</summary>
    public static bool NeedsNumbers(PlayerControl seer)
        => seer != null && seer.GetRoleClass() is not VoteGuesser && AddonCanStillGuess(seer);

    /// <summary>会議の名前の前に付ける番号</summary>
    public static string NumberPrefix(PlayerControl seer, PlayerControl target, bool isForMeeting)
    {
        if (!isForMeeting || target == null || !target.IsAlive() || !NeedsNumbers(seer)) return "";
        if (!Numbers.TryGetValue(target.PlayerId, out var n)) return "";
        return $"<color=#ffff00><size=110%>{n}</size></color> ";
    }

    static int Count(Dictionary<byte, int> d, byte id) => d.TryGetValue(id, out var v) ? v : 0;

    static void Error(PlayerControl pc, string key, string titleKey = "GuessErrortitle")
        => Utils.SendMessage(GetString(key), pc.PlayerId, "<#e6b422>" + GetString(titleKey) + "</color>");
    static void Tell(PlayerControl pc, string text, Color color, string titleKey)
        => Utils.SendMessage(text, pc.PlayerId, Utils.ColorString(color, GetString(titleKey)));

    /// <summary>/bt の処理 (ホストの側)</summary>
    public static void Run(ChatCommandContext c)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var pc = c.Player;
        if (pc == null || !GameStates.IsInGame) return;
        if (!pc.IsAlive()) return;

        var roleClass = pc.GetRoleClass() as VoteGuesser;
        bool addon = pc.Is(CustomRoles.Guesser) && !Guesser.IsBlockedTeller(pc.GetCustomRole());
        if (roleClass == null && !addon)
        {
            Utils.SendMessage(GetString("NotGuesserError"), pc.PlayerId, Utils.ColorString(Palette.AcceptedGreen, GetString("NotGuesserErrortitle")));
            return;
        }
        if (!GameStates.IsMeeting || MeetingHud.Instance == null)
        {
            Error(pc, "GuessErrorNotMeeting");
            return;
        }
        if (GameStates.ExiledAnimate
            || MeetingHud.Instance.state is MeetingHud.MeetingStates.Results or MeetingHud.MeetingStates.Proceeding)
        {
            Utils.SendMessage(GetString("GuessErrorTuiho"), pc.PlayerId, $"<#ab80c2>{GetString("GuessErrorTuihoTitle")}</color>");
            return;
        }
        if (!TryParse(c.Arg(1), c.Arg(2), out var target, out var role, out var parseError))
        {
            Utils.SendMessage(parseError, pc.PlayerId, "<#e6b422>" + GetString("GuessErrortitle") + "</color>");
            return;
        }

        // 役職のゲッサーで、撃ち方にコマンドが入っていて、まだ撃てる時は役職のほうで撃つ
        if (roleClass != null && roleClass.CanCommandGuess)
        {
            if (!roleClass.TryCommandGuess(target, role, out var error))
                Utils.SendMessage(error, pc.PlayerId, "<#e6b422>" + GetString("GuessErrortitle") + "</color>");
            return;
        }
        if (!addon)
        {
            // 役職のゲッサーだけで、コマンドでは撃てない (撃ち方が投票だけ・もう撃てない)
            Error(pc, roleClass.UsesCommand ? "GuessercountError" : "GuessErrorVoteOnly");
            return;
        }
        AddonGuess(pc, target, role);
    }

    /// <summary>属性のゲッサーの撃ち方 (hamo と同じ決まり)</summary>
    static void AddonGuess(PlayerControl pc, PlayerControl target, CustomRoles role)
    {
        if (Count(guessed, pc.PlayerId) >= Guesser.CanGuessTime.GetInt())
        {
            Tell(pc, GetString("GuessercountError"), Palette.AcceptedGreen, "GuessercountErrorT");
            return;
        }
        if (Count(meetingGuessed, pc.PlayerId) >= Guesser.OwnCanGuessTime.GetInt())
        {
            Tell(pc, GetString("GuesserMTGcountError"), Palette.AcceptedGreen, "GuesserMTGcountErrorT");
            return;
        }
        var team = pc.GetCustomRole().GetCustomRoleTypes();
        if (team == CustomRoleTypes.Impostor && role == CustomRoles.Egoist) return;
        if (Blocked(pc, team, target, role)) return;

        if (role == CustomRoles.GM || target.Is(CustomRoles.GM) || (role.IsAddOn() && role != CustomRoles.Lovers))
        {
            Error(pc, "GuessErrorCannotGuess");
            return;
        }
        if (target.Is(CustomRoles.Stand))
        {
            var owner = (target.GetRoleClass() as TownOfHostL.Roles.Neutral.Stand)?.GetOwner();
            if (owner != null && owner.Player.IsAlive())
            {
                Utils.SendMessage("<color=#8B4513>" + GetString("MeetingSheriffStandImmune") + "</color>", pc.PlayerId);
                return;
            }
        }
        if (pc.Is(CustomRoles.Egoist) && target.Is(CustomRoleTypes.Impostor) && Guesser.ICanGuessNakama.GetBool()) return;

        bool suicide;
        if (pc.PlayerId == target.PlayerId)
        {
            Tell(pc, GetString("Guesssuicide"), Color.cyan, "Guesssuicidetitle");
            suicide = true;
        }
        else if (role == CustomRoles.Lovers) suicide = !target.Is(CustomRoles.Lovers);
        else suicide = !Hits(target, role);

        // 当たっていても、守られている人は撃てない (撃った人に跳ね返す)
        if (!suicide && pc.PlayerId != target.PlayerId)
        {
            var shield = GuessShield.Of(target);
            if (shield is GuessShield.Mode.Block)
            {
                Utils.SendMessage(GetString("GuessShieldBlocked"), pc.PlayerId);
                Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} は守られていてゲスが効かなかった", "Guesser");
                return;
            }
            if (shield is GuessShield.Mode.Reflect)
            {
                suicide = true;
                Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} は守られていてゲスが跳ね返った", "Guesser");
            }
        }

        guessed[pc.PlayerId] = Count(guessed, pc.PlayerId) + 1;
        meetingGuessed[pc.PlayerId] = Count(meetingGuessed, pc.PlayerId) + 1;

        var dead = suicide ? pc : target;
        Logger.Info($"{pc.GetNameWithRole().RemoveHtmlTags()} が {target.GetNameWithRole().RemoveHtmlTags()} を {role} でゲス → {dead.GetNameWithRole().RemoveHtmlTags()} が死ぬ", "Guesser");
        var roleText = Utils.ColorString(Utils.GetRoleColor(role), Utils.GetRoleName(role));
        if (suicide)
            Utils.AddGameLog("Guess", string.Format(GetString("guessfall"), pc.ColoredName(), target.ColoredName(), roleText));
        else
            Utils.AddGameLog("Guess", string.Format(GetString("guesssuccess"), pc.ColoredName(), target.ColoredName()));

        Kill(pc, dead, suicide ? CustomDeathReason.Misfire : CustomDeathReason.Guess);
        Utils.SendMessage(dead.ColoredName() + GetString("Meetingkill"), title: GetString("MSKillTitle"));
        var ghostText = suicide
            ? string.Format(GetString("Rgobaku"), pc.ColoredName(), target.ColoredName(), roleText)
            : string.Format(GetString("RMeetingKill"), pc.ColoredName(), dead.ColoredName());
        _ = new LateTask(() =>
        {
            foreach (var ghost in Main.AllPlayerControls.Where(p => p != null && !p.IsAlive()))
                Utils.SendMessage(ghostText, ghost.PlayerId, GetString("RMSKillTitle"));
        }, 2f, "Guess Msg To Ghosts");
    }

    /// <summary>陣営ごとの「撃てない物」。止めた時は理由を送って true</summary>
    static bool Blocked(PlayerControl pc, CustomRoleTypes team, PlayerControl target, CustomRoles role)
    {
        bool snitch = target.GetPlayerTaskState()?.IsTaskFinished == true && role == CustomRoles.Snitch;
        bool white = role == CustomRoles.UltraStar;
        bool vanilla = role.IsVanilla();
        OptionItem optSnitch = null, optNakama = null, optWhite = null, optVanilla = null;
        bool nakama = false;
        string teamKey = "";
        Color teamColor = Color.white;
        switch (team)
        {
            case CustomRoleTypes.Crewmate:
                optNakama = Guesser.CCanGuessNakama; optWhite = Guesser.CCanWhiteCrew; optVanilla = Guesser.CCanGuessVanilla;
                nakama = role.IsCrewmate();
                teamKey = "Crewmate"; teamColor = Palette.CrewmateBlue;
                break;
            case CustomRoleTypes.Impostor:
                optSnitch = Guesser.ICanGuessTaskDoneSnitch; optNakama = Guesser.ICanGuessNakama; optWhite = Guesser.ICanWhiteCrew; optVanilla = Guesser.ICanGuessVanilla;
                nakama = role.IsImpostor() && target.Is(CustomRoleTypes.Impostor);
                teamKey = "Impostor"; teamColor = Palette.ImpostorRed;
                break;
            case CustomRoleTypes.Madmate:
                optSnitch = Guesser.MCanGuessTaskDoneSnitch; optNakama = Guesser.MCanGuessNakama; optWhite = Guesser.MCanWhiteCrew; optVanilla = Guesser.MCanGuessVanilla;
                nakama = role.IsImpostorTeam();
                teamKey = "Madmate"; teamColor = new Color32(255, 128, 0, 255);
                break;
            case CustomRoleTypes.Neutral:
                optSnitch = Guesser.NCanGuessTaskDoneSnitch; optWhite = Guesser.NCanWhiteCrew; optVanilla = Guesser.NCanGuessVanilla;
                teamKey = "Neutral"; teamColor = Palette.DisabledGrey;
                break;
        }
        if (snitch && optSnitch != null && !optSnitch.GetBool())
        {
            Tell(pc, string.Format(GetString("GuessSnitch"), GetString(teamKey)), teamColor, "GuessSnitchTitle");
            return true;
        }
        if (nakama && optNakama != null && !optNakama.GetBool())
        {
            Tell(pc, GetString("GuessTeamMate"), teamColor, "GuessTeamMateTitle");
            return true;
        }
        if (white && optWhite != null && !optWhite.GetBool())
        {
            Tell(pc, string.Format(GetString("GuessWhiteRole"), GetString(teamKey)), Utils.GetRoleColor(CustomRoles.UltraStar), "GuessWhiteRoleTitle");
            return true;
        }
        if (vanilla && optVanilla != null && !optVanilla.GetBool())
        {
            Utils.SendMessage(GetString("GuessVanillaRoleTitle"), pc.PlayerId, Utils.ColorString(Utils.GetRoleColor(role), GetString("GuessVanillaRole")));
            return true;
        }
        return false;
    }

    /// <summary>当たりか。別の役職の姿 (殺した後の猫など) は元の役職でも当たり</summary>
    public static bool Hits(PlayerControl target, CustomRoles role)
        => target.Is(role) || target.GetCustomRole().AliasOf() == role;

    /// <summary>会議中に撃って死なせる (役職のゲッサーと同じやり方)</summary>
    public static void Kill(PlayerControl killer, PlayerControl target, CustomDeathReason reason)
    {
        target.SetRealKiller(killer, true);
        target.Data.IsDead = true;
        target.RpcExileV3(reason);
        CustomRoleManager.CheckMurderInfos[target.PlayerId] = new MurderInfo(killer, target, target, target);
        CustomRoleManager.OnMurderPlayer(target, target);

        //キルフラッシュ表示
        Main.AllPlayerControls.Do(pc => pc.KillFlash());

        if (MeetingHud.Instance != null)
        {
            foreach (var va in MeetingHud.Instance.playerStates)
            {
                if (va.VotedForId != target.PlayerId) continue;
                var voter = Utils.GetPlayerById(va.PlayerId);
                if (voter == null) continue;
                MeetingHud.Instance.RpcClearVote(voter.PlayerId);
            }
        }
        TownOfHostL.Modules.MeetingVoteManager.OnMeetingKill(target.PlayerId);
    }

    // 色の名前 (ColorId の順)。日本語・英語・hamo の呼び方
    static readonly string[][] ColorWords =
    {
        new[] { "レッド", "赤", "red" },
        new[] { "ブルー", "青", "blue" },
        new[] { "グリーン", "緑", "green" },
        new[] { "ピンク", "桃", "pink" },
        new[] { "オレンジ", "橙", "orange" },
        new[] { "イエロー", "黄", "yellow" },
        new[] { "ブラック", "黒", "black" },
        new[] { "ホワイト", "白", "white" },
        new[] { "パープル", "紫", "purple" },
        new[] { "ブラウン", "茶", "brown" },
        new[] { "シアン", "水", "cyan" },
        new[] { "ライム", "黄緑", "lime" },
        new[] { "マルーン", "小豆", "maroon" },
        new[] { "ローズ", "薄桃", "rose" },
        new[] { "バナナ", "ばなーぬ", "banana" },
        new[] { "グレー", "灰", "gray", "grey" },
        new[] { "タン", "tan" },
        new[] { "コーラル", "珊瑚", "coral" },
    };

    static PlayerControl FindByColor(string word)
    {
        var w = word.Trim().ToLowerInvariant();
        if (w.Length == 0) return null;
        // 長い呼び方から見る (「黄緑」を「黄」や「緑」と取り違えない)
        int best = -1, bestLen = 0;
        for (int id = 0; id < ColorWords.Length; id++)
        {
            var names = ColorWords[id].Append(Palette.GetColorName(id)).Where(x => !string.IsNullOrEmpty(x)).Select(x => x.ToLowerInvariant());
            foreach (var name in names)
            {
                if (w == name || (w.Contains(name) && name.Length > bestLen))
                {
                    if (w == name) return Main.AllAlivePlayerControls.FirstOrDefault(p => p.Data.DefaultOutfit.ColorId == id);
                    best = id; bestLen = name.Length;
                }
            }
        }
        return best < 0 ? null : Main.AllAlivePlayerControls.FirstOrDefault(p => p.Data.DefaultOutfit.ColorId == best);
    }

    /// <summary>「番号か色」と「役職名か #役職の番号」を読む</summary>
    public static bool TryParse(string who, string roleName, out PlayerControl target, out CustomRoles role, out string error)
    {
        target = null;
        role = CustomRoles.NotAssigned;
        error = GetString("GuessError1");
        if (string.IsNullOrWhiteSpace(who) || string.IsNullOrWhiteSpace(roleName)) return false;

        if (int.TryParse(who, out var num))
        {
            var id = Numbers.FirstOrDefault(kv => kv.Value == num);
            if (id.Value == num) target = Utils.GetPlayerById(id.Key);
        }
        else target = FindByColor(who);

        if (target == null || !target.IsAlive() || target.Data.Disconnected)
        {
            error = GetString("GuessError2");
            return false;
        }
        if (roleName.StartsWith("#") && int.TryParse(roleName[1..], out var roleNumber) && Enum.IsDefined(typeof(CustomRoles), roleNumber))
            role = (CustomRoles)roleNumber;
        else if (!ChatCommands.TryGetRoleByName(roleName, out role))
        {
            error = GetString("GuessError1");
            return false;
        }
        error = "";
        return true;
    }
}
