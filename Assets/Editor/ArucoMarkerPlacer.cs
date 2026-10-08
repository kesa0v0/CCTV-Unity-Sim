using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/ArChu의 ArUco 마커를 A4(210x297mm) 크기로 바닥 4장 + 맞은편 벽 2장 배치한다.
/// 메뉴: Tools/Place ArUco Markers (다시 실행하면 기존 "ArucoMarkers" 루트를 교체)
/// 방 좌표: X 0~10.58, Z 0~8.57, 원점 = 바닥 모서리. 카메라는 z=0쪽 모서리에서 +Z를 본다.
/// </summary>
public static class ArucoMarkerPlacer
{
    const string Dir = "Assets/ArChu/";
    const string Root = "ArucoMarkers";
    const float W = 0.21f, H = 0.297f;   // A4 (m)
    const float Lift = 0.003f;           // z-fighting 방지

    struct Spot { public int id; public Vector3 pos; public bool wall; public float yaw; public Spot(int id, Vector3 pos, bool wall, float yaw = 0f) { this.id = id; this.pos = pos; this.wall = wall; this.yaw = yaw; } }

    static readonly Spot[] Spots =
    {
        // 바닥 4장: 격자(4m × 4m, 방 가운데)와 카메라 사이에 연 모양으로. 격자 옆·뒤 바닥은 먼 카메라에서 10px 안팎이라 쓰지 않는다.
        // 0번 = rel 좌표 원점, 격자 C1에서 1m 앞 (A1 = rel (-2, 1)). 그림 위쪽이 +Z(먼 벽)를 향한다
        // 1·6번은 각자 가까운 카메라에서 크게(약 89x58px), 7번은 두 카메라에서 같은 크기로 보인다
        new Spot(0, new Vector3(5.29f, Lift, 1.285f), false),   // rel (0, 0)
        new Spot(1, new Vector3(6.79f, Lift, 0.285f), false),   // rel (1.5, -1)
        new Spot(6, new Vector3(3.79f, Lift, 0.285f), false),   // rel (-1.5, -1)
        new Spot(7, new Vector3(5.29f, Lift, 1.785f), false),   // rel (0, 0.5) — 격자 C1과 0.5m
        // 벽 2장 (보정용): 두 카메라에 모두 보이는 카메라 맞은편 벽(z=8.57), 높이를 다르게
        new Spot(2, new Vector3(4.29f, 0.8f, 8.57f - Lift), true),   // rel (-1, 7.285, 0.8)
        new Spot(3, new Vector3(6.29f, 1.4f, 8.57f - Lift), true),   // rel (1, 7.285, 1.4)
    };

    [MenuItem("Tools/Place ArUco Markers")]
    static void Place()
    {
        var old = GameObject.Find(Root);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "Place ArUco Markers");

        foreach (var s in Spots)
        {
            string path = $"{Dir}aruco_dict4x4_50_id_{s.id:00}_180mm_A4.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { Debug.LogError("마커 텍스처 없음: " + path); continue; }

            var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = tex };
            string matPath = $"{Dir}Marker_{s.id:00}.mat";
            AssetDatabase.CreateAsset(mat, matPath);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = $"aruco_{s.id:00}_{(s.wall ? "wall" : "floor")}";
            q.transform.SetParent(root.transform, false);
            q.transform.position = s.pos;
            // Quad 법선은 -Z. 바닥은 X축 +90도로 법선을 위로, 벽은 yaw로 방 안쪽을 향하게 함(z=8.57 벽: 0도, x=0 벽: -90도, x=10.58 벽: 90도)
            q.transform.rotation = s.wall ? Quaternion.Euler(0f, s.yaw, 0f) : Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(W, H, 1f);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    }
}
