using UnityEngine;

namespace TownOfHostL;

/// <remarks>エアシップの足場とアップロードのタスクの設定は TownOfHost-K が元 (TownOfHost-hamo 経由)。</remarks>
public static partial class Options
{
    public static OptionItem MapModification;
    public static OptionItem AirShipPlatform;
    public static OptionItem UploadDataIsLongTask;

    static void SetupHamoMapTaskOptions()
    {
        MapModification = BooleanOptionItem.Create("MapModification", false, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColorcode("#ccff66");
        AirShipPlatform = StringOptionItem.Create("AirShipPlatform", new[] { "ColoredOff", "AssignAlgorithm.Random", "PlatfromLeft", "PlatfromRight" }, 0, TabGroup.ModMainSettings, false).SetParent(MapModification);
        UploadDataIsLongTask = BooleanOptionItem.Create("UploadDataIsLongTask", false, TabGroup.ModMainSettings, false).SetParent(MapModification);
    }
}

public static class HamoMapTask
{
    static bool Mod => Options.MapModification != null && Options.MapModification.GetBool();

    public static bool UploadDataIsLong => Mod && Options.UploadDataIsLongTask.GetBool();

    public static void SetPlatform()
    {
        if (!AmongUsClient.Instance.AmHost || !Mod) return;
        if (!GameMap.IsAirship) return;
        var airship = Object.FindObjectOfType<AirshipStatus>();
        if (!airship || airship.GapPlatform == null) return;
        switch (Options.AirShipPlatform.GetValue())
        {
            case 1: airship.GapPlatform.SetSide(IRandom.Instance.Next(2) is 0); break;
            case 2: airship.GapPlatform.SetSide(true); break;
            case 3: airship.GapPlatform.SetSide(false); break;
        }
    }
}
