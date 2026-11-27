using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Modules;

namespace TownOfHostL
{
    // 手元だけのキー操作 (解像度・翻訳の読み直しと書き出し・ログのダンプ・実行ファイルのフォルダ)。
    // TOHL の設定や役職に触れないので、TOHL 入りの客でもホストでも、どのモードでも当てる
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    [HarmonyPatch(typeof(ControllerManager), nameof(ControllerManager.Update))]
    class ControllerLocalKeysPatch
    {
        static readonly (int, int)[] resolutions = { (480, 270), (640, 360), (800, 450), (1024, 576), (1280, 720), (1366, 768), (1600, 900) };
        static int resolutionIndex = 0;
        public static void Postfix()
        {
            if (!Input.anyKeyDown) return;
            if (SettingsSearchBox.IsTyping || SettingsUI.TextField.Typing || RoleDictionary.Typing) return;
            RoleDictionary.CheckHotkey();
            // 客の間はホストの設定が届いている (同じ版の TOHL のホストと握手できた) 時だけ。届いていなければ一覧も出ず、コピーは自分の設定になる
            if (TownOfHostL.Core.VanillaHideAndSeek.IsClientMode && OptionItem.HasRemoteValues) ControllerManagerUpdatePatch.OptionViewKeys();
            //解像度変更
            if (Input.GetKeyDown(KeyCode.F11))
            {
                resolutionIndex++;
                if (resolutionIndex >= resolutions.Length) resolutionIndex = 0;
                ResolutionManager.SetResolution(resolutions[resolutionIndex].Item1, resolutions[resolutionIndex].Item2, false);
            }
            //カスタム翻訳のリロード
            if (ControllerManagerUpdatePatch.GetKeysDown(KeyCode.F5, KeyCode.T))
            {
                Logger.Info("Reload Custom Translation File", "KeyCommand");
                Translator.LoadLangs();
                Logger.SendInGame("Reloaded Custom Translation File");
            }
            if (ControllerManagerUpdatePatch.GetKeysDown(KeyCode.F5, KeyCode.X))
            {
                Logger.Info("Export Custom Translation File", "KeyCommand");
                Translator.ExportCustomTranslation();
                Logger.SendInGame("Exported Custom Translation File");
            }
            //ログファイルのダンプ
            if (ControllerManagerUpdatePatch.GetKeysDown(KeyCode.F1, KeyCode.LeftControl))
            {
                Logger.Info("Dump Logs", "KeyCommand");
                Utils.DumpLog();
            }
            //実行ファイルのフォルダを開く
            if (ControllerManagerUpdatePatch.GetKeysDown(KeyCode.F10))
            {
                Utils.OpenDirectory(System.Environment.CurrentDirectory);
            }
        }
    }

