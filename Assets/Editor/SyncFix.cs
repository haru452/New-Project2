using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.Udon;

// アップロードのエラー「Object Sync cannot share an object with a manually synchronized Udon Behaviour」を直す。
// 手動同期の Udon と VRCObjectSync が同じ物に付いていたら、VRCObjectSync を外す（敵機の頭脳 EnemyBrain は本体とは別なので要らない）。
// Backup_flight/run_syncfix.flag があれば再読み込み時に一度だけ実行。
[InitializeOnLoad]
public static class SyncFix
{
    const string Flag = "Backup_flight/run_syncfix.flag";
    static SyncFix() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }
    [MenuItem("Tools/同期のエラーを直す（ObjectSync と手動同期）")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("SyncFix " + System.DateTime.Now);
        int fixedN = 0, left = 0;
        foreach (var os in Object.FindObjectsOfType<VRCObjectSync>(true))
        {
            bool manual = false;
            foreach (var u in os.GetComponents<UdonBehaviour>()) if (u.SyncMethod == VRC.SDKBase.Networking.SyncType.Manual) manual = true;
            if (!manual) continue;
            string path = os.name; var p = os.transform.parent; while (p != null) { path = p.name + "/" + path; p = p.parent; }
            var rbOn = os.GetComponent<Rigidbody>();
            var ed = os.GetComponent<EnemyDrone>();
            bool bodyElsewhere = ed != null && ed.rb != null && ed.rb.gameObject != os.gameObject;
            if (os.name == "EnemyBrain" || bodyElsewhere || rbOn == null)
            {
                Undo.DestroyObjectImmediate(os);
                log.AppendLine("外した: " + path);
                fixedN++;
            }
            else { log.AppendLine("※ そのまま（本体と同じ物）: " + path); left++; }
        }
        log.AppendLine("ObjectSync を " + fixedN + " か所で外しました。残り " + left + " か所");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
        log.AppendLine("シーン保存: " + (EditorSceneManager.SaveOpenScenes() ? "OK" : "失敗"));
        log.AppendLine("DONE");
        File.WriteAllText("Backup_flight/syncfix_report.txt", log.ToString());
    }
}
