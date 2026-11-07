using HarmonyLib;

using TownOfHostL.Roles.Core;
using TownOfHostL.Patches;

namespace TownOfHostL
{
    //参考元:https://github.com/yukieiji/ExtremeRoles/blob/master/ExtremeRoles/Patches/AirShipStatusPatch.cs
    [HarmonyPatch(typeof(AirshipStatus), nameof(AirshipStatus.PrespawnStep))]
    public static class AirshipStatusPrespawnStepPatch
    {
        public static bool Prefix()
        {
            if (PlayerControl.LocalPlayer.GetCustomRole().IsSpectator() && // 観戦者は湧き画面をスキップ
                !RandomSpawn.CustomNetworkTransformPatch.FirstTP[PlayerControl.LocalPlayer.PlayerId])
                return false;

            return true;
        }
    }
}