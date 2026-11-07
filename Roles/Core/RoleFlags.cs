using System;

namespace TownOfHostL.Roles.Core;

/// <summary>
/// 役職の分類。役職クラスを持つ役職は SimpleRoleInfo.Create の flags で、持たない役職 (属性・CC・HAS など) は
/// RoleCatalog の表で宣言する。判定は CustomRolesHelper の IsXxx から引く。
/// 上半分は役職の分類、LearnsExiledTeam から下は役職にも属性にも付けられる性質。
/// </summary>
[Flags]
public enum RoleFlags : ulong
{
    None = 0,
    /// <summary>本編の役職そのもの (Crewmate・Engineer・Impostor など)</summary>
    Vanilla = 1UL << 0,
    /// <summary>組で配る役職。人数は組の数で数え、変えられない (FixedCount も兼ねる)</summary>
    Pair = 1UL << 1,
    /// <summary>人数の設定を変えられない役職</summary>
    FixedCount = 1UL << 2,
    /// <summary>出現率と人数の設定を作らない役職 (ユニットの構成員・封印中の役職など)</summary>
    DontShowOption = 1UL << 3,
    /// <summary>Loyalty を付けられない役職</summary>
    DontSelectLoyalty = 1UL << 4,
    /// <summary>役職ごとに属性を直接付ける設定を持つ役職 (マッドメイトはこの指定が無くても持つ)</summary>
    AddAddOn = 1UL << 5,
    BuffAddOn = 1UL << 6,
    DebuffAddOn = 1UL << 7,
    /// <summary>バフ・デバフ以外の属性 (LastImpostor・Lovers など)</summary>
    OtherAddOn = 1UL << 8,
    /// <summary>役職名の表示で、省略せず先頭に書く属性</summary>
    FirstWriteAddOn = 1UL << 9,
    /// <summary>キルボタンをキル以外の効果に使う役職</summary>
    DirectKill = 1UL << 10,
    /// <summary>トップに載せる新役職</summary>
    NewRole = 1UL << 11,
    CCLeader = 1UL << 12,
    CCColorCat = 1UL << 13,
    CCNoCat = 1UL << 14,
    /// <summary>インポスター陣営に数えるが、他のインポスターの仲間にならない (互いに役職が見えず、キルもできる)</summary>
    LoneImpostor = 1UL << 15,
    /// <summary>
    /// キルできる第三陣営 (キルボタンを別の効果に使うだけの役職は含めない)。
    /// 他の役職の能力が「キル役」として扱う集合は CountedAsKiller で、こちらとは別
    /// </summary>
    NeutralKiller = 1UL << 16,
    /// <summary>キルできるクルー (シェリフ系)。能力が「キル役」として扱う集合は CountedAsKiller</summary>
    CrewKiller = 1UL << 17,
    /// <summary>
    /// 他の役職の能力 (占い・霊媒の「キル役だけ」・スニッチ・弁護士・エクスキューショナー・ダークハイド・ラブカッター) と、
    /// シェリフ系のイントロ・守護の禁止が「キル役」として扱う役職。キルできる役職すべてではなく、遊びの調整で決めた集合
    /// </summary>
    CountedAsKiller = 1UL << 26,

    // ここから下は役職にも属性にも付けられる性質。判定は Core.PlayerTraits から引く
    /// <summary>追放された人がインポスターかどうかを、自分の名前の所で知る</summary>
    LearnsExiledTeam = 1UL << 18,
    /// <summary>死体を通報できない (ReportableByAll の死体は除く)</summary>
    CannotReport = 1UL << 19,
    /// <summary>通報できない人でも、この人の死体は通報できる</summary>
    ReportableByAll = 1UL << 20,
    /// <summary>死んでもタスクが免除されない</summary>
    KeepsTasksWhenDead = 1UL << 21,
    /// <summary>タスクの一覧の頭に「偽のタスク」と出す</summary>
    FakeTasksLabel = 1UL << 22,
    /// <summary>会議の始めに、自分の名前を送り直す (タスクの進み具合などを会議の時点の値にする)</summary>
    RefreshesNameOnMeetingStart = 1UL << 23,
    /// <summary>タスクを持っていても、クルーの勝利のタスク数に数えない</summary>
    TasksNotCountedForWin = 1UL << 24,
    /// <summary>タスクを持たない (役職クラスを持たない役職用。クラスを持つ役職は RoleBase の HasTasks で決める)</summary>
    NoTasks = 1UL << 25,
    /// <summary>元の陣営 (クルー・インポスター) の勝利に入らない</summary>
    NoTeamWin = 1UL << 27,
    /// <summary>どの勝ち方でも勝者にならない</summary>
    NeverWins = 1UL << 28,
    /// <summary>試合に加わらない観戦者。人数に数えず・勝たず・湧かず、役職と死が全員に見え、他人の役職とキルの光が見える</summary>
    Spectator = 1UL << 29,
    /// <summary>人数の勝敗のどの陣営にも数えない (役職クラスを持たない役職用。クラスを持つ役職は CountType で決める)</summary>
    NotCounted = 1UL << 30,
    /// <summary>会議の名前の CO 可否の表示に、役職ごとの設定を作らない</summary>
    NoComingOutOption = 1UL << 31,
}
