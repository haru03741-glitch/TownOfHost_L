using HarmonyLib;
using Hazel;

namespace TownOfHostL;

/// <summary>モードが時間切れで起こす致命的な妨害 (原子炉・酸素・ヘリ)。起こした妨害は、モードの間は直せない</summary>
public static class HamoSabotageTrigger
{
    static float countdown = -1f;
    public static bool Active;

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        countdown = -1f;
        Active = false;
    }

    static bool BlocksRepair => Active && Options.CurrentGameMode is CustomGameMode.SuddenDeath;

    public static void Critical(float seconds)
    {
        if (!AmongUsClient.Instance.AmHost || ShipStatus.Instance == null) return;
        countdown = seconds;
        Active = true;
        ShipStatus.Instance.RpcUpdateSystem(Utils.GetCriticalSabotageSystemType(), 128);
    }

    [HarmonyPatch(typeof(ReactorSystemType), nameof(ReactorSystemType.UpdateSystem))]
    static class Reactor
    {
        static bool Prefix([HarmonyArgument(1)] MessageReader msgReader)
        {
            if (!BlocksRepair) return true;
            var r = MessageReader.Get(msgReader);
            var amount = r.ReadByte();
            r.Recycle();
            return amount == 128;
        }
        static void Postfix(ReactorSystemType __instance)
        {
            if (countdown <= 0 || !__instance.IsActive) return;
            if (__instance.Countdown > countdown) __instance.Countdown = countdown;
        }
    }

    [HarmonyPatch(typeof(LifeSuppSystemType), nameof(LifeSuppSystemType.UpdateSystem))]
    static class LifeSupp
    {
        static bool Prefix([HarmonyArgument(1)] MessageReader msgReader)
        {
            if (!BlocksRepair) return true;
            var r = MessageReader.Get(msgReader);
            var amount = r.ReadByte();
            r.Recycle();
            return amount == 128;
        }
        static void Postfix(LifeSuppSystemType __instance)
        {
            if (countdown <= 0 || !__instance.IsActive) return;
            if (__instance.Countdown > countdown) __instance.Countdown = countdown;
        }
    }

    [HarmonyPatch(typeof(HeliSabotageSystem), nameof(HeliSabotageSystem.UpdateSystem))]
    static class Heli
    {
        static bool Prefix([HarmonyArgument(1)] MessageReader msgReader)
        {
            if (!BlocksRepair) return true;
            var r = MessageReader.Get(msgReader);
            var amount = r.ReadByte();
            r.Recycle();
            return amount == 128;
        }
        static void Postfix(HeliSabotageSystem __instance)
        {
            if (countdown <= 0 || !__instance.IsActive) return;
            if (__instance.Countdown > countdown) __instance.Countdown = countdown;
        }
    }
}
