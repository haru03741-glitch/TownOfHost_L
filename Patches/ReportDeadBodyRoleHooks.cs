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
    /// <summary>今の会議の名前 (初手強制会議など)。会議の頭の帯 (通報:〇〇の死体 の所) に出す。会議が終わったら消す</summary>
    public static (string info, string color)? BannerInfo;
    static int exReportWaited;
    /// <summary>通報できない理由と残り時間</summary>
    public static Dictionary<byte, (float time, RoleBase.DontReportReason reason)> DontReport = new();

    [Attributes.GameModuleInitializer]
    public static void InitRoleReport()
    {
        ForceReport = false;
        meetingInfo = null;
        BannerInfo = null;
        exReportWaited = 0;
        DontReport.Clear();
    }

    /// <summary>誰かの役職が通報を取り消すか (RoleBase.CancelReportDeadBody)</summary>
    static bool RoleCancelsReport(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        foreach (var role in CustomRoleManager.AllActiveRoles.Values.ToArray())
        {
            var reason = RoleBase.DontReportReason.None;
            // 1 つの役職の例外で通報の前置きごと止まり、会議が開かなくならないよう、例外は「取り消さない」として扱う
            bool cancels;
            try { cancels = role.CancelReportDeadBody(reporter, target, ref reason); }
            catch (System.Exception ex) { Logger.Exception(ex, "CancelReportDeadBody"); cancels = false; }
            if (cancels)
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
        if (!string.IsNullOrEmpty(Meetinginfo)) meetingInfo = BannerInfo = (Meetinginfo, colorcode);
        ForceReport = Cancelcheck is false;
        reporter.ReportDeadBody(target);
        ForceReport = false;
        // 通報の前置きが捨てた (会議を開くと決めなかった) 時は、立てた旗と見出しを戻す。
        // 残すと次の追放の後まで、会議の前を見る能力が止まったままになり、次の会議に古い見出しが出る。
        // 預かった通報と、自分を対象にする会議 (前置きの判定を通らず本編へ渡す) は後で開くので戻さない
        if (!MeetingStates.MeetingCalled && !GameStates.IsMeeting && !TownOfHostL.Modules.Revive.HasHeldReport
            && target?.PlayerId != reporter.PlayerId)
        {
            GameStates.CalledMeeting = false;
            meetingInfo = null;
            BannerInfo = null;
        }
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
                // 見出しと本文に同じ字を入れると 1 通の中に 2 回出るので、本文だけにする (見出しはふだんのシステムメッセージ)
                Utils.SendMessage(Utils.ColorString(StringHelper.CodeColor(data.color), title));
            }, 3f, "MeetingInfo");
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    class BannerInfoClearPatch
    {
        public static void Postfix() => BannerInfo = null;
    }
}
