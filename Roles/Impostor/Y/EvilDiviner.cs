using AmongUs.GameOptions;
using System.Collections.Generic;
using Hazel;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class EvilDiviner : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(EvilDiviner),
            player => new EvilDiviner(player),
            CustomRoles.EvilDiviner,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 600,
            SetupOptionItem,
            "イビルディバイナー"
        );
    public EvilDiviner(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        DivinationMaxCount = OptionDivinationMaxCount.GetInt();
    }
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionDivinationMaxCount;
    enum OptionName
    {
        EvilDivinerDivinationMaxCount,
    }
    private static float KillCooldown;
    private static int DivinationMaxCount;

    // 1 人ごとに持つ (複数人いる時に回数と占った相手が混ざらないように)
    int divinationCount;
    readonly HashSet<byte> divinationTargets = new();

    public static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDivinationMaxCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.EvilDivinerDivinationMaxCount, new(1, 15, 1), 5, false)
            .SetValueFormat(OptionFormat.Times);
    }
    public override void Add()
    {
        divinationCount = DivinationMaxCount;
        Player.AddDoubleTrigger();
    }
    private void SendRPC(byte playerId, byte targetId)
    {
        using var sender = CreateSender(CustomRPC.SetEvilDiviner);
        sender.Writer.Write(playerId);
        sender.Writer.Write(targetId);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.SetEvilDiviner) return;

        reader.ReadByte();
        divinationTargets.Add(reader.ReadByte());
    }

    public float CalculateKillCooldown() => KillCooldown;
    public override string GetProgressText(bool comms = false) => Utils.ColorString(divinationCount > 0 ? Palette.ImpostorRed : Color.gray, $"[{divinationCount}]");

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (divinationTargets.Contains(seen.PlayerId) && Player.IsAlive())
            enabled = true;
    }

    public bool OnCheckMurder(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;
        if (divinationCount > 0)
        {
            return killer.CheckDoubleTrigger(target, () => { SetDivination(killer, target); });
        }
        else return true;
    }
    void SetDivination(PlayerControl killer, PlayerControl target)
    {
        if (divinationTargets.Add(target.PlayerId))
        {
            divinationCount--;
            Logger.Info($"{killer.GetNameWithRole()}：占った 占い先→{target.GetNameWithRole()} || 残り{divinationCount}回", "EvilDiviner");
            Utils.NotifyRoles(SpecifySeer: killer);

            //キルクールの適正化
            killer.SetKillCooldown();
        }
    }
}