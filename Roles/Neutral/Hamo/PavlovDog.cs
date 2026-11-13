using TownOfHostL.Roles.Neutral;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Main;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Neutral;

public sealed class PavlovDog : PavlovDogBase
{
    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(PavlovDog),
            player => new PavlovDog(player),
            CustomRoles.PavlovDog,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 53500,
            SetupOptionItem,
            "pvd",
            "#F4A96A",
            isDesyncImpostor: true,
            countType: CountTypes.Pavlov,
            assignInfo: new RoleAssignInfo(CustomRoles.PavlovDog, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = [CustomRoles.PavlovOwner]
            },
            // 犬が 1 匹も生きていなければ人数で勝たない
            killingFaction: new(CountTypes.Pavlov, CustomWinner.Pavlov, 300,
                [CustomRoles.PavlovDog, CustomRoles.PavlovOwner, CustomRoles.PavlovDogImprint], canWin: HasAliveDog)
        );

    public PavlovDog(PlayerControl player)
        : base(RoleInfo, player)
    {
    }

    static OptionItem OptionImprintCooldown;
    static OptionItem OptionMaxImprintCount;
    static OptionItem OptionOwnerSuicideOnImpostor;
    static OptionItem OptionDogImpostorVision;
    static OptionItem OptionDogCanVent;
    static OptionItem OptionRampageKillCooldown;
    static OptionItem OptionRampageSuicideTime;
    static OptionItem OptionResetRampageTimerOnMeeting;

    enum OptionName
    {
        PavlovOwnerImprintCooldown,
        PavlovOwnerMaxImprintCount,
        PavlovOwnerSuicideOnImpostor,
        PavlovDogImpostorVision,
        PavlovDogCanVent,
        PavlovDogRampageKillCooldown,
        PavlovDogRampageSuicideTime,
        PavlovDogResetRampageTimerOnMeeting,
    }

    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo);
        ObjectOptionitem.Create(RoleInfo, "PavlovOwnerOption", true, "")
            .SetOptionName(() => "Pavlov Owner Option");
        OptionImprintCooldown = FloatOptionItem.Create(RoleInfo, OptionName.PavlovOwnerImprintCooldown, new(0f, 60f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxImprintCount = IntegerOptionItem.Create(RoleInfo, OptionName.PavlovOwnerMaxImprintCount, new(1, 10, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
        OptionOwnerSuicideOnImpostor = BooleanOptionItem.Create(RoleInfo, OptionName.PavlovOwnerSuicideOnImpostor, true, false);

        ObjectOptionitem.Create(RoleInfo, "PavlovDogOption", true, "")
            .SetOptionName(() => "Pavlov Dog Option");
        OptionDogImpostorVision = BooleanOptionItem.Create(RoleInfo, OptionName.PavlovDogImpostorVision, true, false);
        OptionDogCanVent = BooleanOptionItem.Create(RoleInfo, OptionName.PavlovDogCanVent, true, false);
        OptionRampageKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.PavlovDogRampageKillCooldown, new(2.5f, 60f, 2.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRampageSuicideTime = FloatOptionItem.Create(RoleInfo, OptionName.PavlovDogRampageSuicideTime, new(10f, 120f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionResetRampageTimerOnMeeting = BooleanOptionItem.Create(RoleInfo, OptionName.PavlovDogResetRampageTimerOnMeeting, true, false);

        RoleAddAddons.Create(RoleInfo, NeutralKiller: true);

        HideRoleOptions(CustomRoles.PavlovOwner);
        HideRoleOptions(CustomRoles.PavlovDogImprint);
    }


    public static void HideRoleOptions(CustomRoles role)
    {
        if (Options.CustomRoleSpawnChances != null &&
            Options.CustomRoleSpawnChances.TryGetValue(role, out var spawnOption))
        {
            spawnOption.SetHidden(true);
        }

        if (Options.CustomRoleCounts != null &&
            Options.CustomRoleCounts.TryGetValue(role, out var countOption))
        {
            countOption.SetHidden(true);
        }
    }

    public static bool IsPavlovTeamRole(CustomRoles role)
        => role is CustomRoles.PavlovDog or CustomRoles.PavlovOwner or CustomRoles.PavlovDogImprint;

    public static bool IsPavlovTeam(PlayerControl player)
        => player != null && IsPavlovTeamRole(player.GetCustomRole());

    public static bool HasAliveDog()
        => Main.AllAlivePlayerControls.Any(pc =>
            pc != null &&
            pc.IsAlive() &&
            (pc.Is(CustomRoles.PavlovDog) || pc.Is(CustomRoles.PavlovDogImprint)));

    public static bool IsOwnerDogAndOneNonKillerAlive()
    {
        var alivePlayers = Main.AllAlivePlayerControls
            .Where(pc => pc != null && pc.IsAlive())
            .ToArray();

        if (!alivePlayers.Any(pc => pc.Is(CustomRoles.PavlovOwner))) return false;
        if (!alivePlayers.Any(pc => pc.Is(CustomRoles.PavlovDog) || pc.Is(CustomRoles.PavlovDogImprint))) return false;

        var nonPavlovAlive = alivePlayers.Where(pc => !IsPavlovTeam(pc)).ToArray();
        if (nonPavlovAlive.Length != 1) return false;

        return IsNonKiller(nonPavlovAlive[0]);
    }

    static bool IsNonKiller(PlayerControl player)
    {
        if (player == null || !player.IsAlive()) return false;

        if (player.Is(CustomRoleTypes.Impostor)) return false;
        if (player.IsNeutralKiller()) return false;
        if (player.GetRoleClass() is IKiller) return false;

        return true;
    }

    public static float GetImprintCooldown() => OptionImprintCooldown?.GetFloat() ?? 20f;
    public static int GetMaxImprintCount() => OptionMaxImprintCount?.GetInt() ?? 2;
    public static bool GetOwnerSuicideOnImpostor() => OptionOwnerSuicideOnImpostor?.GetBool() ?? true;
    public static bool GetDogImpostorVision() => OptionDogImpostorVision?.GetBool() ?? true;
    public static bool GetDogCanVent() => OptionDogCanVent?.GetBool() ?? true;
    public static float GetRampageKillCooldown() => OptionRampageKillCooldown?.GetFloat() ?? 15f;
    public static float GetRampageSuicideTime() => OptionRampageSuicideTime?.GetFloat() ?? 30f;
    public static bool GetResetRampageTimerOnMeeting() => OptionResetRampageTimerOnMeeting?.GetBool() ?? true;
}

public sealed class PavlovOwner : RoleBase, IKiller, IAdditionalWinner, ISchrodingerCatOwner
{
    public override bool NeedsSeenNames => true;
    static RoleTypes GetOwnerBaseRoleType()
        => CanAnyOwnerImprintNow() ? RoleTypes.Impostor : RoleTypes.Crewmate;

    static bool CanAnyOwnerImprintNow()
    {
        if (PavlovDog.HasAliveDog()) return false;

        return Main.AllAlivePlayerControls.Any(pc =>
            pc != null &&
            pc.IsAlive() &&
            pc.GetRoleClass() is PavlovOwner owner &&
            owner.RemainingImprintCount > 0);
    }

    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(PavlovOwner),
            player => new PavlovOwner(player),
            CustomRoles.PavlovOwner,
            GetOwnerBaseRoleType,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + //ID被り解消のためだから許して...()
            77259,
            SetupOptionItem,
            "pvo",
            "#F4A96A",
            isDesyncImpostor: true,
            countType: CountTypes.Pavlov,
            assignInfo: new RoleAssignInfo(CustomRoles.PavlovOwner, CustomRoleTypes.Neutral)
            {
                IsInitiallyAssignableCallBack = () => false,
                AssignCountRule = new(0, 0, 1)
            }
        );

    public PavlovOwner(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.False)
    {
        RemainingImprintCount = PavlovDog.GetMaxImprintCount();
        LastCanImprintState = false;
    }

    int RemainingImprintCount;
    bool LastCanImprintState;
    /// <summary>犬がいない間は、刷り込み回数が残っている時だけパブロフ陣営として数える</summary>
    public override bool CountsForGameEnd => HasRemainingImprintCount() || PavlovDog.HasAliveDog();

    public SchrodingerCat.TeamType SchrodingerCatChangeTo => SchrodingerCat.TeamType.Pavlov;
    public bool HasRemainingImprintCount() => RemainingImprintCount > 0;

    bool CanImprintNow()
        => Player.IsAlive() &&
           RemainingImprintCount > 0 &&
           !PavlovDog.HasAliveDog();

    void RefreshState(bool force = false)
    {
        var canImprint = CanImprintNow();
        if (!force && LastCanImprintState == canImprint) return;

        LastCanImprintState = canImprint;
        if (!AmongUsClient.Instance.AmHost) return;
        Player.MarkDirtySettings();
        Utils.NotifyRoles();
    }

    private static void SetupOptionItem()
    {
        PavlovDog.HideRoleOptions(CustomRoles.PavlovOwner);
    }

    public override void Add()
    {
        RemainingImprintCount = PavlovDog.GetMaxImprintCount();
        LastCanImprintState = CanImprintNow();
        SendRPC();
        RefreshState(force: true);
    }

    public float CalculateKillCooldown() => PavlovDog.GetImprintCooldown();
    public bool CanUseKillButton() => CanImprintNow();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (player != Player) return;
        RefreshState();
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        info.DoKill = false;

        if (!AmongUsClient.Instance.AmHost) return;
        if (!info.CanKill) return;
        if (!CanImprintNow()) return;
        if (RemainingImprintCount <= 0) return;
        if (!Player.IsAlive()) return;

        var target = info.AttemptTarget;
        if (target == null || !target.IsAlive()) return;
        if (PavlovDog.IsPavlovTeam(target)) return;

        RemainingImprintCount--;
        SendRPC();
        RefreshState(force: true);

        if (PavlovDog.GetOwnerSuicideOnImpostor() && target.GetCustomRole().IsImpostor())
        {
            Suicide();
            return;
        }

        Player.RpcProtectedMurderPlayer(target);
        target.RpcProtectedMurderPlayer(Player);
        target.RpcProtectedMurderPlayer(target);


        target.RpcChangeRole(CustomRoles.PavlovDog);
        if (target.GetRoleClass() is PavlovDogBase dog)
            dog.SetOwner(Player.PlayerId);

        Player.MarkDirtySettings();
        Utils.NotifyRoles();
    }

    void Suicide()
    {
        if (!Player.IsAlive()) return;

        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state == null) return;

        state.DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayerV2(Player);
    }

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (CustomWinnerHolder.WinnerTeam is not CustomWinner.Pavlov) return false;

        winnerRole = Player.GetCustomRole();
        return true;
    }

    public override string GetProgressText(bool comms = false)
        => $"<color=#b8860b>({RemainingImprintCount})</color>";

    public override bool KnowsRoleOf(PlayerControl seen, bool isMeeting) => PavlovDog.IsPavlovTeam(Player) && PavlovDog.IsPavlovTeam(seen);

    public override bool ShowsRoleTo(PlayerControl seer, bool isMeeting) => PavlovDog.IsPavlovTeam(seer) && PavlovDog.IsPavlovTeam(Player);

    public bool OverrideKillButtonText(out string text)
    {
        text = GetString("PavlovImprintButtonText");
        return true;
    }


    void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(RemainingImprintCount);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        RemainingImprintCount = reader.ReadInt32();
        RefreshState(force: true);
    }
}

