using UnityEngine;

public enum SimScenario { S1, S2, S3, S4 }

/// <summary>
/// 임시(placeholder) 가구·사람·카트를 코드로 배치하고 시나리오 이동 경로를 설정한다.
/// 실제 모델을 쓸 때는 Tracked + (필요 시) WaypointMover를 직접 붙여 씬에 두고 spawnPlaceholders를 끄면 된다.
/// 방 좌표: X 0~10.58, Z 0~8.57, 원점 = 바닥 모서리.
/// </summary>
public static class ScenarioBuilder
{
    public static void Build(SimScenario scenario, bool includeFurniture, GameObject personPrefab = null, RuntimeAnimatorController personController = null, GameObject cartPrefab = null, GameObject backpackPrefab = null)
    {
        var root = new GameObject("ScenarioObjects").transform;

        // 책상 4개 + 의자 4개 (고정 배치). 씬에 직접 가구를 배치했다면 includeFurniture=false
        float[] xs = { 3.5f, 7.0f };
        float[] zs = { 3.0f, 5.5f };
        int n = 0;
        if (includeFurniture)
            foreach (float x in xs)
                foreach (float z in zs)
                {
                n++;
                Box(root, $"desk_{n}", "desk", new Vector3(x, 0.375f, z), new Vector3(1.2f, 0.75f, 0.6f), new Color(0.55f, 0.4f, 0.25f));
                Box(root, $"chair_{n}", "chair", new Vector3(x, 0.45f, z - 0.7f), new Vector3(0.45f, 0.9f, 0.45f), new Color(0.2f, 0.3f, 0.6f));
            }

        switch (scenario)
        {
            case SimScenario.S1:
                // 사람 1명이 방 전체를 한 바퀴: 두 카메라 가까운 곳(약 2.4m) → 오른쪽 → 먼 쪽 → 왼쪽 → 가운데 (약 28초)
                Person(root, personPrefab, personController, "person_1", 1f, 0f,
                    new WaypointMover.Stop(2.0f, 2.0f, 1f),
                    new WaypointMover.Stop(8.6f, 2.0f),
                    new WaypointMover.Stop(9.2f, 4.5f),
                    new WaypointMover.Stop(8.4f, 7.4f, 1f),
                    new WaypointMover.Stop(2.2f, 7.4f),
                    new WaypointMover.Stop(1.4f, 4.5f),
                    new WaypointMover.Stop(3.6f, 3.4f),
                    new WaypointMover.Stop(5.3f, 1.8f));
                break;

            case SimScenario.S2:
                // 사람 2명이 가까운 줄(z 2.2/3.4)과 먼 줄(z 7.6/6.6)에서 1m 간격으로 엇갈려 지나감 + 카트 왕복 (약 27초)
                Person(root, personPrefab, personController, "person_1", 1f, 0f,
                    new WaypointMover.Stop(1.2f, 2.2f),
                    new WaypointMover.Stop(9.4f, 2.2f),
                    new WaypointMover.Stop(9.0f, 7.6f),
                    new WaypointMover.Stop(2.2f, 7.6f),
                    new WaypointMover.Stop(2.6f, 4.2f),
                    new WaypointMover.Stop(3.6f, 1.6f));
                Person(root, personPrefab, personController, "person_2", 1f, 0f,
                    new WaypointMover.Stop(9.4f, 3.4f),
                    new WaypointMover.Stop(1.2f, 3.4f),
                    new WaypointMover.Stop(1.6f, 6.6f),
                    new WaypointMover.Stop(8.4f, 6.6f),
                    new WaypointMover.Stop(7.6f, 3.4f),
                    new WaypointMover.Stop(5.3f, 1.8f));
                Cart(root, cartPrefab, "cart_1", 0.5f,
                    new WaypointMover.Stop(7.0f, 5.6f),
                    new WaypointMover.Stop(10.0f, 5.6f, 8f),
                    new WaypointMover.Stop(7.0f, 5.6f));
                break;

            case SimScenario.S3:
                // 정지 장면: 9곳(가까움/중간/먼 줄 × 왼쪽/가운데/오른쪽)에 순간이동해 3초씩 서 있기 → 270프레임
                var p = Person(root, personPrefab, personController, "person_1", 1f, 0f, StaticPoints);
                p.teleport = true;
                p.Apply(0f);
                break;

            case SimScenario.S4:
                // 사람 2명이 가까워지는 5개 구간(각 10초) + 통로에 놓인 백팩 3개. 구간 사이는 걸어서 이동 (총 50초 = 500프레임)
                BuildS4(root, personPrefab, personController, backpackPrefab);
                break;
        }
    }

