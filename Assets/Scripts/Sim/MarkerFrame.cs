using UnityEngine;

/// <summary>
/// 바닥 ArUco 마커를 원점으로 하는 상대 좌표계(rel).
/// 원점 = 마커 중심을 바닥(y=0)에 내린 점, x = 마커 그림의 오른쪽, y = 그림의 위쪽, z = 바닥에서 위로 (m).
/// Unity 월드(왼손 좌표계, Y 위)와 달리 오른손 좌표계이며 OpenCV 마커 좌표계와 같은 방향이다.
/// </summary>
public readonly struct MarkerFrame
{
    public readonly Vector3 origin, x, y, z;   // Unity 월드 기준

    /// <summary>바닥에 눕힌 Quad 마커(ArucoMarkerPlacer): 그림 오른쪽 = transform.right, 그림 위쪽 = transform.up.</summary>
    public MarkerFrame(Transform marker)
    {
        origin = new Vector3(marker.position.x, 0f, marker.position.z);
        z = Vector3.up;
        x = Vector3.ProjectOnPlane(marker.right, z).normalized;
        y = Vector3.ProjectOnPlane(marker.up, z).normalized;
    }

    public Vector3 ToRel(Vector3 world)
    {
        Vector3 d = world - origin;
        return new Vector3(Vector3.Dot(d, x), Vector3.Dot(d, y), Vector3.Dot(d, z));
    }

    /// <summary>월드 방향 벡터를 rel 방향으로 (원점 이동 없음).</summary>
    public Vector3 DirToRel(Vector3 dir) => new Vector3(Vector3.Dot(dir, x), Vector3.Dot(dir, y), Vector3.Dot(dir, z));

    /// <summary>rel 바닥 좌표 (x, y) → 월드 좌표 (높이 0).</summary>
    public Vector3 ToWorld(Vector2 rel) => origin + x * rel.x + y * rel.y;
}
