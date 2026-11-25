using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Linq;
using InnerNet;
using Mathf = UnityEngine.Mathf;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Neutral;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.AddOns;

namespace TownOfHostL.Modules
{
    public class PlayerGameOptionsSender : GameOptionsSender
    {
        public static void SetDirty(PlayerControl player) => SetDirty(player.PlayerId);
        public static void SetDirty(byte playerId) =>
            AllSenders.OfType<PlayerGameOptionsSender>()
            .Where(sender => sender.player.PlayerId == playerId)
            .ToList().ForEach(sender => sender.SetDirty());
        public static void SetDirtyToAll() =>
            AllSenders.OfType<PlayerGameOptionsSender>()
            .ToList().ForEach(sender => sender.SetDirty());

        private IGameOptions cachedGameOptions = null;
        public override IGameOptions BasedGameOptions =>
            Main.RealOptionsData.Restore(
                cachedGameOptions ??= new NormalGameOptionsV12(new UnityLogger().Cast<ILogger>()).Cast<IGameOptions>()
            );
        public override bool IsDirty { get; protected set; }

        public PlayerControl player;
        // 最後に送った設定のバイト列。同じなら送らない。会議中に送った物は覚えない (会議の後に必ず送り直す)
        byte[] lastSent;
        bool skippedLastSend;

        public PlayerGameOptionsSender(PlayerControl player)
        {
            this.player = player;
        }
        public void SetDirty() => IsDirty = true;
        /// <summary>組み立て済みの設定を、この人にだけそのまま送る。</summary>
        public void SendRaw(IGameOptions opt) => SendBuiltOptions(opt);

        public override void SendGameOptions()
        {
            // 懐中電灯を点けている間は、送ったかくれんぼ型の設定を上書きしない (終わりに Flashlight が送り直す)
            if (player != null && Flashlight.IsHolding(player.PlayerId)) return;
            var opt = BuildGameOptions();
            if (opt == null) return;
            if (player.AmOwner)
            {
                foreach (var com in GameManager.Instance.LogicComponents)
                {
                    if (com.TryCast<LogicOptions>(out var lo))
                    {
                        lo.SetGameOptions(opt);
                        lo.ClearDirtyFlag();
                    }
                }
                GameOptionsManager.Instance.CurrentGameOptions = opt;
            }
            else
            {
                SendBuiltOptions(opt);
            }
        }

        protected override bool ShouldSend(Il2CppStructArray<byte> optionArray)
        {
            int n = optionArray.Length;
            bool same = lastSent != null && lastSent.Length == n;
            if (same)
            {
                for (int i = 0; i < n; i++)
                    if (lastSent[i] != optionArray[i]) { same = false; break; }
            }
            return !same;
        }

        // 送り終えてから覚える (送る途中で例外になった物を「送った」とみなすと、同じ内容を二度と送らない)
        protected override void OnSent(Il2CppStructArray<byte> optionArray)
        {
            // 客の接続が引けずに送らなかった時は覚えない (覚えると、繋がった後に同じ内容を送らない)
            if (skippedLastSend) { lastSent = null; return; }
            int n = optionArray.Length;
            if (GameStates.IsMeeting) lastSent = null;
            else
            {
                if (lastSent == null || lastSent.Length != n) lastSent = new byte[n];
                for (int i = 0; i < n; i++) lastSent[i] = optionArray[i];
            }
        }

        public override string Describe() => player != null ? $"{player.PlayerId}" : "null";

        public override void SendOptionsArray(Il2CppStructArray<byte> optionArray)
        {
            // 宛先 -1 は全員宛てになり、この人の設定 (視界・クールなど) が全員に効いてしまう
            skippedLastSend = player.LacksClient("GameOptions");
            if (skippedLastSend) return;
            for (byte i = 0; i < GameManager.Instance.LogicComponents.Count; i++)
            {
                if (GameManager.Instance.LogicComponents[i].TryCast<LogicOptions>(out _))
                {
                    SendOptionsArray(optionArray, i, player.GetClientId());
                }
            }
        }
        public static void RemoveSender(byte playerId)
        {
            var sender = AllSenders.OfType<PlayerGameOptionsSender>()
            .FirstOrDefault(sender => sender.player != null && sender.player.PlayerId == playerId);
            if (sender == null) return;
            sender.player = null;
            AllSenders.Remove(sender);
        }
        public override IGameOptions BuildGameOptions()
        {
            if (Main.RealOptionsData == null)
            {
                Main.RealOptionsData = new OptionBackupData(GameOptionsManager.Instance.CurrentGameOptions);
            }

            var opt = BasedGameOptions;
            AURoleOptions.SetOpt(opt);
            if (Roles.Impostor.NormalViper.RoleInfo.IsEnable)
                AURoleOptions.ViperDissolveTime = Roles.Impostor.NormalViper.DissolveTime;
            var state = PlayerState.GetByPlayerId(player.PlayerId);
            opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultCrewmateVision);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, Main.DefaultImpostorVision);

