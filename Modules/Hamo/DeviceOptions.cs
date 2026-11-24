using UnityEngine;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

/// <remarks>機器の制限の追加の設定は TownOfHost-K が元 (TownOfHost-hamo 経由)。</remarks>
public static partial class Options
{
    public static OptionItem DisableDevicesIgnoreCompleteTask;
    public static OptionItem DisableForceRecordsAdomin;

    static void SetupHamoDeviceOptions()
    {
        DisableDevicesIgnoreCompleteTask = BooleanOptionItem.Create("DisableDevicesIgnoreComleteTask", false, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreCrewmates);
        DisableForceRecordsAdomin = BooleanOptionItem.Create("DisableForceRecordsAdomin", false, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreConditions);
    }
}

public static class HamoDevices
{
    public static bool CrewIgnored(PlayerControl pc)
        => (Options.DisableDevicesIgnoreCrewmates?.GetBool() ?? false) && pc.Is(CustomRoleTypes.Crewmate)
        && (!(Options.DisableDevicesIgnoreCompleteTask?.GetBool() ?? false) || pc.GetPlayerTaskState().IsTaskFinished);

    public static bool ForceRecords
        => (Options.DisableForceRecordsAdomin?.GetBool() ?? false)
        && (Options.DisableRecordsAdmin_Airship?.GetBool() ?? false)
        && (MapNames)Main.NormalOptions.MapId == MapNames.Airship;

    public static bool NearRecords(Vector2 pos)
        => ForceRecords && Vector2.Distance(pos, DisableDevice.DevicePos["AirshipRecordsAdmin"]) <= DisableDevice.UsableDistance();
}
