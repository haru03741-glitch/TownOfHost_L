using System;
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
/// かくれんぼの間は TOHL のパッチを本編のかくれんぼのモードが挙げた束 (<see cref="Modes.GameMode.LiveBundles"/>) だけに絞って本編のまま遊ばせ、通常へ戻ったら全部を当て直す。
/// 切り替えの口 (チャットコマンド・モードの変化の見張り・毎フレームの駆動) は外さない別の Harmony と MonoBehaviour に置く。
/// GameManager を出し直す手順は More Roles Plus (haru03741-glitch/More-Roles-Plus, GPL-3.0) の GameModeSwitch を参考に書き直した。
/// </summary>
public static class VanillaHideAndSeek
{
    // Client: ほかの人の部屋に客として入っている間。ホストの処理 (Standard) を外し、本編の客のまま遊ぶ。
    // ホストが同じ版の TOHL なら、届いた役職と設定で TOHL の画面を出す (Modules/Net/ModClientSync・ModClientView)
    private enum State { Live, Suspended, Client, Unloaded }

    private static State state = State.Live;
    private static Harmony resident;

    // ロビーでの切り替えの進み具合
    private static GameModes? requested;
    private static GameModes spawnMode;
    private static float spawnAt = -1f;
    private static bool resumeAfterSpawn;

    // 本編は試合の後にロビーへ戻る途中などで、かくれんぼの部屋のままモードを一瞬「通常」にする。
    // 部屋の中で戻すのは、部屋の GameManager が通常の物である状態が続いた時だけにする
    private static float normalSince = -1f;
    private const float ResumeSettle = 1f;

    // 古い GameManager の破棄が客の側で済んでから新しい物を出す
    // (客が一瞬固まると 0.5 秒では Despawn と Spawn が同じフレームに届き、新しい GameManager が Awake で自分を消す)
    private const float SpawnDelay = 0.8f;

    public static bool IsSuspended => state == State.Suspended;
    /// <summary>客として部屋にいて、TOHL のホストの処理を外している間</summary>
    public static bool IsClientMode => state == State.Client;

    /// <summary>客の間に当てておく束。画面の表示と、部屋の作り・探しにしか効かず、電文を送らない物だけ</summary>
    private const Modes.PatchBundles ClientBundles = Modes.PatchBundles.Core;

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
        Modes.PatchBundleSet.Apply(Modes.PatchBundles.None);
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
            if (FollowClient()) return;
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

    private static readonly System.Collections.Generic.Dictionary<byte, int> clientIdsAtSuspend = new();
    private static void Suspend()
    {
        if (state != State.Live) return;
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
        // 休んでいる間に抜けて入れ替わった人を戻りで見分けるため、番号ごとの客の ID を控える
        clientIdsAtSuspend.Clear();
        foreach (var pc in PlayerControl.AllPlayerControls)
            if (pc && pc.Data) clientIdsAtSuspend[pc.PlayerId] = pc.GetClientId();
        // 溜まっているのは通常モードの処理なので、その束を外した後の本編のかくれんぼでは走らせずに捨てる
        if (LateTask.Tasks.Count > 0) Logger.Info($"休む前に LateTask を {LateTask.Tasks.Count} 件捨てます", "VanillaHideAndSeek");
        LateTask.Tasks.Clear();
        Modes.PatchBundleSet.Apply(Modes.ModeCatalog.Vanilla.LiveBundles);
        // 左上の設定の一覧は、書き直しと隠す処理が通常モードの束にある。出したままだと通常モードの古い一覧が試合中の画面に残る
        HudManagerPatch.HideOptionShower();
        // 今あるロビーにはかくれんぼの束の Start の後置が走らないので、ここで開始人数を合わせる
        if (GameStartManager.InstanceExists) GameStartManager.Instance.MinPlayers = 1;
        state = State.Suspended;
        Logger.Info("かくれんぼの間 TOHL を休ませます", "VanillaHideAndSeek");
    }

