using System;
using HarmonyLib;

namespace TownOfHostL.Patches;

// 試合開始時、本編のホストは客のマップ読み込み完了を 10 秒 (Airship/Fungle は 15 秒) しか待たず、
// 間に合わない客を ClientTimeout で切断する。スマホなど読み込みの遅い端末は 12 秒以上かかることがあり、
// 開始と同時に落とされる。本編の処理はそのまま使い、待ちの起点 (_start) を寄せ続けて上限を 25 秒
// (Airship/Fungle は 30 秒) に延ばす。全員の読み込みが終われば本編がすぐに先へ進むので、速い部屋は待たない。
// 本編の更新でコルーチンの型名 (_CoStartGameHost_d__NN) が変わった時はビルドが通らなくなるので、ここを直す。
[HarmonyPatch(typeof(AmongUsClient._CoStartGameHost_d__28), nameof(AmongUsClient._CoStartGameHost_d__28.MoveNext))]
static class StartGameLoadWaitPatch
{
    const double WaitSeconds = 25;
    const double WaitSecondsLargeMap = 30;

    static long lastWrittenTicks;
    static Il2CppSystem.DateTime realStart;
    static bool loggedOverVanilla;

    static void Prefix(AmongUsClient._CoStartGameHost_d__28 __instance)
    {
        try
        {
            var start = __instance._start_5__2;
            if (start.Ticks == 0) return; // まだ待ちに入っていない

            // 本編が書いた起点 (= 自分が最後に書いた値でも、覚えている起点でもない値) を見たら新しい待ちとして覚え直す。
            // コルーチンのアドレスは次の試合で使い回されることがあるので、アドレスでは判定しない。
            if (start.Ticks != lastWrittenTicks && start.Ticks != realStart.Ticks)
            {
                realStart = start;
                loggedOverVanilla = false;
                Logger.Info("客の読み込み待ちを開始", "StartGameLoadWait");
            }

            var now = Il2CppSystem.DateTime.Now;
            double elapsed = (now.Ticks - realStart.Ticks) / (double)TimeSpan.TicksPerSecond;
            byte mapId = (byte)GameMap.Current;
            double limit = mapId is 4 or 5 ? WaitSecondsLargeMap : WaitSeconds;
            if (!loggedOverVanilla && elapsed >= (mapId is 4 or 5 ? 15 : 10))
            {
                loggedOverVanilla = true;
                Logger.Info($"本編の上限を超えて待機中 ({elapsed:F1}秒 / 上限 {limit}秒)", "StartGameLoadWait");
            }
            // 上限までは本編から見た経過を 0 に保ち、上限を過ぎたら本当の起点に戻して本編に切断させる
            var next = elapsed < limit ? now : realStart;
            lastWrittenTicks = next.Ticks;
            __instance._start_5__2 = next;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "StartGameLoadWait");
        }
    }
}
