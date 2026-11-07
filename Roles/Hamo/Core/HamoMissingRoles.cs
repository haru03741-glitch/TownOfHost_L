using System.Linq;
using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

// ================================================================
// hamo にあって TOH_L に持ってきていない役職・属性への参照の受け皿。
// その役職がいないので、いつも「いない時」の答えを返す。
// ================================================================

namespace TownOfHostL
{
    /// <summary>hamo の地蔵 (持ってきていない)。部屋の記録は何もしない</summary>
    public static class Jizo
    {
        public static bool BomKilled;
        public static void Checkroom(PlainShipRoom room, PlayerControl player) { }
        public static void BomCheckroom(PlainShipRoom room, PlayerControl player) { }
        public static void BomClear() { BomKilled = false; }
    }
    /// <summary>hamo の特別会議 (持ってきていない)</summary>
    public static class SatsumatoImo
    {
        public static bool IsSpecialMeetingNoSwap() => false;
        public static bool CanSeeImpostorNameColor(CustomRoles role) => false;
    }
    public static class HamoViper
    {
        /// <summary>ヴァイパーで溶けるまでの秒数 (本編の設定)</summary>
        public static float ViperDissolveTime
        {
            get
            {
                try { return Main.NormalOptions.GetFloat(FloatOptionNames.ViperDissolveTime); }
                catch { return 15f; }
            }
        }
    }
}

namespace TownOfHostL.Roles.AddOns.Common
{
    /// <summary>hamo の記憶喪失 (持ってきていない)。能力はいつも使える</summary>
    public static class Amnesia
    {
        public static bool CheckAbility(PlayerControl player) => true;
        public static bool CheckAbilityreturn(PlayerControl player) => false;
    }
}

namespace TownOfHostL.Roles.Hamo.Crewmate
{
    /// <summary>hamo のオオカミ少年 (持ってきていない) の、見え方の役職を選ぶ所だけ</summary>
    public static class WolfBoy
    {
        public static bool SeenAsImpostor => true;
        public static CustomRoles PickDisguise(bool random, CustomRoleTypes type, CustomRoles pure)
        {
            if (!random) return pure;
            var roles = Options.CustomRoleCounts.Keys
                .Where(r => r is not CustomRoles.Tairou && r.GetRoleInfo() != null
                    && r.GetCustomRoleTypes() == type && r.IsEnable())
                .ToArray();
            return roles.Length == 0 ? pure : roles[IRandom.Instance.Next(roles.Length)];
        }
    }
}
