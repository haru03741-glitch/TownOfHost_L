using System.Collections.Generic;
using AmongUs.GameOptions;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Modules;

/// <summary>
/// 見る人ごとの本編の役職 (RoleTypes) を 1 か所で扱う (ホストのみ)。
///
/// 表: 見る人 (seer) の画面で、見られる人 (target) をどの役職で送ったかを覚える。送る関所
/// (RpcSetRoleDesync・RpcSetRoleNormal・SetRoleEx) で記録するので、呼び出し元は気にしなくてよい。
/// 幽霊の役職は記録しない (生きている時の見え方だけを持ち、復活で戻すのに使う)。
/// 会議中に AntiBlackout が送る一時的な役職は関所を通らないので記録されない。
/// 役職が替わった人の行は消す (PlayerState.SetMainRole)。替えた側が送り直せば、また記録される。
///
/// 画面の表: 見る人の画面に今ある target の役職を、幽霊・一時の見え方 (暗転対策の身代わり・溶かしの Viper など) も含めて覚える。
/// 客へ本編の SetRole を書く所は、どれも <see cref="Note"/> か関所を通す (tools/check-setrole-entry.ps1)。
/// ホストの画面は、手元だけで役職を付ける所 (CoSetRole・RoleManager.SetRole) があるので表を使わず、手元の役職を直に読む。
/// 役職が替わっても客の画面は変わらないので、こちらは役職替えでは消さない。target が死んだ時は、客が手元で幽霊にするので消す。
///
/// 規則 (<see cref="Default"/>): 表に無い組と、役職の基底が変わった時の見え方を決める。
/// 会議明けの配り直しと復活は、表 → 無ければ規則、の順に引いて送る。
/// </summary>
public static class RoleView
{
    private static readonly Dictionary<(byte seer, byte target), RoleTypes> living = new();
    private static readonly Dictionary<(byte seer, byte target), RoleTypes> onScreen = new();

    [GameModuleInitializer]
    public static void Init()
    {
        living.Clear();
        onScreen.Clear();
    }

    // ---- 表 ----

    /// <summary>関所 (生きている時の見え方として送った物) の記録。幽霊の役職は画面の表にだけ書く</summary>
    internal static void Record(PlayerControl seer, PlayerControl target, RoleTypes role)
        => Note(seer, target, role, transient: false);

