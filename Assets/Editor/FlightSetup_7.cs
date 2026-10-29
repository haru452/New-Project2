using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using VRC.SDK3.Components;
using VRC.Udon;
using TMPro;
using UdonSharp;

// FlightSetup.cs の続き（長いので分けたファイル。中身は同じクラス FlightSetup）
public static partial class FlightSetup
{
    static void SetupBoarding(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null) continue;
            var st = c.GetComponent<VRCStation>();
            if (st == null) continue;
            // クリックを受けるには機体本体に当たり判定が要る
            bool hasCol = false;
            foreach (var col in c.car.GetComponents<Collider>()) if (!col.isTrigger) hasCol = true;
            if (!hasCol) continue;

            // 以前は機体本体に付けていたが、乗っている間に機体ごと（コックピットも）水色に光るので外す
            var oldB = c.car.GetComponent<BoardPlane>();
            if (oldB != null)
            {
                UdonBehaviour oub = UdonSharpEditorUtility.GetBackingUdonBehaviour(oldB);
                Undo.DestroyObjectImmediate(oldB);
                if (oub != null) Undo.DestroyObjectImmediate(oub);
            }
            Transform trig = null;
            bool onHull = false;   // R35: 後ろのハッチの所だけの小さな当たり判定
            if (IsR35(c.car))
            {
                // 以前は胴体（エンジン）の見た目に付けていたが、乗っている間に光って画面全体が水色になるので外す
                foreach (var ob in c.car.GetComponentsInChildren<BoardPlane>(true))
                {
                    if (ob.gameObject.name == "BoardTrigger") continue;
                    GameObject og = ob.gameObject;
                    UdonBehaviour oub2 = UdonSharpEditorUtility.GetBackingUdonBehaviour(ob);
                    Undo.DestroyObjectImmediate(ob);
                    if (oub2 != null) Undo.DestroyObjectImmediate(oub2);
                    foreach (var tcol in og.GetComponents<Collider>()) if (tcol.isTrigger) Undo.DestroyObjectImmediate(tcol);
                }
                onHull = true;
            }
            trig = c.car.transform.Find("BoardTrigger");
            if (trig == null)
            {
                var tg = new GameObject("BoardTrigger");
                Undo.RegisterCreatedObjectUndo(tg, "Flight setup");
                tg.transform.SetParent(c.car.transform, false);
                trig = tg.transform;
            }
            foreach (var tc in trig.GetComponents<Collider>()) Undo.DestroyObjectImmediate(tc);
            if (onHull)
            {
                // ハッチ（後ろのスロープ）の位置に、見た目の無い小さな当たり判定。光るのはこの小さな箱だけ
                Transform hatch = null;
                if (!IsOmega(c.car))
                    foreach (var t2 in c.car.GetComponentsInChildren<Transform>(true)) if (t2.name == "Dropship_Hatch") { hatch = t2; break; }
                Bounds hbd;
                var hb = Undo.AddComponent<BoxCollider>(trig.gameObject);
                hb.isTrigger = true;
                if (hatch != null && PartBounds(hatch, out hbd))
                {
                    hb.center = trig.InverseTransformPoint(hbd.center);
                    Vector3 hs = trig.InverseTransformVector(hbd.size);
                    hb.size = new Vector3(Mathf.Clamp(Mathf.Abs(hs.x), 1.5f, 4f), Mathf.Clamp(Mathf.Abs(hs.y), 1.5f, 3f), Mathf.Clamp(Mathf.Abs(hs.z), 1.5f, 4f));
                }
                else if (IsOmega(c.car))
                {
                    // Omega: 操縦席のまわり（乗り込む所）だけ
                    hb.center = trig.InverseTransformPoint(c.transform.position + Vector3.up * 0.5f);
                    hb.size = new Vector3(1.4f, 1.3f, 1.8f);   // 操縦席のまわりだけ（小さめ）
                }
                else
                {
                    var rbc = c.car.GetComponent<BoxCollider>();
                    Vector3 cc = rbc != null ? rbc.center : Vector3.zero;
                    float hz = rbc != null ? rbc.size.z * 0.5f : 5f;
                    hb.center = cc - new Vector3(0f, 0f, hz);
                    hb.size = new Vector3(3f, 2.5f, 2f);
                }
            }
            else foreach (var src in c.car.GetComponents<Collider>())
            {
                if (src.isTrigger) continue;
                if (src is BoxCollider sb)
                {
                    var nb = Undo.AddComponent<BoxCollider>(trig.gameObject);
                    nb.center = sb.center; nb.size = sb.size * 1.06f + Vector3.one * 0.1f; nb.isTrigger = true;   // 機体の当たり判定より少し大きく（クリックが先に当たるように）
                }
                else if (src is SphereCollider ss)
                {
                    var ns = Undo.AddComponent<SphereCollider>(trig.gameObject);
                    ns.center = ss.center; ns.radius = ss.radius * 1.06f + 0.05f; ns.isTrigger = true;
                }
                break;
            }
            var b = trig.GetComponent<BoardPlane>();
            if (b == null) b = UdonSharpEditor.UdonSharpUndo.AddComponent<BoardPlane>(trig.gameObject);
            Undo.RecordObject(b, "Flight setup");
            b.station = st;
            b.plane = c;
            UdonSharpEditorUtility.CopyProxyToUdon(b);
            UdonBehaviour bub = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (bub != null)
            {
                Undo.RecordObject(bub, "Flight setup");
                bub.interactText = "乗る";
                // 機体の大きさに合わせて、少し離れていても反応するように
                float r = 2f;
                Bounds cb;
                if (MeshBounds(c.car.transform, "", out cb)) r = Mathf.Max(r, cb.extents.magnitude * 0.4f);
                // R35 は機体のすぐそば（3m）まで近づいた時だけ選べる。離れていると光らない
                bub.proximity = onHull ? 3f : Mathf.Clamp(r, 2f, 12f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(bub);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(b);

            Undo.RecordObject(c, "Flight setup");
            c.boarder = b;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
            n++;
        }
        log.AppendLine("乗る: " + n + " 機の本体をクリックで乗れるように設定");
    }

