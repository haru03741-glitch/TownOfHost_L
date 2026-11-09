using System;
using AmongUs.Matchmaking;
using HarmonyLib;
using InnerNet;
using TownOfHostL.Core.Modes;

namespace TownOfHostL.Modules.Net;

// 本編の Mod GUID 登録 (Among Us 18.0 以降)。https://github.com/Innersloth-LLC/AmongUsModdingInformation
// ykundesu/SuperNewRoles の Patches/ModRegistrationPatch.cs (GPL-3.0) を haru03741-glitch/More-Roles-Plus の src/Net/ModRegistration.cs (GPL-3.0) が
// 部屋探しの画面と中身の無い条件の掃除まで広げた物を元にした。
// 部屋を立てる時に GUID を付けると、matchmaker がその部屋を「同じ mod の部屋」として扱う。
// 部屋を探す時に GUID を付けると、同じ GUID の部屋だけが一覧に出る。
// GUID の付いた部屋は、GUID を付けずに探すと TOHL のクライアントからも見えなくなる (2026-11-09 実機)。
// +25 (ConstantsPatch) で名乗るクライアントの検索には、GUID が無くても通常の部屋が 1 件も出なかった (同日・アジア)。
// コードでの参加は GUID が無くても通るので、客は mod 無しのままで入れる (同日・バニラの Android で確認)。
// GUID はローカルの部屋に付けると入れなくなる。公式以外のサーバーが受け付けるかも分からないので、公式サーバーの時だけ付ける。
// 部屋の作り・探し方だけに効き、電文は送らないので、どのモードでも当てる。
[HarmonyPatch, PatchBundle(PatchBundles.Core)]
public static class ModRegistration
{
    // 一度決めたら変えない (変えると前の版の部屋と別扱いになる)
    public const string Guid = "75e6fd86-d106-4e1f-b7c2-7adbee7ea4c7";

    private static void Set(bool on) => CurrentModRegistration.ModRegistrationGuidString = on ? Guid : string.Empty;

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HostGame)), HarmonyPrefix]
    private static void HostGamePrefix(InnerNetClient __instance)
    {
        var on = __instance.NetworkMode == NetworkModes.OnlineGame && OfficialServer.IsOfficialRegion;
        Set(on);
        Logger.Info($"部屋を立てる: Mod GUID {(on ? "付ける" : "付けない")}", "ModRegistration");
    }

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.JoinGame)), HarmonyPrefix]
    private static void JoinGamePrefix(InnerNetClient __instance)
        => Set(__instance.NetworkMode == NetworkModes.OnlineGame && OfficialServer.IsOfficialRegion);

    [HarmonyPatch(typeof(InnerNetServer), nameof(InnerNetServer.StartAsLocalServer)), HarmonyPrefix]
    private static void StartAsLocalServerPrefix() => Set(false);

    // 「ゲームを探す」は HTTP で一覧を取り、RequestGameList を通らない。画面を開いた時と条件を作り直す時に GUID が読まれる。
    // この画面ではまだ NetworkMode が OnlineGame になっていないので、リージョンだけで決める。
    private static void ForSearch() => Set(Main.OnlyTohlLobbies.Value && OfficialServer.IsOfficialRegion);

    [HarmonyPatch(typeof(FindAGameManager), nameof(FindAGameManager.Start)), HarmonyPrefix]
    private static void FindAGameStartPrefix() => ForSearch();

    [HarmonyPatch(typeof(CurrentModRegistration), nameof(CurrentModRegistration.UpdateFilterSetWithModRegistrationSettings))]
    private static class FilterSetPatch
    {
        // 部屋を立てる・入る時に呼ばれても、HostGame/JoinGame で決めた GUID を探す側の設定で上書きしないよう、部屋探しの画面にいる時だけ決め直す
        private static void Prefix()
        {
            if (UnityEngine.Object.FindObjectOfType<FindAGameManager>() != null) ForSearch();
        }

        // 前に GUID 付きで探した時の mod の条件が、GUID を外した後に中身の無いまま残ることがある。
        // 中身の無い条件を送ると一覧の取得が 400 で失敗するので外す。
        private static void Postfix(GameFilterSet __0)
        {
            try
            {
                var filters = __0?.Filters;
                if (filters == null) return;
                for (int i = filters.Count - 1; i >= 0; i--)
                {
                    var f = filters[i];
                    if (f != null && f.OptionType == GameFilter.FILTER_TYPE_MOD && f.SubFilter == null) filters.RemoveAt(i);
                }
            }
            catch (Exception e)
            {
                Logger.Warn($"mod の条件の掃除に失敗: {e.Message}", "ModRegistration");
            }
        }
    }
}
