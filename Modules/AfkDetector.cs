using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 一定時間動かない人 (放置) を見つける (ホストのみ)。
/// ロビーではキックでき、試合中は本人に警告してから、設定の結果 (知らせるだけ・キック・自滅) を与える。
/// 会議・イントロ・追放の演出の間は数えず、再開したら全員を「今動いた」として数え直す (会議で全員が移るため)。
/// 足止めされて動けない人 (速度 0) は数えない。名前の書き換えは使わず、知らせはチャットで本人とホストにだけ送る。
/// </summary>
/// <remarks>
/// 検出の流れは TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/AfkDetector.cs) を、
/// 警告の猶予・結果の選択・足止め中を除く条件は EndlessHostRoles (Modules/AFKDetector.cs) を元にした。
/// </remarks>
public static class AfkDetector
{
    public static OptionItem Enabled;
    public static OptionItem Seconds;
    public static OptionItem KickInLobby;
    public static OptionItem Consequence;

    enum Result { Notify, Kick, Suicide }
    static readonly string[] results = { "AfkResult.Notify", "AfkResult.Kick", "AfkResult.Suicide" };

    /// <summary>警告してから結果を与えるまでの猶予 (秒)</summary>
    const float WarningSeconds = 15f;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("AfkDetect", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(new Color32(0, 255, 165, 255))
            .SetGameMode(CustomGameMode.All);
        Seconds = IntegerOptionItem.Create("AfkDetectSeconds", new(30, 600, 10), 120, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        KickInLobby = StringOptionItem.Create("AfkKickInLobby", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.All);
        Consequence = StringOptionItem.Create("AfkConsequence", results, 0, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.All);
    }

    sealed class Track
    {
        public Vector2 Position;
        public float StillSince;
        /// <summary>警告した時刻 (まだなら -1)</summary>
        public float WarnedAt = -1f;
        public bool Done;
    }
    static readonly Dictionary<byte, Track> tracks = new();
    static float checkTimer;
    static bool wasCounting;

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class HudUpdatePatch
    {
        public static void Postfix()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || Enabled == null || !Enabled.GetBool())
            {
                if (tracks.Count > 0) tracks.Clear();
                return;
            }
            var counting = GameStates.IsLobby
                || (GameStates.IsInTask && !GameStates.Intro && !GameStates.IsMeeting && ExileController.Instance == null);
            if (!counting) { wasCounting = false; return; }
            if (!wasCounting) { tracks.Clear(); wasCounting = true; }

            checkTimer += Time.deltaTime;
            if (checkTimer < 1f) return; // 1 秒に 1 回で足りる
            checkTimer = 0f;
            try { Check(); }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(AfkDetector)); }
        }
    }

    static void Check()
    {
        var limit = Seconds.GetInt();
        var now = Time.time;
        var lobby = GameStates.IsLobby;
        // 処分 (キック・死亡) は 1 秒に 1 人まで。会議明けなどで何人も同時に期限を迎えても、送信をまとめて出さない
        var applied = false;
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || pc.AmOwner || pc.Data.Disconnected) continue;
            if (!lobby && (!pc.IsAlive() || PlayerSpeed.Get(pc.PlayerId) <= 0.0001f))
            {
                tracks.Remove(pc.PlayerId);
                continue;
            }
            var pos = pc.GetTruePosition();
            if (!tracks.TryGetValue(pc.PlayerId, out var t))
            {
                tracks[pc.PlayerId] = new Track { Position = pos, StillSince = now };
                continue;
            }
            if (Vector2.Distance(pos, t.Position) > 0.1f)
            {
                if (t.WarnedAt >= 0f && !t.Done) Utils.SendMessage(GetString("AfkRecovered"), pc.PlayerId);
                t.Position = pos;
                t.StillSince = now;
                t.WarnedAt = -1f;
                t.Done = false;
                continue;
            }
            if (t.Done || now - t.StillSince < limit) continue;

            if (lobby)
            {
                if (applied) continue;
                applied = KickInLobby.GetBool();
                OnAfkInLobby(pc, limit);
                t.Done = true;
                continue;
            }

            var result = (Result)Consequence.GetValue();
            if (result == Result.Notify)
            {
                Utils.SendMessage(string.Format(GetString("AfkNotify"), pc.ColoredName(), limit), PlayerControl.LocalPlayer.PlayerId);
                t.Done = true;
                continue;
            }
            if (t.WarnedAt < 0f)
            {
                t.WarnedAt = now;
                Utils.SendMessage(string.Format(GetString("AfkWarning"), (int)WarningSeconds), pc.PlayerId);
                continue;
            }
            if (now - t.WarnedAt < WarningSeconds || applied) continue;
            applied = true;
            t.Done = true;
            Apply(pc, result, limit);
        }
    }

    /// <summary>その人が動いていない秒数 (数えていなければ 0)。操作盤の表示用</summary>
    public static float StillSeconds(byte playerId)
        => tracks.TryGetValue(playerId, out var t) ? Time.time - t.StillSince : 0f;

    /// <summary>退出した人の記録を消す (同じ PlayerId で別の人が入った時に引き継がない)</summary>
    public static void OnPlayerLeft(byte playerId) => tracks.Remove(playerId);

    static void OnAfkInLobby(PlayerControl pc, int limit)
    {
        if (!KickInLobby.GetBool())
        {
            Utils.SendMessage(string.Format(GetString("AfkNotify"), pc.ColoredName(), limit), PlayerControl.LocalPlayer.PlayerId);
            return;
        }
        Logger.Info($"{pc.Data.PlayerName} が {limit} 秒動かないのでロビーからキック", nameof(AfkDetector));
        Utils.SendMessage(string.Format(GetString("AfkKicked"), pc.Data.PlayerName, limit), PlayerControl.LocalPlayer.PlayerId);
        var clientId = pc.GetClientId();
        if (clientId != -1) AmongUsClient.Instance.KickPlayer(clientId, false);
        tracks.Remove(pc.PlayerId);
    }

    static void Apply(PlayerControl pc, Result result, int limit)
    {
        Logger.Info($"{pc.Data.PlayerName} が放置のため {result}", nameof(AfkDetector));
        switch (result)
        {
            case Result.Kick:
                Utils.SendMessage(string.Format(GetString("AfkKicked"), pc.Data.PlayerName, limit), PlayerControl.LocalPlayer.PlayerId);
                var clientId = pc.GetClientId();
                if (clientId != -1) AmongUsClient.Instance.KickPlayer(clientId, false);
                break;
            case Result.Suicide:
                if (PlayerState.GetByPlayerId(pc.PlayerId) is { } state) state.DeathReason = CustomDeathReason.Suicide;
                pc.RpcMurderPlayerV2(pc);
                Utils.AddGameLog("Afk", string.Format(GetString("AfkSuicideLog"), pc.ColoredName()));
                break;
        }
        tracks.Remove(pc.PlayerId);
    }
}
