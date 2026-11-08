using System;
using TownOfHostL.Roles.Core;
using UnityEngine; // ScriptableObject 用

namespace TownOfHostL
{
    public class BooleanOptionItem : OptionItem
    {
        public bool Bool => GetValue() == 1;

        public const string TEXT_true = "ColoredOn";
        public const string TEXT_false = "ColoredOff";

        // Boolean 専用の設定クラス
        public class BooleanGameSetting : ScriptableObject
        {
            public bool Value
            {
                get => value;
                set
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);
                }
            }
            private bool value;

            public Action<bool> OnValueChanged;
        }

        // 内部の BooleanGameSetting インスタンス
        public BooleanGameSetting Setting { get; private set; }

        // コンストラクタ
        public BooleanOptionItem(string name, bool defaultValue, TabGroup tab, bool isSingleValue)
            : base(name, defaultValue ? 1 : 0, tab, isSingleValue)
        {
            Setting = ScriptableObject.CreateInstance<BooleanGameSetting>();
            Setting.Value = defaultValue;

            // OptionItem と Setting を同期
            Setting.OnValueChanged += newValue => SetValue(newValue ? 1 : 0);
        }

        public static BooleanOptionItem Create(
            string name, bool defaultValue, TabGroup tab, bool isSingleValue
        )
        {
            return new BooleanOptionItem(name, defaultValue, tab, isSingleValue);
        }

        public static BooleanOptionItem Create(
            Enum name, bool defaultValue, TabGroup tab, bool isSingleValue
        )
        {
            return new BooleanOptionItem(name.ToString(), defaultValue, tab, isSingleValue);
        }

        public static BooleanOptionItem Create(
            SimpleRoleInfo roleInfo, Enum name, bool defaultValue, bool isSingleValue, OptionItem parent = null
        )
        {
            var opt = new BooleanOptionItem(
                name.ToString(), defaultValue, roleInfo.Tab, isSingleValue
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }
        public static BooleanOptionItem Create(
            SimpleRoleInfo roleInfo, string name, bool defaultValue, bool isSingleValue, OptionItem parent = null
        )
        {
            var opt = new BooleanOptionItem(
                name, defaultValue, roleInfo.Tab, isSingleValue
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }

        // Getter
        public override string GetString()
        {
            return Translator.GetString(GetBool() ? TEXT_true : TEXT_false);
        }

        // Setter
        public override void SetValue(int value, bool doSync = true)
        {
            int normalizedValue = value % 2 == 0 ? 0 : 1;
            base.SetValue(normalizedValue, doSync);

            // ScriptableObject 側も同期
            bool settingValue = normalizedValue != 0;
            if (Setting != null && Setting.Value != settingValue)
            {
                Setting.Value = settingValue;
            }
        }
    }
}
