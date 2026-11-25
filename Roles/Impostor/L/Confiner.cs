using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// 閉じ込め屋: 変身のボタンで選んだ人がいる部屋のドアを、その人の画面でだけ決めた時間閉める。
// 周りの人の画面ではドアは開いたままなので、閉じ込められた人だけが出られない。
public sealed class Confiner : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Confiner),
            player => new Confiner(player),
            CustomRoles.Confiner,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 100,
            SetupOptionItem,
            "cnf"
        );

    public Confiner(PlayerControl player)
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
        ConfinerDuration,
    }

    private byte targetId = byte.MaxValue;
    // 閉めを解く時刻 (Time.time)
    private float releaseAt;
    // 閉められなかった時に、名前の下へ出す知らせを消す時刻
    private float failNoticeUntil;

    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, OptionName.ConfinerDuration, new(2f, 30f, 1f), 8f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = cooldown;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    public override string GetAbilityButtonText() => GetString("ConfinerButtonText");

    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        animate = false;
        if (!Player.IsAlive() || Is(target) || !target.IsAlive()) return false;

        var room = target.GetPlainShipRoom();
        var closed = 0;
        // ホスト本人の画面のドアは全員のドアそのものなので閉められない (PersonalDoors が 0 を返す)
        if (room != null)
        {
            Release();
            closed = PersonalDoors.CloseRoomFor(target, room.RoomId);
        }
        if (closed == 0)
        {
            Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: 閉められるドアが無い ({(room == null ? "部屋の外" : room.RoomId)})", nameof(Confiner));
            failNoticeUntil = Time.time + 3f;
            Utils.NotifyRoles(SpecifySeer: Player);
            return false;
        }

        targetId = target.PlayerId;
        releaseAt = Time.time + duration;
        Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: {room.RoomId} のドア {closed} 枚を {duration} 秒閉める", nameof(Confiner));
        Player.RpcResetAbilityCooldown();
        return false;
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (failNoticeUntil > 0f && Time.time >= failNoticeUntil)
        {
            failNoticeUntil = 0f;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
        if (targetId == byte.MaxValue) return;
        if (Time.time >= releaseAt || !Player.IsAlive()) Release();
    }

    // 会議の後に閉めた状態を持ち越さない
    public override void OnStartMeeting() => Release();

    public override void OnDestroy() => Release();

    private void Release()
    {
        if (targetId == byte.MaxValue) return;
        var target = Utils.GetPlayerById(targetId);
        targetId = byte.MaxValue;
        if (target != null) PersonalDoors.ClearFor(target);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != Player || seen != Player || failNoticeUntil <= 0f) return "";
        return Utils.ColorString(RoleInfo.RoleColor, GetString("ConfinerNoDoor"));
    }
}
