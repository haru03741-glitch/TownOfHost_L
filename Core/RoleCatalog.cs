using System.Collections.Generic;
using UnityEngine;

using TownOfHostL;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.AddOns;

namespace TownOfHostL.Core;

/// <summary>1 つの役職の分類・陣営・色。起動後は変わらない。</summary>
public sealed class RoleDef
{
    public readonly CustomRoles Role;
    public readonly CustomRoleTypes Team;
    public readonly RoleFlags Flags;
    /// <summary>役職クラスの登録。クラスを持たない役職は null</summary>
    public readonly SimpleRoleInfo Info;
    public readonly string ColorCode;
    public readonly Color Color;
    public readonly bool HasColor;

    public RoleDef(CustomRoles role, CustomRoleTypes team, RoleFlags flags, SimpleRoleInfo info, string colorCode)
    {
        Role = role;
        Team = team;
        Flags = flags;
        Info = info;
        ColorCode = colorCode;
        // 読めない色コードでも、本編の TryParseHtmlString が返す色をそのまま持つ
        HasColor = ColorUtility.TryParseHtmlString(colorCode, out Color);
    }
    public bool Has(RoleFlags flags) => (Flags & flags) != 0;
}

/// <summary>
/// 役職ごとの分類の表。役職クラスを持つ役職は SimpleRoleInfo から、属性は AddOnInfo から、どちらも持たない役職は下の Unclassed から組む。
/// 起動時に全役職クラスの登録が済んだ所で Build し、以降は enum の値で配列を引くだけにする。
/// </summary>
public static class RoleCatalog
{
    private const CustomRoleTypes Crew = CustomRoleTypes.Crewmate;

    /// <summary>役職クラスも属性の定義も持たない役職の陣営・色・分類</summary>
    private static readonly (CustomRoles role, CustomRoleTypes team, string color, RoleFlags flags)[] Unclassed =
    {
        (CustomRoles.NotAssigned, Crew, "#ffffff", RoleFlags.None),
        (CustomRoles.GM, Crew, "#ff5b70", RoleFlags.NoTasks | RoleFlags.Spectator | RoleFlags.DontShowOption | RoleFlags.NoComingOutOption),
        // 本編の役職のうち、役職クラスを持たない物 (Normal 版で配る)
        (CustomRoles.Phantom, Crew, null, RoleFlags.Vanilla | RoleFlags.NoComingOutOption),
        (CustomRoles.Tracker, Crew, null, RoleFlags.Vanilla | RoleFlags.NoComingOutOption),
        (CustomRoles.SKMadmate, CustomRoleTypes.Madmate, "#ff1919", RoleFlags.DontShowOption | RoleFlags.FakeTasksLabel | RoleFlags.NoTasks),

        (CustomRoles.Archenemy, Crew, "#ff6347", RoleFlags.FirstWriteAddOn | RoleFlags.TasksNotCountedForWin | RoleFlags.NoTeamWin),

        // HideAndSeek
        (CustomRoles.HASFox, CustomRoleTypes.Neutral, "#e478ff", RoleFlags.NoTasks | RoleFlags.NotCounted),
        (CustomRoles.HASTroll, CustomRoleTypes.Neutral, "#00ff00", RoleFlags.NoTasks | RoleFlags.NotCounted),

        // CatchCat
        (CustomRoles.CCRedLeader, CustomRoleTypes.Impostor, "#ff0000", RoleFlags.CCLeader),
        (CustomRoles.CCBlueLeader, Crew, "#00b4eb", RoleFlags.CCLeader),
        (CustomRoles.CCYellowLeader, Crew, "#f8cd46", RoleFlags.CCLeader),
        (CustomRoles.CCNoCat, Crew, "#696969", RoleFlags.CCNoCat),
        (CustomRoles.CCRedCat, Crew, "#ff0000", RoleFlags.CCColorCat),
        (CustomRoles.CCBlueCat, Crew, "#00b4eb", RoleFlags.CCColorCat),
        (CustomRoles.CCYellowCat, Crew, "#f8cd46", RoleFlags.CCColorCat),
    };

    // NotAssigned (-1) から MaxAddon までを、enum の値 + 1 の位置に置く
    private const int Offset = 1;
    private static readonly int Size = (int)CustomRoles.MaxAddon + Offset + 1;
    private static RoleDef[] defs;
    // 表の外の値 (DummyNext など) は、Build の後に一度組んだら覚えておく
    private static readonly Dictionary<CustomRoles, RoleDef> outside = new();
    private static readonly Dictionary<CustomRoles, (CustomRoleTypes team, string color, RoleFlags flags)> unclassedMap = BuildUnclassedMap();

    /// <summary>全役職クラスの RoleInfo が登録された後に 1 回呼ぶ</summary>
    public static void Build()
    {
        var table = new RoleDef[Size];
        for (int i = 0; i < Size; i++)
            table[i] = Compose((CustomRoles)(i - Offset));
        defs = table;
    }

    public static RoleDef Get(CustomRoles role)
    {
        var table = defs;
        int i = (int)role + Offset;
        if (table == null) return Compose(role); // Build の前は覚えずにその場で組む
        if ((uint)i < (uint)table.Length) return table[i];
        if (!outside.TryGetValue(role, out var def)) outside[role] = def = Compose(role);
        return def;
    }

    private static RoleDef Compose(CustomRoles role)
    {
        var info = role.GetRoleInfo();
        var addOn = AddOnList.Get(role);
        var hasUnclassed = unclassedMap.TryGetValue(role, out var u);

        // 属性は陣営を決めないのでクルー
        var team = info?.CustomRoleType ?? (hasUnclassed ? u.team : Crew);
        var flags = (info?.Flags ?? RoleFlags.None) | (addOn?.Flags ?? RoleFlags.None) | (hasUnclassed ? u.flags : RoleFlags.None);
        var color = addOn?.ColorCode ?? (hasUnclassed && u.color != null ? u.color : info?.RoleColorCode);
        return new RoleDef(role, team, flags, info, color);
    }

    private static Dictionary<CustomRoles, (CustomRoleTypes, string, RoleFlags)> BuildUnclassedMap()
    {
        var map = new Dictionary<CustomRoles, (CustomRoleTypes, string, RoleFlags)>(Unclassed.Length);
        foreach (var (role, team, color, flags) in Unclassed)
            map.Add(role, (team, color, flags));
        return map;
    }
}
