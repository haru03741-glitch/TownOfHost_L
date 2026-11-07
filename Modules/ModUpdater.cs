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
            if (!isChecked)
            {
                CheckRelease(Main.BetaBuildURL.Value != "").GetAwaiter().GetResult();
            }
            MainMenuManagerPatch.UpdateButton.Button.gameObject.SetActive(hasUpdate);
            MainMenuManagerPatch.UpdateButton.Button.transform.Find("FontPlacer/Text_TMP").GetComponent<TMPro.TMP_Text>().SetText($"<size=7><color=#00bfff>TownOfHost</color>_<color=#ffff00>L\n★ UPDATE ★</color>\n[ <color=#73e673>{latestTitle}</color> ]</size>");
        }
        public static async Task<bool> CheckRelease(bool beta = false)
        {
            string url = beta ? Main.BetaBuildURL.Value : URL + "/releases/latest";
            try
            {
                string result;
                // メインメニューの表示中に同期で待つので、GitHub に届かない時に長く止まらないよう短く切る
                using (HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "TownOfHost Updater");
                    using var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseContentRead);
                    if (!response.IsSuccessStatusCode || response.Content == null)
                    {
                        Logger.Error($"ステータスコード: {response.StatusCode}", "CheckRelease");
                        return false;
                    }
                    result = await response.Content.ReadAsStringAsync();
                }
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
                        if (assets[i]["name"].ToString() == "TownOfHost_L-Steam.dll" && Constants.GetPlatformType() == Platforms.StandaloneSteamPC)
                        {
                            downloadUrl = assets[i]["browser_download_url"].ToString();
                            break;
                        }
                        if (assets[i]["name"].ToString() == "TownOfHost_L-Epic.dll" && Constants.GetPlatformType() == Platforms.StandaloneEpicPC)
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
            _ = DownloadDLL(url);
            return;
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
        public static async Task<bool> DownloadDLL(string url)
        {
            try
            {
                using HttpClient client = new();
                using var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    // 途中で切れた時に壊れた DLL が plugins に残らないよう、別名に書き終えてから置き換える
                    string path = $"BepInEx/plugins/TownOfHost_L-v{latestVersion}.dll";
                    string tempPath = path + ".download";
                    using (var content = response.Content)
                    using (var stream = content.ReadAsStream())
                    using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(file);
                    }
                    File.Move(tempPath, path, true);
                    ShowPopup(GetString("updateRestart"), true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ダウンロードに失敗しました。\n{ex}", "DownloadDLL", false);
                try { File.Delete($"BepInEx/plugins/TownOfHost_L-v{latestVersion}.dll.download"); } catch { }
            }
            ShowPopup(GetString("updateManually"), true);
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