using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public enum Cam2Corner { A, B, C }

/// <summary>
/// Play 모드에서 시나리오를 재생하며 unity_runs/&lt;sessionName&gt;/ 에
/// cam1/, cam2/ JPG, frames.jsonl, cameras.json, events.json 을 기록한다.
/// 좌표는 Unity 월드 좌표(world)와 원점 마커 기준 상대 좌표(rel, MarkerFrame)를 함께 기록한다.
/// </summary>
public class SimRecorder : MonoBehaviour
{
    [Header("Output")]
    public string sessionName = "unity-grid-sc1";
    [Tooltip("촬영 계획서의 시나리오 번호 (0~9)")]
    [Range(0, ScenarioBuilder.MaxScenario)] public int scenario = 1;
    [Tooltip("시나리오 0의 회차 (1~3). 다른 시나리오는 무시")]
    [Range(1, ScenarioBuilder.Scenario0Rounds)] public int round = 1;
    public int framerate = 15;
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
    [Tooltip("rel 좌표 원점 마커. 비우면 이름이 aruco_00_floor인 오브젝트")]
    public Transform originMarker;
    [Tooltip("사람·가방·의자 등 시나리오 오브젝트를 코드로 생성")]
    public bool spawnPlaceholders = true;
    [Tooltip("직접 배치한 책상·의자의 부모. 격자와 겹치므로 기본은 숨김")]
    public Transform furnitureRoot;
    [Tooltip("furnitureRoot를 보이게 두고 추적할지. 자식에 Tracked가 없으면 이름(desk*/chair*/backpack*)으로 자동 추가")]
    public bool useFurniture = false;
    [Tooltip("사람 모델 프리팹(피벗=발). 비우면 캡슐")]
    public GameObject personPrefab;
    [Tooltip("Moving(bool) 파라미터를 가진 Animator Controller")]
    public RuntimeAnimatorController personController;
    [Tooltip("가방 모델 프리팹(피벗 무관 — 바닥에 자동 정렬). 비우면 기본 도형 가방. bbox 정밀도를 위해 메시 Read/Write 켜기")]
    public GameObject backpackPrefab;
    [Tooltip("의자를 꺼낼 모델(schooldesk.fbx). 이름에 chair가 들어간 자식만 쓴다. 비우면 상자. 메시 Read/Write 켜기")]
    public GameObject chairSourcePrefab;

    [Header("Extra ground truth")]
    [Tooltip("카메라별 가려짐 비율 visible_ratio 기록 (ID 마스크 추가 렌더링)")]
    public bool recordVisibility = true;
    [Tooltip("가려짐 계산 해상도 = 1920x1080 / 이 값")]
    [Range(1, 4)] public int visibilityDownscale = 2;
    [Tooltip("비우면 Hidden/Sim/IdMask")]
    public Shader idMaskShader;

    const int Width = 1920, Height = 1080;

    string outDir;
    RenderTexture rt;
    Texture2D tex;
    StreamWriter framesWriter;
    VisibilityMeter visibility;
    MarkerFrame markerFrame;
    ScenarioInfo info;
    int frameCount;       // 시나리오 길이로 정해짐
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
        if (originMarker == null)
        {
            var m = GameObject.Find("aruco_00_floor");
            if (m != null) originMarker = m.transform;
        }
        if (originMarker == null)
        {
            Debug.LogError("SimRecorder: 원점 마커(aruco_00_floor)가 없습니다. Tools/Place ArUco Markers를 실행하세요.");
            enabled = false;
            return;
        }
        markerFrame = new MarkerFrame(originMarker);

        if (applyCameraRig) ApplyRig();
        foreach (var c in new[] { cam1, cam2 }) ConfigureCamera(c);

        if (furnitureRoot != null)
        {
            if (useFurniture) Tracked.AutoAddChildren(furnitureRoot);
            else furnitureRoot.gameObject.SetActive(false);
        }
        if (spawnPlaceholders)
        {
            var assets = new ScenarioBuilder.Assets
            {
                person = personPrefab, personController = personController, backpack = backpackPrefab, chairSource = chairSourcePrefab,
            };
            info = ScenarioBuilder.Build(scenario, scenario == 0 ? round : 0, markerFrame, assets);
            frameCount = Mathf.RoundToInt(info.duration * framerate) + 1;   // 마지막 프레임 시각 = duration
        }
        else
        {
            info = null;
            frameCount = 10 * framerate;
        }

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
        CheckGridVisible();
        if (recordVisibility)
        {
            var sh = idMaskShader != null ? idMaskShader : Shader.Find("Hidden/Sim/IdMask");
            if (sh == null) Debug.LogError("SimRecorder: Hidden/Sim/IdMask 셰이더를 찾을 수 없어 visible_ratio를 기록하지 않습니다.");
            else visibility = new VisibilityMeter(sh, Width / visibilityDownscale, Height / visibilityDownscale);
        }
        WriteCamerasJson();
        if (info != null) WriteEventsJson();
        framesWriter = new StreamWriter(Path.Combine(outDir, "frames.jsonl"), false, new UTF8Encoding(false));
        recording = true;
        string what = info != null ? $"시나리오 {info.scenario}{(info.scenario == 0 ? $"-{info.round}" : "")} {info.title}" : "시나리오 없음";
        Debug.Log($"SimRecorder: 녹화 시작 → {outDir} ({what}, {frameCount}프레임 = {(frameCount - 1) / (float)framerate:0.#}초 @ {framerate}fps)");
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
        visibility?.Release();
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

