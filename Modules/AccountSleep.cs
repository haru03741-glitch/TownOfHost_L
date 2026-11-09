using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TownOfHostL.Core.Modes;
using UnityEngine;

namespace TownOfHostL.Modules;

// Based on https://github.com/haru03741-glitch/More-Roles-Plus (src/Menu/AccountSleep.cs, GPL-3.0)
// その元は https://github.com/waffle-ful/Aeterna-End-K-not (Patches/CalamityMenu/VanillaSuppressor.cs・AccountDialogWake.cs, GPL-3.0)
// メニューのアカウント表示 (AccountManager) は、ログインが終わるまで (メニューが出てから数秒) 黒い幕で
// メニュー全体を押せなくする。起動直後のメニューでだけ止めておき、メニューをすぐ使えるようにする。
// 起こすのは: メニューの起動処理が終わった時 (アカウント欄が戻る)・マイアカウントを押した時・
// 本編がログイン失敗や年齢確認などの画面を出す時 (止めたままだと見えない画面がボタンを待ち、ログインが進まない)。
// 止めている間はアカウント欄が出ないので、ログインが終わるまでメニューの左上が空く。
// メニューでしか効かず電文も送らないので、どのモードでも当てておく
[HarmonyPatch, PatchBundle(PatchBundles.Core)]
internal static class AccountSleep
{
    private static bool asleep;
    private static bool sleptBeforeLogin; // 本編の幕を外したまま、ログインがまだ終わっていない
    private static bool continuingWithoutMenu; // メニューの無いまま、ログインの続きを回した
    private static bool loginFailed; // ログインに失敗した (トークン更新を待たせ続けない)
    private static MainMenuManager menu;

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]
    private static void SleepOnMenu(MainMenuManager __instance)
    {
        // 前のメニューが起動処理を終える前に消えていたら、止めたままにしない
        if (asleep && !menu) Wake("previous menu gone");
        continuingWithoutMenu = false;
        if (asleep || !Main.SleepAccountMenu.Value) return;
        var eos = EOSManager.Instance;
        if (!eos || eos.loginFlowFinished) return; // ログイン済みでメニューへ戻った時は本編のまま
        if (!AccountManager.InstanceExists) return;
        var am = AccountManager.Instance;
        if (!am || !am.gameObject.activeSelf) return;
        am.gameObject.SetActive(false);
        // 本編は右の窓のマスクをログインが終わってから有効にする。窓の中の絵はマスクの内側だけ描くので、
        // 幕を外したままだとログインまで右の窓 (背景・オンラインの画面のボタン) が真っ黒になる
        if (__instance.rightPanelMask) __instance.rightPanelMask.SetActive(true);
        // 本編はログインの途中 (権限の確認が済むまで) 「オンライン」を押せなくしている。
        // 部屋作りの通信は下の HoldTokenRefresh でログインまで待たせるので、メニューが出たらすぐ押せるようにする
        var online = __instance.PlayOnlineButton;
        if (online)
        {
            online.SetButtonEnableState(true);
            var sprite = online.GetComponentInChildren<PlayOnlineButtonSprite>();
            if (sprite) sprite.SetState(true);
        }
        menu = __instance;
        asleep = true;
        sleptBeforeLogin = true;
        Logger.Info("asleep until the menu finishes starting up", "AccountSleep");
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate)), HarmonyPostfix]
    private static void WatchStartup(MainMenuManager __instance)
    {
        if (!sleptBeforeLogin || !__instance.finishStartup) return;
        sleptBeforeLogin = false; // ログインはメニューの上で終わった
        if (asleep && __instance == menu) Wake("startup finished");
    }

    private static void Wake(string why)
    {
        if (!asleep) return;
        asleep = false;
        menu = null;
        try
        {
            if (!AccountManager.InstanceExists) return;
            var am = AccountManager.Instance;
            if (am && !am.gameObject.activeSelf) am.gameObject.SetActive(true);
            Logger.Info($"woken ({why})", "AccountSleep");
        }
        catch (System.Exception e) { Logger.Warn($"wake failed: {e.Message}", "AccountSleep"); }
    }

    // ログインの途中でメニューから別の画面へ移ると、本編のログインの最終段がメニューと一緒に消えた物
    // (排出ボタン・ショップ) を触って例外で止まり、フレンドコードもログイン完了も来ないまま
    // ローカル扱いになる。メニューが無ければ排出ボタンの演出を飛ばして続き (保存待ち→フレンドコード→ショップ→完了) だけ始める
    [HarmonyPatch(typeof(EOSManager), nameof(EOSManager.BeginFinalPartsOfLoginFlow)), HarmonyPrefix]
    private static bool MenuGoneLoginFinish(EOSManager __instance)
    {
        if (!sleptBeforeLogin || Object.FindObjectOfType<MainMenuManager>()) return true;
        sleptBeforeLogin = false;
        continuingWithoutMenu = true;
        Wake("menu gone");
        Logger.Info("menu gone before login finished, continuing without it", "AccountSleep");
        __instance.StartCoroutine(__instance.WaitForStorageToSave());
        return false;
    }

    // 続きの中のショップの初期化も、消えたメニューのショップなら飛ばす
    [HarmonyPatch(typeof(StoreMenu), nameof(StoreMenu.Initialize)), HarmonyPrefix]
    private static bool StoreGoneGuard(StoreMenu __instance)
    {
        if (!continuingWithoutMenu || __instance) return true;
        continuingWithoutMenu = false;
        Logger.Info("store gone before login finished, skipped its setup", "AccountSleep");
        return false;
    }

    // ログインの途中で部屋を作る・探すと、本編のマッチメーカーのトークン更新が UserIDToken の NullReference で止まり、
    // そのプロセスでは以後ずっと部屋が作れなくなる (2026-11-08 実機で確認。アカウント表示を止めなくても起きる)。
    // 画面は止めず、トークン更新だけをログインが終わるまで待たせる。確定の後の「接続中」がその分だけ延びる
    [HarmonyPatch(typeof(HttpMatchmakerManager), nameof(HttpMatchmakerManager.CoRefreshTokenInternal)), HarmonyPostfix]
    private static void HoldTokenRefresh(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (LoginSettled()) return;
        __result = WaitForLogin(__result).WrapToIl2Cpp();
    }

    private static System.Collections.IEnumerator WaitForLogin(Il2CppSystem.Collections.IEnumerator original)
    {
        float start = Time.realtimeSinceStartup;
        Logger.Info("token refresh held until login finishes", "AccountSleep");
        while (!LoginSettled()) yield return null;
        Logger.Info($"token refresh resumed after {Time.realtimeSinceStartup - start:F1}s", "AccountSleep");
        while (original.MoveNext()) yield return original.Current;
    }

    private static bool LoginSettled()
    {
        if (loginFailed) return true;
        var eos = EOSManager.Instance;
        return !eos || eos.loginFlowFinished;
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenAccountMenu)), HarmonyPrefix]
    private static void OpenAccountMenu() => Wake("my account");

    // 本編のアカウント画面を出す入口。asleep の bool だけ見て抜けるので、メニュー以外で呼ばれても軽い
    [HarmonyPatch]
    private static class DialogPatches
    {
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.SignInFail))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.SignInSuccess))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.PlatformSignInFail))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.ShowAgeGate))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.ShowPermissionsRequestForm))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.ShowGuardianEmailSentConfirm))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.EditGuardianEmail))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.UpdateMissingGuardianEmail))]
        [HarmonyPatch(typeof(SignInScreen), nameof(SignInScreen.Open))]
        [HarmonyPatch(typeof(PrivacyPolicyScreen), nameof(PrivacyPolicyScreen.Show))]
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (asleep) Wake("account dialog");
        }
    }

    // ログインに失敗したら、そこから先は本編の画面に任せる (ログインが終わらないままトークン更新を止め続けない)
    [HarmonyPatch]
    private static class SignInFailPatches
    {
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.SignInFail))]
        [HarmonyPatch(typeof(AccountManager), nameof(AccountManager.PlatformSignInFail))]
        [HarmonyPrefix]
        public static void Prefix()
        {
            loginFailed = true;
            sleptBeforeLogin = false;
        }
    }
}
