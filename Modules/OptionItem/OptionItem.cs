using TownOfHostL.Roles.Core;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using TownOfHostL.Modules;
using UnityEngine;

namespace TownOfHostL
{
    /// <summary>設定が前提にする試合の仕組み。モードがその仕組みを持たない時は、設定を画面に出さない。</summary>
    [Flags]
    public enum OptionTopic
    {
        None = 0,
        /// <summary>通報・緊急会議・投票</summary>
        Meeting = 1,
        /// <summary>ドア以外のサボタージュ</summary>
        Sabotage = 2,
        /// <summary>ドアのサボタージュ</summary>
        DoorSabotage = 4,
        /// <summary>ドアかドア以外か、どちらかのサボタージュ</summary>
        AnySabotage = 8,
    }

    public abstract class OptionItem
    {
        #region static
        public static IReadOnlyList<OptionItem> AllOptions => _allOptions;
        private static readonly List<OptionItem> _allOptions = new(1024);
        public static int CurrentPreset { get; set; }
        /// <summary>保存のキーが重なった設定がある (DEBUG ビルドでだけ立てる)</summary>
        public static bool KeyDuplicated { get; private set; } = false;
        #endregion

        // 必須情報(コンストラクタで必ず設定させる必要がある値)
        /// <summary>
        /// 保存に使うキー。親をたどった名前の道筋 (例: Sheriff/KillCooldown)。
        /// 同じ道筋の設定が他にもある時は、区別のため 2 つ目から #2・#3… を付ける (KeyName で重ならないようにする)。
        /// </summary>
        public string Key => key ??= BuildKey();
        private string key;
        public string Name { get; }
        /// <summary>
        /// キーの道筋で Name の代わりに使う名前。%role% などの置き換えで役職ごとに表示が変わり、
        /// 同じ親の下に同じ Name が並ぶ設定に、言語に依らない名前を与える。
        /// </summary>
        public string KeyName { get; private set; }
        public int DefaultValue { get; }
        public TabGroup Tab { get; }
        public bool IsSingleValue { get; }

        // 任意情報(空・nullを許容する または、ほとんど初期値で問題ない値)
        public Color NameColor { get; protected set; }
        public OptionFormat ValueFormat { get; protected set; }
        public CustomGameMode GameMode { get; protected set; }
        public bool IsHeader { get; protected set; }
        public string IsHeaderName { get; protected set; }
        public bool IsHidden { get; protected set; }
        public bool IsFixValue { get; protected set; }
        public bool IsText { get; protected set; }
        /// <summary>この設定が前提にする試合の仕組み。その仕組みの無いモードでは画面に出さない (子は親の分も受け継ぐ)。</summary>
        public OptionTopic Topic { get; protected set; }

        public Dictionary<string, string> ReplacementDictionary
        {
            get => _replacementDictionary;
            set
            {
                if (value == null) _replacementDictionary?.Clear();
                else _replacementDictionary = value;
            }
        }
        private Dictionary<string, string> _replacementDictionary;

        // 設定値情報 (オプションの値に関わる情報)
        public int[] AllValues { get; private set; } = new int[NumPresets];
        public int CurrentValue
        {
            get => GetValue();
            set => SetValue(value);
        }
        public int SingleValue { get; private set; }

        // 親子情報
        public OptionItem Parent { get; private set; }
        public List<OptionItem> Children;

        // イベント
        // eventキーワードにより、クラス外からのこのフィールドに対する以下の操作は禁止されます。
        // - 代入 (+=, -=を除く)
        // - 直接的な呼び出し
        public event EventHandler<UpdateValueEventArgs> UpdateValueEvent;

        // コンストラクタ
        public OptionItem(string name, int defaultValue, TabGroup tab, bool isSingleValue)
        {
            // 必須情報の設定
            Name = name;
            DefaultValue = defaultValue;
            Tab = tab;
            IsSingleValue = isSingleValue;

            // 任意情報の初期値設定
            NameColor = Color.white;
            ValueFormat = OptionFormat.None;
            GameMode = CustomGameMode.Standard;
            IsHeader = false;
            IsHeaderName = "";
            IsHidden = false;
            IsFixValue = false;
            IsText = false;
            IsFeatureOption = CreatingFeatures;
            Index = _allOptions.Count;
            // オブジェクト初期化
            Children = new();

            // デフォルト値に設定
            if (this is PresetOptionItem)
            {
                SingleValue = DefaultValue;
                CurrentPreset = SingleValue;
            }
            else if (IsSingleValue)
            {
                SingleValue = DefaultValue;
            }
            else
            {
                for (int i = 0; i < NumPresets; i++)
                    AllValues[i] = DefaultValue;
            }

            _allOptions.Add(this);
        }