/*public sealed class PavlovDogImprint : PavlovDogBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(PavlovDogImprint),
            player => new PavlovDogImprint(player),
            CustomRoles.PavlovDogImprint,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 77270,
            SetupOptionItem,
            "pvi",
            "#F4A96A",
            isDesyncImpostor: true,
            countType: CountTypes.Pavlov,
            assignInfo: new RoleAssignInfo(CustomRoles.PavlovDogImprint, CustomRoleTypes.Neutral)
            {
                IsInitiallyAssignableCallBack = () => false,
                AssignCountRule = new(0, 0, 1)
            }
        );

    public PavlovDogImprint(PlayerControl player)
        : base(RoleInfo, player)
    {
    }

    private static void SetupOptionItem()
    {
        PavlovDog.HideRoleOptions(CustomRoles.PavlovDogImprint);
    }
}*/

public abstract class PavlovDogBase : RoleBase, IKiller, IAdditionalWinner, ISchrodingerCatOwner
{
    public override bool NeedsSeenNames => true;
    protected PavlovDogBase(SimpleRoleInfo roleInfo, PlayerControl player)
        : base(roleInfo, player, () => HasTask.False)
    {
        OwnerId = byte.MaxValue;
        IsRampage = false;
        RampageTimer = null;
    }

    protected byte OwnerId;
    bool IsRampage;
    float? RampageTimer;

