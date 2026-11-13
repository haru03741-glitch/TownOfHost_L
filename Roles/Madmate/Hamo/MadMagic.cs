using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Modules.SelfVoteManager;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Madmate;

public sealed class MadMagic : RoleBase, IKillFlashSeeable, IDeathReasonSeeable, ISelfVoter
{
    public override bool NeedsSeenNames => canSeeDeathReason;
    // 役職の元: TownOfHost-hamo
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadMagic),
            player => new MadMagic(player),
            CustomRoles.MadMagic,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.Hamo + 93000,
            SetupOptionItem,
            "mmg",
            "#ff1919",
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );

    static OptionItem OptionTurns;
    static OptionItem[] OptionChoices;
    enum OptionName { MadMagicTurns, MadMagicChoice1, MadMagicChoice2, MadMagicChoice3 }

    static CustomRoles[] Candidates => CustomRolesHelper.AllRoles
        .Where(r => r.GetRoleInfo()?.CustomRoleType == CustomRoleTypes.Madmate && r is not (CustomRoles.MadWorker or CustomRoles.MadMagic or CustomRoles.SKMadmate))
        .ToArray();

    static void SetupOptionItem()
    {
        OptionTurns = IntegerOptionItem.Create(RoleInfo, OptionName.MadMagicTurns, new(1, 99, 1), 2, false).SetValueFormat(OptionFormat.Turns);
        var names = new[] { "Random" }.Concat(Candidates.Select(r => r.ToString())).ToArray();
        OptionChoices = new[]
        {
            StringOptionItem.Create(RoleInfo, OptionName.MadMagicChoice1, names, 0, false),
            StringOptionItem.Create(RoleInfo, OptionName.MadMagicChoice2, names, 0, false),
            StringOptionItem.Create(RoleInfo, OptionName.MadMagicChoice3, names, 0, false),
        };
        Options.OverrideTasksData.Create(RoleInfo);
    }

    public MadMagic(PlayerControl player) : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        canSeeKillFlash = Options.MadmateCanSeeKillFlash.GetBool();
        canSeeDeathReason = Options.MadmateCanSeeDeathReason.GetBool();
    }
    static bool canSeeKillFlash;
    static bool canSeeDeathReason;
    public bool? CheckKillFlash(MurderInfo info) => canSeeKillFlash;
    public bool CheckSeeDeathReason(PlayerControl seen) => canSeeDeathReason;

    readonly List<CustomRoles> choices = new();
    bool choosing;

    // ===== 変身中の人(役職が変わるとこのクラスは消えるので static で覚える) =====
    static readonly Dictionary<byte, int> transformed = new();

    public override void Add()
    {
        choices.Clear();
        var pool = Candidates.ToList();
        foreach (var opt in OptionChoices)
        {
            var v = opt.GetValue();
            CustomRoles r;
            if (v <= 0 || v - 1 >= pool.Count) r = pool.Count > 0 ? pool[IRandom.Instance.Next(pool.Count)] : CustomRoles.Madmate;
            else r = pool[v - 1];
            choices.Add(r);
        }
    }

    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability() || voter.PlayerId != Player.PlayerId || !Player.IsAlive()) return true;
        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
            {
                choosing = true;
                var list = string.Join("\n", choices.Select((r, i) => $"{i + 1}. {Utils.GetRoleColorAndName(r)}"));
                Utils.SendMessage(string.Format(GetString("MadMagicChoose"), list, OptionTurns.GetInt()), Player.PlayerId);
            }
            if (status is VoteStatus.Skip)
            {
                choosing = false;
                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            }
            SetMode(Player, status is VoteStatus.Self);
            return false;
        }
        return true;
    }

    public void HandleCommand(string[] args)
    {
        var me = Player.PlayerId;
        if (!GameStates.IsMeeting || !choosing) { Utils.SendMessage(GetString("MadMagicNotChoosing"), me); return; }
        if (args.Length < 2 || !int.TryParse(args[1], out var n) || n < 1 || n > choices.Count)
        {
            Utils.SendMessage(GetString("MadMagicUsage"), me);
            return;
        }
        var role = choices[n - 1];
        choosing = false;
        transformed[me] = MeetingStates.Day + OptionTurns.GetInt();
        Utils.SendMessage(string.Format(GetString("MadMagicTransformed"), Utils.GetRoleColorAndName(role), OptionTurns.GetInt()), me);
        Transform(Player, role);
    }

    static void Transform(PlayerControl pc, CustomRoles role)
    {
        // タスク数は変わらない(PlayerState のタスク情報はそのまま)
        pc.RpcChangeRole(role);
        var baseRole = role.GetRoleInfo()?.BaseRoleType?.Invoke() ?? RoleTypes.Crewmate;
        pc.RpcSetRoleDesync(baseRole, pc.GetClientId());
        pc.MarkDirtySettings();
        Utils.NotifyRoles();
    }

    /// <param name="day">終わった会議の日</param>
    public static void AfterMeeting(int day)
    {
        if (transformed.Count == 0) return;
        foreach (var (id, until) in transformed.ToList())
        {
            if (day < until) continue;
            transformed.Remove(id);
            var pc = Utils.GetPlayerById(id);
            if (pc == null || !pc.IsAlive()) continue;
            Transform(pc, CustomRoles.MadMagic);
            Utils.SendMessage(GetString("MadMagicReverted"), id);
        }
    }

    [Attributes.GameModuleInitializer]
    public static void Clear() => transformed.Clear();

    public static bool IsTransformed(byte id) => transformed.ContainsKey(id);
}