        // Setter
        public OptionItem Do(Action<OptionItem> action)
        {
            action(this);
            return this;
        }

        public OptionItem SetColor(Color value) => Do(i => i.NameColor = value);
        public OptionItem SetValueFormat(OptionFormat value) => Do(i => i.ValueFormat = value);
        public OptionItem SetGameMode(CustomGameMode value) => Do(i => i.GameMode = value);
        public OptionItem SetHeader(bool value, string str = "") => Do(i => { i.IsHeader = value; if (str != "") i.IsHeaderName = str; });
        public OptionItem SetHidden(bool value) => Do(i => i.IsHidden = value);
        public OptionItem SetTopic(OptionTopic value) => Do(i => i.Topic = value);
        /// <summary>自分と親の話題を合わせた物。</summary>
        public OptionTopic AllTopics
        {
            get
            {
                var t = OptionTopic.None;
                for (var o = this; o != null; o = o.Parent) t |= o.Topic;
                return t;
            }
        }
        /// <summary>index 以降に作った設定のうち、話題がまだ無い物に付ける (見出しの節をまとめて印す)。</summary>
        public static void SetTopicFrom(int index, OptionTopic value)
        {
            for (int i = index; i < _allOptions.Count; i++)
                if (_allOptions[i].Topic == OptionTopic.None) _allOptions[i].Topic = value;
        }
        public Func<bool> EnabledCondition { get; private set; }
        public OptionItem SetEnabled(Func<bool> condition) => Do(i => i.EnabledCondition = condition);
        public OptionItem SetFixValue(bool value) => Do(i => i.IsFixValue = value);
        public OptionItem SetText(bool value) => Do(i => i.IsText = value);
        public OptionItem SetReplacementDictionary(Dictionary<string, string> value) => Do(i => i.ReplacementDictionary = value);
        public OptionItem SetKeyName(string value) => Do(i => i.KeyName = value);

        // 作った後に親を付け替えた時は前の親の子から外す (残すと設定画面や /n の木で 2 回並ぶ)
        public OptionItem SetParent(OptionItem parent) => Do(i =>
        {
            if (i.Parent == parent && parent?.Children.Contains(i) == true) return;
            i.Parent?.Children.Remove(i);
            i.Parent = parent;
            parent?.SetChild(i);
        });
        public OptionItem SetChild(OptionItem child) => Do(i => i.Children.Add(child));
        // 設定の機能 (見出し・値の表示の差し替え)
        public OptionZeroNotation ZeroNotation { get; protected set; } = OptionZeroNotation.None;
        public Func<string> funcOptionName { get; protected set; }
        public OptionItem SetZeroNotation(OptionZeroNotation value) => Do(i => i.ZeroNotation = value);
        public OptionItem SetOptionName(Func<string> value) => Do(i => i.funcOptionName = value);
        public OptionItem SetTooltip(Func<string> value) => this;
        public OptionItem SetParentRole(CustomRoles parentrole) => this;
        public OptionItem SetColorcode(string value) => Do(i => { if (ColorUtility.TryParseHtmlString(value, out var c)) i.NameColor = c; });
        public OptionItem SetInfo(string value) => this;
        public OptionItem RegisterUpdateValueEvent(EventHandler<UpdateValueEventArgs> handler)
            => Do(i => UpdateValueEvent += handler);

        // 置き換え辞書
        public OptionItem AddReplacement((string key, string value) kvp)
            => Do(i =>
            {
                ReplacementDictionary ??= new();
                ReplacementDictionary.Add(kvp.key, kvp.value);
            });
        public OptionItem RemoveReplacement(string key)
            => Do(i => ReplacementDictionary?.Remove(key));

