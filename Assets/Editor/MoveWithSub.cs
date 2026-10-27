using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 伊402 を動かした分だけ、まわりに置いた物（機体の出てくる場所・カタパルト・R35 4 機・乗り降りの扉など）も動かす。
// 元の伊402 の位置 (290.65, -11.2, 297.2) のまわり 300m にある物が対象（海・雲・カメラは除く）。
// Backup_flight/run_movesub.flag があれば再読み込み時に一度だけ実行。
[InitializeOnLoad]
public static class MoveWithSub
{
    const string Flag = "Backup_flight/run_movesub.flag";
    static readonly Vector3 OldSub = new Vector3(290.6499f, -11.2f, 297.2f);
    static MoveWithSub() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }
    static bool Near(Vector3 p) { Vector3 d = p - OldSub; d.y = 0f; return d.magnitude < 300f; }

    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("MoveWithSub " + System.DateTime.Now);
        try
        {
            GameObject sub = GameObject.Find("伊402_完全版");
            if (sub == null) { log.AppendLine("ERROR: 伊402_完全版 が無い"); return; }
            Vector3 delta = sub.transform.position - OldSub;
            log.AppendLine("伊402 のずれ: " + delta.ToString("F2"));
            if (delta.magnitude < 1f) { log.AppendLine("ずれていないので何もしません"); return; }
            var cat = Object.FindObjectOfType<Catapult>(true);
            if (cat != null && !Near(cat.transform.position)) { log.AppendLine("カタパルトはもう元の場所に無い（動かし済み）ので何もしません"); return; }
            var skip = new System.Collections.Generic.HashSet<string> { "Ocean", "CloudLayer", "Camera" };
            var roots = new System.Collections.Generic.List<Transform>();
            foreach (var g in sub.scene.GetRootGameObjects()) roots.Add(g.transform);
            int moved = 0;
            foreach (var t in roots)
            {
                if (t == sub.transform || skip.Contains(t.name)) continue;
                if (t.name == "transport")
                {
                    // 乗り降りの扉: 伊402 のそばにある物だけ動かす（艦橋・博物館の側はそのまま）
                    foreach (Transform ch in t)
                    {
                        if (!Near(ch.position)) { log.AppendLine("  そのまま: transport/" + ch.name + " " + ch.position.ToString("F0")); continue; }
                        Undo.RecordObject(ch, "Move with sub");
                        ch.position += delta;
                        log.AppendLine("動かした: transport/" + ch.name + " → " + ch.position.ToString("F0"));
                        moved++;
                    }
                    continue;
                }
                if (!Near(t.position)) continue;
                Undo.RecordObject(t, "Move with sub");
                t.position += delta;
                log.AppendLine("動かした: " + t.name + (t.gameObject.activeInHierarchy ? "" : " [off]") + " → " + t.position.ToString("F0"));
                moved++;
            }
            log.AppendLine("合計 " + moved + " 個");
            EditorSceneManager.MarkSceneDirty(sub.scene);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveOpenScenes() ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        finally { File.WriteAllText("Backup_flight/movesub_report.txt", log.ToString()); }
        // 敵が出てこない範囲（出撃地点のまわり）などを新しい場所で設定し直す
        if (log.ToString().Contains("DONE")) EditorApplication.delayCall += () => FlightSetup.Run();
    }
}
