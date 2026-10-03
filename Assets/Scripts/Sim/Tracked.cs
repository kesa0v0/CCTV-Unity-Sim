using System.Collections.Generic;
using UnityEngine;

/// <summary>정답 데이터(frames.jsonl)에 기록할 오브젝트. cls: person, chair, cart, desk.</summary>
public class Tracked : MonoBehaviour
{
    public static readonly List<Tracked> All = new List<Tracked>();

    public string objectId;
    public string cls;

    [Tooltip("true면 transform.position(피벗=발)을 world로 쓴다. false면 렌더러 bounds 아래면 중심.")]
    public bool pivotIsGround;

    /// <summary>부모의 자식마다 Tracked를 붙인다. cls = 이름에서 추정, id = cls_번호(하이어라키 순서대로 1부터).</summary>
    public static void AutoAddChildren(Transform root)
    {
        var counts = new Dictionary<string, int>();
        foreach (Transform child in root)
        {
            if (child.GetComponent<Tracked>() != null) continue;
            string lower = child.name.ToLowerInvariant();
            string cls = lower.Contains("chair") ? "chair"
                       : lower.Contains("desk") || lower.Contains("table") ? "desk"
                       : lower.Contains("cart") ? "cart"
                       : lower.Contains("person") ? "person" : null;
            if (cls == null)
            {
                Debug.LogWarning($"Tracked: '{child.name}'의 cls를 알 수 없어 건너뜀 (이름에 desk/chair/cart/person 포함 필요)");
                continue;
            }
            var t = child.gameObject.AddComponent<Tracked>();
            counts.TryGetValue(cls, out int n);
            counts[cls] = ++n;
            t.objectId = $"{cls}_{n}";
            t.cls = cls;
        }
    }

    Mesh bakeMesh;
    readonly List<Vector3> localVerts = new List<Vector3>();

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy() { if (bakeMesh != null) Destroy(bakeMesh); }

    /// <summary>
    /// SkinnedMeshRenderer가 있으면 현재 포즈를 BakeMesh로 구워 모든 꼭짓점을 월드 좌표로 담는다
    /// (같은 오브젝트의 일반 MeshFilter 메시도 포함). 스킨 메시가 없으면 false — bounds 방식을 쓴다.
    /// </summary>
    public bool TryGetPoseVertices(List<Vector3> worldVerts)
    {
        worldVerts.Clear();
        var skinned = GetComponentsInChildren<SkinnedMeshRenderer>();
        if (skinned.Length == 0) return false;

        if (bakeMesh == null) bakeMesh = new Mesh();
        foreach (var smr in skinned)
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            smr.BakeMesh(bakeMesh, true);   // 스케일 포함, 위치·회전은 미적용
            var t = smr.transform;
            var m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            bakeMesh.GetVertices(localVerts);
            foreach (var v in localVerts) worldVerts.Add(m.MultiplyPoint3x4(v));
        }
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var m = mf.transform.localToWorldMatrix;
            mf.sharedMesh.GetVertices(localVerts);
            foreach (var v in localVerts) worldVerts.Add(m.MultiplyPoint3x4(v));
        }
        return worldVerts.Count > 0;
    }

    public bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }

    /// <summary>바닥에 닿는 점 (Unity 월드 좌표 그대로).</summary>
    public Vector3 GroundPoint(Bounds bounds)
    {
        if (pivotIsGround) return transform.position;
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }
}
