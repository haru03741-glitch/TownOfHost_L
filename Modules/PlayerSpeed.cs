using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Attributes;

namespace TownOfHostL.Modules;

// プレイヤーの移動速度。速度を変える役職は、自分の名前 (key) で修飾を付け外しし、1 つの値を直接書き換えない。
// 直接書き換えると、足止めと加速のように 2 つの役職が重なった時に、片方の戻しがもう片方の値を消す
// (控えておいた凍結中の値に戻して永久に止まる・加速が消える など)。
// 効く速度 = 凍結が 1 つでもあれば止める。無ければ (上書き ?? 部屋の設定) × 倍率の積 + 加算の和。
public static class PlayerSpeed
{
    enum Kind { Freeze, Override, Multiply, Add }
    readonly record struct Modifier(Kind Kind, float Value);

    static readonly Dictionary<byte, Dictionary<string, Modifier>> modifiers = new();

    [GameModuleInitializer]
    public static void Init() => modifiers.Clear();

    // 止める。key ごとに付け外しするので、別の役職の凍結や加速と重なっても互いの戻しを消さない
    public static void Freeze(byte playerId, string key) => Set(playerId, key, new(Kind.Freeze, 0f));
    // 部屋の設定の代わりに使う速度
    public static void Override(byte playerId, string key, float speed) => Set(playerId, key, new(Kind.Override, speed));
    public static void Multiply(byte playerId, string key, float rate) => Set(playerId, key, new(Kind.Multiply, rate));
    public static void Add(byte playerId, string key, float amount) => Set(playerId, key, new(Kind.Add, amount));

    public static void Remove(byte playerId, string key)
    {
        if (modifiers.TryGetValue(playerId, out var mods)) mods.Remove(key);
    }
    public static void RemoveFromAll(string key)
    {
        foreach (var mods in modifiers.Values) mods.Remove(key);
    }
    public static bool Has(byte playerId, string key) => modifiers.TryGetValue(playerId, out var mods) && mods.ContainsKey(key);
    public static float GetAdd(byte playerId, string key) =>
        modifiers.TryGetValue(playerId, out var mods) && mods.TryGetValue(key, out var m) && m.Kind == Kind.Add ? m.Value : 0f;

    static void Set(byte playerId, string key, Modifier modifier)
    {
        if (!modifiers.TryGetValue(playerId, out var mods))
        {
            mods = new();
            modifiers[playerId] = mods;
        }
        mods[key] = modifier;
    }

    public static float Get(byte playerId)
    {
        float baseSpeed = Main.RealOptionsData?.GetFloat(FloatOptionNames.PlayerSpeedMod) ?? 1f;
        if (!modifiers.TryGetValue(playerId, out var mods) || mods.Count == 0) return baseSpeed;

        float? overrideSpeed = null;
        float rate = 1f;
        float add = 0f;
        foreach (var m in mods.Values)
        {
            switch (m.Kind)
            {
                case Kind.Freeze: return Main.MinSpeed;
                case Kind.Override: overrideSpeed = m.Value; break;
                case Kind.Multiply: rate *= m.Value; break;
                case Kind.Add: add += m.Value; break;
            }
        }
        return Mathf.Max(Main.MinSpeed, (overrideSpeed ?? baseSpeed) * rate + add);
    }
}
