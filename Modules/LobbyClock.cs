using System;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>ロビーの右上の表示に足す、今日の日付と時刻の行。字は分が変わった時だけ組み直す</summary>
public static class LobbyClock
{
    private static string text = "";
    private static int shownMinute = -1;

    public static string Text
    {
        get
        {
            var now = DateTime.Now;
            int minute = now.Hour * 60 + now.Minute;
            if (minute == shownMinute) return text;
            shownMinute = minute;
            var days = GetString("LobbyClock.Weekdays").Split(',');
            string day = days.Length == 7 ? days[(int)now.DayOfWeek] : now.DayOfWeek.ToString()[..3];
            text = $"{now.Month}/{now.Day}({day}) {now:HH:mm}";
            return text;
        }
    }
}
