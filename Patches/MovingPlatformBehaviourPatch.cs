using HarmonyLib;

namespace TownOfHostL.Patches;

[HarmonyPatch(typeof(MovingPlatformBehaviour))]
public static class MovingPlatformBehaviourPatch
{
    private static bool isDisabled = false;

    [HarmonyPatch(nameof(MovingPlatformBehaviour.Start)), HarmonyPrefix]
    public static void StartPrefix(MovingPlatformBehaviour __instance)
    {
        isDisabled = Options.DisableAirshipMovingPlatform.GetBool();

        if (isDisabled)
        {
            __instance.transform.localPosition = __instance.DisabledPosition;
            ShipStatus.Instance.Cast<AirshipStatus>().outOfOrderPlat.SetActive(true);
        }
    }
    // 本編の DLL では、この getter は同じ機械語の OverlayKillAnimation.IsClassicAnimation などと 1 つの番地に畳まれていて、
    // この前置きはそれらの呼び出しでも走る。ぬーんの物の時だけ書き換える
    [HarmonyPatch(nameof(MovingPlatformBehaviour.IsDirty), MethodType.Getter), HarmonyPrefix]
    public static bool GetIsDirtyPrefix(MovingPlatformBehaviour __instance, ref bool __result)
    {
        if (isDisabled && __instance is not null
            && Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(__instance.Pointer) == Il2CppInterop.Runtime.Il2CppClassPointerStore<MovingPlatformBehaviour>.NativeClassPtr)
        {
            __result = false;
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(MovingPlatformBehaviour.Use), typeof(PlayerControl)), HarmonyPrefix]
    public static bool UsePrefix([HarmonyArgument(0)] PlayerControl player)
    {
        // プレイヤーがぬーん使用不可状態のときに使用をブロック
        if (!PlayerState.GetByPlayerId(player.PlayerId).CanUseMovingPlatform)
        {
            return false;
        }
        return !isDisabled;
    }
    [HarmonyPatch(nameof(MovingPlatformBehaviour.Use), typeof(PlayerControl)), HarmonyPostfix]
    public static void UsePostfix([HarmonyArgument(0)] PlayerControl player, bool __runOriginal)
    {
        if (__runOriginal) Roles.Neutral.Spelunker.OnMovingPlatformUsed(player);
    }
    [HarmonyPatch(nameof(MovingPlatformBehaviour.SetSide)), HarmonyPrefix]
    public static bool SetSidePrefix() => !isDisabled;
}
