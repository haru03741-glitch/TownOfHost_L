using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

partial class ReportDeadBodyPatch
{
    /// <summary>次の通報を確認なしで通す</summary>
    public static bool HamoForceReport;
    /// <summary>会議の始めに出す、会議の名前 (hamo の Meetinginfo)</summary>
    static (string info, string color)? hamoMeetingInfo;
    static int exReportWaited;
    /// <summary>通報できない理由と残り時間 (hamo)</summary>
    public static Dictionary<byte, (float time, HamoRoleBase.DontReportreson reason)> DontReport = new();

    [Attributes.GameModuleInitializer]
    public static void HamoInit()
    {
        HamoForceReport = false;
        hamoMeetingInfo = null;
        exReportWaited = 0;
        DontReport.Clear();
    }

    /// <summary>hamo の役職が通報を取り消すか</summary>
    static bool HamoCancelReport(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        foreach (var role in HamoHooks.AllHamoRoles)
        {
            var reason = HamoRoleBase.DontReportreson.None;
            if (role.CancelReportDeadBody(reporter, target, ref reason))
            {
                Logger.Info($"{reporter.GetNameWithRole().RemoveHtmlTags()} の通報を {role.Player.GetNameWithRole().RemoveHtmlTags()} が取り消し ({reason})", "ReportDeadBody");
                DontReport[reporter.PlayerId] = (0f, reason);
                Utils.NotifyRoles(SpecifySeer: reporter);
                _ = new LateTask(() =>
                {
                    DontReport.Remove(reporter.PlayerId);
                    Utils.NotifyRoles(SpecifySeer: reporter);
                }, 5f, "HamoDontReport");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// hamo の ExReportDeadBody。役職の能力で会議を開く。
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
            _ = new LateTask(() => ExReportDeadBody(reporter, target, Cancelcheck, Meetinginfo, colorcode), 0.5f, "ExReportDeadBody.WaitExile", true);
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
        if (!string.IsNullOrEmpty(Meetinginfo)) hamoMeetingInfo = (Meetinginfo, colorcode);
        HamoForceReport = Cancelcheck is false;
        reporter.ReportDeadBody(target);
        HamoForceReport = false;
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    class HamoMeetingInfoPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost || hamoMeetingInfo is not { } data) return;
            hamoMeetingInfo = null;
            _ = new LateTask(() =>
            {
                var title = Translator.GetString(data.info);
                Utils.SendMessage(Utils.ColorString(StringHelper.CodeColor(data.color), title), title: Utils.ColorString(StringHelper.CodeColor(data.color), title));
            }, 3f, "HamoMeetingInfo", true);
        }
    }
}
