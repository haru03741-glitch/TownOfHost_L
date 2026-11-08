using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;

namespace TownOfHostL;

/// <summary>hamo の見出し用の設定。子の設定をまとめるだけで、値は持たない</summary>
public class ObjectOptionitem : TextOptionItem
{
    public bool IsHedderObject;
    public string ClickActionkey;
    public Func<string> ValueOverride;
    public ObjectOptionitem SetValueOverride(Func<string> value) { ValueOverride = value; return this; }

    public ObjectOptionitem(string name, bool isHeader, string clickAction, TabGroup tab)
        : base(name, 0, tab, false)
    {
        IsHedderObject = isHeader;
        ClickActionkey = clickAction;
    }
    public static ObjectOptionitem Create(string name, bool isHeader, string clickAction, TabGroup tab)
        => new(name, isHeader, clickAction, tab);
    public static ObjectOptionitem Create(SimpleRoleInfo roleInfo, Enum name, bool isHeader, string clickAction, OptionItem parent = null)
        => Create(roleInfo, name.ToString(), isHeader, clickAction, parent);
    public static ObjectOptionitem Create(SimpleRoleInfo roleInfo, string name, bool isHeader, string clickAction, OptionItem parent = null)
    {
        var opt = new ObjectOptionitem(name, isHeader, clickAction, roleInfo.Tab);
        opt.SetParent(parent ?? roleInfo.RoleOption);
        return opt;
    }
    public override bool GetBool() => Parent == null || Parent.GetBool();
    public override string GetString() => ValueOverride != null ? ValueOverride() : base.GetString();
}

/// <summary>hamo の役職選択の設定。役職を順に切り替える</summary>
public class FilterOptionItem : StringOptionItem
{
    public List<CustomRoles> Sele;
    public Func<CustomRoles[]> NotAssin;
    public (bool impostor, bool madmate, bool crewmate, bool neutral, bool addon) roles;

    public FilterOptionItem(string name, int defaultValue, TabGroup tab, bool isSingleValue, bool imp = false, bool mad = false, bool crew = false, bool neu = false, bool addon = false, Func<CustomRoles[]> notassing = null)
        : base(name, defaultValue, tab, isSingleValue, BuildSelections(out var list))
    {
        Sele = list;
        NotAssin = notassing;
        roles = (imp, mad, crew, neu, addon);
    }
    static string[] BuildSelections(out List<CustomRoles> list)
    {
        list = new() { CustomRoles.NotAssigned };
        list.AddRange(EnumHelper.GetAllValues<CustomRoles>().Where(r => r > CustomRoles.NotAssigned && r < CustomRoles.MaxMain && r is not CustomRoles.GM));
        list.AddRange(EnumHelper.GetAllValues<CustomRoles>().Where(r => r > CustomRoles.StartAddon && r < CustomRoles.MaxAddon));
        return list.Select(r => r.ToString()).ToArray();
    }
    public static FilterOptionItem Create(SimpleRoleInfo roleInfo, Enum name, int defaultIndex, bool isSingleValue, OptionItem parent = null, bool imp = false, bool mad = false, bool crew = false, bool neu = false, bool addon = false, Func<CustomRoles[]> notassing = null)
        => Create(roleInfo, name.ToString(), defaultIndex, isSingleValue, parent, imp, mad, crew, neu, addon, notassing);
    public static FilterOptionItem Create(SimpleRoleInfo roleInfo, string name, int defaultIndex, bool isSingleValue, OptionItem parent = null, bool imp = false, bool mad = false, bool crew = false, bool neu = false, bool addon = false, Func<CustomRoles[]> notassing = null)
    {
        var opt = new FilterOptionItem(name, defaultIndex, roleInfo.Tab, isSingleValue, imp, mad, crew, neu, addon, notassing);
        opt.SetParent(parent ?? roleInfo.RoleOption);
        return opt;
    }

