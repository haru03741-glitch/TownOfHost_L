using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHostL.Roles.Neutral;

public sealed class Obolus : RoleBase, ILNKiller, IAdditionalWinner
{
    // 役職の元: Nebula on the Ship (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Obolus),
            player => new Obolus(player),
            CustomRoles.Obolus,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 553000,
            SetupOptionItem,
            "ob",
            "#bd871a",
            isDesyncImpostor: true,
            flags: RoleFlags.NeutralKiller
        );
    public Obolus(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        CanVent = OptionCanVent.GetBool();
        KillCount = OptionCanKillCount.GetInt();
        WinFlag = false;
        Daycount = 1;
        NeedCount = OptionNeedKillCount.GetInt();
        KilledOnThisTurn = false;
    }
    static OptionItem OptionKillCooldown;
    static OptionItem OptionAddWin;
    public static OptionItem OptionCanVent;
    static OptionItem OptionKillLockTurn;
    static OptionItem OptionCanKillCount;
    static OptionItem OptionHasImpostorVision;
    static OptionItem OptionCanWinOtherKiller;
    static OptionItem OptionNeedKillCount;
    enum OptionName
    {
        CountKillerAddWin,
        ObolusKillLockTurn,
        WolfBoyShotLimit,
        ObolusCanWinOtherKiller,
        ObolusCanKill,
        ObolusNeedKillCont,
    }
    public static bool CanVent;
    private static float KillCooldown;
    int KillCount;
    bool WinFlag;
    int Daycount;
    int NeedCount;
    bool KilledOnThisTurn;
    bool IKiller.CanKill => !KilledOnThisTurn;

    // 追加: ターゲット役職ごとの「このキルで勝利可能」トグル
    public static Dictionary<CustomRoles, OptionItem> ExtraWinKillTargetOptions = new();

    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, defo: 1);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionKillLockTurn = IntegerOptionItem.Create(RoleInfo, OptionName.ObolusKillLockTurn, new(0, 99, 1), 2, false)
            .SetValueFormat(OptionFormat.day);
        OptionCanKillCount = IntegerOptionItem.Create(RoleInfo, OptionName.WolfBoyShotLimit, new(1, 14, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionNeedKillCount = IntegerOptionItem.Create(RoleInfo, OptionName.ObolusNeedKillCont, new(1, 14, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, GeneralOption.ImpostorVision, true, false);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, true, false);
        OptionAddWin = BooleanOptionItem.Create(RoleInfo, OptionName.CountKillerAddWin, false, false);
        OptionCanWinOtherKiller = BooleanOptionItem.Create(RoleInfo, OptionName.ObolusCanWinOtherKiller, false, false);
        SetUpExtraWinKillTargetOptions();
        RoleAddAddons.Create(RoleInfo);
    }

    // 追加: 役職ごとの勝利対象トグルをまとめて生成
    private static void SetUpExtraWinKillTargetOptions()
    {
        foreach (var role in CustomRolesHelper.AllStandardRoles.Where(x => x.IsNeutralKiller() && x != CustomRoles.Obolus))
        {
            

            var roleName = Utils.GetRoleName(role);
            var replacementDic = new Dictionary<string, string>
        {
            { "%role%", Utils.ColorString(Utils.GetRoleColor(role), roleName) }
        };

            var item = BooleanOptionItem
                .Create(OptionName.ObolusCanKill + "%role%", false, RoleInfo.Tab, false)
                .SetParent(OptionCanWinOtherKiller)
                .SetParentRole(CustomRoles.Obolus)
                .SetKeyName($"{OptionName.ObolusCanKill}{role}");
            item.ReplacementDictionary = replacementDic;

            ExtraWinKillTargetOptions[role] = item;
        }
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(OptionHasImpostorVision.GetBool());
    }
    public float CalculateKillCooldown() => KillCooldown;
    public override void Add()
    {
        KillCount = OptionCanKillCount.GetInt();
        Daycount = 1;
        WinFlag = false;
    }
    private void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(KillCount);
        sender.Writer.Write(WinFlag);
        sender.Writer.Write(Daycount);
        sender.Writer.Write(NeedCount);
        sender.Writer.Write(KilledOnThisTurn);
    }
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        KillCount = reader.ReadInt32();
        WinFlag = reader.ReadBoolean();
        Daycount = reader.ReadInt32();
        NeedCount = reader.ReadInt32();
        KilledOnThisTurn = reader.ReadBoolean();
    }
    public bool CanUseKillButton() => KillCount > 0 && Daycount > OptionKillLockTurn.GetInt() && !KilledOnThisTurn;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => CanVent;
    public bool IstargetRole(CustomRoles role)
    {
        if (ExtraWinKillTargetOptions.TryGetValue(role, out var option) && option.GetBool() && OptionCanWinOtherKiller.GetBool())
        {
            return true;
        }
        return role.GetCustomRoleTypes() is CustomRoleTypes.Impostor;
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (Daycount <= OptionKillLockTurn.GetInt())
        {
            info.DoKill = false;
            return;
        }
        if (KilledOnThisTurn is true)
        {
            info.DoKill = false;
            return;
        }
        (var killer, var target) = info.AttemptTuple;
        KilledOnThisTurn = true;
        --KillCount;
        SendRPC();
        if (IstargetRole(target.GetCustomRole()))
        {
            --NeedCount;
            if (NeedCount <= 0)
            {
                ForceSoloWin();
            }
        }
        _ = new LateTask(() =>
        {
            if (target.IsAlive())
            {
                //脳筋だけどこれで（）
                KilledOnThisTurn = false;
                SendRPC();
            }
        }, Main.LagTime, "");
    }
    private void ForceSoloWin()
    {
        WinFlag = true;

        if (OptionAddWin.GetBool()) return;
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.CountKiller, Player.PlayerId))
        {
            CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
        }
    }
    public override void AfterMeetingTasks()
    {
        ++Daycount;
        KilledOnThisTurn = false;
        SendRPC();
    }
    public override string GetProgressText(bool comms = false)
    {
        var text = Utils.ColorString(KillCount > 0 ? Color.yellow : Color.gray, $"({KillCount})");
        // キルを封印している間は、あと何日でキルできるかを出す(出ていないと、なぜキルできないのか分からなかった)
        var lockLeft = OptionKillLockTurn.GetInt() - Daycount + 1;
        if (lockLeft > 0) text += Utils.ColorString(Color.gray, $" 封印 あと{lockLeft}日");
        return text;
    }
    public bool CheckWin(ref CustomRoles winnerRole) => OptionAddWin.GetBool() && WinFlag;
}