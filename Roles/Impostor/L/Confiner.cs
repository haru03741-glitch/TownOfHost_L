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
        cannotOpenByTask = OptionCannotOpenByTask.GetBool();
    }

    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionCannotOpenByTask;
    private static float cooldown;
    private static float duration;
    private static bool cannotOpenByTask;

    private enum OptionName
    {
        ConfinerDuration,
        ConfinerCannotOpenByTask,
    }

    private byte targetId = byte.MaxValue;
    private SystemTypes targetRoom;
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
        OptionCannotOpenByTask = BooleanOptionItem.Create(RoleInfo, OptionName.ConfinerCannotOpenByTask, false, false);
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
        if (room != null && target.PlayerId == targetId && room.RoomId == targetRoom)
        {
            // 同じ人を同じ部屋で選び直した時は、時間だけ延ばす (開けて閉め直すと客の画面でドアが一瞬開く)
            releaseAt = Time.time + duration;
            Player.RpcResetAbilityCooldown();
            return false;
        }
        var previous = targetId;
        // 同じ人の別の部屋なら、前の部屋のドアを先に戻す。別の人なら、新しい人を閉めてから前の人を戻す
        if (room != null && previous == target.PlayerId) Release();
        // ホスト本人の画面のドアは全員のドアそのものなので閉められない (PersonalDoors が 0 を返す)
        var closed = room == null ? 0 : PersonalDoors.CloseRoomFor(target, room.RoomId, cannotOpenByTask);
        if (closed == 0)
        {
            Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: 閉められるドアが無い ({(room == null ? "部屋の外" : room.RoomId)})", nameof(Confiner));
            // 連打された時に、知らせを出している間は名前の送り直しを重ねない
            var showing = failNoticeUntil > 0f;
            failNoticeUntil = Time.time + 3f;
            if (!showing) Utils.NotifyRoles(SpecifySeer: Player);
            return false;
        }
        if (previous != target.PlayerId) Release();

        targetId = target.PlayerId;
        targetRoom = room.RoomId;
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
    public override void OnStartMeeting()
    {
        failNoticeUntil = 0f;
        Release();
    }

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
