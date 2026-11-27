using System.Linq;
using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHostL.Roles.Neutral;

/// <summary>妖狐の仲間が、親の妖狐が死んだら後を追う処理 (子狐・背徳者で共通)</summary>
static class SpectreTeam
{
    /// <summary>作業中に親が死んでいたら、自分も死ぬ (ホスト)</summary>
    public static void CheckFollowDeath(PlayerControl me)
    {
        if (!me.IsAlive()) return;
        var spectre = Spectre.GetMySpectre(me.PlayerId);
        if (spectre == null || spectre.Player.IsAlive()) return;
        var state = PlayerState.GetByPlayerId(me.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.FollowingSuicide;
        me.SetRealKiller(spectre.Player);
        Kill.Suicide(me);
        Logger.Info($"{me.GetNameWithRole()}: 妖狐の後を追った", "SpectreTeam");
    }
    /// <summary>会議で親が死んだ (追放・推察) 時は、会議明けに一緒に死ぬ</summary>
    public static void AfterMeeting(PlayerControl me)
    {
        if (!AmongUsClient.Instance.AmHost || !me.IsAlive()) return;
        var spectre = Spectre.GetMySpectre(me.PlayerId);
        if (spectre == null) return;
        if (spectre.Player.IsAlive() && !Main.AfterMeetingDeathPlayers.ContainsKey(spectre.Player.PlayerId)) return;
        Main.AfterMeetingDeathPlayers.TryAdd(me.PlayerId, CustomDeathReason.FollowingSuicide);
    }
}

/// <summary>
/// 子狐 (Nebula on the Ship の SpectreFollower)<br/>
/// 妖狐の仲間。お揚げを食べると、親の妖狐の満腹度が増える。親が勝てば勝ち、親が死ぬと後を追う。
/// </summary>
public sealed class SpectreFollower : RoleBase
{
    // 役職の元: Nebula on the Ship
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SpectreFollower),
            player => new SpectreFollower(player),
            CustomRoles.SpectreFollower,
            () => Spectre.OptionFollowerCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 51550,
            null,
            "sfo",
            "#b998c5",
            countType: CountTypes.Fox,
            flags: RoleFlags.DontShowOption,
            introTeam: CustomRoles.Spectre
        );
    public SpectreFollower(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    { }

    public override void OnFixedUpdate(PlayerControl _)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.InMeetingWindow) return;
        SpectreTeam.CheckFollowDeath(Player);
        if (!Player.IsAlive()) return;
        var spectre = Spectre.GetMySpectre(Player.PlayerId);
        if (spectre == null || !spectre.Player.IsAlive()) return;
        // お揚げを食べると、親の満腹度が増える
        SpectreDishes.TryEat(Player, () => spectre.AddSatiety(Spectre.OptionFollowerSatietyRate.GetFloat()));
    }
    public override void AfterMeetingTasks() => SpectreTeam.AfterMeeting(Player);
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = 0f;
        AURoleOptions.EngineerInVentMaxTime = 0f;
        opt.SetVision(true);
    }
    public override string GetProgressText(bool comms = false)
        => Spectre.GetMySpectre(Player.PlayerId)?.SatietyText() ?? "";
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId || !Player.IsAlive()) return "";
        return SpectreDishes.LowerText(Player, Spectre.OptionFollowerShowDishesOnMap.GetBool(), full: true);
    }
}

