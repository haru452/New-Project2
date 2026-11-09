using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// S2・S3 の船を減らして、S4・S5・S7 を増やす（船の見た目だけを入れ替える）
//  ・空母の艦隊の船・空母・貨物船は変えない
//  ・S2・S3 はそれぞれ名前順で先頭の Keep 隻を残し、残りを S4 → S5 → S7 の順に入れ替える
//  ・前にこのツールで S8（小さい船）にした船も、S4・S5・S7 に入れ替える（もとからある S8 は変えない）
//  ・船の外側（WarShip_x: 耐久値・大砲・航路・護衛）はそのまま。見た目の中に後から足した物（煙・炎・航跡・サーチライトなど）は新しい見た目へ移す
//  ・新しい見た目は、船底（いちばん下）と真ん中を前の見た目にそろえて置く
//  ・最後に「船の煙と炎の位置を直す」と「S2・S3 の護衛艦を設定する」もやり直す
// メニュー「Tools/S2・S3 の船を減らして S4・S5・S7 にする」
//  Backup_flight/run_swapships.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class ShipSwapModels
{
    const int Keep = 2;   // S2・S3 をそれぞれ何隻残すか
    const string Dir = "Assets/Military vehicles (Sea)/Prefab/";
    static readonly string[] NewTypes = { "S4", "S5", "S7" };   // S8 は小さい（長さ 39m）ので使わない
    const string FlagPath = "Backup_flight/run_swapships.flag";

    static ShipSwapModels() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/S2・S3 の船を減らして S4・S5・S7 にする")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipSwapModels " + System.DateTime.Now);
        Undo.SetCurrentGroupName("Swap ship models");
        int group = Undo.GetCurrentGroup();
        try { Swap(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Undo.CollapseUndoOperations(group);
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/swap_ships_log.txt", log.ToString());
        Debug.Log(log.ToString());
        // 煙・炎の位置と、S2・S3 の設定をやり直す
        ShipFxFix.Run();
        ShipS23Setup.Run();
    }

    public static string TypeOf(GameObject model)
    {
        string p = model != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model) : null;
        if (string.IsNullOrEmpty(p) || !p.StartsWith(Dir)) return null;
        return Path.GetFileNameWithoutExtension(p);
    }

    // 前にこのツールで S8 にした船（もとからある S8 は入れない）
    static readonly string[] MadeS8 = { "WarShip_16 (5)", "WarShip_16 (9)", "WarShip_16 (13)" };

    static void Swap(StringBuilder log)
    {
        var s2 = new List<WarShip>(); var s3 = new List<WarShip>(); var s8 = new List<WarShip>();
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
        {
            if (w.model == null || w.useFormation || w.unarmed) continue;
            if (w.target != null && w.target.kind != 1) continue;   // 空母・貨物船は変えない
            string t = TypeOf(w.model);
            if (t == "S2") s2.Add(w); else if (t == "S3") s3.Add(w);
            else if (t == "S8" && System.Array.IndexOf(MadeS8, w.name) >= 0) s8.Add(w);
        }
        s2 = s2.OrderBy(w => w.name).ToList(); s3 = s3.OrderBy(w => w.name).ToList();
        log.AppendLine("S2 " + s2.Count + " 隻・S3 " + s3.Count + " 隻（それぞれ " + Keep + " 隻残す）");
        var change = new List<WarShip>();
        change.AddRange(s2.Skip(Keep)); change.AddRange(s3.Skip(Keep)); change.AddRange(s8.OrderBy(w => w.name));
        foreach (var w in s2.Take(Keep).Concat(s3.Take(Keep))) log.AppendLine(w.name + ": " + TypeOf(w.model) + " のまま");

        int k = 0;
        var counts = new Dictionary<string, int>();
        foreach (var w in change)
        {
            string nt = NewTypes[k % NewTypes.Length]; k++;
            string old = TypeOf(w.model);
            if (SwapOne(w, nt, log)) { counts[nt] = (counts.ContainsKey(nt) ? counts[nt] : 0) + 1; log.AppendLine(w.name + ": " + old + " → " + nt); }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("入れ替えた船: " + string.Join("・", counts.Select(kv => kv.Key + " " + kv.Value + " 隻")) + "。シーンを保存してください");
    }

    public static bool SwapOne(WarShip w, string type, StringBuilder log)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + type + ".prefab");
        if (asset == null) { log.AppendLine(w.name + ": " + type + ".prefab が無い（そのまま）"); return false; }
        GameObject oldM = w.model;
        Transform ot = oldM.transform;
        Bounds ob;
        if (!HullBounds(oldM, out ob)) { log.AppendLine(w.name + ": 前の見た目の形が測れない（そのまま）"); return false; }

        var nm = (GameObject)PrefabUtility.InstantiatePrefab(asset, ot.parent);
        Undo.RegisterCreatedObjectUndo(nm, "Swap ship models");
        nm.name = type;
        nm.transform.localPosition = ot.localPosition;
        nm.transform.localRotation = ot.localRotation;
        nm.transform.localScale = ot.localScale;
        nm.transform.SetSiblingIndex(ot.GetSiblingIndex());
        nm.SetActive(oldM.activeSelf);
        Bounds nb;
        if (HullBounds(nm, out nb))
        {
            // 船底と真ん中をそろえる
            Vector3 d = new Vector3(ob.center.x - nb.center.x, ob.min.y - nb.min.y, ob.center.z - nb.center.z);
            nm.transform.position += d;
        }

        // 見た目の中に後から足した物（煙・炎・航跡・サーチライトなど）を新しい見た目へ移す
        int moved = 0;
        foreach (var ag in PrefabUtility.GetAddedGameObjects(oldM))
        {
            var g = ag.instanceGameObject;
            if (g == null) continue;
            Undo.SetTransformParent(g.transform, nm.transform, "Swap ship models");
            moved++;
        }

        // 当たり判定: 新しい見た目に無ければ、船体の箱を作る（飛行機セットアップと同じ）
        var cols = new List<Collider>();
        foreach (var c in nm.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
        if (cols.Count == 0 && HullBounds(nm, out nb))
        {
            var bc = Undo.AddComponent<BoxCollider>(nm);
            Transform st = nm.transform;
            Vector3 hullCenter = new Vector3(nb.center.x, nb.min.y + nb.size.y * 0.3f, nb.center.z);
            Vector3 hullSize = new Vector3(nb.size.x, nb.size.y * 0.6f, nb.size.z);
            bc.center = st.InverseTransformPoint(hullCenter);
            Vector3 sz = st.InverseTransformVector(hullSize);
            bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            cols.Add(bc);
        }

        // 耐久値の見た目と当たり判定のリストを入れ替える（前の見た目の分だけ。ほかの物はそのまま）
        if (w.target != null)
        {
            Undo.RecordObject(w.target, "Swap ship models");
            var rs = new List<Renderer>();
            if (w.target.renderers != null) foreach (var r in w.target.renderers) if (r != null && !r.transform.IsChildOf(ot)) rs.Add(r);
            foreach (var r in nm.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !rs.Contains(r)) rs.Add(r);
            var cs = new List<Collider>();
            if (w.target.colliders != null) foreach (var c in w.target.colliders) if (c != null && !c.transform.IsChildOf(ot)) cs.Add(c);
            foreach (var c in cols) if (!cs.Contains(c)) cs.Add(c);
            w.target.renderers = rs.ToArray();
            w.target.colliders = cs.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(w.target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(w.target);
        }
        Undo.RecordObject(w, "Swap ship models");
        w.model = nm;
        UdonSharpEditorUtility.CopyProxyToUdon(w);
        PrefabUtility.RecordPrefabInstancePropertyModifications(w);
        Undo.DestroyObjectImmediate(oldM);
        if (moved > 0) log.AppendLine("  " + w.name + ": 後から足した物 " + moved + " 個を新しい見た目へ移した");
        return true;
    }

    // 船体の形（プレハブにもともと入っている見た目だけ。後から足したサーチライトの光などは入れない）
    static bool HullBounds(GameObject m, out Bounds b)
    {
        b = new Bounds(m.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in m.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (PrefabUtility.GetCorrespondingObjectFromSource(r) == null) continue;   // 後から足した物
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }
}
