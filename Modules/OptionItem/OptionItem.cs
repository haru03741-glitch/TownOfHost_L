using TownOfHostL.Roles.Core;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using TownOfHostL.Modules;
using UnityEngine;

namespace TownOfHostL
{
    public abstract class OptionItem
    {
        #region static
        public static IReadOnlyList<OptionItem> AllOptions => _allOptions;
        private static readonly List<OptionItem> _allOptions = new(1024);
        public static IReadOnlyDictionary<int, OptionItem> FastOptions => _fastOptions;
        private static readonly Dictionary<int, OptionItem> _fastOptions = new(1024);
        public static int CurrentPreset { get; set; }
        public static bool IdDuplicated { get; private set; } = false;
        #endregion

        // 必須情報(コンストラクタで必ず設定させる必要がある値)
        public int Id { get; }
        /// <summary>
        /// 保存に使うキー。親をたどった名前の道筋 (例: Sheriff/KillCooldown)。
        /// 同じ道筋の設定が他にもある時は、区別のため #ID を付ける。
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

        public OptionBehaviour OptionBehaviour;

        // イベント
        // eventキーワードにより、クラス外からのこのフィールドに対する以下の操作は禁止されます。
        // - 代入 (+=, -=を除く)
        // - 直接的な呼び出し
        public event EventHandler<UpdateValueEventArgs> UpdateValueEvent;

        // コンストラクタ
        public OptionItem(int id, string name, int defaultValue, TabGroup tab, bool isSingleValue)
        {
            // 必須情報の設定
            Id = id;
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
            // オブジェクト初期化
            Children = new();

            // デフォルト値に設定
            if (Id == PresetId)
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

            // ID 登録
            if (_fastOptions.TryAdd(id, this))
            {
                _allOptions.Add(this);
            }
            else
            {
#if DEBUG
                IdDuplicated = true;
#endif
                var existing = _fastOptions[id];
                Logger.Error(
                    $"ID:{id} が重複しています " +
                    $"(既存: Name={existing.Name}, Tab={existing.Tab}, Default={existing.DefaultValue}; " +
                    $"新規: Name={Name}, Tab={Tab}, Default={DefaultValue})",
                    "OptionItem"
                );
            }
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
        public OptionItem SetFixValue(bool value) => Do(i => i.IsFixValue = value);
        public OptionItem SetText(bool value) => Do(i => i.IsText = value);
        public OptionItem SetReplacementDictionary(Dictionary<string, string> value) => Do(i => i.ReplacementDictionary = value);
        public OptionItem SetKeyName(string value) => Do(i => i.KeyName = value);

        public OptionItem SetParent(OptionItem parent) => Do(i =>
        {
            i.Parent = parent;
            parent.SetChild(i);
        });
        public OptionItem SetChild(OptionItem child) => Do(i => i.Children.Add(child));
        // hamo の設定の機能
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
        public virtual int GetValue() => IsSingleValue ? SingleValue : AllValues[CurrentPreset];
        /// <summary>機能の設定 (offsetId.FeatSpecial 以降の帯。役職とゲームモード以外の設定一覧に出す物)</summary>
        public bool IsFeatureOption => Id >= (int)Options.offsetId.FeatSpecial;

        // Hidden判定
        public virtual bool IsHiddenOn(CustomGameMode mode)
        {
            return IsHidden || (GameMode != CustomGameMode.All && GameMode != mode);
        }

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

        // 外部からの操作
        public virtual void Refresh()
        {
            if (OptionBehaviour is StringOption stringOption)
            {
                stringOption.TitleText.text = GetName();
                stringOption.Value = CurrentValue;
                stringOption.oldValue = CurrentValue;
                stringOption.ValueText.text = GetString();
            }
            else if (OptionBehaviour is NumberOption numberOption)
            {
                numberOption.TitleText.text = GetName();
                numberOption.Value = GetFloat();
                numberOption.oldValue = numberOption.Value;
                numberOption.ValueText.text = GetString();
            }
            else if (OptionBehaviour is ToggleOption toggleOption)
            {
                toggleOption.TitleText.text = GetName();
                toggleOption.CheckMark.enabled = GetBool();
            }
        }
        public virtual void SetValue(int afterValue, bool doSave, bool doSync = true)
        {
            int beforeValue = CurrentValue;
            if (IsSingleValue)
                SingleValue = afterValue;
            else
                AllValues[CurrentPreset] = afterValue;

            CallUpdateValueEvent(beforeValue, afterValue);
            Refresh();
            if (Children.Count > 0 && beforeValue != CurrentValue)
            {
                GameOptionsMenuPatch.RefreshChildSettings();
            }
            if (doSync)
                SyncAllOptions();
            if (doSave)
                OptionSaver.RequestSave();
        }
        public virtual void SetValue(int afterValue, bool doSync = true) => SetValue(afterValue, true, doSync);

        public void SetAllValues(int[] values) => AllValues = values; // プリセット読み込み専用

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
            // 確定の後に作られた設定。道筋が既にあれば #ID で区別する
            var path = KeyPath;
            if (byKey != null && !byKey.TryAdd(path, this))
            {
                path = $"{path}#{Id}";
                byKey[path] = this;
            }
            return path;
        }

        /// <summary>
        /// 全部の設定のキーを確定する。親から順に、同じ道筋が重なったら、先に作られた 1 つは道筋のまま、残りに #ID を付ける。
        /// 後から同じ名前の設定を足しても、前からある設定のキーは変わらない。
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
                    Logger.Warn($"設定のキー {path} が {g.Count} 個で重なっているので、後から作られた方に #ID を付けて区別します", "OptionItem");
                    for (int i = 0; i < g.Count; i++)
                    {
                        var o = g[i];
                        o.key = i == 0 && byKey.TryAdd(path, o) ? path : $"{path}#{o.Id}";
                        byKey[o.key] = o;
                    }
                }
            }
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
            foreach (var op in AllOptions)
                op.Refresh();
            SyncAllOptions();
        }
        public static void SyncAllOptions()
        {
            // 同期無効化: ホスト専用モードでは他プレイヤーへオプションを送信しない
            //if (
            //    Main.AllPlayerControls.Count() <= 1 ||
            //    AmongUsClient.Instance.AmHost == false ||
            //    PlayerControl.LocalPlayer == null
            //) return;

            //RPC.SyncCustomSettingsRPC();
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

        public const int NumPresets = 5;
        public const int PresetId = 0;
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
    /// <summary>値が 0 の時の表示 (hamo)</summary>
    public enum OptionZeroNotation
    {
        None,
        Infinity,
        Hyphen,
        Off
    }
}
