using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>기대 경고: 이 쌍이 window(시작·끝 이벤트 이름) 동안 expect("warning" | "unscored")여야 한다. 적히지 않은 쌍은 경고 없음.</summary>
public struct ExpectedAlert
{
    public string a, b, expect, from, to;
    public ExpectedAlert(string a, string b, string expect, string from = "start", string to = "end")
    { this.a = a; this.b = b; this.expect = expect; this.from = from; this.to = to; }
}

/// <summary>Build 결과: 컷 길이, 이벤트 시각, 기대 경고 (events.json).</summary>
public class ScenarioInfo
{
    public int scenario, round;
    public string title;
    public float duration;
    public readonly List<KeyValuePair<string, float>> events = new List<KeyValuePair<string, float>>();
    public readonly List<ExpectedAlert> alerts = new List<ExpectedAlert>();

    public void Event(string name, float t) => events.Add(new KeyValuePair<string, float>(name, t));
}

/// <summary>
/// 촬영 계획서의 시나리오 0~9를 바닥 격자 위에 배치한다.
/// 격자: 열 A~E = x 0~4m, 행 1~5 = y 0~4m. A1은 원점 마커(aruco_00_floor) 기준 rel (-2, 1) — 마커는 C1에서 1m 앞(카메라 쪽).
/// 모든 컷: 3초 정지 → "출발"(t=3) → 이동 → 마지막 정지 후 3초.
/// </summary>
public static class ScenarioBuilder
{
    public const int MaxScenario = 9;
    public const int Scenario0Rounds = 3;

    /// <summary>격자 A1의 rel 좌표. 격자 좌표 + 이 값 = rel 좌표.</summary>
    public static readonly Vector2 GridOriginRel = new Vector2(-2f, 1f);

    const float Go = 3f;       // "출발" 시각
    const float Hold = 3f;     // 마지막 정지 후 녹화 시간
    const float Walk = 1f;     // 걷는 속도 (m/s)
    const float Slide = 0.5f;  // 3번 가방이 미끄러지는 속도 (m/s)

    // 계획서 객체 → id: P1~P3 = person_1~3, 캐리어 = backpack_1 (가방으로 대체), 의자1·2 = chair_1·2
    const string P1 = "person_1", P2 = "person_2", P3 = "person_3", Bag = "backpack_1", Chair1 = "chair_1", Chair2 = "chair_2";

    /// <summary>격자점 사이 추가 표시 (격자 좌표).</summary>
    static readonly Dictionary<string, Vector2> Marks = new Dictionary<string, Vector2>
    {
        { "S1", new Vector2(1.5f, 2f) }, { "S2", new Vector2(2.5f, 2f) }, { "S3", new Vector2(1.75f, 2f) },
        { "S4", new Vector2(2.25f, 2f) }, { "S5", new Vector2(2f, 2.75f) }, { "S6", new Vector2(1f, 0.75f) },
    };

    /// <summary>"C3" → (2, 2), "S6" → (1, 0.75). 격자 좌표(m).</summary>
    public static Vector2 Pt(string name)
    {
        if (Marks.TryGetValue(name, out Vector2 m)) return m;
        int col = name[0] - 'A', row = name[1] - '1';
        if (name.Length != 2 || col < 0 || col > 4 || row < 0 || row > 4) throw new ArgumentException("격자점 이름: " + name);
        return new Vector2(col, row);
    }

    /// <summary>
    /// 한 객체의 이동 경로를 시간 순으로 쌓아 키프레임으로 만든다 (좌표는 격자 이름).
    /// Until(t) = 그 자리에서 t까지 정지, To(점) = 일정 속도로 이동.
    /// </summary>
    class Track
    {
        public readonly List<WaypointMover.Key> keys = new List<WaypointMover.Key>();
        public float T { get; private set; }
        readonly Func<Vector2, Vector2> toXZ;
        Vector2 p;

        public Track(string start, Func<Vector2, Vector2> toXZ)
        {
            this.toXZ = toXZ;
            p = Pt(start);
            keys.Add(new WaypointMover.Key(0f, toXZ(p)));
        }

