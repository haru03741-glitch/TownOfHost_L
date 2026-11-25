using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TownOfHostL.Core;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Neutral;

/// <summary>
/// 妖狐 (Nebula on the Ship の Spectre)<br/>
/// お揚げを食べて満腹度を保ちながら生き残る。満腹度が勝利に必要な量より多いまま生きていれば、
/// サボタージュ以外の勝ちを乗っ取る。満腹度が尽きると餓死する。満腹度を使って透明化できる。<br/>
/// 素の客で遊べるよう、皿は名前の文字で描いた物 (CNO) で置き、食べるのは皿のそばに少し立つことで行う。
/// </summary>
public sealed class Spectre : RoleBase, IKiller, IUsePhantomButton
{
    // 役職の元: Nebula on the Ship
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Spectre),
            player => new Spectre(player),
            CustomRoles.Spectre,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 51500,
            SetupOptionItem,
            "spe",
            "#b998c5",
            isDesyncImpostor: true,
            countType: CountTypes.Fox,
            assignInfo: new RoleAssignInfo(CustomRoles.Spectre, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 15, 1),
                AssignUnitRolesGetter = GetAssignUnit,
            },
            winTakeOvers: [new(WinTakeOverStage.AfterSoloWins, 100, (ref GameOverReason reason, CustomWinner _) => CheckSpectreWin(ref reason))]
        );
    public Spectre(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        LeftAliveTime = InitialSatiety * SatietyRate;
        lastShown = "";
        killerIds.Clear();
    }

    #region 設定
    static OptionItem OptionCanVent;
    static OptionItem OptionShowWhereKillersAre;
    static OptionItem OptionImmoralistVariation;
    static OptionItem OptionSpectreFollowerChance;
    static OptionItem OptionNumOfDishes;
    static OptionItem OptionSatietyRate;
    static OptionItem OptionMaxSatiety;
    static OptionItem OptionInitialSatiety;
    static OptionItem OptionRequiredSatietyForWinning;
    static OptionItem OptionShowDishesOnMap;
    static OptionItem OptionFriesReplenishmentCooldown;
    static OptionItem OptionVanishCost;
    static OptionItem OptionVanishCooldown;
    static OptionItem OptionVanishDuration;
    static OptionItem OptionAdrenaline;
    static OptionItem OptionAdrenalineRate;

    // 子狐・背徳者の設定 (この役職の設定の下に並べる)
    internal static OptionItem OptionFollowerCanVent;
    internal static OptionItem OptionFollowerSatietyRate;
    internal static OptionItem OptionFollowerShowDishesOnMap;
    internal static OptionItem OptionImmoralistCanVent;
    internal static OptionItem OptionImmoralistRespawnCooldown;
    internal static OptionItem OptionImmoralistRespawnDuration;
    internal static OptionItem OptionImmoralistShowDishesOnMap;
    internal static OptionItem OptionImmoralistCanSuicide;
    internal static OptionItem OptionImmoralistShowKillFlash;

    enum OptionName
    {
        SpectreShowWhereKillersAre,
        SpectreImmoralistVariation,
        SpectreFollowerChance,
        SpectreNumOfDishes,
        SpectreSatiety,
        SpectreMaxSatiety,
        SpectreInitialSatiety,
        SpectreRequiredSatietyForWinning,
        SpectreShowDishesOnMap,
        SpectreFriesReplenishmentCooldown,
        SpectreVanishCost,
        SpectreVanishCooldown,
        SpectreVanishDuration,
        SpectreAdrenaline,
        SpectreAdrenalineRate,
        SpectreFollowerSatietyRate,
        SpectreFollowerShowDishesOnMap,
        SpectreImmoralistRespawnCooldown,
        SpectreImmoralistRespawnDuration,
        SpectreImmoralistShowDishesOnMap,
        SpectreImmoralistCanSuicide,
        SpectreImmoralistShowKillFlash,
    }
    public enum ImmoralistVariation
    {
        SpectreVariation_None,
        SpectreVariation_Follower,
        SpectreVariation_Immoralist,
        SpectreVariation_Random,
        SpectreVariation_Alternate,
    }

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, defo: 15);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, true, false);
        OptionShowWhereKillersAre = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreShowWhereKillersAre, true, false);
        OptionImmoralistVariation = StringOptionItem.Create(RoleInfo, OptionName.SpectreImmoralistVariation, EnumHelper.GetAllNames<ImmoralistVariation>(), 0, false);
        OptionSpectreFollowerChance = IntegerOptionItem.Create(RoleInfo, OptionName.SpectreFollowerChance, new(10, 90, 1), 50, false)
            .SetValueFormat(OptionFormat.Percent);

        ObjectOptionitem.Create(RoleInfo, "SpectreDishSetting", true, null);
        OptionNumOfDishes = IntegerOptionItem.Create(RoleInfo, OptionName.SpectreNumOfDishes, new(0, 20, 1), 6, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionSatietyRate = FloatOptionItem.Create(RoleInfo, OptionName.SpectreSatiety, new(20f, 100f, 2.5f), 45f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxSatiety = FloatOptionItem.Create(RoleInfo, OptionName.SpectreMaxSatiety, new(1f, 8f, 0.25f), 5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionInitialSatiety = FloatOptionItem.Create(RoleInfo, OptionName.SpectreInitialSatiety, new(0.5f, 8f, 0.25f), 4f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionRequiredSatietyForWinning = FloatOptionItem.Create(RoleInfo, OptionName.SpectreRequiredSatietyForWinning, new(0f, 7.75f, 0.25f), 4f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionShowDishesOnMap = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreShowDishesOnMap, true, false);
        OptionFriesReplenishmentCooldown = FloatOptionItem.Create(RoleInfo, OptionName.SpectreFriesReplenishmentCooldown, new(20f, 240f, 5f), 80f, false)
            .SetValueFormat(OptionFormat.Seconds);

        ObjectOptionitem.Create(RoleInfo, "SpectreVanishSetting", true, null);
        OptionVanishCost = FloatOptionItem.Create(RoleInfo, OptionName.SpectreVanishCost, new(0f, 2f, 0.25f), 0.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionVanishCooldown = FloatOptionItem.Create(RoleInfo, OptionName.SpectreVanishCooldown, new(0f, 60f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVanishDuration = FloatOptionItem.Create(RoleInfo, OptionName.SpectreVanishDuration, new(2.5f, 30f, 2.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);

        ObjectOptionitem.Create(RoleInfo, "SpectreAdrenalineSetting", true, null);
        OptionAdrenaline = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreAdrenaline, false, false);
        OptionAdrenalineRate = FloatOptionItem.Create(RoleInfo, OptionName.SpectreAdrenalineRate, new(0f, 0.875f, 0.125f), 0.25f, false, OptionAdrenaline)
            .SetValueFormat(OptionFormat.Multiplier);

        ObjectOptionitem.Create(RoleInfo, "SpectreFollowerSetting", true, null);
        OptionFollowerCanVent = BooleanOptionItem.Create(RoleInfo, "SpectreFollowerCanVent", true, false);
        OptionFollowerSatietyRate = FloatOptionItem.Create(RoleInfo, OptionName.SpectreFollowerSatietyRate, new(0.25f, 2f, 0.25f), 0.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionFollowerShowDishesOnMap = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreFollowerShowDishesOnMap, true, false);

        ObjectOptionitem.Create(RoleInfo, "SpectreImmoralistSetting", true, null);
        OptionImmoralistCanVent = BooleanOptionItem.Create(RoleInfo, "SpectreImmoralistCanVent", true, false);
        OptionImmoralistRespawnCooldown = FloatOptionItem.Create(RoleInfo, OptionName.SpectreImmoralistRespawnCooldown, new(0f, 60f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionImmoralistRespawnDuration = FloatOptionItem.Create(RoleInfo, OptionName.SpectreImmoralistRespawnDuration, new(0.5f, 10f, 0.5f), 3f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionImmoralistShowDishesOnMap = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreImmoralistShowDishesOnMap, false, false);
        OptionImmoralistCanSuicide = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreImmoralistCanSuicide, true, false);
        OptionImmoralistShowKillFlash = BooleanOptionItem.Create(RoleInfo, OptionName.SpectreImmoralistShowKillFlash, true, false);
    }

    internal static float SatietyRate => OptionSatietyRate.GetFloat();
    internal static float MaxSatiety => OptionMaxSatiety.GetFloat();
    static float InitialSatiety => OptionInitialSatiety.GetFloat();
    internal static float RequiredSatiety => OptionRequiredSatietyForWinning.GetFloat();
    internal static float FriesReplenishmentCooldown => OptionFriesReplenishmentCooldown.GetFloat();
    internal static int NumOfDishes => OptionNumOfDishes.GetInt();
    static float VanishCost => OptionVanishCost.GetFloat();
    #endregion

    #region 子狐・背徳者の割り当て
    static bool alternateFlag = false;
    [Attributes.GameModuleInitializer]
    public static void InitGame()
    {
        alternateFlag = false;
        PairMap.Clear();
        paired = false;
        SpectreDishes.Init();
    }
    /// <summary>妖狐 1 人ごとに配る役職の組 (設定で子狐・背徳者を付ける)</summary>
    static CustomRoles[] GetAssignUnit()
    {
        CustomRoles? follower = (ImmoralistVariation)OptionImmoralistVariation.GetValue() switch
        {
            ImmoralistVariation.SpectreVariation_Follower => CustomRoles.SpectreFollower,
            ImmoralistVariation.SpectreVariation_Immoralist => CustomRoles.SpectreImmoralist,
            ImmoralistVariation.SpectreVariation_Random =>
                IRandom.Instance.Next(100) < OptionSpectreFollowerChance.GetInt() ? CustomRoles.SpectreFollower : CustomRoles.SpectreImmoralist,
            ImmoralistVariation.SpectreVariation_Alternate =>
                (alternateFlag = !alternateFlag) ? CustomRoles.SpectreImmoralist : CustomRoles.SpectreFollower,
            _ => null,
        };
        return follower is CustomRoles f ? [CustomRoles.Spectre, f] : [CustomRoles.Spectre];
    }

    /// <summary>子狐・背徳者 → その親の妖狐</summary>
    static readonly Dictionary<byte, byte> PairMap = new();
    static bool paired;
    /// <summary>子狐・背徳者を親の妖狐と組にする (ホスト。全員の役職ができた後に 1 回)</summary>
    static void EnsurePaired()
    {
        if (paired || !AmongUsClient.Instance.AmHost) return;
        paired = true;
        var spectres = Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.Spectre)).OrderBy(pc => pc.PlayerId).ToList();
        var followers = Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.SpectreFollower) || pc.Is(CustomRoles.SpectreImmoralist))
            .OrderBy(pc => pc.PlayerId).ToList();
        int i = 0;
        foreach (var follower in followers)
        {
            if (spectres.Count == 0) break;
            var spectre = spectres[i % spectres.Count];
            i++;
            PairMap[follower.PlayerId] = spectre.PlayerId;
            // 妖狐と仲間は、互いを妖狐の色で見る
            NameColorManager.Add(spectre.PlayerId, follower.PlayerId, RoleInfo.RoleColorCode);
            NameColorManager.Add(follower.PlayerId, spectre.PlayerId, RoleInfo.RoleColorCode);
            Logger.Info($"{follower.GetNameWithRole()} => {spectre.GetNameWithRole()}", "Spectre.Pair");
        }
    }
    /// <summary>この子狐・背徳者の親の妖狐</summary>
    internal static Spectre GetMySpectre(byte followerId)
    {
        EnsurePaired();
        if (!PairMap.TryGetValue(followerId, out var id)) return null;
        return Utils.GetPlayerById(id)?.GetRoleClass() as Spectre;
    }
    IEnumerable<byte> TeamMemberIds() => PairMap.Where(kv => kv.Value == Player.PlayerId).Select(kv => kv.Key);
    internal void NotifyTeam()
    {
        Utils.NotifyRoles(SpecifySeer: Player);
        foreach (var id in TeamMemberIds())
            if (Utils.GetPlayerById(id) is PlayerControl pc) Utils.NotifyRoles(SpecifySeer: pc);
    }
    #endregion

    #region 満腹度
    /// <summary>残りの生存時間 (秒)。満腹度 = これ / お揚げ 1 個あたりの腹持ち</summary>
    public float LeftAliveTime { get; private set; }
    public float Satiety => LeftAliveTime / SatietyRate;
    public void AddSatiety(float satiety)
    {
        LeftAliveTime = Mathf.Clamp(LeftAliveTime + satiety * SatietyRate, 0f, MaxSatiety * SatietyRate);
        RefreshDisplay(force: true);
    }
    bool Won => Player.IsAlive() && Satiety > RequiredSatiety;

    string lastShown;
    void RefreshDisplay(bool force = false)
    {
        var now = SatietyText();
        if (!force && now == lastShown) return;
        lastShown = now;
        NotifyTeam();
    }
    internal string SatietyText()
    {
        var s = Mathf.Max(0f, Satiety);
        var color = s > RequiredSatiety ? RoleInfo.RoleColorCode : "#ff8080";
        return $"<{color}>({s:F1}/{MaxSatiety:0.##})</color>";
    }
    #endregion

    readonly HashSet<byte> killerIds = new();
    float killerRefreshTimer;
    // 皿のそばに立っている時間 (食べるまで)
    internal static readonly Dictionary<byte, (int dishId, float time)> Eating = new();
    internal const float EatTime = 2.5f;
    internal const float EatRange = 0.9f;

    public override void OnFixedUpdate(PlayerControl _)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.InMeetingWindow) return;
        EnsurePaired();
        SpectreDishes.Update();

        if (vanishTimer > 0f)
        {
            vanishTimer -= Time.fixedDeltaTime;
            if (vanishTimer <= 0f || !Player.IsAlive()) EndVanish();
        }
        if (!Player.IsAlive()) return;

        // 満腹度が減る (アドレナリンモードでは、キラーのそばで減りにくい)
        var rate = OptionAdrenaline.GetBool() && ThereIsKillerNearbyMe() ? OptionAdrenalineRate.GetFloat() : 1f;
        LeftAliveTime -= Time.fixedDeltaTime * rate;
        if (LeftAliveTime <= 0f)
        {
            LeftAliveTime = 0f;
            var state = PlayerState.GetByPlayerId(Player.PlayerId);
            if (state != null) state.DeathReason = CustomDeathReason.Starved;
            Player.RpcMurderPlayerV2(Player);
            Logger.Info($"{Player.GetNameWithRole()}: 餓死", "Spectre");
            return;
        }
        RefreshDisplay();

        // お揚げを食べる
        SpectreDishes.TryEat(Player, () => AddSatiety(1f));

        // キラーの矢印
        if (OptionShowWhereKillersAre.GetBool())
        {
            killerRefreshTimer -= Time.fixedDeltaTime;
            if (killerRefreshTimer <= 0f)
            {
                killerRefreshTimer = 1f;
                RefreshKillerArrows();
            }
        }
    }

    static bool IsKillerPlayer(PlayerControl pc, out Color color)
    {
        color = Color.white;
        if (pc.GetCustomRole().IsImpostor())
        {
            color = Palette.ImpostorRed;
            return true;
        }
        if (pc.Is(CustomRoles.Sheriff)) return true;
        if (pc.GetRoleClass() is IKiller killer && killer.IsKiller)
        {
            color = Utils.GetRoleColor(pc.GetCustomRole());
            return true;
        }
        return false;
    }
    bool ThereIsKillerNearbyMe()
        => Main.AllAlivePlayerControls.Any(pc => pc.PlayerId != Player.PlayerId && IsKillerPlayer(pc, out _)
            && Vector2.Distance(pc.transform.position, Player.transform.position) < 4f);
    void RefreshKillerArrows()
    {
        var now = Main.AllAlivePlayerControls.Where(pc => pc.PlayerId != Player.PlayerId && IsKillerPlayer(pc, out _))
            .Select(pc => pc.PlayerId).ToHashSet();
        var changed = false;
        foreach (var id in killerIds.Where(id => !now.Contains(id)).ToArray())
        {
            TargetArrow.Remove(Player.PlayerId, id);
            killerIds.Remove(id);
            changed = true;
        }
        foreach (var id in now.Where(id => !killerIds.Contains(id)).ToArray())
        {
            TargetArrow.Add(Player.PlayerId, id);
            killerIds.Add(id);
            changed = true;
        }
        if (changed) Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnReportDeadBody(PlayerControl _, NetworkedPlayerInfo __)
    {
        // 会議の始めに、満腹度を最低 20 秒分にする
        LeftAliveTime = Mathf.Max(20f, LeftAliveTime);
        Eating.Clear();
        EndVanish();
    }
    public override void OnRevived()
    {
        LeftAliveTime = 40f;
        RefreshDisplay(force: true);
    }

    #region 透明化 (ファントムのボタン)
    // 素の客の手元では妖狐はクルーメイトなので、ファントムの消える電文が効かない。
    // 隠れんぼの鬼 (VanillaHnsHiddenSeeker) と同じく、ほかの人にだけ「一番近いベントに入った」と送って姿を消し、
    // 効果が切れたらベントから出して本当の位置へ戻す
    float vanishTimer;
    int vanishVent = -1;
    ushort sentSid;
    bool hasSentSid;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        // 満腹度を使っても、お揚げ 1 個分の満腹度を保てる時だけ
        if (vanishTimer > 0f || !Player.IsAlive() || Player.inVent || !((VanishCost + 1f) < Satiety))
        {
            ResetCooldown = false;
            return;
        }
        ResetCooldown = true;
        AddSatiety(-VanishCost);
        StartVanish();
        Logger.Info($"{Player.GetNameWithRole()}: 透明化 (満腹度 {Satiety:F2})", "Spectre");
    }

    void StartVanish()
    {
        var vent = NearestVent(Player.GetTruePosition());
        if (vent == null) return;
        vanishVent = vent.Id;
        vanishTimer = OptionVanishDuration.GetFloat();
        // 本編の客はベントに入る人をベントまで歩かせるので、先にベントの上へ置いてその場で入らせる
        SendSnapToOthers((Vector2)vent.transform.position - Player.Collider.offset);
        SendVentToOthers(RpcCalls.EnterVent, vanishVent);
        Utils.NotifyRoles(SpecifySeer: Player);
    }
    void EndVanish()
    {
        if (vanishVent < 0) return;
        var ventId = vanishVent;
        vanishVent = -1;
        vanishTimer = 0f;
        SendVentToOthers(RpcCalls.ExitVent, ventId);
        // 本編の客はベントから出す時に人をそのベントの上へ置くので、出し終えた頃に本当の位置へ戻す
        var target = Player;
        _ = new LateTask(() =>
        {
            if (target && target.Data != null && !target.Data.Disconnected) SendSnapToOthers((Vector2)target.transform.position);
        }, 0.25f, "Spectre.SnapBack");
        Utils.NotifyRoles(SpecifySeer: Player);
    }
    void SendVentToOthers(RpcCalls call, int ventId)
    {
        try
        {
            foreach (var pc in Main.AllPlayerControls)
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected || pc.AmOwner || pc.PlayerId == Player.PlayerId) continue;
                int clientId = pc.GetClientId();
                if (clientId < 0) continue;
                var writer = AmongUsClient.Instance.StartRpcImmediately(Player.MyPhysics.NetId, (byte)call, Hazel.SendOption.Reliable, clientId);
                writer.WritePacked(ventId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }
        catch (Exception ex) { Logger.Exception(ex, "Spectre"); }
    }
    void SendSnapToOthers(Vector2 pos)
    {
        try
        {
            var net = Player.NetTransform;
            ushort sid = (ushort)(net.lastSequenceId + 3);
            // 止まっているとホストの番号は進まないので、前に送った番号 + 入る・出るの 2 つ分より先にする
            if (hasSentSid && NetHelpers.SidGreaterThan((ushort)(sentSid + 3), sid)) sid = (ushort)(sentSid + 3);
            sentSid = sid;
            hasSentSid = true;
            foreach (var pc in Main.AllPlayerControls)
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected || pc.AmOwner || pc.PlayerId == Player.PlayerId) continue;
                int clientId = pc.GetClientId();
                if (clientId < 0) continue;
                var writer = AmongUsClient.Instance.StartRpcImmediately(net.NetId, (byte)RpcCalls.SnapTo, Hazel.SendOption.Reliable, clientId);
                NetHelpers.WriteVector2(pos, writer);
                writer.Write(sid);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }
        catch (Exception ex) { Logger.Exception(ex, "Spectre"); }
    }
    static Vent NearestVent(Vector2 pos)
    {
        Vent best = null;
        float bestDist = float.MaxValue;
        if (!ShipStatus.Instance) return null;
        foreach (var v in ShipStatus.Instance.AllVents)
        {
            if (v == null) continue;
            float d = Vector2.Distance(pos, v.transform.position);
            if (d < bestDist) { bestDist = d; best = v; }
        }
        return best;
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionVanishCooldown.GetFloat() + OptionVanishDuration.GetFloat();
        opt.SetVision(true);
    }
    public override string GetAbilityButtonText() => GetString("SpectreVanishButtonText");
    #endregion

    #region キルボタン (使わない)
    public bool CanKill => false;
    public bool IsKiller => false;
    public bool CanUseKillButton() => false;
    public float CalculateKillCooldown() => 999f;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => OptionCanVent.GetBool();
    #endregion

    #region 表示
    public override string GetProgressText(bool comms = false) => SatietyText();
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId || !Player.IsAlive()) return "";
        var arrows = "";
        foreach (var id in killerIds)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || !pc.IsAlive() || !IsKillerPlayer(pc, out var color)) continue;
            var arrow = TargetArrow.GetArrows(Player, id);
            if (arrow != "") arrows += Utils.ColorString(color, arrow);
        }
        return arrows;
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId || !Player.IsAlive()) return "";
        var text = SpectreDishes.LowerText(Player, OptionShowDishesOnMap.GetBool(), full: true);
        if (vanishVent < 0) return text;
        var vanish = Utils.ColorString(RoleInfo.RoleColor, GetString("SpectreVanishing"));
        return text == "" ? vanish : vanish + "\n" + text;
    }
    #endregion

    #region 勝利
    static bool CheckSpectreWin(ref GameOverReason reason)
    {
        // サボタージュによる終わりは乗っ取らない
        if (reason is GameOverReason.ImpostorsBySabotage) return false;
        var winners = CustomRoleManager.AllActiveRoles.Values.OfType<Spectre>().Where(s => s.Won).ToArray();
        if (winners.Length == 0) return false;
        if (!CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Spectre, winners[0].Player.PlayerId)) return false;
        foreach (var s in winners)
        {
            CustomWinnerHolder.WinnerIds.Add(s.Player.PlayerId);
            CustomWinnerHolder.NeutralWinnerIds.Add(s.Player.PlayerId);
            // 同じ組の子狐・背徳者も勝つ
            foreach (var id in s.TeamMemberIds()) CustomWinnerHolder.WinnerIds.Add(id);
        }
        reason = GameOverReason.ImpostorsByKill;
        return true;
    }
    #endregion
}

