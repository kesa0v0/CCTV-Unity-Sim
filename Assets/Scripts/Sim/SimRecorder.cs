using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public enum Cam2Corner { A, B, C }

/// <summary>
/// Play 모드에서 시나리오를 재생하며 unity_runs/&lt;sessionName&gt;/ 에
/// cam1/, cam2/ JPG, frames.jsonl, cameras.json 을 기록한다.
/// 좌표는 Unity 월드 좌표를 변환 없이 그대로 기록한다.
/// </summary>
public class SimRecorder : MonoBehaviour
{
    [Header("Output")]
    public string sessionName = "unity-classroom-01";
    public SimScenario scenario = SimScenario.S1;
    public int frameCount = 300;
    public int framerate = 10;
    public long startTsMs = 1790900000000L;
    [Range(1, 100)] public int jpgQuality = 90;
    public bool quitWhenDone = true;

    [Header("Cameras")]
    public Camera cam1;
    public Camera cam2;
    public float verticalFovDeg = 46.8f;
    public bool applyCameraRig = true;
    [Tooltip("cam1은 (0,0,0) 모서리 고정. cam2는 A=(W,0,0) B=(0,0,D) C=(W,0,D)")]
    public Cam2Corner cam2Corner = Cam2Corner.A;
    public float roomWidth = 10.58f;   // X
    public float roomDepth = 8.57f;    // Z
    public float cameraInset = 0.3f;
    public float cameraHeight = 2.0f;
    public float cameraPitchDeg = 25f;

    [Header("Scene")]
    [Tooltip("사람·카트 등 시나리오 오브젝트를 코드로 생성")]
    public bool spawnPlaceholders = true;
    [Tooltip("임시 책상·의자까지 생성. 씬에 가구를 직접 배치했다면 끄기")]
    public bool spawnFurniturePlaceholders = false;
    [Tooltip("직접 배치한 책상·의자의 부모. 자식에 Tracked가 없으면 이름(desk*/chair*/cart*)으로 자동 추가")]
    public Transform furnitureRoot;
    [Tooltip("사람 모델 프리팹(피벗=발). 비우면 캡슐")]
    public GameObject personPrefab;
    [Tooltip("Moving(bool) 파라미터를 가진 Animator Controller")]
    public RuntimeAnimatorController personController;

    const int Width = 1920, Height = 1080;

    string outDir;
    RenderTexture rt;
    Texture2D tex;
    StreamWriter framesWriter;
    int frame;            // 마지막으로 기록한 프레임 번호 (1부터)
    bool recording;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    void Awake()
    {
        if (cam1 == null || cam2 == null)
        {
            Debug.LogError("SimRecorder: cam1/cam2를 지정하세요.");
            enabled = false;
            return;
        }

        if (applyCameraRig) ApplyRig();
        foreach (var c in new[] { cam1, cam2 }) ConfigureCamera(c);

        if (furnitureRoot != null) Tracked.AutoAddChildren(furnitureRoot);
        if (spawnPlaceholders) ScenarioBuilder.Build(scenario, spawnFurniturePlaceholders, personPrefab, personController);

        Time.captureFramerate = framerate;
    }

