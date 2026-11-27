using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.Roles;

/// <summary>
/// 人数ごとの役職の構成。ON にすると、部屋の人数 (3〜15 人) に合わせて
/// インポスター・マッドメイト・ニュートラルの数を、人数ごとに決めておいた数へ自動で変える。
/// クルーの役職は残りの枠 (役職の確率で配り、足りない分は普通のクルー)。
/// ロビーではインポスターの数 (本編の設定) も人数に合わせて書き換えて、客へ送る
/// </summary>
public static class PlayerCountComposition
{
    public const int MinPlayers = 3;
    public const int MaxPlayers = 15;

    public static OptionItem Enabled;
    static readonly Dictionary<int, (OptionItem imp, OptionItem mad, OptionItem neu)> table = new();

    public static bool IsOn => Enabled != null && Enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard && !TownOfHostL.Core.Modes.ModeCatalog.IsCatchCat;

    // はじめの数 (よくある構成): インポスターは 6 人まで 1・10 人まで 2・それより多いと 3。
    // マッドメイトは 8 人から 1・13 人から 2。ニュートラルは 6 人から 1・11 人から 2
    static int DefaultImp(int n) => n <= 6 ? 1 : n <= 10 ? 2 : 3;
    static int DefaultMad(int n) => n >= 13 ? 2 : n >= 8 ? 1 : 0;
    static int DefaultNeu(int n) => n >= 11 ? 2 : n >= 6 ? 1 : 0;

    public static void SetupOptionItem()
    {
        table.Clear();
        Enabled = BooleanOptionItem.Create("PlayerCountComposition", false, TabGroup.ModMainSettings, false)
            .SetColor(Palette.LightBlue);
        for (int n = MinPlayers; n <= MaxPlayers; n++)
        {
            var rep = new Dictionary<string, string> { { "%n%", n.ToString() } };
            OptionItem Make(string type, int max, int def)
            {
                var o = IntegerOptionItem.Create($"PlayerCountComposition.{type}", new(0, max, 1), Math.Min(def, max), TabGroup.ModMainSettings, false)
                    .SetParent(Enabled)
                    .SetKeyName($"PlayerCountComposition.{n}.{type}")
                    .SetValueFormat(OptionFormat.Players);
                o.ReplacementDictionary = new(rep);
                return o;
            }
            var imp = Make("Impostor", Math.Min(5, n - 1), DefaultImp(n));
            imp.SetHeader(true);
            var mad = Make("Madmate", n - 1, DefaultMad(n));
            var neu = Make("Neutral", n - 1, DefaultNeu(n));
            table[n] = (imp, mad, neu);
        }
    }

    /// <summary>この人数の時の構成 (インポスター・マッドメイト・ニュートラル・クルーの役職の枠)。人数に収まるよう削る</summary>
    public static (int imp, int mad, int neu, int crew) Get(int players)
    {
        var n = Mathf.Clamp(players, MinPlayers, MaxPlayers);
        var (io, mo, no) = table[n];
        var imp = Mathf.Clamp(io.GetInt(), 0, Math.Max(0, players - 1));
        var left = Math.Max(0, players - imp);
        var mad = Mathf.Clamp(mo.GetInt(), 0, left);
        left -= mad;
        var neu = Mathf.Clamp(no.GetInt(), 0, left);
        left -= neu;
        return (imp, mad, neu, left);
    }

    static int PlayerCount()
    {
        try { return GameData.Instance ? GameData.Instance.PlayerCount : 0; }
        catch { return 0; }
    }

    static int lastPlayers = -1;
    static int lastImp = -1;
    static float nextCheck;

    /// <summary>ロビーで人数が変わったら、本編のインポスターの数を合わせて客へ送る (ホストだけ)</summary>
    public static void LobbyUpdate()
    {
        if (!IsOn || !AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) { lastPlayers = -1; return; }
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;
        var players = PlayerCount();
        if (players <= 0) return;
        var imp = Get(players).imp;
        if (players == lastPlayers && imp == lastImp && Main.NormalOptions != null && Main.NormalOptions.NumImpostors == imp) return;
        lastPlayers = players;
        lastImp = imp;
        ApplyImpostorCount(imp);
    }

    /// <summary>開始を押した時: その時の人数の構成を本編の設定へ入れる</summary>
    public static void ApplyAtGameStart()
    {
        if (!IsOn) return;
        var players = PlayerCount();
        if (players <= 0) return;
        ApplyImpostorCount(Get(players).imp);
    }

    static void ApplyImpostorCount(int imp)
    {
        try
        {
            if (Main.NormalOptions == null || Main.NormalOptions.NumImpostors == imp) return;
            Main.NormalOptions.NumImpostors = imp;
            Logger.Info($"人数ごとの構成: インポスターを {imp} 人にした", nameof(PlayerCountComposition));
            if (PlayerControl.LocalPlayer == null || GameOptionsManager.Instance == null) return;
            var opt = Main.NormalOptions.Cast<IGameOptions>();
            PlayerControl.LocalPlayer.RpcSyncSettings(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(opt, AprilFoolsMode.IsAprilFoolsModeToggledOn));
        }
        catch (Exception ex) { Logger.Warn($"インポスターの数を変えられなかった: {ex.Message}", nameof(PlayerCountComposition)); }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class LobbyPatch
    {
        public static void Postfix()
        {
            try { LobbyUpdate(); } catch { }
        }
    }
}
