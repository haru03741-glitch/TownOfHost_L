using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// 神隠し: 変身のボタンで選んだ人を、一番近いベントに吸い込んで閉じ込め、決めた時間の後にランダムな別のベントから出す。
// 閉じ込められた人は周りから消え、ベントのボタンを持たなければ自分では出られない (VentCage)。
public sealed class Kamikakushi : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Kamikakushi),
            player => new Kamikakushi(player),
            CustomRoles.Kamikakushi,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 200,
            SetupOptionItem,
            "kmk"
        );

    public Kamikakushi(PlayerControl player)
    : base(RoleInfo, player)
    {
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
    }

    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static float cooldown;
    private static float duration;

    private enum OptionName
    {
        KamikakushiDuration,
    }

    // 吸い込めなかった時に、名前の下へ出す知らせを消す時刻
    private float failNoticeUntil;

    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, OptionName.KamikakushiDuration, new(2f, 30f, 1f), 8f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = cooldown;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    public override string GetAbilityButtonText() => GetString("KamikakushiButtonText");

    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        animate = false;
        if (!Player.IsAlive() || Is(target) || !target.IsAlive()) return false;

        if (!VentCage.Trap(target, duration))
        {
            // ベントの中・梯子の上などで吸い込めない時は、クールを使わずに知らせだけ出す
            var showing = failNoticeUntil > 0f;
            failNoticeUntil = Time.time + 3f;
            if (!showing) Utils.NotifyRoles(SpecifySeer: Player);
            return false;
        }
        Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: {duration} 秒ベントに閉じ込める", nameof(Kamikakushi));
        Player.RpcResetAbilityCooldown();
        return false;
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || failNoticeUntil <= 0f || Time.time < failNoticeUntil) return;
        failNoticeUntil = 0f;
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnStartMeeting() => failNoticeUntil = 0f;

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != Player || seen != Player || failNoticeUntil <= 0f) return "";
        return Utils.ColorString(RoleInfo.RoleColor, GetString("KamikakushiFailed"));
    }
}
