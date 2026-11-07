using HarmonyLib;

namespace TownOfHostL.Patches;

// 接続の時に「mod 入りのホスト」と名乗り、手元でもそう振る舞う。
// 公式サーバーは接続時の名乗りを覚えていて、mod 入りのホストなら PlayerInfo をホストが作り、そうでなければサーバーが作る。
// 名乗りと手元の振る舞いが食い違うと、ホストが自分のプレイヤーを作らなかったり、作ってはいけない PlayerInfo を作って切断される。
// そのため、かくれんぼの間に TOHL を休ませても外さないよう、属性ではなく常駐の Harmony (Core.VanillaHideAndSeek) から当てる。
public static class ConstantsPatch
{
    public static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Constants), nameof(Constants.GetBroadcastVersion)),
            postfix: new HarmonyMethod(typeof(ConstantsPatch), nameof(GetBroadcastVersionPostfix)));
        harmony.Patch(AccessTools.Method(typeof(Constants), nameof(Constants.IsVersionModded)),
            prefix: new HarmonyMethod(typeof(ConstantsPatch), nameof(IsVersionModdedPrefix)));
    }

    private static void GetBroadcastVersionPostfix(ref int __result)
    {
        if (GameStates.IsLocalGame) return;

        __result += 25;
    }

    private static bool IsVersionModdedPrefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}
