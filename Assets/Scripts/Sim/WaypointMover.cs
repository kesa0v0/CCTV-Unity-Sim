using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시뮬레이션 시간 t의 순수 함수로 위치를 계산한다 (재생할 때마다 같은 결과).
/// 웨이포인트 사이를 일정 속도로 이동하고, 각 웨이포인트에서 wait초 멈춘다.
/// </summary>
public class WaypointMover : MonoBehaviour
{
    public static readonly List<WaypointMover> All = new List<WaypointMover>();

    public struct Stop
    {
        public Vector2 xz;   // 바닥 좌표 (x, z)
        public float wait;   // 도착 후 멈추는 시간(초)
        public Stop(float x, float z, float wait = 0f) { xz = new Vector2(x, z); this.wait = wait; }
    }

    /// <summary>시간 지정 키프레임: 시각 t(초)에 바닥 좌표 xz에 있고, 사이는 직선 보간.</summary>
    public struct Key
    {
        public float t;
        public Vector2 xz;
        public Key(float t, float x, float z) { this.t = t; xz = new Vector2(x, z); }
    }

    public List<Stop> stops = new List<Stop>();
    [Tooltip("비어 있지 않으면 stops/speed 대신 이 키프레임(시간 오름차순)으로 이동. 같은 좌표를 두 번 쓰면 그 사이는 정지.")]
    public List<Key> keys = new List<Key>();
    public float speed = 1f;
    public float startDelay;
    public float groundY;
    [Tooltip("true면 웨이포인트 사이를 걷지 않고 순간이동한다 (정지 장면용). 항상 대기 자세.")]
    public bool teleport;

    /// <summary>마지막 Apply에서 이동 중이었는지 (frames.jsonl의 moving).</summary>
    public bool IsMoving { get; private set; }

    Animator[] animators;
    Vector3 lastPos;
    bool hasLast;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Apply(float t)
    {
        if (keys.Count > 0) { ApplyKeys(t); return; }
        if (stops.Count == 0) return;
        float time = t - startDelay;
        Vector2 pos = stops[0].xz;
        Vector2 dir = Vector2.zero;
        bool haveDir = false;

        if (time > 0f)
        {
            float remaining = time;
            // 첫 웨이포인트 대기
            if (remaining < stops[0].wait) remaining = -1f;
            else remaining -= stops[0].wait;

            for (int i = 1; remaining >= 0f && i < stops.Count; i++)
            {
                Vector2 from = stops[i - 1].xz, to = stops[i].xz;
                float len = Vector2.Distance(from, to);
                float travel = teleport ? 0f : len / speed;
                dir = len > 1e-4f ? (to - from) / len : dir;
                haveDir = haveDir || len > 1e-4f;
                if (remaining < travel)
                {
                    pos = Vector2.Lerp(from, to, remaining / travel);
                    remaining = -1f;
                    break;
                }
                remaining -= travel;
                pos = to;
                if (remaining < stops[i].wait) { remaining = -1f; break; }
                remaining -= stops[i].wait;
            }
        }
        else
        {
            // 출발 전: 첫 지점에서 첫 이동 방향을 바라봄
            if (stops.Count > 1) { dir = (stops[1].xz - stops[0].xz).normalized; haveDir = true; }
        }

        var newPos = new Vector3(pos.x, groundY, pos.y);
        bool moving = !teleport && hasLast && (newPos - lastPos).sqrMagnitude > 1e-8f;
        lastPos = newPos;
        hasLast = true;
        transform.position = newPos;
        IsMoving = moving;
        SetMoving(moving);
        if (haveDir && dir.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
    }

    void ApplyKeys(float t)
    {
        Vector2 pos = keys[keys.Count - 1].xz;
        Vector2 dir = Vector2.zero;   // 직전(없으면 첫) 이동 구간의 방향
        Vector2 firstDir = Vector2.zero;
        bool placed = t <= keys[0].t;
        if (placed) pos = keys[0].xz;
        for (int i = 1; i < keys.Count; i++)
        {
            Vector2 from = keys[i - 1].xz, to = keys[i].xz;
            float len = Vector2.Distance(from, to);
            Vector2 d = len > 1e-4f ? (to - from) / len : Vector2.zero;
            if (firstDir == Vector2.zero) firstDir = d;
            if (!placed && t <= keys[i].t)
            {
                float span = keys[i].t - keys[i - 1].t;
                pos = span > 1e-6f ? Vector2.Lerp(from, to, (t - keys[i - 1].t) / span) : to;
                placed = true;
            }
            if (keys[i - 1].t <= t && d != Vector2.zero) dir = d;
        }
        if (dir == Vector2.zero) dir = firstDir;

        var newPos = new Vector3(pos.x, groundY, pos.y);
        bool moving = hasLast && (newPos - lastPos).sqrMagnitude > 1e-8f;
        lastPos = newPos;
        hasLast = true;
        transform.position = newPos;
        IsMoving = moving;
        SetMoving(moving);
        if (dir != Vector2.zero) transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
    }

    /// <summary>자식 Animator에 bool 파라미터 "Moving"이 있으면 이동 중 여부를 전달 (걷기/대기 전환).</summary>
    void SetMoving(bool moving)
    {
        if (animators == null) animators = GetComponentsInChildren<Animator>();
        foreach (var a in animators)
        {
            if (a == null || a.runtimeAnimatorController == null) continue;
            foreach (var prm in a.parameters)
                if (prm.name == "Moving" && prm.type == AnimatorControllerParameterType.Bool)
                    a.SetBool("Moving", moving);
        }
    }
}