        // Getter
        public virtual string GetName(bool disableColor = false, bool colorLighter = false)
        {
            var funcName = funcOptionName?.Invoke();
            if (!string.IsNullOrEmpty(funcName))
                return disableColor ? funcName : Utils.ColorString(colorLighter ? NameColor.ShadeColor(-0.3f) : NameColor, funcName);
            return disableColor ?
                Translator.GetString(Name, ReplacementDictionary) :
                Utils.ColorString(colorLighter ? NameColor.ShadeColor(-0.3f) : NameColor, Translator.GetString(Name, ReplacementDictionary));
        }
        public virtual bool GetBool() => CurrentValue != 0 && (Parent == null || Parent.GetBool());
        public virtual int GetInt() => CurrentValue;
        public virtual float GetFloat() => CurrentValue;
        public virtual string GetString() => ApplyFormat(CurrentValue.ToString());
        public virtual int GetValue() => remoteValues != null && this is not PresetOptionItem ? remoteValues[Index] : StoredValue;
        /// <summary>手元に持っている値 (保存する値)。客の間にホストの値を見ている時も、こちらは変えない</summary>
        public int StoredValue => IsSingleValue ? SingleValue : AllValues[CurrentPreset];
        /// <summary><see cref="AllOptions"/> の中の位置。同じ版の TOHL どうしで同じになるので、ホストの値を客へ写す時の番号に使う</summary>
        public int Index { get; }

        // ホストの部屋に同じ版の TOHL の客として入っている間だけ、ホストから届いた値を読む (保存はしない・部屋を出たら捨てる)
        private static int[] remoteValues;
        public static bool HasRemoteValues => remoteValues != null;
        /// <summary>ホストの値を読む層を作る (全部を初期値にしておき、届いた値で上書きする)</summary>
        public static void BeginRemoteValues()
        {
            remoteValues = new int[_allOptions.Count];
            for (int i = 0; i < _allOptions.Count; i++) remoteValues[i] = _allOptions[i].DefaultValue;
        }
        public static void SetRemoteValue(int index, int value)
        {
            if (remoteValues != null && (uint)index < (uint)remoteValues.Length) remoteValues[index] = value;
        }
        public static void EndRemoteValues() => remoteValues = null;
        /// <summary>機能の設定 (設定一覧の「機能」の欄に出す物)。作った時に <see cref="CreatingFeatures"/> が立っていたか</summary>
        public bool IsFeatureOption { get; }
        /// <summary>立てている間に作った設定を機能の設定にする (Options.Load が機能の節の間だけ立てる)</summary>
        public static bool CreatingFeatures { get; set; }


        public string ApplyFormat(string value)
        {
            if (ZeroNotation != OptionZeroNotation.None && value == "0")
            {
                switch (ZeroNotation)
                {
                    case OptionZeroNotation.Infinity: return "∞";
                    case OptionZeroNotation.Hyphen: return "―";
                    case OptionZeroNotation.Off: return "×";
                }
            }
            if (ValueFormat == OptionFormat.None) return value;
            return string.Format(Translator.GetString("Format." + ValueFormat), value);
        }

        public virtual void SetValue(int afterValue, bool doSave, bool doSync = true)
        {
            int beforeValue = CurrentValue;
            var beforeText = Modules.SettingChangeNotice.Wants(this) ? GetString() : null;
            if (IsSingleValue)
                SingleValue = afterValue;
            else
                AllValues[CurrentPreset] = afterValue;

            CallUpdateValueEvent(beforeValue, afterValue);
            if (beforeValue != afterValue) Modules.Net.ModClientSync.OnOptionChanged(this);
            if (doSave)
                OptionSaver.RequestSave();
            if (beforeText != null && beforeValue != afterValue) Modules.SettingChangeNotice.OnChanged(this, beforeText, GetString());
            // 開いている設定画面の頁へ、値と出る行の変わりを写す (画面の側で落ちても、値と保存は済んでいる)
            if (!SettingsUI.SettingsPages.Batching) SettingsUI.SettingsPages.RefreshAllSafe();
        }
        public virtual void SetValue(int afterValue, bool doSync = true) => SetValue(afterValue, true, doSync);

        public void SetAllValues(int[] values) // プリセット読み込み専用
        {
            AllValues = values;
            Modules.Net.ModClientSync.OnOptionsReloaded();
        }

        // 演算子オーバーロード
        public static OptionItem operator ++(OptionItem item)
            => item.Do(i => i.SetValue(i.CurrentValue + 1));
        public static OptionItem operator --(OptionItem item)
            => item.Do(i => i.SetValue(i.CurrentValue - 1));

        // ---- キー ----
        private static Dictionary<string, OptionItem> byKey;

