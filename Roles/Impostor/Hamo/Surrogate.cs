using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Madmate;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

/// <summary>
/// サロゲートとマッドプロキシの主従関係・共有キルクール・後追いを一元管理する。
/// すべてホストで決定し、通常のカスタム役職同期と位置同期を利用して全員へ反映する。
/// </summary>
public static class SurrogateSystem
{
    private static readonly Dictionary<byte, byte> OwnerToProxy = new();
    private static readonly Dictionary<byte, byte> ProxyToOwner = new();
    private static readonly HashSet<byte> FollowingDeathProcessed = new();

    public static void Init()
    {
        OwnerToProxy.Clear();
        ProxyToOwner.Clear();
        FollowingDeathProcessed.Clear();
    }

    /// <summary>持ち主が生き返ったら、また死んだ時に代わりの人が後を追うようにする</summary>
    public static void OnOwnerRevived(byte ownerId)
    {
        if (OwnerToProxy.TryGetValue(ownerId, out var proxyId)) FollowingDeathProcessed.Remove(proxyId);
    }

    public static bool TryGetProxy(byte ownerId, out PlayerControl proxy)
    {
        proxy = null;
        return OwnerToProxy.TryGetValue(ownerId, out var proxyId)
            && (proxy = Utils.GetPlayerById(proxyId)) != null;
    }

    public static bool TryGetOwner(byte proxyId, out PlayerControl owner)
    {
        owner = null;
        return ProxyToOwner.TryGetValue(proxyId, out var ownerId)
            && (owner = Utils.GetPlayerById(ownerId)) != null;
    }

    public static bool IsLinked(PlayerControl owner, PlayerControl proxy)
        => owner != null && proxy != null
        && OwnerToProxy.TryGetValue(owner.PlayerId, out var proxyId)
        && proxyId == proxy.PlayerId;

    public static bool CanCreateProxy(PlayerControl owner, PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost || owner == null || target == null) return false;
        if (!owner.IsAlive() || !target.IsAlive() || owner.PlayerId == target.PlayerId) return false;
        if (OwnerToProxy.ContainsKey(owner.PlayerId)) return false;
        if (target.Is(CustomRoleTypes.Impostor)) return false;

        if (target.Is(CustomRoles.GM)) return false;
        return true;
    }

    public static bool CreateProxy(PlayerControl owner, PlayerControl target)
    {
        if (!CanCreateProxy(owner, target)) return false;

        OwnerToProxy[owner.PlayerId] = target.PlayerId;
        ProxyToOwner[target.PlayerId] = owner.PlayerId;

        target.RpcChangeRole(CustomRoles.MadProxy);
        // 見え方の表に残す (会議明けの配り直しで、インポスターの見え方へ戻す)
        target.RpcSetRoleNormal(RoleTypes.Impostor, true);
        target.SyncSettings();

        ShareKillCooldown(owner, target, Surrogate.KillCooldown);
        Utils.NotifyRoles();
        Utils.AddGameLog("Surrogate", $"{owner.ColoredName()} が {target.ColoredName()} をマッドプロキシにしました。");
        return true;
    }

    public static void ShareKillCooldown(PlayerControl first, PlayerControl second, float cooldown)
    {
        if (first == null || second == null) return;
        cooldown = Mathf.Max(0f, cooldown);
        Main.AllPlayerKillCooldown[first.PlayerId] = cooldown;
        Main.AllPlayerKillCooldown[second.PlayerId] = cooldown;
        first.SetKillCooldown(cooldown, ForceProtect: true);
        second.SetKillCooldown(cooldown, ForceProtect: true);
        first.SyncSettings();
        second.SyncSettings();
    }

    public static void ShareKillCooldownAfterKill(PlayerControl killer)
    {
        if (!AmongUsClient.Instance.AmHost || killer == null) return;

        if (TryGetProxy(killer.PlayerId, out var proxy))
        {
            // サロゲート自身のキルはサロゲート設定のキルクールを共有する。
            ShareKillCooldown(killer, proxy, Surrogate.KillCooldown);
            return;
        }
        if (TryGetOwner(killer.PlayerId, out var owner))
        {
            // プロキシのキルはサロゲート配下のマッドプロキシ設定キルクールを共有する。
            ShareKillCooldown(owner, killer, Surrogate.MadProxyKillCooldown);
        }
    }

    public static bool TrySwap(PlayerControl owner)
    {
        if (!AmongUsClient.Instance.AmHost || owner == null || !owner.IsAlive()) return false;
        if (!TryGetProxy(owner.PlayerId, out var proxy) || !proxy.IsAlive()) return false;
        if (owner.inVent || proxy.inVent) return false;
        // キルクール中は交換できない。HUD側のキルタイマーと同じ実値で判定する。
        if (owner.killTimer > 0.05f || proxy.killTimer > 0.05f) return false;

        Vector2 ownerPos = owner.GetTruePosition();
        Vector2 proxyPos = proxy.GetTruePosition();
        // 入れ替えは 2 本で 1 組。片方だけ予算で落ちると 2 人が重なる
        if (!TownOfHostL.Modules.Net.SnapToBudget.CanSend()) return false;
        owner.RpcSnapTo(proxyPos, force: true);
        proxy.RpcSnapTo(ownerPos, force: true);
        Utils.NotifyRoles();
        return true;
    }

    public static void TransformProxyAfterKill(PlayerControl proxy)
    {
        if (!AmongUsClient.Instance.AmHost || proxy == null || !proxy.IsAlive()) return;
        if (!TryGetOwner(proxy.PlayerId, out var owner) || !owner.IsAlive()) return;

        float duration = Surrogate.MadProxyTransformDuration;
        if (duration <= 0f) return;
        proxy.RpcShapeshift(owner, false);
        _ = new LateTask(() =>
        {
            if (proxy != null && proxy.IsAlive() && proxy.Is(CustomRoles.MadProxy))
                proxy.RpcShapeshift(proxy, false);
        }, duration, "MadProxyTransformEnd");
    }

    public static void CheckOwnerDeath(PlayerControl owner)
    {
        if (!AmongUsClient.Instance.AmHost || owner == null || owner.IsAlive()) return;
        if (!OwnerToProxy.TryGetValue(owner.PlayerId, out var proxyId)) return;
        if (!FollowingDeathProcessed.Add(proxyId)) return;

        var proxy = Utils.GetPlayerById(proxyId);
        if (proxy == null || !proxy.IsAlive()) return;

        var state = PlayerState.GetByPlayerId(proxy.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.FollowingSuicide;
        bool duringMeeting = GameStates.CalledMeeting || GameStates.ExiledAnimate || AntiBlackout.IsCached;
        if (duringMeeting) proxy.RpcExileV3();
        else proxy.RpcMurderPlayerV2(proxy);
    }
}

