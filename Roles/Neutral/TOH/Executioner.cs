using System.Collections.Generic;
using System.Linq;
using Hazel;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Neutral;
public sealed class Executioner : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Executioner),
            player => new Executioner(player),
            CustomRoles.Executioner,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuTOH + 600,
            SetupOptionItem,
            "エクスキューショナー",
            "#611c3a",
            introSound: () => GetIntroSound(RoleTypes.Shapeshifter)
        );
    public Executioner(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        CanTargetImpostor = OptionCanTargetImpostor.GetBool();
        CanTargetNeutralKiller = OptionCanTargetNeutralKiller.GetBool();
        ChangeRolesAfterTargetKilled = ChangeRoles[OptionChangeRolesAfterTargetKilled.GetValue()];

        Executioners.Add(this);
        CustomRoleManager.OnPlayerDeadOthers.Add(OnPlayerDeadOthers);

        TargetExiled = false;
    }

    private static OptionItem OptionCanTargetImpostor;
    private static OptionItem OptionCanTargetNeutralKiller;
    public static OptionItem OptionChangeRolesAfterTargetKilled;
    enum OptionName
    {
        ExecutionerCanTargetImpostor,
        ExecutionerCanTargetNeutralKiller,
        ExecutionerChangeRolesAfterTargetKilled
    }

    private static bool CanTargetImpostor;
    private static bool CanTargetNeutralKiller;
    public static CustomRoles ChangeRolesAfterTargetKilled;

    public static HashSet<Executioner> Executioners = new(15);
    public byte TargetId;
    private bool TargetExiled;
    public static CustomRoles[] AdditionalRoles => [ChangeRolesAfterTargetKilled];
    public static readonly CustomRoles[] ChangeRoles =
    {
            CustomRoles.Crewmate, CustomRoles.Jester, CustomRoles.Opportunist,
    };

    private static void SetupOptionItem()
    {
        var cRolesString = ChangeRoles.Select(x => x.ToString()).ToArray();
        OptionCanTargetImpostor = BooleanOptionItem.Create(RoleInfo, OptionName.ExecutionerCanTargetImpostor, false, false);
        OptionCanTargetNeutralKiller = BooleanOptionItem.Create(RoleInfo, OptionName.ExecutionerCanTargetNeutralKiller, false, false);
        OptionChangeRolesAfterTargetKilled = StringOptionItem.Create(RoleInfo, OptionName.ExecutionerChangeRolesAfterTargetKilled, cRolesString, 1, false);
    }
    public override bool NeedsSeenNames => true;

    public override void Add()
    {
        //ターゲット割り当て
        if (!AmongUsClient.Instance.AmHost) return;

        var playerId = Player.PlayerId;
        List<PlayerControl> targetList = new();
        var rand = IRandom.Instance;
        foreach (var target in Main.AllPlayerControls)
        {
            if (playerId == target.PlayerId) continue;
            else if (!CanTargetImpostor && target.Is(CustomRoleTypes.Impostor)) continue;
            else if (!CanTargetNeutralKiller && target.IsCountedNeutralKiller()) continue;
            if (target.GetCustomRole().IsSpectator()) continue;

            targetList.Add(target);
        }
        // 候補がいない時は相手なしのまま (例外で役職の配布を止めない)
        if (targetList.Count == 0)
        {
            Logger.Warn($"{Player.GetNameWithRole()}:ターゲットの候補がいない", "Executioner");
            TargetId = byte.MaxValue;
            return;
        }
        var SelectedTarget = targetList[rand.Next(targetList.Count)];
        TargetId = SelectedTarget.PlayerId;
        SendRPC();
        Logger.Info($"{Player.GetNameWithRole()}:{SelectedTarget.GetNameWithRole()}", "Executioner");
    }
    public override void OnDestroy()
    {
        Executioners.Remove(this);

        if (Executioners.Count <= 0)
        {
            CustomRoleManager.OnPlayerDeadOthers.Remove(OnPlayerDeadOthers);
        }
    }
    public void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        using var sender = CreateSender(CustomRPC.SetExecutionerTarget);
        sender.Writer.Write(TargetId);
    }
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        byte targetId = reader.ReadByte();
        TargetId = targetId;
    }
    // 死んだ時に外したターゲット (生き返った時に戻す)
    private byte targetBeforeDeath = byte.MaxValue;
    public override void OnMurderPlayerAsTarget(MurderInfo _)
    {
        if (TargetId != byte.MaxValue) targetBeforeDeath = TargetId;
        TargetId = byte.MaxValue;
        SendRPC();
    }
    /// <summary>生き返ったら、死ぬ前のターゲットがまだ生きていれば狙い直す</summary>
    public override void OnRevived()
    {
        var target = Utils.GetPlayerById(targetBeforeDeath);
        targetBeforeDeath = byte.MaxValue;
        if (TargetId != byte.MaxValue || target == null || !target.IsAlive()) return;
        TargetId = target.PlayerId;
        SendRPC();
    }
    /// <summary>ターゲットが追放以外で死んだら役職が変わる (追放された時は OnExileWrapUp で勝ちになる)</summary>
    public static void OnPlayerDeadOthers(byte playerId, CustomDeathReason _)
    {
        if (playerId == CustomRoleManager.ExileWrapUpPlayerId) return;
        ChangeRoleByTarget(playerId);
    }
    public override string GetMark(PlayerControl seer, PlayerControl seen, bool _ = false)
    {
        //seenが省略の場合seer
        seen ??= seer;

        return TargetId == seen.PlayerId ? Utils.ColorString(RoleInfo.RoleColor, "♦") : "";
    }
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Player?.IsAlive() != true) return;
        if (exiled.PlayerId != TargetId) return;

        TargetExiled = true;

        if (!DecidedWinner)
        {
            if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return; //勝者がいるなら処理をスキップ

            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Executioner);
        }
        CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
    }
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        return TargetExiled && CustomWinnerHolder.WinnerTeam != CustomWinner.Default;
    }
    public void ChangeRole()
    {
        Player.RpcSetCustomRole(ChangeRolesAfterTargetKilled);
        Utils.NotifyRoles();
    }

    public static void ChangeRoleByTarget(byte targetId)
    {
        foreach (var executioner in Executioners.ToArray())
        {
            if (executioner.TargetId != targetId) continue;

            // 同じ相手を引いた処刑人が何人いても、全員変える
            executioner.ChangeRole();
        }
    }
}