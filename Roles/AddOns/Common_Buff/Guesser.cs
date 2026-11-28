using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.AddOns.Common;

/// <summary>
/// 属性のゲッサー。会議中にチャットの /bt (番号) (役職名) で誰かの役職を当て、当たれば相手、外れれば自分が死ぬ。
/// 撃つ処理は <see cref="GuessCommand"/> (役職のゲッサーのコマンドと同じ所)。
/// </summary>
/// <remarks>
/// TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Roles/AddOns/Common/Buff/Guesser.cs・Modules/GuessManager.cs・
/// Roles/AddOns/Assin/AddOnsAssignData.cs) から移した。陣営ごとの配る人数と、陣営ごとの撃てる物の設定も hamo と同じ。
/// From は hamo と同じ The Other Roles。
/// </remarks>
public static class Guesser
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Guesser,
        AddOnGroup.Buff,
        "#999900",
        "∮",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "ゲッサー");

    private static List<byte> playerIdList = new();

    public static OptionItem ImpostorMaximum;
    public static OptionItem MadmateMaximum;
    public static OptionItem CrewmateMaximum;
    public static OptionItem NeutralMaximum;

    public static OptionItem CanGuessTime;
    public static OptionItem OwnCanGuessTime;
    //crew
    public static OptionItem CCanGuessVanilla;
    public static OptionItem CCanGuessNakama;
    public static OptionItem CCanWhiteCrew;
    public static OptionItem WarnIfCrewmate;
    //imp
    public static OptionItem ICanGuessVanilla;
    public static OptionItem ICanGuessNakama;
    public static OptionItem ICanGuessTaskDoneSnitch;
    public static OptionItem ICanWhiteCrew;
    //mad
    public static OptionItem MCanGuessVanilla;
    public static OptionItem MCanGuessNakama;
    public static OptionItem MCanGuessTaskDoneSnitch;
    public static OptionItem MCanWhiteCrew;
    //Neutral
    public static OptionItem NCanGuessVanilla;
    public static OptionItem NCanGuessTaskDoneSnitch;
    public static OptionItem NCanWhiteCrew;
    public static OptionItem TellerCannotGuess;

    // 占い系の役職 (占いとゲッサーで自分だけで答えが出せてしまうので、設定で止められるようにする)。L にある物だけ
    static readonly CustomRoles[] TellerRoles = { CustomRoles.FortuneTeller };
    // ここに入れた役職には、属性のゲッサーを配らない
    static readonly CustomRoles[] DenyRoles = { CustomRoles.God, CustomRoles.GM };

    /// <summary>「占い系はゲッサーを使えない」が ON で、その人が占い系か</summary>
    public static bool IsBlockedTeller(CustomRoles role)
        => TellerCannotGuess?.GetBool() == true && System.Array.IndexOf(TellerRoles, role) >= 0;

    static OptionItem TeamMaximum(string team, Color color, int max, OptionItem parent)
    {
        // 名前は 4 つとも同じなので、保存のキーは陣営で分ける
        var o = IntegerOptionItem.Create("%roleTypes%Maximum", new(0, max, 1), max, TabGroup.Addons, false)
            .SetParent(parent)
            .SetKeyName($"{team}Maximum")
            .SetValueFormat(OptionFormat.Players);
        o.ReplacementDictionary = new() { { "%roleTypes%", Utils.ColorString(color, GetString(team)) } };
        return o;
    }
    // 陣営ごとに同じ名前の設定が並ぶので、保存のキーは陣営の頭文字 (C/I/M/N) を付けて分ける
    static OptionItem Bool(string team, string name, bool def, OptionItem parent, Color color)
        => BooleanOptionItem.Create(name, def, TabGroup.Addons, false).SetParent(parent).SetKeyName(team + name).SetColor(color);

    public static void SetupCustomOption()
    {
        SetupRoleOptions(TabGroup.Addons, CustomRoles.Guesser);
        var spawn = CustomRoleSpawnChances[CustomRoles.Guesser];
        // 陣営ごとに配る人数の上限 (hamo の AddOnsAssignData と同じ)
        CrewmateMaximum = TeamMaximum("TeamCrewmate", Palette.CrewmateBlue, 15, spawn);
        ImpostorMaximum = TeamMaximum("TeamImpostor", Palette.ImpostorRed, 3, spawn);
        MadmateMaximum = TeamMaximum("Madmate", Palette.ImpostorRed, 15, spawn);
        NeutralMaximum = TeamMaximum("Neutral", Palette.AcceptedGreen, 15, spawn);

        //共通設定
        CanGuessTime = IntegerOptionItem.Create("CanGuessTime", new(1, 15, 1), 3, TabGroup.Addons, false).SetParent(spawn).SetValueFormat(OptionFormat.Players);
        OwnCanGuessTime = IntegerOptionItem.Create("OwnCanGuessTime", new(1, 15, 1), 1, TabGroup.Addons, false).SetParent(spawn).SetValueFormat(OptionFormat.Players);
        var crew = Palette.CrewmateBlue;
        var imp = Palette.ImpostorRed;
        var mad = new Color32(255, 128, 0, 255);
        var neu = Palette.DisabledGrey;
        //クルーメイト
        CCanGuessVanilla = Bool("C", "CanGuessVanilla", true, spawn, crew);
        CCanGuessNakama = Bool("C", "CanGuessNakama", true, spawn, crew);
        CCanWhiteCrew = Bool("C", "CanWhiteCrew", false, spawn, crew);
        WarnIfCrewmate = Bool("W", "GuesserWarnIfCrewmate", false, spawn, crew);
        //imp
        ICanGuessVanilla = Bool("I", "CanGuessVanilla", true, spawn, imp);
        ICanGuessNakama = Bool("I", "CanGuessNakama", true, spawn, imp);
        ICanGuessTaskDoneSnitch = Bool("I", "CanGuessTaskDoneSnitch", false, spawn, imp);
        ICanWhiteCrew = Bool("I", "CanWhiteCrew", false, spawn, imp);
        //mad
        MCanGuessVanilla = Bool("M", "CanGuessVanilla", true, spawn, mad);
        MCanGuessNakama = Bool("M", "CanGuessNakama", true, spawn, mad);
        MCanGuessTaskDoneSnitch = Bool("M", "CanGuessTaskDoneSnitch", true, spawn, mad);
        MCanWhiteCrew = Bool("M", "CanWhiteCrew", false, spawn, mad);
        //Neu
        NCanGuessVanilla = Bool("N", "CanGuessVanilla", true, spawn, neu);
        NCanGuessTaskDoneSnitch = Bool("N", "CanGuessTaskDoneSnitch", false, spawn, neu);
        NCanWhiteCrew = Bool("N", "CanWhiteCrew", false, spawn, neu);
        TellerCannotGuess = BooleanOptionItem.Create("GuesserTellerCannotGuess", false, TabGroup.Addons, false).SetParent(spawn);
    }

    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        GuessCommand.Reset();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
    }

    /// <summary>
    /// 試合の始めに配る。陣営ごとに上限まで選び、合わせて設定の人数より多ければ減らす (hamo の AddOnsAssignData.AssignTargetList と同じ)
    /// </summary>
    public static void Assign(List<PlayerControl> players)
    {
        var count = CustomRoles.Guesser.GetRealCount();
        if (count <= 0 || players == null) return;
        var rnd = IRandom.Instance;
        var valid = players.Where(pc =>
        {
            var role = pc.GetCustomRole();
            if (DenyRoles.Contains(role) || role.IsSpectator()) return false;
            if (IsBlockedTeller(role)) return false;
            if (pc.Is(CustomRoles.Guesser)) return false;
            return true;
        }).ToList();
        var candidates = new List<PlayerControl>();
        void Pick(OptionItem max, CustomRoleTypes team)
        {
            var n = max?.GetInt() ?? 0;
            var list = valid.Where(pc => pc.Is(team)).ToList();
            for (var i = 0; i < n && list.Count > 0; i++)
            {
                var p = list[rnd.Next(list.Count)];
                candidates.Add(p);
                list.Remove(p);
            }
        }
        Pick(CrewmateMaximum, CustomRoleTypes.Crewmate);
        Pick(ImpostorMaximum, CustomRoleTypes.Impostor);
        Pick(MadmateMaximum, CustomRoleTypes.Madmate);
        Pick(NeutralMaximum, CustomRoleTypes.Neutral);
        while (candidates.Count > count)
            candidates.RemoveAt(rnd.Next(candidates.Count));
        foreach (var pc in candidates)
        {
            PlayerState.GetByPlayerId(pc.PlayerId).SetSubRole(CustomRoles.Guesser);
            Logger.Info("属性設定:" + pc?.Data?.PlayerName + " = " + pc.GetCustomRole() + " + " + CustomRoles.Guesser, "AssignSubRoles");
        }
    }

    /// <summary>クルーメイトに付いた時、イントロに乱撃ちの注意を出すか</summary>
    public static bool ShouldWarn(PlayerControl pc)
        => WarnIfCrewmate?.GetBool() == true && pc != null && pc.Is(CustomRoles.Guesser) && pc.GetCustomRole().IsCrewmate();
}
