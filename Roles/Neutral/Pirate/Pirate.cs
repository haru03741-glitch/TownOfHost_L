using AmongUs.GameOptions;
using System;
using System.Collections.Generic;
using System.Linq;

using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Neutral;
public sealed class Pirate : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Pirate),
            player => new Pirate(player),
            CustomRoles.Pirate,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1000,
            SetupOptionItem,
            "海賊",
            "#cc4b33",
            true,
            countType: CountTypes.Pirate,
            assignInfo: new RoleAssignInfo(CustomRoles.Pirate, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1)
            },
            flags: RoleFlags.FixedCount | RoleFlags.DirectKill | RoleFlags.NeutralKiller,
            killingFaction: new(CountTypes.Pirate, CustomWinner.Pirate, 200,
                [CustomRoles.Pirate, CustomRoles.Gang], [CustomRoles.Gang])
        );
    public Pirate(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCoolDown = OptionKillCoolDown.GetFloat();
        HasImpostorVision = OptionHasImpostorVision.GetBool();
        LimitTurn = OptionLimitTurn.GetInt();
        CanImpostorBeGang = OptionCanImpostorBeGang.GetBool();
        CanMadmateBeGang = OptionCanMadmateBeGang.GetBool();
        CanNeutralBeGang = OptionCanNeutralBeGang.GetBool();

        if (BuffAddonAssignTarget.GetValue() == 0)//Random
        {
            int chance = IRandom.Instance.Next(0, BuffAddonRoles.Length);
            grantAddonRole = BuffAddonRoles[chance];
            Logger.Info($"ランダム付与属性決定：{grantAddonRole}", "Pirate");
        }
        else
        {
            grantAddonRole = BuffAddonRoles[BuffAddonAssignTarget.GetValue() - 1];
            Logger.Info($"付与属性：{grantAddonRole}", "Pirate");
        }

        TurnNumber = 1;
    }
    private static HashSet<Pirate> Pirates = new(15);

    private static OptionItem OptionKillCoolDown;
    private static OptionItem OptionHasImpostorVision;
    private static OptionItem OptionLimitTurn;
    private static OptionItem OptionCanImpostorBeGang;
    private static OptionItem OptionCanMadmateBeGang;
    private static OptionItem OptionCanNeutralBeGang;
    private static OptionItem BuffAddonAssignTarget;
    private static Options.OverrideTasksData Tasks;
    enum OptionName
    {
        PirateLimitTurn,
        PirateImpostorCanBeGang,
        PirateMadmateCanBeGang,
        PirateNeutralCanBeGang,
        PirateBuffAddonAssignTarget,
    }
    private static float KillCoolDown;
    private static bool HasImpostorVision;
    private static int LimitTurn;
    private static bool CanImpostorBeGang;
    private static bool CanMadmateBeGang;
    private static bool CanNeutralBeGang;
    private static int TurnNumber;
    public static CustomRoles grantAddonRole = CustomRoles.NotAssigned;

    static CustomRoles[] BuffAddonRoles = CustomRolesHelper.AllAddOnRoles.Where(role => role.IsBuffAddOn() && role != CustomRoles.Loyalty).ToArray();
    static string[] buffRoleArrays = BuffAddonRoles.Select(role => role.ToString()).ToArray();
    static string[] randArrays = { "Random" };
    static string[] selectStringArray = randArrays.Concat(buffRoleArrays).ToArray();
    private static void SetupOptionItem()
    {
        OptionKillCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, GeneralOption.ImpostorVision, true, false);
        OptionLimitTurn = IntegerOptionItem.Create(RoleInfo, OptionName.PirateLimitTurn, new(1, 30, 1), 3, false)
            .SetValueFormat(OptionFormat.Turns);
        OptionCanImpostorBeGang = BooleanOptionItem.Create(RoleInfo, OptionName.PirateImpostorCanBeGang, false, false);
        OptionCanMadmateBeGang = BooleanOptionItem.Create(RoleInfo, OptionName.PirateMadmateCanBeGang, true, false);
        OptionCanNeutralBeGang = BooleanOptionItem.Create(RoleInfo, OptionName.PirateNeutralCanBeGang, true, false);

        BuffAddonAssignTarget = StringOptionItem.Create(RoleInfo, OptionName.PirateBuffAddonAssignTarget, selectStringArray, 0, false);

        // 20-23を使用
        Tasks = Options.OverrideTasksData.Create(RoleInfo, Options.CustomRoleSpawnChances[RoleInfo.RoleName], CustomRoles.Gang);
    }

    public PlayerControl Gang;
    bool isMadeGang = false;

    // 試合を跨いで前の試合のインスタンスが残らないようにする
    public override void OnDestroy()
    {
        Pirates.Remove(this);
    }

    public override void Add()
    {
        var playerId = Player.PlayerId;
        Gang = null;
        Pirates.Add(this);

        isMadeGang = false;
    }
    public float CalculateKillCooldown() => KillCoolDown;
    public bool CanUseKillButton()
    {
        if (!isMadeGang) return true;
        if (Gang == null) return true;

        return GangTaskRate() >= 50;
    }
    public bool CanUseImpostorVentButton()
    {
        if (!isMadeGang) return false;
        if (Gang == null) return true;

        return GangTaskRate() >= 25;
    }
    // 一味のタスクの進み (%)。タスクを持たない時は 0 で割らず、終えたものとして扱う
    int GangTaskRate()
    {
        var state = Gang.GetPlayerTaskState();
        return state.AllTasksCount <= 0 ? 100 : state.CompletedTasksCount * 100 / state.AllTasksCount;
    }
    public override void ApplyGameOptions(IGameOptions opt) => opt.SetVision(HasImpostorVision);
    // 一味にできない相手への押しはキルでないので、守りより前に振り分ける (押した相手の守りを使わせない)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        if (isMadeGang) return true;

        var (killer, target) = info.AttemptTuple;
        if ((!CanImpostorBeGang && target.Is(CustomRoleTypes.Impostor))
            || (!CanMadmateBeGang && target.Is(CustomRoleTypes.Madmate))
            || (!CanNeutralBeGang && target.Is(CustomRoleTypes.Neutral)))
        {
            killer.RpcProtectedMurderPlayer(target);
            return false;
        }
        return true;
    }
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        // ガード持ちに関わらず能力発動する直接キル役職 (役職に関わらない守りは通らない)。
        // それでも守護者などで守りの力が勝ったキルでは一味にしない (相手が死なないのに一味に数えてしまう)
        // セージの守りのように、役職がキルを止めた (DoKill を下ろした) 時も一味にしない
        if (!info.IsCanKilling) return;
        if (isMadeGang) return;

        var (killer, target) = info.AttemptTuple;

        Gang = target;
        Logger.Info($"{killer.GetNameWithRole()} : 一味にした[{target.GetNameWithRole()}]", "Pirate");

        Utils.NotifyRoles(SpecifySeer: killer);
        isMadeGang = true;
    }

    // 一味の対象海賊
    public static PlayerControl PirateOfGang(PlayerControl target)
    {
        foreach (var pirate in Pirates.ToArray())
        {
            if (pirate.Gang == target) return pirate.Player;
        }
        return null;
    }

    // Gang task
    public static bool TargetSetGhostAndTask(PlayerControl target)
    {
        foreach (var pirate in Pirates.ToArray())
        {
            if (pirate.Gang != target) continue;

            if (AmongUsClient.Instance.AmHost)
            {
                target.RpcSetCustomRole(CustomRoles.Gang);
                target.GetPlayerTaskState().CompletedTasksCount = 0;
                target.Data.RpcSetTasks(Array.Empty<byte>()); //タスクを再配布
                // 数は配り直した後のタスクで数える (前に数えると元の役職の共通タスクの分が残る)
                target.GetPlayerTaskState().AllTasksCount = target.Data.Tasks.Count;
                target.SyncSettings();
                Utils.NotifyRoles();
            }
            return true;
        }
        return false;
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
    {
        //seenが省略の場合seer
        seen ??= seer;
        if (seer == Player && Gang == seen) return Utils.ColorString(RoleInfo.RoleColor, "▲");

        return string.Empty;
    }
    public override void OnStartMeeting() => TurnNumber++;
    public override string GetProgressText(bool comms = false)
    {
        if (LimitTurn < TurnNumber || !Player.IsAlive() || isMadeGang) return string.Empty;

        return $"[{TurnNumber}/{LimitTurn}]".Color(RoleInfo.RoleColor);
    }
    public override void AfterMeetingTasks()
    {
        if (LimitTurn >= TurnNumber || !Player.IsAlive() || isMadeGang) return;

        Main.AfterMeetingDeathPlayers.TryAdd(Player.PlayerId, CustomDeathReason.Suicide);
        Logger.Info($"PlatonicLover:dead, Turn:{TurnNumber} > {LimitTurn}", "Pirate");
    }
}
