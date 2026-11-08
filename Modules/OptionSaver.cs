using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TownOfHostL.Modules;

/// <summary>
/// 設定の保存。シングルオプションと全プリセットを 1 つの JSON にまとめ、設定のキー (OptionItem.Key) で値を引く。
/// 数値 ID を振り直しても保存した値はずれない。
/// </summary>
public static class OptionSaver
{
    private static readonly DirectoryInfo SaveDataDirectoryInfo = new("./TOHL_DATA/SaveData/");
    private static readonly FileInfo SaveFileInfo = new($"{SaveDataDirectoryInfo.FullName}/Options.json");
    private static readonly LogHandler logger = Logger.Handler(nameof(OptionSaver));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>形式に互換性のない変更 (キーの付け方・プリセット数など) を加える時に上げる</summary>
    public const int Version = 1;

    public static void Initialize()
    {
        SaveDataDirectoryInfo.Refresh();
        if (!SaveDataDirectoryInfo.Exists)
        {
            SaveDataDirectoryInfo.Create();
            SaveDataDirectoryInfo.Attributes |= FileAttributes.Hidden;
        }
    }

    /// <summary>すべての設定を読み込む。全部の設定が作られた後に 1 回呼ぶ。</summary>
    public static void Load()
    {
        OptionItem.FinalizeKeys();

        SaveData data = null;
        try
        {
            SaveFileInfo.Refresh();
            if (SaveFileInfo.Exists)
            {
                var json = File.ReadAllText(SaveFileInfo.FullName);
                if (json.Length > 0) data = JsonSerializer.Deserialize<SaveData>(json);
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"設定の読み込みに失敗しました。前のファイルを .bak に残して既定値で始めます: {ex.Message}");
            Backup();
            data = null;
        }

        // 5 つの枠で保存していた頃のファイル: 枠ごとにプリセットのファイルへ書き出し、選んでいた枠を今の値にする
        bool migrated = false;
        if (data?.Presets != null && data.Presets.Length > 1)
        {
            Backup();
            int current = data.Single != null && data.Single.TryGetValue(OldPresetKey, out var picked) ? Math.Clamp(picked, 0, data.Presets.Length - 1) : 0;
            string[] names = { Main.Preset1.Value, Main.Preset2.Value, Main.Preset3.Value, Main.Preset4.Value, Main.Preset5.Value };
            for (int i = 0; i < data.Presets.Length; i++)
            {
                if (data.Presets[i] == null) continue;
                var name = i < names.Length && names[i] != $"Preset_{i + 1}" ? names[i] : $"Preset{i + 1}";
                var written = SettingsUI.PresetStore.WriteMigrated(name, $"Preset{i + 1}", data.Presets[i]);
                if (written != null) logger.Info($"{i + 1} 番目の枠の設定をプリセット「{written}」に書き出しました");
            }
            data.Presets = new[] { data.Presets[current] };
            migrated = true;
        }

        if (data == null || data.Version != Version)
        {
            if (data != null)
            {
                logger.Info($"保存した設定の形式 ({data.Version}) が今の形式 ({Version}) と違うので、前のファイルを .bak に残して既定値で始めます");
                Backup();
            }
            WriteNow();
            return;
        }

        if (data.Single != null)
        {
            foreach (var (key, value) in data.Single)
                if (OptionItem.TryGetByKey(key, out var option) && option.IsSingleValue)
                    option.SetValue(value, doSave: false);
        }
        if (data.Presets != null)
        {
            for (int i = 0; i < data.Presets.Length && i < OptionItem.NumPresets; i++)
            {
                if (data.Presets[i] == null) continue;
                foreach (var (key, value) in data.Presets[i])
                    if (OptionItem.TryGetByKey(key, out var option) && !option.IsSingleValue)
                        option.AllValues[i] = value;
            }
        }
        if (migrated) WriteNow();
    }

    // 5 つの枠を選んでいた設定の鍵 (枠を 1 つにする前の保存を読む時だけ使う)
    private const string OldPresetKey = "Preset";

    private static SaveData Generate()
    {
        var single = new Dictionary<string, int>();
        var presets = new Dictionary<string, int>[OptionItem.NumPresets];
        for (int i = 0; i < presets.Length; i++) presets[i] = new();

        foreach (var option in OptionItem.AllOptions)
        {
            if (option.IsSingleValue)
                single[option.Key] = option.SingleValue;
            else
                for (int i = 0; i < presets.Length && i < option.AllValues.Length; i++)
                    presets[i][option.Key] = option.AllValues[i];
        }
        return new SaveData { Version = Version, Single = single, Presets = presets };
    }

    /// <summary>読めなかったファイルを上書きする前に残しておく</summary>
    private static void Backup()
    {
        try
        {
            SaveFileInfo.Refresh();
            if (SaveFileInfo.Exists) File.Copy(SaveFileInfo.FullName, SaveFileInfo.FullName + ".bak", true);
        }
        catch (Exception ex)
        {
            logger.Warn($"設定のファイルを .bak に残せませんでした: {ex.Message}");
        }
    }

    /// <summary>今すぐ書く。書きかけで落ちても前のファイルが残るよう、別のファイルに書いてから置き換える。書けたら true。</summary>
    private static bool WriteNow()
    {
        // 接続済みで、ホストでなければ保存しない
        if (AmongUsClient.Instance != null && !AmongUsClient.Instance.AmHost) return true;
        try
        {
            Initialize();
            var json = JsonSerializer.Serialize(Generate(), JsonOptions);
            var path = SaveFileInfo.FullName;
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            return true;
        }
        catch (Exception ex)
        {
            logger.Warn($"設定の保存に失敗しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>現在の設定をすぐ書く</summary>
    public static void Save()
    {
        dirty = true;
        Flush();
    }

    // ---- まとめて保存 ----
    // 値を 1 つ変えるたびに書くと、プリセットの切り替えのような一括の変更で何千回も書くことになる。
    // 変わった印だけ付けて、最後の変更から少し待ってから 1 回だけ書く。
    private const float SaveDelaySeconds = 1f;
    // 書けなかった時 (他のアプリがファイルを掴んでいるなど) は、印を残して少し後にもう一度書く
    private const float RetryDelaySeconds = 5f;
    private static bool dirty;
    private static float saveAt = -1f;

    /// <summary>保存を予約する</summary>
    public static void RequestSave()
    {
        // 接続済みで、ホストでなければ保存しない
        if (AmongUsClient.Instance != null && !AmongUsClient.Instance.AmHost) return;
        dirty = true;
        saveAt = UnityEngine.Time.realtimeSinceStartup + SaveDelaySeconds;
    }

    /// <summary>毎フレーム呼ぶ。予約から時間が経っていれば書く。</summary>
    public static void Tick()
    {
        if (saveAt < 0f || UnityEngine.Time.realtimeSinceStartup < saveAt) return;
        Flush();
    }

    /// <summary>予約されている保存を今すぐ書く</summary>
    public static void Flush()
    {
        saveAt = -1f;
        if (!dirty) return;
        dirty = false;
        if (!WriteNow())
        {
            dirty = true;
            saveAt = UnityEngine.Time.realtimeSinceStartup + RetryDelaySeconds;
        }
    }

    public class SaveData
    {
        public int Version { get; set; }
        /// <summary>シングルオプションの値 (キー → 値)</summary>
        public Dictionary<string, int> Single { get; set; }
        /// <summary>プリセットごとの値 (キー → 値)</summary>
        public Dictionary<string, int>[] Presets { get; set; }
    }
}