    // S4 키프레임 (시각 초, x, z). 최소 거리: 4-1 0.5m(t≈3.5~7) · 4-2 0.3m(t≈14.2) · 4-3 0.5m(t≈23.4~25) · 4-4 1.0m(t≈31~36) · 4-5 0.5m(t≈44.8~).
    // 구간 밖(이동 중)에는 두 사람이 1m 이상 떨어져 있고, 모든 점이 두 카메라 시야 안이며 책상·의자 영역(x 3.7~7.0, z 4.0~6.0)을 피한다.
    static readonly WaypointMover.Key[] S4PersonA =
    {
        new WaypointMover.Key(0f, 2.2f, 3.0f), new WaypointMover.Key(0.5f, 2.2f, 3.0f),
        new WaypointMover.Key(3.5f, 5.05f, 3.0f),   // 4-1 정면 접근
        new WaypointMover.Key(8.5f, 5.05f, 3.0f),
        new WaypointMover.Key(10.5f, 7.0f, 3.6f), new WaypointMover.Key(12.0f, 7.0f, 5.0f), new WaypointMover.Key(13.0f, 7.0f, 5.0f),
        new WaypointMover.Key(14.7f, 8.7f, 5.0f),   // 4-2 직각 교차 (B 앞을 가로지름)
        new WaypointMover.Key(15.0f, 8.7f, 5.0f),
        new WaypointMover.Key(15.9f, 8.0f, 4.6f), new WaypointMover.Key(26.4f, 8.0f, 4.6f),   // 4-3 A는 서 있음
        new WaypointMover.Key(28.3f, 8.2f, 2.8f), new WaypointMover.Key(31.0f, 8.2f, 2.8f),
        new WaypointMover.Key(36.2f, 3.0f, 2.8f),   // 4-4 나란히 걷기 (-x 방향)
        new WaypointMover.Key(36.5f, 3.0f, 2.8f),
        new WaypointMover.Key(41.0f, 2.4f, 7.3f), new WaypointMover.Key(43.0f, 2.4f, 7.3f),
        new WaypointMover.Key(44.5f, 3.9f, 7.3f),   // 4-5 먼 곳 정면 접근
        new WaypointMover.Key(50.0f, 3.9f, 7.3f),
    };

    static readonly WaypointMover.Key[] S4PersonB =
    {
        new WaypointMover.Key(0f, 8.4f, 3.0f), new WaypointMover.Key(0.5f, 8.4f, 3.0f),
        new WaypointMover.Key(3.5f, 5.55f, 3.0f), new WaypointMover.Key(7.0f, 5.55f, 3.0f),
        new WaypointMover.Key(9.45f, 8.0f, 3.0f), new WaypointMover.Key(12.424f, 8.0f, 3.0f),
        new WaypointMover.Key(16.424f, 8.0f, 7.0f), new WaypointMover.Key(21.5f, 8.0f, 7.0f),   // 4-2에서 +z로 걷다가 4-3 시작 위치에서 대기
        new WaypointMover.Key(23.4f, 8.0f, 5.1f), new WaypointMover.Key(25.0f, 8.0f, 5.1f),     // 4-3 A에게 다가와 0.5m
        new WaypointMover.Key(26.3f, 8.0f, 6.4f), new WaypointMover.Key(27.3f, 8.0f, 6.4f),     // 멀어짐
        new WaypointMover.Key(29.9f, 8.2f, 3.8f), new WaypointMover.Key(31.0f, 8.2f, 3.8f),
        new WaypointMover.Key(36.2f, 3.0f, 3.8f), new WaypointMover.Key(36.5f, 3.0f, 3.8f),
        new WaypointMover.Key(37.5f, 3.2f, 4.8f), new WaypointMover.Key(39.1f, 3.4f, 6.4f),
        new WaypointMover.Key(42.1f, 6.2f, 7.3f), new WaypointMover.Key(43.0f, 6.2f, 7.3f),
        new WaypointMover.Key(44.8f, 4.4f, 7.3f),
        new WaypointMover.Key(50.0f, 4.4f, 7.3f),
    };

    /// <summary>S4 백팩 (x, z, yaw°): 가까운 쪽 / 먼 쪽 / 오른쪽 앞 통로. 사람이 지나가는 길에서 0.6~0.9m.</summary>
    static readonly Vector3[] S4Backpacks =
    {
        new Vector3(3.6f, 2.0f, 30f),
        new Vector3(5.6f, 7.9f, -20f),
        new Vector3(7.3f, 2.4f, 70f),
    };

    static void BuildS4(Transform root, GameObject personPrefab, RuntimeAnimatorController personController, GameObject backpackPrefab)
    {
        var a = Person(root, personPrefab, personController, "person_1", 1f, 0f);
        a.keys.AddRange(S4PersonA);
        a.Apply(0f);
        var b = Person(root, personPrefab, personController, "person_2", 1f, 0f);
        b.keys.AddRange(S4PersonB);
        b.Apply(0f);

        for (int i = 0; i < S4Backpacks.Length; i++)
            Backpack(root, backpackPrefab, $"backpack_{i + 1}", new Vector2(S4Backpacks[i].x, S4Backpacks[i].y), S4Backpacks[i].z);
    }

