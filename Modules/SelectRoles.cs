using System;
using System.Collections.Generic;
using System.Linq;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Modules;

public static class SelectRoles
{
    private static readonly Dictionary<byte, CustomRoles> reserved = new();

    public static IReadOnlyDictionary<byte, CustomRoles> Reserved => reserved;
    public static int Count => reserved.Count;

    public static void Set(byte playerId, CustomRoles role) => reserved[playerId] = role;
    public static void Clear() => reserved.Clear();

    public static void AddToAssignRoleList(List<CustomRoles> assignRoleList)
    {
        // 同じ役職を 2 人以上に予約した時も、その人数ぶんを一覧の先頭に置く (元の一覧の方が多ければそちらの数を保つ)
        foreach (var group in reserved.Values.GroupBy(r => r))
        {
            int had = assignRoleList.RemoveAll(r => r == group.Key);
            for (int i = Math.Max(had, group.Count()); i > 0; i--)
                assignRoleList.Insert(0, group.Key);
        }
    }

    public static PlayerControl TakePlayer(CustomRoles role, List<PlayerControl> candidates)
    {
        var playerId = byte.MaxValue;
        foreach (var pair in reserved)
        {
            if (pair.Value != role) continue;
            if (!candidates.Any(pc => pc.PlayerId == pair.Key)) continue;
            playerId = pair.Key;
            break;
        }
        if (playerId == byte.MaxValue) return null;

        reserved.Remove(playerId);
        return candidates.First(pc => pc.PlayerId == playerId);
    }

    /// <summary>
    /// 役職を渡す人を選ぶ。その役職の予約があればその人、無ければ他の役職を予約していない人から無作為に選ぶ
    /// (後の周で配るデシンクの役職の予約を、先に配る役職が取らないように)。
    /// 予約していない人が残っていなければ null (この役職は配らない。人数を超えた役職が配られないのと同じ扱い)
    /// </summary>
    public static PlayerControl PickPlayer(CustomRoles role, List<PlayerControl> candidates, IRandom rand)
    {
        var picked = TakePlayer(role, candidates);
        if (picked != null) return picked;
        if (reserved.Count == 0) return candidates[rand.Next(candidates.Count)];
        var free = candidates.Where(pc => !reserved.ContainsKey(pc.PlayerId)).ToList();
        return free.Count > 0 ? free[rand.Next(free.Count)] : null;
    }
}
