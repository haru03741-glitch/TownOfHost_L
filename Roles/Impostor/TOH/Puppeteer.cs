using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Hazel;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;
public sealed class Puppeteer : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Puppeteer),
            player => new Puppeteer(player),
            CustomRoles.Puppeteer,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpTOH + 1000,
            null,
            "パペッティア"
        );
    public Puppeteer(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {

        CustomRoleManager.OnFixedUpdateOthers.Add(OnFixedUpdateOthers);
    }
    /// <summary>
    /// Key: ターゲットのPlayerId, Value: パペッティア
    /// </summary>
    private static Dictionary<byte, Puppeteer> Puppets = new(15);
    public override bool NeedsSeenNames => true;

    // 同じ役職が複数いる時に、1 人の作成や破棄で他の持ち主の分まで消さないよう、共有の一覧は試合の開始で空にし、破棄では自分の分だけ外す
    [TownOfHostL.Attributes.GameModuleInitializer]
    public static void Init() => Puppets.Clear();
    public override void OnDestroy()
    {
        foreach (var targetId in Puppets.Where(kv => kv.Value == this).Select(kv => kv.Key).ToArray())
            Puppets.Remove(targetId);
    }

    private void SendRPC(byte targetId, byte typeId)
    {
        using var sender = CreateSender(CustomRPC.SyncPuppet);

        sender.Writer.Write(typeId);
        sender.Writer.Write(targetId);
    }
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.SyncPuppet) return;

        var typeId = reader.ReadByte();
        var targetId = reader.ReadByte();

        switch (typeId)
        {
            case 0: //Dictionaryのクリア
                Puppets.Clear();
                break;
            case 1: //Dictionaryに追加
                Puppets[targetId] = this;
                break;
            case 2: //DictionaryのKey削除
                Puppets.Remove(targetId);
                break;
        }
    }
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!info.CanKill) return; //キル出来ない相手には無効
        var (puppeteer, target) = info.AttemptTuple;

        Puppets[target.PlayerId] = this;
        SendRPC(target.PlayerId, 1);
        puppeteer.SetKillCooldown();
        Utils.NotifyRoles(SpecifySeer: puppeteer);
        info.DoKill = false;
    }
    public override void OnReportDeadBody(PlayerControl _, NetworkedPlayerInfo __)
    {
        Puppets.Clear();
        SendRPC(byte.MaxValue, 0);
    }
    public static void OnFixedUpdateOthers(PlayerControl puppet)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (Puppets.TryGetValue(puppet.PlayerId, out var puppeteer))
            puppeteer.CheckPuppetKill(puppet);
    }
    private void CheckPuppetKill(PlayerControl puppet)
    {
        if (!puppet.IsAlive())
        {
            Puppets.Remove(puppet.PlayerId);
            SendRPC(puppet.PlayerId, 2);
        }
        else
        {
            var puppetPos = puppet.transform.position;//puppetの位置
            Dictionary<PlayerControl, float> targetDistance = new();
            foreach (var pc in Main.AllAlivePlayerControls.ToArray())
            {
                if (pc.PlayerId != puppet.PlayerId && !pc.Is(CountTypes.Impostor))
                {
                    var dis = Vector2.Distance(puppetPos, pc.transform.position);
                    targetDistance.Add(pc, dis);
                }
            }
            if (targetDistance.Keys.Count <= 0) return;

            var min = targetDistance.OrderBy(c => c.Value).FirstOrDefault();//一番値が小さい
            var target = min.Key;
            var KillRange = GameManager.Instance.LogicOptions.GetKillDistance();
            if (min.Value <= KillRange && puppet.CanMove && target.CanMove)
            {
                RPC.PlaySoundRPC(Player.PlayerId, Sounds.KillSound);
                target.SetRealKiller(Player);
                puppet.RpcMurderPlayer(target);
                Utils.MarkEveryoneDirtySettings();
                Puppets.Remove(puppet.PlayerId);
                SendRPC(puppet.PlayerId, 2);
                Utils.NotifyRoles();
            }
        }
    }
    public override string GetMark(PlayerControl seer, PlayerControl seen, bool _ = false)
    {
        //seenが省略の場合seer
        seen ??= seer;

        if (!(Puppets.ContainsValue(this) &&
            Puppets.ContainsKey(seen.PlayerId))) return "";

        return Utils.ColorString(RoleInfo.RoleColor, "◆");
    }
    public bool OverrideKillButtonText(out string text)
    {
        text = GetString("PuppeteerOperateButtonText");
        return true;
    }
}