using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Attributes;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ベントの制限 (ホストのみ)。
/// ・生きている人が決めた人数以下になったら、誰もベントに入れない (入ると押し出す)
/// ・ベントの中にいられる時間の上限を超えたら押し出す
/// </summary>
/// <remarks>TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/VentManager.cs CheckVentLimit・OptionHolder.cs CantUseVentMode / MaxInVentMode) を元にした。</remarks>
public static class VentLimit
{
    static OptionItem enabled;
    static OptionItem banAtAlive;
    static OptionItem maxStay;

    public static void SetupCustomOption()
    {
        enabled = StringOptionItem.Create("VentLimit", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.Standard);
        banAtAlive = IntegerOptionItem.Create("VentBanAtAlive", new(0, 15, 1), 0, TabGroup.ModMainSettings, false).SetParent(enabled)
            .SetValueFormat(OptionFormat.Players).SetZeroNotation(OptionZeroNotation.Off)
            .SetGameMode(CustomGameMode.Standard);
        maxStay = FloatOptionItem.Create("VentMaxStay", new(0f, 60f, 1f), 0f, TabGroup.ModMainSettings, false).SetParent(enabled)
            .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity)
            .SetGameMode(CustomGameMode.Standard);
    }

    static bool IsActive => enabled != null && enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;

    // ベントに入った時刻 (中にいる人だけ)
    static readonly Dictionary<byte, float> enteredAt = new();
    static bool banAnnounced;

    [GameModuleInitializer]
    public static void Init()
    {
        enteredAt.Clear();
        banAnnounced = false;
    }

    /// <summary>生きている人が少ないのでベントを使えない</summary>
    static bool IsBanned
    {
        get
        {
            var n = banAtAlive.GetInt();
            return n > 0 && Main.AllAlivePlayerControls.Count() <= n;
        }
    }

    /// <summary>ベントに入った時 (ホストのみ)。押し出すなら true</summary>
    public static bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!IsActive || physics == null || physics.myPlayer == null) return false;
        var pc = physics.myPlayer;
        if (IsBanned)
        {
            if (!banAnnounced)
            {
                banAnnounced = true;
                Utils.SendMessageAutoSplit(string.Format(GetString("VentLimit.Banned"), banAtAlive.GetInt()), true, GetString("VentLimit.Title"));
            }
            // 入る動きの途中で押し出すと客の画面で戻らないことがあるので、少し待つ (役職の不可と同じ待ち)
            _ = new LateTask(() => { if (physics != null && GameStates.IsInTask) physics.RpcBootFromVent(ventId); }, 0.5f, "VentLimit.Ban");
            return true;
        }
        if (maxStay.GetFloat() > 0f) enteredAt[pc.PlayerId] = Time.time;
        return false;
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class FixedUpdatePatch
    {
        static float nextCheck;

        public static void Postfix(PlayerControl __instance)
        {
            if (!__instance.AmOwner || enteredAt.Count == 0 || !AmongUsClient.Instance.AmHost) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;
            if (!IsActive || !GameStates.IsInTask) { enteredAt.Clear(); return; }
            var limit = maxStay.GetFloat();
            // 押し出しの電文が一度に重ならないよう、1 回の巡回で 2 人まで (残りは次の巡回)
            var booted = 0;
            foreach (var (id, at) in enteredAt.ToArray())
            {
                var pc = Utils.GetPlayerById(id);
                // ホストの側では客の inVent が立たないので、ベントの出入りの記録で中にいるかを見る
                if (pc == null || !RoleHooks.NowVentId.ContainsKey(id) || !pc.IsAlive())
                {
                    enteredAt.Remove(id);
                    continue;
                }
                if (Time.time - at < limit || booted >= 2) continue;
                booted++;
                enteredAt.Remove(id);
                var vent = RoleHooks.NowVentId[id];
                pc.MyPhysics.RpcBootFromVent(vent);
                Logger.Info($"{pc.Data?.PlayerName} をベントから押し出した ({limit} 秒)", nameof(VentLimit));
            }
        }
    }
}
