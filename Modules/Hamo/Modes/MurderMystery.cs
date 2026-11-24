using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Core.Modes;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem MMTimeLimit;
    public static OptionItem MMSeeImpostorArrow;
    public static OptionItem MMCrewvision;
    public static OptionItem MMImpostorvision;

    static void SetupMurderMysteryOptions()
    {
        const CustomGameMode mode = CustomGameMode.MurderMystery;
        MMTimeLimit = FloatOptionItem.Create("MMTimeLimit", new(70, 600, 5), 360, TabGroup.ModMainSettings, false)
            .SetHeader(true).SetColor(Color.blue).SetValueFormat(OptionFormat.Seconds).SetGameMode(mode);
        MMSeeImpostorArrow = BooleanOptionItem.Create("MMSeeImpostorArrow", true, TabGroup.ModMainSettings, false).SetColor(Color.blue).SetGameMode(mode);
        MMCrewvision = FloatOptionItem.Create("MMCrewvision", new(0.1f, 1.5f, 0.05f), 0.4f, TabGroup.ModMainSettings, false)
            .SetColor(Color.blue).SetValueFormat(OptionFormat.Multiplier).SetGameMode(mode);
        MMImpostorvision = FloatOptionItem.Create("MMImpostorvision", new(0.1f, 3f, 0.05f), 2f, TabGroup.ModMainSettings, false)
            .SetColor(Color.blue).SetValueFormat(OptionFormat.Multiplier).SetGameMode(mode);
        MoveRoleOptionsToMode(CustomRoles.MMArcher, mode);
    }
}

/// <summary>マーダーミステリー。インポスターと保安官 (弓) とクルーだけで、会議もサボタージュも無い</summary>
public sealed class MurderMysteryMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.MurderMystery;
    public override bool AllowsDoorSabotage => false;
    public override bool AllowsSabotage => false;
    public override bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        MurderMystery.OnReportDeadBody(reporter, target);
        return false;
    }
    public override (string Key, Color Color)? Label => ("MurderMystery", new Color32(26, 56, 156, 255));
}

public static class MurderMystery
{
    public static bool IsActive => Options.CurrentGameMode == CustomGameMode.MurderMystery;
    public static List<byte> TaskArchers = new();
    public static float Timer;
    public static bool Sabotage;
    public static int? DeadArcherCount;

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        Timer = (Options.MMTimeLimit?.GetFloat() ?? 360f) + 5;
        DeadArcherCount = null;
        Sabotage = false;
        TaskArchers = new();
    }

    public static void AfterAssign()
    {
        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
        CustomRoleManager.MarkOthers.Add(GetMarkOthers);
        GameEndChecker.SetPredicateTo(Rules);
    }

    public static void OnIntroEnd()
    {
        if (!IsActive) return;
        DeadArcherCount = Main.AllPlayerControls.Count(pc => pc.Is(CustomRoles.MMArcher)) is 0 ? null : 0;
    }

    static IEnumerable<PlayerControl> AliveImpostors => Main.AllAlivePlayerControls.Where(pc => pc.GetCustomRole().IsImpostor());
    static IEnumerable<PlayerControl> AliveCrews => Main.AllAlivePlayerControls.Where(pc => pc.GetCustomRole().IsCrewmate());

    public static void OnFixedUpdate()
    {
        if (!IsActive || !HamoModes.IntroDone || Sabotage) return;
        Timer -= Time.fixedDeltaTime;
        if (Timer > 60) return;
        Sabotage = true;
        HamoSabotageTrigger.Critical(60f);
        if (Options.MMSeeImpostorArrow.GetBool())
            foreach (var impostor in AliveImpostors)
                foreach (var target in AliveCrews)
                    TargetArrow.Add(impostor.PlayerId, target.PlayerId);
        Utils.MarkEveryoneDirtySettings();
        Utils.NotifyRoles();
    }

    public static void ApplyGameOptions(PlayerControl player, IGameOptions opt)
    {
        if (!Sabotage) return;
        var vision = player.GetCustomRole().IsImpostor() ? Options.MMImpostorvision.GetFloat() : Options.MMCrewvision.GetFloat();
        opt.SetFloat(FloatOptionNames.ImpostorLightMod, vision);
        opt.SetFloat(FloatOptionNames.CrewLightMod, vision);
    }

    /// <summary>通報した時、倒れた保安官の弓を引き継ぐ</summary>
    public static void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!IsActive || reporter == null || !reporter.IsAlive() || target == null) return;
        if (!reporter.GetCustomRole().IsCrewmate()) return;
        if (reporter.Is(CustomRoles.MMArcher) && !TaskArchers.Contains(reporter.PlayerId)) return;
        if (CustomRoleManager.GetByPlayerId(target.PlayerId) is not MMArcher dead || dead.IsPromotioned) return;
        if (reporter.GetRoleClass() is MMArcher mm)
        {
            mm.Arrowtime = MMArcher.OptionArrowTime.GetInt() is 0 ? null : MMArcher.OptionArrowTime.GetInt();
            mm.IsPromotioned = false;
        }
        else reporter.RpcSetCustomRole(CustomRoles.MMArcher);
        dead.IsPromotioned = true;
        TaskArchers.Remove(reporter.PlayerId);
        if (DeadArcherCount.HasValue) DeadArcherCount--;
        Utils.NotifyRoles();
    }

    /// <summary>タスクを終えたクルーは、引き継ぎ無しの保安官になる</summary>
    public static void OnCompleteTask(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !player.IsAlive()) return;
        if (!player.GetPlayerTaskState().IsTaskFinished || player.Is(CustomRoles.MMArcher)) return;
        TaskArchers.Add(player.PlayerId);
        player.RpcSetCustomRole(CustomRoles.MMArcher);
        Utils.NotifyRoles();
    }

    public static void OnDeath(PlayerControl target)
    {
        if (!IsActive || DeadArcherCount is null) return;
        if (target.GetRoleClass() is MMArcher { IsPromotioned: false })
        {
            DeadArcherCount++;
            foreach (var pc in Main.AllPlayerControls) pc.KillFlash();
        }
    }

    static string GetMarkOthers(PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        seen ??= seer;
        if (seer != seen) return "";
        var count = $"({AliveImpostors.Count()}|{AliveCrews.Count()})";
        if (seer.GetCustomRole().IsImpostor() && Sabotage && Options.MMSeeImpostorArrow.GetBool())
            return TargetArrow.GetArrows(seer, AliveCrews.Select(pc => pc.PlayerId).ToArray()) + count;
        return count;
    }

    static string GetLowerTextOthers(PlayerControl seer, PlayerControl seen, bool isForMeeting, bool isForHud)
    {
        seen ??= seer;
        if (seen != seer || DeadArcherCount is null) return "";
        var crewtext = "";
        if ((seer.GetRoleClass() as MMArcher)?.IsPromotioned is true or null && seer.GetCustomRole().IsCrewmate())
            crewtext = GetString("MM_ArceherIsDead_Crew");
        return DeadArcherCount.Value > 0 ? $"<#ff1919>{GetString("MM_ArceherIsDead")}{crewtext}</color>" : $"<#30b6ef>{GetString("MM_ArceherIsAlive")}</color>";
    }

    public static readonly GameEndRuleSet Rules = new(CheckGameEnd, GameEndRules.BySabotage);

    static bool CheckGameEnd(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        int imp = AliveImpostors.Count();
        int crew = AliveCrews.Count();
        if (imp == 0 && crew == 0) CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
        else if (crew <= 0) CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
        else if (imp == 0)
        {
            reason = GameOverReason.CrewmatesByVote;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
        }
        else return false;
        return true;
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    static class DeathPatch
    {
        static void Postfix([HarmonyArgument(0)] PlayerControl target)
        {
            if (!AmongUsClient.Instance.AmHost || target == null || target.IsAlive()) return;
            OnDeath(target);
        }
    }
}

