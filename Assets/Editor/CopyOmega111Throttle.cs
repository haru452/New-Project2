using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Omega_111 で手で直したスロットル（台・溝・レバー）の位置・向き・大きさを、ほかの機体に写す
//  ・写すのはスロットルの部品だけ（操縦桿・計器・座席は変えない）
//  ・Omega_111 は見本なので変えない
//  ・同じ名前の部品どうしで写す（何も消さない・作らない → レバーが動くつながりはそのまま）
// メニュー「Tools/Omega_111 のスロットルの位置をほかの機体に写す」
//  Backup_flight/run_copy_throttle.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class CopyOmega111Throttle
{
    const string Source = "Omega_111";
    const string FlagPath = "Backup_flight/run_copy_throttle.flag";
    static readonly string[] Parts = { "ThrottlePivot", "ThrottleBox", "ThrottleSlot" };

    static CopyOmega111Throttle() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/Omega_111 のスロットルの位置をほかの機体に写す")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("CopyOmega111Throttle " + System.DateTime.Now);
        try { Copy(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/copy_throttle_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Copy(StringBuilder log)
    {
        Transform srcLv = null;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && c.car.name == Source && c.cockpitRoot != null) { srcLv = c.cockpitRoot.transform.Find("Levers"); break; }
        if (srcLv == null) { log.AppendLine("ERROR: " + Source + " のレバーが見つからない"); return; }
        foreach (var nm in Parts)
        {
            Transform s = srcLv.Find(nm);
            log.AppendLine("見本 " + Source + " " + nm + ": " + (s != null ? V(s.localPosition) : "無い"));
        }

        int planes = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || c.cockpitRoot == null || c.car.name == Source) continue;
            Transform lv = c.cockpitRoot.transform.Find("Levers");
            if (lv == null || lv == srcLv) continue;
            int moved = 0, missing = 0;
            foreach (var nm in Parts)
            {
                Transform s = srcLv.Find(nm), t = lv.Find(nm);
                if (s == null) continue;
                if (t == null) { missing++; continue; }
                CopyTree(s, t, ref moved, ref missing);
            }
            planes++;
            log.AppendLine(c.car.name + ": " + moved + " 個の部品を合わせた" + (missing > 0 ? "（見つからない部品 " + missing + " 個）" : ""));
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine(Source + " のスロットルの位置を、ほかの " + planes + " 機に写しました（" + Source + " は変えていない）。シーンを保存してください");
    }

    // 位置・向き・大きさを写し、同じ名前の子にもくり返す
    static void CopyTree(Transform s, Transform t, ref int moved, ref int missing)
    {
        if (t.localPosition != s.localPosition || t.localRotation != s.localRotation || t.localScale != s.localScale)
        {
            Undo.RecordObject(t, "Copy Omega_111 throttle");
            t.localPosition = s.localPosition;
            t.localRotation = s.localRotation;
            t.localScale = s.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            moved++;
        }
        for (int i = 0; i < s.childCount; i++)
        {
            Transform sc = s.GetChild(i), tc = t.Find(sc.name);
            if (tc == null) { missing++; continue; }
            CopyTree(sc, tc, ref moved, ref missing);
        }
    }

    static string V(Vector3 v) { return "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")"; }
}
