using System;

using AmongUs.GameOptions;

using Hazel;

using TownOfHostL.Modules;

using TownOfHostL.Patches;

using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;

using UnityEngine;



namespace TownOfHostL.Roles.Crewmate;



public sealed class Sage : RoleBase, IUsePetButton

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



    public override void OnDestroy()

    {
        Holdings.RemoveSpeed(Player.PlayerId, nameof(Sage));




        if (isBarrierActive)

        {

            Holdings.RemoveSpeed(Player.PlayerId, nameof(Sage));

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



    void IUsePetButton.OnPet() => ActivateBarrier();
    public void ActivateBarrier()

    {

        if (!Player.IsAlive()) return;

        if (isBarrierActive) return;

        if (cooldownTimer > 0f) return;



        isBarrierActive = true;

        barrierTimer = 0f;

        savedPosition = Player.transform.position;



        Holdings.Freeze(Player.PlayerId, nameof(Sage));

        Player.MarkDirtySettings();

        Player.SyncSettings();



        SnapPlayerToSaved();



        Utils.NotifyRoles();

        Logger.Info($"{Player.Data?.PlayerName} がバリアを発動", "Sage");

    }



    private void DeactivateBarrier(bool resetCooldown = true)

    {

        if (!isBarrierActive) return;

        isBarrierActive = false;

        barrierTimer = 0f;



        Holdings.RemoveSpeed(Player.PlayerId, nameof(Sage));

        Player.MarkDirtySettings();

        Player.SyncSettings();



        if (resetCooldown)

        {

            // 送る設定のクールは cooldownTimer から作るので、先に戻してから、設定が届いた後に数え直す
            cooldownTimer = BarrierCooldown;

            Player.RpcResetAbilityCooldown(sync: true);

        }



        Utils.NotifyRoles();

    }



    // 障壁の間はその場に留める。ずれを直す TP は 0.2 秒以上あけ、0.3 以上ずれた時だけ送る (毎フレーム送ると SnapTo の予算が尽きる)
    const float ResnapInterval = 0.2f, ResnapMinDrift = 0.3f;
    float nextResnapAt;

    private void SnapPlayerToSaved(bool force = false)
    {
        if (Player == null) return;
        if (Player.RpcSnapTo(savedPosition, force)) nextResnapAt = Time.time + ResnapInterval;
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

        if (Time.time >= nextResnapAt && Vector2.Distance(currentPos, savedPosition) > ResnapMinDrift)

            SnapPlayerToSaved();



        if (!TownOfHostL.Modules.PlayerSpeed.Has(Player.PlayerId, nameof(Sage)))

        {

            Holdings.Freeze(Player.PlayerId, nameof(Sage));

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



        // 反射のキルは起きたことの結果なので、役職が外れても走らせる
        _ = new LateTask(() =>

        {

            if (!killer.IsAlive()) return;

            PlayerState.GetByPlayerId(killer.PlayerId).DeathReason = CustomDeathReason.Counter;

            Kill.Suicide(killer);

            Utils.AddGameLog("Sage",

                string.Format(Translator.GetString("SageBarrierReflectLog"), Player.ColoredName(), killer.ColoredName()));

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



        cooldownTimer = BarrierCooldown;

        Player.RpcResetAbilityCooldown(sync: true);

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

            return $"{size}<color={color}>{string.Format(Translator.GetString("SageBarrierActive"), remaining)}</color>";

        }



        return $"{size}<color={color}>{Translator.GetString("SageBarrierHint")}</color>";

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

}

