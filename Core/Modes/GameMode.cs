using UnityEngine;

namespace TownOfHostL.Core.Modes;

/// <summary>モードの間に TOHL のパッチをどこまで当てておくか。</summary>
public enum GamePatchScope
{
    /// <summary>TOHL のパッチを全部当てる。</summary>
    All,
    /// <summary>常駐の物 (名乗り・チャットコマンド・モードの切り替え) だけを残し、本編のまま遊ばせる。</summary>
    ResidentOnly,
}

/// <summary>
/// 1 つのゲームモードの決まり。モードで変わる所は、各所がモードの名前を見て分けるのではなく、ここへ問い合わせる。
/// 既定の値は通常モードの決まりにしてある。
/// </summary>
public abstract class GameMode
{
    public abstract CustomGameMode Id { get; }

    public virtual GamePatchScope PatchScope => GamePatchScope.All;

    /// <summary>通常モードの役職の仕組み (デシンクの割り当て・イントロ・会議中の名前の保留・通常の勝敗) で試合を回すか。</summary>
    public virtual bool UsesStandardRoles => true;

    /// <summary>かくれんぼの共通の決まり (開始時の待機・会議とサボタージュ無し) を使うか。</summary>
    public virtual bool IsHideAndSeek => false;

    /// <summary>試合の開始から、インポスターがキルできるようになるまでの秒数。</summary>
    public virtual float StartWaitTime => 0f;

    public virtual bool AllowsDoorSabotage => true;
    public virtual bool AllowsSabotage => true;
    /// <summary>死体の通報と緊急会議を開けるか。</summary>
    public virtual bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target) => true;
    public virtual bool BootsFromVents => false;
    public virtual bool DisablesDevices => false;
    /// <summary>試合中に色を変えたプレイヤーを死なせるか (色で陣営を見分けるモード)。</summary>
    public virtual bool KillsOnColorChange => false;

    /// <summary>この設定をメニューや設定の一覧に出すか。</summary>
    public virtual bool ShowsOption(OptionItem option)
        => option.GameMode == CustomGameMode.All || option.GameMode == Id;

    /// <summary>
    /// 通常の役職の仕組みを使わないモードの割り当て。<see cref="UsesStandardRoles"/> が false の時だけ呼ばれる。
    /// 本編の割り当ての代わりに走る。
    /// </summary>
    public virtual void SelectRoles() { }

    /// <summary>ロビーの右上に出すモードの名前。null なら出さない。</summary>
    public virtual (string Key, Color Color)? Label => null;
}
