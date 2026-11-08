using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 試合中に生きていた人が設定の人数だけ切断したら、自動で廃村にする。
/// 切断が続いて遊べなくなった部屋を、ホストが操作しなくても片付けるため。
/// </summary>
public static class AutoAbort
{
    public static OptionItem Enabled;
    public static OptionItem Count;

    private static int disconnectedAlive;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("AutoAbortOnDisconnect", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.All);
        Count = IntegerOptionItem.Create("AutoAbortDisconnectCount", new(1, 15, 1), 2, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Players)
            .SetGameMode(CustomGameMode.All);
    }

    [GameModuleInitializer]
    public static void Initialize() => disconnectedAlive = 0;

    /// <summary>試合中に、生きていた人が切断した時に呼ぶ (ホストだけ)。</summary>
    public static void OnAliveDisconnected()
    {
        if (!AmongUsClient.Instance.AmHost || !Enabled.GetBool()) return;
        disconnectedAlive++;
        if (disconnectedAlive < Count.GetInt() || CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
        Logger.Info($"生きていた {disconnectedAlive} 人が切断したので廃村にする", "AutoAbort");
        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
        GameManager.Instance.LogicFlow.CheckEndCriteria();
    }
}