    void Start()
    {
        outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "unity_runs", sessionName));
        Directory.CreateDirectory(Path.Combine(outDir, "cam1"));
        Directory.CreateDirectory(Path.Combine(outDir, "cam2"));
        // 이전 실행 결과만 지운다 (같은 세션 이름으로 다시 돌릴 때)
        foreach (var d in new[] { "cam1", "cam2" })
            foreach (var f in Directory.GetFiles(Path.Combine(outDir, d), "*.jpg")) File.Delete(f);

        rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 1
        };
        rt.Create();
        tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam1.targetTexture = rt;   // WorldToScreenPoint 가 1920x1080 기준이 되도록 지정
        cam2.targetTexture = rt;

        LogObjectSizes();
        WriteCamerasJson();
        framesWriter = new StreamWriter(Path.Combine(outDir, "frames.jsonl"), false, new UTF8Encoding(false));
        recording = true;
        Debug.Log($"SimRecorder: 녹화 시작 → {outDir} ({frameCount}프레임, {scenario})");
    }

    void Update()
    {
        if (!recording) return;
        // 이번에 기록할 프레임 f의 시뮬레이션 시간 = (f-1) / framerate
        float t = frame / (float)framerate;
        foreach (var m in WaypointMover.All) m.Apply(t);
    }

    void LateUpdate()
    {
        if (!recording) return;
        frame++;
        SaveCamera(cam1, "cam1");
        SaveCamera(cam2, "cam2");
        WriteFrameLine();

        if (frame >= frameCount) Finish();
    }

    void Finish()
    {
        recording = false;
        framesWriter.Flush();
        framesWriter.Dispose();
        Debug.Log($"SimRecorder: 완료 ({frame}프레임) → {outDir}");
#if UNITY_EDITOR
        if (quitWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#else
        if (quitWhenDone) Application.Quit();
#endif
    }

    void OnDestroy()
    {
        if (framesWriter != null && recording) framesWriter.Dispose();
        if (rt != null) rt.Release();
        Time.captureFramerate = 0;
    }

    /// <summary>추적 오브젝트의 실제 크기(m)를 Console에 출력 — 모델 스케일 점검용.</summary>
    void LogObjectSizes()
    {
        var sb = new StringBuilder("SimRecorder: 오브젝트 크기 (가로 X × 높이 Y × 깊이 Z, m)\n");
        foreach (var obj in Tracked.All)
            if (obj.TryGetBounds(out Bounds b))
                sb.Append($"  {obj.objectId} ({obj.cls}): {b.size.x:0.00} × {b.size.y:0.00} × {b.size.z:0.00}\n");
        Debug.Log(sb.ToString());
    }

    // ---- 카메라 ----

    void ApplyRig()
    {
        Vector3 center = new Vector3(roomWidth * 0.5f, 0f, roomDepth * 0.5f);
        Place(cam1, new Vector3(cameraInset, cameraHeight, cameraInset), center);

        Vector3 p2;
        switch (cam2Corner)
        {
            case Cam2Corner.B: p2 = new Vector3(cameraInset, cameraHeight, roomDepth - cameraInset); break;
            case Cam2Corner.C: p2 = new Vector3(roomWidth - cameraInset, cameraHeight, roomDepth - cameraInset); break;
            default: p2 = new Vector3(roomWidth - cameraInset, cameraHeight, cameraInset); break;
        }
        Place(cam2, p2, center);
    }

    void Place(Camera cam, Vector3 pos, Vector3 lookTarget)
    {
        Vector3 d = lookTarget - pos;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(cameraPitchDeg, yaw, 0f));
    }

    void ConfigureCamera(Camera c)
    {
        c.enabled = false;               // Render()를 직접 호출해 두 카메라를 같은 프레임에 그린다
        c.usePhysicalProperties = false;
        c.fieldOfView = verticalFovDeg;  // Unity는 세로 화각
        c.allowHDR = false;
        c.allowMSAA = false;
        c.allowDynamicResolution = false;
        c.orthographic = false;
    }

    void SaveCamera(Camera cam, string folder)
    {
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(outDir, folder, frame.ToString("D6") + ".jpg"), tex.EncodeToJPG(jpgQuality));
    }

    // ---- JSON ----

    static string F(float v) => v.ToString("0.####", Inv);

    static string Vec(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";

    static string CamJson(Camera c, string id)
    {
        var t = c.transform;
        var q = t.rotation;
        return "    {\"camera_id\": \"" + id + "\", \"image_size\": [" + Width + ", " + Height + "], " +
               "\"vertical_fov_deg\": " + F(c.fieldOfView) + ", " +
               "\"position\": " + Vec(t.position) + ", " +
               "\"rotation_quat\": [" + F(q.x) + ", " + F(q.y) + ", " + F(q.z) + ", " + F(q.w) + "], " +
               "\"rotation_euler_deg\": " + Vec(t.eulerAngles) + "}";
    }

    void WriteCamerasJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"unity_version\": \"").Append(Application.unityVersion).Append("\",\n  \"cameras\": [\n");
        sb.Append(CamJson(cam1, "cam1")).Append(",\n").Append(CamJson(cam2, "cam2")).Append("\n  ]\n}\n");
        File.WriteAllText(Path.Combine(outDir, "cameras.json"), sb.ToString(), new UTF8Encoding(false));
    }

    void WriteFrameLine()
    {
        long ts = startTsMs + (long)frame * (1000 / framerate);
        var sb = new StringBuilder();
        sb.Append("{\"frame\": ").Append(frame).Append(", \"ts\": ").Append(ts).Append(", \"objects\": [");

        bool first = true;
        foreach (var obj in Tracked.All)
        {
            if (!obj.TryGetBounds(out Bounds b)) continue;

            // 사람(스킨 메시): 현재 포즈 꼭짓점 전체, 그 외: bounds 8개 꼭짓점
            if (!obj.TryGetPoseVertices(bboxPoints)) BoundsCorners(b, bboxPoints);

            var boxes = new List<string>();
            if (TryBBox(cam1, bboxPoints, out string b1)) boxes.Add("\"cam1\": " + b1);
            if (TryBBox(cam2, bboxPoints, out string b2)) boxes.Add("\"cam2\": " + b2);
            if (boxes.Count == 0) continue;   // 어느 카메라에도 안 보이면 기록하지 않음

            if (!first) sb.Append(", ");
            first = false;
            sb.Append("{\"object_id\": \"").Append(obj.objectId).Append("\", \"cls\": \"").Append(obj.cls)
              .Append("\", \"world\": ").Append(Vec(obj.GroundPoint(b)))
              .Append(", \"bbox\": {").Append(string.Join(", ", boxes)).Append("}}");
        }
        sb.Append("]}");
        framesWriter.WriteLine(sb.ToString());
    }

    readonly List<Vector3> bboxPoints = new List<Vector3>();

    static void BoundsCorners(Bounds b, List<Vector3> points)
    {
        points.Clear();
        Vector3 min = b.min, max = b.max;
        for (int i = 0; i < 8; i++)
            points.Add(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
    }

    /// <summary>월드 점들을 투영한 최소·최대. y는 위아래를 뒤집어 이미지 좌표로 만든다.</summary>
    bool TryBBox(Camera cam, List<Vector3> points, out string json)
    {
        json = null;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        foreach (var p in points)
        {
            Vector3 sp = cam.WorldToScreenPoint(p);
            if (sp.z <= 0f) continue;          // 카메라 뒤
            float y = Height - sp.y;
            minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
            minY = Mathf.Min(minY, y);    maxY = Mathf.Max(maxY, y);
            any = true;
        }
        if (!any) return false;

        minX = Mathf.Clamp(minX, 0f, Width);  maxX = Mathf.Clamp(maxX, 0f, Width);
        minY = Mathf.Clamp(minY, 0f, Height); maxY = Mathf.Clamp(maxY, 0f, Height);
        if (maxX - minX < 1f || maxY - minY < 1f) return false;   // 화면 밖

        json = $"[{F(minX)}, {F(minY)}, {F(maxX)}, {F(maxY)}]";
        return true;
    }
}
