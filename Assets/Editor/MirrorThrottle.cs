using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// スロットル（エンジンの強さのレバー）を、操縦桿（WASD で動くレバー）と左右対称の位置に置く
//  ・操縦席の真ん中の線（Levers の X = 0）をはさんで、操縦桿の反対側へ
//  ・高さは、スロットルの台の底と操縦桿の台の底をそろえる
//  ・操縦桿は動かさない。スロットルの部品（台・溝・レバー）はまとめて同じだけ動かす（形は変えない）
// メニュー「Tools/スロットルを操縦桿と左右対称に置く」
public static class MirrorThrottle
{
    static readonly string[] ThrottleParts = { "ThrottlePivot", "ThrottleBox", "ThrottleSlot" };

    [MenuItem("Tools/スロットルを操縦桿と左右対称に置く")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("MirrorThrottle " + System.DateTime.Now);
        try { Mirror(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/mirror_throttle_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Mirror(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || c.cockpitRoot == null) continue;
            Transform lv = c.cockpitRoot.transform.Find("Levers");
            if (lv == null) continue;
            Transform stick = lv.Find("StickPivot"), plate = lv.Find("StickPlate");
            Transform tp = lv.Find("ThrottlePivot"), box = lv.Find("ThrottleBox");
            if (stick == null || tp == null) { log.AppendLine(c.car.name + ": 操縦桿かスロットルが見つからない（そのまま）"); continue; }

            // 目標: 左右（X）は反対、前後（Z）は同じ
            Vector3 s = stick.localPosition;
            Vector3 target = new Vector3(-s.x, tp.localPosition.y, s.z);
            // 高さ: 台の底をそろえる（台が無ければ、レバーの付け根の高さの差を作った時と同じ 0.035 にする）
            if (plate != null && box != null)
            {
                float plateBottom = plate.localPosition.y - plate.localScale.y * 0.5f;
                float boxBottom = box.localPosition.y - box.localScale.y * 0.5f;
                target.y = tp.localPosition.y + (plateBottom - boxBottom);
            }
            else target.y = s.y + 0.035f;

            Vector3 d = target - tp.localPosition;
            log.AppendLine(c.car.name + ": 操縦桿 " + V(s) + " / スロットル " + V(tp.localPosition) + " → " + V(target) + "（" + V(d) + " 動かす）");
            if (d.sqrMagnitude < 1e-8f) { n++; continue; }
            foreach (var nm in ThrottleParts)
            {
                Transform t = lv.Find(nm);
                if (t == null) continue;
                Undo.RecordObject(t, "Mirror throttle");
                t.localPosition += d;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
            n++;
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine(n + " 機のスロットルを、操縦桿と左右対称に置きました。シーンを保存してください");
    }

    static string V(Vector3 v) { return "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")"; }
}
