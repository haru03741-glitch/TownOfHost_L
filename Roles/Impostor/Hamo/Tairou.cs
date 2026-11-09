using System.Linq;
using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor
{
    public sealed class Tairou : RoleBase, IImpostor
    {
        // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
            typeof(Tairou),
            player => new Tairou(player),
            CustomRoles.Tairou,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 7400,
            SetupOptionItem,
            "t"
        );
        public Tairou(PlayerControl player)
            : base(
                RoleInfo,
                player
            )
        {
            TairoDeathReason = OptionTairoDeathReason.GetBool();
            TairouNotify = OptionTairouNotify.GetBool();
        }
        public static OptionItem OptionTairoDeathReason;
        public static OptionItem OptionTairouNotify;
        public static OptionItem OptionTellResult;
        CustomRoles? disguise;
        enum OptionName
        {
            TairoDeathReason,
            TairouNotify,
            TairouTellResult
        }
        public static bool TairoDeathReason;
        public static bool TairouNotify;
        private static void SetupOptionItem()
        {
            OptionTairoDeathReason = BooleanOptionItem.Create(RoleInfo, OptionName.TairoDeathReason, true, false);
            OptionTairouNotify = BooleanOptionItem.Create(RoleInfo, OptionName.TairouNotify, true, false);
            OptionTellResult = StringOptionItem.Create(RoleInfo, OptionName.TairouTellResult, new[] { "TellResultCrewmate", "TellResultRandomRole" }, 0, false);
        }
        public override CustomRoles TellResults(PlayerControl player)
        {
            return disguise ??= PickDisguise(OptionTellResult.GetValue() == 1);
        }
        /// <summary>占いで見せる役職。random なら、この試合で出るクルーの役職から 1 つ (無ければクルーメイト)</summary>
        static CustomRoles PickDisguise(bool random)
        {
            if (!random) return CustomRoles.Crewmate;
            var roles = Options.CustomRoleCounts.Keys
                .Where(r => r is not CustomRoles.Tairou && r.GetRoleInfo() != null
                    && r.GetCustomRoleTypes() == CustomRoleTypes.Crewmate && r.IsEnable())
                .ToArray();
            return roles.Length == 0 ? CustomRoles.Crewmate : roles[IRandom.Instance.Next(roles.Length)];
        }
        public override bool OnCheckMurderAsTarget(MurderInfo info)
        {
            return true;
        }
        public override string MeetingAddMessage()
        {
            if (Player.IsAlive() && TairouNotify)
            {
                string TairouTitle = $"<size=90%><color=#ff0000>{GetString("Message.TairouTitle")}</size></color>";
                return TairouTitle + "\n<size=70%>" + GetString("Message.TairouAlive") + "</size>\n";
            }
            return "";
        }
}
}