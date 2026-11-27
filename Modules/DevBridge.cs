#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.Modules;

[HarmonyPatch(typeof(PassiveButtonManager), nameof(PassiveButtonManager.Update))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
static class DevBridge
{
    private const string InPath = "./TOHL_DATA/devcmd.txt";
    private const string OutPath = "./TOHL_DATA/devout.txt";
    private static float nextCheck;

    public static void Prefix()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.2f;
        if (!File.Exists(InPath)) return;
        string[] lines;
        try { lines = File.ReadAllLines(InPath); File.Delete(InPath); }
        catch { return; }
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { sb.AppendLine($"> {line}"); Run(line.Trim().Split(' ', 2), sb); }
            catch (Exception e) { sb.AppendLine("ERR " + e); }
        }
        try { File.AppendAllText(OutPath, sb.ToString()); } catch { }
    }

    private static PassiveButton[] Buttons()
        => UnityEngine.Object.FindObjectsOfType<PassiveButton>().Where(b => b && b.isActiveAndEnabled).ToArray();

    private static string PathOf(Transform t)
    {
        var s = t.name;
        for (int i = 0; i < 2 && t.parent; i++) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    private static void Run(string[] a, StringBuilder sb)
    {
        switch (a[0])
        {
            case "buttons":
                foreach (var b in Buttons())
                {
                    var sp = Camera.main ? Camera.main.WorldToScreenPoint(b.transform.position) : Vector3.zero;
                    sb.AppendLine($"{PathOf(b.transform)} @({(int)sp.x},{Screen.height - (int)sp.y})");
                }
                break;
            case "press":
                {
                    var b = Buttons().FirstOrDefault(x => x.name == a[1]) ?? Buttons().FirstOrDefault(x => PathOf(x.transform).Contains(a[1]));
                    if (!b) { sb.AppendLine("NOTFOUND"); break; }
                    b.OnClick.Invoke();
                    sb.AppendLine("PRESSED " + PathOf(b.transform));
                    break;
                }
            case "click":
                {
                    var p = a[1].Split(' ');
                    var sp = new Vector3(float.Parse(p[0]), Screen.height - float.Parse(p[1]), 0f);
                    var cam = Camera.main;
                    var w = cam.ScreenToWorldPoint(sp);
                    var hit = Physics2D.OverlapPointAll(w).Select(c => c.GetComponent<PassiveButton>()).Where(x => x && x.isActiveAndEnabled && (!x.ClickMask || x.ClickMask.OverlapPoint(w))).FirstOrDefault();
                    if (!hit) { sb.AppendLine("NOHIT"); break; }
                    hit.OnClick.Invoke();
                    sb.AppendLine("CLICKED " + PathOf(hit.transform));
                    break;
                }
            case "pinmode":
                {
                    var p = a[1].Split(' ');
                    TownOfHostL.Modules.SpawnEditor.TohlSpawnEditor.SetMode(int.Parse(p[0]), int.Parse(p[1]));
                    sb.AppendLine("PINMODE " + TownOfHostL.Modules.SpawnEditor.TohlSpawnEditor.GetMode(int.Parse(p[0])));
                    break;
                }
            case "map":
                DleksMap.SetLobbyMap(GameOptionsManager.Instance.CurrentGameOptions, byte.Parse(a[1]));
                sb.AppendLine("MAP " + GameMap.Current);
                break;
            case "start":
                if (GameStartManager.InstanceExists) GameStartManager.Instance.BeginGame();
                sb.AppendLine("START");
                break;
            case "pos":
                sb.AppendLine($"POS {PlayerControl.LocalPlayer.transform.position}");
                break;
            case "tree":
                {
                    void Dump(Transform tr, int depth)
                    {
                        var comps = string.Join(",", tr.GetComponents<Component>().Select(x => x.GetIl2CppType().Name));
                        var sr = tr.GetComponent<SpriteRenderer>();
                        sb.AppendLine($"{new string(' ', depth * 2)}{tr.name} act={tr.gameObject.activeSelf} [{comps}]{(sr ? $" col={sr.color} sprite={(sr.sprite ? sr.sprite.name : "-")}" : "")}");
                        if (depth < 4) for (int i = 0; i < tr.childCount; i++) Dump(tr.GetChild(i), depth + 1);
                    }
                    if (AccountManager.InstanceExists) Dump(AccountManager.Instance.transform, 0);
                    break;
                }
            case "pins":
                TownOfHostL.Modules.SpawnEditor.SpawnPinEditor.SetOpen(a.Length < 2 || a[1] != "off");
                sb.AppendLine("PINS " + TownOfHostL.Modules.SpawnEditor.SpawnPinEditor.IsOpen);
                break;
            case "freeplay":
                AmongUsClient.Instance.TutorialMapId = int.Parse(a[1]);
                UnityEngine.SceneManagement.SceneManager.LoadScene("Tutorial");
                sb.AppendLine("FREEPLAY " + a[1]);
                break;
            case "rooms":
                if (!ShipStatus.Instance) { sb.AppendLine("NOSHIP"); break; }
                foreach (var r in ShipStatus.Instance.AllRooms)
                {
                    if (!r || !r.roomArea) continue;
                    var b = r.roomArea.bounds;
                    sb.AppendLine($"{r.RoomId} c=({b.center.x:F2},{b.center.y:F2}) min=({b.min.x:F2},{b.min.y:F2}) max=({b.max.x:F2},{b.max.y:F2})");
                }
                break;
            case "tp":
                {
                    var p = a[1].Split(' ');
                    PlayerControl.LocalPlayer.NetTransform.SnapTo(new Vector2(float.Parse(p[0]), float.Parse(p[1])));
                    sb.AppendLine("TP");
                    break;
                }
            case "hotkeys":
                ModHotkeys.ForceOpen = a.Length < 2 || a[1] != "off";
                foreach (var h in ModHotkeys.All) sb.AppendLine($"{h.Id} = {ModHotkeys.Format(h.Combos)}");
                sb.AppendLine("MAPPER " + ModHotkeys.PanelOpen);
                break;
            case "scanmask":
                sb.AppendLine("SCANMASK " + TownOfHostL.Modules.SpawnEditor.SpawnMask.ScanIfNeeded(true));
                break;
            case "snap":
                {
                    var p = a[1].Split(' ');
                    var ok = TownOfHostL.Modules.SpawnEditor.TohlSpawnEditor.TrySnap(new Vector2(float.Parse(p[0]), float.Parse(p[1])), out var r);
                    sb.AppendLine($"SNAP {ok} {r.x:F2} {r.y:F2}");
                    break;
                }
            case "spawnfile":
                SpawnEditorKit.SpawnStore.Load();
                foreach (var id in new[] { 0, 1, 2, 4, 5 })
                    foreach (var pt in SpawnEditorKit.SpawnStore.Library.Current.GetPoints(id, false) ?? new())
                        sb.AppendLine($"map{id} {pt.Name} {pt.X:F2} {pt.Y:F2}");
                break;
            case "mapclick":
                {
                    var p = a[1].Split(' ');
                    sb.AppendLine("MAPCLICK " + TownOfHostL.SettingsUI.SpawnMapView.ClickScreen(new Vector3(float.Parse(p[0]), Screen.height - float.Parse(p[1]), 0f)));
                    break;
                }
            case "chat":
                {
                    // チャット欄へ字を入れて送る (入力欄の字の判定も通す)
                    var chat = HudManager.Instance.Chat;
                    var area = chat.freeChatField.textArea;
                    ChatInput.SetText(area, a.Length > 1 ? a[1] : "", a.Length > 1 ? a[1].Length : 0);
                    sb.AppendLine("FIELD " + area.text);
                    chat.SendChat();
                    sb.AppendLine("SENT");
                    break;
                }
            case "dict":
                RoleDictionary.Toggle();
                sb.AppendLine("DICT");
                break;
            case "find":
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>().Where(t => t.name.Contains(a[1])))
                    sb.AppendLine($"{PathOf(t)} active={t.gameObject.activeInHierarchy} pos={t.position}");
                break;
            default:
                sb.AppendLine("UNKNOWN");
                break;
        }
    }
}
#endif