    bool IsSelectable(CustomRoles role)
    {
        if (role is CustomRoles.NotAssigned) return true;
        if ((NotAssin?.Invoke() ?? Array.Empty<CustomRoles>()).Contains(role)) return false;
        if (role > CustomRoles.StartAddon) return roles.addon;
        return role.GetCustomRoleTypes() switch
        {
            CustomRoleTypes.Impostor => roles.impostor,
            CustomRoleTypes.Madmate => roles.madmate,
            CustomRoleTypes.Neutral => roles.neutral,
            _ => roles.crewmate,
        };
    }
    public override string GetString()
    {
        var role = GetRole();
        return role is CustomRoles.NotAssigned ? Translator.GetString("Unsettled") : UtilsRoleText.GetRoleColorAndtext(role);
    }
    public CustomRoles GetRole()
    {
        var index = Rule.GetValueByIndex(CurrentValue);
        return index >= 0 && index < Sele.Count ? Sele[index] : CustomRoles.NotAssigned;
    }
    public void SetRoleValue(CustomRoles role)
    {
        var index = Sele.IndexOf(role);
        base.SetValue(index < 0 || !IsSelectable(role) ? 0 : index);
    }
    public override void SetValue(int value, bool doSync = true)
    {
        // 選べない役職は飛ばして、押した向きに進める
        var current = GetValue();
        var step = value >= current ? 1 : -1;
        if (value == 0 && current == Sele.Count - 1) step = 1;
        var count = Sele.Count;
        var next = ((value % count) + count) % count;
        for (int i = 0; i < count && !IsSelectable(Sele[next]); i++)
            next = ((next + step) % count + count) % count;
        base.SetValue(next, doSync);
    }
}

/// <summary>hamo の単独勝利の優先度</summary>
public class SoloWinOption
{
    public static Dictionary<CustomRoles, SoloWinOption> AllData = new();
    public CustomRoles Role { get; private set; }
    public OptionItem OptionWin;
    public SoloWinOption(TabGroup tab, CustomRoles role, CustomRoles roleName = CustomRoles.NotAssigned, int defo = 0, OptionItem parent = null)
    {
        Role = role;
        var rolename = roleName == CustomRoles.NotAssigned ? role : roleName;
        OptionWin = IntegerOptionItem.Create("SoloWinOption", new(0, 50, 1), defo, tab, false)
            .SetParent(parent ?? CustomRoleSpawnChances[role]);
        OptionWin.ReplacementDictionary = new() { { "%role%", Utils.ColorString(Utils.GetRoleColor(rolename), Utils.GetRoleName(rolename)) } };
        AllData.TryAdd(rolename, this);
    }
    public static SoloWinOption Create(SimpleRoleInfo roleInfo, CustomRoles rolename = CustomRoles.NotAssigned, Func<bool> show = null, int defo = 0)
        => new(roleInfo.Tab, roleInfo.RoleName, rolename, defo);
    /// <summary>役職の単独勝利の優先度 (大きいほど優先)</summary>
    public static int GetPriority(CustomRoles role) => AllData.TryGetValue(role, out var d) ? d.OptionWin.GetInt() : 0;
}

/// <summary>hamo のキル距離の上書き設定</summary>
public class OverrideKilldistance
{
    public static Dictionary<CustomRoles, OverrideKilldistance> AllData = new();
    public OptionItem Killdistance;
    public enum KillDistance { KDShort, KDMidium, KDLong }
    public OverrideKilldistance(TabGroup tab, CustomRoles role)
    {
        Killdistance = StringOptionItem.Create("Killdistance", EnumHelper.GetAllNames<KillDistance>(), 0, tab, false)
            .SetParent(CustomRoleSpawnChances[role]);
        AllData.TryAdd(role, this);
    }
    public static OverrideKilldistance Create(SimpleRoleInfo roleInfo, CustomRoles rolename = CustomRoles.NotAssigned)
        => new(roleInfo.Tab, roleInfo.RoleName);
    /// <summary>役職のキル距離の上書きがあれば返す</summary>
    public static bool TryGet(CustomRoles role, out int distance)
    {
        distance = 0;
        if (!AllData.TryGetValue(role, out var data)) return false;
        distance = data.Killdistance.GetValue();
        return true;
    }
}

/// <summary>「根拠の薄いキルは禁止」の注意を出すかの設定</summary>
public static class SaruWarningOption
{
    private static readonly Dictionary<CustomRoles, OptionItem> PerRoleOption = new();
    public static OptionItem AddTo(SimpleRoleInfo roleInfo)
    {
        var opt = BooleanOptionItem.Create("WarnNoSaru", false, roleInfo.Tab, false);
        opt.SetParent(roleInfo.RoleOption);
        PerRoleOption[roleInfo.RoleName] = opt;
        return opt;
    }
    public static bool ShouldWarn(CustomRoles role) => PerRoleOption.TryGetValue(role, out var opt) && opt.GetBool();
}

