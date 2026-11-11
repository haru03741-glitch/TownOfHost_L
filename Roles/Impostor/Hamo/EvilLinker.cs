using System.Collections.Generic;

using System.Linq;

using AmongUs.GameOptions;

using Hazel;

using TownOfHostL.Modules;

using TownOfHostL.Patches;

using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;

using UnityEngine;



namespace TownOfHostL.Roles.Impostor;



public sealed class EvilLinker : RoleBase, IImpostor, IUsePhantomButton

{

    // 役職の元: TownOfHost_Pko (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(EvilLinker),
            player => new EvilLinker(player),
            CustomRoles.EvilLinker,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 126700,
            SetupOptionItem,
            "el"
        );



    public EvilLinker(PlayerControl player)

        : base(RoleInfo, player)

    {

        PlaceCooldown = OptionPlaceCooldown.GetFloat();

        MaxPairs = OptionMaxPairs.GetInt();

        WarpCooldown = OptionWarpCooldown.GetFloat();



        linkPairs = new();

        pendingDummy = null;

        placedCount = 0;

        cooldownTimer = PlaceCooldown;

        warpCooldowns = new();

    }



    static OptionItem OptionPlaceCooldown;

    static OptionItem OptionMaxPairs;

    static OptionItem OptionWarpCooldown;



    static float PlaceCooldown;

    static int MaxPairs;

    static float WarpCooldown;



    enum OptionName

    {

        EvilLinkerPlaceCooldown,

        EvilLinkerMaxPairs,

        EvilLinkerWarpCooldown,

    }



    static void SetupOptionItem()

    {

        OptionPlaceCooldown = FloatOptionItem.Create(RoleInfo, OptionName.EvilLinkerPlaceCooldown,

            new(2.5f, 60f, 2.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);

        OptionMaxPairs = IntegerOptionItem.Create(RoleInfo, OptionName.EvilLinkerMaxPairs,

            new(1, 10, 1), 3, false).SetValueFormat(OptionFormat.Times);

        OptionWarpCooldown = FloatOptionItem.Create(RoleInfo, OptionName.EvilLinkerWarpCooldown,

            new(1f, 60f, 1f), 10f, false).SetValueFormat(OptionFormat.Seconds);

    }



    // 今のキルクールを返すと、ほかの仕組みが足した分が積み重なり、分身を置くたびにキルクールが増えていた。基本のキルクールを返す
    public float CalculateKillCooldown() => Options.DefaultKillCooldown;

    public bool CanUseSabotageButton() => true;

    public bool CanUseImpostorVentButton() => true;

    bool IUsePhantomButton.IsPhantomRole => true;

    bool IUsePhantomButton.IsresetAfterKill => false;



    bool IUsePhantomButton.SyncAbilityCooldownWithKillCooldown => false;



    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)

    {

        AdjustKillCooldown = true;

        ResetCooldown = false;



        if (!Player.IsAlive()) return;

        if (cooldownTimer > 0f) return;



        if (!AmongUsClient.Instance.AmHost)

        {

            SendActionRpc();

            return;

        }



        PerformPlacePortal();

    }



    class LinkPair

    {

        public EvilLinkerDummy DummyA;

        public EvilLinkerDummy DummyB;

        public int ColorId;

        public bool Activated;

        public HashSet<byte> InRangeA = new();

        public HashSet<byte> InRangeB = new();

    }



    static readonly int[] PairColors = { 1, 11, 10, 2, 5, 4, 3, 14, 17, 8 };

    const int PendingColor = 7;



    readonly List<LinkPair> linkPairs;

    LinkPair pendingDummy;

    int placedCount;

    float cooldownTimer;

    readonly Dictionary<byte, float> warpCooldowns;



    public override void Add()

    {

        linkPairs.Clear();

        pendingDummy = null;

        placedCount = 0;

        cooldownTimer = PlaceCooldown;

        warpCooldowns.Clear();

        PetActionManager.Register(Player.PlayerId, OnPetAction);

    }



    public override void OnSpawn(bool initialState = false)

    {

        cooldownTimer = PlaceCooldown + 1.5f;

        Player.RpcResetAbilityCooldown(sync: true);

    }



    public override void OnDestroy()

    {

        PetActionManager.Unregister(Player.PlayerId);

    }



    public override void ApplyGameOptions(IGameOptions opt)

    {

        AURoleOptions.PhantomCooldown = cooldownTimer > 0f ? cooldownTimer : 0.1f;

    }



    void OnPetAction()

    {

        if (!Player.IsAlive()) return;

        if (cooldownTimer > 0f) return;



        if (!AmongUsClient.Instance.AmHost)

        {

            SendActionRpc();

            return;

        }



        PerformPlacePortal();

    }



    void PerformPlacePortal()

    {

        var pos = Player.transform.position;



        if (pendingDummy == null)

        {

            if (placedCount >= MaxPairs) return;



            int colorId = PairColors[placedCount % PairColors.Length];

            var pair = new LinkPair

            {

                ColorId = colorId,

                DummyA = new EvilLinkerDummy((Vector2)pos, Player, PendingColor, activated: false),

                Activated = false,

            };

            pendingDummy = pair;

            linkPairs.Add(pair);



            cooldownTimer = PlaceCooldown;

            Player.RpcResetAbilityCooldown(sync: true);

        }

        else

        {

            pendingDummy.DummyB = new EvilLinkerDummy((Vector2)pos, Player, PendingColor, activated: false);

            pendingDummy = null;

            placedCount++;



            cooldownTimer = PlaceCooldown;

            Player.RpcResetAbilityCooldown(sync: true);

        }



        SendRpc();

        Utils.NotifyRoles();

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (cooldownTimer > 0f)

        {

            cooldownTimer -= Time.fixedDeltaTime;

            if (cooldownTimer < 0f) cooldownTimer = 0f;

        }



        if (!AmongUsClient.Instance.AmHost) return;

        if (!GameStates.IsInTask || GameStates.IsMeeting) return;



        if (!Player.IsAlive() && linkPairs.Count > 0)

        {

            foreach (var pair in linkPairs.ToArray())

            {

                try { pair.DummyA?.Despawn(); } catch { }

                try { pair.DummyB?.Despawn(); } catch { }

            }

            linkPairs.Clear();

            pendingDummy = null;

            return;

        }



        foreach (var pid in warpCooldowns.Keys.ToArray())

        {

            warpCooldowns[pid] -= Time.fixedDeltaTime;

            if (warpCooldowns[pid] <= 0f)

                warpCooldowns.Remove(pid);

        }



        const float warpRange = 0.2f;



        foreach (var pair in linkPairs)

        {

            if (!pair.Activated) continue;

            if (pair.DummyA == null || pair.DummyB == null) continue;



            var nowInRangeA = new HashSet<byte>();

            var nowInRangeB = new HashSet<byte>();



            foreach (var pc in Main.AllAlivePlayerControls)

            {

                float distA = Vector2.Distance(pc.transform.position, pair.DummyA.Position);

                float distB = Vector2.Distance(pc.transform.position, pair.DummyB.Position);



                if (distA <= warpRange) nowInRangeA.Add(pc.PlayerId);

                if (distB <= warpRange) nowInRangeB.Add(pc.PlayerId);



                if (distA <= warpRange && !pair.InRangeA.Contains(pc.PlayerId)

                    && !warpCooldowns.ContainsKey(pc.PlayerId))

                {

                    pc.RpcSnapTo(pair.DummyB.Position);

                    warpCooldowns[pc.PlayerId] = WarpCooldown;

                    Utils.AddGameLog("EvilLinker",

                        $"{pc.ColoredName()} がポータルA→Bへワープ");

                }

                else if (distB <= warpRange && !pair.InRangeB.Contains(pc.PlayerId)

                    && !warpCooldowns.ContainsKey(pc.PlayerId))

                {

                    pc.RpcSnapTo(pair.DummyA.Position);

                    warpCooldowns[pc.PlayerId] = WarpCooldown;

                    Utils.AddGameLog("EvilLinker",

                        $"{pc.ColoredName()} がポータルB→Aへワープ");

                }

            }



            pair.InRangeA = nowInRangeA;

            pair.InRangeB = nowInRangeB;

        }

    }



    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        // 組がそろった物だけ、全員に見えるポータルに出し直す (置いた本人にだけ見せていた CNO は、ほかの客の手元から消してあるので作り直す)。
        // もう全員に見えている組と、片方だけの組は、CNO が会議をまたいで残るのでそのままにする
        for (int i = 0; i < linkPairs.Count; i++)
        {
            var pair = linkPairs[i];
            if (pair.Activated || pair.DummyA == null || pair.DummyB == null) continue;
            int idx = i;
            var colorId = pair.ColorId;
            var owner = Player;
            var oldA = pair.DummyA;
            var oldB = pair.DummyB;
            _ = new LateTask(() =>
            {
                // 待つ間に本人が死んだり組が消えたりしたら作らない
                if (!linkPairs.Contains(pair) || owner == null || !owner.IsAlive()) return;
                oldA.Despawn();
                oldB.Despawn();
                pair.DummyA = new EvilLinkerDummy(oldA.Position, owner, colorId, activated: true);
                pair.DummyB = new EvilLinkerDummy(oldB.Position, owner, colorId, activated: true);
                pair.Activated = true;
                pair.InRangeA.Clear();
                pair.InRangeB.Clear();
            }, idx * 0.6f + 1.0f, $"EvilLinker.Activate.{idx}");
        }

        cooldownTimer = PlaceCooldown;



        Player.MarkDirtySettings();

        Player.RpcResetAbilityCooldown(sync: true);

        Player.ResetKillCooldown();

        Player.SetKillCooldown(ForceProtect: true);



        foreach (var pair in linkPairs)

        {

            pair.InRangeA.Clear();

            pair.InRangeB.Clear();

        }



        SendRpc();

    }



    public override void OnStartMeeting()

    {

        warpCooldowns.Clear();

        foreach (var pair in linkPairs)

        {

            pair.InRangeA.Clear();

            pair.InRangeB.Clear();

        }

    }



    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)

    {

        warpCooldowns.Clear();

    }



    public override void OnMurderPlayerAsTarget(MurderInfo info)

    {

        foreach (var pair in linkPairs.ToArray())

        {

            try { pair.DummyA?.Despawn(); } catch { }

            try { pair.DummyB?.Despawn(); } catch { }

        }

        linkPairs.Clear();

        pendingDummy = null;

    }



    public override string GetProgressText(bool comms = false)

    {

        if (!Player.IsAlive()) return "";

        string pend = pendingDummy != null ? " <color=#ffff00>1/2</color>" : "";

        return $"<color={RoleInfo.RoleColorCode}>({placedCount}/{MaxPairs}組){pend}</color>";

    }



    void SendRpc()

    {

        if (!AmongUsClient.Instance.AmHost) return;

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write((byte)0);

        sender.Writer.Write(placedCount);

        sender.Writer.Write(cooldownTimer);

        sender.Writer.Write(pendingDummy != null);

    }



    void SendActionRpc()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write((byte)1);

    }



    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)

    {
        if (rpcType != CustomRPC.CustomRoleSync) return;

        byte kind = reader.ReadByte();



        if (kind == 0)

        {

            placedCount = reader.ReadInt32();

            cooldownTimer = reader.ReadSingle();

            reader.ReadBoolean();

        }

        else if (kind == 1)

        {

            if (AmongUsClient.Instance.AmHost)

                PerformPlacePortal();

        }

    }

}



