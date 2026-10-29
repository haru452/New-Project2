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
    static bool GatherEnemyGear(GameObject f)
    {
        Transform root = f.transform.Find("LandingGear");
        var ed = f.GetComponentInChildren<EnemyDrone>(true);
        if (root == null || ed == null) return false;
        var pivots = new System.Collections.Generic.List<Transform>();
        var folds = new System.Collections.Generic.List<Vector3>();
        var bases = new System.Collections.Generic.List<Quaternion>();
        var vis = new System.Collections.Generic.List<GameObject>();
        foreach (var pv in root.GetComponentsInChildren<Transform>(true))
        {
            if (pv.name != "Pivot") continue;
            string pn = pv.parent != null ? pv.parent.name : "";
            pivots.Add(pv);
            folds.Add(pn.Contains("Left") ? new Vector3(0f, 0f, 90f) : (pn.Contains("Right") ? new Vector3(0f, 0f, -90f) : new Vector3(-90f, 0f, 0f)));
            bases.Add(pv.localRotation);
            vis.Add(pv.gameObject);
        }
        Undo.RecordObject(ed, "Flight setup");
        ed.gearPivots = pivots.ToArray();
        ed.gearFold = folds.ToArray();
        ed.gearBaseRot = bases.ToArray();
        ed.gearVisuals = vis.ToArray();
        ed.gearRoot = root.gameObject;
        UdonSharpEditorUtility.CopyProxyToUdon(ed);
        return pivots.Count > 0;
    }

    static void SyncEnemyGears(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var sigs = new System.Collections.Generic.Dictionary<GameObject, string>();
        var count = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var f in fighters)
        {
            Transform g = f.transform.Find("LandingGear");
            if (g == null) continue;
            string sg = GearSignature(EnemyModelOf(f), g);   // 見た目から見た位置で比べる
            sigs[f] = sg;
            count[sg] = count.ContainsKey(sg) ? count[sg] + 1 : 1;
        }
        if (sigs.Count == 0) return;
        if (count.Count == 1) { log.AppendLine("脚（敵）: 全 " + sigs.Count + " 機とも同じ形（揃っています）"); return; }
        GameObject tmpl = null;
        var uniq = new System.Collections.Generic.List<GameObject>();
        foreach (var kv in sigs) if (count[kv.Value] == 1) uniq.Add(kv.Key);
        if (uniq.Count == 1 && sigs.Count >= 3) tmpl = uniq[0];
        else foreach (var f in fighters) if (f.name == "EnemyFighter_1" && sigs.ContainsKey(f)) { tmpl = f; break; }
        if (tmpl == null) { log.AppendLine("脚（敵）: どの機体を元にするか決められないので、コピーしませんでした"); return; }
        Transform src = tmpl.transform.Find("LandingGear");
        Transform sm = EnemyModelOf(tmpl);
        Vector3 relP = sm.InverseTransformPoint(src.position);
        Quaternion relR = Quaternion.Inverse(sm.rotation) * src.rotation;
        int n = 0;
        foreach (var f in fighters)
        {
            if (f == tmpl) continue;
            Transform car = f.transform;
            Transform g = car.Find("LandingGear");
            if (g != null) Undo.DestroyObjectImmediate(g.gameObject);
            var cp = Object.Instantiate(src.gameObject, car);
            Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
            cp.name = "LandingGear";
            Transform m = EnemyModelOf(f);
            cp.transform.position = m.TransformPoint(relP);
            cp.transform.rotation = m.rotation * relR;
            cp.transform.localScale = src.localScale;
            n++;
        }
        log.AppendLine("脚（敵）: " + tmpl.name + " の脚を他の " + n + " 機にコピー（機体の見た目の位置に合わせて）");
    }

    // 敵の戦闘機: 脚の見た目だけ付ける。手で直せるよう、作るのは最初の 1 回だけ
    static void TryEnemyGear(GameObject go, StringBuilder log)
    {
        Transform tr = go.transform;
        if (tr.Find("LandingGear") != null) { return; }
        Bounds wb;
        if (!MeshBounds(tr, "", out wb)) return;
        Vector3 lc = tr.InverseTransformPoint(wb.center);
        Vector3 ls = tr.InverseTransformVector(wb.size);
        ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        float len = Mathf.Abs(Vector3.Dot(tr.TransformVector(new Vector3(0f, 0f, ls.z)), tr.forward));
        MakeGear(tr, BoxCorners(tr, lc, ls), tr.forward, tr.up, Mathf.Max(len / 14f, 0.1f), null, false);
        log.AppendLine("脚: " + go.name + "（敵）に脚を付けました（長さ " + len.ToString("F1") + "m の機体）");
    }

    // ---------------- 敵機（古いドローン） ----------------
    static bool enemyRetried;
    const string EnemyScriptPath = "Assets/My script/drone/EnemyDrone.cs";
    const string EnemyAssetPath = "Assets/My script/drone/EnemyDrone.asset";
    const int EnemyCount = 16;
    const float EnemyScale = 4.5f;   // 見やすいように元の大きさの 4.5 倍

    static void SetupEnemies(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        var poolObjs = (putOut != null && putOut.dronePool != null) ? putOut.dronePool.Pool : null;
        var drones = new System.Collections.Generic.List<controller>();
        var pilots = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null) continue;
            if (IsR35(c.car)) { pilots.Add(c); continue; }
            if (poolObjs != null && System.Array.IndexOf(poolObjs, c.car) >= 0) continue;
            if (c.car.GetComponent<Rigidbody>() == null) continue;
            drones.Add(c);
        }
        drones.Sort((a, b) => string.CompareOrdinal(a.car.name, b.car.name));
        // 「enemy aircraft」の戦闘機があれば、ドローンは全部しまって戦闘機を敵にする
        Transform fighterRoot = FindByNameContains("enemy aircraft");
        if (fighterRoot == null) fighterRoot = FindByNameContains("enemyaircraft");
        if (fighterRoot != null)
        {
            foreach (var dc in drones) { Undo.RecordObject(dc.car, "Flight setup"); dc.car.SetActive(false); }
            EnsureFlyingAwacs(fighterRoot, log);   // 飛ぶ警戒機 3 機（基地の警戒機と同じ見た目）
            EnsureCarrierFighters(fighterRoot, log);   // 空母の戦闘機 4 機（水色）
            EnsureBaseFighters(fighterRoot, log);      // 基地の戦闘機 4 機
            SetupFighters(fighterRoot, putOut, pilots, log);
            SetupParkedAwacs(log);                 // 基地に止まっている警戒機（壊せる）
            return;
        }
        // 足りない分は複製して増やす
        int origCount = drones.Count;
        for (int i = 0; origCount > 0 && drones.Count < EnemyCount; i++)
        {
            GameObject src = drones[i % origCount].car;
            GameObject cp = Object.Instantiate(src, src.transform.parent);
            cp.name = "EnemyDrone_" + (drones.Count + 1);
            Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
            controller cc = cp.GetComponentInChildren<controller>(true);
            if (cc == null) break;
            Undo.RecordObject(cc, "Flight setup");
            cc.car = cp;
            drones.Add(cc);
        }
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;

        int k = 0;
        foreach (var c in drones)
        {
            GameObject car = c.car;
            bool use = k < EnemyCount;
            Undo.RecordObject(car, "Flight setup");
            car.SetActive(use);
            if (!use) { k++; continue; }

            // 人が乗る仕組みを止める（座席・クリックで乗る・ドローン用の重力）
            Undo.RecordObject(c.gameObject, "Flight setup");
            c.gameObject.SetActive(false);
            foreach (var mb in car.GetComponents<UdonSharpBehaviour>())
            {
                if (mb is ajsutGravity || mb is BoardPlane)
                {
                    Undo.RecordObject(mb, "Flight setup");
                    mb.enabled = false;
                    UdonBehaviour bub = UdonSharpEditorUtility.GetBackingUdonBehaviour(mb);
                    if (bub != null) { Undo.RecordObject(bub, "Flight setup"); bub.enabled = false; }
                }
            }
            // 大きさを 3 倍に（元の大きさを子の印に覚えておき、何回実行しても 3 倍のまま）
            Transform baseMark = car.transform.Find("EnemyBaseScale");
            if (baseMark == null)
            {
                var bm = new GameObject("EnemyBaseScale");
                Undo.RegisterCreatedObjectUndo(bm, "Flight setup");
                bm.transform.SetParent(car.transform, false);
                bm.transform.localPosition = car.transform.localScale;
                baseMark = bm.transform;
            }
            Undo.RecordObject(car.transform, "Flight setup");
            car.transform.localScale = baseMark.localPosition * EnemyScale;

            var rb = car.GetComponent<Rigidbody>();
            Undo.RecordObject(rb, "Flight setup");
            rb.useGravity = false;
            rb.isKinematic = false;

            // 頭脳（EnemyDrone と耐久値）を子に作る
            Transform brain = car.transform.Find("EnemyBrain");
            if (brain != null) Undo.DestroyObjectImmediate(brain.gameObject);
            var bg = new GameObject("EnemyBrain");
            Undo.RegisterCreatedObjectUndo(bg, "Flight setup");
            bg.transform.SetParent(car.transform, false);
            brain = bg.transform;

            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (r.transform.IsChildOf(c.transform)) continue;   // 座席まわり
                rends.Add(r);
            }
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var col in car.GetComponents<Collider>()) if (!col.isTrigger) cols.Add(col);

            var ft = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(bg);
            ft.renderers = rends.ToArray();
            ft.colliders = cols.ToArray();
            ft.maxHp = 10;
            ft.missileDamage = 15;
            ft.respawnTime = 25f;
            var ex = MakePS("Explosion", brain, fxMat);
            ConfigureExplosion(ex, 6.5f, 90);   // 大きさに合わせて大きな爆発
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 25f;
            exAu.maxDistance = 2000f;
            ft.explosionFx = ex;
            ft.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(ft);

            var ed = UdonSharpEditor.UdonSharpUndo.AddComponent<EnemyDrone>(bg);
            ed.rb = rb;
            ed.target = ft;
            ed.planes = pilots.ToArray();
            Transform gun = car.transform.Find("Gun");
            ed.muzzle = gun;
            ed.tracer = gun != null && gun.Find("Tracer") != null ? gun.Find("Tracer").GetComponent<ParticleSystem>() : null;
            ed.gunAudio = gun != null ? gun.GetComponent<AudioSource>() : null;
            ed.areaCenter = CloudCenter;
            if (putOut != null && putOut.catapultPoint != null) { ed.avoidCenter = putOut.catapultPoint.position; ed.noFlyRef = putOut.catapultPoint; ed.noFlyRadius = 1000f; }   // 出撃地点のまわりには出ない
            ed.seaLevel = seaY;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);

            // 最初の位置: 中心のまわりに散らす（雲の下・高度 200m）
            float ang = k * Mathf.PI * 2f / EnemyCount;
            Vector3 p = CloudCenter + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1800f;
            p.y = seaY + 200f;
            Vector3 fwd = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            Undo.RecordObject(car.transform, "Flight setup");
            car.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 90f, 0f));
            k++;
        }
        log.AppendLine("敵機: " + Mathf.Min(drones.Count, EnemyCount) + " 機を雲の下に配置（ドローン " + drones.Count + " 機中）/ 狙う相手 " + pilots.Count + " 機");
    }

    static Transform FindByNameContains(string key)
    {
        key = key.ToLower();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name.ToLower().Contains(key) && t.parent == null) return t;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name.ToLower().Contains(key)) return t;
        return null;
    }

    // ---------------- 敵の戦闘機（enemy aircraft の中の機体） ----------------
    const int FighterCount = 16;

    static void SetupFighters(Transform root, PutOutDrone putOut, System.Collections.Generic.List<controller> pilots, StringBuilder log)
    {
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        AudioClip gunClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;

        // 機体 = root の直下で見た目を持つ物（無ければ root 自身）
        var fighters = new System.Collections.Generic.List<GameObject>();
        foreach (Transform ch in root)
        {
            if (ch.name == "EnemyBrain" || ch.name == "Gun") continue;
            if (ch.GetComponentInChildren<Renderer>(true) != null) fighters.Add(ch.gameObject);
        }
        if (fighters.Count == 0 && root.GetComponentInChildren<Renderer>(true) != null) fighters.Add(root.gameObject);
        if (fighters.Count == 0) { log.AppendLine("戦闘機: enemy aircraft の中に機体が見つからない"); return; }
        int orig = fighters.Count;
        for (int i = 0; fighters.Count < FighterCount && fighters[0] != root.gameObject; i++)
        {
            GameObject src = fighters[i % orig];
            GameObject cp = Object.Instantiate(src, src.transform.parent);
            Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
            fighters.Add(cp);
        }

        ScaleFighters(fighters, root, log);   // 敵の戦闘機を半分の大きさに（本物の戦闘機くらい）
        Bounds firstB = new Bounds();
        int n = 0, fno = 0;
        foreach (var go in fighters)
        {
            n++;
            Undo.RecordObject(go, "Flight setup");
            go.SetActive(true);
            if (go != root.gameObject && !IsAwacs(go) && !IsCarrierFighter(go) && !IsBaseFighter(go)) go.name = "EnemyFighter_" + (++fno);   // 警戒機・空母の戦闘機は名前を変えない
            Transform tr = go.transform;
            // 古い部品を消して作り直し
            foreach (var nm in new[] { "EnemyBrain", "Gun" })
            {
                Transform old;
                int guard = 0;
                while ((old = tr.Find(nm)) != null && guard++ < 10) Undo.DestroyObjectImmediate(old.gameObject);
            }
            // 影を落とさない（軽量化）
            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                Undo.RecordObject(r, "Flight setup");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rends.Add(r);
            }
            // 大きさ（機体の向きの中で）
            Bounds wb;
            if (!MeshBounds(tr, "", out wb)) wb = new Bounds(tr.position, Vector3.one * 10f);
            Vector3 lc = tr.InverseTransformPoint(wb.center);
            Vector3 ls = tr.InverseTransformVector(wb.size);
            ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            if (n == 1) firstB = new Bounds(lc, Vector3.Scale(ls, tr.lossyScale));

            // 物理
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            Undo.RecordObject(rb, "Flight setup");
            rb.mass = 5000f;
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) if (!col.isTrigger) cols.Add(col);
            if (cols.Count == 0)
            {
                var bc = Undo.AddComponent<BoxCollider>(go);
                bc.center = lc;
                bc.size = ls * 0.8f;
                cols.Add(bc);
            }
            if (go.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null) Undo.AddComponent<VRC.SDK3.Components.VRCObjectSync>(go);

            // 機銃（機首の先）
            var gun = new GameObject("Gun");
            Undo.RegisterCreatedObjectUndo(gun, "Flight setup");
            gun.transform.SetParent(tr, false);
            gun.transform.position = tr.TransformPoint(lc + new Vector3(0f, 0f, ls.z * 0.5f + 1f));
            gun.transform.rotation = tr.rotation;
            SetWorldScaleOne(gun.transform);
            var trc = MakePS("Tracer", gun.transform, fxMat);
            var tm = trc.main;
            tm.startLifetime = 1.4f;
            tm.startSpeed = 500f;
            tm.startSize = 0.4f;
            tm.startColor = new Color(1f, 0.45f, 0.25f, 1f);
            tm.maxParticles = 200;
            var tsh = trc.shape;
            tsh.enabled = false;
            var tiv = trc.inheritVelocity;
            tiv.enabled = true;
            tiv.mode = ParticleSystemInheritVelocityMode.Initial;
            tiv.curve = new ParticleSystem.MinMaxCurve(1f);
            var trr = trc.GetComponent<ParticleSystemRenderer>();
            trr.renderMode = ParticleSystemRenderMode.Stretch;
            trr.velocityScale = 0.025f;
            trr.lengthScale = 2f;
            var gau = gun.AddComponent<AudioSource>();
            gau.clip = gunClip;
            gau.playOnAwake = false;
            gau.spatialBlend = 1f;
            gau.minDistance = 15f;
            gau.maxDistance = 1200f;

            // 頭脳（耐久値と飛び方）
            var bg = new GameObject("EnemyBrain");
            Undo.RegisterCreatedObjectUndo(bg, "Flight setup");
            bg.transform.SetParent(tr, false);
            var ft = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(bg);
            ft.renderers = rends.ToArray();
            ft.colliders = cols.ToArray();
            ft.maxHp = 10;
            ft.missileDamage = 15;
            ft.respawnTime = 25f;
            ft.kind = 0;   // 飛行機
            var ex = MakePS("Explosion", bg.transform, fxMat);
            ConfigureExplosion(ex, 5f, 90);
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 30f;
            exAu.maxDistance = 2500f;
            ft.explosionFx = ex;
            ft.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(ft);

            var ed = UdonSharpEditor.UdonSharpUndo.AddComponent<EnemyDrone>(bg);
            ed.rb = rb;
            ed.target = ft;
            ed.planes = pilots.ToArray();
            ed.muzzle = gun.transform;
            ed.tracer = trc;
            ed.gunAudio = gau;
            ed.noseIsZ = true;
            // ダメージの煙と炎
            Material smokeM = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
            if (smokeM == null) smokeM = fxMat;
            var fsm = MakePS("DamageSmoke", bg.transform, smokeM);
            fsm.transform.position = wb.center;
            var fsmm = fsm.main;
            fsmm.loop = true;
            fsmm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            fsmm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            fsmm.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            fsmm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.7f), new Color(0.35f, 0.35f, 0.35f, 0.6f));
            fsmm.maxParticles = 200;
            var fsol2 = fsm.sizeOverLifetime; fsol2.enabled = true; fsol2.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            SetFade(fsm, new Color(0.25f, 0.25f, 0.25f), new Color(0.5f, 0.5f, 0.5f));
            var ffi = MakePS("DamageFire", bg.transform, fxMat);
            ffi.transform.position = wb.center;
            var ffim = ffi.main;
            ffim.loop = true;
            ffim.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            ffim.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            ffim.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            ffim.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
            ffim.maxParticles = 150;
            SetFade(ffi, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));
            ed.dmgSmoke = fsm;
            ed.dmgFire = ffi;
            // ミサイル（この機体専用の 1 発。見た目は機体の外に置く）
            var mRoot = GameObject.Find("EnemyMissiles");
            if (mRoot == null) { mRoot = new GameObject("EnemyMissiles"); Undo.RegisterCreatedObjectUndo(mRoot, "Flight setup"); }
            Transform oldM = mRoot.transform.Find("Missile_" + go.name);
            if (oldM != null) Undo.DestroyObjectImmediate(oldM.gameObject);
            var em = new GameObject("Missile_" + go.name);
            Undo.RegisterCreatedObjectUndo(em, "Flight setup");
            em.transform.SetParent(mRoot.transform, false);
            var emb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(emb.GetComponent<Collider>());
            emb.name = "Body";
            emb.transform.SetParent(em.transform, false);
            emb.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            emb.transform.localScale = new Vector3(0.3f, 1.1f, 0.3f);
            Material emMat = LoadOrCreateMat("ShipRocket", Shader.Find("Standard"));
            emb.GetComponent<MeshRenderer>().sharedMaterial = emMat;
            emb.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var efl = MakePS("Flame", em.transform, fxMat);
            efl.transform.localPosition = new Vector3(0f, 0f, -1.2f);
            var eflm = efl.main;
            eflm.loop = true; eflm.playOnAwake = true;
            eflm.simulationSpace = ParticleSystemSimulationSpace.Local;
            eflm.startLifetime = 0.12f; eflm.startSpeed = 0f;
            eflm.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            eflm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.45f));
            eflm.maxParticles = 30;
            var efle = efl.emission; efle.rateOverTime = 80f;
            var efls = efl.shape; efls.enabled = false;
            var etr = em.AddComponent<TrailRenderer>();
            etr.sharedMaterial = smokeM;
            etr.time = 2.5f;
            etr.minVertexDistance = 2f;
            etr.widthCurve = AnimationCurve.Linear(0f, 0.8f, 1f, 3.5f);
            var eg = new Gradient();
            eg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1f) },
                       new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            etr.colorGradient = eg;
            etr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var eau = em.AddComponent<AudioSource>();
            eau.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/missile.wav");
            eau.playOnAwake = false; eau.spatialBlend = 1f; eau.minDistance = 15f; eau.maxDistance = 1500f;
            em.SetActive(false);
            ed.missile = em.transform;
            var splashG = GameObject.Find("MissileSplash");
            var crashG = GameObject.Find("CrashExplosion");
            ed.missileSplash = splashG != null ? splashG.GetComponent<ParticleSystem>() : null;
            ed.missileBoom = crashG != null ? crashG.GetComponent<ParticleSystem>() : null;
            ed.missileBoomAudio = crashG != null ? crashG.GetComponent<AudioSource>() : null;
            ed.areaCenter = CloudCenter;
            ed.seaLevel = seaY;
            if (putOut != null && putOut.catapultPoint != null) { ed.avoidCenter = putOut.catapultPoint.position; ed.noFlyRef = putOut.catapultPoint; ed.noFlyRadius = 1000f; }
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
        }
        var gearFighters = fighters.FindAll(fg => fg != root.gameObject && !IsAwacs(fg));   // 警戒機はもともとタイヤ付きなので脚は付けない
        foreach (var fg in gearFighters) TryEnemyGear(fg, log);   // 敵の脚: 全機（無い機体にだけ作る）
        SyncEnemyGears(gearFighters, log);   // 手で直した 1 機の脚を全機にコピー
        int geared = 0;
        foreach (var fg in gearFighters) if (GatherEnemyGear(fg)) geared++;   // 飛んでいる間はしまう脚として登録
        LimitRegularFighters(fighters, log);
        ConfigureAwacs(fighters, log);
        ConfigureCarrierFighters(fighters, log);
        ConfigureBaseFighters(fighters, log);
        log.AppendLine("脚（敵）: " + geared + " 機に出し入れの動きを設定（地面の近く " + 60 + "m 以内で出す）");
        log.AppendLine("戦闘機: " + fighters.Count + " 機（元 " + orig + " 機）を敵に設定。機体の大きさ " + firstB.size.ToString("F1") + "m（z が機首の向き）。ドローンはしまいました");
    }

    // ---------------- 敵の戦闘機の大きさ ----------------
    const float FighterScale = 1f;   // 大きさは元のまま（一度半分にしたのを戻す）
    static void ScaleFighters(System.Collections.Generic.List<GameObject> fighters, Transform root, StringBuilder log)
    {
        int n = 0;
        foreach (var g in fighters)
        {
            if (g == root.gameObject || IsAwacs(g)) continue;
            Transform t = g.transform;
            float target = FighterScale / (t.parent != null ? t.parent.lossyScale.x : 1f);
            if (Mathf.Abs(t.localScale.x - target) < 0.001f) continue;   // もう半分になっている
            // 止まっている機体が浮かないよう、いちばん下の高さを保ったまま縮める
            float low0 = LowestY(t);
            Undo.RecordObject(t, "Flight setup");
            t.localScale = new Vector3(target, target, target);
            float low1 = LowestY(t);
            if (low0 < float.MaxValue && low1 < float.MaxValue) t.position += Vector3.up * (low0 - low1);
            n++;
        }
        if (n > 0) log.AppendLine("戦闘機: " + n + " 機を " + FighterScale + " 倍の大きさにしました（警戒機はそのまま）");
    }
    static float LowestY(Transform t)
    {
        float y = float.MaxValue;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            y = Mathf.Min(y, r.bounds.min.y);
        }
        return y;
    }

    // ---------------- 基地の戦闘機（military base の滑走路から発進） ----------------
    const int BaseFighterCount = 4;
    const int RegularFighterCount = 10;   // もともと飛んでいる敵の戦闘機の数（残りはしまう）
    static bool IsBaseFighter(GameObject g) { return g != null && g.name.StartsWith("BASE_"); }
}
