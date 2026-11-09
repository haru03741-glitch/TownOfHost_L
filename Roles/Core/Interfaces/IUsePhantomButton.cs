using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>
/// ワンクリックファントムボタンを使う役職
/// </summary>
public interface IUsePhantomButton
{
    public static Dictionary<byte, float> IPPlayerKillCooldown = new();
    public void Init(PlayerControl player)
    {
        if (!IPPlayerKillCooldown.TryAdd(player.PlayerId, 0))
        {
            IPPlayerKillCooldown[player.PlayerId] = 0;
            //Logger.Info($"{player.Data.PlayerName}ファントムワンクリに追加済みなのでリセット", "IusePhantomButton");
            return;
        }
        Logger.Info($"{player.Data?.PlayerName}:ファントムワンクリに追加", "IusePhantomButton");
    }
    //キルクールを...
    public void FixedUpdate(PlayerControl player)
    {
        if (!player.IsAlive()) return;
        if (player.GetRoleClass() is IUsePhantomButton)
            if (!GameStates.Intro && GameStates.InGame && GameStates.IsInTask && !GameStates.IsMeeting)
            {
                if (player.inVent) return;
                if (IPPlayerKillCooldown.TryGetValue(player.PlayerId, out var now))
                {
                    var killcool = now + Time.fixedDeltaTime;
                    IPPlayerKillCooldown[player.PlayerId] = killcool;
                }
                else Init(player);
            }
    }

    public static float GetRemainingKillCooldown(PlayerControl player)
    {
        IPPlayerKillCooldown.TryGetValue(player.PlayerId, out var turnTimer);
        Main.AllPlayerKillCooldown.TryGetValue(player.PlayerId, out var killCooldown);

        if (MeetingStates.FirstMeeting
            && !Options.FixFirstKillCooldown.GetBool()
            && killCooldown > 10f
            && PlayerState.GetByPlayerId(player.PlayerId)?.Is10secKillButton == true)
            killCooldown = 10f;

        var cooldown = killCooldown - turnTimer;
        return cooldown <= 1f ? 0.005f : cooldown;
    }

    public void CheckOnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        if (!UseOneclickButton)
        {
            AdjustKillCooldown = true;
            return;
        }
        OnClick(ref AdjustKillCooldown, ref ResetCooldown);
    }
    /// <summary>
    /// ファントムワンクリックを使った時に呼ばれる関数<br/>
    /// クールダウンのリセットが発動後行われる。<br/><br/>
    /// AdjustKillCooldownがtureでキルクールの調整が行われる<br/>
    /// ↑ 役職で使用後キルクールダウンをリセットする時はfalse<br/><br/>
    /// ResetCooldownがtrueでファントムボタンのクールリセットを入れる<br/>
    /// ↑ falseでクール無し<br/><br/>
    /// [ホストのみ]
    ///  </summary>
    /// <param name="AdjustKillCooldown">trueで使用後キルクールの調整処理を行う</param>
    /// <param name="ResetCooldown">trueでアビリティリセット処理を入れる<br/> nullでファントム戻し処理もいれない</param>
    public abstract void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown);
    /// <summary>ワンクリックボタンが使えるか</summary>
    public bool UseOneclickButton => true;
    /// <summary>ファントム置き換えにするかどうか</summary>
    public bool IsPhantomRole => true;

    /// <summary>キル後、クールダウンをリセットするか</summary>
    public bool IsresetAfterKill => true;

    /// <summary>
    /// ファントムボタン使用時、能力クールダウンを現在のキルクールダウンへ同期するか。
    /// </summary>
    public bool SyncAbilityCooldownWithKillCooldown => false;

    /// <summary>
    /// 共通ファントム処理で算出した現在のキルクールダウンを役職へ通知する。
    /// </summary>
    public void SetSyncedAbilityCooldown(float cooldown) { }
}

