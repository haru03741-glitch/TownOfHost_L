using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using TMPro;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Madmate;
using TownOfHostL.Roles.Vanilla;
using UnityEngine;

using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Neutral;

public sealed class Eater : RoleBase, IKiller, IUsePhantomButton, IKillFlashSeeable
{
    // 役職の元: ExtremeRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Eater),
            player => new Eater(player),
            CustomRoles.Eater,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 51200,
            SetupOptionItem,
            "Ea",
            "#662B2C",
            isDesyncImpostor: true,
            countType: CountTypes.Eater,
            flags: RoleFlags.NeutralKiller
        );

    static OptionItem OptionCanUseVent;
    static OptionItem OptionSwallowCooldown;
    static OptionItem OptionEatCooldown;
    static OptionItem OptionWinCount;
    static OptionItem OptionSwallowTime;
    static OptionItem OptionSwallowRange;
    static OptionItem OptionShowArrow;
    static OptionItem OptionHasImpostorVision;

    float EatCooldown => OptionEatCooldown.GetFloat();

    enum OptionName
    {
        EaterVisionRange,
        EaterLinkCooldowns,
        EaterSharedCooldown,
        EaterSwallowCooldown,
        EaterEatCooldown,
        EaterWinCount,
        EaterSwallowTime,
        EaterEatIncreaseRate,
        EaterSwallowRange,
        EaterSwallowCooldownIncreaseRate,
        EaterMeetingReduceRate
    }

    sealed class PendingSwallowInfo
    {
        public byte TargetId;
        public float Elapsed;
        public float Required;
        public PendingSwallowInfo(byte targetId, float required)
        {
            TargetId = targetId;
            Required = required;
            Elapsed = 0f;
        }
    }

    readonly Dictionary<byte, Vector3> deadBodyPositions = new();
    static readonly HashSet<byte> EatenBodies = new();

    int eatOrSwallowCount;
    bool eatMode;
    float eatCooldownTimer;

    PendingSwallowInfo pendingSwallow;
    public Eater(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        eatOrSwallowCount = 0;
        eatMode = true;
        pendingSwallow = null;
        EatenBodies.Clear();
        Viperkilledplayers = new();
    }
    public override void Add()
    {
        eatOrSwallowCount = 0;
    }
    [Attributes.GameModuleInitializer]
    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, defo: 1);

        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, GeneralOption.ImpostorVision, true, false);
        OptionCanUseVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, false, false);
        OptionSwallowCooldown = FloatOptionItem.Create(RoleInfo, OptionName.EaterSwallowCooldown, new(0f, 60f, 1f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionEatCooldown = FloatOptionItem.Create(RoleInfo, OptionName.EaterEatCooldown, new(0f, 60f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionWinCount = IntegerOptionItem.Create(RoleInfo, OptionName.EaterWinCount, new(1, 10, 1), 3, false);
        OptionSwallowTime = FloatOptionItem.Create(RoleInfo, OptionName.EaterSwallowTime, new(0.5f, 60f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSwallowRange = FloatOptionItem.Create(RoleInfo, OptionName.EaterSwallowRange, new(1.25f, 5f, 0.25f), 1.75f, false)
        .SetValueFormat(OptionFormat.Multiplier);
        OptionShowArrow = BooleanOptionItem.Create(RoleInfo, "EaterShowArrow", true, false);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(OptionHasImpostorVision.GetBool());
        AURoleOptions.PhantomCooldown = 0.1f;
    }

    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseImpostorVentButton() => OptionCanUseVent.GetBool();
    public bool CanUseSabotageButton() => false;
    public float CalculateKillCooldown() => OptionSwallowCooldown.GetFloat();
    Dictionary<byte, float> Viperkilledplayers = new();

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;

        if (!Player.IsAlive()) return;
        if (pendingSwallow != null) return;
        eatMode = !eatMode;
    }

    public bool UseOneclickButton => true;
    public bool IsPhantomRole => true;
    public bool IsresetAfterKill => false;

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        info.DoKill = false;
        killer.SetKillCooldown(OptionSwallowTime.GetFloat(), ForceProtect: true);
        if (target.IsAlive() && pendingSwallow == null)
        {
            pendingSwallow = new(target.PlayerId, 0f);
            Utils.NotifyRoles(SpecifySeer: killer);
        }
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        pendingSwallow = null;
        var send = string.Format(GetString("EaterMInfo"), eatOrSwallowCount);
        Utils.SendMessage(send, Player.PlayerId);
        eatOrSwallowCount++;
        deadBodyPositions.Do(oniku => GetArrow.Remove(Player.PlayerId, oniku.Value));
        deadBodyPositions.Clear();

        eatCooldownTimer = 0f;
        RpcClearDiePlayerPos();
    }

    public bool? CheckKillFlash(MurderInfo info)
    {
        var pos = info.AppearanceTarget.GetTruePosition();
        deadBodyPositions.Add(info.AppearanceTarget.PlayerId, pos);
        GetArrow.Add(Player.PlayerId, pos);
        if (info.AppearanceKiller.GetCustomRole() is CustomRoles.Viper) Viperkilledplayers.Add(info.AppearanceTarget.PlayerId, Utils.ViperDissolveTime);
        RpcAddDiePlayerPos(info.AppearanceTarget.PlayerId, pos);

        return OptionShowArrow.GetBool();
    }

    public void RpcAddDiePlayerPos(byte targetId, Vector2 pos)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.WritePacked((int)RPC_Types.AdddeadBodyPositions);
        sender.Writer.Write(targetId);
        NetHelpers.WriteVector2(pos, sender.Writer);
    }
    public void RpcClearDiePlayerPos()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.WritePacked((int)RPC_Types.CleardeadBodyPositions);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        eatCooldownTimer += Time.fixedDeltaTime;
        if (eatCooldownTimer >= EatCooldown)
        {
            eatCooldownTimer = EatCooldown;
        }
        if (!AmongUsClient.Instance.AmHost) return;
        if (GameStates.IsInTask && pendingSwallow != null)
        {
            if (!Player.IsAlive())
            {
                pendingSwallow = null;
                Utils.NotifyRoles(SpecifySeer: Player);
            }
            else
            {
                var ar_target = Utils.GetPlayerById(pendingSwallow.TargetId);
                var ar_time = pendingSwallow.Elapsed;
                if (!ar_target.IsAlive())
                {
                    pendingSwallow = null;
                }
                else if (ar_time >= OptionSwallowTime.GetFloat())
                {
                    var target = Utils.GetPlayerById(pendingSwallow.TargetId);

                    if (!target.IsAlive())
                    {
                        Logger.Info("ターゲットが死亡しています", "Eater");

                        pendingSwallow = null;
                    }
                    Player.SetKillCooldown(ForceProtect: true);
                    ++eatOrSwallowCount;
                    PlayerState.GetByPlayerId(target.PlayerId).DeathReason = CustomDeathReason.Swallowed;
                    target.RpcExileV3();
                    Logger.Info($"食べた数：{eatOrSwallowCount}", "Eater");

                    if (eatOrSwallowCount >= OptionWinCount.GetInt())
                    {
                        Win();
                    }
                    pendingSwallow = null;
                }
                else
                {
                    float dis;
                    dis = Vector2.Distance(Player.transform.position, ar_target.transform.position);
                    if (dis <= OptionSwallowRange.GetFloat())
                    {
                        pendingSwallow.Elapsed += Time.fixedDeltaTime;
                    }
                    else
                    {
                        pendingSwallow = null;
                        Utils.NotifyRoles(SpecifySeer: Player);
                        Player.SetKillCooldown(0.1f, ForceProtect: true);

                        Logger.Info($"Canceled: {Player.GetNameWithRole().RemoveHtmlTags()}", "Arsonist");
                    }
                }
            }
        }
    }

    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportReason reason)
    {
        if (!eatMode || pendingSwallow != null)
        {
            return false;
        }
        if (eatCooldownTimer < EatCooldown)
        {
            return false;
        }
        if (reporter.PlayerId == Player.PlayerId && target != null && !EatenBodies.Contains(target?.PlayerId ?? (byte)250))
        {
            reason = DontReportReason.Eat;
            EatenBodies.Add(target.PlayerId);
            deadBodyPositions.Where(poss => poss.Key == target.PlayerId).Do(poss => GetArrow.Remove(Player.PlayerId, poss.Value));
            RpcEatPlayer(target.PlayerId);
            eatOrSwallowCount++;
            Logger.Info($"食べた数：{eatOrSwallowCount}", "Eater");

            if (eatOrSwallowCount >= OptionWinCount.GetInt())
            {
                Win();
            }
            eatCooldownTimer = 0f;
            return true;
        }
        if (reporter?.PlayerId != target?.PlayerId && target != null && EatenBodies.Contains(target?.PlayerId ?? (byte)250))
        {
            reason = DontReportReason.Eat;
            Logger.Info($"{target?.PlayerName ?? "???"}は食事済みだからキャンセル", "Vulture");
            return true;
        }
        return false;
    }
    public void Win()
    {
        ForceSoloWin();
    }
    public bool CanWinByLastSurvivorRule()
    {
        if (!Player.IsAlive()) return false;
        var alivePlayers = Main.AllAlivePlayerControls.ToList();
        if (alivePlayers.Count != 2) return false;

        var other = alivePlayers.FirstOrDefault(pc => pc.PlayerId != Player.PlayerId);
        if (other == null) return false;

        return IsValidLastOpponent(other);
    }

    public bool TryWinByLastSurvivorRule()
    {
        if (!CanWinByLastSurvivorRule()) return false;
        if (!CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Eater, Player.PlayerId, true)) return false;

        CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
        CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
        return true;
    }

    static bool IsValidLastOpponent(PlayerControl other)
    {
        if (other.Is(CustomRoleTypes.Crewmate)) return true;
        if (!other.Is(CustomRoleTypes.Neutral)) return false;

        if (other.GetRoleClass() is IKiller killer)
            return !killer.CanUseKillButton();

        return true;
    }

    private void ForceSoloWin()
    {
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Eater, Player.PlayerId))
        {
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
        }
    }


    public void RpcEatPlayer(byte targetId)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.WritePacked((int)RPC_Types.EatPlayer);
        sender.Writer.Write(targetId);
    }
    enum RPC_Types
    {
        EatPlayer,
        AdddeadBodyPositions,
        CleardeadBodyPositions
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        switch ((RPC_Types)reader.ReadPackedInt32())
        {
            case RPC_Types.EatPlayer:
                var targetId = reader.ReadByte();
                EatenBodies.Add(targetId);
                deadBodyPositions.Where(poss => poss.Key == targetId).Do(poss => GetArrow.Remove(Player.PlayerId, poss.Value));
                break;
            case RPC_Types.AdddeadBodyPositions:
                var playerPos = NetHelpers.ReadVector2(reader);
                deadBodyPositions.Add(reader.ReadByte(), playerPos);
                GetArrow.Add(Player.PlayerId, playerPos);
                break;
            case RPC_Types.CleardeadBodyPositions:
                deadBodyPositions.Do(oniku => GetArrow.Remove(Player.PlayerId, oniku.Value));
                deadBodyPositions.Clear();
                break;
        }
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        if (isForMeeting) return "";
        seen ??= seer;
        var text = "食らう";
        if (!eatMode || pendingSwallow != null)
        {
            text = "死体通報";
        }
        return $"<color=#662B2C>現在のモード：{text}モード</color>";
    }

    public override string GetProgressText(bool comms = false)
    {
        var CoolDownLeft = Math.Ceiling(EatCooldown - eatCooldownTimer);
        var text = Utils.ColorString(Color.yellow, $"({CoolDownLeft})");
        return $"{text}";
    }

    public override string GetAbilityButtonText() => GetString("Modechenge");

    public bool OverrideKillButtonText(out string text)
    {
        text = GetString("Swallowed");
        return true;
    }


    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        //死亡済み or 会議中 or 他人にマークの場合は空
        if (!Player.IsAlive() || seer.PlayerId != seen.PlayerId) return "";

        var text = "";
        if (OptionShowArrow.GetBool() && !isForMeeting)
        {
            var str = $" <color={RoleInfo.RoleColorCode}>";

            //事前に保存しておいた奴を出す
            foreach (var arrow in deadBodyPositions)
            {
                str += GetArrow.GetArrows(seer, arrow.Value);
            }
            text = $"{str}</color>";
        }
        return text;
    }

}