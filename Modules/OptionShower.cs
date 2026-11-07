using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using TownOfHostL.Roles;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

using TownOfHostL.Core.Modes;

namespace TownOfHostL
{
    public static class OptionShower
    {
        public static int currentPage = 0;
        public static List<string> pages = new();
        static OptionShower()
        {

        }
        // 全設定を文字列に組み立て直すのは重い (役職を多く有効にすると 1 回 3ms・1MB 前後) ので、毎フレームは行わない。
        // 0.5 秒ごとに「表示の元になる値」の要約 (全オプションの値・本編の設定文・言語・人数・ホストか) だけを調べ、
        // 変わった時だけ組み立て直す。ページ切り替え (Tab / 数字キー) はすぐ反映する。
        static float nextCheckAt;
        static int builtPage = -1;
        static int builtSignature;
        static bool built;
        static string cachedText = "";
        public static string GetText()
        {
            float now = Time.unscaledTime;
            if (built && builtPage == currentPage && now < nextCheckAt) return cachedText;
            if (now >= nextCheckAt || !built)
            {
                nextCheckAt = now + 0.5f;
                int playerCount = GameData.Instance ? GameData.Instance.PlayerCount : 10;
                string hudText = GameOptionsManager.Instance.CurrentGameOptions.ToHudString(playerCount);
                int signature = Signature(hudText);
                if (!built || signature != builtSignature)
                {
                    BuildPages(hudText);
                    builtSignature = signature;
                    built = true;
                    builtPage = -1;
                }
            }
            if (builtPage != currentPage)
            {
                string text = ComposePage();
                builtPage = currentPage;
                if (!string.Equals(text, cachedText, System.StringComparison.Ordinal)) cachedText = text;
            }
            return cachedText;
        }
        static int Signature(string hudText)
        {
            var h = new System.HashCode();
            h.Add(hudText);
            h.Add(TranslationController.Instance.currentLanguage.languageID);
            h.Add(Main.ForceJapanese.Value);
            h.Add(Main.JapaneseRoleName.Value);
            h.Add(AmongUsClient.Instance.AmHost);
            h.Add(OptionItem.CurrentPreset);
            var all = OptionItem.AllOptions;
            for (int i = 0; i < all.Count; i++) h.Add(all[i].GetValue());
            return h.ToHashCode();
        }
        static string ComposePage()
        {
            if (currentPage >= pages.Count) currentPage = pages.Count - 1; //現在のページが最大ページ数を超えていれば最後のページに修正
            return $"{pages[currentPage]}{GetString("PressTabToNextPage")}({currentPage + 1}/{pages.Count})";
        }
        static void BuildPages(string hudText)
        {
            //初期化
            StringBuilder sb = new();
            pages = new()
            {
                //1ページに基本ゲーム設定を格納
                hudText + "\n\n"
            };
            //ゲームモードの表示
            sb.Append($"{Options.GameMode.GetName()}: {Options.GameMode.GetString()}\n\n");
            sb.AppendFormat("{0}: {1}\n\n", RoleAssignManager.OptionAssignMode.GetName(), RoleAssignManager.OptionAssignMode.GetString());
            if (Options.HideGameSettings.GetBool() && !AmongUsClient.Instance.AmHost)
            {
                sb.Append($"<color=#ff0000>{GetString("Message.HideGameSettings")}</color>");
            }
            else
            {
                /*if (Options.IsONMode)
                {
                    //有効な役職一覧
                    if (Options.EnableGM.GetBool())
                        sb.Append($"<color={Utils.GetRoleColorCode(CustomRoles.GM)}>{Utils.GetRoleName(CustomRoles.GM)}:</color> {Options.EnableGM.GetString()}\n\n");
                    sb.Append(GetString("ActiveRolesList")).Append("\n");
                    foreach (var kvp in Options.CustomRoleSpawnChances)
                        if (kvp.Value.GameMode is CustomGameMode.OneNight or CustomGameMode.All && kvp.Value.GetBool()) //ONか全てのゲームモードで表示する役職
                            sb.Append($"{Utils.ColorString(Utils.GetRoleColor(kvp.Key), Utils.GetRoleName(kvp.Key))}: {kvp.Value.GetString()}×{kvp.Key.GetCount()}\n");
                    pages.Add(sb.ToString() + "\n\n");
                    sb.Clear();
                }
                else */
                if (ModeCatalog.Current.UsesStandardRoles)
                {
                    //有効な役職一覧
                    if (Options.EnableGM.GetBool())
                        sb.Append($"<color={Utils.GetRoleColorCode(CustomRoles.GM)}>{Utils.GetRoleName(CustomRoles.GM)}:</color> {Options.EnableGM.GetString()}\n\n");
                    sb.Append(GetString("ActiveRolesList")).Append('\n');
                    foreach (var kvp in Options.CustomRoleSpawnChances)
                        if (kvp.Value.IsShown() && kvp.Value.GetBool()) //スタンダードか全てのゲームモードで表示する役職
                        {
                            var role = kvp.Key;
                            // 陣営ごとのマーク
                            if (role.IsAddOn() || role.IsOtherAddOn())
                                sb.Append("<size=85%><color=#ee82ee>○</color></size>");
                            else if (role.GetCustomRoleTypes() == CustomRoleTypes.Unit) sb.Append("<color=#7fff00>Ⓤ</color>");
                            else sb.Append(Utils.GetTeamMark(role, 85));

                            sb.Append($"{Utils.ColorString(Utils.GetRoleColor(role), Utils.GetRoleName(role))}：{kvp.Value.GetString()}×{role.GetCount()}");
                            if (role.IsPairRole()) sb.Append(GetString("Pair"));
                            sb.Append('\n');
                        }
                    pages.Add(sb.ToString() + "\n\n");
                    sb.Clear();
                }
                //有効な役職と詳細設定一覧
                pages.Add("");
                if (RoleAssignManager.OptionAssignMode.GetBool())
                {
                    ShowChildren(RoleAssignManager.OptionAssignMode, ref sb, Color.white);
                    sb.Append('\n');
                }
                nameAndValue(Options.EnableGM);
                if (!ModeCatalog.IsCatchCat)
                {
                    foreach (var kvp in Options.CustomRoleSpawnChances)
                    {
                        if (!kvp.Key.IsEnable() || !kvp.Value.IsShown()) continue;
                        sb.Append('\n');
                        sb.Append($"{Utils.ColorString(Utils.GetRoleColor(kvp.Key), Utils.GetRoleName(kvp.Key))}: {kvp.Value.GetString()}×{kvp.Key.GetCount()}");
                        if (kvp.Key.IsPairRole()) sb.Append(GetString("Pair"));
                        sb.Append('\n');
                        ShowChildren(kvp.Value, ref sb, Utils.GetRoleColor(kvp.Key).ShadeColor(-0.5f), 1);
                        string rule = Utils.ColorString(Palette.ImpostorRed.ShadeColor(-0.5f), "┣ ");
                        string ruleFooter = Utils.ColorString(Palette.ImpostorRed.ShadeColor(-0.5f), "┗ ");
                        if (kvp.Key.IsMadmate()) //マッドメイトの時に追加する詳細設定
                        {
                            //sb.Append($"{rule}{Options.MadmateCanFixLightsOut.GetName()}: {Options.MadmateCanFixLightsOut.GetString()}\n");
                            //sb.Append($"{rule}{Options.MadmateCanFixComms.GetName()}: {Options.MadmateCanFixComms.GetString()}\n");
                            //sb.Append($"{rule}{Options.MadmateHasImpostorVision.GetName()}: {Options.MadmateHasImpostorVision.GetString()}\n");
                            //sb.Append($"{rule}{Options.MadmateCanSeeKillFlash.GetName()}: {Options.MadmateCanSeeKillFlash.GetString()}\n");
                            //sb.Append($"{rule}{Options.MadmateCanSeeOtherVotes.GetName()}: {Options.MadmateCanSeeOtherVotes.GetString()}\n");
                            //sb.Append($"{rule}{Options.MadmateCanSeeDeathReason.GetName()}: {Options.MadmateCanSeeDeathReason.GetString()}\n");
                            //sb.Append($"{rule}{Options.MadmateRevengeCrewmate.GetName()}: {Options.MadmateRevengeCrewmate.GetString()}\n");
                            sb.Append($"{rule}{Options.MadmateVentCooldown.GetName()}: {Options.MadmateVentCooldown.GetString()}\n");
                            sb.Append($"{ruleFooter}{Options.MadmateVentMaxTime.GetName()}: {Options.MadmateVentMaxTime.GetString()}\n");
                        }
                        if (kvp.Key.CanMakeMadmate()) //シェイプシフター役職の時に追加する詳細設定
                        {
                            sb.Append($"{ruleFooter}{Options.CanMakeMadmateCount.GetName()}: {Options.CanMakeMadmateCount.GetString()}\n");
                        }
                    }
                }
                foreach (var opt in OptionItem.AllOptions.Where(x => x.IsFeatureOption && x.IsShown() && x.Parent == null))
                {
                    if (opt.IsText) sb.Append('\n');
                    else
                    {
                        if (opt.IsHeader) sb.Append('\n');
                        sb.Append($"{opt.GetName()}: {opt.GetString()}\n");
                        if (opt.GetBool())
                            ShowChildren(opt, ref sb, Color.white, 1);
                    }
                }
                //Onの時に子要素まで表示するメソッド
                void nameAndValue(OptionItem o) => sb.Append($"{o.GetName()}: {o.GetString()}\n");
            }
            //1ページにつき35行までにする処理
            // (最後のページを文字列の足し算で伸ばして毎回数え直すと、役職が多い時に二乗で重くなるので、行数を持ち回る)
            var tmp = sb.ToString().Split("\n\n");
            var cur = new StringBuilder(pages[^1]);
            int curLines = CountNewLines(pages[^1]);
            for (var i = 0; i < tmp.Length; i++)
            {
                int chunkLines = CountNewLines(tmp[i]);
                if (curLines + 1 + chunkLines + 1 > 35)
                {
                    pages[^1] = cur.ToString();
                    pages.Add("");
                    cur.Clear();
                    curLines = 0;
                }
                cur.Append(tmp[i]).Append("\n\n");
                curLines += chunkLines + 2;
            }
            pages[^1] = cur.ToString();
        }
        static int CountNewLines(string s)
        {
            int n = 0;
            foreach (char c in s) if (c == '\n') n++;
            return n;
        }
        public static void Next()
        {
            currentPage++;
            if (currentPage >= pages.Count) currentPage = 0; //現在のページが最大ページを超えていれば最初のページに
        }
        private static void ShowChildren(OptionItem option, ref StringBuilder sb, Color color, int deep = 0)
        {
            foreach (var opt in option.Children.Select((v, i) => new { Value = v, Index = i + 1 }))
            {
                if (opt.Value.Name == "Maximum") continue; //Maximumの項目は飛ばす
                if (deep > 0)
                {
                    sb.Append(string.Concat(Enumerable.Repeat(Utils.ColorString(color, "┃"), deep - 1)));
                    sb.Append(Utils.ColorString(color, opt.Index == option.Children.Count ? "┗ " : "┣ "));
                }
                sb.Append($"{opt.Value.GetName()}: {opt.Value.GetString()}\n");
                if (opt.Value.GetBool()) ShowChildren(opt.Value, ref sb, color, deep + 1);
            }
        }
    }
}