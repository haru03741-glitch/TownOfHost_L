using System;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.Modules;

// 役職辞典の本体と F4 は Core にあるので、ボタンも本編のかくれんぼと、同じ版の TOHL のホストの部屋にいる客の間に出す
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class RoleDictionaryHudButton
{
    private const string Name = "Tohl_RoleDictButton";
    private static Sprite icon;

    private const string LobbyName = "Tohl_LobbyDictButton";
    private static GameObject lobbyButton;
    private static Transform chatButton;

    public static void Postfix(HudManager __instance)
    {
        var b = __instance.MatchInfoButton;
        if (!b) return;
        // ほかの Mod や本編のホストの部屋では、本編のボタンのままにする
        if (TownOfHostL.Core.VanillaHideAndSeek.IsClientMode && !OptionItem.HasRemoteValues)
        {
            if (lobbyButton) lobbyButton.SetActive(false);
            return;
        }
        if (b.name != Name) Replace(b);
        UpdateLobbyButton(__instance, b);
    }

    private static void UpdateLobbyButton(HudManager hud, PassiveButton source)
    {
        if (!GameStates.IsLobby)
        {
            if (lobbyButton && lobbyButton.activeSelf) lobbyButton.SetActive(false);
            return;
        }
        if (!chatButton) chatButton = hud.transform.Find("ChatUi/ChatButton");
        var gear = hud.SettingsButton ? hud.SettingsButton.transform : null;
        if (!chatButton || !gear) return;
        if (!lobbyButton)
        {
            lobbyButton = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            lobbyButton.name = LobbyName;
            foreach (var a in lobbyButton.GetComponentsInChildren<AspectPosition>(true)) UnityEngine.Object.Destroy(a);
            var pb = lobbyButton.GetComponent<PassiveButton>();
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            pb.OnClick.AddListener((Action)RoleDictionary.Toggle);
        }
        if (!lobbyButton.activeSelf) lobbyButton.SetActive(true);
        var c = chatButton.position;
        var g = gear.position;
        lobbyButton.transform.position = new Vector3(c.x - (g.x - c.x) * 1.6f, g.y, source.transform.position.z);
    }

    private static void Replace(PassiveButton b)
    {
        b.name = Name;

        b.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        b.OnClick.AddListener((Action)RoleDictionary.Toggle);
        foreach (var r in b.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!r.sprite) continue;
            var n = r.sprite.name.ToLowerInvariant();
            if (n.Contains("info") || n.Contains("question") || n.Contains("help") || n.Contains("match")) r.sprite = Icon;
        }
    }

    private static Sprite Icon
    {
        get
        {
            if (icon) return icon;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            var ink = new Color32(40, 40, 46, 255);
            var paper = new Color32(236, 236, 240, 255);
            var shade = new Color32(190, 192, 200, 255);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = (x - 64f) / 64f, fy = (y - 64f) / 64f;
                    float bend = 0.08f * (1f - Mathf.Abs(fx) / 0.82f);
                    float topEdge = 0.55f - bend, bottomEdge = -0.55f - bend;
                    bool inBook = Mathf.Abs(fx) <= 0.82f && fy <= topEdge && fy >= bottomEdge;
                    bool outline = Mathf.Abs(fx) <= 0.9f && fy <= topEdge + 0.08f && fy >= bottomEdge - 0.08f;
                    Color32 c = new(0, 0, 0, 0);
                    if (outline) c = ink;
                    if (inBook)
                    {
                        c = paper;
                        if (Mathf.Abs(fx) < 0.045f) c = ink;
                        else
                        {
                            float lx = Mathf.Abs(fx);
                            if (lx > 0.17f && lx < 0.68f)
                                for (int i = 0; i < 4; i++)
                                {
                                    float ly = 0.3f - i * 0.2f - bend;
                                    if (Mathf.Abs(fy - ly) < 0.035f) c = shade;
                                }
                        }
                    }
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            icon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 200f);
            icon.hideFlags |= HideFlags.HideAndDontSave;
            return icon;
        }
    }
}
