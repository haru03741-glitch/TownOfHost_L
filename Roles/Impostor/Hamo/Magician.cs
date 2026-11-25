using System;
using System.Collections.Generic;
using System.Linq;

using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

/// <summary>
/// マジシャン (ExtremeRoles の Magician)<br/>
/// 能力「ジャグリング」で、動ける生存者の一部を、生存者のいる場所 (とスポーン地点) へランダムに飛ばす。
/// </summary>
public sealed class Magician : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: ExtremeRoles
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Magician),
            player => new Magician(player),
            CustomRoles.Magician,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 5400,
            SetupOptionItem,
            "mgc"
        );

    public Magician(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Cooldown = OptionCooldown.GetFloat();
        TeleportTargetRate = OptionTeleportTargetRate.GetInt() / 100f;
        DupeTeleportTarget = OptionDupeTeleportTargetTo.GetBool();
        IncludeSpawnPoint = OptionIncludeSpawnPoint.GetBool();
        IncludeRolePlayer = OptionIncludeRolePlayer.GetBool();
        AbilityCount = OptionAbilityCount.GetInt();
    }

    static OptionItem OptionCooldown;
    static OptionItem OptionAbilityCount;
    static OptionItem OptionTeleportTargetRate;
    static OptionItem OptionDupeTeleportTargetTo;
    static OptionItem OptionIncludeSpawnPoint;
    static OptionItem OptionIncludeRolePlayer;
    enum OptionName
    {
        MagicianAbilityCount,
        MagicianTeleportTargetRate,
        MagicianDupeTeleportTargetTo,
        MagicianIncludeSpawnPoint,
        MagicianIncludeRolePlayer,
    }

    static float Cooldown;
    static float TeleportTargetRate;
    static bool DupeTeleportTarget;
    static bool IncludeSpawnPoint;
    static bool IncludeRolePlayer;
    int AbilityCount;

    // エアシップの湧き場所を選ぶ前の待ち位置
    static readonly Vector2 AirshipWaitPos = new(-25f, 40f);

    private static void SetupOptionItem()
    {
        OptionAbilityCount = IntegerOptionItem.Create(RoleInfo, OptionName.MagicianAbilityCount, new(1, 10, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0.5f, 120f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionTeleportTargetRate = IntegerOptionItem.Create(RoleInfo, OptionName.MagicianTeleportTargetRate, new(10, 100, 10), 100, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionDupeTeleportTargetTo = BooleanOptionItem.Create(RoleInfo, OptionName.MagicianDupeTeleportTargetTo, true, false);
        OptionIncludeSpawnPoint = BooleanOptionItem.Create(RoleInfo, OptionName.MagicianIncludeSpawnPoint, false, false);
        OptionIncludeRolePlayer = BooleanOptionItem.Create(RoleInfo, OptionName.MagicianIncludeRolePlayer, false, false);
    }

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        // 能力はキルクールに関係しない
        AdjustKillCooldown = true;
        if (AbilityCount <= 0 || !Player.IsAlive()) return;
        // 飛ばす人がいない時は回数もクールも使わない
        if (!Juggling()) { ResetCooldown = false; return; }
        ResetCooldown = true;

        AbilityCount--;
        Logger.Info($"{Player.GetNameWithRole()}: ジャグリング (残り {AbilityCount})", "Magician");
        if (AbilityCount <= 0) Player.MarkDirtySettings();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    /// <summary>ジャグリング。テレポートする人がいなければ false (回数を使わない)</summary>
    bool Juggling()
    {
        // テレポート先にもなる、動ける生存者
        var validPlayer = Main.AllAlivePlayerControls.Where(pc =>
            !pc.inVent &&
            !pc.inMovingPlat &&
            !pc.onLadder &&
            !pc.GetCustomRole().IsSpectator() &&
            (pc.PlayerId != Player.PlayerId || IncludeRolePlayer)).ToArray();

        var teleportPlayer = validPlayer.OrderBy(_ => IRandom.Instance.Next(int.MaxValue))
            .Take((int)Math.Ceiling(validPlayer.Length * TeleportTargetRate)).ToList();
        if (teleportPlayer.Count == 0) return false;

        var targetPos = validPlayer
            .Select(pc => (Vector2)pc.transform.position)
            .Where(pos => Vector2.Distance(pos, AirshipWaitPos) > 3f)
            .ToList();

        if (IncludeSpawnPoint)
        {
            var map = GetSpawnMap();
            if (map != null) targetPos.AddRange(map.Positions.Values);
        }
        if (targetPos.Count == 0) return false;

        if (DupeTeleportTarget)
        {
            foreach (var pc in teleportPlayer)
                pc.RpcSnapTo(targetPos[IRandom.Instance.Next(targetPos.Count)]);
        }
        else
        {
            teleportPlayer = teleportPlayer.OrderBy(_ => IRandom.Instance.Next(int.MaxValue)).ToList();
            for (int i = 0; i < targetPos.Count && i < teleportPlayer.Count; i++)
                teleportPlayer[i].RpcSnapTo(targetPos[i]);
        }
        return true;
    }

    static RandomSpawn.SpawnMap GetSpawnMap() => RandomSpawn.ForCurrentMap();

    bool IUsePhantomButton.IsPhantomRole => AbilityCount > 0;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = AbilityCount <= 0 ? 200f : Cooldown;
    }
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(0 < AbilityCount ? RoleInfo.RoleColor : Color.gray, $"({AbilityCount})");
    public override string GetAbilityButtonText() => GetString("MagicianAbilityText");
}
