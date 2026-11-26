using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Attributes;

namespace TownOfHostL.Modules;

/// <summary>
/// 本編の客の手元にある、能力ボタンのクールの残りを、ホストが数えておく。
/// <para>
/// 能力のクールは客の役職の中にあり、ホストからは読めない。ただし本編がクールを始めるのは決まった出来事だけで、
/// どれもホストから見える: ベントから出た (Engineer)・変身を戻した (Shapeshifter)・姿を現した (Phantom)・
/// 守護の電文を本人に送った (どの役職でも役職のクールを設定値から始める)。始めた後は、会議の外では止まらずに減り、
/// 会議の終わりと役職の作り直しで 0 になる。出来事の時刻と、その時に客が持っていた設定値から残りを出す。
/// </para>
/// <para>
/// 数えるのは Engineer・Shapeshifter・Phantom だけ。Scientist・Tracker・Detective はボタンを押した事がホストへ届かないので数えられない。
/// </para>
/// </summary>
public static class AbilityCooldownClock
{
    // ベントから出る動きの長さ。本編は動きの終わりにクールを始める
    private const float ExitVentTime = 0.5f;

    // クールが 0 になる時刻 (Time.realtimeSinceStartup)。無い人はクールの途中でない
    private static readonly Dictionary<byte, float> readyAt = new();

    [GameModuleInitializer]
    public static void Init() => readyAt.Clear();

    /// <summary>ホストが数えられる役職か</summary>
    public static bool Tracks(RoleTypes role)
        => role is RoleTypes.Engineer or RoleTypes.Shapeshifter or RoleTypes.Phantom;

    /// <summary>その役職のクールの長さを決める設定</summary>
    public static FloatOptionNames OptionOf(RoleTypes role) => role switch
    {
        RoleTypes.Engineer => FloatOptionNames.EngineerCooldown,
        RoleTypes.Shapeshifter => FloatOptionNames.ShapeshifterCooldown,
        _ => FloatOptionNames.PhantomCooldown,
    };

    /// <summary>その人の能力のクールの残り (秒)</summary>
    public static float Remaining(byte playerId)
        => readyAt.TryGetValue(playerId, out float at) ? Mathf.Max(0f, at - Time.realtimeSinceStartup) : 0f;

    /// <summary>その人のクールを 0 にする (役職が作り直された時)</summary>
    public static void Clear(byte playerId) => readyAt.Remove(playerId);

    /// <summary>全員のクールを 0 にする (会議。本編は会議の終わりに能力のクールを 0 にする)</summary>
    public static void ClearAll() => readyAt.Clear();

    /// <summary>
    /// 客の手元で、役職のクールが設定値から始まる時に呼ぶ。delay は客の手元で始まるまでの間
    /// </summary>
    public static void Restart(PlayerControl pc, float delay = 0f)
    {
        if (pc == null || pc.AmOwner || !AmongUsClient.Instance.AmHost) return;
        var role = RoleView.Living(pc, pc);
        if (!Tracks(role)) return;
        // キノコの混乱の間、本編は変身のクールを 0 に固定する
        if (role == RoleTypes.Shapeshifter && Utils.IsActive(SystemTypes.MushroomMixupSabotage))
        {
            readyAt.Remove(pc.PlayerId);
            return;
        }
        float cooldown = PlayerGameOptionsSender.Of(pc.PlayerId)?.SentAbilityCooldown(role) ?? 0f;
        if (cooldown <= 0f) readyAt.Remove(pc.PlayerId);
        else readyAt[pc.PlayerId] = Time.realtimeSinceStartup + delay + cooldown;
    }

    /// <summary>ベントから出た時。Engineer はベントから出る動きの終わりにクールを始める</summary>
    public static void OnExitVent(PlayerControl pc)
    {
        if (pc == null || pc.AmOwner || RoleView.Living(pc, pc) != RoleTypes.Engineer) return;
        Restart(pc, ExitVentTime);
    }

    /// <summary>客が変身を戻すと知らせてきた時。本編の客は、変身している間に戻す時だけクールを始める</summary>
    public static void OnRevertShapeshift(PlayerControl pc)
    {
        if (pc == null || pc.AmOwner || RoleView.Living(pc, pc) != RoleTypes.Shapeshifter) return;
        Restart(pc);
    }

    /// <summary>客が姿を現すと知らせてきた時</summary>
    public static void OnAppear(PlayerControl pc)
    {
        if (pc == null || pc.AmOwner || RoleView.Living(pc, pc) != RoleTypes.Phantom) return;
        Restart(pc);
    }

    /// <summary>キノコの混乱が始まった時。本編は変身のクールを 0 にする</summary>
    public static void OnMushroomMixup()
    {
        foreach (var pc in Main.AllPlayerControls)
            if (pc && !pc.AmOwner && RoleView.Living(pc, pc) == RoleTypes.Shapeshifter) readyAt.Remove(pc.PlayerId);
    }
}