        public Track Until(float t)
        {
            if (t > T) { T = t; keys.Add(new WaypointMover.Key(T, toXZ(p))); }
            return this;
        }

        public Track To(string point, float speed = Walk)
        {
            Vector2 q = Pt(point);
            T += Vector2.Distance(p, q) / speed;
            p = q;
            keys.Add(new WaypointMover.Key(T, toXZ(p)));
            return this;
        }
    }

    /// <summary>시나리오 0번 회차별 배치: 캐리어, 의자1, 의자2, P1·P2·P3 (출발, 정지).</summary>
    static readonly string[][] Round0 =
    {
        new[] { "B2", "D2", "B4", "A1", "A5", "C3", "C5", "E5", "E1" },
        new[] { "C2", "A2", "E2", "A5", "D3", "C5", "D5", "E5", "D1" },
        new[] { "C3", "A1", "E5", "B2", "A3", "D4", "E1", "A5", "C5" },
    };

    public struct Assets
    {
        public GameObject person, backpack, chairSource;
        public RuntimeAnimatorController personController;
    }

    // Build 동안 쓰는 상태
    static Transform root;
    static MarkerFrame frame;
    static Assets assets;

    static Vector2 ToXZ(Vector2 grid)
    {
        Vector3 w = frame.ToWorld(grid + GridOriginRel);
        return new Vector2(w.x, w.z);
    }

    /// <summary>격자 x축과 나란한 회전에 yaw(도)를 더한 것.</summary>
    static Quaternion GridRotation(float yawDeg = 0f) => Quaternion.LookRotation(frame.y, Vector3.up) * Quaternion.Euler(0f, yawDeg, 0f);

