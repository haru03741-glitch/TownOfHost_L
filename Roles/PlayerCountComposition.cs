using System;
using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.Roles;

/// <summary>
/// 人数ごとの役職の構成。ON にすると、部屋の人数 (3〜15 人) ごとに決めた
/// インポスター・マッドメイト・クルー・ニュートラルの最少と最多で、アサインモードの代わりに配る
/// (配り方はランダムのアサインと同じ: 最少をまず入れ、残りを最多までの間でくじ引き)
/// </summary>
public static class PlayerCountComposition
{
    public const int MinPlayers = 3;
    public const int MaxPlayers = 15;

    public static OptionItem Enabled;
    /// <summary>設定画面で今いじる人数 (選んだ人数の 8 行だけを出す)</summary>
    public static OptionItem EditPlayers;
    static readonly Dictionary<(int n, CustomRoleTypes type), (OptionItem min, OptionItem max)> table = new();

    public static bool IsOn => Enabled != null && Enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard && !TownOfHostL.Core.Modes.ModeCatalog.IsCatchCat;

    static readonly CustomRoleTypes[] Types = { CustomRoleTypes.Impostor, CustomRoleTypes.Madmate, CustomRoleTypes.Crewmate, CustomRoleTypes.Neutral };

    // はじめの数 (よくある構成)。インポスター・マッド・ニュートラルは最少と最多を同じにし、クルーの役職は残りを 0〜全部でくじ引き
    static (int min, int max) Default(int n, CustomRoleTypes type)
    {
        int v;
        switch (type)
        {
            case CustomRoleTypes.Impostor: v = n <= 6 ? 1 : n <= 10 ? 2 : 3; return (v, v);
            case CustomRoleTypes.Madmate: v = n >= 13 ? 2 : n >= 8 ? 1 : 0; return (v, v);
            case CustomRoleTypes.Neutral: v = n >= 11 ? 2 : n >= 6 ? 1 : 0; return (v, v);
            default: return (0, n - 1);
        }
    }

    public static void SetupOptionItem()
    {
        table.Clear();
        Enabled = BooleanOptionItem.Create("PlayerCountComposition", false, TabGroup.ModMainSettings, false)
            .SetColor(Palette.LightBlue);
        EditPlayers = IntegerOptionItem.Create("PlayerCountComposition.Edit", new(MinPlayers, MaxPlayers, 1), MinPlayers, TabGroup.ModMainSettings, false)
            .SetParent(Enabled)
            .SetValueFormat(OptionFormat.Players);
        for (int n = MinPlayers; n <= MaxPlayers; n++)
        {
            var count = n;
            foreach (var type in Types)
            {
                var rep = new Dictionary<string, string>
                {
                    { "%n%", n.ToString() },
                    { "%roleType%", Translator.GetString($"CustomRoleTypes.{type}") },
                };
                var max = type == CustomRoleTypes.Impostor ? Math.Max(1, n - 1) : n;
                var (dmin, dmax) = Default(n, type);
                OptionItem Make(string kind, int def)
                {
                    var o = IntegerOptionItem.Create($"PlayerCountComposition.{kind}", new(0, max, 1), Math.Min(def, max), TabGroup.ModMainSettings, false)
                        .SetParent(Enabled)
                        .SetKeyName($"PlayerCountComposition.{n}.{type}.{kind}")
                        .SetValueFormat(OptionFormat.Players)
                        // 「設定する人数」で選んだ人数の分だけ出す (13 人分を全部並べると長すぎる)
                        .SetEnabled(() => EditPlayers.GetInt() == count);
                    o.ReplacementDictionary = new(rep);
                    return o;
                }
                var minOpt = Make("Min", dmin);
                var maxOpt = Make("Max", dmax);
                table[(n, type)] = (minOpt, maxOpt);
            }
        }
    }

    /// <summary>この人数の時の、その陣営の最少と最多 (最多が最少より小さい時は最少にそろえる)</summary>
    public static (int min, int max) Get(int players, CustomRoleTypes type)
    {
        var n = Mathf.Clamp(players, MinPlayers, MaxPlayers);
        if (!table.TryGetValue((n, type), out var o)) return (0, 0);
        var min = o.min.GetInt();
        return (min, Math.Max(min, o.max.GetInt()));
    }

    // 開始の時に決めたインポスターの数 (本編の「インポスター数」の設定より優先する)。ロビーに戻ったら消す
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class LobbyStartPatch
    {
        public static void Postfix() => RoleAssignManager.ImpostorCountOverride = null;
    }
}
