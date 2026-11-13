using UnityEngine;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// HUD のカメラに絵を置くための下地。根の GameObject をカメラの子に置き、大きさを「1 = 画面の 1 画素」に合わせる。
/// 子は画面の左下を (0,0)・上を +y とした画素の座標で置ける (<see cref="Px"/>)。ズームでカメラの写す広さが変わっても、根の大きさを合わせ直すので画面上の大きさは変わらない。
/// </summary>
public sealed class HudSpace
{
    public GameObject Root { get; private set; }
    Camera camera;
    float placedW = -1f, placedH, placedOrtho;
    float nextCheck;
    public float ScreenW { get; private set; }
    public float ScreenH { get; private set; }

    /// <summary>HUD を描くカメラ (HUD が無い場面では null)</summary>
    public static Camera FindCamera() => HudManager.InstanceExists ? HudManager.Instance.UICamera : null;

    /// <summary>HUD と同じ奥行き (カメラからの距離)</summary>
    public static float Depth(Camera cam)
    {
        float near = cam.nearClipPlane, far = cam.farClipPlane;
        var d = HudManager.InstanceExists ? HudManager.Instance.transform.position.z - cam.transform.position.z : float.NaN;
        return d > near && d < far ? d : (near + far) / 2f;
    }

    public bool Alive => Root != null;

    /// <summary>根を用意して、画面の大きさとズームに合わせる。カメラが無ければ false (根は消す)</summary>
    public bool Ensure(string name, float towardCamera)
    {
        // カメラと画面の大きさを読むと本編を呼ぶので、根がある間は 0.25 秒ごとに確かめる (根が壊れたらすぐ作り直す)
        var now = Time.unscaledTime;
        if (Root is not null && now < nextCheck && Root != null) return true;
        nextCheck = now + 0.25f;
        var cam = FindCamera();
        if (cam == null) { Destroy(); return false; }
        if (Root == null || camera != cam)
        {
            Destroy();
            camera = cam;
            Root = new GameObject(name) { layer = 5 };
            Root.transform.SetParent(cam.transform, false);
            placedW = -1f;
        }
        float sw = Screen.width, sh = Screen.height, ortho = cam.orthographicSize;
        ScreenW = sw;
        ScreenH = sh;
        if (Fx.Same(placedW, sw) && Fx.Same(placedH, sh) && Fx.Same(placedOrtho, ortho)) return true;
        placedW = sw; placedH = sh; placedOrtho = ortho;
        var perPx = 2f * ortho / sh;
        var z = Fx.Max(cam.nearClipPlane + 0.05f, Depth(cam) - towardCamera);
        Root.transform.localPosition = Fx.V3(0f, 0f, z);
        Root.transform.localScale = Fx.V3(perPx, perPx, 1f);
        return true;
    }

    /// <summary>画面の左下からの画素の座標を、根の中の座標にする</summary>
    public Vector3 Px(float x, float y, float z = 0f) => Fx.V3(x - ScreenW / 2f, y - ScreenH / 2f, z);

    public void Destroy()
    {
        if (Root != null) Object.Destroy(Root);
        Root = null;
        camera = null;
    }
}