public sealed class Surrogate : RoleBase, IImpostor, IKiller, IUsePhantomButton
{
    // 役職の元: TownOfHost-hamo
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Surrogate),
            player => new Surrogate(player),
            CustomRoles.Surrogate,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 95000,
            SetupOptionItem,
            "Srg",
            "#ff1919",
            introSound: () => GetIntroSound(RoleTypes.Phantom)
        );

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionPhantomCooldown;
    private static OptionItem OptionMadProxySection;
    private static OptionItem OptionMadProxyKillCooldown;
    public static OptionItem OptionMadProxyTransformDuration;
    public static float KillCooldown => OptionKillCooldown?.GetFloat() ?? 40f;
    public static float MadProxyKillCooldown => OptionMadProxyKillCooldown?.GetFloat() ?? 40f;
    public static float MadProxyTransformDuration => OptionMadProxyTransformDuration?.GetFloat() ?? 12f;
    private static float PhantomCooldown => OptionPhantomCooldown?.GetFloat() ?? 35f;

    public Surrogate(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 1f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionPhantomCooldown = FloatOptionItem.Create(RoleInfo, "SurrogatePhantomCooldown", new(0f, 255f, 1f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);

        OptionMadProxySection = ObjectOptionitem.Create(RoleInfo, "MadProxySettings", true, null);
        OptionMadProxyKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 1f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(OptionMadProxySection);
        OptionMadProxyTransformDuration = FloatOptionItem.Create(RoleInfo, GeneralOption.Duration, new(0f, 255f, 1f), 12f, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(OptionMadProxySection);
    }

    public override void ApplyGameOptions(IGameOptions opt)
        => AURoleOptions.PhantomCooldown = PhantomCooldown;

    public override RoleTypes? AfterMeetingRole => RoleTypes.Phantom;
    public bool CanUseKillButton() => Player.IsAlive();
    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.UseOneclickButton => true;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = true;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;

        if (SurrogateSystem.TryGetProxy(Player.PlayerId, out var proxy))
        {
            SurrogateSystem.TrySwap(Player);
            return;
        }

        var target = Player.GetKillTarget(true);
        if (target is null)
        {
            ResetCooldown = false;
            return;
        }
        SurrogateSystem.CreateProxy(Player, target);
    }

    public override string GetAbilityButtonText() => SurrogateSystem.TryGetProxy(Player.PlayerId, out _) ? GetString("SurrogateSwap") : "SK";



    public void OnMurderPlayerAsKiller(MurderInfo info)
        => SurrogateSystem.ShareKillCooldownAfterKill(Player);

    public override void OnMurderPlayerAsTarget(MurderInfo info)
        => SurrogateSystem.CheckOwnerDeath(Player);

    public override void OnRevived()
        => SurrogateSystem.OnOwnerRevived(Player.PlayerId);

    public override void OnFixedUpdate(PlayerControl player)
        => SurrogateSystem.CheckOwnerDeath(Player);

    public override void AfterMeetingTasks()
        => SurrogateSystem.CheckOwnerDeath(Player);
}