    private static void Resume()
    {
        if (state != State.Suspended) return;
        Modes.PatchBundleSet.Apply(Modes.ModeCatalog.Standard.LiveBundles);
        state = State.Live;
        Logger.Info("TOHL を戻しました", "VanillaHideAndSeek");
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
        // 休んでいる間は抜けた人の版を消していない。休む前と別の人がいる番号 (空いた番号に入った別人) の版を捨てる
        // 客の間から戻った時 (控えが無い) は見ない。自分の版は消さない
        if (clientIdsAtSuspend.Count > 0)
        try
        {
            foreach (var id in System.Linq.Enumerable.ToArray(Main.playerVersion.Keys))
            {
                if (PlayerControl.LocalPlayer && id == PlayerControl.LocalPlayer.PlayerId) continue;
                var pc = Utils.GetPlayerById(id);
                int clientId = pc != null ? pc.GetClientId() : -1;
                if (clientId < 0 || !clientIdsAtSuspend.TryGetValue(id, out var before) || before != clientId)
                    Main.playerVersion.Remove(id);
            }
        }
        catch (Exception ex) { Logger.Warn($"版の記録を確かめられませんでした: {ex.Message}", "VanillaHideAndSeek"); }
        clientIdsAtSuspend.Clear();
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
    // かくれんぼの部屋に入りかけたらすぐ休む (TOHL のロビーの処理をかくれんぼの部屋で走らせない)。
    // メニューでは作成モードがかくれんぼでも休まない (休むと TOHL のメニューの部品が出ない)
    private static void FollowMode()
    {
        if (spawnAt >= 0f || resumeAfterSpawn || requested != null) return;
        var client = AmongUsClient.Instance;
        // 部屋を立てる・入るための接続を始めた所から部屋の中とみなす。部屋に入った時の TOHL の処理 (OnGameJoined) より前に休むため
        bool inRoom = client != null && (client.GameState != InnerNetClient.GameStates.NotJoined || client.connection != null);
        var mode = CurrentMode;
        if (mode == GameModes.HideNSeek && inRoom)
        {
            normalSince = -1f;
            if (state == State.Live) Suspend();
            return;
        }
        if (state != State.Suspended) { normalSince = -1f; return; }
        // 部屋の外 (メニューで作成モードがかくれんぼのままの時も) か、通常の部屋なら戻す
        bool normalRoom = mode != GameModes.HideNSeek && GameManager.Instance && GameManager.Instance.TryCast<NormalGameManager>() != null;
        if (inRoom && !normalRoom) { normalSince = -1f; return; }
        // 部屋の外ではモードの揺れを待たずに戻す (メニューの Start より先に TOHL のパッチを当てておく)
        if (!inRoom) { normalSince = -1f; Resume(); return; }
        var now = Time.realtimeSinceStartup;
        if (normalSince < 0f) { normalSince = now; return; }
        if (now - normalSince < ResumeSettle) return;
        normalSince = -1f;
        Resume();
    }

    // 客として部屋に入ったら Standard を外し、部屋を出た時に戻す。
    // ホストを引き継いでも戻さない (引き継いだ部屋の上で TOHL の開始の処理を動かすと公式サーバーに Hacking で切られる
    // 2026-11-19 実機)。引き継いだ部屋は本編のホストのまま回し、TOHL で遊ぶなら部屋を立て直す。
    // 客の間は true を返す (かくれんぼの見張りは動かさない)
    private static bool FollowClient()
    {
        var client = AmongUsClient.Instance;
        if (state != State.Client)
        {
            bool joined = client != null && client.GameState is InnerNetClient.GameStates.Joined or InnerNetClient.GameStates.Started;
            if (!joined || client.AmHost) return false;
            EnterClient();
            return true;
        }
        bool inRoom = client != null && (client.GameState != InnerNetClient.GameStates.NotJoined || client.connection != null);
        if (!inRoom)
        {
            LeaveClient();
            return false;
        }
        return true;
    }

    private static void EnterClient()
    {
        requested = null;
        spawnAt = -1f;
        resumeAfterSpawn = false;
        normalSince = -1f;
        // 溜まっているのは部屋に入った時の TOHL の処理なので、束を外した後には走らせない
        if (LateTask.Tasks.Count > 0) Logger.Info($"客になる前に LateTask を {LateTask.Tasks.Count} 件捨てます", "ClientMode");
        LateTask.Tasks.Clear();
        // 部屋に入った時の TOHL の処理 (OnGameJoined) より先に切り替わるので、前の部屋の版の記録はここで捨てる
        Main.playerVersion = new();
        Modules.Net.ModClientSync.Reset();
        GameStates.InGame = false;
        Modes.PatchBundleSet.Apply(ClientBundles);
        state = State.Client;
        Logger.Info("客として部屋に入ったので、TOHL のホストの処理を外して本編の客のまま遊びます", "ClientMode");
    }

    private static void LeaveClient()
    {
        Modules.Net.ModClientSync.Reset();
        state = State.Live;
        Modes.PatchBundleSet.Apply(Modes.ModeCatalog.Standard.LiveBundles);
        Logger.Info("客の間に外していた TOHL の処理を戻しました", "ClientMode");
        if (LobbyBehaviour.Instance) ResumeLobby();
    }

    // 休んでいる間は TOHL のチャットの処理が無いので、/mode だけをここで拾う
    // 客の間は TOHL のチャットの処理 (Standard) が無いので、ここでコマンドを受ける (SendChatAsClient)
    private static bool SendChatPrefix(ChatController __instance)
    {
        if (state == State.Client) return SendChatAsClient(__instance);
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

    /// <summary>
    /// 客の間のチャットの送信。客の間は TOHL のコマンドの処理 (Standard の束) を外しているので、ここで代わりに受ける。
    /// 自分の画面だけで動くコマンドはその場で動かして送らない。ホストで動くコマンドは /cmd を付けて送る (付けると他の人に見えない)。
    /// 送るなら true
    /// </summary>
    private static bool SendChatAsClient(ChatController chat)
    {
        try
        {
            var field = chat.freeChatField;
            if (!field || !field.textArea) return true;
            var text = field.textArea.text;
            if (string.IsNullOrEmpty(text)) return true;
            TownOfHostL.Modules.ChatInput.Remember(text);
            var args = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0) return true;
            bool viaCmd = TownOfHostL.ChatCommands.StripCmd(ref args);
            var command = TownOfHostL.ChatCommandTable.Find(args[0]);
            if (command?.Local != null)
            {
                var ctx = new TownOfHostL.ChatCommandContext(args, PlayerControl.LocalPlayer, chat);
                try { command.Local(ctx); }
                catch (Exception ex) { Logger.Exception(ex, "ClientMode.Local"); }
                field.textArea.Clear();
                field.textArea.SetText(ctx.CancelText);
                return false;
            }
            if (viaCmd || command?.Remote == null) return true;
            var cmdText = $"/cmd {text.Trim()}";
            TownOfHostL.Modules.ChatInput.SetText(field.textArea, cmdText, cmdText.Length);
        }
        catch (Exception ex) { Logger.Exception(ex, "ClientMode"); }
        return true;
    }

    private static void Say(string text)
    {
        try
        {
            if (HudManager.InstanceExists && PlayerControl.LocalPlayer)
                TownOfHostL.Modules.HostNote.Post(text);
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
