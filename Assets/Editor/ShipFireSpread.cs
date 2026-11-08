using System.Collections.Generic;
using System.IO;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 沈む船の炎を、前と後ろの 2 か所から 5 か所に増やす（船の前から後ろまで並べる）
//  ・増やす炎は、今ある炎をそのまま写して作る（大きさ・色などは同じ）
//  ・並べる場所: 今の 2 個（前・後ろ）はそのまま。真ん中・もっと前・もっと後ろに足す
//  ・何回実行しても、5 個より多くはならない
// メニュー「Tools/船の炎を 5 か所に増やす」
//  Backup_flight/run_shipfire5.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class ShipFireSpread
{
    public const int FireCount = 5;
    const string FlagPath = "Backup_flight/run_shipfire5.flag";

    // i 番目の炎の位置: 船の真ん中 + 前後の向き × この数（0 番と 1 番は今までの前・後ろ）
    static readonly float[] Factors = { 1f, -1f, 0f, 2f, -2f };
    public static float Factor(int i) { return i < Factors.Length ? Factors[i] : (i % 2 == 0 ? 1f : -1f); }

    static ShipFireSpread() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/船の炎を 5 か所に増やす")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipFireSpread " + System.DateTime.Now);
        int ok = 0;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            try { if (Spread(ws, log)) ok++; }
            catch (System.Exception e) { log.AppendLine(ws.name + ": 失敗 " + e.Message); }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("炎を " + FireCount + " か所にした船: " + ok + " 隻。シーンを保存してください");
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/ship_fire5_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static bool Spread(WarShip ws, StringBuilder log)
    {
        var list = new List<ParticleSystem>();
        if (ws.dmgFire != null) foreach (var p in ws.dmgFire) if (p != null) list.Add(p);
        if (list.Count < 2) { log.AppendLine(ws.name + ": 炎が " + list.Count + " 個しか無い（そのまま）"); return false; }
        if (ws.model == null) { log.AppendLine(ws.name + ": 見た目（model）が無い（そのまま）"); return false; }
        Transform mt = ws.model.transform;

        // 今の前・後ろの炎から、船の真ん中と前後の向きを知る（model から見た位置）
        Vector3 a = mt.InverseTransformPoint(list[0].transform.position);
        Vector3 b = mt.InverseTransformPoint(list[1].transform.position);
        Vector3 center = (a + b) * 0.5f, along = (a - b) * 0.5f;

        int made = 0;
        while (list.Count < FireCount)
        {
            var go = (GameObject)Object.Instantiate(list[0].gameObject, list[0].transform.parent);
            go.name = "Fire";
            Undo.RegisterCreatedObjectUndo(go, "Ship fire spread");
            list.Add(go.GetComponent<ParticleSystem>());
            made++;
        }
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i].transform;
            Undo.RecordObject(t, "Ship fire spread");
            t.position = mt.TransformPoint(center + along * Factor(i));
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        Undo.RecordObject(ws, "Ship fire spread");
        ws.dmgFire = list.ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(ws);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ws);
        log.AppendLine(ws.name + ": 炎 " + list.Count + " か所（" + made + " 個足した）");
        return true;
    }
}