    /// <summary>격자점 25개의 바닥이 두 카메라 화면 안에 있는지 확인 — 원점 마커를 옮기지 않았으면 격자가 화면 밖으로 나간다.</summary>
    void CheckGridVisible()
    {
        var missing = new List<string>();
        for (int c = 0; c < 5; c++)
            for (int r = 0; r < 5; r++)
            {
                string name = $"{(char)('A' + c)}{r + 1}";
                Vector3 w = markerFrame.ToWorld(ScenarioBuilder.Pt(name) + ScenarioBuilder.GridOriginRel);
                foreach (var cam in new[] { cam1, cam2 })
                {
                    Vector3 sp = cam.WorldToScreenPoint(w);
                    if (sp.z <= 0f || sp.x < 0f || sp.x > Width || sp.y < 0f || sp.y > Height) missing.Add($"{name}({cam.name})");
                }
            }
        if (missing.Count > 0)
            Debug.LogWarning("SimRecorder: 화면 밖 격자점 — 원점 마커 위치를 확인하세요 (Tools/Place ArUco Markers): " + string.Join(", ", missing));
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

    static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    string CamJson(Camera c, string id)
    {
        var t = c.transform;
        var q = t.rotation;
        float fy = Height * 0.5f / Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        // OpenCV 카메라 축(x 오른쪽, y 아래, z 앞)을 rel 좌표로 — rel ← 카메라 회전행렬의 열
        return "    {\"camera_id\": \"" + id + "\", \"image_size\": [" + Width + ", " + Height + "], " +
               "\"vertical_fov_deg\": " + F(c.fieldOfView) + ", " +
               "\"intrinsics_cv\": {\"fx\": " + F(fy) + ", \"fy\": " + F(fy) + ", \"cx\": " + F(Width * 0.5f) + ", \"cy\": " + F(Height * 0.5f) + "}, " +
               "\"position\": " + Vec(t.position) + ", " +
               "\"rotation_quat\": [" + F(q.x) + ", " + F(q.y) + ", " + F(q.z) + ", " + F(q.w) + "], " +
               "\"rotation_euler_deg\": " + Vec(t.eulerAngles) + ", " +
               "\"rel_position\": " + Vec(markerFrame.ToRel(t.position)) + ", " +
               "\"rel_axes_cv\": {\"x_right\": " + Vec(markerFrame.DirToRel(t.right)) +
               ", \"y_down\": " + Vec(markerFrame.DirToRel(-t.up)) +
               ", \"z_forward\": " + Vec(markerFrame.DirToRel(t.forward)) + "}}";
    }

    void WriteCamerasJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"unity_version\": \"").Append(Application.unityVersion).Append("\",\n");
        sb.Append("  \"rel_frame\": {\"marker\": ").Append(Str(originMarker.name))
          .Append(", \"origin_world\": ").Append(Vec(markerFrame.origin))
          .Append(", \"x_world\": ").Append(Vec(markerFrame.x))
          .Append(", \"y_world\": ").Append(Vec(markerFrame.y))
          .Append(", \"z_world\": ").Append(Vec(markerFrame.z))
          .Append(", \"grid_A1_rel\": [").Append(F(ScenarioBuilder.GridOriginRel.x)).Append(", ").Append(F(ScenarioBuilder.GridOriginRel.y)).Append("]},\n");
        sb.Append("  \"cameras\": [\n");
        sb.Append(CamJson(cam1, "cam1")).Append(",\n").Append(CamJson(cam2, "cam2")).Append("\n  ]\n}\n");
        File.WriteAllText(Path.Combine(outDir, "cameras.json"), sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>시나리오 메타데이터: 이벤트 시각(초·프레임)과 기대 경고. 프레임 f의 시각 = (f-1)/fps.</summary>
    void WriteEventsJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"scenario\": ").Append(info.scenario)
          .Append(",\n  \"round\": ").Append(info.round)
          .Append(",\n  \"title\": ").Append(Str(info.title))
          .Append(",\n  \"fps\": ").Append(framerate)
          .Append(",\n  \"frame_count\": ").Append(frameCount)
          .Append(",\n  \"duration_s\": ").Append(F(info.duration))
          .Append(",\n  \"events\": [\n");
        for (int i = 0; i < info.events.Count; i++)
        {
            var e = info.events[i];
            sb.Append("    {\"name\": ").Append(Str(e.Key)).Append(", \"t\": ").Append(F(e.Value))
              .Append(", \"frame\": ").Append(Mathf.RoundToInt(e.Value * framerate) + 1).Append("}")
              .Append(i < info.events.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n  \"expected_alerts\": [");
        for (int i = 0; i < info.alerts.Count; i++)
        {
            var a = info.alerts[i];
            sb.Append(i == 0 ? "\n" : ",\n")
              .Append("    {\"pair\": [").Append(Str(a.a)).Append(", ").Append(Str(a.b)).Append("], \"expect\": ").Append(Str(a.expect))
              .Append(", \"from\": ").Append(Str(a.from)).Append(", \"to\": ").Append(Str(a.to)).Append("}");
        }
        sb.Append(info.alerts.Count > 0 ? "\n  ],\n" : "],\n");
        sb.Append("  \"note\": \"expected_alerts에 없는 쌍은 경고가 없어야 한다. 경고 1m 미만, 위험 0.5m 미만 (바닥 거리).\"\n}\n");
        File.WriteAllText(Path.Combine(outDir, "events.json"), sb.ToString(), new UTF8Encoding(false));
    }

