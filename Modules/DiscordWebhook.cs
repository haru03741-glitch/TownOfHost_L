using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ホストが自分で入れた Discord の Webhook へ、試合の結果 (勝者・全員の役職) を送る。ゲームログは添付のファイルにする。
/// 送り先はホストの手元の設定 (BepInEx の設定 Other.WebhookURL) だけで、送るかどうかもホストが切り替える (初めは送らない)。
/// 送り先は discord.com の Webhook の URL に限る。送信は別のスレッドで行い、試合の進行を待たせない。
/// </summary>
/// <remarks>試合結果を Discord の Webhook へ送る機能は TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/Debugger.cs Webhook.SendResult・WebhookSettingDialog.cs) を元にした。</remarks>
public static class DiscordWebhook
{
    // Discord の 1 通の本文の上限は 2000 字
    const int MaxContent = 1900;
    static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex ValidUrl = new(@"^https://((ptb|canary)\.)?discord(app)?\.com/api/webhooks/\d+/[\w-]+$", RegexOptions.Compiled);
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static bool IsValidUrl(string url) => !string.IsNullOrWhiteSpace(url) && ValidUrl.IsMatch(url.Trim());
    public static bool HasUrl => IsValidUrl(Main.WebhookURL.Value);
    public static bool Enabled => HasUrl && Main.WebhookSendResult.Value;

    /// <summary>送り先を入れる。正しい形でなければ false</summary>
    public static bool SetUrl(string url)
    {
        url = url?.Trim() ?? "";
        if (!IsValidUrl(url)) return false;
        Main.WebhookURL.Value = url;
        return true;
    }

    /// <summary>/webhook URL・/webhook on|off|test・/webhook (今の状態)</summary>
    public static void Command(ChatCommandContext c)
    {
        void Reply(string text) => HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text);
        var arg = c.Arg(1).Trim();
        switch (arg.ToLowerInvariant())
        {
            case "":
                Reply(Status());
                return;
            case "on":
                if (!HasUrl) { Reply(GetString("Webhook.NoUrl")); return; }
                Main.WebhookSendResult.Value = true;
                Reply(Status());
                return;
            case "off":
                Main.WebhookSendResult.Value = false;
                Reply(Status());
                return;
            case "clear":
                Main.WebhookURL.Value = "none";
                Main.WebhookSendResult.Value = false;
                Reply(Status());
                return;
            case "test":
                if (!HasUrl) { Reply(GetString("Webhook.NoUrl")); return; }
                Post(GetString("Webhook.TestMessage"), null);
                Reply(GetString("Webhook.TestSent"));
                return;
        }
        if (!SetUrl(arg)) { Reply(GetString("Webhook.BadUrl")); return; }
        Main.WebhookSendResult.Value = true;
        Reply(Status());
    }

    static string Status()
        => !HasUrl ? GetString("Webhook.NoUrl")
            : string.Format(GetString("Webhook.Status"), GetString(Main.WebhookSendResult.Value ? "ColoredOn" : "ColoredOff"));

    /// <summary>結果の画面が出た時に呼ぶ (ホストのみ)</summary>
    public static void SendResult()
    {
        if (!Enabled || !AmongUsClient.Instance.AmHost) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append("**").Append(GetString("LastResult")).Append("**  ")
              .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("  ")
              .Append(Main.NormalOptions != null && Main.NormalOptions.MapId < Constants.MapNames.Length ? Constants.MapNames[Main.NormalOptions.MapId] : "").Append('\n');
            sb.Append(Plain(SetEverythingUpPatch.LastWinsText)).Append('\n');
            var others = new List<byte>(PlayerState.AllPlayerStates.Keys);
            foreach (var id in Main.winnerList)
            {
                sb.Append("★ ").Append(Plain(Utils.SummaryTexts(id, true))).Append('\n');
                others.Remove(id);
            }
            foreach (var id in others) sb.Append("・").Append(Plain(Utils.SummaryTexts(id, true))).Append('\n');
            var content = sb.ToString();
            if (content.Length > MaxContent) content = content[..MaxContent] + "…";
            var log = Plain(GameLog.LastText ?? "");
            Post(content, log.Length > 0 ? log : null);
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(DiscordWebhook)); }
    }

    static string Plain(string s) => Tags.Replace(s ?? "", "").Replace("\r", "");

    /// <summary>本文と、あればゲームログのファイルを送る (別のスレッドで)</summary>
    static void Post(string content, string logText)
    {
        var url = Main.WebhookURL.Value.Trim();
        // 送る文の @everyone などで人を呼ばないようにする
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["content"] = content,
            ["username"] = "Town Of Host_L",
            ["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = Array.Empty<string>() },
        });
        Task.Run(async () =>
        {
            try
            {
                using var form = new MultipartFormDataContent { { new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json" } };
                if (logText != null)
                    form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(logText)), "files[0]", "gamelog.txt");
                using var res = await http.PostAsync(url, form);
                if (!res.IsSuccessStatusCode) Logger.Warn($"Webhook の送信に失敗: {(int)res.StatusCode}", nameof(DiscordWebhook));
            }
            catch (Exception ex) { Logger.Warn($"Webhook の送信に失敗: {ex.Message}", nameof(DiscordWebhook)); }
        });
    }
}