    [HarmonyPatch(typeof(ControllerManager), nameof(ControllerManager.Update))]
    class ControllerManagerUpdatePatch
    {
        /// <summary>
        /// 設定の一覧を見るキー (Tab と 1〜9 でページ・Alt+C で設定を文字でコピー)。
        /// ホストはこのパッチから、TOHL 入りの客は <see cref="ControllerLocalKeysPatch"/> から呼ぶ (客の間はこのパッチが外れている)
        /// </summary>
        public static void OptionViewKeys()
        {
            if (GameStates.IsLobby)
            {
                //カスタム設定切り替え
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    // Shift + Tab で一覧を隠す・出す
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) OptionShower.Hidden = !OptionShower.Hidden;
                    else
                    {
                        OptionShower.Hidden = false;
                        OptionShower.Next();
                    }
                }
                for (var i = 0; i < 9; i++)
                {
                    if (ORGetKeysDown(KeyCode.Alpha1 + i, KeyCode.Keypad1 + i) && OptionShower.pages.Count >= i + 1)
                        OptionShower.currentPage = i;
                }
            }
            //現在の設定をテキストとしてコピー
            if (GetKeysDown(KeyCode.LeftAlt, KeyCode.C) && !Input.GetKey(KeyCode.LeftShift) && !GameStates.IsNotJoined)
            {
                Utils.CopyCurrentSettings();
            }
        }

        public static void Postfix(ControllerManager __instance)
        {
            // どの操作もそのフレームに押されたキーで始まる。何も押されていないフレームは、キーの組ごとの配列も作らずに抜ける
            if (!Input.anyKeyDown) return;
            // 設定画面の検索欄に字を打っている間は、キー操作として受け取らない
            if (SettingsSearchBox.IsTyping || SettingsUI.TextField.Typing || RoleDictionary.Typing) return;
            OptionViewKeys();
            if (GameStates.IsLobby)
            {
                // 現在の設定を文字列形式のデータに変換してコピー
                if (GetKeysDown(KeyCode.O, KeyCode.LeftAlt))
                {
                    OptionSerializer.SaveToClipboard();
                }
                // 現在の設定を文字列形式のデータに変換してファイルに出力
                if (GetKeysDown(KeyCode.L, KeyCode.LeftAlt))
                {
                    OptionSerializer.SaveToFile();
                }
                // クリップボードから文字列形式の設定データを読み込む
                if (GetKeysDown(KeyCode.P, KeyCode.LeftAlt))
                {
                    OptionSerializer.LoadFromClipboard();
                }
            }

            //--以下ホスト専用コマンド--//
            if (!AmongUsClient.Instance.AmHost) return;
            //全員にキルフラッシュ表示する（暗転時修復用）
            if (GetKeysDown(KeyCode.K, KeyCode.L, KeyCode.LeftControl))
            {
                if (GameStates.InGame)
                    Main.AllPlayerControls.Do(pc => pc.KillFlash());

            }
            //廃村
            if (GetKeysDown(KeyCode.Return, KeyCode.L, KeyCode.LeftShift) && GameStates.IsInGame && Main.IsSetRoleFinished) 
            {
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
                GameManager.Instance.LogicFlow.CheckEndCriteria();
            }
            // 試合中 (タスクの間) でも自分のチャット欄を出す。客の画面は変えない
            if (GetKeysDown(KeyCode.Return, KeyCode.C, KeyCode.LeftShift) && HudManager.InstanceExists && HudManager.Instance.Chat && !HudManager.Instance.Chat.IsOpenOrOpening)
            {
                HudManager.Instance.Chat.SetVisible(true);
            }
            //ミーティングを強制終了
            if (GetKeysDown(KeyCode.Return, KeyCode.M, KeyCode.LeftShift) && GameStates.IsMeeting)
            {
                MeetingHud.Instance.RpcClose();
            }
            //即スタート
            if (Input.GetKeyDown(KeyCode.LeftShift) && GameStates.IsCountDown)
            {
                Logger.Info("CountDownTimer set to 0", "KeyCommand");
                GameStartManager.Instance.countDownTimer = 0;
            }
            //カウントダウンキャンセル
            if (Input.GetKeyDown(KeyCode.C) && GameStates.IsCountDown)
            {
                Logger.Info("Reset CountDownTimer", "KeyCommand");
                GameStartManager.Instance.ResetStartState();
            }
            //現在の有効な設定の説明を表示
            if (GetKeysDown(KeyCode.N, KeyCode.LeftShift, KeyCode.LeftControl))
            {
                Main.isChatCommand = true;
                Utils.ShowActiveSettingsHelp();
            }
            //現在の有効な設定を表示
            if (GetKeysDown(KeyCode.N, KeyCode.LeftControl) && !Input.GetKey(KeyCode.LeftShift))
            {
                Main.isChatCommand = true;
                Utils.ShowActiveSettings();
            }
            //TOHオプションをデフォルトに設定
            if (GetKeysDown(KeyCode.Delete, KeyCode.LeftControl) && !(HudManager.InstanceExists && HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening))
            {
                OptionItem.AllOptions.ToArray().Where(x => x is not PresetOptionItem).Do(x => x.SetValue(x.DefaultValue));
            }
            //自分自身の死体をレポート
            if (GetKeysDown(KeyCode.Return, KeyCode.M, KeyCode.RightShift) && GameStates.IsInTask)
            {
                var pc = PlayerControl.LocalPlayer;
                if (pc.IsAlive()) pc.ReportDeadBody(pc.Data);
                else pc.NoCheckStartMeeting(pc.Data);

            }
            //自分自身を追放
            if (GetKeysDown(KeyCode.Return, KeyCode.E, KeyCode.LeftShift) && GameStates.IsInGame)
            {
                PlayerControl.LocalPlayer.RpcExile();
            }

            //--以下デバッグモード用コマンド--//
            if (!DebugModeManager.IsDebugMode) return;

            //投票をクリア
            if (Input.GetKeyDown(KeyCode.V) && GameStates.IsMeeting && !GameStates.IsOnlineGame)
            {
                MeetingHud.Instance.RpcClearVote(PlayerControl.LocalPlayer.PlayerId);
            }
            //ログをゲーム内にも出力するかトグル
            if (GetKeysDown(KeyCode.F2, KeyCode.LeftControl))
            {
                Logger.isAlsoInGame = !Logger.isAlsoInGame;
                Logger.SendInGame($"ログのゲーム内出力: {Logger.isAlsoInGame}");
            }            
            //--以下フリープレイ用コマンド--//
            //if (!GameStates.IsFreePlay) return;
            //キルクールを0秒に設定
            if (Input.GetKeyDown(KeyCode.X))
            {
                PlayerControl.LocalPlayer.Data.Object.SetKillTimer(0f);
            }
            //自身のタスクをすべて完了
            if (Input.GetKeyDown(KeyCode.O))
            {
                foreach (var task in PlayerControl.LocalPlayer.myTasks)
                    PlayerControl.LocalPlayer.RpcCompleteTask(task.Id);
            }
            //イントロテスト
            if (Input.GetKeyDown(KeyCode.G))
            {
                HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, Color.black));
                HudManager.Instance.StartCoroutine(DestroyableSingleton<HudManager>.Instance.CoShowIntro());
            }
            //タスクカウントの表示切替
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                Main.VisibleTasksCount = !Main.VisibleTasksCount;
                DestroyableSingleton<HudManager>.Instance.Notifier.AddDisconnectMessage("VisibleTaskCountが" + Main.VisibleTasksCount.ToString() + "に変更されました。");
            }
            //エアシップのトイレのドアを全て開ける
            if (Input.GetKeyDown(KeyCode.P))
            {
                ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 79);
                ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 80);
                ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 81);
                ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 82);
            }
            //現在の座標を取得
            if (Input.GetKeyDown(KeyCode.I))
            {
                var room = PlayerControl.LocalPlayer.GetPlainShipRoom();
                var roomName = "";

                if (room == null)
                {
                    roomName = Translator.GetString("FailToTrack");
                }
                else
                {
                    roomName = Translator.GetString(room.RoomId.ToString());
                }

                var pos = PlayerControl.LocalPlayer.GetTruePosition().ToString();
                Logger.Info(pos, "GetLocalPlayerPos");
                Logger.SendInGame($"position: {pos}/ [@{roomName}]");
            }//マスゲーム用コード
            /*if (Input.GetKeyDown(KeyCode.C))
            {
                foreach(var pc in PlayerControl.AllPlayerControls) {
                    if(!pc.AmOwner) pc.MyPhysics.RpcEnterVent(2);
                }
            }
            if (Input.GetKeyDown(KeyCode.V))
            {
                Vector2 pos = PlayerControl.LocalPlayer.NetTransform.transform.position;
                foreach(var pc in PlayerControl.AllPlayerControls) {
                    if(!pc.AmOwner) {
                        pc.RpcSnapTo(pos);
                        pos.x += 0.5f;
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                foreach(var pc in PlayerControl.AllPlayerControls) {
                    if(!pc.AmOwner) pc.MyPhysics.RpcExitVent(2);
                }
            }
            if (Input.GetKeyDown(KeyCode.N))
            {
                VentilationSystem.Update(VentilationSystem.Operation.StartCleaning, 0);
            }*/
            //マスゲーム用コード終わり
        }
        internal static bool GetKeysDown(params KeyCode[] keys)
        {
            if (keys.Any(k => Input.GetKeyDown(k)) && keys.All(k => Input.GetKey(k)))
            {
                Logger.Info($"KeyDown:{keys.Where(k => Input.GetKeyDown(k)).First()} in [{string.Join(",", keys)}]", "GetKeysDown");
                return true;
            }
            return false;
        }
        static bool ORGetKeysDown(params KeyCode[] keys) => keys.Any(k => Input.GetKeyDown(k));
    }

    [HarmonyPatch(typeof(ConsoleJoystick), nameof(ConsoleJoystick.HandleHUD))]
    class ConsoleJoystickHandleHUDPatch
    {
        public static void Postfix()
        {
            HandleHUDPatch.Postfix(ConsoleJoystick.player);
        }
    }
    [HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.HandleHud))]
    class KeyboardJoystickHandleHUDPatch
    {
        public static void Postfix()
        {
            HandleHUDPatch.Postfix(KeyboardJoystick.player);
        }
    }
    class HandleHUDPatch
    {
        public static void Postfix(Rewired.Player player)
        {
            if (player.GetButtonDown(8) && // 8:キルボタンのactionId
            PlayerControl.LocalPlayer.Data?.Role?.IsImpostor == false &&
            PlayerControl.LocalPlayer.CanUseKillButton())
            {
                DestroyableSingleton<HudManager>.Instance.KillButton.DoClick();
            }
            if (player.GetButtonDown(50) && // 50:インポスターのベントボタンのactionId
            PlayerControl.LocalPlayer.Data?.Role?.IsImpostor == false &&
            PlayerControl.LocalPlayer.CanUseImpostorVentButton())
            {
                DestroyableSingleton<HudManager>.Instance.ImpostorVentButton.DoClick();
            }
        }
    }
}