    public SchrodingerCat.TeamType SchrodingerCatChangeTo => SchrodingerCat.TeamType.Pavlov;

    public void SetOwner(byte ownerId)
    {
        OwnerId = ownerId;
        SendRPC();
    }

    public float CalculateKillCooldown()
        => IsRampage ? PavlovDog.GetRampageKillCooldown() : Options.DefaultKillCooldown;

    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => PavlovDog.GetDogCanVent();

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(PavlovDog.GetDogImpostorVision());
        AURoleOptions.ShapeshifterCooldown = IsRampage ? PavlovDog.GetRampageSuicideTime() : 255f;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    void RestartRampageAbilityCooldown()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        Player.MarkDirtySettings();
        Player.SyncSettings();
        Player.RpcResetAbilityCooldown();
    }

    public override bool OnCheckShapeshift(PlayerControl target, ref bool shouldAnimate)
    {
        shouldAnimate = false;
        return false;
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.AttemptTarget != null && info.AttemptTarget.Is(CustomRoles.PavlovOwner))
        {
            info.DoKill = false;
            return;
        }

        if (!info.CanKill) return;

        if (!IsRampage) return;

        RampageTimer = null;
        info.AttemptKiller.MarkDirtySettings();
        SendRPC();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!IsRampage || !PavlovDog.GetResetRampageTimerOnMeeting()) return;

        RampageTimer = null;
        SendRPC();
    }

    public override void OnStartMeeting()
    {
        if (!IsRampage || !PavlovDog.GetResetRampageTimerOnMeeting()) return;

        RampageTimer = null;
        SendRPC();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (!player.IsAlive())
        {
            if (IsRampage || RampageTimer != null)
            {
                IsRampage = false;
                RampageTimer = null;
                SendRPC();
            }
            return;
        }

        if (!GameStates.IsInTask || GameStates.CalledMeeting || GameStates.Intro) return;

        TryBindOwner();

        if (!IsRampage && ShouldStartRampage())
        {
            IsRampage = true;
            RampageTimer = 0f;
            RestartRampageAbilityCooldown();
            SendRPC();
            return;
        }

        if (!IsRampage) return;

        if (RampageTimer == null)
        {
            RampageTimer = 0f;
            RestartRampageAbilityCooldown();
            SendRPC();
            return;
        }

        if (RampageTimer >= PavlovDog.GetRampageSuicideTime())
        {
            Suicide();
            return;
        }

        RampageTimer += Time.fixedDeltaTime;
    }

    void TryBindOwner()
    {
        if (OwnerId != byte.MaxValue) return;

        var owner = Main.AllPlayerControls.FirstOrDefault(pc => pc.Is(CustomRoles.PavlovOwner));
        if (owner == null) return;

        OwnerId = owner.PlayerId;
        SendRPC();
    }

    bool ShouldStartRampage()
    {
        if (OwnerId == byte.MaxValue) return false;

        var owner = Utils.GetPlayerById(OwnerId);
        if (owner == null) return true;
        if (!owner.IsAlive()) return true;

        return owner.GetCustomRole() != CustomRoles.PavlovOwner;
    }

    void Suicide()
    {
        if (!Player.IsAlive()) return;

        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state == null) return;

        state.DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayerV2(Player);

        IsRampage = false;
        RampageTimer = null;
        SendRPC();
    }

    public override bool CanUseAbilityButton() => IsRampage;
    public override string GetAbilityButtonText() => GetString("SerialKillerSuicideButtonText");


    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (CustomWinnerHolder.WinnerTeam is not CustomWinner.Pavlov) return false;

        winnerRole = Player.GetCustomRole();
        return true;
    }

    public override string GetProgressText(bool comms = false)
    {
        if (!IsRampage) return "";
        return "<color=#ff6633>(!!)</color>";
    }

    public override bool KnowsRoleOf(PlayerControl seen, bool isMeeting) => PavlovDog.IsPavlovTeam(Player) && PavlovDog.IsPavlovTeam(seen);

    public override bool ShowsRoleTo(PlayerControl seer, bool isMeeting) => PavlovDog.IsPavlovTeam(seer) && PavlovDog.IsPavlovTeam(Player);

    protected void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(OwnerId);
        sender.Writer.Write(IsRampage);
        sender.Writer.Write(RampageTimer != null);
        sender.Writer.Write(RampageTimer ?? 0f);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        OwnerId = reader.ReadByte();
        IsRampage = reader.ReadBoolean();
        var hasTimer = reader.ReadBoolean();
        var timer = reader.ReadSingle();
        RampageTimer = hasTimer ? timer : null;
    }
}
