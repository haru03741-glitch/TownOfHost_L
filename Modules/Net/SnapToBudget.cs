using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL.Modules.Net;

// ホストが送る SnapTo の本数を管理する。公式サーバーは SnapTo を直近約6秒で約330本送るとホストを切断する
// (送る速さには関係なく本数で切られる)。
// 公式サーバーでは、最大100本・毎秒4本ずつ回復する財布から1本ずつ払う。任意の6秒で送れるのは約124本で、
// 切断される本数の4割に収まる。残りは、数を決められない送り元 (死亡に伴う1本など) と大人数の余裕に回す。
// 役職の TP は送る前に CanSend で確かめ、財布が空ならその TP を丸ごとやめる (ホストの側だけ動かすと、客と位置がずれる)。
public static class SnapToBudget
{
    const float Capacity = 100f;
    const float RefillPerSecond = 4f;
    // 止められない送信で財布が借りになっても、空けて待つのは最長でこの本数ぶんまで
    const float MaxDebt = 20f;

    const float WarnWindow = 6f;
    const int WarnCount = 200;

    static float tokens = Capacity;
    static float refilledAt;
    static float lastRefuseLogAt = -1f;
    static readonly Queue<float> sentAt = new();
    static float lastWarnAt = -WarnWindow;

    static void Refill(float now)
    {
        tokens = Mathf.Min(Capacity, tokens + (now - refilledAt) * RefillPerSecond);
        refilledAt = now;
    }

    // SnapTo を 1 本送ってよいか。公式サーバー以外では常に true。数えるのは Record (送信の書き込み口) で行う。
    public static bool CanSend()
    {
        if (!OfficialServer.IsActive) return true;
        float now = Time.realtimeSinceStartup;
        Refill(now);
        if (tokens >= 1f) return true;

        if (now - lastRefuseLogAt > 1f)
        {
            lastRefuseLogAt = now;
            Logger.Warn("SnapTo の送信が多すぎるため、TP を取りやめました", "SnapToBudget");
        }
        return false;
    }

    // ホストが送った SnapTo を 1 本数える。止められない送信もここで払うので、財布は借りになることがある。
    public static void Record()
    {
        float now = Time.realtimeSinceStartup;
        Refill(now);
        tokens = Mathf.Max(-MaxDebt, tokens - 1f);

        sentAt.Enqueue(now);
        while (sentAt.Count > 0 && now - sentAt.Peek() > WarnWindow) sentAt.Dequeue();
        if (sentAt.Count >= WarnCount && now - lastWarnAt > WarnWindow)
        {
            lastWarnAt = now;
            Logger.Warn($"SnapTo が直近{WarnWindow}秒で{sentAt.Count}本 (公式サーバーは約330本で切断)", "SnapToBudget");
        }
    }
}
