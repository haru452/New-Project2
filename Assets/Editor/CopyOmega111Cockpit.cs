using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Omega_111 で手で動かした操縦席（Cockpit の中全部: RealCockpit・Levers・HUD の部品）の位置・向き・大きさを、ほかの Omega に写す
//  ・同じ名前の部品どうしで、位置・向き・大きさだけを写す（何も消さない・作らない → 計器の針などのつながりはそのまま）
//  ・Omega_111 は見本なので、絶対に変えない
//  ・座席（VRCChair3）は写さない
// メニュー「Tools/Omega_111 の操縦席の位置をほかの Omega に写す」
//  Backup_flight/run_copy_cockpit.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class CopyOmega111Cockpit
{
    const string Source = "Omega_111";
    const string FlagPath = "Backup_flight/run_copy_cockpit.flag";

    static CopyOmega111Cockpit() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/Omega_111 の操縦席の位置をほかの Omega に写す")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("CopyOmega111Cockpit " + System.DateTime.Now);
        try { Copy(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/copy_omega111_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Copy(StringBuilder log)
    {
        controller src = null;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && c.car.name == Source && c.cockpitRoot != null) { src = c; break; }
        if (src == null) { log.AppendLine("ERROR: " + Source + " の操縦席が見つからない"); return; }
        Transform sr = src.cockpitRoot.transform;

        int planes = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c == src || c.car == null || c.cockpitRoot == null) continue;
            if (!c.car.name.StartsWith("Omega")) continue;
            if (c.car.name == Source) continue;   // 念のため（見本は変えない）
            Transform tr = c.cockpitRoot.transform;
            int moved = 0, missing = 0;
            // Cockpit の中を全部（RealCockpit・Levers と、HUD の部品）。Cockpit そのものの位置も
            CopyTree(sr, tr, ref moved, ref missing);
            planes++;
            log.AppendLine(c.car.name + ": " + moved + " 個の部品の位置を合わせた" + (missing > 0 ? "（" + Source + " にしか無い部品 " + missing + " 個）" : ""));
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine(Source + " の操縦席の位置を、ほかの Omega " + planes + " 機に写しました（" + Source + " は変えていない）。シーンを保存してください");
    }

    // 子どもは「同じ名前の何番目か」で対応させる（Toggle など同じ名前の部品が多いため）
    static void CopyTree(Transform s, Transform t, ref int moved, ref int missing)
    {
        if (SetPose(t, s)) moved++;
        var seen = new Dictionary<string, int>();
        for (int i = 0; i < s.childCount; i++)
        {
            Transform sc = s.GetChild(i);
            int k; seen.TryGetValue(sc.name, out k); seen[sc.name] = k + 1;
            Transform tc = NthChild(t, sc.name, k);
            if (tc == null) { missing++; continue; }
            CopyTree(sc, tc, ref moved, ref missing);
        }
    }

    static Transform NthChild(Transform p, string name, int n)
    {
        for (int i = 0; i < p.childCount; i++)
        {
            Transform c = p.GetChild(i);
            if (c.name != name) continue;
            if (n == 0) return c;
            n--;
        }
        return null;
    }

    static bool SetPose(Transform t, Transform s)
    {
        if (t.localPosition == s.localPosition && t.localRotation == s.localRotation && t.localScale == s.localScale) return false;
        Undo.RecordObject(t, "Copy Omega_111 cockpit");
        t.localPosition = s.localPosition; t.localRotation = s.localRotation; t.localScale = s.localScale;
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        return true;
    }
}
