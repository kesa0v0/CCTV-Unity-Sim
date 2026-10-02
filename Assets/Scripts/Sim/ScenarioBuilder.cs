using UnityEngine;

public enum SimScenario { S1, S2 }

/// <summary>
/// 임시(placeholder) 가구·사람·카트를 코드로 배치하고 시나리오 이동 경로를 설정한다.
/// 실제 모델을 쓸 때는 Tracked + (필요 시) WaypointMover를 직접 붙여 씬에 두고 spawnPlaceholders를 끄면 된다.
/// 방 좌표: X 0~10.58, Z 0~8.57, 원점 = 바닥 모서리.
/// </summary>
public static class ScenarioBuilder
{
    public static void Build(SimScenario scenario, bool includeFurniture, GameObject personPrefab = null, RuntimeAnimatorController personController = null)
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

        if (scenario == SimScenario.S1)
        {
            // 사람 1명: 책상 사이를 걷다가 멈춤 (약 30초)
            Person(root, personPrefab, personController, "person_1", 1f, 0f,
                new WaypointMover.Stop(2.8f, 2.2f),
                new WaypointMover.Stop(2.8f, 4.0f),
                new WaypointMover.Stop(5.2f, 4.0f, 3f),
                new WaypointMover.Stop(5.2f, 7.5f),
                new WaypointMover.Stop(9.0f, 7.5f, 3f),
                new WaypointMover.Stop(9.0f, 4.0f),
                new WaypointMover.Stop(5.2f, 4.0f));
        }
        else
        {
            // 사람 2명이 (5.2, 4.0) 부근에서 교차 + 카트 1대 이동
            Person(root, personPrefab, personController, "person_1", 1f, 0f,
                new WaypointMover.Stop(3.0f, 2.0f),
                new WaypointMover.Stop(5.2f, 2.0f),
                new WaypointMover.Stop(5.2f, 7.5f),
                new WaypointMover.Stop(9.0f, 7.5f));
            Person(root, personPrefab, personController, "person_2", 1f, 2.4f,
                new WaypointMover.Stop(9.5f, 4.0f),
                new WaypointMover.Stop(1.2f, 4.0f),
                new WaypointMover.Stop(1.2f, 7.0f));
            var cart = Box(root, "cart_1", "cart", new Vector3(1.5f, 0.45f, 6.6f), new Vector3(0.8f, 0.9f, 0.5f), new Color(0.3f, 0.6f, 0.3f));
            var mover = cart.AddComponent<WaypointMover>();
            mover.speed = 0.5f;
            mover.groundY = 0.45f;
            mover.stops.Add(new WaypointMover.Stop(1.5f, 6.6f));
            mover.stops.Add(new WaypointMover.Stop(9.5f, 6.6f));
        }
    }

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

    static void Person(Transform parent, GameObject prefab, RuntimeAnimatorController controller, string id, float speed, float delay, params WaypointMover.Stop[] stops)
    {
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);

        if (prefab != null)
        {
            // 실제 사람 모델: 피벗이 발 위치인 프리팹이어야 한다
            var model = Object.Instantiate(prefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var anim = model.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;   // 위치는 WaypointMover가 정한다
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
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
    }
}
