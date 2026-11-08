using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using AmongUs.GameOptions;

namespace TownOfHostL.Modules;

public static class OptionSerializer
{
    private static LogHandler logger = Logger.Handler(nameof(OptionSerializer));
    // 中身の形が数値 ID の差の並びだった頃の文字列 (%TOHOptions%) とは別の見出しにして、取り違えて読まないようにする
    private const string Header = "%TOHLOptions%", Footer = "%End%";
    private static readonly DirectoryInfo exportDir = new("./TOHL_DATA/OptionOutputs");
    public static void SaveToClipboard()
    {
        GUIUtility.systemCopyBuffer = GenerateOptionsString();
        Logger.SendInGame(Utils.ColorString(Color.green, Translator.GetString("Message.CopiedOptions")));
    }
    public static void SaveToFile()
    {
        if (!exportDir.Exists)
        {
            exportDir.Create();
        }
        var output = $"{exportDir.FullName}/Preset{OptionItem.CurrentPreset}_{DateTime.Now.Ticks}.txt";
        File.WriteAllText(output, GenerateOptionsString());
        Utils.OpenDirectory(exportDir.FullName);
        Logger.SendInGame(Utils.ColorString(Color.green, Translator.GetString("Message.ExportedOptions")));
    }
    public static void LoadFromClipboard()
    {
        LoadOptionsString(GUIUtility.systemCopyBuffer);
    }
    /// <summary>
    /// <see cref="GenerateModOptionsString"/>と<see cref="GenerateVanillaOptionsString"/>を合成し，<see cref="LoadOptionsString"/>で読み込める文字列データを生成します<br/>
    /// <see cref="Header"/>から始まって<see cref="Footer"/>で終わります<br/>
    /// '&amp;'がMod設定とバニラ設定を区切ります
    /// </summary>
    /// <returns>生成された文字列</returns>
    public static string GenerateOptionsString()
    {
        var builder = new StringBuilder(Header, 1024);
        builder.Append(GenerateModOptionsString());
        builder.Append('&');
        builder.Append(GenerateVanillaOptionsString());
        builder.Append(Footer);
        return builder.ToString();
    }
    /// <summary>
    /// <see cref="LoadModOptionsString"/>で読み込める、現在のプリセットの Mod 設定の文字列を作ります<br/>
    /// 初期値と違う設定だけを「キー=値」で並べ、';' で区切ります (キーは <see cref="OptionItem.Key"/>)<br/>
    /// キーで指すので、設定が足されたり並びが変わったりした版の間でも、同じキーの設定はそのまま読めます
    /// </summary>
    /// <returns>生成された文字列</returns>
    public static string GenerateModOptionsString()
    {
        var builder = new StringBuilder(4096);
        foreach (var option in OptionItem.AllOptions)
        {
            if (option is PresetOptionItem) continue;
            var value = option.GetValue();
            if (value == option.DefaultValue) continue;
            var key = option.Key;
            if (key.IndexOfAny(Reserved) >= 0 || key.Contains(Footer))
            {
                logger.Warn($"区切りの文字を含むキーなので書き出しません: {key}");
                continue;
            }
            builder.Append(key).Append(KeyValueSeparator).Append(value).Append(EntrySeparator);
        }
        return builder.ToString();
    }
    private const char EntrySeparator = ';', KeyValueSeparator = '=';
    private static readonly char[] Reserved = { EntrySeparator, KeyValueSeparator, '&' };
    /// <summary>
    /// <see cref="LoadVanillaOptionsString"/>で読み込める現在のバニラ設定の文字列データを生成します<br/>
    /// <see cref="GameOptionsFactory.ToBytes"/>で生成されたバイト列を<see cref="Convert.ToBase64String"/>でBase64文字列に変換します
    /// </summary>
    /// <returns>生成された文字列</returns>
    public static string GenerateVanillaOptionsString()
    {
        // 保存時はエイプリルフール無効
        byte[] bytes = GameOptionsManager.Instance.gameOptionsFactory.ToBytes(GameOptionsManager.Instance.CurrentGameOptions, false);
        return Convert.ToBase64String(bytes);
    }
    /// <summary>
    /// <see cref="GenerateOptionsString"/>で生成された形式の文字列を読み込んで現在のプリセットを上書きします
    /// </summary>
    /// <param name="source">オプション情報の文字列</param>
    public static void LoadOptionsString(string source)
    {
        if (!AmongUsClient.Instance.AmHost)
        {
            Logger.SendInGame(Translator.GetString("Message.OnlyHostCanLoadOptions"));
            return;
        }

        if (string.IsNullOrEmpty(source))
        {
            logger.Info("文字列が空");
            goto Failed;
        }

        var headerAt = source.IndexOf(Header);
        if (headerAt < 0)
        {
            logger.Info("ヘッダがありません");
            goto Failed;
        }
        var footerAt = source.IndexOf(Footer, headerAt);
        if (footerAt < 0)
        {
            logger.Info("フッタがありません");
            goto Failed;
        }
        // ヘッダ以前とフッタ以降を削除
        source = source[(headerAt + Header.Length)..footerAt];

        try
        {
            var entries = source.Split('&');
            LoadModOptionsString(entries[0]);
            LoadVanillaOptionsString(entries[1]);
            Logger.SendInGame(Utils.ColorString(Color.green, Translator.GetString("Message.LoadedOptions")));
        }
        catch (Exception ex)
        {
            logger.Exception(ex);
            goto Failed;
        }
        return;

    Failed:
        Logger.SendInGame(Translator.GetString("Message.FailedToLoadOptions"));
    }
    /// <summary>
    /// <see cref="GenerateModOptionsString"/>で生成された形式の文字列を読み込んで現在のプリセットを上書きします<br/>
    /// 書かれていない設定は初期値に戻します。今の版に無いキーは読み飛ばします
    /// </summary>
    /// <param name="source">オプション情報の文字列</param>
    public static void LoadModOptionsString(string source)
    {
        var entries = source.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries);
        var parsed = new Dictionary<OptionItem, int>(entries.Length);
        var unknown = 0;
        foreach (var entry in entries)
        {
            var at = entry.LastIndexOf(KeyValueSeparator);
            if (at <= 0 || !int.TryParse(entry[(at + 1)..], out var value))
            {
                logger.Info($"読めない項目を飛ばします: {entry}");
                continue;
            }
            if (OptionItem.TryGetByKey(entry[..at], out var option)) parsed[option] = value;
            else unknown++;
        }
        if (unknown > 0) logger.Info($"今の版に無い設定 {unknown} 個を読み飛ばしました");
        SettingsUI.SettingsPages.Batch(() =>
        {
            foreach (var option in OptionItem.AllOptions)
            {
                if (option is PresetOptionItem) continue;
                option.SetValue(parsed.TryGetValue(option, out var value) ? value : option.DefaultValue, false);
            }
        });
        OptionItem.SyncAllOptions();
    }
    /// <summary>
    /// <see cref="GenerateVanillaOptionsString"/>で生成された形式の文字列を読み込んで適用します
    /// </summary>
    /// <param name="source">オプション情報の文字列</param>
    public static void LoadVanillaOptionsString(string source)
    {
        var bytes = Convert.FromBase64String(source);
        var options = GameOptionsManager.Instance.gameOptionsFactory.FromBytes(bytes);
        GameOptionsManager.Instance.GameHostOptions = GameOptionsManager.Instance.CurrentGameOptions = options;
        GameManager.Instance.LogicOptions.SetGameOptions(options);
        GameManager.Instance.LogicOptions.SyncOptions();
    }
}