/// <summary>妖狐のお揚げの皿 (ホストのみ)。皿は誰にでも見える (CNO)</summary>
public static class SpectreDishes
{
    public sealed class Dish
    {
        public int Id;
        public Vector2 Position;
        public bool Ate = true;
        public float RespawnCooldown;
        public CustomNetObject Cno;
    }
    public static readonly List<Dish> All = new();
    static bool spawned;
    static float lastUpdate = -1f;
    static readonly float[] InitialRespawnCoeff = [1.0f, 0.6f, 0.2f, 0.5f, 0.8f, 0.3f, 0.7f, 0.4f, 0.9f, 1.0f, 1.0f, 1.0f, 1.0f];

    // 4×6 マスの絵。体を覆う大きさ (ポータルの絵に合わせる)
    const string Prefix = "<size=70%><line-height=97%><cspace=0.16em>";
    static string Row(string hex) => $"<alpha=#FF><mark=#{hex}FF><alpha=#00>WWWW</mark>";
    static readonly string FullSprite = Prefix + string.Join("\n", Row("7a4a1e"), Row("e3a33c"), Row("e3a33c"), Row("c9822a"), Row("f0f0f0"), Row("bdbdbd"));
    static readonly string EmptySprite = Prefix + string.Join("\n", Row("7a4a1e"), Row("dcdcdc"), Row("dcdcdc"), Row("dcdcdc"), Row("f0f0f0"), Row("bdbdbd"));

