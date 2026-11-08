using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 船のダメージの煙と炎を、船の形に合わせて置き直す
//  ・船体の部品（FlightTarget の renderers）だけで形を測る（サーチライトの光の筋などは入れない）
//  ・形の長い方向に沿って、前と後ろの甲板のあたりに 1 組ずつ置く
//  ・ゲーム中は、この位置を覚えて、沈む時に船と一緒に動かす（WarShip_3.cs）
// メニュー「Tools/船の煙と炎の位置を直す」
public static class ShipFxFix
{
    [MenuItem("Tools/船の煙と炎の位置を直す")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipFxFix " + System.DateTime.Now);
        int ok = 0;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            try { if (Fix(ws, log)) ok++; }
            catch (System.Exception e) { log.AppendLine(ws.name + ": 失敗 " + e.Message); }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("煙と炎を置き直した船: " + ok + " 隻。シーンを保存してください");
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/ship_fx_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static bool Fix(WarShip ws, StringBuilder log)
    {
        bool hasFx = (ws.dmgSmoke != null && System.Array.Exists(ws.dmgSmoke, p => p != null)) || (ws.dmgFire != null && System.Array.Exists(ws.dmgFire, p => p != null));
        if (!hasFx) { log.AppendLine(ws.name + ": 煙と炎が無い（そのまま）"); return false; }
        if (ws.model == null) { log.AppendLine(ws.name + ": 見た目（model）が無いので直せない"); return false; }
        Transform mt = ws.model.transform;

        // 船体の部品だけで、model から見た形（箱）を測る
        Renderer[] rs = ws.target != null && ws.target.renderers != null && ws.target.renderers.Length > 0 ? ws.target.renderers : ws.model.GetComponentsInChildren<MeshRenderer>(true);
        Vector3 mn = Vector3.one * 1e9f, mx = Vector3.one * -1e9f;
        bool any = false;
        string biggest = ""; float bigLen = 0f;
        foreach (var r in rs)
        {
            if (r == null || !r.transform.IsChildOf(mt)) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // 粒子・軌跡は入れない
            if (NotHull(r.transform, mt)) continue;                               // サーチライトの光の筋・砲弾など
            Mesh mesh = null;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
            var sk = r as SkinnedMeshRenderer;
            if (sk != null) mesh = sk.sharedMesh;
            if (mesh == null) continue;
            Bounds b = mesh.bounds;
            Vector3 rmn = Vector3.one * 1e9f, rmx = Vector3.one * -1e9f;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 p = mt.InverseTransformPoint(r.transform.TransformPoint(corner));
                rmn = Vector3.Min(rmn, p); rmx = Vector3.Max(rmx, p);
            }
            mn = Vector3.Min(mn, rmn); mx = Vector3.Max(mx, rmx);
            float rl = Vector3.Scale(rmx - rmn, mt.lossyScale).magnitude;
            if (rl > bigLen) { bigLen = rl; biggest = r.name; }
            any = true;
        }
        if (!any) { log.AppendLine(ws.name + ": 船体の形が測れない"); return false; }

        Vector3 c = (mn + mx) * 0.5f, e = (mx - mn) * 0.5f;
        Vector3 along = e.x > e.z ? new Vector3(e.x * 0.35f, 0f, 0f) : new Vector3(0f, 0f, e.z * 0.35f);
        float seaLocal = mt.InverseTransformPoint(new Vector3(mt.position.x, ws.seaLevel, mt.position.z)).y;
        float deck = Mathf.Max(seaLocal + 4f / Mathf.Max(mt.lossyScale.y, 0.0001f), Mathf.Lerp(seaLocal, mx.y, 0.3f));   // 甲板のあたり（海面より上）
        float lift = 2f / Mathf.Max(mt.lossyScale.y, 0.0001f);
        Place(ws.dmgSmoke, mt, new Vector3(c.x, deck, c.z), along, lift);
        Place(ws.dmgFire, mt, new Vector3(c.x, deck, c.z), along, 0f);
        Vector3 size = Vector3.Scale(mx - mn, mt.lossyScale);
        log.AppendLine(ws.name + ": 長さ " + Mathf.Max(size.x, size.z).ToString("F0") + "m 幅 " + Mathf.Min(size.x, size.z).ToString("F0") + "m（いちばん大きい部品: " + biggest + "）→ 煙と炎を前後に置き直した");
        return true;
    }

    // 船体ではない部品（測る時に入れない）
    static readonly string[] NotHullNames = { "Searchlight", "Beam", "Lamp", "Shells", "Shell", "Rockets", "Rocket", "CannonFx", "Muzzles", "AAGun", "Tracer", "ShipWake", "ShipDamage", "Explosion", "Flash" };
    static bool NotHull(Transform t, Transform root)
    {
        for (Transform p = t; p != null && p != root; p = p.parent)
            foreach (var n in NotHullNames)
                if (p.name == n || p.name.StartsWith(n + "_") || p.name.StartsWith(n + " (")) return true;
        return false;
    }

    static void Place(ParticleSystem[] list, Transform mt, Vector3 center, Vector3 along, float up)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null) continue;
            Transform t = list[i].transform;
            Undo.RecordObject(t, "Ship fx");
            t.position = mt.TransformPoint(center + along * ShipFireSpread.Factor(i) + Vector3.up * up);   // 0 前・1 後ろ・2 真ん中・3 もっと前・4 もっと後ろ
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
    }
}
