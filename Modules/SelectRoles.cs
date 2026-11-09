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
}
