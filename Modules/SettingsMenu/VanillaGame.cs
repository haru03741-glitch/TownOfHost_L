// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/VanillaGame.cs and src/Roles/Builtin/VanillaRoles.cs (VanillaOpt) (GPL-3.0)
using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

// 本編の「ゲーム設定」タブの項目 (インポスター数・キルのクールダウンなど) を、設定画面の行の形 (MenuOpt) で持つ。
// 値は本編の設定 (GameOptionsManager.CurrentGameOptions) にあり、保存と同期は本編の仕組みのまま。
// 見出しの並びは今のゲームモードの本編の一覧のとおり。設定画面を開くたびに作り直す (言語とゲームモードが変わりうるので)
internal static class VanillaGame
{
    internal sealed class Section
    {
        public string Title;
        public readonly List<VanillaMenuOpt> Opts = new();
    }

    private static readonly List<Section> Sections = new();

    public static IReadOnlyList<Section> All => Sections;

    // 本編の設定を画面で変えた (閉じた時に本編の保存と同期を回す)
    public static bool Dirty;

    public static void Rebuild()
    {
        Sections.Clear();
        var gm = GameManager.Instance;
        var list = gm ? gm.GameSettingsList : null;
        var cats = list ? list.AllCategories : null;
        if (cats == null) return;
        var tc = TranslationController.Instance;
        foreach (var cat in cats)
        {
            if (cat == null || cat.AllGameSettings == null) continue;
            string title = tc ? tc.GetString(cat.CategoryName) : cat.CategoryName.ToString();
            var sec = new Section { Title = title };
            foreach (var s in cat.AllGameSettings)
            {
                var opt = VanillaMenuOpt.From(s);
                if (opt != null) sec.Opts.Add(opt);
            }
            if (sec.Opts.Count > 0) Sections.Add(sec);
        }
    }

    // 本編は、設定が初期値のまま (IsDefaults) の間、人が入るたびに人数のおすすめの値で設定を上書きする。
    // 本編の画面は値を変えると初期値の印を下ろしてカスタムに切り替えるので、こちらで値を書いた時も同じにする
    public static void MarkCustom(IGameOptions opts)
    {
        if (opts == null) return;
        if (opts.TryCast<NormalGameOptionsV12>() is { } normal)
        {
            normal.IsDefaults = false;
            normal.RulesPreset = RulesPresets.Custom;
        }
        else if (opts.TryCast<HideNSeekGameOptionsV12>() is { } hns)
        {
            hns.IsDefaults = false;
            hns.RulesPreset = RulesPresets.Custom;
        }
    }

    // 保存しておいた本編の設定を読み込む時、部屋の言語は今の部屋の値のままにする。
    // 公式鯖は、部屋と違う言語の入った設定を送ったホストを切断する (2026-11-27 公式アジアで実測)
    public static void KeepRoomLanguage(IGameOptions loaded, IGameOptions current)
    {
        if (loaded == null || current == null) return;
        var keywords = current.Keywords;
        if (loaded.TryCast<NormalGameOptionsV12>() is { } normal) normal.Keywords = keywords;
        else if (loaded.TryCast<HideNSeekGameOptionsV12>() is { } hns) hns.Keywords = keywords;
    }

    // 設定画面を閉じた時 (ホスト): 本編の設定を変えていれば本編の保存と同期を回す
    public static void OnMenuClosed()
    {
        if (!Dirty) return;
        Dirty = false;
        try
        {
            var opts = GameOptionsManager.Instance;
            if (opts == null) return;
            // かくれんぼの時は本編の設定の欄も別 (保存先も別)
            if (opts.CurrentGameOptions != null && opts.CurrentGameOptions.GameMode == GameModes.HideNSeek) opts.SaveHideNSeekHostOptions();
            else opts.SaveNormalHostOptions();
            if (GameManager.Instance) GameManager.Instance.LogicOptions.SyncOptions();
        }
        catch (Exception e)
        {
            Logger.Warn($"本編の設定の同期に失敗: {e.Message}", "VanillaGame");
        }
    }
}

