using UnityEngine;
using AmongUs.GameOptions;
using Hazel;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using System.Collections.Generic;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

class Penguin : RoleBase, IImpostor
{
    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Penguin),
            player => new Penguin(player),
            CustomRoles.Penguin,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 6000,
            SetupOptionItem,
            "pe"
        );
    public Penguin(PlayerControl player)
        : base(RoleInfo, player)
    {
        AbductTimerLimit = OptionAbductTimerLimit.GetFloat();
        MeetingKill = OptionMeetingKill.GetBool();

        CustomRoleManager.OnEnterVentOthers.Add(OnEnterVentOthers);
    }
    public override void OnDestroy()
    {
        Penguins = new();
        AbductVictim = null;
    }

    static OptionItem OptionAbductTimerLimit;
    static OptionItem OptionMeetingKill;

    enum OptionName
    {
        PenguinAbductTimerLimit,
        PenguinMeetingKill,
    }

    private PlayerControl AbductVictim;
    public PlayerControl DragVictim => AbductVictim;
    private float AbductTimer;
    private float AbductTimerLimit;
    private bool stopCount;
    private bool MeetingKill;
    static HashSet<Penguin> Penguins = new();

    //拉致中にキルしそうになった相手の能力を使わせないための処置
    public bool IsKiller => AbductVictim == null;
    public static void SetupOptionItem()
    {
        OptionAbductTimerLimit = FloatOptionItem.Create(RoleInfo, OptionName.PenguinAbductTimerLimit, new(5f, 100f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMeetingKill = BooleanOptionItem.Create(RoleInfo, OptionName.PenguinMeetingKill, false, false);
    }
    public override void Add()
    {
        AbductTimer = 255f;
        stopCount = false;
        oldpos = Vector2.zero;
        movecount = 0;
        Penguins.Add(this);
    }
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.ShapeshifterCooldown = AbductVictim != null ? AbductTimer : 255f;
    private void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write(AbductVictim?.PlayerId ?? 255);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        var victim = reader.ReadByte();
        if (victim == 255)
        {
            AbductVictim = null;
            AbductTimer = 255f;
        }
        else
        {
            AbductVictim = Utils.GetPlayerById(victim);
            AbductTimer = AbductTimerLimit;
        }
    }
    void AddVictim(PlayerControl target)
    {
        PlayerState.GetByPlayerId(target.PlayerId).CanUseMovingPlatform = MyState.CanUseMovingPlatform = false;
        CheckMurderPatch.TimeSinceLastKill[Player.PlayerId] = 0f;
        AbductVictim = target;
        AbductTimer = AbductTimerLimit;
        Player.RpcResetAbilityCooldown(sync: true);
        SendRPC();
        movecount = 0;
        oldpos = Player.GetTruePosition();
        lastTpPos = new(float.MaxValue, float.MaxValue);
        nextTpAt = 0f;
        TownOfHostL.Modules.PlayerSpeed.Freeze(target.PlayerId, "PenguinVictim");
        target.MarkDirtySettings();
    }
    void RemoveVictim()
    {
        if (AbductVictim != null)
        {
            PlayerState.GetByPlayerId(AbductVictim.PlayerId).CanUseMovingPlatform = true;
            TownOfHostL.Modules.PlayerSpeed.Remove(AbductVictim.PlayerId, "PenguinVictim");
            AbductVictim.MarkDirtySettings();
            AbductVictim = null;
        }
        MyState.CanUseMovingPlatform = true;
        AbductTimer = 255f;
        Player.RpcResetAbilityCooldown(sync: true);
        SendRPC();
        movecount = 0;
    }
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var target = info.AttemptTarget;
        if (AbductVictim != null)
        {
            if (target != AbductVictim)
            {
                //拉致中は拉致相手しか切れない
                Player.RpcMurderPlayer(AbductVictim);
                Player.ResetKillCooldown();
                info.DoKill = false;
            }
            RemoveVictim();
        }
        else
        {
            if (info.CheckHasGuard())
            {
                info.IsGuard = false;
                return;
            }
            info.DoKill = false;
            AddVictim(target);
        }
    }
    public bool OverrideKillButtonText(out string text)
    {
        if (AbductVictim != null)
        {
            text = GetString("KillButtonText");
        }
        else
        {
            text = GetString("PenguinKillButtonText");
        }
        return true;
    }
    public override string GetAbilityButtonText()
    {
        return GetString("PenguinTimerText");
    }
    public override bool CanUseAbilityButton()
    {
        return AbductVictim != null;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        stopCount = true;
        // 時間切れ状態で会議を迎えたらはしご中でも構わずキルする
        if (AbductVictim != null && AbductTimer <= 0f)
        {
            Player.RpcMurderPlayer(AbductVictim);
        }
        if (MeetingKill)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AbductVictim == null) return;
            Player.RpcMurderPlayer(AbductVictim);
            RemoveVictim();
        }
    }
    public override void OnSpawn(bool initialState)
    {
        RestartAbduct();
    }
    public void RestartAbduct()
    {
        if (AbductVictim != null)
        {
            stopCount = false;
            nextTpAt = 0f;
        }
    }
    // 連れ回しの TP は 0.2 秒以上あけ、前に送った位置から 0.3 以上動いた時だけ送る (止まっている間も送り続けると SnapTo の予算が尽きる)
    const float TpInterval = 0.2f, TpMinMove = 0.3f;
    float nextTpAt;
    Vector2 lastTpPos = new(float.MaxValue, float.MaxValue);
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;

        if (!stopCount)
            AbductTimer -= Time.fixedDeltaTime;

        if (AbductVictim != null)
        {
            if (!Player.IsAlive() || !AbductVictim.IsAlive())
            {
                RemoveVictim();
                return;
            }
            if (AbductTimer <= 0f && !Player.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
            {
                // 先にIsDeadをtrueにする(はしごチェイス封じ)
                AbductVictim.Data.IsDead = true;
                GameData.Instance.DirtyAllData();
                // ペンギン自身がはしご上にいる場合，はしごを降りてからキルする
                if (!AbductVictim.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
                {
                    var abductVictim = AbductVictim;
                    _ = new LateTask(() =>
                    {
                        var sId = abductVictim.NetTransform.lastSequenceId + 5;
                        abductVictim.NetTransform.SnapTo(Player.transform.position, (ushort)sId);
                        Player.MurderPlayer(abductVictim);

                        var sender = CustomRpcSender.Create("PenguinMurder");
                        {
                            sender.AutoStartRpc(abductVictim.NetTransform.NetId, (byte)RpcCalls.SnapTo);
                            {
                                NetHelpers.WriteVector2(Player.transform.position, sender.stream);
                                sender.Write(abductVictim.NetTransform.lastSequenceId);
                            }
                            sender.EndRpc();
                            sender.AutoStartRpc(Player.NetId, (byte)RpcCalls.MurderPlayer);
                            {
                                sender.WriteNetObject(abductVictim);
                                sender.Write((int)ExtendedPlayerControl.SucceededFlags);
                            }
                            sender.EndRpc();
                        }
                        sender.SendMessage();
                    }, 0.3f, "PenguinMurder");
                    RemoveVictim();
                }
            }
            // はしごの上にいるプレイヤーにはSnapToRPCが効かずホストだけ挙動が変わるため，一律でテレポートを行わない
            else if (!AbductVictim.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
            {
                var position = (Vector2)Player.transform.position;
                if (Time.time >= nextTpAt && Vector2.Distance(position, lastTpPos) >= TpMinMove)
                {
                    nextTpAt = Time.time + TpInterval;
                    lastTpPos = position;
                    movecount += Vector2.Distance(position, oldpos);
                    oldpos = position;
                    if (Player.PlayerId != 0)
                    {
                        AbductVictim.RpcSnapTo(position);
                    }
                    else
                    {
                        _ = new LateTask(() =>
                        {
                            if (AbductVictim != null)
                            {
                                AbductVictim.RpcSnapTo(position);
                            }
                        }
                        , 0.25f, "");
                    }
                }
            }
        }
        else if (AbductTimer <= 100f)
        {
            AbductTimer = 255f;
            Player.RpcResetAbilityCooldown(sync: true);
        }
    }
    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        if (AbductVictim != null)
        {
        }
    }
    public static bool OnEnterVentOthers(PlayerPhysics physics, int ventId)
    {
        var user = physics.myPlayer;
        foreach (var Penguin in Penguins)
        {
            if (Penguin.AbductVictim != null)
            {
                if (Penguin.AbductVictim.PlayerId == user.PlayerId)
                {
                    _ = new LateTask(() =>
                    {
                        if (user.inVent && user.IsAlive())
                            physics.RpcBootFromVent(ventId);
                    }, 0.8f, "PenginTargetnonvent");
                    return true;
                }
            }
        }
        return true;
    }
    Vector2 oldpos;
    float movecount;
}
