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
    static void SetupR35Pool(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.dronePool == null) { log.AppendLine("R35プール: 格納庫のプールが見つからない"); return; }
        var pool = putOut.dronePool;

        controller template = null;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && IsR35(c.car)) { template = c; break; }
        if (template == null) { log.AppendLine("R35プール: 椅子付きの R35 が見つからない"); return; }

        // すでに R35 だけのプールなら何もしない
        bool allR35 = pool.Pool != null && pool.Pool.Length > 0;
        if (pool.Pool != null) foreach (var g in pool.Pool) if (!IsR35(g)) allR35 = false;
        if (allR35) { log.AppendLine("R35プール: 設定済み（" + pool.Pool.Length + " 機）"); }
        else
        {
            var list = new System.Collections.Generic.List<GameObject>();
            list.Add(template.car);
            for (int i = 1; i < R35PoolSize; i++)
            {
                var copy = (GameObject)Object.Instantiate(template.car, template.car.transform.parent);
                copy.name = "R35_" + (i + 1);
                copy.transform.position = template.car.transform.position + Vector3.right * 40f * i;
                Undo.RegisterCreatedObjectUndo(copy, "Flight setup");
                list.Add(copy);
            }
            // 今までのドローンは消さずに非表示（元に戻せるように）
            if (pool.Pool != null)
            {
                foreach (var g in pool.Pool)
                {
                    if (g == null || list.Contains(g)) continue;
                    Undo.RecordObject(g, "Flight setup");
                    g.SetActive(false);
                }
            }
            var mainDrone = GameObject.Find("drone");
            if (mainDrone != null && mainDrone.GetComponentInChildren<controller>(true) != null && !list.Contains(mainDrone))
            {
                Undo.RecordObject(mainDrone, "Flight setup");
                mainDrone.SetActive(false);
            }
            foreach (var g in list)
            {
                Undo.RecordObject(g, "Flight setup");
                g.SetActive(false);   // プールの中の機体は、ボタンで出すまで非表示
            }
            Undo.RecordObject(pool, "Flight setup");
            pool.Pool = list.ToArray();
            EditorUtility.SetDirty(pool);
            log.AppendLine("R35プール: " + list.Count + " 機を格納庫のプールに入れた（ドローンは非表示）");
        }

        Undo.RecordObject(putOut, "Flight setup");
        putOut.NumberOFDrone = pool.Pool.Length;
        UdonSharpEditorUtility.CopyProxyToUdon(putOut);

        // テクスチャを 2048 に（ワールドのダウンロードサイズを抑える）
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/R35" }))
        {
            string tp = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (imp != null && imp.maxTextureSize > 2048)
            {
                imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
            }
        }
    }

    // ---------------- R35 の部品の動き ----------------
    static bool partsRetried;
    const string PartsScriptPath = "Assets/My script/drone/R35Parts.cs";
    const string PartsAssetPath = "Assets/My script/drone/R35Parts.asset";

    static void SetupR35Parts(StringBuilder log)
    {
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car) || c.axisReference == null || IsOmega(c.car)) continue;
            Transform car = c.car.transform;
            Transform ax = c.axisReference;
            Vector3 rightL = car.InverseTransformDirection(ax.right).normalized;
            Vector3 fwdW = ax.forward, upW = ax.up, rightW = ax.right;

            Bounds body = new Bounds(car.position, Vector3.zero);
            Transform bodyT = car.Find("Dropship_R35");
            Bounds tmpB;
            if (PartBounds(bodyT, out tmpB)) body = tmpB;

            Transform e1 = car.Find("Dropship_R35-Engine1"), e2 = car.Find("Dropship_R35-Engine2");
            Transform gl = car.Find("Dropship_R35-Landing-L"), gr = car.Find("Dropship_R35-Landing-R"), gf = car.Find("Dropship_R35-Landing_Front");
            Transform hatch = car.Find("Dropship_Hatch");
            Transform l1 = car.Find("Dropship_R35-Laser"), l2 = car.Find("Dropship_R35-Laser_B");

            var parts = car.GetComponent<R35Parts>();
            if (parts == null) parts = UdonSharpEditor.UdonSharpUndo.AddComponent<R35Parts>(c.car);
            Undo.RecordObject(parts, "Flight setup");
            parts.plane = c;
            parts.seaLevel = seaY;
            parts.lateralAxis = rightL;

            parts.engines = new[] { e1, e2 };
            parts.enginePivots = new[] { PivotOf(car, e1), PivotOf(car, e2) };

            parts.gears = new[] { gl, gr, gf };
            var offs = new Vector3[3];
            Transform[] gs = { gl, gr, gf };
            for (int i = 0; i < 3; i++)
            {
                if (gs[i] == null) continue;
                Bounds gb;
                float h = PartBounds(gs[i], out gb) ? gb.size.y : 1f;
                offs[i] = car.InverseTransformVector(upW * h * 0.7f);
            }
            parts.gearRetractOffsets = offs;
            parts.gearDeployHeight = 20f;

            // ハッチ: 横にあれば上が蝶番で外へ、前後にあれば下が蝶番でスロープのように開く
            parts.hatch = hatch;
            string hatchKind = "なし";
            Bounds hb;
            if (hatch != null && PartBounds(hatch, out hb))
            {
                Vector3 rel = hb.center - body.center;
                float side = Vector3.Dot(rel, rightW), along = Vector3.Dot(rel, fwdW);
                if (Mathf.Abs(side) > Mathf.Abs(along))
                {
                    parts.hatchPivot = car.InverseTransformPoint(hb.center + upW * hb.extents.y);
                    parts.hatchAxis = car.InverseTransformDirection(fwdW).normalized;
                    parts.hatchOpenAngle = side > 0f ? -75f : 75f;
                    hatchKind = side > 0f ? "右の扉" : "左の扉";
                }
                else
                {
                    parts.hatchPivot = car.InverseTransformPoint(hb.center - upW * hb.extents.y);
                    parts.hatchAxis = rightL;
                    parts.hatchOpenAngle = along < 0f ? -70f : 70f;
                    hatchKind = along < 0f ? "後ろのスロープ" : "前のスロープ";
                }
            }

            parts.lasers = new[] { l1, l2 };
            parts.laserRecoil = car.InverseTransformVector(-fwdW * 0.15f);
            UdonSharpEditorUtility.CopyProxyToUdon(parts);
            n++;
            if (n == 1) log.AppendLine("R35の部品: エンジン " + (e1 != null) + "/" + (e2 != null) + " 脚 " + (gl != null) + "/" + (gr != null) + "/" + (gf != null) + " ハッチ " + hatchKind + " レーザー " + (l1 != null) + "/" + (l2 != null));
        }
        log.AppendLine("R35の部品の動き: " + n + " 機に設定");
    }

    static Vector3 PivotOf(Transform car, Transform part)
    {
        if (part == null) return Vector3.zero;
        Bounds b;
        return car.InverseTransformPoint(PartBounds(part, out b) ? b.center : part.position);
    }

    // ---------------- 大きさの測り方（非表示の物でも測れる） ----------------
    // MeshFilter のメッシュの大きさから、ワールドでの範囲を求める（Renderer.bounds は非表示だと 0 になるため）
    static bool MeshBounds(Transform root, string namePrefix, out Bounds bd)
    {
        bd = new Bounds();
        bool any = false;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (!string.IsNullOrEmpty(namePrefix) && !mf.name.StartsWith(namePrefix)) continue;
            bool inGear = false;   // 脚（タイヤ）は機体の大きさに含めない
            for (Transform pt = mf.transform; pt != null && pt != root.parent; pt = pt.parent) if (pt.name == "LandingGear") { inGear = true; break; }
            if (inGear) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 w = mf.transform.TransformPoint(c);
                if (!any) { bd = new Bounds(w, Vector3.zero); any = true; } else bd.Encapsulate(w);
            }
        }
        return any;
    }

    static bool PartBounds(Transform part, out Bounds bd)
    {
        bd = new Bounds();
        if (part == null) return false;
        var mf = part.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        Bounds mb = mf.sharedMesh.bounds;
        bool any = false;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 w = part.TransformPoint(c);
            if (!any) { bd = new Bounds(w, Vector3.zero); any = true; } else bd.Encapsulate(w);
        }
        return true;
    }

    // R35 の当たり判定・着地の高さ・操縦中に隠す外側
    static void FixR35(StringBuilder log)
    {
        int n = 0;
        var omegaList = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Transform car = c.car.transform;
            Bounds bd;
            bool omega = IsOmega(c.car);
            if (!MeshBounds(omega ? car.Find("OmegaBody") : car, omega ? "" : "Dropship", out bd)) continue;
            var bc = c.car.GetComponent<BoxCollider>();
            if (bc == null) bc = Undo.AddComponent<BoxCollider>(c.car);
            Undo.RecordObject(bc, "Flight setup");
            bc.center = car.InverseTransformPoint(bd.center);
            Vector3 sz = car.InverseTransformVector(bd.size);
            bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));

            var ext = new System.Collections.Generic.List<Renderer>();
            if (omega) { }   // Omega はガラスの操縦席なので、乗っている間も機体は隠さない
            else foreach (var r in c.car.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Dropship")) ext.Add(r);

            Undo.RecordObject(c, "Flight setup");
            float halfH = Mathf.Max(bd.center.y - bd.min.y, 0.5f);
            c.gearHeight = halfH + 0.3f;
            c.waterContactHeight = halfH + 0.2f;
            if (omega) omegaList.Add(c);   // 脚はループの後でまとめて
            c.exteriorRenderers = ext.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
            if (n == 1) log.AppendLine("R35の大きさ: " + bd.size.ToString("F1") + "m（当たり判定をこの大きさに）");
        }
        if (n > 0) log.AppendLine("R35の当たり判定・高さ: " + n + " 機を修正");
        SetupOmegaGears(omegaList, log);   // 出し入れできる脚（タイヤ）
    }

    // ---------------- 出し入れできる脚（Omega） ----------------
    // 前輪 1 本（前に畳む）と主脚 2 本（内側に畳む）。タイヤには当たり判定があり、出し切った時だけ機体を支える
    // Omega_1 の脚を手で直したら、その形を他の Omega にコピーする（自動で作った形と違う 1 機を「手で直した物」とみなす）
    const float GearDrop = 1.4f;     // 胴体の一番下からタイヤの一番下まで（m、長さ 14m の機体の時）
    const float GearWheelR = 0.33f;  // タイヤの半径（m、同上）

    static Vector3[] BoxCorners(Transform t, Vector3 center, Vector3 size)
    {
        var a = new Vector3[8];
        for (int i = 0; i < 8; i++)
            a[i] = t.TransformPoint(center + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        return a;
    }

    // 脚の見た目（と当たり判定）を作る。sc = 大きさの倍率（機体の長さ / 14m）
    static Transform MakeGear(Transform car, Vector3[] corners, Vector3 fwd, Vector3 up, float sc, PhysicMaterial pm, bool colliders)
    {
        Vector3 right = Vector3.Cross(up, fwd);
        Vector3 cW = Vector3.zero;
        foreach (var p in corners) cW += p;
        cW /= corners.Length;
        float minF = 1e9f, maxF = -1e9f, minR = 1e9f, maxR = -1e9f, minU = 1e9f;
        foreach (var p in corners)
        {
            Vector3 d = p - cW;
            float pf = Vector3.Dot(d, fwd), pr = Vector3.Dot(d, right), pu = Vector3.Dot(d, up);
            minF = Mathf.Min(minF, pf); maxF = Mathf.Max(maxF, pf);
            minR = Mathf.Min(minR, pr); maxR = Mathf.Max(maxR, pr);
            minU = Mathf.Min(minU, pu);
        }
        float len = maxF - minF, wid = maxR - minR, midF = (minF + maxF) * 0.5f;
        var rootGo = new GameObject("LandingGear");
        Undo.RegisterCreatedObjectUndo(rootGo, "Flight setup");
        Transform root = rootGo.transform;
        root.SetParent(car, false);
        root.SetPositionAndRotation(cW, Quaternion.LookRotation(fwd, up));
        SetWorldScaleOne(root);
        int layer = car.gameObject.layer;
        rootGo.layer = layer;

        Material metal = LoadOrCreateMat("GearMetal", Shader.Find("Standard"));
        metal.SetColor("_Color", new Color(0.62f, 0.64f, 0.66f));
        metal.SetFloat("_Metallic", 0.8f);
        metal.SetFloat("_Glossiness", 0.55f);
        EditorUtility.SetDirty(metal);
        Material tire = LoadOrCreateMat("GearTire", Shader.Find("Standard"));
        tire.SetColor("_Color", new Color(0.06f, 0.06f, 0.06f));
        tire.SetFloat("_Metallic", 0f);
        tire.SetFloat("_Glossiness", 0.15f);
        EditorUtility.SetDirty(tire);

        float drop = GearDrop * sc, wr = GearWheelR * sc;
        float side = Mathf.Min(wid * 0.16f, 2.2f * sc);
        string[] names = { "GearNose", "GearLeft", "GearRight" };
        Vector3[] pos = {
            new Vector3(0f, minU + 0.05f * sc, midF + len * 0.30f),
            new Vector3(-side, minU + 0.05f * sc, midF - len * 0.06f),
            new Vector3(side, minU + 0.05f * sc, midF - len * 0.06f) };
        float strutLen = drop - wr;
        for (int i = 0; i < 3; i++)
        {
            var mount = new GameObject(names[i]).transform;
            mount.SetParent(root, false);
            mount.localPosition = pos[i];
            mount.gameObject.layer = layer;
            var pv = new GameObject("Pivot").transform;
            pv.SetParent(mount, false);
            pv.gameObject.layer = layer;
            var strut = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(strut.GetComponent<Collider>());
            strut.name = "Strut";
            strut.layer = layer;
            strut.transform.SetParent(pv, false);
            strut.transform.localPosition = new Vector3(0f, -strutLen * 0.5f, 0f);
            strut.transform.localScale = new Vector3(0.13f * sc, strutLen * 0.5f, 0.13f * sc);
            strut.GetComponent<MeshRenderer>().sharedMaterial = metal;
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
            wheel.name = "Wheel";
            wheel.layer = layer;
            wheel.transform.SetParent(pv, false);
            wheel.transform.localPosition = new Vector3(0f, -strutLen, 0f);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(wr * 2f, (i == 0 ? 0.1f : 0.13f) * sc, wr * 2f);
            wheel.GetComponent<MeshRenderer>().sharedMaterial = tire;
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.name = "Hub";
            hub.layer = layer;
            hub.transform.SetParent(pv, false);
            hub.transform.localPosition = new Vector3(0f, -strutLen, 0f);
            hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            hub.transform.localScale = new Vector3(wr * 1.1f, (i == 0 ? 0.105f : 0.135f) * sc, wr * 1.1f);
            hub.GetComponent<MeshRenderer>().sharedMaterial = metal;
            if (colliders)
            {
                var colGo = new GameObject("WheelCollider");
                colGo.layer = layer;
                colGo.transform.SetParent(pv, false);
                colGo.transform.localPosition = new Vector3(0f, -strutLen, 0f);
                var scol = colGo.AddComponent<SphereCollider>();
                scol.radius = wr;
                if (pm != null) scol.sharedMaterial = pm;
            }
        }
        return root;
    }

    // 脚の形の「指紋」（位置・回転・大きさを丸めて並べた文字列）。手で直したかどうかの見分けに使う
    static string GearSignature(Transform car, Transform root)
    {
        var sb = new StringBuilder();
        Quaternion inv = Quaternion.Inverse(car.rotation);
        Vector3 rp = car.InverseTransformPoint(root.position);
        Vector3 rr = (inv * root.rotation).eulerAngles;
        sb.Append(rp.x.ToString("F3")).Append(',').Append(rp.y.ToString("F3")).Append(',').Append(rp.z.ToString("F3")).Append(';')
          .Append(rr.x.ToString("F0")).Append(',').Append(rr.y.ToString("F0")).Append(',').Append(rr.z.ToString("F0")).Append('|');
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            Vector3 lp = t.localPosition, le = t.localEulerAngles, ls = t.localScale;
            sb.Append(t.name).Append(':')
              .Append(lp.x.ToString("F3")).Append(',').Append(lp.y.ToString("F3")).Append(',').Append(lp.z.ToString("F3")).Append(';')
              .Append(le.x.ToString("F0")).Append(',').Append(le.y.ToString("F0")).Append(',').Append(le.z.ToString("F0")).Append(';')
              .Append(ls.x.ToString("F3")).Append(',').Append(ls.y.ToString("F3")).Append(',').Append(ls.z.ToString("F3")).Append('|');
        }
        return sb.ToString().Replace("-0.000", "0.000").Replace("360", "0");
    }

    static void OmegaAxes(controller c, out Vector3 fwd, out Vector3 up)
    {
        Transform car = c.car.transform;
        Transform axis = car.Find("FlightAxis");
        fwd = axis != null ? axis.forward : car.forward;
        up = axis != null ? axis.up : car.up;
    }

    // 全 Omega の脚: 無ければ作る / 手で直した 1 機があれば、その形を他にコピー / 最後に controller へ登録
    static void SetupOmegaGears(System.Collections.Generic.List<controller> list, StringBuilder log)
    {
        if (list.Count == 0) return;
        // 1) 手で直した脚を探す
        //    ・脚の形が 1 機だけ他と違う → その 1 機を手で直した物とみなす（2 回目以降に直した時）
        //    ・全部同じ → 揃っているので何もしない
        //    ・自動で作った時の形と違うのが 1 機だけ → それを元にする（最初に直した時）
        var sigs = new System.Collections.Generic.Dictionary<controller, string>();
        var count = new System.Collections.Generic.Dictionary<string, int>();
        var edited = new System.Collections.Generic.List<controller>();
        foreach (var c in list)
        {
            Transform car = c.car.transform;
            Transform g = car.Find("LandingGear");
            if (g == null) continue;
            string sg = GearSignature(car, g);
            sigs[c] = sg;
            count[sg] = count.ContainsKey(sg) ? count[sg] + 1 : 1;
            var bc = c.car.GetComponent<BoxCollider>();
            Vector3 fwd, up;
            OmegaAxes(c, out fwd, out up);
            Transform tmp = MakeGear(car, BoxCorners(car, bc.center, bc.size), fwd, up, 1f, null, true);
            string expect = GearSignature(car, tmp);
            Object.DestroyImmediate(tmp.gameObject);
            if (sg != expect) edited.Add(c);
        }
        controller tmpl = null;
        if (count.Count == 1 && sigs.Count == list.Count) log.AppendLine("脚: 全 " + list.Count + " 機とも同じ形（揃っています）");
        else
        {
            var uniq = new System.Collections.Generic.List<controller>();
            foreach (var kv in sigs) if (count[kv.Value] == 1) uniq.Add(kv.Key);
            if (uniq.Count == 1 && sigs.Count >= 3) tmpl = uniq[0];
            else if (edited.Count == 1) tmpl = edited[0];
            else if (edited.Count > 1)
            {
                var named = edited.FindAll(e => e.car.name == "Omega_1");
                if (named.Count == 1) tmpl = named[0];
                else log.AppendLine("脚: 形の違う脚が " + edited.Count + " 機あり、どれを元にするか決められないので、コピーしませんでした");
            }
        }
        if (tmpl != null) log.AppendLine("脚: " + tmpl.car.name + " の手で直した脚を元にします");

        // 2) コピー / 新しく作る
        foreach (var c in list)
        {
            Transform car = c.car.transform;
            Transform g = car.Find("LandingGear");
            if (tmpl != null && c != tmpl)
            {
                Transform src = tmpl.car.transform.Find("LandingGear");
                if (g != null) Undo.DestroyObjectImmediate(g.gameObject);
                var cp = Object.Instantiate(src.gameObject, car);
                Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
                cp.name = "LandingGear";
                Transform tc = tmpl.car.transform;
                cp.transform.position = car.TransformPoint(tc.InverseTransformPoint(src.position));
                cp.transform.rotation = car.rotation * (Quaternion.Inverse(tc.rotation) * src.rotation);
                cp.transform.localScale = src.localScale;
                log.AppendLine("脚: " + car.name + " に " + tmpl.car.name + " の脚をコピー");
            }
            else if (g == null)
            {
                var bc = c.car.GetComponent<BoxCollider>();
                Vector3 fwd, up;
                OmegaAxes(c, out fwd, out up);
                MakeGear(car, BoxCorners(car, bc.center, bc.size), fwd, up, 1f, bc.sharedMaterial, true);
                log.AppendLine("脚: " + car.name + " に前輪 1・主脚 2 を作りました");
            }
            GatherGear(c, log);
        }
    }

    // LandingGear の中身を controller に登録。タイヤの当たり判定は見た目のタイヤに合わせ直す
    static void GatherGear(controller c, StringBuilder log)
    {
        Transform car = c.car.transform;
        Transform root = car.Find("LandingGear");
        if (root == null) return;
        Vector3 fwd, up;
        OmegaAxes(c, out fwd, out up);
        var bc = c.car.GetComponent<BoxCollider>();
        var pivots = new System.Collections.Generic.List<Transform>();
        var folds = new System.Collections.Generic.List<Vector3>();
        var bases = new System.Collections.Generic.List<Quaternion>();
        var cols = new System.Collections.Generic.List<Collider>();
        var vis = new System.Collections.Generic.List<GameObject>();
        float bottomU = 1e9f;
        foreach (var pv in root.GetComponentsInChildren<Transform>(true))
        {
            if (pv.name != "Pivot") continue;
            string pn = pv.parent != null ? pv.parent.name : "";
            Vector3 fold = pn.Contains("Left") ? new Vector3(0f, 0f, 90f) : (pn.Contains("Right") ? new Vector3(0f, 0f, -90f) : new Vector3(-90f, 0f, 0f));
            Transform wheel = null;
            foreach (var t in pv.GetComponentsInChildren<Transform>(true)) if (t.name == "Wheel") { wheel = t; break; }
            SphereCollider scol = pv.GetComponentInChildren<SphereCollider>(true);
            if (scol == null)
            {
                var colGo = new GameObject("WheelCollider");
                colGo.layer = car.gameObject.layer;
                colGo.transform.SetParent(pv, false);
                scol = colGo.AddComponent<SphereCollider>();
                if (bc != null) scol.sharedMaterial = bc.sharedMaterial;
            }
            Undo.RecordObject(scol, "Flight setup");
            Undo.RecordObject(scol.transform, "Flight setup");
            if (wheel != null)
            {
                // 当たり判定を見た目のタイヤの位置・大きさに
                scol.transform.position = wheel.position;
                float worldR = Mathf.Abs(wheel.lossyScale.x) * 0.5f;
                Vector3 ls = scol.transform.lossyScale;
                float m = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                scol.center = Vector3.zero;
                scol.radius = m > 0.0001f ? worldR / m : worldR;
            }
            Vector3 lsc = scol.transform.lossyScale;
            float wr = scol.radius * Mathf.Max(Mathf.Abs(lsc.x), Mathf.Max(Mathf.Abs(lsc.y), Mathf.Abs(lsc.z)));
            float u = Vector3.Dot(scol.transform.TransformPoint(scol.center) - car.position, up) - wr;
            bottomU = Mathf.Min(bottomU, u);
            pivots.Add(pv); folds.Add(fold); bases.Add(pv.localRotation); cols.Add(scol); vis.Add(pv.gameObject);
        }
        if (pivots.Count == 0) { log.AppendLine("脚: " + car.name + " の LandingGear に Pivot が見つからない"); return; }
        float boxBottomU = 1e9f;
        if (bc != null) foreach (var p in BoxCorners(car, bc.center, bc.size)) boxBottomU = Mathf.Min(boxBottomU, Vector3.Dot(p - car.position, up));
        Undo.RecordObject(c, "Flight setup");
        c.gearPivots = pivots.ToArray();
        c.gearFold = folds.ToArray();
        c.gearBaseRot = bases.ToArray();
        c.gearColliders = cols.ToArray();
        c.gearVisuals = vis.ToArray();
        c.gearRoot = root.gameObject;
        c.gearHeightDown = -bottomU + 0.3f;
        c.gearLift = Mathf.Max(boxBottomU - bottomU, 0f);
        UdonSharpEditorUtility.CopyProxyToUdon(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
    }

    // 敵の戦闘機の脚をそろえる: 形が 1 機だけ他と違う（＝手で直した）機体の脚を、全機にコピー。
    // 見分けられない時は EnemyFighter_20 を元にする
    // 機体の見た目（モデル）の Transform。機体の根元と見た目の位置がずれている機体があるので、脚はこれを基準にする
    static Transform EnemyModelOf(GameObject f)
    {
        foreach (Transform ch in f.transform)
        {
            if (ch.name == "LandingGear" || ch.name == "Gun" || ch.name == "EnemyBrain") continue;
            if (ch.GetComponentInChildren<Renderer>(true) != null) return ch;
        }
        return f.transform;
    }

    // 敵の脚を EnemyDrone に登録
}
