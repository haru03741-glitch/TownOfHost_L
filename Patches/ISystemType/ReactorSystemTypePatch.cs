using HarmonyLib;
using Hazel;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Patches.ISystemType;

[HarmonyPatch(typeof(ReactorSystemType), nameof(ReactorSystemType.UpdateSystem))]
public static class ReactorSystemTypeUpdateSystemPatch
{
    public static bool Prefix(ReactorSystemType __instance, [HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader, ref bool __state /* 止まっている所からサボを始める電文か */)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }
        // 弾いた時や進行中のサボに、時間の書き換えを当てない
        __state = amount == ReactorSystemType.StartCountdown && !__instance.IsActive;

        if (player.GetRoleClass() is ISystemTypeUpdateHook systemTypeUpdateHook && !systemTypeUpdateHook.UpdateReactorSystem(__instance, amount))
        {
            return false;
        }
        return true;
    }
    public static void Postfix(ReactorSystemType __instance, bool __state, bool __runOriginal)
    {
        // サボタージュ発動時
        if (__state && __runOriginal && __instance.IsActive)
        {
            var duration = (MapNames)Main.NormalOptions.MapId switch
            {
                MapNames.Skeld when Options.SabotageTimeControl_Skeld.GetBool() => Options.SkeldReactorTimeLimit.GetFloat(),
                MapNames.MiraHQ when Options.SabotageTimeControl_MiraHQ.GetBool() => Options.MiraHQReactorTimeLimit.GetFloat(),
                MapNames.Polus when Options.SabotageTimeControl_Polus.GetBool() => Options.PolusReactorTimeLimit.GetFloat(),
                MapNames.Fungle when Options.SabotageTimeControl_Fungle.GetBool() => Options.FungleReactorTimeLimit.GetFloat(),
                _ => float.NaN,
            };
            if (!float.IsNaN(duration))
            {
                __instance.Countdown = duration;
            }
        }
    }
}