    public static void Init()
    {
        All.Clear();
        spawned = false;
        lastUpdate = -1f;
        Spectre.Eating.Clear();
    }

    static RandomSpawn.SpawnMap GetSpawnMap() => Main.NormalOptions.MapId switch
    {
        0 or 3 => new RandomSpawn.SkeldSpawnMap(),
        1 => new RandomSpawn.MiraHQSpawnMap(),
        2 => new RandomSpawn.PolusSpawnMap(),
        4 => new RandomSpawn.AirshipSpawnMap(),
        5 => new RandomSpawn.FungleSpawnMap(),
        _ => null,
    };

    /// <summary>皿を置く (試合で 1 回)。部屋の湧き場所から、互いに 7.5 以上離れた所を選ぶ</summary>
    static void Spawn()
    {
        spawned = true;
        var map = GetSpawnMap();
        if (map == null || Spectre.NumOfDishes <= 0) return;
        var pool = map.Positions.Values
            .Select(p => Main.NormalOptions.MapId == 3 ? new Vector2(-p.x, p.y) : p)
            .OrderBy(_ => IRandom.Instance.Next(int.MaxValue)).ToList();
        var picked = new List<Vector2>();
        foreach (var p in pool)
        {
            if (picked.Count >= Spectre.NumOfDishes) break;
            if (picked.All(q => Vector2.Distance(p, q) >= 7.5f)) picked.Add(p);
        }
        foreach (var p in pool)
        {
            if (picked.Count >= Spectre.NumOfDishes) break;
            if (!picked.Contains(p)) picked.Add(p);
        }
        foreach (var pos in picked)
        {
            var dish = new Dish { Id = All.Count, Position = pos };
            All.Add(dish);
            if (dish.Id % 2 == 0)
            {
                dish.Ate = false;
                dish.RespawnCooldown = Spectre.FriesReplenishmentCooldown;
            }
            else
            {
                dish.Ate = true;
                dish.RespawnCooldown = Spectre.FriesReplenishmentCooldown * InitialRespawnCoeff[Math.Min(dish.Id / 2, InitialRespawnCoeff.Length - 1)];
            }
            dish.Cno = CustomNetObject.Create(dish.Ate ? EmptySprite : FullSprite, pos);
        }
        Logger.Info($"皿を {All.Count} 枚置いた", "Spectre");
    }

