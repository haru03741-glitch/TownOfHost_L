using System;
using TownOfHostL.Roles.Core;

namespace TownOfHostL
{
    public class TextOptionItem : OptionItem
    {
        // 必須情報
        public IntegerValueRule Rule;

        // コンストラクタ
        public TextOptionItem(string name, int defaultValue, TabGroup tab, bool isSingleValue)
        : base(name, defaultValue, tab, isSingleValue)
        {
            IsText = true;
            //IsHeader = true;
        }
        public static TextOptionItem Create(
            string name, TabGroup tab, bool isSingleValue = false
        )
        {
            return new TextOptionItem(
                name, 0, tab, isSingleValue
            );
        }
        public static TextOptionItem Create(
            Enum name, TabGroup tab, bool isSingleValue = false
        )
        {
            return new TextOptionItem(
                name.ToString(), 0, tab, isSingleValue
            );
        }
        public static TextOptionItem Create(
            SimpleRoleInfo roleInfo, Enum name, bool isSingleValue = false, OptionItem parent = null
        )
        {
            var opt = new TextOptionItem(
                name.ToString(), 0, roleInfo.Tab, isSingleValue
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }

        // Getter
        public override int GetInt() => Rule.GetValueByIndex(CurrentValue);
        public override float GetFloat() => Rule.GetValueByIndex(CurrentValue);
        public override string GetString()
        {
            return Translator.GetString(Name);
        }

        // Setter
        public override void SetValue(int value, bool doSync = true)
        {
        }
    }
}