    // ---------------- 新しい機体（椅子付き）を飛べるようにする ----------------
    static void SetupNewAircraft(StringBuilder log)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/R35/")) continue;

            VRCStation st = null;
            foreach (var s2 in go.GetComponentsInChildren<VRCStation>(true))
                if (s2.gameObject.name != "GunnerSeat") { st = s2; break; }   // 2 人目の席は除く
            if (st == null) { log.AppendLine("新しい機体: " + go.name + " に椅子(VRCStation)が無いのでスキップ"); continue; }
            var c = st.GetComponent<controller>();
            bool isNew = c == null;

            // 見た目の大きさ（椅子は除く）
            Bounds bd = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(st.transform) || r is ParticleSystemRenderer) continue;
                if (!any) { bd = r.bounds; any = true; } else bd.Encapsulate(r.bounds);
            }

            // 物理
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            rb.mass = 800f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 当たり判定（無ければ機体の箱）
            bool hasCol = false;
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger || col.transform.IsChildOf(st.transform)) continue;
                hasCol = true;
            }
            if (!hasCol && any)
            {
                var bc = Undo.AddComponent<BoxCollider>(go);
                Transform tr = go.transform;
                bc.center = tr.InverseTransformPoint(bd.center);
                Vector3 sz = tr.InverseTransformVector(bd.size);
                bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            }

            // 機首の向き = 椅子が向いている方向（水平）
            Transform axis = go.transform.Find("FlightAxis");
            if (axis == null)
            {
                var ag = new GameObject("FlightAxis");
                Undo.RegisterCreatedObjectUndo(ag, "Flight setup");
                ag.transform.SetParent(go.transform, false);
                axis = ag.transform;
            }
            Vector3 f = st.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.001f) f = go.transform.forward;
            axis.position = go.transform.position;
            axis.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);

            // 飛行スクリプト
            if (isNew) c = UdonSharpEditor.UdonSharpUndo.AddComponent<controller>(st.gameObject);
            Undo.RecordObject(c, "Flight setup");
            c.car = go;
            c.axisReference = axis;
            // 機体の大きさに合わせて、着地・着水の判定の高さを決める
            float halfH = any ? Mathf.Max(bd.center.y - bd.min.y, 0.5f) : 1.3f;
            c.gearHeight = halfH + 0.3f;
            c.waterContactHeight = halfH + 0.2f;
            UdonSharpEditorUtility.CopyProxyToUdon(c);

            log.AppendLine("新しい機体: " + go.name + (isNew ? " を飛べるように設定" : " を更新") + " / 大きさ " + (any ? bd.size.ToString("F1") : "?") + " / 機首 " + axis.forward.ToString("F2"));
        }
    }

    // ---------------- 大砲を撃ってくる船 ----------------
    static bool shipRetried;
    const string ShipScriptPath = "Assets/My script/drone/WarShip.cs";
    const string ShipAssetPath = "Assets/My script/drone/WarShip.asset";
}