/// <summary>
/// hamo の「役職に属性を付ける」設定。
/// TOH_L にある属性だけを設定に出し、ゲーム開始時にその役職の人へ付与する。
/// </summary>
public class RoleAddAddons
{
    public static Dictionary<CustomRoles, RoleAddAddons> AllData = new();
    public CustomRoles Role { get; private set; }
    public OptionItem GiveAddons;
    readonly List<(CustomRoles addon, OptionItem option)> grants = new();

    static readonly (CustomRoles addon, string name, bool impostorOk)[] Addons =
    {
        (CustomRoles.AddWatch, "GiveWatching", true),
        (CustomRoles.PlusVote, "GivePlusVote", true),
        (CustomRoles.TieBreaker, "GiveTiebreaker", true),
        (CustomRoles.Autopsy, "GiveAutopsy", true),
        (CustomRoles.Revenger, "GiveRevenger", true),
        (CustomRoles.Guarding, "GiveGuarding", true),
        (CustomRoles.Management, "GiveManagement", true),
        (CustomRoles.AddSeer, "GiveSeeing", true),
        (CustomRoles.AddLight, "GiveLighting", false),
        (CustomRoles.InfoPoor, "GiveInfoPoor", true),
        (CustomRoles.NonReport, "GiveNonReport", true),
        (CustomRoles.Clumsy, "GiveClumsy", true),
        (CustomRoles.Sunglasses, "GiveSunglasses", true),
    };

    public RoleAddAddons(TabGroup tab, CustomRoles role, bool NeutralKiller = false, bool MadMate = false, bool DefaaultOn = false)
    {
        Role = role;
        var isImpostor = role.IsImpostor();
        GiveAddons = BooleanOptionItem.Create("addaddons", DefaaultOn || NeutralKiller, tab, false)
            .SetParent(CustomRoleSpawnChances[role]).SetValueFormat(OptionFormat.None);
        foreach (var (addon, name, impostorOk) in Addons)
        {
            if (!impostorOk && isImpostor) continue;
            var defo = addon switch
            {
                CustomRoles.AddLight => NeutralKiller,
                CustomRoles.Clumsy => MadMate,
                _ => false,
            };
            grants.Add((addon, BooleanOptionItem.Create(name, defo, tab, false).SetParent(GiveAddons)));
        }
        AllData.TryAdd(role, this);
    }
    public static RoleAddAddons Create(SimpleRoleInfo roleInfo, CustomRoles rolename = CustomRoles.NotAssigned, bool NeutralKiller = false, bool MadMate = false, bool DefaaultOn = false)
        => new(roleInfo.Tab, roleInfo.RoleName, NeutralKiller, MadMate, DefaaultOn);

    /// <summary>役職に付ける属性</summary>
    public IEnumerable<CustomRoles> GetGrantAddons()
        => GiveAddons.GetBool() ? grants.Where(g => g.option.GetBool()).Select(g => g.addon) : Enumerable.Empty<CustomRoles>();

    /// <summary>ゲーム開始時、設定された属性をその役職の人へ付ける</summary>
    public static void GrantAll()
    {
        foreach (var pc in Main.AllPlayerControls)
        {
            if (!AllData.TryGetValue(pc.GetCustomRole(), out var data)) continue;
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            foreach (var addon in data.GetGrantAddons())
            {
                if (state.SubRoles.Contains(addon)) continue;
                state.SetSubRole(addon);
                Logger.Info($"{pc.GetNameWithRole()} に {addon} を付与 (役職の属性設定)", "RoleAddAddons");
            }
        }
    }
}

/// <summary>守護者などの、ゲッサーへの守り</summary>
public static class GuessShield
{
    public enum Mode { None, Block, Reflect }
    public static readonly string[] Selections = { "GuessShieldNone", "GuessShieldBlock", "GuessShieldReflect" };
    public static Mode Of(PlayerControl target) => Roles.Hamo.Crewmate.Guardian.GuessShieldFor(target);
}
