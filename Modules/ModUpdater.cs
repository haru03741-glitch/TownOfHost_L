using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL
{
    [HarmonyPatch]
    public class ModUpdater
    {
        private static readonly string URL = "https://api.github.com/repos/haru03741-glitch/TownOfHost_L";
        public static bool hasUpdate = false;
        public static bool isBroken = false;
        public static bool isChecked = false;
        public static Version latestVersion = null;
        public static string latestTitle = null;
        public static string downloadUrl = null;
        public static GenericPopup InfoPopup;

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.LowerThanNormal)]
        public static void StartPostfix()
        {
            DeleteOldDLL();
            InfoPopup = UnityEngine.Object.Instantiate(Twitch.TwitchManager.Instance.TwitchPopup);
            InfoPopup.name = "InfoPopup";
            InfoPopup.TextAreaTMP.GetComponent<RectTransform>().sizeDelta = new(2.5f, 2f);
            // 問い合わせはメインスレッドで待たず、終わったらメニューの LateUpdate で読んでボタンへ反映する
            if (!isChecked && checkTask == null)
            {
                // 設定と本編の値はメインスレッドで読んでから渡す (問い合わせはスレッドプールで動く)
                checkBetaUrl = Main.BetaBuildURL.Value;
                checkPlatform = Constants.GetPlatformType();
                isChecked = true;
                string url = checkBetaUrl != "" ? checkBetaUrl : URL + "/releases/latest";
                checkTask = Task.Run(() => FetchRelease(url));
            }
            if (checkTask == null) ShowUpdateButton();
        }
        // 裏のスレッドでは文字列を取ってくるだけにする。JSON を読む Newtonsoft は本編の il2cpp の物で、
        // il2cpp に登録されていないスレッドで il2cpp の物を作ると、そこで GC が走った時にゲームが落ちる
        // ("Fatal error in GC: Collecting from unknown thread")。Unity の物・ログ・翻訳もメインスレッドで触る
        private static Task<(string Body, string Error)> checkTask;
        private static string checkBetaUrl;
        private static Platforms checkPlatform;
        private static Task<bool> downloadTask;
        private static string downloadError;

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate)), HarmonyPostfix]
        public static void LateUpdatePostfix()
        {
            if (downloadTask is { IsCompleted: true })
            {
                bool ok = downloadTask.Status == TaskStatus.RanToCompletion && downloadTask.Result;
                downloadTask = null;
                if (!ok) Logger.Error($"ダウンロードに失敗しました。\n{downloadError}", "DownloadDLL", false);
                ShowPopup(GetString(ok ? "updateRestart" : "updateManually"), true);
            }
            if (checkTask is not { IsCompleted: true }) return;
            var (body, error) = checkTask.Status == TaskStatus.RanToCompletion ? checkTask.Result : (null, checkTask.Exception?.ToString());
            checkTask = null;
            CheckRelease(body, error, checkBetaUrl != "", checkPlatform);
            ShowUpdateButton();
        }
        private static void ShowUpdateButton()
        {
            var button = MainMenuManagerPatch.UpdateButton;
            if (Templates.SimpleButton.IsNullOrDestroyed(button)) return;
            button.Button.gameObject.SetActive(hasUpdate);
            button.Button.transform.Find("FontPlacer/Text_TMP").GetComponent<TMPro.TMP_Text>().SetText($"<size=7><color=#00bfff>TownOfHost</color>_<color=#ffff00>L\n★ UPDATE ★</color>\n[ <color=#73e673>{latestTitle}</color> ]</size>");
        }
        // スレッドプールで動く。管理コードだけを使う (il2cpp・Unity・ログ・翻訳は触らない)
        private static async Task<(string Body, string Error)> FetchRelease(string url)
        {
            try
            {
                // GitHub に届かない時に、更新の有無が決まらないまま長く待たないよう短く切る
                using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Add("User-Agent", "TownOfHost Updater");
                using var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content == null) return (null, $"ステータスコード: {response.StatusCode}");
                return (await response.Content.ReadAsStringAsync().ConfigureAwait(false), null);
            }
            catch (Exception ex)
            {
                return (null, ex.ToString());
            }
        }

        // メインスレッドで、取ってきた中身を読む
        private static bool CheckRelease(string result, string error, bool beta, Platforms platform)
        {
            if (result == null)
            {
                isBroken = true;
                Logger.Error($"リリースのチェックに失敗しました。\n{error}", "CheckRelease", false);
                return false;
            }
            try
            {
                JObject data = JObject.Parse(result);
                if (beta)
                {
                    latestTitle = data["name"].ToString();
                    downloadUrl = data["url"].ToString();
                    hasUpdate = latestTitle != ThisAssembly.Git.Commit;
                }
                else
                {
                    latestVersion = new(data["tag_name"]?.ToString().TrimStart('v'));
                    latestTitle = $"v{latestVersion}";
                    JArray assets = data["assets"].Cast<JArray>();
                    for (int i = 0; i < assets.Count; i++)
                    {
                        if (assets[i]["name"].ToString() == "TownOfHost_L-Steam.dll" && platform == Platforms.StandaloneSteamPC)
                        {
                            downloadUrl = assets[i]["browser_download_url"].ToString();
                            break;
                        }
                        if (assets[i]["name"].ToString() == "TownOfHost_L-Epic.dll" && platform == Platforms.StandaloneEpicPC)
                        {
                            downloadUrl = assets[i]["browser_download_url"].ToString();
                            break;
                        }
                        if (assets[i]["name"].ToString() == $"TownOfHost_L-v{latestVersion}.dll")
                            downloadUrl = assets[i]["browser_download_url"].ToString();
                    }
                    hasUpdate = latestVersion.CompareTo(Main.version) > 0;
                }
                if (downloadUrl == null)
                {
                    Logger.Error("ダウンロードURLを取得できませんでした。", "CheckRelease");
                    return false;
                }
                isChecked = true;
                isBroken = false;
            }
            catch (Exception ex)
            {
                isBroken = true;
                Logger.Error($"リリースのチェックに失敗しました。\n{ex}", "CheckRelease", false);
                return false;
            }
            return true;
        }
        public static void StartUpdate(string url)
        {
            ShowPopup(GetString("updatePleaseWait"));
            if (!BackupDLL())
            {
                ShowPopup(GetString("updateManually"), true);
                return;
            }
            // ダウンロードは裏のスレッドで、終わりの知らせはメニューの LateUpdate で出す
            string path = $"BepInEx/plugins/TownOfHost_L-v{latestVersion}.dll";
            downloadTask ??= Task.Run(() => DownloadDLL(url, path));
        }
        public static bool BackupDLL()
        {
            try
            {
                File.Move(Assembly.GetExecutingAssembly().Location, Assembly.GetExecutingAssembly().Location + ".bak");
            }
            catch
            {
                Logger.Error("バックアップに失敗しました", "BackupDLL");
                return false;
            }
            return true;
        }
        public static void DeleteOldDLL()
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "*.bak"))
                {
                    Logger.Info($"{Path.GetFileName(path)}を削除", "DeleteOldDLL");
                    File.Delete(path);
                }
            }
            catch
            {
                Logger.Error("削除に失敗しました", "DeleteOldDLL");
            }
            return;
        }
        // スレッドプールで動く。管理コードだけを使う
        private static async Task<bool> DownloadDLL(string url, string path)
        {
            try
            {
                using HttpClient client = new();
                using var response = await client.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    // 途中で切れた時に壊れた DLL が plugins に残らないよう、別名に書き終えてから置き換える
                    string tempPath = path + ".download";
                    using (var content = response.Content)
                    using (var stream = content.ReadAsStream())
                    using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(file);
                    }
                    File.Move(tempPath, path, true);
                    return true;
                }
                downloadError = $"ステータスコード: {response.StatusCode}";
            }
            catch (Exception ex)
            {
                downloadError = ex.ToString();
                try { File.Delete(path + ".download"); } catch { }
            }
            return false;
        }
        private static void DownloadCallBack(object sender, DownloadProgressChangedEventArgs e)
        {
            ShowPopup($"{GetString("updateInProgress")}\n{e.BytesReceived}/{e.TotalBytesToReceive}({e.ProgressPercentage}%)");
        }
        private static void ShowPopup(string message, bool showButton = false)
        {
            if (InfoPopup != null)
            {
                InfoPopup.Show(message);
                var button = InfoPopup.transform.FindChild("ExitGame");
                if (button != null)
                {
                    button.gameObject.SetActive(showButton);
                    button.GetComponentInChildren<TextTranslatorTMP>().TargetText = StringNames.QuitLabel;
                    button.GetComponent<PassiveButton>().OnClick = new();
                    button.GetComponent<PassiveButton>().OnClick.AddListener((Action)(() =>
                    {
                        Application.OpenURL("https://github.com/haru03741-glitch/TownOfHost_L/releases/latest");
                        Application.Quit();
                    }));
                }
            }
        }
    }
}