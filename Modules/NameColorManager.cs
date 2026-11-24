using Hazel;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL
{
    public static partial class NameColorManager
    {
        public static string ApplyNameColorData(this string name, PlayerControl seer, PlayerControl target, bool isMeeting)
        {
            if (!AmongUsClient.Instance.IsGameStarted) return name;
            if (isMeeting && (seer.GetRoleClass()?.HidesNameColorInMeeting(target) ?? false)) return name;
            if (Options.IsSyncColorMode && isMeeting) return name;

            if (!TryGetData(seer, target, out var colorCode))
            {
                if (KnowTargetRoleColor(seer, target, isMeeting))
                    colorCode = target.GetRoleColorCode();
            }
            // 役職に関わらない色の上書き (ジャニターの対象など)。登録はその試合の役職が行う
            colorCode = CustomRoleManager.ApplyNameColorOthers(seer, target, colorCode);

            string openTag = "", closeTag = "";
            if (colorCode != "")
            {
                if (!colorCode.StartsWith('#'))
                    colorCode = "#" + colorCode;
                openTag = $"<color={colorCode}>";
                closeTag = "</color>";
            }
            return openTag + name + closeTag;
        }
        private static bool KnowTargetRoleColor(PlayerControl seer, PlayerControl target, bool isMeeting)
        {
            return TownOfHostL.Core.NativeObject.Same(seer, target)
                || target.GetCustomRole().IsSpectator()
                || (seer.Is(CustomRoleTypes.Impostor) && target.Is(CustomRoleTypes.Impostor) && !seer.GetCustomRole().IsLoneImpostor() && !target.GetCustomRole().IsLoneImpostor())
                || (target.GetRoleClass()?.ShowsRoleColorToAll(isMeeting) ?? false)
                || HamoMadmate.SeesImpostor(seer, target)
                || (seer.GetRoleClass()?.KnowsRoleColorOf(target, isMeeting) ?? false);
        }
        public static bool TryGetData(PlayerControl seer, PlayerControl target, out string colorCode)
        {
            colorCode = "";
            var state = PlayerState.GetByPlayerId(seer.PlayerId);
            if (!state.TargetColorData.TryGetValue(target.PlayerId, out var value)) return false;
            colorCode = value;
            if (colorCode == "") colorCode = target.GetRoleColorCode();
            return true;
        }

        public static void Add(byte seerId, byte targetId, string colorCode = "")
        {
            var state = PlayerState.GetByPlayerId(seerId);
            if (state.TargetColorData.TryGetValue(targetId, out var value))
            {
                if (colorCode == value) return;
                Logger.Info($"SameKeyAdd seerId: {seerId}, targetId: {targetId}, colorCode: {colorCode},", "NameColorManager");
            }
            state.TargetColorData[targetId] = colorCode;
        }
        public static void Remove(byte seerId, byte targetId)
        {
            var state = PlayerState.GetByPlayerId(seerId);
            if (!state.TargetColorData.ContainsKey(targetId)) return;
            state.TargetColorData.Remove(targetId);
        }
        public static void RemoveAll(byte seerId)
        {
            PlayerState.GetByPlayerId(seerId).TargetColorData.Clear();
        }
    }
}