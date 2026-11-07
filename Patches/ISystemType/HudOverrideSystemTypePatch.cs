using HarmonyLib;
using Hazel;
using TownOfHostL.Core;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL.Patches.ISystemType;

[HarmonyPatch(typeof(HudOverrideSystemType), nameof(HudOverrideSystemType.UpdateSystem))]
public static class HudOverrideSystemTypeUpdateSystemPatch
{
    public static bool Prefix(HudOverrideSystemType __instance, [HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }

        var playerRole = player.GetRoleClass();
        var isMadmate = PlayerTraits.IsMadmateForSabotage(player, playerRole);
        if ((amount & HudOverrideSystemType.DamageBit) <= 0 && ((isMadmate && !Options.MadmateCanFixComms.GetBool())
                                                                || PlayerTraits.IsClumsy(player)))
        {
            return false;
        }

        if (playerRole is ISystemTypeUpdateHook systemTypeUpdateHook && !systemTypeUpdateHook.UpdateHudOverrideSystem(__instance, amount))
        {
            return false;
        }
        return true;
    }
    public static void Postfix()
    {
        Camouflage.CheckCamouflage();
        Utils.NotifyRoles();
    }
}
