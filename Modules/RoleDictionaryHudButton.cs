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
            if (lobbyReportButton) lobbyReportButton.SetActive(false);
            if (reportButton) reportButton.SetActive(false);
            return;
        }
        if (b.name != Name) Replace(b);
        UpdateLobbyButton(__instance, b);
        UpdateReportButtons(b);
    }

    // ---- バグ・要望の報告のボタン (辞典のボタンの左隣。ロビーでも試合中でも) ----
    private const string ReportName = "Tohl_ReportButton";
    private const string LobbyReportName = "Tohl_LobbyReportButton";
    private static GameObject reportButton, lobbyReportButton;
    private static Sprite reportIcon;

    private static GameObject MakeReportButton(PassiveButton source, string name)
    {
        var go = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
        go.name = name;
        foreach (var a in go.GetComponentsInChildren<AspectPosition>(true)) UnityEngine.Object.Destroy(a);
        var pb = go.GetComponent<PassiveButton>();
        pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        pb.OnClick.AddListener((Action)ReportWindow.Toggle);
        foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true))
            if (r.sprite && r.sprite == Icon) r.sprite = ReportIcon;
        return go;
    }

    private static float ButtonWidth(PassiveButton b)
    {
        // 見えている枠の絵の幅 (当たり判定は枠より細いので、それで並べると枠が重なる)
        try
        {
            float w = 0f;
            foreach (var r in b.GetComponentsInChildren<SpriteRenderer>(false))
                if (r && r.enabled && r.sprite) w = Mathf.Max(w, r.bounds.size.x);
            if (w > 0.2f) return w;
            if (b.Colliders != null && b.Colliders.Length > 0 && b.Colliders[0]) return Mathf.Max(0.3f, b.Colliders[0].bounds.size.x);
        }
        catch { }
        return 0.7f;
    }

    private static void UpdateReportButtons(PassiveButton dict)
    {
        // 試合中: 本のボタンの左隣
        var inGame = !GameStates.IsLobby && dict.gameObject.activeInHierarchy;
        if (inGame)
        {
            if (!reportButton) reportButton = MakeReportButton(dict, ReportName);
            if (!reportButton.activeSelf) reportButton.SetActive(true);
            var p = dict.transform.position;
            reportButton.transform.position = new Vector3(p.x - ButtonWidth(dict) * 1.06f, p.y, p.z);
            // 試合の始まりの演出の間など、本のボタンが押せない時に写すと押せないままになるので、毎フレーム合わせる
            var rb = reportButton.GetComponent<PassiveButton>();
            if (rb && rb.enabled != dict.enabled) rb.enabled = dict.enabled;
            if (rb && rb.Colliders != null && dict.Colliders != null)
                for (int i = 0; i < rb.Colliders.Length && i < dict.Colliders.Length; i++)
                    if (rb.Colliders[i] && dict.Colliders[i] && rb.Colliders[i].enabled != dict.Colliders[i].enabled) rb.Colliders[i].enabled = dict.Colliders[i].enabled;
        }
        else if (reportButton && reportButton.activeSelf) reportButton.SetActive(false);

        // ロビー: ロビーの辞典のボタンの左隣
        if (GameStates.IsLobby && lobbyButton && lobbyButton.activeSelf)
        {
            if (!lobbyReportButton) lobbyReportButton = MakeReportButton(lobbyButton.GetComponent<PassiveButton>(), LobbyReportName);
            if (!lobbyReportButton.activeSelf) lobbyReportButton.SetActive(true);
            var p = lobbyButton.transform.position;
            lobbyReportButton.transform.position = new Vector3(p.x - ButtonWidth(lobbyButton.GetComponent<PassiveButton>()) * 1.06f, p.y, p.z);
        }
        else if (lobbyReportButton && lobbyReportButton.activeSelf) lobbyReportButton.SetActive(false);
    }

    /// <summary>報告のボタンの絵: 白い吹き出しに「!」</summary>
    private static Sprite ReportIcon
    {
        get
        {
            if (reportIcon) return reportIcon;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            var ink = new Color32(40, 40, 46, 255);
            var paper = new Color32(236, 236, 240, 255);
            var mark = new Color32(229, 72, 72, 255);
            // 吹き出しの体 (角の丸い四角) と、左下へ出る尾
            static float RoundRect(float x, float y, float hw, float hh, float r)
            {
                var dx = Mathf.Max(Mathf.Abs(x) - (hw - r), 0f);
                var dy = Mathf.Max(Mathf.Abs(y) - (hh - r), 0f);
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            }
            static bool InTri(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
            {
                float d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
                float d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
                float d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = (x - 64f) / 64f, fy = (y - 64f) / 64f;
                    var body = RoundRect(fx, fy - 0.12f, 0.8f, 0.55f, 0.22f);
                    // 尾: 体の左下から斜め下へ出る三角 (縁は少し大きい三角で描く)
                    bool tailOut = InTri(fx, fy, -0.58f, -0.3f, -0.08f, -0.3f, -0.68f, -0.92f);
                    bool tailIn = InTri(fx, fy, -0.5f, -0.3f, -0.18f, -0.3f, -0.6f, -0.78f);
                    Color32 c = new(0, 0, 0, 0);
                    if (body < 0.07f || tailOut) c = ink;
                    if (body < 0f || tailIn) c = paper;
                    // 「!」
                    if (Mathf.Abs(fx) < 0.085f && fy > 0.02f && fy < 0.52f) c = mark;
                    if (fx * fx + (fy + 0.16f) * (fy + 0.16f) < 0.095f * 0.095f) c = mark;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            reportIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 200f);
            reportIcon.hideFlags |= HideFlags.HideAndDontSave;
            return reportIcon;
        }
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
