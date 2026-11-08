using UnityEngine;

namespace TownOfHostL.Core;

/// <summary>
/// 見る人 (seer) から見た、見られる人 (seen) の名前の上の役職名の欄。
/// 役職の上書き (OverrideDisplayRoleNameAsSeen / AsSeer) はこれを書き換え、字は最後に RoleText で 1 回だけ組む。
/// </summary>
public struct RoleNameView
{
    /// <summary>役職名の欄を出すか</summary>
    public bool Enabled;
    /// <summary>属性を書くか (先頭に出す属性は除く)。SetMainRole で差し替えた時は、その指定に従う</summary>
    public bool ShowAddons { get; private set; }
    /// <summary>欄の前に足す字 (番号・印など)</summary>
    public string Prefix;

    /// <summary>SetMainRole で差し替えた役職名の色</summary>
    public Color MainColor { get; private set; }
    /// <summary>SetMainRole で差し替えた役職名</summary>
    public string MainText { get; private set; }
    public bool MainChanged { get; private set; }
    /// <summary>Replace で置き換えた欄の字 (null なら置き換えていない)</summary>
    public string Replaced { get; private set; }

    public RoleNameView(bool enabled)
    {
        Enabled = enabled;
        ShowAddons = enabled;
        Prefix = "";
        MainColor = Color.white;
        MainText = null;
        MainChanged = false;
        MainShowAddons = false;
        Replaced = null;
    }

    /// <summary>
    /// 役職名を別の名前と色にする (偽装・正体の公開など)。属性は showAddons の時だけ書く。
    /// 今の役職と同じ名前と色なら、差し替えずに今の役職の表示 (前の役職・見せかけの役職を含む) のまま出す。
    /// </summary>
    public void SetMainRole(Color color, string text, bool showAddons = false)
    {
        MainColor = color;
        MainText = text;
        MainChanged = true;
        MainShowAddons = showAddons;
        Replaced = null;
    }
    /// <summary>SetMainRole で差し替えた名前に、属性も書くか</summary>
    public bool MainShowAddons { get; private set; }

    /// <summary>欄の字を丸ごと置き換える (陣営の表示・印だけなど)。それまでに足した Prefix も消す</summary>
    public void Replace(string text)
    {
        Replaced = text;
        Prefix = "";
    }
}
