using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Options;

namespace TownOfHostL.Roles.Impostor;

public sealed class Mare : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Mare),
            player => new Mare(player),
            CustomRoles.Mare,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpTOH + 1100,
            SetupCustomOption,
            "メアー",
            assignInfo: new(CustomRoles.Mare, CustomRoleTypes.Impostor)
            {
                // 停電が存在する。ロビー (開始前の人数の検査) では船がまだ無いので、マップで決める (停電が無いのはザ・ファングルだけ)
                IsInitiallyAssignableCallBack = () => GameStates.IsInGame && ShipStatus.Instance != null
                    ? ShipStatus.Instance.Systems.TryGetValue(SystemTypes.Electrical, out var systemType) && systemType.TryCast<SwitchSystem>(out _)
                    : GameMap.Current is not MapNames.Fungle,
            }
        );
    public Mare(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Holdings.Subscribe(CustomRoleManager.EveryoneNeedsSeenNamesOthers, () => Utils.IsActive(SystemTypes.Electrical));
        KillCooldownInLightsOut = OptionKillCooldownInLightsOut.GetFloat();
        SpeedInLightsOut = OptionSpeedInLightsOut.GetFloat();
    }

    private static OptionItem OptionKillCooldownInLightsOut;
    private static OptionItem OptionSpeedInLightsOut;
    enum OptionName
    {
        MareAddSpeedInLightsOut,
        MareKillCooldownInLightsOut,
    }
    private float KillCooldownInLightsOut;
    private float SpeedInLightsOut;
    // 人ごとに持つ (共有すると、2 人いる時に先に解除した 1 人しか停電明けの設定を送り直さない)
    private bool IsActivateKill;

    public static void SetupCustomOption()
    {
        OptionSpeedInLightsOut = FloatOptionItem.Create(RoleInfo, OptionName.MareAddSpeedInLightsOut, new(0.2f, 10.0f, 0.2f), 0.3f, false)
        .SetValueFormat(OptionFormat.Multiplier);
            OptionKillCooldownInLightsOut = FloatOptionItem.Create(RoleInfo, OptionName.MareKillCooldownInLightsOut, new(2.5f, 180f, 2.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public bool CanUseKillButton() => IsActivateKill;
    public float CalculateKillCooldown() => IsActivateKill ? KillCooldownInLightsOut : DefaultKillCooldown;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        // 停電中だけ加速する
        if (IsActivateKill) Holdings.AddSpeed(Player.PlayerId, "Mare", SpeedInLightsOut);
        else Holdings.RemoveSpeed(Player.PlayerId, "Mare");
    }
    private void ActivateKill(bool activate)
    {
        IsActivateKill = activate;
        if (AmongUsClient.Instance.AmHost)
        {
            Player.MarkDirtySettings();
            Utils.NotifyRoles();
        }
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (GameStates.IsInTask && IsActivateKill)
        {
            if (!Utils.IsActive(SystemTypes.Electrical))
            {
                //停電解除されたらキルモード解除
                ActivateKill(false);
            }
        }
    }
    public override bool OnSabotage(PlayerControl player, SystemTypes systemType)
    {
        if (systemType == SystemTypes.Electrical)
        {
            _ = After(() =>
            {
                //まだ停電が直っていなければキル可能モードに
                if (GameStates.IsInTask && Utils.IsActive(SystemTypes.Electrical))
                {
                    ActivateKill(true);
                }
            }, 4.0f, "Mare Activate Kill");
        }
        return true;
    }
    public override bool ShowsRoleColorToAll(bool isMeeting) => !isMeeting && IsActivateKill;
}