    public static void SetState(Dish dish, bool ate)
    {
        dish.Ate = ate;
        dish.RespawnCooldown = Spectre.FriesReplenishmentCooldown;
        dish.Cno?.ChangeSprite(ate ? EmptySprite : FullSprite);
        // 皿の様子を見られる人の表示を更新
        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
            if (role is Spectre or SpectreFollower or SpectreImmoralist) Utils.NotifyRoles(SpecifySeer: role.Player);
    }

    /// <summary>1 フレームに 1 回だけ進める (妖狐が何人いても)</summary>
    public static void Update()
    {
        if (!spawned) Spawn();
        if (lastUpdate == Time.fixedTime) return;
        lastUpdate = Time.fixedTime;
        foreach (var dish in All)
        {
            if (!dish.Ate) continue;
            dish.RespawnCooldown -= Time.fixedDeltaTime;
            if (dish.RespawnCooldown < 0f) SetState(dish, false);
        }
    }

    public static Dish Nearest(Vector2 pos, float range, bool ate)
        => All.Where(d => d.Ate == ate && Vector2.Distance(pos, d.Position) <= range)
            .OrderBy(d => Vector2.Distance(pos, d.Position)).FirstOrDefault();

    /// <summary>お揚げの載った皿のそばに <see cref="Spectre.EatTime"/> 秒立つと食べる</summary>
    public static void TryEat(PlayerControl pc, Action onEat)
    {
        if (pc.inVent || pc.inMovingPlat || pc.onLadder)
        {
            Spectre.Eating.Remove(pc.PlayerId);
            return;
        }
        var dish = Nearest(pc.GetTruePosition(), Spectre.EatRange, ate: false);
        if (dish == null)
        {
            if (Spectre.Eating.Remove(pc.PlayerId)) Utils.NotifyRoles(SpecifySeer: pc);
            return;
        }
        Spectre.Eating.TryGetValue(pc.PlayerId, out var now);
        var time = now.dishId == dish.Id ? now.time + Time.fixedDeltaTime : Time.fixedDeltaTime;
        if (now.dishId != dish.Id || now.time == 0f) Utils.NotifyRoles(SpecifySeer: pc);
        if (time < Spectre.EatTime)
        {
            Spectre.Eating[pc.PlayerId] = (dish.Id, time);
            return;
        }
        Spectre.Eating.Remove(pc.PlayerId);
        SetState(dish, true);
        onEat();
        Logger.Info($"{pc.GetNameWithRole()}: お揚げを食べた (皿 {dish.Id})", "Spectre");
    }

    /// <summary>下の文字: 食べている途中の表示と、お揚げの載った (または空の) 皿のある部屋</summary>
    public static string LowerText(PlayerControl seer, bool showDishes, bool full)
    {
        var text = "";
        if (Spectre.Eating.ContainsKey(seer.PlayerId)) text = GetString("SpectreEating");
        if (showDishes)
        {
            var rooms = All.Where(d => d.Ate != full).Select(d => d.Position.GetShipRoomName()).Where(r => r != "").Distinct().ToArray();
            var dishText = string.Format(GetString(full ? "SpectreFullDishes" : "SpectreEmptyDishes"), rooms.Length == 0 ? "-" : string.Join(" / ", rooms));
            text = text == "" ? dishText : text + "\n" + dishText;
        }
        return text == "" ? "" : Utils.ColorString(Spectre.RoleInfo.RoleColor, text);
    }
}