/// <summary>
/// 背徳者 (Nebula on the Ship の SpectreImmoralist)<br/>
/// 妖狐の仲間。生きている間は空の皿にお揚げを供え、死んだ後は皿のお揚げを消せる (親が生きている間)。
/// 好きな時に自決できる (ペットをなでる)。親が勝てば勝ち、親が死ぬと後を追う。
/// </summary>
public sealed class SpectreImmoralist : RoleBase, IKillFlashSeeable, IUsePetButton
{
    // 役職の元: Nebula on the Ship
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SpectreImmoralist),
            player => new SpectreImmoralist(player),
            CustomRoles.SpectreImmoralist,
            () => Spectre.OptionImmoralistCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 51580,
            null,
            "sim",
            "#b998c5",
            countType: CountTypes.Fox,
            flags: RoleFlags.DontShowOption,
            introTeam: CustomRoles.Spectre
        );
    public SpectreImmoralist(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        RespawnCooldown = Spectre.OptionImmoralistRespawnCooldown.GetFloat();
        RespawnDuration = Spectre.OptionImmoralistRespawnDuration.GetFloat();
        cooldown = RespawnCooldown;
        suicideCooldown = SuicideCooldown;
        placing = null;
    }
    float RespawnCooldown;
    float RespawnDuration;
    const float SuicideCooldown = 5f;
    const float PlaceRange = 0.8f;
    // 死後に皿のお揚げを消すまで、そばに立つ時間
    const float RemoveTime = 1f;

    float cooldown;
    float suicideCooldown;
    (int dishId, float time)? placing;

    public override void OnFixedUpdate(PlayerControl _)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.InMeetingWindow) return;
        SpectreTeam.CheckFollowDeath(Player);

        if (Player.IsAlive())
        {
            if (suicideCooldown > 0f) suicideCooldown -= Time.fixedDeltaTime;
            if (cooldown > 0f)
            {
                cooldown -= Time.fixedDeltaTime;
                if (cooldown <= 0f) Utils.NotifyRoles(SpecifySeer: Player);
                return;
            }
            // 奉納: 空の皿のそばに少し立つと、お揚げが出る (離れると取りやめ)
            Step(ate: true, RespawnDuration, dish =>
            {
                SpectreDishes.SetState(dish, false);
                cooldown = RespawnCooldown;
                Logger.Info($"{Player.GetNameWithRole()}: お揚げを奉納 (皿 {dish.Id})", "SpectreImmoralist");
            });
        }
        else
        {
            // 消去: 親が生きている間、お揚げの載った皿のそばに少し立つと、お揚げを消す
            var spectre = Spectre.GetMySpectre(Player.PlayerId);
            if (spectre == null || !spectre.Player.IsAlive())
            {
                placing = null;
                return;
            }
            Step(ate: false, RemoveTime, dish =>
            {
                SpectreDishes.SetState(dish, true);
                Logger.Info($"{Player.GetNameWithRole()}: お揚げを消した (皿 {dish.Id})", "SpectreImmoralist");
            });
        }
    }
    void Step(bool ate, float need, System.Action<SpectreDishes.Dish> done)
    {
        var dish = Player.inVent ? null : SpectreDishes.Nearest(Player.GetTruePosition(), PlaceRange, ate);
        if (dish == null)
        {
            if (placing != null)
            {
                placing = null;
                Utils.NotifyRoles(SpecifySeer: Player);
            }
            return;
        }
        var time = placing is (int id, float t) && id == dish.Id ? t + Time.fixedDeltaTime : Time.fixedDeltaTime;
        if (placing == null || placing.Value.dishId != dish.Id) Utils.NotifyRoles(SpecifySeer: Player);
        if (time < need)
        {
            placing = (dish.Id, time);
            return;
        }
        placing = null;
        done(dish);
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnReportDeadBody(PlayerControl _, NetworkedPlayerInfo __) => placing = null;
    public override void AfterMeetingTasks() => SpectreTeam.AfterMeeting(Player);

    // 自決 (ペットをなでる)
    bool IUsePetButton.UsesPet => Spectre.OptionImmoralistCanSuicide.GetBool();
    public void OnPet()
    {
        if (!Spectre.OptionImmoralistCanSuicide.GetBool() || !Player.IsAlive() || suicideCooldown > 0f) return;
        if (!GameStates.IsInTask || GameStates.InMeetingWindow) return;
        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.Suicide;
        Kill.Suicide(Player);
        Logger.Info($"{Player.GetNameWithRole()}: 自決", "SpectreImmoralist");
    }

    public bool CheckKillFlash(MurderInfo info) => Spectre.OptionImmoralistShowKillFlash.GetBool();
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = 0f;
        AURoleOptions.EngineerInVentMaxTime = 0f;
        opt.SetVision(true);
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId) return "";
        var alive = Player.IsAlive();
        var text = "";
        if (placing != null) text = Translator.GetString(alive ? "SpectreImmoralistPlacing" : "SpectreImmoralistRemoving");
        else if (alive && cooldown > 0f) text = Translator.GetString("SpectreImmoralistCooldown");
        if (text != "") text = Utils.ColorString(RoleInfo.RoleColor, text);
        var dishes = SpectreDishes.LowerText(Player, Spectre.OptionImmoralistShowDishesOnMap.GetBool(), full: !alive);
        if (dishes == "") return text;
        return text == "" ? dishes : text + "\n" + dishes;
    }
}
