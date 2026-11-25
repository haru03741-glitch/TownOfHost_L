using HarmonyLib;
using Hazel;

namespace TownOfHostL.Patches.ISystemType;

[HarmonyPatch(typeof(SecurityCameraSystemType), nameof(SecurityCameraSystemType.UpdateSystem))]
public static class SecurityCameraSystemTypeUpdateSystemPatch
{
    public static bool Prefix([HarmonyArgument(1)] MessageReader msgReader)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }

        // カメラ無効時，バニラプレイヤーはカメラを開けるので点滅させない
        if (amount == SecurityCameraSystemType.IncrementOp)
        {
            var camerasDisabled = GameMap.Base switch
            {
                MapNames.Skeld => Options.DisableCamera_Skeld.GetBool(),
                MapNames.Polus => Options.DisableCamera_Polus.GetBool(),
                MapNames.Airship => Options.DisableCamera_Airship.GetBool(),
                _ => false,
            };
            return !camerasDisabled;
        }
        return true;
    }
}
