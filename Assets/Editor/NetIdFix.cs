using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// アップロードのエラー「Failed to assign network IDs, 2 errors」を直す。
// VRCWorld のネットワーク ID の表から、消した物（空になった行）と、同期の部品を外した 2 つの EnemyBrain の行を消す。
// 消した行は、次のビルドで新しい ID が自動で付く。Backup_flight/run_netidfix.flag があれば一度だけ実行。
[InitializeOnLoad]
public static class NetIdFix
{
    const string Flag = "Backup_flight/run_netidfix.flag";
    static NetIdFix() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }
    static string PathOf(Transform t) { string p = t.name; var q = t.parent; while (q != null) { p = q.name + "/" + p; q = q.parent; } return p; }
    [MenuItem("Tools/ネットワーク ID の表を直す")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("NetIdFix " + System.DateTime.Now);
        try
        {
            var desc = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>(true);
            if (desc == null) { log.AppendLine("ERROR: VRCWorld（SceneDescriptor）が無い"); return; }
            var so = new SerializedObject(desc);
            var arr = so.FindProperty("NetworkIDs");
            if (arr == null || !arr.isArray) { log.AppendLine("ERROR: NetworkIDs が見つからない"); return; }
            int before = arr.arraySize, removed = 0;
            var seenGo = new System.Collections.Generic.HashSet<int>();
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var el = arr.GetArrayElementAtIndex(i);
                var goP = el.FindPropertyRelative("gameObject");
                var idP = el.FindPropertyRelative("ID");
                var go = goP != null ? goP.objectReferenceValue as GameObject : null;
                string why = null;
                if (go == null) why = "物が無い（消した物）";
                else
                {
                    string p = PathOf(go.transform);
                    if (go.name == "EnemyBrain" && (p.Contains("EnemyFighter_3/EnemyFighter_2") || p.Contains("EnemyFighter_2/EnemyFighter_1"))) why = "同期の部品を外した物";
                    else if (!seenGo.Add(go.GetInstanceID())) why = "同じ物が 2 回";
                }
                if (why == null) continue;
                log.AppendLine("消した行: ID " + (idP != null ? idP.intValue.ToString() : "?") + "  " + (go != null ? PathOf(go.transform) : "(なし)") + "  … " + why);
                arr.DeleteArrayElementAtIndex(i);
                removed++;
            }
            so.ApplyModifiedProperties();
            log.AppendLine("ネットワーク ID の表: " + before + " 行 → " + (before - removed) + " 行（" + removed + " 行を消しました）");
            EditorSceneManager.MarkSceneDirty(desc.gameObject.scene);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(desc.gameObject.scene) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        finally { File.WriteAllText("Backup_flight/netidfix_report.txt", log.ToString()); }
    }
}
