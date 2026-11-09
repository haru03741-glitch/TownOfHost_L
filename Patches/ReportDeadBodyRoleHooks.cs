using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

partial class ReportDeadBodyPatch
{
    /// <summary>次の通報を確認なしで通す</summary>
    public static bool ForceReport;
    /// <summary>会議の始めに出す、会議の名前</summary>
    static (string info, string color)? meetingInfo;
    static int exReportWaited;
    /// <summary>通報できない理由と残り時間</summary>
    public static Dictionary<byte, (float time, RoleBase.DontReportReason reason)> DontReport = new();

    [Attributes.GameModuleInitializer]
    public static void InitRoleReport()
    {
        ForceReport = false;
        meetingInfo = null;
        exReportWaited = 0;
        DontReport.Clear();
    }

    /// <summary>誰かの役職が通報を取り消すか (RoleBase.CancelReportDeadBody)</summary>
    static bool RoleCancelsReport(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        foreach (var role in CustomRoleManager.AllActiveRoles.Values.ToArray())
        {
            var reason = RoleBase.DontReportReason.None;
            if (role.CancelReportDeadBody(reporter, target, ref reason))
            {
                Logger.Info($"{reporter.GetNameWithRole().RemoveHtmlTags()} の通報を {role.Player.GetNameWithRole().RemoveHtmlTags()} が取り消し ({reason})", "ReportDeadBody");
                DontReport[reporter.PlayerId] = (0f, reason);
                Utils.NotifyRoles(SpecifySeer: reporter);
                _ = new LateTask(() =>
                {
                    DontReport.Remove(reporter.PlayerId);
                    Utils.NotifyRoles(SpecifySeer: reporter);
                }, 5f, "DontReport");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 役職の能力で通報する。役職の能力で会議を開く。
    /// Cancelcheck が false なら、残りボタンや通報の取り消しを見ずに開く。
    /// </summary>
    public static void ExReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target = null, bool? Cancelcheck = true, string Meetinginfo = "", string colorcode = "#000000")
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 追放の画面の途中に会議を始めると暗転のまま進めなくなるので、終わるまで待つ
        if (ExileController.Instance != null)
        {
            if (exReportWaited >= 40) { exReportWaited = 0; Logger.Warn("追放の画面が終わらないので、会議を開くのをやめました", "ExReportDeadBody"); return; }
            exReportWaited++;
            _ = new LateTask(() => ExReportDeadBody(reporter, target, Cancelcheck, Meetinginfo, colorcode), 0.5f, "ExReportDeadBody.WaitExile");
            return;
        }
        exReportWaited = 0;
        if (CustomWinnerHolder.WinnerTeam is not CustomWinner.Default) return;
        if (reporter == null) { Logger.Error("通報者が null", "ExReportDeadBody"); return; }
        Logger.Info($"{reporter.GetNameWithRole().RemoveHtmlTags()} => {target?.Object?.GetNameWithRole()?.RemoveHtmlTags() ?? "null"}", "ExReportDeadBody");
        if (GameStates.IsMeeting || GameStates.IsLobby) return;
        var State = PlayerState.GetByPlayerId(reporter.PlayerId);
        if (State.NumberOfRemainingButtons <= 0 && target is null && Cancelcheck is not false) return;

        GameStates.CalledMeeting = true;
        if (!string.IsNullOrEmpty(Meetinginfo)) meetingInfo = (Meetinginfo, colorcode);
        ForceReport = Cancelcheck is false;
        reporter.ReportDeadBody(target);
        ForceReport = false;
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    class MeetingInfoPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost || meetingInfo is not { } data) return;
            meetingInfo = null;
            _ = new LateTask(() =>
            {
                var title = Translator.GetString(data.info);
                Utils.SendMessage(Utils.ColorString(StringHelper.CodeColor(data.color), title), title: Utils.ColorString(StringHelper.CodeColor(data.color), title));
            }, 3f, "MeetingInfo");
        }
    }
}