/// <summary>マーダーミステリーの保安官。ファントムのボタンで弓を構え、もう一度押すか 5 秒で、動いた向きの逆へ矢を放つ</summary>
public sealed class MMArcher : RoleBase, IKiller, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MMArcher),
            player => new MMArcher(player),
            CustomRoles.MMArcher,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 993000,
            SetUpOptionItem,
            "mmar",
            "#30b6ef",
            isDesyncImpostor: true,
            introSound: () => GetIntroSound(RoleTypes.Shapeshifter),
            countType: CountTypes.Crew
        );

    public MMArcher(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    Vector2 arrowStep;
    Vector2 arrowPos;
    Vector2 setPos;
    bool flying;
    bool setting;
    float timer;
    public bool IsPromotioned;
    public int? Arrowtime;

    static OptionItem OptionCoolDown;
    static OptionItem OptionLostArrowtimer;
    public static OptionItem OptionArrowTime;
    static OptionItem OptionArrowTimeForTaskend;
    static OptionItem OptionArrowSpeed;
    static OptionItem OptionArrowMiss;

    enum OptionName
    {
        ArcherArrowTime, ArcherLostArrowtimer, ArcherArrowSpeed,
        MMArcherArrowMiss, MMArcherArrowTimeForTaskend
    }

    static void SetUpOptionItem()
    {
        OptionCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0f, 180f, 0.5f), 10, false).SetValueFormat(OptionFormat.Seconds);
        OptionLostArrowtimer = FloatOptionItem.Create(RoleInfo, OptionName.ArcherLostArrowtimer, new(0.5f, 10, 0.1f), 3f, false).SetValueFormat(OptionFormat.Seconds);
        OptionArrowTime = IntegerOptionItem.Create(RoleInfo, OptionName.ArcherArrowTime, new(0, 99, 1), 0, false).SetZeroNotation(OptionZeroNotation.Infinity);
        OptionArrowTimeForTaskend = IntegerOptionItem.Create(RoleInfo, OptionName.MMArcherArrowTimeForTaskend, new(0, 99, 1), 3, false).SetZeroNotation(OptionZeroNotation.Infinity);
        OptionArrowSpeed = FloatOptionItem.Create(RoleInfo, OptionName.ArcherArrowSpeed, new(0.5f, 10, 0.25f), 1f, false);
        OptionArrowMiss = StringOptionItem.Create(RoleInfo, OptionName.MMArcherArrowMiss, new[] { "MMArcher_Miss0", "MMArcher_Miss1", "MMArcher_Miss2" }, 0, false);
    }

    static int SpeedSteps => OptionArrowSpeed.GetValue() + 2;

    public override void Add()
    {
        flying = setting = false;
        timer = 0;
        IsPromotioned = false;
        Arrowtime = OptionArrowTime.GetInt() is 0 ? null : OptionArrowTime.GetInt();
        if (MurderMystery.TaskArchers.Contains(Player.PlayerId))
        {
            Arrowtime = OptionArrowTimeForTaskend.GetInt() is 0 ? null : OptionArrowTimeForTaskend.GetInt();
            IsPromotioned = true;
        }
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        AURoleOptions.PhantomCooldown = OptionCoolDown.GetFloat();
    }

    bool IKiller.CanUseKillButton() => false;
    bool IKiller.CanUseSabotageButton() => false;
    bool IKiller.CanUseImpostorVentButton() => false;
    bool IUsePhantomButton.IsPhantomRole => Arrowtime is not 0;

    void Shoot()
    {
        setting = false;
        timer = 0;
        var dir = (Player.GetTruePosition() - setPos).normalized;
        arrowStep = dir;
        while (arrowStep.x + arrowStep.y > 0.4f || arrowStep.x + arrowStep.y < -0.4f
            || arrowStep.x > 0.15f || arrowStep.x < -0.15f
            || arrowStep.y > 0.15f || arrowStep.y < -0.15f)
            arrowStep *= 0.9f;
        arrowStep *= -1;
        arrowPos = setPos + new Vector2(0, 0.3f);
        flying = true;
        Utils.NotifyRoles(SpecifySeer: Player);
        Player.SetKillCooldown();
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = true;
        if (flying || !Player.IsAlive()) return;
        if (setting)
        {
            Shoot();
            return;
        }
        if (Arrowtime is 0) return;
        ResetCooldown = false;
        setting = true;
        setPos = Player.GetTruePosition();
        if (Arrowtime.HasValue) Arrowtime--;
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || (!flying && !setting) || !Player.IsAlive()) return;
        timer += Time.fixedDeltaTime;
        if (setting)
        {
            if (timer > 5f) Shoot();
            return;
        }
        if (timer > OptionLostArrowtimer.GetFloat())
        {
            Reset();
            return;
        }
        for (var i = 0; i <= SpeedSteps; i++)
            if (!Step()) break;
    }

    bool Clear()
    {
        var vector = arrowPos + arrowStep - setPos;
        float dis = vector.magnitude;
        if (PhysicsHelpers.AnyNonTriggersBetween(setPos, vector.normalized, dis, Constants.ShipAndAllObjectsMask)) return false;
        if (PhysicsHelpers.AnyNonTriggersBetween(setPos, vector.normalized, dis, Constants.ShadowMask)) return false;
        return true;
    }

    bool Step()
    {
        arrowPos += arrowStep * 0.25f;
        if (!Clear())
        {
            Reset();
            return false;
        }
        var near = Main.AllAlivePlayerControls
            .Where(t => t.PlayerId != Player.PlayerId)
            .Select(t => (t, d: Vector2.Distance(arrowPos, t.transform.position)))
            .Where(x => x.d <= 0.6f)
            .OrderBy(x => x.d)
            .Select(x => x.t)
            .FirstOrDefault();
        if (near == null) return true;
        var miss = OptionArrowMiss.GetValue();
        if (near.GetCustomRole().IsCrewmate() && miss is 1)
        {
            MyState.DeathReason = CustomDeathReason.Misfire;
            Player.RpcMurderPlayerV2(Player);
            return false;
        }
        if (CustomRoleManager.OnCheckMurder(Player, near, near, near, true, Killpower: 1, deathReason: CustomDeathReason.Hit))
        {
            Player.KillFlash();
            if (near.GetCustomRole().IsCrewmate() && miss is 0)
            {
                MyState.DeathReason = CustomDeathReason.Misfire;
                Player.RpcMurderPlayerV2(Player);
                return false;
            }
        }
        Reset();
        Player.RpcResetAbilityCooldown();
        return false;
    }

    void Reset()
    {
        flying = setting = false;
        timer = 0;
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override string GetProgressText(bool comms = false) => Arrowtime is null ? "" : $"<#{(Arrowtime is 0 ? "ff1919" : "cccccc")}> ({Arrowtime.Value})";

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seer != Player || seen != Player || !Player.IsAlive() || isForMeeting || Arrowtime is 0) return "";
        var size = isForHud ? "" : "<size=60%>";
        if (flying) return $"{size}<#8cffff>{GetString("ArcherLower_ArrowActive")}</size>\n";
        return $"{size}<#8cffff>{GetString(setting ? "ArcherLower_SetBow" : "ArcherLower_Phantom")}</color></size>\n";
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => Reset();

    public override string GetAbilityButtonText() => GetString("Archer_AbilityButton");
}
