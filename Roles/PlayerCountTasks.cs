using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL.Roles;

/// <summary>
/// 人数ごとのタスク数。ON にすると、部屋の人数 (3〜15 人・ホストが GM の時はホストを数えない) ごとに決めた
/// 通常・ロング・ショートのタスク数で試合を始める (部屋の本編の設定は書き換えない。その試合の間だけ)
/// </summary>
public static class PlayerCountTasks
{
    public static OptionItem Enabled;
    /// <summary>設定画面で今いじる人数 (選んだ人数の 3 行だけを出す)</summary>
    public static OptionItem EditPlayers;
    static readonly Dictionary<int, (OptionItem common, OptionItem longTasks, OptionItem shortTasks)> table = new();

    public static bool IsOn => Enabled != null && Enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;

    public static void SetupOptionItem()
    {
        table.Clear();
        Enabled = BooleanOptionItem.Create("PlayerCountTasks", false, TabGroup.ModMainSettings, false)
            .SetColor(Palette.LightBlue);
        EditPlayers = IntegerOptionItem.Create("PlayerCountTasks.Edit", new(PlayerCountComposition.MinPlayers, PlayerCountComposition.MaxPlayers, 1),
                PlayerCountComposition.MinPlayers, TabGroup.ModMainSettings, false)
            .SetParent(Enabled)
            .SetValueFormat(OptionFormat.Players);
        for (int n = PlayerCountComposition.MinPlayers; n <= PlayerCountComposition.MaxPlayers; n++)
        {
            var count = n;
            OptionItem Make(string kind, int max, int def)
            {
                var o = IntegerOptionItem.Create($"PlayerCountTasks.{kind}", new(0, max, 1), def, TabGroup.ModMainSettings, false)
                    .SetParent(Enabled)
                    .SetKeyName($"PlayerCountTasks.{count}.{kind}")
                    .SetValueFormat(OptionFormat.Pieces)
                    .SetEnabled(() => EditPlayers.GetInt() == count);
                o.ReplacementDictionary = new() { { "%n%", count.ToString() } };
                return o;
            }
            // 上限は本編の設定と同じ (通常 4・ロング 15・ショート 23)。はじめは本編のはじめの数
            table[n] = (Make("Common", 4, 1), Make("Long", 15, 1), Make("Short", 23, 2));
        }
    }

    /// <summary>この人数の時のタスク数</summary>
    public static (int common, int longTasks, int shortTasks) Get(int players)
    {
        var n = Mathf.Clamp(players, PlayerCountComposition.MinPlayers, PlayerCountComposition.MaxPlayers);
        if (!table.TryGetValue(n, out var o)) return (Main.NormalOptions.NumCommonTasks, Main.NormalOptions.NumLongTasks, Main.NormalOptions.NumShortTasks);
        return (o.common.GetInt(), o.longTasks.GetInt(), o.shortTasks.GetInt());
    }

    /// <summary>
    /// 試合の始め (部屋の設定の写しに切り替えた後) に、その試合のタスク数を入れる
    /// </summary>
    public static void Apply()
    {
        if (!IsOn || Main.NormalOptions == null) return;
        var players = PlayerCountComposition.CountedPlayers;
        var (common, longTasks, shortTasks) = Get(players);
        // タスクが 1 つも無いと本編がすぐタスク勝ちにするので、最低 1 つは残す
        if (common + longTasks + shortTasks <= 0) shortTasks = 1;
        Main.NormalOptions.NumCommonTasks = common;
        Main.NormalOptions.NumLongTasks = longTasks;
        Main.NormalOptions.NumShortTasks = shortTasks;
        Logger.Info($"人数ごとのタスク数 ({players} 人): 通常 {common}・ロング {longTasks}・ショート {shortTasks}", "PlayerCountTasks");
    }

    /// <summary>「設定する人数」を部屋の今の人数に合わせる (人数ごとの構成と一緒に呼ぶ)</summary>
    public static void FollowPlayerCount()
    {
        if (EditPlayers == null || !AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !GameData.Instance) return;
        var n = Mathf.Clamp(PlayerCountComposition.CountedPlayers, PlayerCountComposition.MinPlayers, PlayerCountComposition.MaxPlayers);
        if (EditPlayers.GetInt() == n) return;
        EditPlayers.SetValue(n - PlayerCountComposition.MinPlayers);
    }
}