// 本編の設定 1 つを読み書きする行
internal sealed class VanillaMenuOpt : MenuOpt
{
    private readonly BaseGameSetting _setting;
    private readonly FloatGameSetting _float;
    private readonly IntGameSetting _int;
    private readonly CheckboxGameSetting _check;
    private readonly StringGameSetting _string;
    private readonly PlayerSelectionGameSetting _player;
    // 人の選択肢 (PlayerSelectionGameSetting)。先頭の -1 は「指名しない」(本編は -1 を未指名として扱う)。
    // ロビーの顔ぶれで変わるので読むたびに作り直す
    private readonly List<int> _playerIds = new();
    private readonly StringNames[] _values; // 文字の選択肢 (StringGameSetting)
    private readonly string _label;
    private readonly float _min, _step;
    private int _count;
    private int _index;

    public StringNames SettingTitle => _setting != null ? _setting.Title : default;

    private VanillaMenuOpt(string label, BaseGameSetting s, FloatGameSetting f, IntGameSetting i, CheckboxGameSetting c, StringGameSetting str, PlayerSelectionGameSetting p) : base(null)
    {
        _label = label;
        _setting = s;
        _float = f;
        _int = i;
        _check = c;
        _string = str;
        _player = p;
        if (p != null) _count = RefreshPlayers();
        else if (str != null)
        {
            var v = str.Values;
            _values = new StringNames[v != null ? v.Length : 0];
            for (int k = 0; k < _values.Length; k++) _values[k] = v[k];
            _count = _values.Length;
        }
        else if (f != null)
        {
            var (lo, hi) = Range(s.Title, f.ValidRange.min, f.ValidRange.max);
            _min = lo;
            _step = Step(s.Title, f.Increment > 0f ? f.Increment : 1f);
            _count = (int)Math.Round((hi - _min) / _step) + 1;
        }
        else if (i != null)
        {
            var (lo, hi) = Range(s.Title, i.ValidRange.min, i.ValidRange.max);
            _min = lo;
            _step = Math.Max((int)Step(s.Title, i.Increment), 1);
            _count = (int)Math.Round((hi - _min) / _step) + 1;
        }
        else _count = 2;
        _count = Math.Max(_count, 1);
    }

    private static (float min, float max) Range(StringNames title, float min, float max)
    {
        switch (title)
        {
            case StringNames.GameShortTasks:
            case StringNames.GameLongTasks:
            case StringNames.GameCommonTasks:
                return (0f, 99f);
            case StringNames.GameKillCooldown:
                return (0f, 180f);
            case StringNames.GameNumImpostors:
                return (0f, 15f);
        }
        return (min, max);
    }

    private static float Step(StringNames title, float step) => title switch
    {
        StringNames.GameKillCooldown => 0.5f,
        StringNames.GameVotingTime or StringNames.GameEmergencyCooldown or StringNames.GameDiscussTime => 1f,
        _ => step,
    };

    // 本編の設定の定義から作る (数値・整数・ON/OFF・文字の選択肢・人の指名。マップはゲームの頁の上の段で選ぶので出さない)
    public static VanillaMenuOpt From(BaseGameSetting s)
    {
        if (!s) return null;
        var f = s.TryCast<FloatGameSetting>();
        var i = f == null ? s.TryCast<IntGameSetting>() : null;
        var c = f == null && i == null ? s.TryCast<CheckboxGameSetting>() : null;
        var str = f == null && i == null && c == null ? s.TryCast<StringGameSetting>() : null;
        var p = f == null && i == null && c == null && str == null ? s.TryCast<PlayerSelectionGameSetting>() : null;
        if (f == null && i == null && c == null && str == null && p == null) return null;
        string label = TranslationController.Instance ? TranslationController.Instance.GetString(s.Title) : s.Title.ToString();
        return new VanillaMenuOpt(label, s, f, i, c, str, p);
    }

    private static IGameOptions Options => GameOptionsManager.Instance?.CurrentGameOptions;