            CustomRoles role = player.GetCustomRole();
            switch (role.GetCustomRoleTypes())
            {
                case CustomRoleTypes.Impostor:
                    AURoleOptions.ShapeshifterCooldown = Options.DefaultShapeshiftCooldown.GetFloat();
                    break;
                case CustomRoleTypes.Madmate:
                    AURoleOptions.EngineerCooldown = Options.MadmateVentCooldown.GetFloat();
                    AURoleOptions.EngineerInVentMaxTime = Options.MadmateVentMaxTime.GetFloat();
                    if (Options.MadmateHasImpostorVision.GetBool())
                        opt.SetVision(true);
                    if (Options.MadmateCanSeeOtherVotes.GetBool())
                        opt.SetBool(BoolOptionNames.AnonymousVotes, false);
                    break;
            }
            if (role.IsCCCatRoles())
            {
                CatchCat.CatPlayer.ApplyGameOptions(player, opt);
            }

            var roleClass = player.GetRoleClass();
            roleClass?.ApplyGameOptions(opt);
            var noisemaker = Main.AllPlayerControls.FirstOrDefault(pc =>
                pc.GetCustomRole().GetRoleTypes() == RoleTypes.Noisemaker);
            noisemaker?.GetRoleClass()?.ApplyGameOptions(opt);
            foreach (var subRole in player.GetCustomSubRoles())
                AddOnList.Get(subRole)?.ApplyGameOptions?.Invoke(opt);
            HamoMadmate.ApplyVision(player, opt);
            HamoModes.ApplyGameOptions(player, opt);
            Blinder.ApplyGameOptionsByOther(player.PlayerId, opt);
            Streaming.Audience.ApplyGameOptions(player.PlayerId, opt);

            if (Main.AllPlayerKillCooldown.TryGetValue(player.PlayerId, out var killCooldown))
            {
                AURoleOptions.KillCooldown = Mathf.Max(0f, killCooldown);
            }

            AURoleOptions.PlayerSpeedMod = Mathf.Clamp(PlayerSpeed.Get(player.PlayerId), Main.MinSpeed, 3f);

            state.taskState.hasTasks = Utils.HasTasks(player.Data, false);
            if (GmOptions.IsGM(player) ? GmOptions.Get(GmOptions.CanSeeVotes) : !Options.GhostCantSeeOtherVotes.GetBool() && player.Data.IsDead)
                opt.SetBool(BoolOptionNames.AnonymousVotes, false);
            if (Options.AdditionalEmergencyCooldown.GetBool() &&
                Options.AdditionalEmergencyCooldownThreshold.GetInt() <= Utils.AllAlivePlayersCount)
            {
                opt.SetInt(
                    Int32OptionNames.EmergencyCooldown,
                    Options.AdditionalEmergencyCooldownTime.GetInt());
            }
            if (Options.SyncButtonMode.GetBool() && Options.SyncedButtonCount.GetValue() <= Options.UsedButtonCount)
            {
                opt.SetInt(Int32OptionNames.EmergencyCooldown, 3600);
            }
            // 暗転 (キルの閃光・通報・ステルス) は、視界を書き換える役職や属性 (ライター・ブラインダー・サングラスなど) より後に書く
            if (state.IsBlackOut)
            {
                opt.SetFloat(FloatOptionNames.CrewLightMod, 0f);
                opt.SetFloat(FloatOptionNames.ImpostorLightMod, 0f);
            }
            // 鬼の体の差し替えの間は、自分の周りだけが見えるまで暗くする。
            // 元の視界の広さ (サングラス・停電など) によらず同じ半径にそろえ、自分の姿と名前の下の文字が切れないようにする
            if (SeekerBody.IsDimmed(player.PlayerId) && !state.IsBlackOut)
            {
                opt.SetFloat(FloatOptionNames.CrewLightMod, SeekerBody.DimVision * SeekerBody.CrewLightCompensation());
                opt.SetFloat(FloatOptionNames.ImpostorLightMod, SeekerBody.DimVision);
            }
            MeetingTimeManager.ApplyGameOptions(opt);
            // 視界の値が決まった後 (暗転・会議の時間より後) に、懐中電灯の半径のずれを足す
            Flashlight.ApplyGameOptions(player, opt);

            AURoleOptions.ShapeshifterCooldown = Mathf.Max(1f, AURoleOptions.ShapeshifterCooldown);
            AURoleOptions.ProtectionDurationSeconds = 0f;

            return opt;
        }

        public override bool AmValid()
        {
            return base.AmValid() && player != null && player.Data != null && !player.Data.Disconnected && Main.RealOptionsData != null;
        }
    }
}