    void WriteFrameLine()
    {
        long ts = startTsMs + (long)Math.Round((frame - 1) * 1000.0 / framerate);
        var sb = new StringBuilder();
        sb.Append("{\"frame\": ").Append(frame).Append(", \"ts\": ").Append(ts).Append(", \"objects\": [");
        var vis1 = visibility?.Measure(cam1);
        var vis2 = visibility?.Measure(cam2);

        bool first = true;
        foreach (var obj in Tracked.All)
        {
            if (!obj.TryGetBounds(out Bounds b)) continue;

            // 사람(스킨 메시)·useMeshVertices: 현재 포즈 꼭짓점 전체, 그 외: bounds 8개 꼭짓점
            if (!obj.TryGetPoseVertices(bboxPoints)) BoundsCorners(b, bboxPoints);

            var boxes = new List<string>();
            if (TryBBox(cam1, bboxPoints, out string b1)) boxes.Add("\"cam1\": " + b1);
            if (TryBBox(cam2, bboxPoints, out string b2)) boxes.Add("\"cam2\": " + b2);
            if (boxes.Count == 0) continue;   // 어느 카메라에도 안 보이면 기록하지 않음

            Vector3 ground = obj.GroundPoint(b);
            if (!first) sb.Append(", ");
            first = false;
            sb.Append("{\"object_id\": \"").Append(obj.objectId).Append("\", \"cls\": \"").Append(obj.cls)
              .Append("\", \"world\": ").Append(Vec(ground))
              .Append(", \"rel\": ").Append(Vec(markerFrame.ToRel(ground)))
              .Append(", \"bbox\": {").Append(string.Join(", ", boxes)).Append("}");
            var mover = obj.GetComponent<WaypointMover>();
            sb.Append(", \"moving\": ").Append(mover != null && mover.IsMoving ? "true" : "false");
            if (visibility != null)
                sb.Append(", \"visible_ratio\": {\"cam1\": ").Append(Ratio(vis1, obj))
                  .Append(", \"cam2\": ").Append(Ratio(vis2, obj)).Append("}");
            if (obj.TryGetAnkles(out Vector3 la, out Vector3 ra))
            {
                Vector3 mid = (la + ra) * 0.5f;
                sb.Append(", \"ankles\": {\"world\": {\"left\": ").Append(Vec(la)).Append(", \"right\": ").Append(Vec(ra)).Append(", \"mid\": ").Append(Vec(mid)).Append("}")
                  .Append(", \"rel\": {\"left\": ").Append(Vec(markerFrame.ToRel(la))).Append(", \"right\": ").Append(Vec(markerFrame.ToRel(ra)))
                  .Append(", \"mid\": ").Append(Vec(markerFrame.ToRel(mid))).Append("}")
                  .Append(", \"cam1\": ").Append(AnklePx(cam1, la, ra))
                  .Append(", \"cam2\": ").Append(AnklePx(cam2, la, ra)).Append("}");
            }
            sb.Append("}");
        }
        sb.Append("]}");
        framesWriter.WriteLine(sb.ToString());
    }

    readonly List<Vector3> bboxPoints = new List<Vector3>();

    static string Ratio(Dictionary<Tracked, float> vis, Tracked obj) =>
        vis != null && vis.TryGetValue(obj, out float r) ? F(r) : "null";   // null = 이 카메라 화면에 없음

    /// <summary>양 발목의 이미지 좌표 (1920x1080, 좌상단 원점). 화면 밖이어도 그대로, 카메라 뒤면 null.</summary>
    static string AnklePx(Camera cam, Vector3 left, Vector3 right) =>
        "{\"left\": " + Px(cam, left) + ", \"right\": " + Px(cam, right) + "}";

    static string Px(Camera cam, Vector3 p)
    {
        Vector3 sp = cam.WorldToScreenPoint(p);
        return sp.z <= 0f ? "null" : $"[{F(sp.x)}, {F(Height - sp.y)}]";
    }

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
