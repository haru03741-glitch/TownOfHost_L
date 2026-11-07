using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

using TownOfHostL;

namespace TownOfHostL.Core;

/// <summary>
/// 部屋にいるプレイヤーの一覧。
/// 本編の PlayerControl.AllPlayerControls は Il2Cpp のリストで、配列へ写すたびに要素ごとに interop を通る。
/// ここでは写しを 1 フレームに 1 回だけ作り、同じフレームの中では使い回す。
/// 写しは作り直すたびに新しい配列にするので、走査の途中で作り直されても走査中の配列は変わらない。
/// </summary>
[Modes.PatchBundle(Modes.PatchBundles.Core)]
public static class Players
{
    private static PlayerControl[] all = Array.Empty<PlayerControl>();
    private static readonly PlayerControl[] byId = new PlayerControl[256];
    private static int builtFrame = -1;
    private static int builtSourceCount = -1;
    private static bool dirty = true;

    private static readonly Func<PlayerControl, bool> isAlive = static pc => pc.IsAlive();
    private static readonly Func<PlayerControl, bool> isDead = static pc => !pc.IsAlive();

    private static readonly Func<PlayerControl, bool> notDestroyed = static pc => pc != null;

    /// <summary>部屋にいる全員 (破棄済みを除く)。破棄されたかは走査した時点で判定する。</summary>
    public static IEnumerable<PlayerControl> All
    {
        get
        {
            Refresh();
            return all.Where(notDestroyed);
        }
    }

    /// <summary>生きている人。生死は走査した時点で判定する。</summary>
    public static IEnumerable<PlayerControl> Alive => All.Where(isAlive);

    /// <summary>死んでいる人。生死は走査した時点で判定する。</summary>
    public static IEnumerable<PlayerControl> Dead => All.Where(isDead);

    public static int Count => All.Count();

    /// <summary>playerId から引く。いなければ null (破棄済みの Unity オブジェクトではなく本当の null)。</summary>
    public static PlayerControl ById(byte playerId)
    {
        Refresh();
        var pc = byId[playerId];
        return pc != null ? pc : null;
    }

    /// <summary>次に読んだ時に作り直させる。プレイヤーの出入りで呼ぶ。</summary>
    public static void Invalidate()
    {
        dirty = true;
        clientsDirty = true;
    }

    // ---- クライアント (接続) ----
    private static readonly InnerNet.ClientData[] clientByPlayerId = new InnerNet.ClientData[256];
    private static int clientsBuiltFrame = -1;
    private static int clientsBuiltSourceCount = -1;
    private static bool clientsDirty = true;
    private static readonly int[] missRetriedFrame = new int[256];

    /// <summary>
    /// playerId のプレイヤーを操作している接続。いなければ null。
    /// 接続にキャラクターが割り当たるのは接続より後なので、見つからない時は作り直して引き直す。
    /// 引き直しは同じ id につき 1 フレームに 1 回まで。接続の無い人 (Bot・切断済み) を毎回引いても全走査を繰り返さない。
    /// </summary>
    public static InnerNet.ClientData ClientOf(byte playerId)
    {
        RefreshClients(force: false);
        var client = clientByPlayerId[playerId];
        if (IsClientOf(client, playerId)) return client;

        int frame = Time.frameCount;
        if (missRetriedFrame[playerId] == frame + 1) return null;
        missRetriedFrame[playerId] = frame + 1; // 0 埋めの初期値と区別するため +1 で持つ
        RefreshClients(force: true);
        client = clientByPlayerId[playerId];
        return IsClientOf(client, playerId) ? client : null;
    }

    private static bool IsClientOf(InnerNet.ClientData client, byte playerId)
        => client != null && client.Character != null && client.Character.PlayerId == playerId;

    private static void RefreshClients(bool force)
    {
        // 破棄済みの Unity オブジェクトは ?. では null にならないので、== で確かめる
        var amongUsClient = AmongUsClient.Instance;
        var source = amongUsClient != null ? amongUsClient.allClients : null;
        if (source == null)
        {
            Array.Clear(clientByPlayerId, 0, clientByPlayerId.Length);
            clientsBuiltSourceCount = -1;
            return;
        }
        int frame = Time.frameCount;
        int count = source.Count;
        if (!force && !clientsDirty && frame == clientsBuiltFrame && count == clientsBuiltSourceCount) return;

        Array.Clear(clientByPlayerId, 0, clientByPlayerId.Length);
        for (int i = 0; i < count; i++)
        {
            var cd = source[i];
            if (cd == null) continue;
            var character = cd.Character;
            if (character == null) continue;
            clientByPlayerId[character.PlayerId] ??= cd;
        }
        clientsBuiltFrame = frame;
        clientsBuiltSourceCount = count;
        clientsDirty = false;
    }

    private static void Refresh()
    {
        var source = PlayerControl.AllPlayerControls;
        if (source == null)
        {
            if (all.Length != 0) Clear();
            return;
        }
        int frame = Time.frameCount;
        int count = source.Count;
        if (!dirty && frame == builtFrame && count == builtSourceCount) return;

        var buffer = new List<PlayerControl>(count);
        for (int i = 0; i < count; i++)
        {
            var pc = source[i];
            if (pc != null) buffer.Add(pc);
        }
        all = buffer.ToArray();

        Array.Clear(byId, 0, byId.Length);
        // 同じ id が 2 つある時は先の方を返す (以前の FirstOrDefault と同じ)
        foreach (var pc in all)
            byId[pc.PlayerId] ??= pc;

        builtFrame = frame;
        builtSourceCount = count;
        dirty = false;
    }

    private static void Clear()
    {
        all = Array.Empty<PlayerControl>();
        Array.Clear(byId, 0, byId.Length);
        builtSourceCount = -1;
        dirty = true;
    }

    // 出入りは AllPlayerControls の要素数でも気づけるが、同じフレームで 1 人抜けて 1 人入ると数が変わらない。
    // 生成と破棄の両方で印を付けておく。
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
    private static class StartPatch
    {
        public static void Postfix() => Invalidate();
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.OnDestroy))]
    private static class DestroyPatch
    {
        public static void Postfix() => Invalidate();
    }
}
