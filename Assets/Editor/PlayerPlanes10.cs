using System.Collections.Generic;
using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// プレイヤーの機体（格納庫のプール）を 10 機にする。足りない分は Omega_111（見本の機体）を複製する
//  ・機体の一覧を持っている部品（敵の戦闘機・軍艦・字幕・音量・勝利の部屋・レーダーなど）の配列に、新しい機体の同じ部品を足す
//    （2 機以上の機体の部品が入っている配列だけ = 「全機の一覧」。1 機だけの部品の配列はさわらない）
//  ・海図台の機体の印（ChartMapLive.markers）は、Omega_111 の印を複製して足す
//  Tools/プレイヤーの機体を 10 機にする、または Backup_flight/run_planes10.flag。結果は Backup_flight/planes10_report.txt
[InitializeOnLoad]
public static class PlayerPlanes10
{
    const string FlagPath = "Backup_flight/run_planes10.flag";
    const int Want = 10;
    const string Template = "Omega_111";
    static readonly string[] Originals = { "Omega_111", "Omega_3", "Omega_4", "Omega_1" };   // 10 機にする前からあった機体

    static PlayerPlanes10() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/プレイヤーの機体を 10 機にする")]
    public static void Run()
    {
        var log = new StringBuilder();
        if (EditorApplication.isPlaying) { File.WriteAllText(FlagPath, "run"); EditorApplication.delayCall += TryFlag; return; }
        try { Build(log); }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/planes10_report.txt", log.ToString());
    }

    static void Build(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.dronePool == null) { log.AppendLine("ERROR 格納庫のプールが無い"); return; }
        var pool = putOut.dronePool;
        var originals = new List<Transform>();
        foreach (var g in pool.Pool) if (g != null) originals.Add(g.transform);
        Transform tmpl = originals.Find(t => t.name == Template);
        if (tmpl == null) { log.AppendLine("ERROR プールに " + Template + " が無い"); return; }
        // もう複製してある時（やり直し）: 元からある 4 機以外を「複製した機体」として、一覧だけ直す（足すのは無い分だけ）
        var copies = new List<Transform>();
        if (originals.Count >= Want)
        {
            foreach (var t in new List<Transform>(originals)) if (System.Array.IndexOf(Originals, t.name) < 0) { copies.Add(t); originals.Remove(t); }
            log.AppendLine("もう " + (originals.Count + copies.Count) + " 機あります。一覧だけ直します（複製した機体: " + copies.Count + " 機）");
        }

