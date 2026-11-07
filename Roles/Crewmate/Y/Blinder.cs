using System.Collections.Generic;
using AmongUs.GameOptions;
using TownOfHostL.Attributes;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Crewmate;
public sealed class Blinder : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Blinder),
            player => new Blinder(player),
            CustomRoles.Blinder,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 800,
            SetupOptionItem,
            "ブラインダー",
            "#883fd1"
        );
    public Blinder(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        BlinderVision = OptionBlinderVision.GetFloat();
    }

    // 2 人目のブラインダーや途中でなった人の分で、他のブラインダーが付けた目隠しを消さないよう試合の始めにだけ空にする
    [GameModuleInitializer]
    public static void Init() => BlindPlayerIdList.Clear();

    private static OptionItem OptionBlinderVision;
    enum OptionName
    {
        BlinderVision,
    }

    public static float BlinderVision;
    // ブラインダー全体で管理するリスト
    public static readonly HashSet<byte> BlindPlayerIdList = new();
    // ブラインダー個人で見た斬られた対象
    PlayerControl blindPlayer;

    private static void SetupOptionItem()
    {
        OptionBlinderVision = FloatOptionItem.Create(RoleInfo, 10, OptionName.BlinderVision, new(0f, 5f, 0.05f), 0.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }
    public override void Add()
    {
        blindPlayer = null;
    }
    public override void OnDestroy()
    {
        if (blindPlayer == null) return;

        BlindPlayerIdList.Remove(blindPlayer.PlayerId);
        blindPlayer.MarkDirtySettings();

        blindPlayer = null;
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetFloat(FloatOptionNames.CrewLightMod, BlinderVision);
    }
    public static void ApplyGameOptionsByOther(byte id, IGameOptions opt)
    {
        if (!CustomRoles.Blinder.IsPresent()) return;

        if (BlindPlayerIdList.Contains(id))
        {
            opt.SetFloat(FloatOptionNames.CrewLightMod, BlinderVision);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, BlinderVision);
            opt.SetVision(false);
        }
    }

    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;

        blindPlayer = killer;
        BlindPlayerIdList.Add(killer.PlayerId);
        killer.MarkDirtySettings();
    }
}