    public override string Key => "vanilla." + _setting.Title;
    public override string Label => _label;
    public override string PlainLabel => _label;
    public override int Index => _index;
    public override int DefaultIndex => 0;
    public override int Count => _count;
    public override bool IsToggle => _check != null;
    public override bool IsText => false;
    public override bool Fixed => false;
    public override bool WrapsAround => _check != null || _string != null || _player != null;
    public override Color NameColor => Color.white;
    // 会議の無いモード (タスクバトルなど) では、本編の会議の設定を出さない
    public override bool Shown => !IsMeetingSetting || Core.Modes.ModeCatalog.Current.HasMeetings;
    private bool IsMeetingSetting => _setting != null && _setting.Title is StringNames.GameNumMeetings or StringNames.GameEmergencyCooldown
        or StringNames.GameDiscussTime or StringNames.GameVotingTime or StringNames.GameConfirmImpostor or StringNames.GameAnonymousVotes;
    public override int Depth => 0;
    public override bool HasDefault => false;
    public override bool HasDetail => false;
    public override string Detail() => null;

#pragma warning disable CS0618 // TryGet〜 は out 引数が il2cpp を跨ぐので、値を返す方を使う
    private float Read()
    {
        var o = Options;
        if (o == null) return 0f;
        if (_float != null) return o.GetFloat(_float.OptionName);
        if (_int != null) return o.GetInt(_int.OptionName);
        if (_string != null) return o.GetInt(_string.OptionName);
        if (_player != null) return o.GetInt(_player.OptionName);
        return o.GetBool(_check.OptionName) ? 1f : 0f;
    }
#pragma warning restore CS0618

    // 指名できる人を作り直す (抜けた人を外し、入った人を足す)。選択肢の数を返す
    private int RefreshPlayers()
    {
        _playerIds.Clear();
        _playerIds.Add(-1);
        var data = GameData.Instance;
        if (data)
        {
            int first = _playerIds.Count;
            foreach (var info in data.AllPlayers)
                if (info && !info.Disconnected) _playerIds.Add(info.PlayerId);
            _playerIds.Sort(first, _playerIds.Count - first, null);
        }
        return _playerIds.Count;
    }

    // 本編の設定から番号を読み直す
    public override void Sync()
    {
        float v = Read();
        if (_player != null)
        {
            _count = RefreshPlayers();
            // 指名した人が抜けていたら「指名しない」と同じ (本編も試合の開始時に居ない人の指名は使わない)
            _index = Math.Max(_playerIds.IndexOf((int)v), 0);
            return;
        }
        _index = _check != null ? (v != 0f ? 1 : 0)
            : _string != null ? Math.Clamp((int)v, 0, Math.Max(_count - 1, 0))
            : Math.Clamp((int)Math.Round((v - _min) / _step), 0, _count - 1);
    }

    private float ValueOf(int index) => _min + index * _step;
    public override double? ValueAt(int index) => _float != null || _int != null ? Math.Round(ValueOf(index), 4) : null;

    public override string Display()
    {
        Sync();
        if (_check != null) return _index != 0 ? "ON" : "OFF";
        if (_player != null) return _setting.GetValueString(_playerIds[_index]);
        if (_string != null)
        {
            if (_index >= _values.Length) return "-";
            return TranslationController.Instance ? TranslationController.Instance.GetString(_values[_index]) : _values[_index].ToString();
        }
        return _setting.GetValueString(ValueOf(_index));
    }

    // 押されたら本編の設定へ書く (同期と保存は画面を閉じた時に本編の仕組みで)
    protected override void Write(int index)
    {
        var o = Options;
        if (o == null) return;
        _index = index;
        if (_float != null) o.SetFloat(_float.OptionName, ValueOf(index));
        else if (_int != null) o.SetInt(_int.OptionName, (int)Math.Round(ValueOf(index)));
        else if (_string != null) o.SetInt(_string.OptionName, index);
        else if (_player != null) o.SetInt(_player.OptionName, _playerIds[Math.Clamp(index, 0, _playerIds.Count - 1)]);
        else o.SetBool(_check.OptionName, index != 0);
        VanillaGame.MarkCustom(o);
        VanillaGame.Dirty = true;
    }
}
