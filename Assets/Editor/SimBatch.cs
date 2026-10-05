using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 커맨드라인에서 시나리오 하나를 녹화하고 에디터를 종료한다 (씬은 저장하지 않음).
/// Unity.exe -batchmode -projectPath unity-sim -executeMethod SimBatch.Run
///           -simScenario S1 -simSession unity-classroom-03-s1 -simFrames 300
/// -nographics를 붙이면 렌더링이 안 되므로 붙이지 않는다.
/// </summary>
[InitializeOnLoad]
public static class SimBatch
{
    const string ScenePath = "Assets/Scenes/SampleScene 1.unity";
    const string ActiveKey = "SimBatch.active";

    // 플레이 모드 진입/종료 때 도메인이 다시 로드되므로 상태는 SessionState에 둔다
    static SimBatch()
    {
        EditorApplication.update += () =>
        {
            if (SessionState.GetBool(ActiveKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(ActiveKey, false);
                EditorApplication.Exit(0);
            }
        };
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var rec = UnityEngine.Object.FindFirstObjectByType<SimRecorder>();
        if (rec == null)
        {
            Debug.LogError("SimBatch: 씬에 SimRecorder가 없습니다.");
            EditorApplication.Exit(1);
            return;
        }

        rec.scenario = (SimScenario)Enum.Parse(typeof(SimScenario), Arg("-simScenario", rec.scenario.ToString()));
        rec.sessionName = Arg("-simSession", rec.sessionName);
        rec.frameCount = int.Parse(Arg("-simFrames", rec.frameCount.ToString()));
        rec.quitWhenDone = true;
        Debug.Log($"SimBatch: {rec.scenario} → {rec.sessionName} ({rec.frameCount}프레임)");

        SessionState.SetBool(ActiveKey, true);
        EditorApplication.EnterPlaymode();
    }

    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}
