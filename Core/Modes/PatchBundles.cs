using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// TOHL のパッチの束。モードは <see cref="GameMode.LiveBundles"/> で、どの束を当てておくかを宣言する。
/// 束を付けていないパッチは <see cref="Standard"/> に入る。
/// </summary>
[Flags]
public enum PatchBundles
{
    None = 0,
    /// <summary>通常モードの仕組み全体。束を付けていないパッチは全部ここ。</summary>
    Standard = 1 << 0,
    /// <summary>どのモードでも要る下回り (試合の寿命・プレイヤーの一覧・LateTask の駆動・TOHL の表記)。電文は送らない。</summary>
    Core = 1 << 1,
    /// <summary>本編のかくれんぼで TOHL が休んでいる間だけ当てる物 (かくれんぼ用の役職)。</summary>
    VanillaHideAndSeek = 1 << 2,

    All = Standard | Core | VanillaHideAndSeek,
}

/// <summary>パッチのクラス (入れ子なら外側のどれか) に付けて、束を決める。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PatchBundleAttribute(PatchBundles bundle) : Attribute
{
    public PatchBundles Bundle { get; } = bundle;
}

/// <summary>束ごとに Harmony を分けて、当てる・外すを束の単位で行う。</summary>
public static class PatchBundleSet
{
    // 当てる順。同じメソッドに当てる束どうしの順番がいつも同じになるよう、足す時もこの順で全部を当て直す
    private static readonly PatchBundles[] Order = [PatchBundles.Core, PatchBundles.VanillaHideAndSeek, PatchBundles.Standard];

    private static readonly Dictionary<PatchBundles, Harmony> harmonies = new();
    private static readonly Dictionary<PatchBundles, List<Type>> types = new();

    /// <summary>いま当たっている束。</summary>
    public static PatchBundles Live { get; private set; }

    /// <summary>束の分け方を読む。<see cref="Standard"/> は TOHL の元の Harmony (ID を変えない) を使う。</summary>
    public static void Init(Harmony standard)
    {
        foreach (var bundle in Order)
        {
            harmonies[bundle] = bundle == PatchBundles.Standard ? standard : new Harmony($"{Main.PluginGuid}.{bundle}");
            types[bundle] = new();
        }
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Main).Assembly))
        {
            var bundle = BundleOf(type);
            if (!types.TryGetValue(bundle, out var list))
                throw new InvalidOperationException($"{type.FullName} の束 {bundle} は 1 つの束ではありません");
            list.Add(type);
        }
    }

    private static PatchBundles BundleOf(Type type)
    {
        for (var t = type; t != null; t = t.DeclaringType)
            if (t.GetCustomAttribute<PatchBundleAttribute>(false) is { } attr) return attr.Bundle;
        return PatchBundles.Standard;
    }

    /// <summary>当てる束を <paramref name="target"/> に合わせる。</summary>
    public static void Apply(PatchBundles target)
    {
        target &= PatchBundles.All;
        if (target == Live) return;
        var sw = Stopwatch.StartNew();
        if ((target & ~Live) != 0)
        {
            // 足す束があれば、当たっている物も外して決まった順で当て直す
            foreach (var bundle in Order) if (Live.HasFlag(bundle)) harmonies[bundle].UnpatchSelf();
            Live = PatchBundles.None;
            foreach (var bundle in Order)
            {
                if (!target.HasFlag(bundle)) continue;
                var harmony = harmonies[bundle];
                foreach (var type in types[bundle]) harmony.CreateClassProcessor(type).Patch();
            }
        }
        else
        {
            foreach (var bundle in Order) if (Live.HasFlag(bundle) && !target.HasFlag(bundle)) harmonies[bundle].UnpatchSelf();
        }
        Live = target;
        int methods = harmonies.Values.Sum(h => h.GetPatchedMethods().Count());
        Logger.Info($"パッチの束を {target} にしました ({methods} メソッド・{sw.ElapsedMilliseconds}ms)", "PatchBundles");
    }
}