        private string KeyPath => Parent == null ? KeyName ?? Name : $"{Parent.Key}/{KeyName ?? Name}";
        private string BuildKey()
        {
            // 確定の後に作られた設定。道筋が既にあれば、空いている #n で区別する
            var path = KeyPath;
            if (byKey == null || byKey.TryAdd(path, this)) return path;
            OnKeyOverlap(path);
            for (int n = 2; ; n++)
            {
                var numbered = $"{path}#{n}";
                if (byKey.TryAdd(numbered, this)) return numbered;
            }
        }

        /// <summary>
        /// 全部の設定のキーを確定する。親から順に、同じ道筋が重なったら、先に作られた 1 つは道筋のまま、残りに作った順で #2・#3… を付ける。
        /// 番号は作る順で決まり、間に同じ名前の設定を足すと後ろの番号がずれるので、重なりは KeyName で無くしておく。
        /// </summary>
        public static void FinalizeKeys()
        {
            byKey = new(_allOptions.Count);
            var depthOf = new Dictionary<OptionItem, int>(_allOptions.Count);
            int Depth(OptionItem o)
            {
                if (depthOf.TryGetValue(o, out var d)) return d;
                d = o.Parent == null ? 0 : Depth(o.Parent) + 1;
                depthOf[o] = d;
                return d;
            }
            var byDepth = new SortedDictionary<int, List<OptionItem>>();
            foreach (var o in _allOptions)
            {
                var d = Depth(o);
                if (!byDepth.TryGetValue(d, out var list)) byDepth[d] = list = new();
                list.Add(o);
            }
            foreach (var level in byDepth.Values)
            {
                var groups = new Dictionary<string, List<OptionItem>>();
                foreach (var o in level)
                {
                    var path = o.KeyPath;
                    if (!groups.TryGetValue(path, out var g)) groups[path] = g = new();
                    g.Add(o);
                }
                foreach (var (path, g) in groups)
                {
                    if (g.Count == 1 && !byKey.ContainsKey(path))
                    {
                        g[0].key = path;
                        byKey[path] = g[0];
                        continue;
                    }
                    OnKeyOverlap(path);
                    var n = 1;
                    foreach (var o in g)
                    {
                        var k = n == 1 ? path : $"{path}#{n}";
                        while (!byKey.TryAdd(k, o)) k = $"{path}#{++n}";
                        o.key = k;
                        n++;
                    }
                }
            }
        }

        private static void OnKeyOverlap(string path)
        {
#if DEBUG
            KeyDuplicated = true;
#endif
            Logger.Error($"設定のキー {path} が重なっています。KeyName で別の名前を付けてください", "OptionItem");
        }

        public static bool TryGetByKey(string key, out OptionItem option)
        {
            option = null;
            return byKey != null && byKey.TryGetValue(key, out option);
        }

        // 全体操作用
        public static void SwitchPreset(int newPreset)
        {
            CurrentPreset = Math.Clamp(newPreset, 0, NumPresets - 1);
            Modules.Net.ModClientSync.OnOptionsReloaded();
            SettingsUI.SettingsPages.RefreshAllSafe();
        }

        // EventArgs
        private void CallUpdateValueEvent(int beforeValue, int currentValue)
        {
            if (UpdateValueEvent == null) return;
            try
            {
                UpdateValueEvent(this, new UpdateValueEventArgs(beforeValue, currentValue));
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Name}] UpdateValueEventの呼び出し時に例外が発生しました", "OptionItem.UpdateValueEvent");
                Logger.Exception(ex, "OptionItem.UpdateValueEvent");
            }
        }

        public class UpdateValueEventArgs : EventArgs
        {
            public int CurrentValue { get; set; }
            public int BeforeValue { get; set; }
            public UpdateValueEventArgs(int beforeValue, int currentValue)
            {
                CurrentValue = currentValue;
                BeforeValue = beforeValue;
            }
        }

        // 値の枠は 1 つ。名前を付けた設定の保存は、設定画面のプリセット (TOHL_DATA/Presets) で持つ
        public const int NumPresets = 1;
    }

    public enum TabGroup
    {
        ModMainSettings,
        ImpostorRoles,
        MadmateRoles,
        CrewmateRoles,
        NeutralRoles,
        UnitRoles,
        Addons,
    }

    public enum OptionFormat
    {
        None,
        Players,
        Seconds,
        Percent,
        Times,
        Multiplier,
        Votes,
        Pieces,
        Pair,
        Turns,
        day,
    }
    /// <summary>値が 0 の時の表示</summary>
    public enum OptionZeroNotation
    {
        None,
        Infinity,
        Hyphen,
        Off
    }
}
