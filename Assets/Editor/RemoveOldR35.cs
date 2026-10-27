using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 元の R35（Dropship）の部品を消す。今は R35 = Omega の機体。
// Omega の中に隠れて残っている Dropship_* / *R35* の部品を消す。消す前に、それを指している設定を書き出す。
// Backup_flight/run_remove_r35.flag があれば再読み込み時に一度だけ実行。
[InitializeOnLoad]
public static class RemoveOldR35
{
    const string Flag = "Backup_flight/run_remove_r35.flag";
    static RemoveOldR35() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }
    static bool Match(string n) { return n.StartsWith("Dropship"); }

    [MenuItem("Tools/元の R35 の部品を消す")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("RemoveOldR35 " + System.DateTime.Now);
        try
        {
            var del = new System.Collections.Generic.List<GameObject>();
            var set = new System.Collections.Generic.HashSet<Object>();
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (!Match(t.name)) continue;
                if (t.parent != null && Match(t.parent.name)) continue;
                del.Add(t.gameObject);
                foreach (var d in t.GetComponentsInChildren<Transform>(true)) { set.Add(d.gameObject); foreach (var c in d.GetComponents<Component>()) if (c != null) set.Add(c); }
            }
            // それを指している設定
            int refs = 0;
            foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null || set.Contains(mb)) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (!set.Contains(it.objectReferenceValue)) continue;
                    log.AppendLine("参照: " + mb.gameObject.name + " / " + mb.GetType().Name + "." + it.propertyPath + " → " + it.objectReferenceValue.name);
                    refs++;
                }
            }
            foreach (var g in del)
            {
                string path = g.name; var p = g.transform.parent; while (p != null) { path = p.name + "/" + path; p = p.parent; }
                log.AppendLine("消す: " + path + (g.activeInHierarchy ? "" : " [off]"));
                Undo.DestroyObjectImmediate(g);
            }
            log.AppendLine("消した: " + del.Count + " 個（参照されていた所 " + refs + " か所は空になります）");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveOpenScenes() ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        finally { File.WriteAllText("Backup_flight/remove_r35_report.txt", log.ToString()); }
    }
}
