using HarmonyLib;
using Hazel;
using TownOfHostL.Core;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL.Patches.ISystemType;

[HarmonyPatch(typeof(HqHudSystemType), nameof(HqHudSystemType.UpdateSystem))]
public static class HqHudSystemTypeUpdateSystemPatch
{
    public static bool Prefix(HqHudSystemType __instance, [HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }

        var tags = (HqHudSystemType.Tags)(amount & HqHudSystemType.TagMask);
        var playerRole = player.GetRoleClass();
        var isMadmate = PlayerTraits.IsMadmateForSabotage(player, playerRole);
        if (tags == HqHudSystemType.Tags.FixBit && ((isMadmate && !Options.MadmateCanFixComms.GetBool())
                                                    || PlayerTraits.IsClumsy(player)))
        {
            return false;
        }

        if (playerRole is ISystemTypeUpdateHook systemTypeUpdateHook && !systemTypeUpdateHook.UpdateHqHudSystem(__instance, amount))
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
