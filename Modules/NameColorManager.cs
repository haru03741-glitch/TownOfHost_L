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
            // 会議で色を付けない名前は白と書いて送る。何も書かないと、本編の客の画面では
            // 自分と相手の本編の役職 (ずらした役職や、試合の始めの途中の役職) でインポスター同士と見て赤くなる
            if (isMeeting && (seer.GetRoleClass()?.HidesNameColorInMeeting(target) ?? false)) return PlainMeetingName(name);
            if (Options.IsSyncColorMode && isMeeting) return PlainMeetingName(name);

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
            else if (isMeeting) return PlainMeetingName(name);
            return openTag + name + closeTag;
        }
        /// <summary>会議の名前を白で囲う (本編の客の画面が勝手に赤くしないように)</summary>
        public static string PlainMeetingName(string name) => $"<color=#ffffff>{name}</color>";
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
        /// <summary>付けた色をそのまま返す (空なら「見られる人の今の役職の色」)。色が付いていなければ false</summary>
        public static bool TryGetCode(byte seerId, byte targetId, out string colorCode)
            => PlayerState.GetByPlayerId(seerId).TargetColorData.TryGetValue(targetId, out colorCode);
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