// イビルリンカーのポータル。CNO の名前の絵で、組の色の扉を描く (体は同じ色で扉の下に隠れる)。
// 組がそろうまでは置いた本人の画面にだけ出す。
// 扉の形は Endless Host Roles (Gurge44, GPL-3.0) の CustomNetObject.Portal から (Gurge44/EndlessHostRoles@97aa6333)
public sealed class EvilLinkerDummy
{
    readonly TownOfHostL.Core.CustomNetObject cno;

    public Vector2 Position { get; }
    public bool Activated { get; }

    public EvilLinkerDummy(Vector2 position, PlayerControl owner, int colorId, bool activated)
    {
        Position = position;
        Activated = activated;
        cno = TownOfHostL.Core.CustomNetObject.Create(PortalSprite(colorId), position, (byte)colorId);
        if (cno == null) Logger.Warn($"ポータルの CNO を作れませんでした (試合の外) {position}", "EvilLinker");
        else if (!activated && owner != null) cno.ShowOnlyTo(owner.PlayerId);
    }

    public void Despawn() => cno?.Despawn();

    static string PortalSprite(int colorId)
    {
        var colors = Palette.PlayerColors;
        var shadows = Palette.ShadowColors;
        int i = colorId >= 0 && colorId < colors.Length ? colorId : 7;
        var frame = "#" + ColorUtility.ToHtmlStringRGB(shadows[i]);
        var door = "#" + ColorUtility.ToHtmlStringRGB(colors[i]);
        string edge = $"<mark={frame}>WWWW</mark>";
        string mid = $"<mark={frame}>W</mark><mark={door}>WW</mark><mark={frame}>W</mark>";
        return $"<size=70%><line-height=97%><cspace=0.16em>{edge}\n{mid}\n{mid}\n{mid}\n{mid}\n{edge}";
    }
}