/// <summary>ワンクリックファントムボタンの押下の処理 (ホストのみ)</summary>
public static class PhantomButton
{
    /// <summary>ファントムのボタン (ワンクリック) を他の人が押した時 (ホスト)。false でバニラの透明化を止める</summary>
    public static bool CheckVanish(PlayerControl player, IUsePhantomButton pb)
    {
        if (player.PlayerId == PlayerControl.LocalPlayer.PlayerId) return false;
        var AdjustKillCooldown = true;
        bool? ResetCooldown = true;
        pb.CheckOnClick(ref AdjustKillCooldown, ref ResetCooldown);

        float cooldown = IUsePhantomButton.GetRemainingKillCooldown(player);
        if (pb.SyncAbilityCooldownWithKillCooldown) pb.SetSyncedAbilityCooldown(cooldown);
        Logger.Info($"Use:{player.GetNameWithRole()}", "PhantomButton");
        if (AdjustKillCooldown)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = cooldown < 10 ? cooldown : cooldown * 2;
            player.SyncSettings();
        }
        var writer = CustomRpcSender.Create("Phantom OneClick", SendOption.None);
        writer.StartMessage(player.GetClientId());
        if (ResetCooldown is not null)
        {
            writer.StartRpc(player.NetId, (byte)RpcCalls.SetRole)
                .Write((ushort)RoleTypes.Impostor)
                .Write(true)
                .EndRpc();
            if (pb.IsPhantomRole)
            {
                writer.StartRpc(player.NetId, (byte)RpcCalls.SetRole)
                    .Write((ushort)RoleTypes.Phantom)
                    .Write(true)
                    .EndRpc();
            }
        }
        if (AdjustKillCooldown && 10 < cooldown)
        {
            writer.StartRpc(player.NetId, (byte)RpcCalls.MurderPlayer)
                .WriteNetObject(player)
                .Write((int)MurderResultFlags.FailedProtected)
                .EndRpc();
        }
        if (ResetCooldown is true && pb.IsPhantomRole)
        {
            writer.StartRpc(player.NetId, (byte)RpcCalls.ProtectPlayer)
                .WriteNetObject(player)
                .Write(0)
                .EndRpc();
        }
        writer.EndMessage();
        writer.SendMessage();
        player.ResetKillCooldown();
        if (AdjustKillCooldown)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = cooldown;
            IUsePhantomButton.IPPlayerKillCooldown[player.PlayerId] = 0f;
        }
        return false;
    }

    /// <summary>ホスト自身がファントムのボタンを押した時</summary>
    public static void HandlePhantomClick(PlayerControl player, IUsePhantomButton pb)
    {
        bool AdjustKillCooldown = true;
        bool? ResetCooldown = true;
        pb.CheckOnClick(ref AdjustKillCooldown, ref ResetCooldown);
        float cooldown = IUsePhantomButton.GetRemainingKillCooldown(player);
        if (pb.SyncAbilityCooldownWithKillCooldown) pb.SetSyncedAbilityCooldown(cooldown);
        if (AdjustKillCooldown)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = cooldown;
            IUsePhantomButton.IPPlayerKillCooldown[player.PlayerId] = 0f;
            player.SetKillTimer(cooldown);
            player.SyncSettings();
        }
        if (ResetCooldown == true) player.Data.Role.SetCooldown();
    }
}

// ===== ファントムのボタン (ホスト自身) =====
[HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.DoClick))]
static class PhantomButtonDoClickPatch
{
    public static bool Prefix(AbilityButton __instance)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        var player = PlayerControl.LocalPlayer;
        if (player == null || HudManager._instance.AbilityButton.isCoolingDown
            || !player.CanMove || !player.IsAlive()) return true;
        if (player.GetRoleClass() is not IUsePhantomButton pb || !pb.UseOneclickButton) return true;

        // 押したのが分かるように少し暗くする
        __instance.OverrideColor(Palette.DisabledGrey);
        _ = new LateTask(() => __instance.OverrideColor(Palette.EnabledColor), 0.07f, "");
        PhantomButton.HandlePhantomClick(player, pb);
        return false;
    }
}
