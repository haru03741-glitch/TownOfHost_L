using HarmonyLib;

namespace TownOfHostL;

// https://github.com/SuperNewRoles/SuperNewRoles/blob/master/SuperNewRoles/Patches/LobbyBehaviourPatch.cs
[HarmonyPatch(typeof(LobbyBehaviour))]
public class LobbyBehaviourPatch
{
    [HarmonyPatch(nameof(LobbyBehaviour.Update)), HarmonyPostfix]
    public static void UpdatePostfix(LobbyBehaviour __instance)
    {
        // Il2Cpp の List.Find に C# のラムダを渡すと、呼ぶたびに delegate の変換で GCHandle が残るので、自前で走査する
        ISoundPlayer MapThemeSound = null;
        var soundPlayers = SoundManager.Instance.soundPlayers;
        for (int i = 0; i < soundPlayers.Count; i++)
        {
            if (soundPlayers[i].Name == "MapTheme")
            {
                MapThemeSound = soundPlayers[i];
                break;
            }
        }
        if (Main.IsMuteLobbyBGM.Value)
        {
            if (MapThemeSound == null) return;
            SoundManager.Instance.StopNamedSound("MapTheme");
        }
        else
        {
            if (MapThemeSound != null) return;
            SoundManager.Instance.CrossFadeSound("MapTheme", __instance.MapTheme, 0.5f);
        }
    }
}