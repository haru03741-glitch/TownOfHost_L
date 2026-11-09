using System;

using AmongUs.GameOptions;

using Hazel;

using TownOfHostL.Modules;

using TownOfHostL.Patches;

using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;

using UnityEngine;



namespace TownOfHostL.Roles.Crewmate;



public sealed class Sage : RoleBase

{

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(Sage),
            player => new Sage(player),
            CustomRoles.Sage,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 34500,
            SetupOptionItem,
            "sg",
            "#aaddff"
        );



    public Sage(PlayerControl player)

        : base(RoleInfo, player)

    {

        BarrierDuration = OptionBarrierDuration.GetFloat();

        BarrierCooldown = OptionBarrierCooldown.GetFloat();

        isBarrierActive = false;

        barrierTimer = 0f;

        cooldownTimer = OptionBarrierCooldown.GetFloat();

        savedPosition = Vector2.zero;

    }



    static OptionItem OptionBarrierDuration;

    static float BarrierDuration;

    static OptionItem OptionBarrierCooldown;

    static float BarrierCooldown;



    bool isBarrierActive;

    float barrierTimer;

    float cooldownTimer;

    Vector2 savedPosition;



    private const string DefaultPetId = "pet_crewmate";



    enum OptionName

    {

        SageBarrierDuration,

        SageBarrierCooldown,

    }



    static void SetupOptionItem()

    {

        OptionBarrierDuration = FloatOptionItem.Create(RoleInfo, OptionName.SageBarrierDuration,

            new(1f, 15f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);

        OptionBarrierCooldown = FloatOptionItem.Create(RoleInfo, OptionName.SageBarrierCooldown,

            new(2.5f, 60f, 2.5f), 20f, false).SetValueFormat(OptionFormat.Seconds);

    }



    public override void Add()

    {

        PetActionManager.Register(Player.PlayerId, ActivateBarrier);



        if (!AmongUsClient.Instance.AmHost) return;

        if (!PetActionManager.AutoGrantPetEnabled) return;



        for (int i = 1; i <= 3; i++)

        {

            int delay = i;

            _ = new LateTask(() =>

            {

                try

                {

                    if (!PetActionManager.AutoGrantPetEnabled) return;

                    if (Player == null) return;



                    string currentPet = Player.Data?.DefaultOutfit?.PetId ?? Player.CurrentOutfit?.PetId ?? "";



                    bool hasPet = !string.IsNullOrEmpty(currentPet)

                               && currentPet.ToLower() != "pet_none"

                               && currentPet.ToLower() != "none";

                    if (hasPet) return;



                    Player.RpcSetPet(DefaultPetId);

                    Logger.Info($"{Player.Data?.PlayerName} にペット付与: {DefaultPetId} (試行{delay}回目)", "Sage");

                }

                catch (Exception e)

                {

                    Logger.Error(e.ToString(), "Sage.SetPet");

                }

            }, delay * 1.5f, $"Sage.SetDefaultPet_{i}");

        }

    }



    public override void OnDestroy()

    {
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(Sage));

        PetActionManager.Unregister(Player.PlayerId);



        if (isBarrierActive)

        {

            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(Sage));

            Player.MarkDirtySettings();

            if (AmongUsClient.Instance.AmHost)

                Player.SyncSettings();

            isBarrierActive = false;

        }

    }



    public override void ApplyGameOptions(IGameOptions opt)

    {

        AURoleOptions.EngineerCooldown = Mathf.Max(cooldownTimer, 0.1f);

        AURoleOptions.EngineerInVentMaxTime = 0f;

    }



    public override bool CanClickUseVentButton => false;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;



    public void ActivateBarrier()

    {

        if (!Player.IsAlive()) return;

        if (isBarrierActive) return;

        if (cooldownTimer > 0f) return;



        isBarrierActive = true;

        barrierTimer = 0f;

        savedPosition = Player.transform.position;



        TownOfHostL.Modules.PlayerSpeed.Freeze(Player.PlayerId, nameof(Sage));

        Player.MarkDirtySettings();

        Player.SyncSettings();



        SnapPlayerToSaved();



        SendRpc();

        Utils.NotifyRoles();

        Logger.Info($"{Player.Data?.PlayerName} がバリアを発動", "Sage");

    }



    private void DeactivateBarrier(bool resetCooldown = true)

    {

        if (!isBarrierActive) return;

        isBarrierActive = false;

        barrierTimer = 0f;



        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(Sage));

        Player.MarkDirtySettings();

        Player.SyncSettings();



        if (resetCooldown)

        {

            AURoleOptions.EngineerCooldown = BarrierCooldown;

            Player.RpcResetAbilityCooldown();

            cooldownTimer = BarrierCooldown;

        }



        SendRpc();

        Utils.NotifyRoles();

    }



    private void SnapPlayerToSaved()

    {

        if (Player == null) return;

        try { Player.NetTransform.SnapTo(savedPosition); } catch { }



        ushort sid = (ushort)(Player.NetTransform.lastSequenceId + 2U);

        var writer = AmongUsClient.Instance.StartRpcImmediately(

            Player.NetTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.Reliable);

        NetHelpers.WriteVector2(savedPosition, writer);

        writer.Write(sid);

        AmongUsClient.Instance.FinishRpcImmediately(writer);

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (!isBarrierActive && cooldownTimer > 0f)

        {

            cooldownTimer -= Time.fixedDeltaTime;

            if (cooldownTimer < 0f) cooldownTimer = 0f;

        }



        if (!isBarrierActive) return;

        if (!AmongUsClient.Instance.AmHost) return;

        if (!Player.IsAlive()) { DeactivateBarrier(false); return; }



        barrierTimer += Time.fixedDeltaTime;



        var currentPos = Player.transform.position;

        if (Vector2.Distance(currentPos, savedPosition) > 0.02f)

            SnapPlayerToSaved();



        if (!TownOfHostL.Modules.PlayerSpeed.Has(Player.PlayerId, nameof(Sage)))

        {

            TownOfHostL.Modules.PlayerSpeed.Freeze(Player.PlayerId, nameof(Sage));

            Player.MarkDirtySettings();

        }



        if (barrierTimer >= BarrierDuration)

            DeactivateBarrier();

    }



    public override bool OnCheckMurderAsTarget(MurderInfo info)

    {

        if (!info.DoKill) return true;



        if (!isBarrierActive) return true;



        var killer = info.AttemptKiller;

        if (killer == null) return true;



        info.DoKill = false;



        Logger.Info($"{Player.Data?.PlayerName} がバリアでキルを反射 → {killer.Data?.PlayerName}", "Sage");



        _ = new LateTask(() =>

        {

            if (!killer.IsAlive()) return;

            PlayerState.GetByPlayerId(killer.PlayerId).DeathReason = CustomDeathReason.Counter;

            killer.RpcMurderPlayerV2(killer);

            Utils.AddGameLog("Sage",

                $"{Player.ColoredName()}のバリアが{killer.ColoredName()}のキルを反射した");

        }, 0.1f, "Sage.ReflectKill");



        DeactivateBarrier();



        return false;

    }



    public override void OnStartMeeting()

    {

        if (isBarrierActive)

            DeactivateBarrier(false);

    }



    public override void AfterMeetingTasks()

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (!Player.IsAlive()) return;



        AURoleOptions.EngineerCooldown = BarrierCooldown;

        Player.RpcResetAbilityCooldown();

        cooldownTimer = BarrierCooldown;

    }



    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,

        bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        if (isForMeeting) return "";



        string size = isForHud ? "" : "<size=60%>";

        string color = RoleInfo.RoleColorCode;



        if (isBarrierActive)

        {

            float remaining = Mathf.Max(0f, BarrierDuration - barrierTimer);

            return $"{size}<color={color}>【バリア発動中】{remaining:F1}s | 動けません</color>";

        }



        return $"{size}<color={color}>ペットなで → 聖なるバリア発動</color>";

    }



    public override string GetProgressText(bool comms = false)

    {

        if (!Player.IsAlive()) return "";

        if (isBarrierActive)

        {

            float remaining = Mathf.Max(0f, BarrierDuration - barrierTimer);

            return $"<color={RoleInfo.RoleColorCode}>({remaining:F1}s)</color>";

        }

        return "";

    }



    void SendRpc()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write(isBarrierActive);

        sender.Writer.Write(barrierTimer);

        sender.Writer.Write(cooldownTimer);

        sender.Writer.Write(savedPosition.x);

        sender.Writer.Write(savedPosition.y);

    }



    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)

    {
        if (rpcType != CustomRPC.CustomRoleSync) return;

        isBarrierActive = reader.ReadBoolean();

        barrierTimer = reader.ReadSingle();

        cooldownTimer = reader.ReadSingle();

        float x = reader.ReadSingle();

        float y = reader.ReadSingle();

        savedPosition = new Vector2(x, y);

    }


}