        // 1) 複製
        var names = new HashSet<string>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name.StartsWith("Omega_")) names.Add(t.name);
        int n = 2;
        while (originals.Count + copies.Count < Want)
        {
            while (names.Contains("Omega_" + n)) n++;
            var go = Object.Instantiate(tmpl.gameObject, tmpl.parent);
            Undo.RegisterCreatedObjectUndo(go, "Planes10");
            go.name = "Omega_" + n; names.Add(go.name);
            go.transform.SetPositionAndRotation(tmpl.position, tmpl.rotation);
            go.transform.localScale = tmpl.localScale;
            go.SetActive(false);   // プールの機体はボタンで出すまで出さない
            copies.Add(go.transform);
            log.AppendLine("複製: " + go.name + "（" + Template + " から）");
        }

        // 2) 機体の外と、元の機体の中の「全機の一覧」に、新しい機体の部品を足す
        int changed = 0;
        foreach (var b in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (b == null || Owner(b.transform, copies) >= 0) continue;
            bool any = false;
            foreach (var fi in ArrayFields(b))
            {
                var arr = fi.GetValue(b) as System.Array;
                if (arr == null || arr.Length == 0 || Owners(arr, originals) < 2) continue;
                var list = new List<Object>();
                foreach (var o in arr) list.Add(o as Object);
                // 見本の機体の部品を、新しい機体の同じ部品に写して足す（見本の部品が無い配列は、最初に見つかった機体を見本にする）
                Transform src = tmpl;
                if (!Has(list, tmpl)) foreach (var o in list) { var t = T(o); int k = t != null ? Owner(t, originals) : -1; if (k >= 0) { src = originals[k]; break; } }
                int before = list.Count;
                var srcItems = new List<Object>();
                foreach (var o in list) { var t = T(o); if (t != null && Under(t, src) && !srcItems.Contains(o)) srcItems.Add(o); }
                foreach (var c in copies)
                    foreach (var o in srcItems)
                    {
                        var m = Map(o, src, c);
                        if (m == null) { log.AppendLine("注意: " + Path(b.transform) + "." + fi.Name + " の " + T(o).name + " が " + c.name + " に無い"); continue; }
                        if (!list.Contains(m)) list.Add(m);
                    }
                if (list.Count == before) continue;
                if (!any) { Undo.RecordObject(b, "Planes10"); any = true; }
                fi.SetValue(b, ToArray(list, fi.FieldType.GetElementType()));
                log.AppendLine(Path(b.transform) + " / " + b.GetType().Name + "." + fi.Name + ": " + before + " → " + list.Count);
            }
            if (any) { Commit(b); changed++; }
        }

        // 3) 海図台の機体の印
        foreach (var live in Object.FindObjectsOfType<ChartMapLive>(true)) ChartMarkers(live, tmpl, copies, log);

        // 4) 新しい機体の中の「全機の一覧」は、見本の機体のものと同じにする（複製した時は、見本の自分が新しい機体の自分に置き換わっているため）
        foreach (var c in copies)
            foreach (var b in c.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var tb = Counterpart(b, c, tmpl) as MonoBehaviour;
                if (tb == null) continue;
                bool any = false;
                foreach (var fi in ArrayFields(b))
                {
                    var tarr = fi.GetValue(tb) as System.Array;
                    if (tarr == null || Owners(tarr, Join(originals, copies)) < 2) continue;
                    if (!any) { Undo.RecordObject(b, "Planes10"); any = true; }
                    fi.SetValue(b, tarr.Clone());
                }
                if (any) Commit(b);
            }

        // 5) プール
        var all = new List<GameObject>();
        foreach (var g in pool.Pool) if (g != null) all.Add(g);
        foreach (var c in copies) if (!all.Contains(c.gameObject)) all.Add(c.gameObject);
        Undo.RecordObject(pool, "Planes10");
        pool.Pool = all.ToArray();
        EditorUtility.SetDirty(pool);
        Undo.RecordObject(putOut, "Planes10");
        putOut.NumberOFDrone = all.Count;
        UdonSharpEditorUtility.CopyProxyToUdon(putOut);
        log.AppendLine("格納庫のプール: " + all.Count + " 機");
        log.AppendLine("一覧を直した部品: " + changed + " 個");

        var scene = putOut.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(scene) ? "OK" : "失敗"));
        log.AppendLine("DONE");
    }

    static void ChartMarkers(ChartMapLive live, Transform tmpl, List<Transform> copies, StringBuilder log)
    {
        if (live.planes == null || live.markers == null) return;
        controller tc = tmpl.GetComponentInChildren<controller>(true);
        int ti = System.Array.IndexOf(live.planes, tc);
        if (ti < 0 || ti >= live.markers.Length || live.markers[ti] == null) { log.AppendLine("注意: 海図台の印: " + Template + " の印が無い"); return; }
        var mk = live.markers[ti];
        Undo.RecordObject(live, "Planes10");
        var planes = new List<controller>(live.planes);
        var markers = new List<Transform>(live.markers);
        var mr = live.markerR != null ? new List<Renderer>(live.markerR) : null;
        foreach (var c in copies)
        {
            var cc = c.GetComponentInChildren<controller>(true);
            if (cc != null && !planes.Contains(cc)) planes.Add(cc);
        }
        // 機体の数まで印を足す（印の i 番目 = planes の i 番目）
        for (int i = markers.Count; i < planes.Count; i++)
        {
            var dup = Object.Instantiate(mk.gameObject, mk.parent);
            Undo.RegisterCreatedObjectUndo(dup, "Planes10");
            dup.name = "Plane_" + (planes[i] != null && planes[i].car != null ? planes[i].car.name : i.ToString());
            markers.Add(dup.transform);
            if (mr != null) mr.Add(dup.GetComponentInChildren<Renderer>(true));
        }
        live.planes = planes.ToArray(); live.markers = markers.ToArray();
        if (mr != null) live.markerR = mr.ToArray();
        Commit(live);
        log.AppendLine("海図台の機体の印: " + markers.Count + " 個");
    }

    // ---- 小さな道具 ----
    static IEnumerable<System.Reflection.FieldInfo> ArrayFields(MonoBehaviour b)
    {
        foreach (var fi in b.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (fi.FieldType.IsArray && typeof(Object).IsAssignableFrom(fi.FieldType.GetElementType())) yield return fi;
    }
    static void Commit(MonoBehaviour b)
    {
        if (b is UdonSharpBehaviour u) UdonSharpEditorUtility.CopyProxyToUdon(u);
        EditorUtility.SetDirty(b);
        PrefabUtility.RecordPrefabInstancePropertyModifications(b);
    }
    static System.Array ToArray(List<Object> list, System.Type et)
    {
        var a = System.Array.CreateInstance(et, list.Count);
        for (int i = 0; i < list.Count; i++) a.SetValue(list[i], i);
        return a;
    }
    static List<Transform> Join(List<Transform> a, List<Transform> b) { var r = new List<Transform>(a); r.AddRange(b); return r; }
    static Transform T(Object o) { if (o == null) return null; if (o is GameObject g) return g.transform; if (o is Component c) return c.transform; return null; }
    static int Owner(Transform t, List<Transform> roots) { for (var p = t; p != null; p = p.parent) { int i = roots.IndexOf(p); if (i >= 0) return i; } return -1; }
    static bool Under(Transform t, Transform root) { for (var p = t; p != null; p = p.parent) if (p == root) return true; return false; }
    static bool Has(List<Object> list, Transform root) { foreach (var o in list) { var t = T(o); if (t != null && Under(t, root)) return true; } return false; }
    static int Owners(System.Array arr, List<Transform> roots)
    {
        var s = new HashSet<int>();
        foreach (var o in arr) { var t = T(o as Object); if (t != null) { int k = Owner(t, roots); if (k >= 0) s.Add(k); } }
        return s.Count;
    }
    static string Rel(Transform t, Transform root)
    {
        var parts = new List<int>();
        for (var p = t; p != root; p = p.parent) parts.Insert(0, p.GetSiblingIndex());
        return string.Join("/", parts);
    }
    static Transform Follow(Transform root, string rel)
    {
        if (rel == "") return root;
        var t = root;
        foreach (var s in rel.Split('/')) { int i = int.Parse(s); if (i >= t.childCount) return null; t = t.GetChild(i); }
        return t;
    }
    // src の中の部品 o と同じ場所・同じ種類の部品を、dst の中から探す
    static Object Map(Object o, Transform src, Transform dst)
    {
        var t = T(o);
        var d = Follow(dst, Rel(t, src));
        if (d == null || (d != dst && d.name != t.name)) return null;   // 機体そのもの（一番上）は名前が違ってよい
        if (o is GameObject) return d.gameObject;
        var type = o.GetType();
        var sc = t.GetComponents(type); var dc = d.GetComponents(type);
        int i = System.Array.IndexOf(sc, o);
        return (i >= 0 && i < dc.Length) ? dc[i] : null;
    }
    static Object Counterpart(Component b, Transform copy, Transform tmpl) { return Map(b, copy, tmpl); }
    static string Path(Transform t) { string s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }
}