    public static ScenarioInfo Build(int scenario, int round, MarkerFrame markerFrame, Assets a)
    {
        root = new GameObject("ScenarioObjects").transform;
        frame = markerFrame;
        assets = a;
        var info = new ScenarioInfo { scenario = scenario, round = round };
        info.Event("start", 0f);
        info.Event("go", Go);
        float end;

        switch (scenario)
        {
            case 0:
            {
                // 정지: 물체 3개 고정, 사람 3명이 자리를 한 번 옮김. 정지한 동안 모든 쌍 1.41m 이상
                if (round < 1 || round > Scenario0Rounds) throw new ArgumentException($"시나리오 0의 회차는 1~{Scenario0Rounds}: {round}");
                info.title = $"정지 {round}회";
                string[] r = Round0[round - 1];
                Backpack(Bag, r[0]);
                Chair(Chair1, r[1]);
                Chair(Chair2, r[2]);
                end = Mathf.Max(
                    Person(P1, new Track(r[3], ToXZ).Until(Go).To(r[4])),
                    Person(P2, new Track(r[5], ToXZ).Until(Go).To(r[6])),
                    Person(P3, new Track(r[7], ToXZ).Until(Go).To(r[8])));
                info.Event("all_stopped", end);
                break;
            }

            case 1:
            {
                // 마주 걷기: S1·S2 통과 순간 1.0m, S3·S4 정지 0.5m
                info.title = "마주 걷기";
                var p1 = new Track("A3", ToXZ).Until(Go).To("S1");
                var p2 = new Track("E3", ToXZ).Until(Go).To("S2");
                info.Event("pass_1m", Mathf.Max(p1.T, p2.T));
                end = Mathf.Max(Person(P1, p1.To("S3")), Person(P2, p2.To("S4")));
                info.Event("all_stopped", end);
                info.alerts.Add(new ExpectedAlert(P1, P2, "warning", "pass_1m"));
                break;
            }

            case 2:
            {
                // 엇갈려 지나가기: P1이 B3를 지날 때 P2 출발, C3에서 직각으로 엇갈림 (최소 약 0.71m)
                info.title = "엇갈려 지나가기";
                var p1 = new Track("A3", ToXZ).Until(Go).To("B3");
                float p2Go = p1.T;
                info.Event("p2_go", p2Go);
                info.Event("p1_at_C3", p1.To("C3").T);
                var p2 = new Track("C1", ToXZ).Until(p2Go).To("C3");
                info.Event("p2_at_C3", p2.T);
                end = Mathf.Max(Person(P1, p1.To("E3")), Person(P2, p2.To("C5")));
                info.Event("all_stopped", end);
                info.alerts.Add(new ExpectedAlert(P1, P2, "unscored"));
                break;
            }

            case 3:
            {
                // 가방 접근: 가방이 혼자 C2 → C4로 미끄러져 C5에 선 P2 앞 1.0m에서 멈춤
                info.title = "가방 접근";
                StandingPerson(P2, "C5");
                end = Backpack(Bag, "C2", new Track("C2", ToXZ).Until(Go).To("C4", Slide));
                info.Event("bag_stopped", end);
                info.alerts.Add(new ExpectedAlert(Bag, P2, "unscored"));   // 정지 거리 = 경고 기준
                break;
            }

            case 4:
            {
                // 의자 뒤에서 나오기: P1 S6 → B3, P2 E3 → S3, 정지 0.75m
                info.title = "의자 뒤에서 나오기";
                Chair(Chair1, "B1");
                Chair(Chair2, "D1");
                float t1 = Person(P1, new Track("S6", ToXZ).Until(Go).To("B3"));
                float t2 = Person(P2, new Track("E3", ToXZ).Until(Go).To("S3"));
                info.Event("p1_stopped", t1);
                info.Event("p2_stopped", t2);
                end = Mathf.Max(t1, t2);
                info.Event("all_stopped", end);
                info.alerts.Add(new ExpectedAlert(P1, Chair1, "warning", "start", "go"));   // 출발 전 0.75m
                info.alerts.Add(new ExpectedAlert(P1, P2, "warning", "all_stopped"));
                break;
            }

            case 5:
            {
                // 세 명 모이기: 모여서 3초 → "복귀" → 제자리
                info.title = "세 명 모이기";
                var p1 = new Track("A3", ToXZ).Until(Go).To("S3");
                var p2 = new Track("E3", ToXZ).Until(Go).To("S2");
                var p3 = new Track("C5", ToXZ).Until(Go).To("S5");
                float gathered = Mathf.Max(p1.T, p2.T, p3.T);
                float back = gathered + Hold;
                info.Event("gathered", gathered);
                info.Event("return", back);
                end = Mathf.Max(
                    Person(P1, p1.Until(back).To("A3")),
                    Person(P2, p2.Until(back).To("E3")),
                    Person(P3, p3.Until(back).To("C5")));
                info.Event("all_back", end);
                foreach (var (a1, b1) in new[] { (P1, P2), (P1, P3), (P2, P3) })
                    info.alerts.Add(new ExpectedAlert(a1, b1, "warning", "gathered", "return"));
                break;
            }

            case 6:
            {
                // 통로 물건 피하기: 물체 x=2 선, P1 x=4 선, 이어서 P2 x=0 선. 경고 없음
                info.title = "통로 물건 피하기";
                Chair(Chair1, "C1");
                Backpack(Bag, "C3");
                Chair(Chair2, "C5");
                float p1End = Person(P1, new Track("E1", ToXZ).Until(Go).To("E5"));
                info.Event("p1_stopped", p1End);
                end = Person(P2, new Track("A5", ToXZ).Until(p1End).To("A1"));
                info.Event("p2_stopped", end);
                break;
            }

            case 7:
            {
                // 가림막 뒤로 지나가기: 가림막(폭 1.5, 높이 1.8) 중심 (2, 1.5), P1은 0.5m 뒤 y=2 선
                info.title = "가림막 뒤로 지나가기";
                Partition(new Vector2(2f, 1.5f), 1.5f, 1.8f);
                end = Mathf.Max(
                    Person(P1, new Track("A3", ToXZ).Until(Go).To("E3")),
                    Person(P2, new Track("E5", ToXZ).Until(Go).To("A5")));
                info.Event("all_stopped", end);
                break;
            }

            case 8:
            {
                // 지그재그: 꺾이는 점에서 멈추지 않음. P2(C4)와 가장 가까울 때 1.41m
                info.title = "지그재그로 걷기";
                StandingPerson(P2, "C4");
                var p1 = new Track("A1", ToXZ).Until(Go);
                foreach (string turn in new[] { "B3", "C1", "D3" })
                    info.Event("turn_" + turn, p1.To(turn).T);
                end = Person(P1, p1.To("E1"));
                info.Event("p1_stopped", end);
                break;
            }

            case 9:
            {
                // 네모 경로: 둘 다 반시계 방향 한 바퀴(16m), 늘 반 바퀴 떨어짐
                info.title = "네모 경로 돌기";
                end = Mathf.Max(
                    Person(P1, new Track("A1", ToXZ).Until(Go).To("E1").To("E5").To("A5").To("A1")),
                    Person(P2, new Track("E5", ToXZ).Until(Go).To("A5").To("A1").To("E1").To("E5")));
                info.Event("all_stopped", end);
                break;
            }

            default:
                throw new ArgumentException($"시나리오는 0~{MaxScenario}: {scenario}");
        }

        info.duration = end + Hold;
        info.Event("end", info.duration);
        return info;
    }

