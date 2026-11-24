using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using InnerNet;
    // Il2CppStructArray<byte>とbyte[]との間での暗黙的な変換の際に発生する重い計算を抑制するため，意図的にIl2CppSystemとIl2CppInterop.Runtime.InteropTypes.Arraysを使用します - Hyz-sui

    namespace TownOfHostL.Modules
    {
        public abstract class GameOptionsSender
        {
            #region Static
            public readonly static List<GameOptionsSender> AllSenders = new(15) { new NormalGameOptionsSender() };

            public static void SendAllGameOptions()
            {
                // 鬼の体の窓の間は GameManager が無いか、かくれんぼの物で、設定を送れない。dirty は残して窓の後で送る
                if (SeekerBody.InWindow) return;
                AllSenders.RemoveAll(static s =>
                {
                    try { return !s.AmValid(); }
                    catch (System.Exception ex) { Logger.Error($"AmValid: {ex.Message}", nameof(GameOptionsSender)); return true; }
                });
                // 1 人分の組み立ての例外で、後ろの人 (ホストは最後) の設定を止めない。
                // 例外の人も dirty を下ろす (残すと 0.2 秒ごとに同じ例外を繰り返す)
                for (int i = 0; i < AllSenders.Count; i++)
                {
                    var sender = AllSenders[i];
                    if (!sender.IsDirty) continue;
                    try { sender.SendGameOptions(); }
                    catch (System.Exception ex) { Logger.Error($"SendGameOptions ({sender.Describe()}): {ex}", nameof(GameOptionsSender)); }
                    finally { sender.IsDirty = false; }
                }
            }
            #endregion

            public abstract IGameOptions BasedGameOptions { get; }
            public abstract bool IsDirty { get; protected set; }

            public virtual void SendGameOptions() => SendBuiltOptions(BuildGameOptions());

            /// <summary>送る直前のバイト列を見て、送るかどうかを決める (同じ物を送り直さないなど)。</summary>
            protected virtual bool ShouldSend(Il2CppStructArray<byte> optionArray) => true;
            /// <summary>送り終えた設定のバイト列 (送る途中で例外になった時は呼ばない)。</summary>
            protected virtual void OnSent(Il2CppStructArray<byte> optionArray) { }
            /// <summary>記録に出す送り先の名前</summary>
            public virtual string Describe() => GetType().Name;

            /// <summary>組み立て済みの設定を書き出して送る。</summary>
            protected void SendBuiltOptions(IGameOptions opt)
            {
                if (opt == null)
                {
                    Logger.Error("BuildGameOptions returned null", nameof(GameOptionsSender));
                    return;
                }
                var currentGameMode = AprilFoolsMode.IsAprilFoolsModeToggledOn ? opt.AprilFoolsOnMode : opt.GameMode;
                MessageWriter writer = MessageWriter.Get(SendOption.None);
                try
                {
                    writer.Write(opt.Version);
                    writer.StartMessage(0);
                    writer.Write((byte)currentGameMode);
                    NormalGameOptionsV12 normalOpt = null;
                    HideNSeekGameOptionsV12 hnsOpt = null;
                    if (opt.TryCast<NormalGameOptionsV12>(out normalOpt) && normalOpt != null)
                    {
                        NormalGameOptionsV12.Serialize(writer, normalOpt);
                    }
                    else if (opt.TryCast<HideNSeekGameOptionsV12>(out hnsOpt) && hnsOpt != null)
                    {
                        HideNSeekGameOptionsV12.Serialize(writer, hnsOpt);
                    }
                    else
                    {
                        writer.EndMessage();
                        writer.Recycle();
                        Logger.Error("オプションのキャストに失敗しました (unknown concrete type)", nameof(GameOptionsSender));
                        return;
                    }
                    writer.EndMessage();
                    if (writer.Length <= 1)
                    {
                        writer.Recycle();
                        Logger.Error("MessageWriter produced invalid length when serializing game options", nameof(GameOptionsSender));
                        return;
                    }
                    var byteArray = new Il2CppStructArray<byte>(writer.Length - 1);
                    Buffer.BlockCopy(writer.Buffer.Cast<Array>(), 1, byteArray.Cast<Array>(), 0, writer.Length - 1);
                    if (ShouldSend(byteArray))
                    {
                        SendOptionsArray(byteArray);
                        OnSent(byteArray);
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.Error($"SendGameOptions exception: {ex.Message}", nameof(GameOptionsSender));
                }
                finally
                {
                    if (writer != null) writer.Recycle();
                }
            }
            public virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray)
            {
                for (byte i = 0; i < GameManager.Instance.LogicComponents.Count; i++)
                {
                    if (GameManager.Instance.LogicComponents[i].TryCast<LogicOptions>(out _))
                    {
                        SendOptionsArray(optionArray, i, -1);
                    }
                }
            }
            protected virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray, byte LogicOptionsIndex, int targetClientId)
            {
                var writer = MessageWriter.Get(SendOption.Reliable);

                writer.StartMessage(targetClientId == -1 ? Tags.GameData : Tags.GameDataTo);
                {
                    writer.Write(AmongUsClient.Instance.GameId);
                    if (targetClientId != -1) writer.WritePacked(targetClientId);
                    writer.StartMessage(1);
                    {
                        writer.WritePacked(GameManager.Instance.NetId);
                        writer.StartMessage(LogicOptionsIndex);
                        {
                            writer.WriteBytesAndSize(optionArray);
                        }
                        writer.EndMessage();
                    }
                    writer.EndMessage();
                }
                writer.EndMessage();

                AmongUsClient.Instance.SendOrDisconnect(writer);
                writer.Recycle();
            }
            public abstract IGameOptions BuildGameOptions();

            public virtual bool AmValid() => true;
        }
    }
