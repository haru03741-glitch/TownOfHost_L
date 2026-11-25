using System.Collections.Generic;
using System.Linq;

using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

/// <summary>
/// スレイヴドライバー (ExtremeRoles の SlaveDriver)<br/>
/// 能力「ハラスメント」で近くの人に印を付ける。会議が始まると、印の人の済んだタスクを設定数だけ新しいタスクに戻す。
/// </summary>
public sealed class SlaveDriver : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: ExtremeRoles
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SlaveDriver),
            player => new SlaveDriver(player),
            CustomRoles.SlaveDriver,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 76400,
            SetupOptionItems,
            "sdr"
        );
    public SlaveDriver(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        CanSeeTaskBar = OptionCanSeeTaskBar.GetBool();
        Cooldown = OptionCooldown.GetFloat();
        RevartTaskNum = OptionRevartTaskNum.GetInt();
        Range = OptionRange.GetFloat();
        AbilityCount = OptionAbilityCount.GetInt();
        effectPlayer.Clear();
    }

    static OptionItem OptionCanSeeTaskBar;
    static OptionItem OptionAbilityCount;
    static OptionItem OptionCooldown;
    static OptionItem OptionRevartTaskNum;
    static OptionItem OptionRange;
    enum OptionName
    {
        SlaveDriverCanSeeTaskBar,
        SlaveDriverAbilityCount,
        SlaveDriverRevartTaskNum,
        SlaveDriverRange,
    }

    static bool CanSeeTaskBar;
    static float Cooldown;
    static int RevartTaskNum;
    static float Range;
    int AbilityCount;
    readonly HashSet<byte> effectPlayer = new();

    static void SetupOptionItems()
    {
        OptionCanSeeTaskBar = BooleanOptionItem.Create(RoleInfo, OptionName.SlaveDriverCanSeeTaskBar, true, false);
        OptionAbilityCount = IntegerOptionItem.Create(RoleInfo, OptionName.SlaveDriverAbilityCount, new(1, 10, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0.5f, 120f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRevartTaskNum = IntegerOptionItem.Create(RoleInfo, OptionName.SlaveDriverRevartTaskNum, new(1, 5, 1), 2, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionRange = FloatOptionItem.Create(RoleInfo, OptionName.SlaveDriverRange, new(0.25f, 3.5f, 0.25f), 0.75f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        if (AbilityCount <= 0 || !Player.IsAlive()) return;

        // 範囲内で一番近く、まだ印の無い人
        var pos = (Vector2)Player.transform.position;
        var target = Main.AllAlivePlayerControls
            .Where(pc => pc.PlayerId != Player.PlayerId && !pc.inVent && !pc.GetCustomRole().IsSpectator())
            .Select(pc => (pc, dis: Vector2.Distance(pos, pc.transform.position)))
            .Where(x => x.dis <= Range)
            .OrderBy(x => x.dis)
            .Select(x => x.pc)
            .FirstOrDefault();
        if (target == null || effectPlayer.Contains(target.PlayerId))
        {
            ResetCooldown = false;
            return;
        }

        ResetCooldown = true;
        effectPlayer.Add(target.PlayerId);
        AbilityCount--;
        Logger.Info($"{Player.GetNameWithRole()} => {target.GetNameWithRole()}: ハラスメント (残り {AbilityCount})", "SlaveDriver");
        if (AbilityCount <= 0) Player.MarkDirtySettings();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnReportDeadBody(PlayerControl _, NetworkedPlayerInfo __)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        foreach (var id in effectPlayer)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null || !Utils.HasTasks(pc.Data, false)) continue;
            if (ReplaceTasks(pc, RevartTaskNum) > 0)
                RoleHooks.MeetingStartMessages.Add((pc.PlayerId, GetString("SlaveDriverReportMessage"), Utils.ColorString(RoleInfo.RoleColor, GetString("SlaveDriver"))));
        }
        effectPlayer.Clear();
    }

    /// <summary>済んだタスクを最大 num 個、同じ種類 (共通・ロング・ショート) の新しいタスクに戻す。戻した数を返す</summary>
    static int ReplaceTasks(PlayerControl pc, int num)
    {
        var ship = ShipStatus.Instance;
        if (ship == null) return 0;
        var tasks = pc.Data.Tasks.ToArray();
        var types = tasks.Select(t => t.TypeId).ToList();
        var replaced = new HashSet<uint>();

        foreach (var task in tasks.OrderBy(_ => IRandom.Instance.Next(int.MaxValue)))
        {
            if (replaced.Count >= num) break;
            if (!task.Complete) continue;

            NormalPlayerTask[] pool;
            if (ship.CommonTasks.Any(t => t.Index == task.TypeId)) pool = ship.CommonTasks;
            else if (ship.LongTasks.Any(t => t.Index == task.TypeId)) pool = ship.LongTasks;
            else if (ship.ShortTasks.Any(t => t.Index == task.TypeId)) pool = ship.ShortTasks;
            else continue;
            if (pool.Length == 0) continue;

            // できれば今持っていない種類にする
            var candidates = pool.Where(t => !types.Contains((byte)t.Index)).ToArray();
            if (candidates.Length == 0) candidates = pool;
            var newType = (byte)candidates[IRandom.Instance.Next(candidates.Length)].Index;

            types[tasks.ToList().IndexOf(task)] = newType;
            replaced.Add(task.Id);
        }
        if (replaced.Count == 0) return 0;

        // 残すべき済みタスク (配り直すと全部未完了になるので、後で済みに戻す)
        var keepCompleted = tasks
            .Select((t, i) => (t, i))
            .Where(x => x.t.Complete && !replaced.Contains(x.t.Id))
            .Select(x => (uint)x.i)
            .ToArray();

        var state = pc.GetPlayerTaskState();
        state.CompletedTasksCount = Mathf.Max(0, state.CompletedTasksCount - replaced.Count);

        RpcSetTasksPatch.SkipPatch = true;
        try { pc.Data.RpcSetTasks(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(types.ToArray())); }
        finally { RpcSetTasksPatch.SkipPatch = false; }

        // 配り直した後、残す済みタスクを済みに戻す (数えはしない)
        _ = new LateTask(() =>
        {
            if (pc == null || pc.Data == null) return;
            PlayerControlCompleteTaskPatch.Silent = true;
            try
            {
                foreach (var idx in keepCompleted)
                {
                    var info = pc.Data.FindTaskById(idx);
                    if (info != null && !info.Complete) pc.RpcCompleteTask(idx);
                }
            }
            finally { PlayerControlCompleteTaskPatch.Silent = false; }
            GameData.Instance.RecomputeTaskCounts();
            Utils.NotifyRoles();
        }, 1f, "SlaveDriver.ReCompleteTasks");

        Logger.Info($"{pc.GetNameWithRole()}: {replaced.Count} 個のタスクを戻した", "SlaveDriver");
        return replaced.Count;
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer.PlayerId != Player.PlayerId || !effectPlayer.Contains(seen.PlayerId)) return "";
        return Utils.ColorString(RoleInfo.RoleColor, " ★");
    }

    bool IUsePhantomButton.IsPhantomRole => AbilityCount > 0;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = AbilityCount <= 0 ? 200f : Cooldown;
        if (CanSeeTaskBar) opt.SetInt(Int32OptionNames.TaskBarMode, (int)TaskBarMode.Normal);
    }
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(0 < AbilityCount ? RoleInfo.RoleColor : Color.gray, $"({AbilityCount})");
    public override string GetAbilityButtonText() => GetString("SlaveDriverAbilityText");
}
