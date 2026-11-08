using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

// 全部の機体の燃料を「満タンから全開で Minutes 分」にする（controller.fullThrottleMinutes）
// メニュー「Tools/燃料の量を設定する」。ログは Backup_flight/fuel_minutes_log.txt
//  Backup_flight/run_fuel_minutes.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class FuelMinutesSetup
{
    const float Minutes = 7f;
    const string FlagPath = "Backup_flight/run_fuel_minutes.flag";

    static FuelMinutesSetup() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/燃料の量を設定する")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("FuelMinutesSetup " + System.DateTime.Now);
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.fullThrottleMinutes == Minutes) continue;
            Undo.RecordObject(c, "Fuel minutes");
            c.fullThrottleMinutes = Minutes;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("燃料: 全開で " + Minutes + " 分にした機体 " + n + " 機。シーンを保存してください");
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/fuel_minutes_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }
}
