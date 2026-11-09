using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

// 空母の艦隊以外の戦艦を、作戦範囲（AreaBoundary の四角）の中だけで走らせる
//  ・ランダムにコースを作る船: keepInArea をオンにして、範囲の中心と大きさを入れる（WarShip_4.cs の InArea）
//  ・コースが決まっている船（roaming でない）: 範囲の外を通るかだけログに出す（直さない）
// メニュー「Tools/戦艦を作戦範囲の中だけで走らせる」。ログは Backup_flight/ship_area_log.txt
//  Backup_flight/run_ship_area.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class ShipAreaSetup
{
    const string FlagPath = "Backup_flight/run_ship_area.flag";

    static ShipAreaSetup() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/戦艦を作戦範囲の中だけで走らせる")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipAreaSetup " + System.DateTime.Now);
        try
        {
            var ab = Object.FindObjectOfType<AreaBoundary>(true);
            if (ab == null) log.AppendLine("ERROR: AreaBoundary が無い（先に「Tools/作戦範囲と広い海を設定する」）");
            else Apply(ab, log);
        }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/ship_area_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    public static void Apply(AreaBoundary ab, StringBuilder log)
    {
        float half = Mathf.Min(ab.halfSize.x, ab.halfSize.y);
        log.AppendLine("作戦範囲: 中心 (" + ab.center.x.ToString("F0") + ", " + ab.center.y.ToString("F0") + ") ±" + half.ToString("F0") + "m");
        int set = 0, fixedOut = 0;
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
        {
            if (w.carrierApproach) continue;   // 空母は外から入ってくる
            Undo.RecordObject(w, "Ship area");
            w.keepInArea = true;
            w.opCenter = ab.center;
            w.opHalf = half;
            w.roamRadius = half * 1.3f;   // うろつく範囲も作戦範囲いっぱいに（コースは範囲の中に寄せる）
            UdonSharpEditorUtility.CopyProxyToUdon(w);
            PrefabUtility.RecordPrefabInstancePropertyModifications(w);
            set++;
            // コースが決まっている船: 範囲の外を通るか
            if (!w.roaming && w.leader == null && !w.useFormation && w.path != null)
            {
                float worst = 0f;
                foreach (var q in w.path)
                    worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(q.x - ab.center.x), Mathf.Abs(q.z - ab.center.y)) - half);
                if (worst > 0f) { fixedOut++; log.AppendLine("注意: " + w.name + " は決まったコースが範囲の外を " + worst.ToString("F0") + "m 通る"); }
            }
            Vector3 p = w.transform.position;
            float outNow = Mathf.Max(Mathf.Abs(p.x - ab.center.x), Mathf.Abs(p.z - ab.center.y)) - half;
            if (outNow > 0f && !w.roaming) log.AppendLine("注意: " + w.name + " は今、範囲の外 " + outNow.ToString("F0") + "m に置いてある");
        }
        log.AppendLine("範囲の中だけを走る設定にした船: " + set + " 隻（空母の艦隊は除く）");
        log.AppendLine("決まったコースが範囲の外を通る船: " + fixedOut + " 隻");
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("シーンを保存してください");
    }
}
