using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Crewmate;

public sealed class Analyzer : RoleBase
{
    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Analyzer),
            player => new Analyzer(player),
            CustomRoles.Analyzer,
            () => OptionAwakening.GetBool() ? RoleTypes.Crewmate : RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 30200,
            SetupOptionItem,
            "NC",
            "#9da6ee",
            introSound: () => GetIntroSound(RoleTypes.Crewmate)
        );
    public Analyzer(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Usecount = 0;
        turnusecount = 0;
        ImpVentId = null;
        CrewmateVentId = null;
        NeutralVentId = null;
        sendtype = [];
        Isfall = false;

        Maximum = OptionMaximum.GetInt();
        turnmax = OptionTurnMax.GetInt();
        Cooldown = OptionCooldown.GetFloat();
        Awakened = !OptionAwakening.GetBool() || OptionCanTaskcount.GetInt() < 1;
        cantaskcount = OptionCanTaskcount.GetInt();
        CanUsetoImp = OptionUsetoImpostor.GetBool();
        CanUsetoCrewmate = OptionUsetoCrewmate.GetBool();
        CanUsetoNeutral = OptionUsetoNeutral.GetBool();
    }
    static OptionItem OptionMaximum; static int Maximum;
    static OptionItem OptionTurnMax; static int turnmax;
    static OptionItem OptionCooldown; static float Cooldown;
    static OptionItem OptionAwakening; bool Awakened;
    static OptionItem OptionCanTaskcount; int cantaskcount;
    static OptionItem OptionUsetoImpostor; bool CanUsetoImp;
    static OptionItem OptionUsetoCrewmate; bool CanUsetoCrewmate;
    static OptionItem OptionUsetoNeutral; bool CanUsetoNeutral;

    int Usecount; int turnusecount;
    bool Isfall;

    (int id, Vector3 pos)? ImpVentId; (int id, Vector3 pos)? CrewmateVentId; (int id, Vector3 pos)? NeutralVentId;
    ICollection<CustomRoleTypes> sendtype;
    enum OptionName
    {
        AnalyzerUseto,
        AnalyzerTurnMax
    }
    public override void Add() => ResetVent();

    static void SetupOptionItem()
    {
        OptionMaximum = IntegerOptionItem.Create(RoleInfo, GeneralOption.OptionCount, new(1, 99, 1), 2, false);
        OptionTurnMax = IntegerOptionItem.Create(RoleInfo, OptionName.AnalyzerTurnMax, new(1, 3, 1), 1, false);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0, 180, 0.5f), 30, false).SetValueFormat(OptionFormat.Seconds);
        OptionCanTaskcount = IntegerOptionItem.Create(RoleInfo, GeneralOption.cantaskcount, new(0, 99, 1), 5, false);
        OptionAwakening = BooleanOptionItem.Create(RoleInfo, GeneralOption.AbilityAwakening, false, false);
        // 3 つとも同じ名前 (%roleTypes% で表示が変わる) なので、保存のキーには陣営の名前を使う
        OptionUsetoImpostor = BooleanOptionItem.Create(RoleInfo, OptionName.AnalyzerUseto, true, false).SetKeyName("AnalyzerUsetoImpostor");
        OptionUsetoCrewmate = BooleanOptionItem.Create(RoleInfo, OptionName.AnalyzerUseto, true, false).SetKeyName("AnalyzerUsetoCrewmate");
        OptionUsetoNeutral = BooleanOptionItem.Create(RoleInfo, OptionName.AnalyzerUseto, true, false).SetKeyName("AnalyzerUsetoNeutral");
        OptionUsetoImpostor.ReplacementDictionary = new Dictionary<string, string> { { "%roleTypes%", Utils.GetRoleColorAndName(CustomRoles.Impostor) } };
        OptionUsetoCrewmate.ReplacementDictionary = new Dictionary<string, string> { { "%roleTypes%", Utils.GetRoleColorAndName(CustomRoles.Crewmate) } };
        OptionUsetoNeutral.ReplacementDictionary = new Dictionary<string, string> { { "%roleTypes%", $"<#cccccc>{GetString("Neutral")}</color>" } };
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = Isfall ? 3 : Cooldown;
        AURoleOptions.EngineerInVentMaxTime = 1;
    }
    public override RoleTypes? AfterMeetingRole => !Awakened || Maximum <= Usecount ? RoleTypes.Crewmate : RoleTypes.Engineer;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        var oldfall = Isfall;
        Isfall = false;
        if (Maximum <= Usecount || turnmax <= turnusecount)
            return false;

        bool Used = false;
        if (ImpVentId.HasValue)
        {
            if (ImpVentId.Value.id == ventId)
            {
                Usecount++;
                turnusecount++;
                sendtype.Add(CustomRoleTypes.Impostor);
                GetArrow.Remove(Player.PlayerId, ImpVentId.Value.pos);
                ImpVentId = null;
                Utils.NotifyRoles(SpecifySeer: Player);
                Used = true;
            }
        }
        if (CrewmateVentId.HasValue)
        {
            if (CrewmateVentId.Value.id == ventId)
            {
                Usecount++;
                turnusecount++;
                sendtype.Add(CustomRoleTypes.Crewmate);
                GetArrow.Remove(Player.PlayerId, CrewmateVentId.Value.pos);
                CrewmateVentId = null;
                Utils.NotifyRoles(SpecifySeer: Player);
                Used = true;
            }
        }
        if (NeutralVentId.HasValue)
        {
            if (NeutralVentId.Value.id == ventId)
            {
                Usecount++;
                turnusecount++;
                sendtype.Add(CustomRoleTypes.Neutral);
                GetArrow.Remove(Player.PlayerId, NeutralVentId.Value.pos);
                NeutralVentId = null;
                Utils.NotifyRoles(SpecifySeer: Player);
                Used = true;
            }
        }
        if (Used is false)
        {
            Isfall = true;
        }
        if (oldfall != Isfall)
        {
            Player.RpcResetAbilityCooldown(sync: true);
        }
        return false;
    }
    public override int? UsesRemaining => Maximum - Usecount;
    public override string GetProgressText(bool comms = false)
        => Maximum <= Usecount || turnmax <= turnusecount ? $"<#cccccc>({Maximum - Usecount})</color>" : $"<{RoleInfo.RoleColorCode}>({Maximum - Usecount})</color>";
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seen) || !Player.IsAlive()) return "";
        if (MyTaskState.HasCompletedEnoughCountOfTasks(cantaskcount) is false) return "";
        if (isForMeeting)
        {
            if (sendtype.Count <= 0) return "";
            var impcount = 0;
            var crewcount = 0;
            var neucount = 0;
            Main.AllAlivePlayerControls.Do(pc =>
            {
                var role = pc.GetTellResults(null);
                if (role.IsImpostor()) impcount++;
                if (role.IsCrewmate() || role.IsMadmate()) crewcount++;
                if (role.IsNeutral())
                {
                    if (role is CustomRoles.GrimReaper) return;
                    neucount++;
                }
            });
            if (sendtype.Contains(CustomRoleTypes.Impostor) is false) impcount = -1;
            if (sendtype.Contains(CustomRoleTypes.Crewmate) is false) crewcount = -1;
            if (sendtype.Contains(CustomRoleTypes.Neutral) is false) neucount = -1;

            var mark = "";
            if (0 <= impcount) mark += $"<#ff1919>Ⓘ:{impcount}</color>";
            if (0 <= crewcount) mark += $"<#8cffff>Ⓒ:{crewcount}</color>";
            if (0 <= neucount) mark += $"<#cccccc>Ⓝ:{neucount}</color>";
            return mark;
        }
        var text = "";
        if (ImpVentId.HasValue) text += $"<#ff1919>Ⓘ{GetArrow.GetArrows(Player, ImpVentId.Value.pos)}</color> ";
        if (CrewmateVentId.HasValue) text += $"<#8cffff>Ⓒ{GetArrow.GetArrows(Player, CrewmateVentId.Value.pos)}</color> ";
        if (NeutralVentId.HasValue) text += $"<#cccccc>Ⓝ{GetArrow.GetArrows(Player, NeutralVentId.Value.pos)}</color> ";

        if (text != "") text = $"<size=60%>{GetString("Analyzer_Ability")}</size>({text})";
        return text;
    }
    public override void AfterMeetingTasks()
    {
        sendtype = [];
        turnusecount = 0;
        ResetVent();
    }
    public override void OnStartMeeting()
    {
        if (Player.IsAlive() is false || sendtype.Count <= 0) return;
        var send = "";

        var impcount = 0;
        var crewcount = 0;
        var neucount = 0;
        Main.AllAlivePlayerControls.Do(pc =>
        {
            var role = pc.GetTellResults(null);
            if (role.IsImpostor()) impcount++;
            if (role.IsCrewmate() || role.IsMadmate()) crewcount++;
            if (role.IsNeutral())
            {
                if (role is CustomRoles.GrimReaper) return;
                neucount++;
            }
        });

        if (sendtype.Contains(CustomRoleTypes.Impostor) is false) impcount = -1;
        if (sendtype.Contains(CustomRoleTypes.Crewmate) is false) crewcount = -1;
        if (sendtype.Contains(CustomRoleTypes.Neutral) is false) neucount = -1;

        if (0 <= impcount) send += string.Format(GetString("Analyzer_MeetingText"), Utils.GetRoleColorAndName(CustomRoles.Impostor), impcount);
        if (0 <= crewcount) send += (send == "" ? "" : "\n") + string.Format(GetString("Analyzer_MeetingText"), Utils.GetRoleColorAndName(CustomRoles.Crewmate), crewcount);
        if (0 <= neucount) send += (send == "" ? "" : "\n") + string.Format(GetString("Analyzer_MeetingText"), $"<#cccccc>{GetString("Neutral")}</color>", neucount);

        if (send == "") return;

        // 会議の始めの知らせに積むだけにする (5 秒後にも直接送ると、同じ文が 2 回出る)
        RoleHooks.MeetingStartMessages.Add((Player.PlayerId, send, $"<{RoleInfo.RoleColorCode}>{GetString("Analyzer_MeetingTitle")}</color>"));
    }
    void ResetVent()
    {
        Isfall = false;
        if (ImpVentId.HasValue)
        {
            GetArrow.Remove(Player.PlayerId, ImpVentId.Value.pos);
        }
        if (CrewmateVentId.HasValue)
        {
            GetArrow.Remove(Player.PlayerId, CrewmateVentId.Value.pos);
        }
        if (NeutralVentId.HasValue)
        {
            GetArrow.Remove(Player.PlayerId, NeutralVentId.Value.pos);
        }
        ImpVentId = null;
        CrewmateVentId = null;
        NeutralVentId = null;

        if (Player.IsAlive() is false || !AmongUsClient.Instance.AmHost) return;

        List<Vent> allvent = new();
        ShipStatus.Instance.AllVents.Do(vent => allvent.Add(vent));

        if (CanUsetoImp)
        {
            var impvent = allvent[IRandom.Instance.Next(allvent.Count)];

            allvent.Remove(impvent);
            ImpVentId = (impvent.Id, impvent.transform.position);
            GetArrow.Add(Player.PlayerId, impvent.transform.position);
        }
        if (CanUsetoCrewmate)
        {
            var crewvent = allvent[IRandom.Instance.Next(allvent.Count)];

            allvent.Remove(crewvent);
            CrewmateVentId = (crewvent.Id, crewvent.transform.position);
            GetArrow.Add(Player.PlayerId, crewvent.transform.position);
        }
        if (CanUsetoNeutral)
        {
            var Neutralvent = allvent[IRandom.Instance.Next(allvent.Count)];

            allvent.Remove(Neutralvent);
            NeutralVentId = (Neutralvent.Id, Neutralvent.transform.position);
            GetArrow.Add(Player.PlayerId, Neutralvent.transform.position);
        }
    }
    // 覚醒するまでは、自分にもクルーメイトに見える
    public override CustomRoles? DisplayedRole => Awakened ? null : CustomRoles.Crewmate;
    public override bool OnCompleteTask()
    {
        if (MyTaskState.HasCompletedEnoughCountOfTasks(cantaskcount))
        {
            if (!Awakened)
            {
                // 次の会議で、本当の役職の説明を送る
                Main.ShowRoleInfoAtMeeting.Add(Player.PlayerId);
                if (Player.IsAlive())
                    Player.RpcSetRoleDesync(RoleTypes.Engineer, Player.GetClientId());
                Awakened = true;
                // MOD を入れた本人へ、見せる役職が変わったことを送り直す
                Modules.Net.ModClientSync.MarkRolesDirty();
            }
            Awakened = true;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
        return true;
    }
}
