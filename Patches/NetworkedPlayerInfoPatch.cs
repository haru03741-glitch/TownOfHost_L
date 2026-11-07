//参考：TOH-K
using System.Collections.Generic;
using HarmonyLib;

namespace TownOfHostL;



[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
class GameDataSerializePatch
{
   
    public static int SerializeMessageCount;
    
    public static bool DontTouch;

    public static bool Prefix(NetworkedPlayerInfo __instance, ref bool __result)
    {
        if (AmongUsClient.Instance == null || !GameStates.IsInGame)
        {
            __result = true;
            return true;
        }
        if (DontTouch)
        {
            __instance.ClearDirtyBits();
            __result = false;
            return false;
        }
        if (SerializeMessageCount > 0)
        {
            SendRealName(__instance);
            __result = true;
            return true;
        }

        if (!Core.Modes.ModeCatalog.Current.UsesStandardRoles
            || !GameStates.IsMeeting
            || ExileController.Instance != null
            || AntiBlackout.IsCached)
        {
            SendRealName(__instance);
            return true;
        }
        __instance.ClearDirtyBits();
        __result = false;
        return false;
    }
    public static void Postfix(NetworkedPlayerInfo __instance)
    {
        if (swappedInfo == null) return;
        if (swappedInfo == __instance) __instance.DefaultOutfit.PlayerName = swappedName;
        swappedInfo = null;
        swappedName = null;
    }

    // 試合中、ホストの手元の名前はホストから見た表示 (役職・タスク数など) に書き換わっている。
    // PlayerInfo は全員に送られるので、送る間だけ本当の名前に戻し、ホストに見えている情報を他の人へ流さない。
    private static NetworkedPlayerInfo swappedInfo;
    private static string swappedName;

    /// <summary>
    /// 全員に同じ名前を一時的に見せている人の、その名前 (Revealer の追放の画面など)。PlayerInfo もこの名前で送る。
    /// 本当の名前で送ると、送った時点で客の画面の名前が本当の名前に戻ってしまう
    /// </summary>
    public static readonly Dictionary<byte, string> SharedNameOverride = new();
    [Attributes.GameModuleInitializer]
    public static void Init() => SharedNameOverride.Clear();

    private static void SendRealName(NetworkedPlayerInfo info)
    {
        var outfit = info.DefaultOutfit;
        if (outfit == null || !Main.AllPlayerNames.TryGetValue(info.PlayerId, out var realName)) return;
        if (SharedNameOverride.TryGetValue(info.PlayerId, out var sharedName)) realName = sharedName;
        if (outfit.PlayerName == realName) return;
        swappedInfo = info;
        swappedName = outfit.PlayerName;
        outfit.PlayerName = realName;
    }
}

[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.MarkDirty))]
class NetworkedPlayerInfoMarkDirtyPatch
{
    public static bool Prefix() => GameDataSerializePatch.DontTouch is false;
}
