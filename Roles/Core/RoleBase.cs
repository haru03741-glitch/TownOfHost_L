using System;
using System.Linq;
using UnityEngine;
using Hazel;
using AmongUs.GameOptions;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Core;

public abstract class RoleBase : IDisposable
{
    public PlayerControl Player { get; private set; }
    /// <summary>
    /// プレイヤーの状態
    /// </summary>
    public readonly PlayerState MyState;
    /// <summary>
    /// プレイヤーのタスク状態
    /// </summary>
    public readonly TaskState MyTaskState;
    /// <summary>
    /// タスクを持っているか。
    /// 初期値はクルー役職のみ持つ
    /// </summary>
    protected Func<HasTask> hasTasks;
    /// <summary>
    /// タスクを持っているか
    /// </summary>
    public HasTask HasTasks => hasTasks.Invoke();
    /// <summary>
    /// タスクが完了しているか
    /// </summary>
    public bool IsTaskFinished => MyTaskState.IsTaskFinished;
    /// <summary>
    /// アビリティボタンで発動する能力を持っているか
    /// </summary>
    public bool HasAbility { get; protected set; }
    public RoleBase(
        SimpleRoleInfo roleInfo,
        PlayerControl player,
        Func<HasTask> hasTasks = null,
        bool? hasAbility = null
    )
    {
        Player = player;
        this.hasTasks = hasTasks ?? (roleInfo.CustomRoleType == CustomRoleTypes.Crewmate ? () => HasTask.True : () => HasTask.False);
        HasAbility = hasAbility ?? roleInfo.BaseRoleType.Invoke() is
            RoleTypes.Shapeshifter or
            RoleTypes.Phantom or
            RoleTypes.Engineer or
            RoleTypes.Scientist or
            RoleTypes.Tracker or
            RoleTypes.GuardianAngel or
            RoleTypes.CrewmateGhost or
            RoleTypes.ImpostorGhost;

        MyState = PlayerState.GetByPlayerId(player.PlayerId);
        MyTaskState = MyState.GetTaskState();

        CustomRoleManager.AllActiveRoles.Add(Player.PlayerId, this);
    }
#pragma warning disable CA1816
    public void Dispose()
    {
        OnDestroy();
        CustomRoleManager.AllActiveRoles.Remove(Player.PlayerId);
        Player = null;
    }
#pragma warning restore CA1816
    /// <summary>
    /// この役職の中に持たせる別の役職を作る (アサシンが持つ役職など)。中の役職は AllActiveRoles に載せない
    /// (同じ人の役職は 1 つだけ)。全員の役職へ配るフックは、外の役職の override から中へ回す。
    /// 壊す時は Dispose ではなく OnDestroy を呼ぶ (Dispose は外の役職の登録も消す)
    /// </summary>
    protected RoleBase CreateInnerRole(SimpleRoleInfo roleInfo)
    {
        CustomRoleManager.AllActiveRoles.Remove(Player.PlayerId);
        try { return roleInfo.CreateInstance(Player); }
        finally { CustomRoleManager.AllActiveRoles[Player.PlayerId] = this; }
    }
    public bool Is(PlayerControl player)
    {
        return player.PlayerId == Player.PlayerId;
    }
    /// <summary>
    /// インスタンス作成後すぐに呼ばれる関数
    /// </summary>
    public virtual void Add()
    { }
    /// <summary>
    /// ロールベースが破棄されるときに呼ばれる関数
    /// </summary>
    public virtual void OnDestroy()
    { }
    /// <summary>
    /// RoleBase専用のRPC送信クラス
    /// 自身のPlayerIdを自動的に送信する
    /// </summary>
    protected class RoleRPCSender : IDisposable
    {
        public MessageWriter Writer;
        public RoleRPCSender(RoleBase role, CustomRPC rpcType)
        {
            Writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)rpcType, SendOption.Reliable, -1);
            Writer.Write(role.Player.PlayerId);
        }
        public void Dispose()
        {
            AmongUsClient.Instance.FinishRpcImmediately(Writer);
        }
    }
    /// <summary>
    /// RPC送信クラスの作成
    /// PlayerIdは自動的に追記されるので意識しなくてもよい。
    /// </summary>
    /// <param name="rpcType">送信するCustomRPC</param>
    /// <returns>送信に使用するRoleRPCSender</returns>
    protected RoleRPCSender CreateSender(CustomRPC rpcType)
    {
        return new RoleRPCSender(this, rpcType);
    }
    /// <summary>
    /// RPCを受け取った時に呼ばれる関数
    /// RoleRPCSenderで送信されたPlayerIdは削除されて渡されるため意識しなくてもよい。
    /// </summary>
    /// <param name="reader">届いたRPCの情報</param>
    /// <param name="rpcType">届いたCustomRPC</param>
    public virtual void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    { }
    /// <summary>
    /// 能力ボタンを使えるかどうか
    /// </summary>
    /// <returns>trueを返した場合、能力ボタンを使える</returns>
    public virtual bool CanUseAbilityButton() => true;
    /// <summary>
    /// BuildGameOptionsで呼ばれる関数
    /// </summary>
    public virtual void ApplyGameOptions(IGameOptions opt)
    { }

    /// <summary>
    /// ターゲットとしてのCheckMurder処理
    /// キラーより先に判定
    /// キル出来ない状態(無敵など)はinfo.CanKill=falseとしてtrueを返す
    /// キル行為自体をなかったことにする場合はfalseを返す。
    /// </summary>
    /// <param name="info">キル関係者情報</param>
    /// <returns>false:キル行為を起こさせない</returns>
    public virtual bool OnCheckMurderAsTarget(MurderInfo info) => true;

    /// <summary>
    /// ターゲットとしてのMurderPlayer処理
    /// </summary>
    /// <param name="info">キル関係者情報</param>
    public virtual void OnMurderPlayerAsTarget(MurderInfo info)
    { }

    /// <summary>
    /// 自視点のみ変身する
    /// 抜け殻を自視点のみに残すことが可能
    /// </summary>
    public virtual bool CanDesyncShapeshift => false;

    /// <summary>
    /// シェイプシフトチェック時に呼ばれる
    /// 自分自身が変身したときのみ呼ばれる
    /// animateを操作して変身アニメーションのカットも可能
    /// </summary>
    /// <param name="target">変身先</param>
    /// <param name="animate">アニメーションを再生するかどうか</param>
    /// <returns>falseを返すと変身がキャンセルされる</returns>
    public virtual bool OnCheckShapeshift(PlayerControl target, ref bool animate) => true;

    /// <summary>
    /// シェイプシフト時に呼ばれる関数
    /// 自分自身について呼ばれるため本人確認不要
    /// Host以外も呼ばれるので注意
    /// </summary>
    /// <param name="target">変身先</param>
    public virtual void OnShapeshift(PlayerControl target)
    { }

    /// <summary>
    /// 透明化チェック時に呼ばれる
    /// 自分自身が透明化したときのみ呼ばれる
    /// ※透明化キャンセルの場合にキルクールが10秒に強制されてしまうため、
    /// falseで返す時があれば能力に応じた任意のキルクールを設定しておく。
    /// </summary>
    /// <returns>falseを返すと透明化がキャンセルされる</returns>
    /// <param name="killCooldown">キルクール</param>
    /// <param name="canResetAbilityCooldown">使用後アビリティクールをリセットするか</param>
    public virtual bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown) => true;

    /// <summary>
    /// タスクターンに常時呼ばれる関数
    /// 自分自身について呼ばれるため本人確認不要
    /// Host以外も呼ばれるので注意
    /// playerが自分以外であるときに処理したい場合は同じ引数でstaticとして実装し
    /// CustomRoleManager.OnFixedUpdateOthersに登録する
    /// </summary>
    /// <param name="player">対象プレイヤー</param>
    public virtual void OnFixedUpdate(PlayerControl player)
    { }

    /// <summary>
    /// 通報時，会議が呼ばれることが確定してから呼ばれる関数<br/>
    /// 通報に関係ないプレイヤーも呼ばれる
    /// </summary>
    /// <param name="reporter">通報したプレイヤー</param>
    /// <param name="target">通報されたプレイヤー</param>
    public virtual void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    { }

    /// <summary>
    /// 自分が復活した時 (ホストのみ)。役職のインスタンスは死ぬ前の物がそのまま使われるので、
    /// 「一度きり」の旗やタイマーを持つ役職は、ここで戻す
    /// </summary>
    public virtual void OnRevived() { }

    /// <summary>
    /// <para>ベントに入ったときに呼ばれる関数</para>
    /// <para>キャンセル可</para>
    /// </summary>
    /// <param name="physics"></param>
    /// <param name="id"></param>
    /// <returns>falseを返すとベントから追い出されます</returns>
    public virtual bool OnEnterVent(PlayerPhysics physics, int ventId) => true;

    /// <summary>
    /// ミーティングが始まった時に呼ばれる関数
    /// </summary>
    public virtual void OnStartMeeting()
    { }

    /// <summary>
    /// 自分が投票した瞬間，票がカウントされる前に呼ばれる<br/>
    /// falseを返すと投票行動自体をなかったことにし，再度投票できるようになる<br/>
    /// 投票行動自体は取り消さず，票だけカウントさせない場合は<see cref="ModifyVote"/>を使用し，doVoteをfalseにする
    /// </summary>
    /// <param name="votedForId">投票先</param>
    /// <returns>falseを返すと投票自体がなかったことになり，投票者自身以外には投票したことがバレません</returns>
    public virtual bool CheckVoteAsVoter(PlayerControl votedFor) => true;

    /// <summary>
    /// 誰かが投票した瞬間に呼ばれ，票を書き換えることができる<br/>
    /// 投票行動自体をなかったことにしたい場合は<see cref="CheckVoteAsVoter"/>を使用する
    /// </summary>
    /// <param name="voterId">投票した人のID</param>
    /// <param name="sourceVotedForId">投票された人のID</param>
    /// <returns>(変更後の投票先(変更しないならnull), 変更後の票数(変更しないならnull), 投票をカウントするか)</returns>
    public virtual (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional) => (null, null, true);

    /// <summary>
    /// 追放後に行われる処理
    /// </summary>
    /// <param name="exiled">追放されるプレイヤー</param>
    /// <param name="DecidedWinner">勝者を確定させるか</param>
    public virtual void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    { }

    /// <summary>
    /// タスクターンが始まる直前に毎回呼ばれる関数
    /// </summary>
    public virtual void AfterMeetingTasks()
    { }

    /// <summary>
    /// タスクが一個完了するごとに呼ばれる関数
    /// </summary>
    /// <returns>falseを返すとバニラ処理をキャンセルする</returns>
    public virtual bool OnCompleteTask() => true;

    /// <summary>停電・通信妨害を直せない (ドジ属性と同じ)。属性のドジは PlayerTraits.IsClumsy が別に見る</summary>
    public virtual bool IsClumsy => false;
    /// <summary>人数による終了判定で、生きている間この人を自分の陣営 (CountType) に数えるか</summary>
    public virtual bool CountsForGameEnd => true;
    /// <summary>アドミン・カメラ・バイタルなどの情報機器を使えない (情報弱者属性と同じ)</summary>
    public virtual bool IsInfoPoor => false;
    /// <summary>投票で追放された時に誰かを道連れにする</summary>
    public virtual bool RevengesOnExile => false;
    /// <summary>追放の道連れで、candidate を道連れの相手に選べるか</summary>
    public virtual bool CanRevengeOn(PlayerControl candidate) => true;
    /// <summary>この役職の人に、target の役職の色が見えるか</summary>
    public virtual bool KnowsRoleColorOf(PlayerControl target, bool isMeeting) => false;
    /// <summary>この役職の人に、seen の役職名が見えるか (相方・占った相手など)。字を書き換える時は OverrideDisplayRoleNameAsSeer を使う</summary>
    public virtual bool KnowsRoleOf(PlayerControl seen, bool isMeeting) => false;
    /// <summary>この役職の人の役職名が、seer に見えるか (相方・正体を明かした相手など)。字を書き換える時は OverrideDisplayRoleNameAsSeen を使う</summary>
    public virtual bool ShowsRoleTo(PlayerControl seer, bool isMeeting) => false;
    /// <summary>この役職の人には、会議で target の名前を色なしで見せる (他の色の決まりより先に効く)</summary>
    public virtual bool HidesNameColorInMeeting(PlayerControl target) => false;
    /// <summary>この役職の人の役職の色が、全員に見えるか</summary>
    public virtual bool ShowsRoleColorToAll(bool isMeeting) => false;
    /// <summary>キノコカオスの間も、キルできる相手の名前が見える (インポスターは役職によらず見える)</summary>
    public virtual bool SeesNamesInMushroomMixup => false;
    /// <summary>この役職の人のタスクの進み具合が、全員に見えるか</summary>
    public virtual bool ShowsTaskProgressToAll => false;
    /// <summary>色の統一モードの会議でも、自分の名前を白で塗りつぶさず役職付きで出す</summary>
    public virtual bool KeepsOwnNameInSyncColorMeeting => false;

    // == Sabotage関連処理 ==
    /// <summary>
    /// 自身がサボタージュを発生させたときに発火する
    /// ドア閉めには関与できない
    /// </summary>
    /// <param name="systemType">サボタージュの種類</param>
    /// <returns>falseでサボタージュをキャンセル</returns>
    public virtual bool OnInvokeSabotage(SystemTypes systemType) => true;

    /// <summary>
    /// 誰かがサボタージュを発生させたときに呼ばれる
    /// </summary>
    /// <param name="player">アクションを起こしたプレイヤー</param>
    /// <param name="systemType">サボタージュの種類</param>
    /// <returns>falseでサボタージュのキャンセル</returns>
    public virtual bool OnSabotage(PlayerControl player, SystemTypes systemType) => true;

    // NameSystem
    /// <summary>
    /// 自分が見る他人の名前 (マーク・色・役職名など) を、NotifyRoles のたびに組み立て直す必要があるか。
    /// false の役職は、死んでいる・インポスターなどの他の条件に当たらない限り、他人の名前を送り直さない。
    /// </summary>
    public virtual bool NeedsSeenNames => false;

    /// <summary>元の陣営 (クルー・インポスター) の勝利に入るか。印の NoTeamWin と違い、試合中に変わってよい</summary>
    public virtual bool WinsWithBaseTeam => true;

    // 名前は下記の構成で表示される
    // [Role][Progress]
    // [Name][Mark]
    // [Lower][suffix]
    // Progress:タスク進捗/残弾等の状態表示
    // Mark:役職能力によるターゲットマークなど
    // Lower:役職用追加文字情報。Modの場合画面下に表示される。
    // Suffix:ターゲット矢印などの追加情報。

    /// <summary>
    /// この役職の人 (seen) の役職名の欄を、seer から見た形に書き換える。見えるかどうかだけなら ShowsRoleTo を使う。
    /// seer が自分自身の時も呼ばれる。
    /// </summary>
    public virtual void OverrideDisplayRoleNameAsSeen(PlayerControl seer, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
    { }
    /// <summary>
    /// この役職の人 (seer) から見た seen の役職名の欄を書き換える。見えるかどうかだけなら KnowsRoleOf を使う。
    /// seen が自分自身の時も呼ばれる。
    /// </summary>
    public virtual void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
    { }
    /// <summary>
    /// 本来の役職名の書き換え
    /// </summary>
    /// <param name="roleColor">RoleNameの色</param>
    /// <param name="roleText">RoleNameのテキスト</param>
    public virtual void OverrideShowMainRoleText(ref Color roleColor, ref string roleText)
    { }
    /// <summary>
    /// 役職の名前・色・設定の一覧で、この役職の代わりに見せる役職 (ポテンシャリストがクルーメイトに見えるなど)。
    /// null ならこの役職のまま。イントロ・/m・役職名の表示はここから引く。/m の説明は、正体を明かさないよう短い説明を出す。
    /// </summary>
    public virtual CustomRoles? DisplayedRole => null;
    /// <summary>
    /// 役職名の表示の上書きを、DisplayedRole → OverrideShowMainRoleText の順にかける。
    /// 役職名や名前の色を取る所はみなここを通す (OverrideShowMainRoleText を直接呼ばない)
    /// </summary>
    public void ApplyShownMainRole(ref Color roleColor, ref string roleText)
    {
        if (DisplayedRole is CustomRoles shown)
        {
            roleColor = Utils.GetRoleColor(shown);
            roleText = Utils.GetRoleName(shown);
        }
        OverrideShowMainRoleText(ref roleColor, ref roleText);
    }
    /// <summary>
    /// 役職の翻訳キーの根を差し替える。名前は「根」、説明は「根Info」「根InfoLong」で引く。
    /// null なら役職名そのまま。状態で説明が変わる役職 (マフィアの Before/After・覚醒したパン屋の NBakery など) が上書きする。
    /// </summary>
    /// <param name="infoLong">長い説明 (/m) を引く時 true</param>
    public virtual string GetRoleKey(bool infoLong) => null;
    /// <summary>
    /// seerによるProgressTextの書き換え
    /// </summary>
    /// <param name="seen">見られる側</param>
    /// <param name="enabled">ProgressTextを表示するかどうか</param>
    /// <param name="text">ProgressTextのテキスト</param>
    public virtual void OverrideProgressTextAsSeer(PlayerControl seen, ref bool enabled, ref string text)
    { }
    /// <summary>
    /// 役職名の横に出るテキスト
    /// </summary>
    /// <param name="comms">コミュサボ中扱いするかどうか</param>
    public virtual string GetProgressText(bool comms = false) => "";
    /// <summary>
    /// seerが自分であるときのMark
    /// seer,seenともに自分以外であるときに表示したい場合は同じ引数でstaticとして実装し
    /// CustomRoleManager.MarkOthersに登録する
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <param name="isForMeeting">会議中フラグ</param>
    /// <returns>構築したMark</returns>
    public virtual string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false) => "";
    /// <summary>
    /// seerが自分であるときのLowerTex
    /// seer,seenともに自分以外であるときに表示したい場合は同じ引数でstaticとして実装し
    /// CustomRoleManager.LowerOthersに登録する
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <param name="isForMeeting">会議中フラグ</param>
    /// <param name="isForHud">ModでHudとして表示する場合</param>
    /// <returns>構築したLowerText</returns>
    public virtual string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false) => "";
    /// <summary>
    /// seer自分であるときのSuffix
    /// seer,seenともに自分以外であるときに表示したい場合は同じ引数でstaticとして実装し
    /// CustomRoleManager.SuffixOthersに登録する
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <param name="isForMeeting">会議中フラグ</param>
    /// <returns>構築したMark</returns>
    public virtual string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false) => "";

    /// <summary>
    /// シェイプシフトボタンのテキストを変更します
    /// </summary>
    public virtual string GetAbilityButtonText()
    {
        StringNames? str = Player.Data.Role.Role switch
        {
            RoleTypes.Engineer => StringNames.VentAbility,
            RoleTypes.Scientist => StringNames.VitalsAbility,
            RoleTypes.Tracker => StringNames.TrackerAbility,
            RoleTypes.Shapeshifter => StringNames.ShapeshiftAbility,
            RoleTypes.Phantom => StringNames.PhantomAbility,
            RoleTypes.GuardianAngel => StringNames.ProtectAbility,
            RoleTypes.ImpostorGhost or RoleTypes.CrewmateGhost => StringNames.HauntAbilityName,
            _ => null
        };
        return str.HasValue ? GetString(str.Value) : "Invalid";
    }

    protected static AudioClip GetIntroSound(RoleTypes roleType)
    {
        foreach (var role in RoleManager.Instance.AllRoles)
        {
            if (role.Role == roleType)
            {
                return role.IntroSound;
            }
        }
        return null; // 見つからなかった場合は null を返す
    }

    /// <summary>
    /// ジャッジが裁決を行ったときに呼ばれます。<br/>
    /// 裁決を無かったことにするなら false
    /// </summary>
    /// <param name="voter">裁決を行ったジャッジ</param>
    /// <param name="votefor">裁決された人</param>
    /// <param name="ExilePlayerid">実際に追放する人</param>
    /// <returns>裁決を成立させるかどうか</returns>
    public virtual bool CallJudgeVote(PlayerControl voter, PlayerControl votefor, ref byte ExilePlayerid) => false;

    // ===== 試合の流れ (ホストのみ) =====
    /// <summary>試合の途中で役職が付いた時、Add の後に呼ばれる (役職の変更・復活など)</summary>
    public virtual void OnAddedMidGame() { }
    /// <summary>イントロが終わって少し後に呼ばれる (全員の役職が出そろった後)</summary>
    public virtual void OnIntroEnd() { }
    /// <summary>
    /// 自分が湧いた時。会議明け (エアシップ以外は追放の演出の締め、エアシップは湧き位置を選んだ時) と、
    /// 試合の始め (initialState = true) に呼ばれる
    /// </summary>
    public virtual void OnSpawn(bool initialState = false) { }
    /// <summary>誰かが死んだ時 (死に方によらず 1 人 1 回)。OnPlayerDeadOthers と同じ所から全員の役職に呼ぶ</summary>
    public virtual void OnPlayerDead(PlayerControl player) { }
    /// <summary>
    /// 自分が会議の後の死 (道連れ・投票による自殺など。追放そのものは OnExileWrapUp) で死んだ時。
    /// SetDead と全員への OnPlayerDead の後に、死んだ人の役職にだけ呼ぶ
    /// </summary>
    public virtual void OnAfterMeetingDeath(CustomDeathReason reason) { }
    /// <summary>誰かが部屋から抜けた時。全員の役職に呼ぶ。抜けた人の PlayerControl は外れていることがあるので PlayerId で渡す</summary>
    public virtual void OnPlayerLeft(byte playerId) { }
    /// <summary>勝者がほぼ決まった後に呼ばれる。勝者を足したり外したりできる</summary>
    public virtual void CheckWinner(GameOverReason reason) { }

    // ===== ベント =====
    /// <summary>ベントのボタンを押せるか (ホストの画面の判定)</summary>
    public virtual bool CanClickUseVentButton => true;
    /// <summary>ベントの中で隣のベントへ移れるか。false ならホストが移動の矢印を止める</summary>
    public virtual bool CanVentMoving(PlayerPhysics physics, int ventId) => true;
    /// <summary>誰かのベントの操作 (入る・出る・移る) が届いた時。全員の役職に呼ぶ</summary>
    public virtual void OnVentilationSystemUpdate(PlayerControl user, VentilationSystem.Operation operation, int ventId) { }

    // ===== サボタージュ =====
    /// <summary>誰かがサボタージュを直そうとした時。全員の役職に呼ぶ</summary>
    public virtual void OnFixSabotage(PlayerControl player, SystemTypes systemType, byte amount) { }
    /// <summary>サボタージュが直り切った時。全員の役職に呼ぶ</summary>
    public virtual void AfterSabotage(SystemTypes systemType) { }

    // ===== タスク =====
    /// <summary>ホストの画面でタスクを使えるか (ホストがこの役職の時)</summary>
    public virtual bool CanTask() => Utils.HasTasks(PlayerControl.LocalPlayer.Data, false);

    // ===== 会議 =====
    /// <summary>会議の始めにチャットへ流す文。空なら流さない</summary>
    public virtual string MeetingAddMessage() => "";
    /// <summary>
    /// 誰かが投票した時に、全員の役職で呼ぶ (CheckVoteAsVoter は投票した本人の役職だけ)。
    /// どれかが false を返すと、その投票をなかったことにする
    /// </summary>
    public virtual bool CheckAnyVote(byte votedForId, PlayerControl voter) => true;
    /// <summary>
    /// 投票の集計の後、追放する人を書き換える。全員の役職で呼ぶ。
    /// true を返すと、同数の時の特殊な決め方 (全員追放など) を使わない
    /// </summary>
    public virtual bool VotingResults(ref NetworkedPlayerInfo exiled, ref bool isTie, System.Collections.Generic.Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool clearAndExile) => false;
    /// <summary>通報 (緊急ボタンも含む) を取り消すか。全員の役職で呼ぶ。true で取り消し、reason に理由を書く</summary>
    public virtual bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportReason reason) => false;
    /// <summary>
    /// 会議明けの配り直しで、本人の画面の本人の役職をこれにする。null なら見え方の表 (Modules.RoleView) のまま
    /// </summary>
    public virtual RoleTypes? AfterMeetingRole => null;

    // ===== 見え方 =====
    /// <summary>占い (役職を調べる能力) で見せる役職。NotAssigned なら本当の役職</summary>
    public virtual CustomRoles TellResults(PlayerControl seer) => CustomRoles.NotAssigned;
    /// <summary>この役職の人 (seen) の役職名の横の文字を、seer から見た形に書き換える</summary>
    public virtual void OverrideProgressTextAsSeen(PlayerControl seer, ref bool enabled, ref string text) { }

    public enum DontReportReason
    {
        None,
        wait,
        NonReport,
        Transparent,
        CantUseButton,
        Eat,
        Other,
        Impostor,
        Jiangshi,
    }

    protected enum GeneralOption
    {
        Cooldown,
        KillCooldown,
        CanVent,
        ImpostorVision,
        CanUseSabotage,
        CanCreateMadmate,
        VentCooldown,
        VentMaxTime,
        TaskTrigger,
        CanCreateSideKick,
        Duration,
        cantaskcount,
        MeetingMaxTime,
        PlayShapeAnimate,
        TaskAwakening,
        AwakeningTaskcount,
        AbilityAwakening,
        OptionCount,
        EngineerInVentCooldown,
        CanUseActiveComms,
    }
}
