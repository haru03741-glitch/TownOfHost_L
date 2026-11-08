using System;
using TownOfHostL.Roles.Core;

namespace TownOfHostL
{
    public class StringOptionItem : OptionItem
    {
        // 必須情報
        public IntegerValueRule Rule;
        public string[] Selections;

        // コンストラクタ
        public StringOptionItem(string name, int defaultValue, TabGroup tab, bool isSingleValue, string[] selections)
        : base(name, defaultValue, tab, isSingleValue)
        {
            Rule = (0, selections.Length - 1, 1);
            Selections = selections;
        }
        public static StringOptionItem Create(
            string name, string[] selections, int defaultIndex, TabGroup tab, bool isSingleValue
        )
        {
            return new StringOptionItem(
                name, defaultIndex, tab, isSingleValue, selections
            );
        }
        public static StringOptionItem Create(
            Enum name, string[] selections, int defaultIndex, TabGroup tab, bool isSingleValue
        )
        {
            return new StringOptionItem(
                name.ToString(), defaultIndex, tab, isSingleValue, selections
            );
        }
        public static StringOptionItem Create(
             SimpleRoleInfo roleInfo, Enum name, string[] selections, int defaultIndex, bool isSingleValue, OptionItem parent = null
        )
        {
            var opt = new StringOptionItem(
                name.ToString(), defaultIndex, roleInfo.Tab, isSingleValue, selections
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }
        public static StringOptionItem Create(
             SimpleRoleInfo roleInfo, string name, string[] selections, int defaultIndex, bool isSingleValue, OptionItem parent = null
        )
        {
            var opt = new StringOptionItem(
                name, defaultIndex, roleInfo.Tab, isSingleValue, selections
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }

        // Getter
        public override int GetInt() => Rule.GetValueByIndex(CurrentValue);
        public override float GetFloat() => Rule.GetValueByIndex(CurrentValue);
        public override string GetString()
        {
            return Translator.GetString(Selections[Rule.GetValueByIndex(CurrentValue)]);
        }
        public int GetChance()
        {
            //0%or100%の場合
            if (Selections.Length == 2) return CurrentValue * 100;

            //0%～100%or5%～100%の場合
            var offset = 12 - Selections.Length;
            var index = CurrentValue + offset;
            var rate = index <= 1 ? index * 5 : (index - 1) * 10;
            return rate;
        }
        public override int GetValue()
            => Rule.RepeatIndex(base.GetValue());

        // Setter
        public override void SetValue(int value, bool doSync = true)
        {
            base.SetValue(Rule.RepeatIndex(value), doSync);
        }
    }
}