    // ---- 객체 ----

    /// <summary>경로를 따라 걷는 사람. 반환 = 마지막 키프레임 시각.</summary>
    static float Person(string id, Track track)
    {
        var mover = PersonObject(id);
        mover.keys.AddRange(track.keys);
        mover.Apply(0f);
        return track.T;
    }

    /// <summary>내내 서 있는 사람. 카메라 쪽(격자 -y)을 본다.</summary>
    static void StandingPerson(string id, string point)
    {
        var mover = PersonObject(id);
        mover.keys.Add(new WaypointMover.Key(0f, ToXZ(Pt(point))));
        mover.transform.rotation = Quaternion.LookRotation(-frame.y, Vector3.up);
        mover.Apply(0f);
    }

    static WaypointMover PersonObject(string id)
    {
        var go = new GameObject(id);
        go.transform.SetParent(root, false);

        if (assets.person != null)
        {
            // 실제 사람 모델: 피벗이 발 위치인 프리팹이어야 한다
            var model = Object.Instantiate(assets.person, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            var anim = model.GetComponentInChildren<Animator>();
            // FBX를 No Avatar(Generic)로 임포트하면 Animator가 없어 걷기 애니메이션이 재생되지 않는다
            if (anim == null && assets.personController != null) anim = model.AddComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;   // 위치는 WaypointMover가 정한다
                // 카메라를 끈 채 Render()로 직접 그리므로 화면 밖 컬링에 걸리지 않게 한다
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (assets.personController != null) anim.runtimeAnimatorController = assets.personController;
            }
        }
        else
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            body.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f); // 높이 1.7m, 지름 0.5m
            StripCollider(body);
            body.GetComponent<Renderer>().material.color = new Color(0.9f, 0.5f, 0.1f);
        }

        var t = go.AddComponent<Tracked>();
        t.objectId = id;
        t.cls = "person";
        t.pivotIsGround = true;
        return go.AddComponent<WaypointMover>();
    }

    /// <summary>백팩 모델 원본이 실제 크기보다 커서(0.8m) 줄이는 배율 — 실제 백팩 길이 약 0.5m.</summary>
    const float BackpackScale = 0.65f;

    /// <summary>바닥에 놓인 가방 (계획서의 캐리어 대신). 넓은 면이 격자 x축과 나란하게. track이 있으면 그 경로로 미끄러진다.</summary>
    static float Backpack(string id, string point, Track track = null)
    {
        var go = Placed(id, "backpack", point, 0f);
        if (assets.backpack != null) AttachModel(go, assets.backpack, null, BackpackScale);
        else BackpackPlaceholder(go.transform);

        if (track == null) return 0f;
        var mover = go.AddComponent<WaypointMover>();
        mover.keys.AddRange(track.keys);
        mover.Apply(0f);
        return track.T;
    }

    /// <summary>schooldesk 모델에서 의자만 꺼내 쓴다. 등받이가 격자 x축과 나란하게.</summary>
    static void Chair(string id, string point)
    {
        var go = Placed(id, "chair", point, 0f);
        if (assets.chairSource != null) AttachModel(go, assets.chairSource, "chair", 1f);
        else Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), Vector3.zero, new Vector3(0.45f, 0.9f, 0.45f), new Color(0.2f, 0.3f, 0.6f));
    }

    /// <summary>가림막: 추적하지 않는 상자 (가리는 물체 역할만). 긴 변이 격자 x축과 나란하다.</summary>
    static void Partition(Vector2 grid, float width, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "partition";
        go.transform.SetParent(root, false);
        Vector2 xz = ToXZ(grid);
        go.transform.SetPositionAndRotation(new Vector3(xz.x, height * 0.5f, xz.y), GridRotation());
        go.transform.localScale = new Vector3(width, height, 0.05f);
        StripCollider(go);
        go.GetComponent<Renderer>().material.color = new Color(0.85f, 0.85f, 0.8f);
    }

    /// <summary>격자점 위에 Tracked 빈 오브젝트를 만든다. 모델은 AttachModel로 붙인다.</summary>
    static GameObject Placed(string id, string cls, string point, float yawDeg)
    {
        var go = new GameObject(id);
        go.transform.SetParent(root, false);
        Vector2 xz = ToXZ(Pt(point));
        go.transform.SetPositionAndRotation(new Vector3(xz.x, 0f, xz.y), GridRotation(yawDeg));
        var t = go.AddComponent<Tracked>();
        t.objectId = id;
        t.cls = cls;
        t.useMeshVertices = true;   // bbox = 메시 꼭짓점 투영 (메시 Read/Write 필요)
        return go;
    }

    /// <summary>
    /// 모델을 붙이고 바닥면 가운데(bounds 아래면 중심)가 go 위치에 오게 옮긴다.
    /// childFilter가 있으면 이름에 그 글자가 들어간 자식만 남긴다 (schooldesk에서 의자만).
    /// </summary>
    static void AttachModel(GameObject go, GameObject prefab, string childFilter, float scale)
    {
        var model = Object.Instantiate(prefab, go.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale *= scale;
        if (childFilter != null)
            foreach (Transform child in model.transform)
                if (child.name.IndexOf(childFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    child.gameObject.SetActive(false);   // Destroy는 프레임 끝에 적용되므로 bounds 계산에서 먼저 뺀다
                    Object.Destroy(child.gameObject);
                }
        foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);

        if (go.GetComponent<Tracked>().TryGetBounds(out Bounds b))
            model.transform.position += go.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
        else
            Debug.LogWarning($"ScenarioBuilder: {go.name} 모델에 보이는 렌더러가 없습니다 (childFilter={childFilter}).");
    }

    static void BackpackPlaceholder(Transform parent)
    {
        var navy = new Color(0.15f, 0.2f, 0.45f);
        Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.21f, 0f), Vector3.zero, new Vector3(0.30f, 0.42f, 0.17f), navy);                  // 본체
        Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.17f, 0.15f, 0.17f), navy);  // 둥근 윗면
        Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.13f, 0.12f), Vector3.zero, new Vector3(0.24f, 0.20f, 0.07f), new Color(0.25f, 0.35f, 0.7f)); // 앞주머니
        foreach (float sx in new[] { -0.08f, 0.08f })
            Part(parent, PrimitiveType.Cube, new Vector3(sx, 0.22f, -0.095f), Vector3.zero, new Vector3(0.05f, 0.34f, 0.02f), new Color(0.1f, 0.1f, 0.1f)); // 어깨끈
    }

    static void Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localEuler, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = size;
        StripCollider(go);
        go.GetComponent<Renderer>().material.color = color;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
    }
}
