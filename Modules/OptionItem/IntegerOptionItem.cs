using System;
using TownOfHostL.Roles.Core;

namespace TownOfHostL
{
    public class IntegerOptionItem : OptionItem
    {
        // 必須情報
        public IntegerValueRule Rule;

        // コンストラクタ
        public IntegerOptionItem(string name, int defaultValue, TabGroup tab, bool isSingleValue, IntegerValueRule rule)
        : base(name, rule.GetNearestIndex(defaultValue), tab, isSingleValue)
        {
            Rule = rule;
        }
        public static IntegerOptionItem Create(
            string name, IntegerValueRule rule, int defaultValue, TabGroup tab, bool isSingleValue
        )
        {
            return new IntegerOptionItem(
                name, defaultValue, tab, isSingleValue, rule
            );
        }
        public static IntegerOptionItem Create(
            Enum name, IntegerValueRule rule, int defaultValue, TabGroup tab, bool isSingleValue
        )
        {
            return new IntegerOptionItem(
                name.ToString(), defaultValue, tab, isSingleValue, rule
            );
        }
        public static IntegerOptionItem Create(
            SimpleRoleInfo roleInfo, Enum name, IntegerValueRule rule, int defaultValue, bool isSingleValue, OptionItem parent = null
        )
        {
            var opt = new IntegerOptionItem(
                name.ToString(), defaultValue, roleInfo.Tab, isSingleValue, rule
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }
        public static IntegerOptionItem Create(
            SimpleRoleInfo roleInfo, string name, IntegerValueRule rule, int defaultValue, bool isSingleValue, OptionItem parent = null
        )
        {
            var opt = new IntegerOptionItem(
                name, defaultValue, roleInfo.Tab, isSingleValue, rule
            );
            opt.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }

        // Getter
        public override int GetInt() => Rule.GetValueByIndex(CurrentValue);
        public override float GetFloat() => Rule.GetValueByIndex(CurrentValue);
        public override string GetString()
        {
            return ApplyFormat(Rule.GetValueByIndex(CurrentValue).ToString());
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