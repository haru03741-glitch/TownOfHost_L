using System;
using System.Diagnostics;
using AmongUs.Data;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using UnityEngine;

using static TownOfHostL.Translator;

namespace TownOfHostL.Core;

/// <summary>
/// 本編のかくれんぼの間、TOHL を休ませる。
/// 本編はモード (通常 / かくれんぼ) を部屋の作成時にしか選べず、試合を回す GameManager もモードごとに別の物なので、
/// ロビーで切り替える時はホストが GameManager を消して新しいモードの物を出し直し、設定を配り直す。
/// かくれんぼの間は TOHL の Harmony を丸ごと外して本編のまま遊ばせ、通常へ戻ったら当て直す。
/// 切り替えの口 (チャットコマンド・モードの変化の見張り・毎フレームの駆動) は外さない別の Harmony と MonoBehaviour に置く。
/// GameManager を出し直す手順は More Roles Plus (haru03741-glitch/More-Roles-Plus, GPL-3.0) の GameModeSwitch を参考に書き直した。
/// </summary>
public static class VanillaHideAndSeek
{
    private enum State { Live, Suspended, Unloaded }

    private static State state = State.Live;
    private static Harmony resident;

    // ロビーでの切り替えの進み具合
    private static GameModes? requested;
    private static GameModes spawnMode;
    private static float spawnAt = -1f;
    private static bool resumeAfterSpawn;

    // 本編は試合の後にロビーへ戻る途中などで、かくれんぼの部屋のままモードを一瞬「通常」にする。
    // 戻すのは、通常が続いた上で部屋の外にいるか、部屋の GameManager が通常の物の時だけにする
    private static float normalSince = -1f;
    private const float ResumeSettle = 1f;

    // 古い GameManager の破棄が客の側で済んでから新しい物を出す
    private const float SpawnDelay = 0.5f;

    public static bool IsSuspended => state == State.Suspended;

    /// <summary>TOHL の PatchAll より前に 1 回だけ呼ぶ。</summary>
    public static void Init(Main plugin)
    {
        resident = new Harmony(Main.PluginGuid + ".resident");
        Modules.DelegateTypeCache.Install(resident);
        Patches.ConstantsPatch.Install(resident);
        resident.Patch(AccessTools.Method(typeof(ChatController), nameof(ChatController.SendChat)),
            prefix: new HarmonyMethod(typeof(VanillaHideAndSeek), nameof(SendChatPrefix)) { priority = Priority.First });
        plugin.AddComponent<Driver>();
    }

    /// <summary>TOHL を完全に外す (unload)。休ませる仕組みも止める。</summary>
    public static void UnpatchForUnload()
    {
        state = State.Unloaded;
        Main.Instance.Harmony.UnpatchSelf();
        resident?.UnpatchSelf();
    }

    /// <summary>チャットコマンド /mode の本体。TOHL が動いている時は TOHL のコマンド表から、休んでいる時は SendChatPrefix から呼ぶ。</summary>
    public static void Command(string[] args)
    {
        var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        GameModes target = mode switch
        {
            "hns" or "hideandseek" or "h" => GameModes.HideNSeek,
            "normal" or "classic" or "n" => GameModes.Normal,
            _ => GameModes.None,
        };
        if (target == GameModes.None) { Say(GetString("ModeSwitch.Usage")); return; }
        var error = CanSwitch(target);
        if (error != null) { Say(GetString(error)); return; }
        // 外す・当て直すのは、TOHL のパッチの中 (今のチャットの処理) を抜けてから、次のフレームで行う
        requested = target;
    }

