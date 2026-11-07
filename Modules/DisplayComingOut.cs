using System.Collections.Generic;
using System.Linq;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

class DisplayComingOut
{
    // CO可否表示
    private static OptionItem Enable;
    private static Dictionary<CustomRoleTypes, OptionItem> EachTypes = [];
    private static Dictionary<CustomRoles, OptionItem> EachRoles = [];

    private static readonly string[] displayComingOut =
    [
        "displayComingOut.None", "displayComingOut.OK", "displayComingOut.Limit", "displayComingOut.NG",
    ];
    public enum EnableComingOut
    {
        None,
        OK,
        Limit,
        NG,
    }
    /// <summary>役職ごとの設定が無い役職 (NoComingOutOption) は None</summary>
    public static EnableComingOut GetEnableComingOut(CustomRoles role)
        => EachRoles.TryGetValue(role, out var option) ? (EnableComingOut)option.GetValue() : EnableComingOut.None;

    public static void SetupCustomOption(int id)
    {
        Enable = BooleanOptionItem.Create(id + 0, "DisplayComingOutEnable", false, TabGroup.ModMainSettings, true).SetColor(Palette.CrewmateBlue);

        SetupTypeComingOut(id);
    }
    private static void SetupTypeComingOut(int baseId)
    {
        int idOffset = baseId + 1;
        foreach (var type in CustomRolesHelper.AllRoleTypes)
        {
            if (type is CustomRoleTypes.Unit) continue;

            Dictionary<string, string> replacementDic = new() { { "%type%", Translator.GetString($"CustomRoleTypes.{type}") } };
            EachTypes[type] = BooleanOptionItem.Create(idOffset, "displayComingOut%type%", false, TabGroup.ModMainSettings, true).SetParent(Enable).SetKeyName($"displayComingOut.{type}");
            EachTypes[type].ReplacementDictionary = replacementDic;

            SetupRoleComingOut(type, baseId);

            idOffset++;
        }
    }
    private static void SetupRoleComingOut(CustomRoleTypes type, int baseId)
    {
        int idOffset = baseId + ((int)type + 1) * 100;
        foreach (var role in CustomRolesHelper.AllStandardRoles.Where(x => x.GetCustomRoleTypes() == type).ToArray())
        {
            if (role.HasRoleFlag(RoleFlags.NoComingOutOption)) continue;

            var roleName = Utils.GetRoleName(role);
            Dictionary<string, string> replacementDic = new() { { "%role%", Utils.ColorString(Utils.GetRoleColor(role), roleName) } };
            EachRoles[role] = StringOptionItem.Create(idOffset, "displayComingOut%role%", displayComingOut, 0, TabGroup.ModMainSettings, true).SetParent(EachTypes[type]).SetKeyName($"displayComingOut.{role}");
            EachRoles[role].ReplacementDictionary = replacementDic;
            idOffset++;
        }
    }

    public static string GetString(CustomRoles role)
    {
        if (!Enable.GetBool()) return string.Empty;
        if (!EachTypes.TryGetValue(role.GetCustomRoleTypes(), out var typeOption) || !typeOption.GetBool()) return string.Empty;

        string coStr = string.Empty;
        switch (GetEnableComingOut(role))
        {
            case EnableComingOut.None:
                coStr = string.Empty;
                break;
            case EnableComingOut.OK:
                coStr = "<color=#c8e7fa>CO○</color><color=#ffffff>)</color>";
                break;
            case EnableComingOut.Limit:
                coStr = "<color=#ffd700>CO△</color><color=#ffffff>)</color>";
                break;
            case EnableComingOut.NG:
                coStr = "<color=#ff6347>CO×</color><color=#ffffff>)</color>";
                break;
        }
        return coStr;
    }

}