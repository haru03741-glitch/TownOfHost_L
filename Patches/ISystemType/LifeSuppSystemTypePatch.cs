using HarmonyLib;
using Hazel;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Patches.ISystemType;

[HarmonyPatch(typeof(LifeSuppSystemType), nameof(LifeSuppSystemType.UpdateSystem))]
public static class LifeSuppSystemUpdateSystemPatch
{
    // 酸素のサボタージュを始める値 (原子炉の ReactorSystemType.StartCountdown と同じ)
    private const byte StartCountdown = 128;

    public static bool Prefix(LifeSuppSystemType __instance, [HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader, ref bool __state /* 止まっている所からサボを始める電文か */)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }
        // 弾いた時や進行中のサボに、時間の書き換えを当てない
        __state = amount == StartCountdown && !__instance.IsActive;

        if (player.GetRoleClass() is ISystemTypeUpdateHook systemTypeUpdateHook && !systemTypeUpdateHook.UpdateLifeSuppSystem(__instance, amount))
        {
            return false;
        }
        return true;
    }
    public static void Postfix(LifeSuppSystemType __instance, bool __state, bool __runOriginal)
    {
        if (!__state || !__runOriginal || !__instance.IsActive || !AmongUsClient.Instance.AmHost) return;
        var duration = (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Skeld when Options.SabotageTimeControl_Skeld.GetBool() => Options.SkeldO2TimeLimit.GetFloat(),
            MapNames.MiraHQ when Options.SabotageTimeControl_MiraHQ.GetBool() => Options.MiraHQO2TimeLimit.GetFloat(),
            _ => float.NaN,
        };
        if (float.IsNaN(duration)) return;
        __instance.Countdown = duration;
        __instance.IsDirty = true;
    }
}
