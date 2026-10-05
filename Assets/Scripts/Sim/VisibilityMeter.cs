using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라별 가려짐 비율(visible_ratio) 계산.
/// 장면 전체를 ID 단색으로 그린 화면에서 그 오브젝트가 차지한 픽셀 수 ÷ 그 오브젝트만 단독으로 그렸을 때의 픽셀 수.
/// 1 = 안 가려짐, 0 = 완전히 가려짐. 화면 밖으로 잘린 부분은 분모·분자 모두에서 빠지므로 가려짐만 반영된다.
/// </summary>
public class VisibilityMeter
{
    const int MaskLayer = 31;   // 단독 렌더링용 임시 레이어 (씬에서 쓰지 않는 레이어)

    readonly Shader shader;
    readonly RenderTexture rt;
    readonly Texture2D tex;
    readonly int w, h;
    readonly List<Tracked> objects = new List<Tracked>();
    readonly Dictionary<Tracked, int> idOf = new Dictionary<Tracked, int>();
    // 움직이지 않는 오브젝트의 단독 픽셀 수 (카메라 고정이므로 한 번만 계산)
    readonly Dictionary<(Camera, Tracked), int> staticFull = new Dictionary<(Camera, Tracked), int>();
    static readonly int IdColor = Shader.PropertyToID("_IdColor");

    public VisibilityMeter(Shader shader, int width, int height)
    {
        this.shader = shader;
        w = width;
        h = height;
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        rt.Create();
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);

        // 추적 대상이 아닌 것(벽·바닥·마커 등)은 검정(0)으로 그려져 가리는 물체 역할만 한다
        Shader.SetGlobalVector(IdColor, Vector4.zero);
        var block = new MaterialPropertyBlock();
        foreach (var obj in Tracked.All)
        {
            int id = objects.Count + 1;
            objects.Add(obj);
            idOf[obj] = id;
            block.SetVector(IdColor, new Vector4((id & 255) / 255f, (id >> 8) / 255f, 0f, 1f));
            foreach (var r in obj.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
        }
    }

    /// <summary>cam에 대한 모든 추적 오브젝트의 visible_ratio. 화면에 안 보이면 항목 없음.</summary>
    public Dictionary<Tracked, float> Measure(Camera cam)
    {
        var prevTarget = cam.targetTexture;
        var prevClear = cam.clearFlags;
        var prevBg = cam.backgroundColor;
        int prevMask = cam.cullingMask;
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;

        var visible = CountPixels(cam);
        var result = new Dictionary<Tracked, float>();
        foreach (var obj in objects)
        {
            if (obj == null) continue;
            int full = FullPixels(cam, obj, prevMask);
            if (full == 0) continue;
            visible.TryGetValue(idOf[obj], out int v);
            result[obj] = Mathf.Clamp01(v / (float)full);
        }

        cam.cullingMask = prevMask;
        cam.targetTexture = prevTarget;
        cam.clearFlags = prevClear;
        cam.backgroundColor = prevBg;
        return result;
    }

    int FullPixels(Camera cam, Tracked obj, int normalMask)
    {
        bool isStatic = obj.GetComponent<WaypointMover>() == null;
        if (isStatic && staticFull.TryGetValue((cam, obj), out int cached)) return cached;

        var renderers = obj.GetComponentsInChildren<Renderer>();
        var layers = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            layers[i] = renderers[i].gameObject.layer;
            renderers[i].gameObject.layer = MaskLayer;
        }
        cam.cullingMask = 1 << MaskLayer;
        CountPixels(cam).TryGetValue(idOf[obj], out int full);
        cam.cullingMask = normalMask;
        for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = layers[i];

        if (isStatic) staticFull[(cam, obj)] = full;
        return full;
    }

    Dictionary<int, int> CountPixels(Camera cam)
    {
        cam.RenderWithShader(shader, "");
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;

        var counts = new Dictionary<int, int>();
        foreach (var c in tex.GetPixels32())
        {
            int id = c.r | (c.g << 8);
            if (id == 0) continue;
            counts.TryGetValue(id, out int n);
            counts[id] = n + 1;
        }
        return counts;
    }

    public void Release()
    {
        rt.Release();
        Object.Destroy(tex);
    }
}