    /// <summary>
    /// seer の画面へ target の SetRole を送ったことを覚える。
    /// transient: 一時の見え方 (後で戻す) で、生きている時の見え方の表には書かない。
    /// keepsCooldown: 送る側が能力のクールを続けさせる (同じ包みで残りの秒から始め直す) ので、数えた残りを消さない
    /// </summary>
    internal static void Note(PlayerControl seer, PlayerControl target, RoleTypes role, bool transient, bool keepsCooldown = false)
    {
        if (seer == null || target == null) return;
        // 試合の始めの配り (イントロ中) も覚えるよう、InGame が立つ前から記録する
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || AmongUsClient.Instance.GameState != AmongUsClient.GameStates.Started) return;
        onScreen[(seer.PlayerId, target.PlayerId)] = role;
        // 本人へ役職を送ると、客の手元で役職が作り直され、能力のクールが 0 になる
        if (seer.PlayerId == target.PlayerId && !keepsCooldown) AbilityCooldownClock.Clear(target.PlayerId);
        if (transient || RoleManager.IsGhostRole(role)) return;
        living[(seer.PlayerId, target.PlayerId)] = role;
        SeekerBody.OnRoleSent(target);
    }

    /// <summary>
    /// 送らずに、生きている時の見え方だけを戻す (会議の後の配り直しが表から引く)。客の画面は変えていないので画面の表は書かない
    /// </summary>
    internal static void RestoreLiving(PlayerControl seer, PlayerControl target, RoleTypes role)
    {
        if (seer == null || target == null || RoleManager.IsGhostRole(role)) return;
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || AmongUsClient.Instance.GameState != AmongUsClient.GameStates.Started) return;
        living[(seer.PlayerId, target.PlayerId)] = role;
    }

    internal static void RecordByClientId(int clientId, PlayerControl target, RoleTypes role)
        => NoteByClientId(clientId, target, role, transient: false);

    internal static void NoteByClientId(int clientId, PlayerControl target, RoleTypes role, bool transient)
    {
        foreach (var seer in Main.AllPlayerControls)
            if (seer.GetClientId() == clientId) { Note(seer, target, role, transient); return; }
    }

    /// <summary>
    /// 記録せずに SetRole を送った (試合の始めの配りの一部など)。画面の表のその組を「分からない」にする。
    /// seer が null なら全員の画面の target
    /// </summary>
    internal static void Unknown(PlayerControl seer, PlayerControl target)
    {
        if (target == null) return;
        if (seer != null) { onScreen.Remove((seer.PlayerId, target.PlayerId)); return; }
        foreach (var pc in Main.AllPlayerControls) onScreen.Remove((pc.PlayerId, target.PlayerId));
    }

    /// <summary>target が死んだ。客は手元で幽霊にするので、全員の画面の target を分からないにする</summary>
    internal static void OnDied(PlayerControl target) => Unknown(null, target);

    /// <summary>seer の画面に今ある target の役職 (ホストの画面は手元の役職・客の画面は送った物を覚えている時だけ)</summary>
    public static bool TryGetOnScreen(PlayerControl seer, PlayerControl target, out RoleTypes role)
    {
        if (seer.AmOwner)
        {
            var current = target.Data != null ? target.Data.Role : null;
            role = current != null ? current.Role : default;
            return current != null;
        }
        return onScreen.TryGetValue((seer.PlayerId, target.PlayerId), out role);
    }

    /// <summary>
    /// seer の画面に今ある target の役職。表に無い組は生きている時の見え方で代える
    /// (試合の始めの配りは、他人の見え方を SetRole でなく GameData で送るので表に入らない)
    /// </summary>
    public static RoleTypes Screen(PlayerControl target, PlayerControl seer)
        => TryGetOnScreen(seer, target, out var role) ? role : Living(target, seer);

    internal static void RecordForAll(PlayerControl target, RoleTypes role)
    {
        foreach (var seer in Main.AllPlayerControls) Record(seer, target, role);
    }

    /// <summary>役職が替わった人の行 (その人の見え方と、その人から見た見え方) を消す</summary>
    public static void Forget(byte playerId)
    {
        if (living.Count == 0) return;
        var stale = new List<(byte, byte)>();
        foreach (var key in living.Keys)
            if (key.seer == playerId || key.target == playerId) stale.Add(key);
        foreach (var key in stale) living.Remove(key);
    }

    public static bool TryGet(PlayerControl seer, PlayerControl target, out RoleTypes role)
        => living.TryGetValue((seer.PlayerId, target.PlayerId), out role);

    /// <summary>生きている target を seer の画面でどう見せるか (表 → 無ければ規則)</summary>
    public static RoleTypes Living(PlayerControl target, PlayerControl seer)
        => TryGet(seer, target, out var role) ? role : Default(target, seer);

    // ---- 規則 ----

    /// <summary>本人の画面の本人の役職 (基底)</summary>
    public static RoleTypes SelfRole(PlayerControl pc)
    {
        var role = pc.GetCustomRole();
        return role.GetRoleInfo()?.BaseRoleType?.Invoke() ?? role.GetRoleTypes();
    }

    /// <summary>
    /// seer の画面の target の既定の見え方。
    /// - 本人 → 基底。死者 → <see cref="Ghost"/>。
    /// - 仲間 (同じ陣営の数え方で、どちらも自分の画面でキルを持つ) → Impostor。
    /// - デシンクの人が見る他の人 → Scientist (キルの対象にするため。Noisemaker はそのまま)。
    /// - デシンクの人を見る他の人 → Crewmate。
    /// - キルを持つ人 (仲間でない) → キルを持つ人からは Scientist、他からは Crewmate。
    /// - それ以外 → target の基底。
    /// </summary>
    public static RoleTypes Default(PlayerControl target, PlayerControl seer)
    {
        if (target.PlayerId == seer.PlayerId)
            return IsDead(target) ? Ghost(target, seer) : SelfRole(target);
        if (IsDead(target)) return Ghost(target, seer);

        bool team = Teammates(seer, target);
        var targetBase = SelfRole(target);
        if (IsDesync(seer)) return team ? RoleTypes.Impostor : targetBase == RoleTypes.Noisemaker ? RoleTypes.Noisemaker : RoleTypes.Scientist;
        if (IsDesync(target)) return team ? RoleTypes.Impostor : RoleTypes.Crewmate;
        if (team) return RoleTypes.Impostor;
        if (IsKillerRole(targetBase)) return IsKillerRole(SelfRole(seer)) ? RoleTypes.Scientist : RoleTypes.Crewmate;
        return targetBase;
    }

    /// <summary>
    /// 死んだ target の、seer の画面での幽霊の役職。キルを持つ本人と、キルを持たない人から見たインポスターは ImpostorGhost
    /// </summary>
    public static RoleTypes Ghost(PlayerControl target, PlayerControl seer)
    {
        bool targetIsKiller = target.GetRoleClass() is IKiller;
        bool seerIsKiller = seer.GetRoleClass() is IKiller;
        if (seer.PlayerId == target.PlayerId ? targetIsKiller : !seerIsKiller && target.Is(CustomRoleTypes.Impostor))
            return RoleTypes.ImpostorGhost;
        return RoleTypes.CrewmateGhost;
    }

    public static bool IsDesync(PlayerControl pc) => SuddenDeath.Active || pc.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true;

    private static bool IsDead(PlayerControl pc) => PlayerState.GetByPlayerId(pc.PlayerId)?.IsDead ?? !pc.IsAlive();

    public static bool IsKillerRole(RoleTypes role)
        => role is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper;

    private static bool Teammates(PlayerControl a, PlayerControl b)
    {
        if (SuddenDeath.Active) return SuddenDeath.SameTeam(a.PlayerId, b.PlayerId);
        var type = a.GetCountTypes();
        if (type is CountTypes.Crew or CountTypes.None or CountTypes.OutOfGame || type != b.GetCountTypes()) return false;
        return IsKillerRole(SelfRole(a)) && IsKillerRole(SelfRole(b));
    }

    // ---- 送る ----

    /// <summary>seer の画面の target を role にする (ホストの画面なら手元で)。関所で記録される</summary>
    public static void Send(PlayerControl target, PlayerControl seer, RoleTypes role)
    {
        if (target == null || seer == null || seer.Data == null || seer.Data.Disconnected) return;
        int clientId = seer.GetClientId();
        if (clientId == -1) return;
        target.RpcSetRoleDesync(role, clientId);
    }

    /// <summary>
    /// 役職の基底が変わった人を、全員の画面で規則どおりに見せ、その人の画面の他の人も規則どおりにする (2n 本)。
    /// 役職 (CustomRoles) を替えてから呼ぶ
    /// </summary>
    public static void ApplyBasisChange(PlayerControl player, RoleTypes? selfRole = null)
    {
        if (player == null) return;
        foreach (var seer in Main.AllPlayerControls)
        {
            Send(player, seer, seer.PlayerId == player.PlayerId && selfRole.HasValue ? selfRole.Value : Default(player, seer));
            if (seer.PlayerId != player.PlayerId) Send(seer, player, Default(seer, player));
        }
    }

    /// <summary>
    /// target を全員の画面でインポスター側の役職 (Impostor・Phantom など) killerRole にする。
    /// 全員宛てに送ると、デシンクのキル役の画面ではインポスターの仲間に見えてキルできなくなるので、
    /// デシンクの人には規則どおりの見え方 (仲間でなければ Scientist) を送る (n 本)
    /// </summary>
    public static void SendKillerRole(PlayerControl target, RoleTypes killerRole)
    {
        if (target == null) return;
        foreach (var seer in Main.AllPlayerControls)
        {
            var role = killerRole;
            if (seer.PlayerId != target.PlayerId && IsDesync(seer) && !seer.AmOwner)
            {
                var rule = Default(target, seer);
                if (rule != RoleTypes.Impostor) role = rule;
            }
            Send(target, seer, role);
        }
    }

    /// <summary>その人を全員の画面で規則どおりに見せる (その人の画面の他の人は変えない。n 本)</summary>
    public static void ApplyToEveryone(PlayerControl player)
    {
        if (player == null) return;
        foreach (var seer in Main.AllPlayerControls) Send(player, seer, Default(player, seer));
    }

    /// <summary>生き返った人を、全員の画面で死ぬ前の見え方に戻す (表 → 無ければ規則。n 本)</summary>
    public static void Replay(PlayerControl target)
    {
        foreach (var seer in Main.AllPlayerControls)
        {
            // 1 人分の失敗で残りの人への送りを止めない
            try { Send(target, seer, Living(target, seer)); }
            catch (System.Exception ex) { Logger.Exception(ex, "RoleView"); }
        }
    }
}
