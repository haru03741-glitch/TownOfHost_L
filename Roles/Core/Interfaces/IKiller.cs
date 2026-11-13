namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>
/// キルボタン持ち役職の必須要素
/// </summary>
public interface IKiller
{
    /// <summary>
    /// キル能力を持っているか
    /// </summary>
    public bool CanKill => true;
    /// <summary>
    /// キルボタン押下 == キルの役職か<br/>
    /// デフォルトでは<see cref="CanKill"/>をそのまま返す
    /// </summary>
    public bool IsKiller => CanKill;

    /// <summary>
    /// キルボタンを使えるかどうか
    /// デフォルトでは<see cref="CanKill"/>をそのまま返す
    /// </summary>
    /// <returns>trueを返した場合，キルボタンを使える</returns>
    public bool CanUseKillButton() => CanKill;
    /// <summary>
    /// キルクールダウンを計算する<br/>
    /// デフォルト: <see cref="Options.DefaultKillCooldown"/>
    /// </summary>
    /// <returns>キルクールダウン(秒)</returns>
    public float CalculateKillCooldown() => Options.DefaultKillCooldown;
    /// <summary>
    /// サボタージュボタンを使えるかどうか
    /// </summary>
    /// <returns>trueを返した場合，サボタージュボタンを使える</returns>
    public bool CanUseSabotageButton() => false;
    /// <summary>
    /// ベントボタンを使えるかどうか
    /// デフォルトでは使用可能
    /// </summary>
    /// <returns>trueを返した場合，ベントボタンを使える</returns>
    public bool CanUseImpostorVentButton() => true;

    /// <summary>
    /// キラーとしてのCheckMurder処理<br/>
    /// 通常キルはブロックされることを考慮しなくてもよい。<br/>
    /// 通常キル以外の能力はinfo.CanKill=falseの場合は効果発揮しないよう実装する。<br/>
    /// キルを行わない場合はinfo.DoKill=falseとする。
    /// </summary>
    /// <param name="info">キル関係者情報</param>
    public void OnCheckMurderAsKiller(MurderInfo info) { }
    /// <summary>
    /// キルのガード (役職に関わらないガード・相手の OnCheckMurderAsTarget・守護者) より前に、キルボタンをキル以外に使うかを決める。
    /// false を返すとキルの判定をそこでやめる (ガードは働かない)。IsKiller の時だけ呼ばれる
    /// </summary>
    public bool OnCheckMurderBeforeGuards(MurderInfo info) => true;
    /// <summary>
    /// キラーとしてのMurderPlayer処理
    /// </summary>
    /// <param name="info">キル関係者情報</param>
    public void OnMurderPlayerAsKiller(MurderInfo info) { }

    /// <summary>
    /// キルボタンのテキストを変更します
    /// </summary>
    /// <param name="text">上書き後のテキスト</param>
    /// <returns>上書きする場合true</returns>
    public bool OverrideKillButtonText(out string text)
    {
        text = default;
        return false;
    }

    /// <summary>キルをしなかった (ガードされた等) 時に呼ばれる</summary>
    public void OnCheckMurderDontKill(MurderInfo info) { }
}
