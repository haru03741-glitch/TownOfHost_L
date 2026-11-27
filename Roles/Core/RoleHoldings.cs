using System;
using System.Collections.Generic;
using TownOfHostL.Attributes;
using TownOfHostL.Modules;

namespace TownOfHostL.Roles.Core;

/// <summary>
/// 役職が自分や他の人・全体に掛けた物 (速さの修飾・他の人向けの登録) を、役職のインスタンスの持ち物として覚え、役職が外れる時
/// (<see cref="RoleBase.Dispose"/>・中の役職の <see cref="RoleBase.DestroyInnerRole"/>) に OnDestroy の後でまとめて外す。
/// <para>
/// 役職は試合の途中でも替わる (サイドキック・任命・マッドメイト化など)。外す処理を役職ごとの OnDestroy に手で書くと、
/// 書き忘れた物 (足止め・加速) が試合の終わりまで残り、逆に全員で共有する登録を 1 人分の後始末で外すと、
/// 同じ役職の残りの人の分まで消える。ここに登録した物は、その役職のインスタンスの分だけが外れる。
/// </para>
/// </summary>
public sealed class RoleHoldings
{
    private readonly RoleBase owner;
    internal RoleHoldings(RoleBase owner) => this.owner = owner;

    private readonly List<Action> releases = new();
    // 速さの修飾 (相手, 鍵)。外す時に、修飾が外れた人の設定を送り直す
    private readonly HashSet<(byte playerId, string key)> speeds = new();

    /// <summary>外し終えた (役職が外れた)。外した後に登録した物はすぐ外す</summary>
    public bool IsReleased { get; private set; }

    // ---- 速さ (Modules.PlayerSpeed) ----

    /// <summary>止める。役職が外れる時に外す</summary>
    public void Freeze(byte playerId, string key) => HoldSpeed(playerId, key, () => PlayerSpeed.Freeze(playerId, key));
    /// <summary>部屋の設定の代わりの速さにする。役職が外れる時に外す</summary>
    public void OverrideSpeed(byte playerId, string key, float speed) => HoldSpeed(playerId, key, () => PlayerSpeed.Override(playerId, key, speed));
    /// <summary>速さに倍率を掛ける。役職が外れる時に外す</summary>
    public void MultiplySpeed(byte playerId, string key, float rate) => HoldSpeed(playerId, key, () => PlayerSpeed.Multiply(playerId, key, rate));
    /// <summary>速さに足す。役職が外れる時に外す</summary>
    public void AddSpeed(byte playerId, string key, float amount) => HoldSpeed(playerId, key, () => PlayerSpeed.Add(playerId, key, amount));

    /// <summary>この役職が付けた速さの修飾を外す (設定の送り直しは呼び元が決める)</summary>
    public void RemoveSpeed(byte playerId, string key)
    {
        PlayerSpeed.Remove(playerId, key);
        speeds.Remove((playerId, key));
    }
    /// <summary>この役職がこの鍵で付けた修飾を、全員から外す</summary>
    public void RemoveSpeedFromAll(string key)
    {
        PlayerSpeed.RemoveFromAll(key);
        speeds.RemoveWhere(s => s.key == key);
    }

    private void HoldSpeed(byte playerId, string key, Action apply)
    {
        if (IsReleased) return;
        apply();
        speeds.Add((playerId, key));
    }

    // ---- 他の人向けの登録 (CustomRoleManager の *Others) ----

    /// <summary>
    /// 登録する。この役職のインスタンスに結び付いた処理 (インスタンスのメソッド・this だけを使うラムダ) は、役職が外れる時に外す。
    /// それ以外 (static のメソッド・何も捕まえないラムダ・ローカルを捕まえたラムダ) は全員で共有する物とみなし、試合の終わりまで残す
    /// (何も捕まえないラムダは、同じ役職の全員で 1 つの処理を使い回すので、1 人の後始末で外すと残りの人の分まで消える)
    /// </summary>
    public void Subscribe<T>(ICollection<T> registry, T handler) where T : Delegate
    {
        if (handler == null) return;
        // 何も捕まえないラムダの Target は null ではなく、コンパイラが作る共有のインスタンスなので、持ち主かどうかで見分ける
        if (!ReferenceEquals(handler.Target, owner))
        {
            if (!registry.Contains(handler)) registry.Add(handler);
            return;
        }
        if (IsReleased) return;
        if (!registry.Contains(handler)) registry.Add(handler);
        releases.Add(() => registry.Remove(handler));
    }

    /// <summary>
    /// 同じ役職の人たちで 1 つを共有する static の処理を登録する。最後の 1 人の役職が外れた時に外す
    /// (それより前に外れた人の後始末では、残りの人の分を消さない)
    /// </summary>
    public void SubscribeShared<T>(ICollection<T> registry, T handler) where T : Delegate
    {
        if (handler == null || IsReleased) return;
        var key = (registry, (Delegate)handler);
        sharedCounts.TryGetValue(key, out var count);
        sharedCounts[key] = count + 1;
        if (!registry.Contains(handler)) registry.Add(handler);
        releases.Add(() =>
        {
            if (!sharedCounts.TryGetValue(key, out var n)) return;
            if (n > 1) { sharedCounts[key] = n - 1; return; }
            sharedCounts.Remove(key);
            registry.Remove(handler);
        });
    }

    // 共有の登録の持ち主の数。登録そのものは試合の始めと終わりに CustomRoleManager が空にするので、同じ時に空にする
    private static readonly Dictionary<(object registry, Delegate handler), int> sharedCounts = new();
    internal static void ResetShared() => sharedCounts.Clear();

    internal void Release(string ownerName)
    {
        if (IsReleased) return;
        IsReleased = true;
        // 足した順の逆に外す (後から付けた物ほど前の物に頼っている)
        for (int i = releases.Count - 1; i >= 0; i--) Run(releases[i], ownerName);
        releases.Clear();
        if (speeds.Count == 0) return;
        // 試合の終わりの後始末では送らない (速さは次の試合に持ち越されず、相手が先に壊れていることもある)
        bool send = GameStates.IsInTask && !GameEndChecker.EndingStarted;
        var touched = new HashSet<byte>();
        foreach (var (playerId, key) in speeds)
        {
            PlayerSpeed.Remove(playerId, key);
            touched.Add(playerId);
        }
        speeds.Clear();
        if (!send) return;
        foreach (var playerId in touched) Utils.GetPlayerById(playerId)?.MarkDirtySettings();
    }

    private static void Run(Action release, string ownerName = "")
    {
        try { release(); }
        catch (Exception ex) { Logger.Exception(ex, $"RoleHoldings.Release {ownerName}"); }
    }
}
