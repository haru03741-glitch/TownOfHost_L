namespace TownOfHostL.Modules;

/// <summary>
/// ホストの手元の死体が、客の画面でも在るかを見る。
/// 本編のヴァイパーの死体は、溶け切ると当たり判定と絵を消して溶けている印を下ろすが、死体の物 (DeadBody) は active のまま残る。
/// FindObjectsOfType&lt;DeadBody&gt; で拾った死体を「在る死体」として扱う所 (食べる・通報させる・数える・矢印) は、これで溶け切った物を除く。
/// </summary>
public static class DeadBodyState
{
    /// <summary>溶け切ったヴァイパーの死体か (客の画面にも無く、通報もできない)</summary>
    public static bool IsDissolved(DeadBody body)
    {
        if (body == null) return false;
        var viper = body.TryCast<ViperDeadBody>();
        return viper != null && viper.maxDissolveTime > 0f && viper.dissolveCurrentTime <= 0f;
    }

    /// <summary>客の画面に在る死体か (破棄された・溶け切った物は false)</summary>
    public static bool IsPresent(DeadBody body) => body != null && !IsDissolved(body);
}