    /// <summary>S3 정지 지점. 각 3초 대기 (첫 지점은 시작부터 3초).</summary>
    public static readonly WaypointMover.Stop[] StaticPoints =
    {
        new WaypointMover.Stop(2.0f, 2.0f, 3f), new WaypointMover.Stop(5.3f, 1.8f, 3f), new WaypointMover.Stop(8.6f, 2.0f, 3f),
        new WaypointMover.Stop(1.6f, 4.5f, 3f), new WaypointMover.Stop(5.3f, 3.4f, 3f), new WaypointMover.Stop(9.0f, 4.5f, 3f),
        new WaypointMover.Stop(2.2f, 7.4f, 3f), new WaypointMover.Stop(5.3f, 7.6f, 3f), new WaypointMover.Stop(8.4f, 7.4f, 3f),
    };

    static GameObject Box(Transform parent, string id, string cls, Vector3 pos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = id;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        StripCollider(go);
        go.GetComponent<Renderer>().material.color = color;
        var t = go.AddComponent<Tracked>();
        t.objectId = id;
        t.cls = cls;
        return go;
    }

    static void Cart(Transform parent, GameObject prefab, string id, float speed, params WaypointMover.Stop[] stops)
    {
        GameObject go;
        if (prefab != null)
        {
            // 실제 카트 모델: 피벗과 무관하게 바닥(y=0)에 놓이도록 모델을 올린다
            go = new GameObject(id);
            go.transform.SetParent(parent, false);
            var model = Object.Instantiate(prefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var t = go.AddComponent<Tracked>();
            t.objectId = id;
            t.cls = "cart";
            if (t.TryGetBounds(out Bounds b)) model.transform.position -= new Vector3(0f, b.min.y, 0f);
        }
        else
        {
            go = Box(parent, id, "cart", Vector3.zero, new Vector3(0.8f, 0.9f, 0.5f), new Color(0.3f, 0.6f, 0.3f));
        }

        var mover = go.AddComponent<WaypointMover>();
        mover.speed = speed;
        mover.groundY = prefab != null ? 0f : 0.45f;
        mover.stops.AddRange(stops);
        mover.Apply(0f);
    }

    /// <summary>백팩 모델 원본이 실제 크기보다 커서(0.8m) 줄이는 배율 — 실제 백팩 길이 약 0.5m.</summary>
    const float BackpackScale = 0.65f;

    /// <summary>바닥에 놓인 백팩. 피벗 = 바닥 접촉면 중심. 프리팹이 없으면 기본 도형으로 만든 임시 백팩.</summary>
    static void Backpack(Transform parent, GameObject prefab, string id, Vector2 xz, float yawDeg)
    {
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(xz.x, 0f, xz.y), Quaternion.Euler(0f, yawDeg, 0f));

        var t = go.AddComponent<Tracked>();
        t.objectId = id;
        t.cls = "backpack";
        t.useMeshVertices = true;   // bbox = 메시 꼭짓점 투영

        if (prefab != null)
        {
            var model = Object.Instantiate(prefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale *= BackpackScale;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            if (t.TryGetBounds(out Bounds b)) model.transform.position -= new Vector3(0f, b.min.y, 0f);
            return;
        }

        var navy = new Color(0.15f, 0.2f, 0.45f);
        Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.21f, 0f), Vector3.zero, new Vector3(0.30f, 0.42f, 0.17f), navy);                  // 본체
        Part(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.17f, 0.15f, 0.17f), navy);  // 둥근 윗면
        Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.13f, 0.12f), Vector3.zero, new Vector3(0.24f, 0.20f, 0.07f), new Color(0.25f, 0.35f, 0.7f)); // 앞주머니
        foreach (float sx in new[] { -0.08f, 0.08f })
            Part(go.transform, PrimitiveType.Cube, new Vector3(sx, 0.22f, -0.095f), Vector3.zero, new Vector3(0.05f, 0.34f, 0.02f), new Color(0.1f, 0.1f, 0.1f)); // 어깨끈
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

    static WaypointMover Person(Transform parent, GameObject prefab, RuntimeAnimatorController controller, string id, float speed, float delay, params WaypointMover.Stop[] stops)
    {
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);

        if (prefab != null)
        {
            // 실제 사람 모델: 피벗이 발 위치인 프리팹이어야 한다
            var model = Object.Instantiate(prefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            var anim = model.GetComponentInChildren<Animator>();
            // FBX를 No Avatar(Generic)로 임포트하면 Animator가 없어 걷기 애니메이션이 재생되지 않는다
            if (anim == null && controller != null) anim = model.AddComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;   // 위치는 WaypointMover가 정한다
                // 카메라를 끈 채 Render()로 직접 그리므로 화면 밖 컬링에 걸리지 않게 한다
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (controller != null) anim.runtimeAnimatorController = controller;
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

        var mover = go.AddComponent<WaypointMover>();
        mover.speed = speed;
        mover.startDelay = delay;
        mover.stops.AddRange(stops);
        mover.Apply(0f);
        return mover;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
    }
}