    private static string CanSwitch(GameModes target)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost) return "ModeSwitch.HostOnly";
        if (!LobbyBehaviour.Instance || client.GameState != InnerNetClient.GameStates.Joined) return "ModeSwitch.LobbyOnly";
        // 開始の数えの間に入れ替えると、開始の処理が古い GameManager を掴んだまま進む
        if (GameStartManager.InstanceExists && GameStartManager.Instance.startState != GameStartManager.StartingStates.NotStarting) return "ModeSwitch.Starting";
        if (requested != null || spawnAt >= 0f || resumeAfterSpawn) return "ModeSwitch.Busy";
        if (CurrentMode == target) return "ModeSwitch.Already";
        return null;
    }

    // GameOptionsManager.CurrentGameOptions は読むだけで SwitchGameMode を呼ぶことがあるので、フィールドを直に見る
    private static GameModes CurrentMode => GameOptionsManager.Instance?.currentGameMode ?? GameModes.Normal;

    // 例外が続いた時にログを溢れさせないよう、しばらく止める
    private static float pausedUntil;

    private static void Tick()
    {
        if (state == State.Unloaded || Time.realtimeSinceStartup < pausedUntil) return;
        try
        {
            if (requested is GameModes target)
            {
                requested = null;
                if (CanSwitchNow(target)) StartSwitch(target);
            }
            if (spawnAt >= 0f && Time.realtimeSinceStartup >= spawnAt) SpawnStep();
            if (resumeAfterSpawn && GameManager.Instance && GameManager.Instance.TryCast<NormalGameManager>() != null)
            {
                resumeAfterSpawn = false;
                Resume();
                Say(GetString("ModeSwitch.DoneNormal"));
            }
            FollowMode();
        }
        catch (Exception ex)
        {
            Logger.Error($"モードの切り替えで例外: {ex}", "VanillaHideAndSeek");
            requested = null;
            spawnAt = -1f;
            resumeAfterSpawn = false;
            pausedUntil = Time.realtimeSinceStartup + 1f;
            EnsureGameManager();
        }
    }

    // 切り替えの途中で失敗して GameManager の無いロビーが残ると開始できないので、今のモードの物を出す
    private static void EnsureGameManager()
    {
        try
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || !LobbyBehaviour.Instance || GameManager.Instance) return;
            var mode = CurrentMode is GameModes.HideNSeek ? GameModes.HideNSeek : GameModes.Normal;
            if (GameOptionsManager.Instance.currentGameMode != mode) GameOptionsManager.Instance.SwitchGameMode(mode);
            var gm = GameManagerCreator.CreateGameManager(mode);
            if (!gm) return;
            client.Spawn(gm, -2, SpawnFlags.None);
            gm.LogicOptions?.SyncOptions();
            Logger.Warn($"GameManager が無かったので {mode} の物を出し直しました", "VanillaHideAndSeek");
        }
        catch (Exception ex)
        {
            Logger.Error($"GameManager を出し直せませんでした: {ex.Message}", "VanillaHideAndSeek");
        }
    }

    // 予約から実行までの 1 フレームの間に状況が変わっていないか
    private static bool CanSwitchNow(GameModes target)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || !LobbyBehaviour.Instance || CurrentMode == target) return false;
        if (GameStartManager.InstanceExists && GameStartManager.Instance.startState != GameStartManager.StartingStates.NotStarting) return false;
        return true;
    }

    private static void StartSwitch(GameModes target)
    {
        var prev = CurrentMode;
        if (target == GameModes.HideNSeek) Suspend();
        try
        {
            RespawnGameManager(target);
        }
        catch (Exception ex)
        {
            // GameManager の無いロビーは開始できないので、元のモードの物を出し直す
            Logger.Error($"{target} への切り替えに失敗したので {prev} に戻します: {ex}", "VanillaHideAndSeek");
            Say(GetString("ModeSwitch.Failed"));
            spawnAt = -1f;
            RespawnGameManager(prev);
        }
    }

    private static void RespawnGameManager(GameModes mode)
    {
        GameOptionsManager.Instance.SwitchGameMode(mode);
        var old = GameManager.Instance;
        if (old)
        {
            AmongUsClient.Instance.Despawn(old);
            // 消した物の破棄はフレームの終わりなので、空けておかないと新しい物が自分を Instance にできない
            GameManager.Instance = null;
        }
        spawnMode = mode;
        spawnAt = Time.realtimeSinceStartup + SpawnDelay;
        Logger.Info($"GameManager を消しました。{SpawnDelay} 秒後に {mode} の物を出します", "VanillaHideAndSeek");
    }

    private static void SpawnStep()
    {
        spawnAt = -1f;
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || !LobbyBehaviour.Instance) return;
        var gm = GameManagerCreator.CreateGameManager(spawnMode);
        if (!gm) throw new InvalidOperationException("CreateGameManager returned null");
        client.Spawn(gm, -2, SpawnFlags.None);
        gm.LogicOptions?.SyncOptions();
        if (spawnMode == GameModes.Normal && GameStartManager.InstanceExists) GameStartManager.Instance.MinPlayers = 1;
        Logger.Info($"{gm.GetIl2CppType().Name} を出しました (netId={gm.NetId})", "VanillaHideAndSeek");
        if (spawnMode == GameModes.Normal && state == State.Suspended) resumeAfterSpawn = true;
        else if (spawnMode == GameModes.HideNSeek) Say(GetString("ModeSwitch.DoneHns"));
    }

    private static void Suspend()
    {
        if (state != State.Live) return;
        // 今は「常駐の物だけを残す」しか無い。本編のかくれんぼで動かすパッチを足す時は、ここで外す範囲を絞る
        if (Modes.ModeCatalog.Vanilla.PatchScope != Modes.GamePatchScope.ResidentOnly) return;
        // ホストの名前に付けた mod の表示を外す (休んでいる間は付け直す処理が動かない)
        try
        {
            if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost && PlayerControl.LocalPlayer)
            {
                var name = Main.nickName != "" ? Main.nickName : DataManager.player.Customization.Name;
                if (PlayerControl.LocalPlayer.Data && PlayerControl.LocalPlayer.Data.PlayerName != name) PlayerControl.LocalPlayer.RpcSetName(name);
            }
        }
        catch (Exception ex) { Logger.Warn($"名前を戻せませんでした: {ex.Message}", "VanillaHideAndSeek"); }
        // 駆動するパッチが外れると溜まったまま止まり、戻した時に一斉に走るので捨てる
        if (LateTask.Tasks.Count > 0) Logger.Info($"休む前に LateTask を {LateTask.Tasks.Count} 件捨てます", "VanillaHideAndSeek");
        LateTask.Tasks.Clear();
        var sw = Stopwatch.StartNew();
        Main.Instance.Harmony.UnpatchSelf();
        state = State.Suspended;
        Logger.Info($"かくれんぼの間 TOHL を休ませます (外すのに {sw.ElapsedMilliseconds}ms)", "VanillaHideAndSeek");
    }

    private static void Resume()
    {
        if (state != State.Suspended) return;
        var sw = Stopwatch.StartNew();
        Main.Instance.Harmony.PatchAll();
        state = State.Live;
        Logger.Info($"TOHL を戻しました (当て直すのに {sw.ElapsedMilliseconds}ms)", "VanillaHideAndSeek");
        if (LobbyBehaviour.Instance) ResumeLobby();
    }

    // 休んでいる間に走らなかったロビーの初期化を、今のロビーに当て直す
    private static void ResumeLobby()
    {
        if (GameStartManager.InstanceExists)
            GameStartManagerPatch.EnsureLobbyUi(GameStartManager.Instance);
        // 休んでいる間に作られた HudManager とプレイヤーには、TOHL の Start の後置処理が走っていない
        if (HudManager.InstanceExists) HudManagerPatch.EnsureOptionShower(HudManager.Instance);
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc && pc.cosmetics && pc.cosmetics.nameText && !pc.cosmetics.nameText.transform.Find("RoleText"))
                PlayerStartPatch.Postfix(pc);
        }
        Players.Invalidate();
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
        // 休んでいる間に入ってきた人は、参加の時の確認 (BAN・名前) を通っていない
        foreach (var client in AmongUsClient.Instance.allClients)
        {
            if (client == null || client.Id == AmongUsClient.Instance.ClientId) continue;
            try
            {
                BanManager.CheckBanPlayer(client);
                BanManager.CheckDenyNamePlayer(client);
            }
            catch (Exception ex) { Logger.Warn($"{client.PlayerName} の参加の確認に失敗: {ex.Message}", "VanillaHideAndSeek"); }
        }
    }

    // /mode の流れの外でモードが変わった時 (かくれんぼの部屋を作る・入る、通常の部屋を作る) に、休む・戻るを合わせる。
    // かくれんぼになったらすぐ休む (TOHL のロビーの処理をかくれんぼの部屋で走らせない)
    private static void FollowMode()
    {
        if (spawnAt >= 0f || resumeAfterSpawn || requested != null) return;
        var mode = CurrentMode;
        if (mode == GameModes.HideNSeek)
        {
            normalSince = -1f;
            if (state == State.Live) Suspend();
            return;
        }
        if (state != State.Suspended) { normalSince = -1f; return; }
        var now = Time.realtimeSinceStartup;
        if (normalSince < 0f) { normalSince = now; return; }
        if (now - normalSince < ResumeSettle) return;
        var client = AmongUsClient.Instance;
        bool outsideRoom = client == null || client.GameState == InnerNetClient.GameStates.NotJoined;
        bool normalRoom = GameManager.Instance && GameManager.Instance.TryCast<NormalGameManager>() != null;
        if (!outsideRoom && !normalRoom) return;
        normalSince = -1f;
        Resume();
    }

    // 休んでいる間は TOHL のチャットの処理が無いので、/mode だけをここで拾う
    private static bool SendChatPrefix(ChatController __instance)
    {
        if (state != State.Suspended) return true;
        var field = __instance.freeChatField;
        if (!field || !field.textArea) return true;
        var text = field.textArea.text;
        if (string.IsNullOrEmpty(text)) return true;
        var args = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0 || args[0] != "/mode") return true;
        field.textArea.Clear();
        Command(args);
        return false;
    }

    private static void Say(string text)
    {
        try
        {
            if (HudManager.InstanceExists && PlayerControl.LocalPlayer)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text);
        }
        catch { }
        Logger.Info(text, "VanillaHideAndSeek");
    }

    public class Driver : MonoBehaviour
    {
        public Driver(IntPtr ptr) : base(ptr) { }
        public void Update() => Tick();
    }
}
