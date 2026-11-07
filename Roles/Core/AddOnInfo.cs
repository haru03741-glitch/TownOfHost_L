using System;
using AmongUs.GameOptions;

namespace TownOfHostL.Roles.Core;

/// <summary>属性の設定のタブで、どの見出しの下に並ぶか</summary>
public enum AddOnGroup
{
    /// <summary>設定のタブに出ない</summary>
    None,
    Impostor,
    Crewmate,
    Neutral,
    Buff,
    Debuff,
}

/// <summary>
/// 1 つの属性の定義。属性のクラスが static readonly で持ち、AddOnList に並べて登録する。
/// 陣営はクルー固定 (属性は陣営を決めない)。
/// </summary>
public sealed class AddOnInfo
{
    public readonly CustomRoles Role;
    public readonly AddOnGroup Group;
    public readonly string ColorCode;
    public readonly RoleFlags Flags;
    /// <summary>名前の役職表示に付ける印の文字。印を付けない属性は null</summary>
    private readonly string markText;
    private string mark;
    /// <summary>設定のタブに項目を作る</summary>
    public readonly Action SetupOptions;
    /// <summary>試合の開始でプレイヤーに付いた時</summary>
    public readonly Action<byte> OnAdd;
    /// <summary>持ち主に送る設定を書き換える</summary>
    public readonly Action<IGameOptions> ApplyGameOptions;
    /// <summary>勝ち方。持たない属性は null</summary>
    public readonly AddOnWin Win;
    /// <summary>/r などで引くチャットコマンドの名前。持たない属性は null</summary>
    public readonly string ChatCommand;
    /// <summary>試合の開始で、設定の人数と確率に従って無作為に配る (重ねて付けられる)</summary>
    public readonly bool AssignAtRandom;

    public AddOnInfo(
        CustomRoles role,
        AddOnGroup group,
        string colorCode,
        string markText = null,
        Action setupOptions = null,
        Action<byte> onAdd = null,
        Action<IGameOptions> applyGameOptions = null,
        RoleFlags flags = RoleFlags.None,
        AddOnWin win = null,
        string chatCommand = null,
        bool assignAtRandom = false)
    {
        Role = role;
        Group = group;
        ColorCode = colorCode;
        this.markText = markText;
        SetupOptions = setupOptions;
        OnAdd = onAdd;
        ApplyGameOptions = applyGameOptions;
        Win = win;
        ChatCommand = chatCommand;
        AssignAtRandom = assignAtRandom;
        Flags = flags | group switch
        {
            AddOnGroup.Buff => RoleFlags.BuffAddOn,
            AddOnGroup.Debuff => RoleFlags.DebuffAddOn,
            AddOnGroup.Impostor or AddOnGroup.Crewmate or AddOnGroup.Neutral => RoleFlags.OtherAddOn,
            _ => RoleFlags.None,
        };
    }

    /// <summary>色付きの印。色は役職の表から引くので、初めて使う時に組む</summary>
    public string Mark => markText == null ? null : mark ??= Utils.ColorString(Utils.GetRoleColor(Role), markText);
}
