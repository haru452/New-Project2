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
    static void SetupCrash(Material fxMat, AudioClip boom, StringBuilder log)
    {
        GameObject old = GameObject.Find("CrashExplosion");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var ex = MakePS("CrashExplosion", null, fxMat);
        ConfigureExplosion(ex, 3f, 60);
        var splash = MakePS("Splash", ex.transform, fxMat);
        var sm = splash.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(12f, 30f);
        sm.startSize = new ParticleSystem.MinMaxCurve(2f, 6f);
        sm.startColor = new Color(0.75f, 0.85f, 0.95f, 0.6f);
        sm.gravityModifier = 1.2f;
        sm.maxParticles = 120;
        var se = splash.emission;
        se.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
        var ss = splash.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 25f;
        ss.radius = 3f;
        splash.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 上向きに水しぶき
        SetFade(splash, new Color(0.9f, 0.95f, 1f), new Color(0.6f, 0.7f, 0.8f));

        var au = ex.gameObject.AddComponent<AudioSource>();
        au.playOnAwake = false;
        au.clip = boom;
        au.spatialBlend = 1f;
        au.minDistance = 30f;
        au.maxDistance = 2500f;
        au.volume = 1f;

        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Transform respawn = (putOut != null && putOut.reportPoint != null) ? putOut.reportPoint.transform : null;
        var pool = putOut != null ? putOut.dronePool : null;

        int pooled = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            Undo.RecordObject(c, "Flight setup");
            c.crashFx = ex;
            c.crashAudio = au;
            c.oceanMaterial = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/Ocean.mat");   // 波に乗るため

            // エンジン音（機体に付けて、乗っている間ループ再生。全員に聞こえる）
            if (c.car != null)
            {
                AudioClip eng = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/engine.wav");
                Transform et = c.car.transform.Find("EngineAudio");
                AudioSource ea;
                if (et == null)
                {
                    var eg = new GameObject("EngineAudio");
                    Undo.RegisterCreatedObjectUndo(eg, "Flight setup");
                    eg.transform.SetParent(c.car.transform, false);
                    ea = eg.AddComponent<AudioSource>();
                }
                else ea = et.GetComponent<AudioSource>();
                ea.clip = eng;
                ea.loop = true;
                ea.playOnAwake = false;
                ea.spatialBlend = 1f;
                ea.minDistance = 6f;
                ea.maxDistance = 700f;
                ea.rolloffMode = AudioRolloffMode.Logarithmic;
                ea.dopplerLevel = 0.5f;   // 近くを通り過ぎると音の高さが変わる
                ea.volume = 0.12f;
                c.engineAudio = ea;
                if (Mathf.Approximately(c.engineVolume, 0f)) c.engineVolume = 0.35f;
            }
            if (Mathf.Approximately(c.cmElevator, 0.4f)) c.cmElevator = 0.24f;   // 昇降舵を実機並みに
            // 速度が出すぎないように（最高速 約200km/h）
            if (Mathf.Approximately(c.parasiteDrag, 0.027f)) c.parasiteDrag = 0.035f;
            if (Mathf.Approximately(c.propMaxSpeed, 110f)) c.propMaxSpeed = 90f;
            if (Mathf.Approximately(c.minEngineFactor, 0.35f)) c.minEngineFactor = 0.2f;
            c.respawnPoint = respawn;
            c.homePool = null;
            if (pool != null && pool.Pool != null && c.car != null)
            {
                foreach (var g in pool.Pool) if (g == c.car) { c.homePool = pool; pooled++; break; }
            }
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        }
        log.AppendLine("墜落: 爆発エフェクト作成 / 格納庫に戻す機体 " + pooled + " 機 / 戻す場所 " + (respawn != null ? respawn.position.ToString("F0") : "なし"));
    }

    // ---------------- ミサイル ----------------
    static void SetupMissiles(Material fxMat, Texture2D tex, StringBuilder log)
    {
        GameObject old = GameObject.Find("MissilePool");
        if (old != null) Undo.DestroyObjectImmediate(old);
        GameObject oldS = GameObject.Find("MissileSplash");
        if (oldS != null) Undo.DestroyObjectImmediate(oldS);

        // 海に落ちた時の大きな水柱（機銃の水柱より高く太い）
        Shader alphaSh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        Material waterMat = LoadOrCreateMat("WaterFx", alphaSh);
        var splash = MakePS("MissileSplash", null, waterMat);
        splash.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var sm = splash.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(18f, 40f);
        sm.startSize = new ParticleSystem.MinMaxCurve(2f, 6f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f, 0.85f), new Color(1f, 1f, 1f, 0.95f));
        sm.gravityModifier = 1f;
        sm.maxParticles = 800;
        var ss = splash.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 10f;
        ss.radius = 2f;
        var sol = splash.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));
        SetFade(splash, Color.white, new Color(0.8f, 0.88f, 0.95f));

        // 煙の尾のマテリアル
        Material smoke = LoadOrCreateMat("MissileSmoke", alphaSh);
        smoke.SetTexture("_MainTex", tex);
        smoke.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(smoke);
        // 本体のマテリアル
        Material body = LoadOrCreateMat("Missile", Shader.Find("Standard"));
        body.SetColor("_Color", new Color(0.55f, 0.58f, 0.6f));
        body.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(body);
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/missile.wav");

        var pool = new GameObject("MissilePool");
        Undo.RegisterCreatedObjectUndo(pool, "Flight setup");
        int count = 0;
        int planeNo = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car))
            {
                // 人が乗らない機体（敵のドローン）にはミサイルを作らない（シーンを軽く）
                Undo.RecordObject(c, "Flight setup");
                c.missiles = new Transform[0];
                UdonSharpEditorUtility.CopyProxyToUdon(c);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                continue;
            }
            planeNo++;
            var arr = new Transform[24];   // 無誘導 16 + ロックオン 8 が同時に飛べる数
            for (int k = 0; k < arr.Length; k++)
            {
                var m = new GameObject("Missile_" + planeNo + "_" + (k + 1));
                m.transform.SetParent(pool.transform, false);

                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Body";
                cyl.transform.SetParent(m.transform, false);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 円柱の軸を前(+Z)に
                cyl.transform.localScale = new Vector3(0.22f, 0.9f, 0.22f);     // 長さ 1.8m
                var cr = cyl.GetComponent<MeshRenderer>();
                cr.sharedMaterial = body;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // 噴射の炎
                var flame = MakePS("Flame", m.transform, fxMat);
                flame.transform.localPosition = new Vector3(0f, 0f, -1f);
                var fm = flame.main;
                fm.loop = true;
                fm.playOnAwake = true;
                fm.simulationSpace = ParticleSystemSimulationSpace.Local;
                fm.startLifetime = 0.12f;
                fm.startSpeed = 0f;
                fm.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f), new Color(1f, 0.9f, 0.5f));
                fm.maxParticles = 30;
                var fe = flame.emission;
                fe.rateOverTime = 80f;
                var fs = flame.shape;
                fs.enabled = false;

                // 白い煙の尾
                var tr = m.AddComponent<TrailRenderer>();
                tr.sharedMaterial = smoke;
                tr.time = 2.5f;
                tr.minVertexDistance = 1.5f;
                tr.widthCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 3f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
                          new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;

                // 噴射の音（ミサイルと一緒に動く）
                var au = m.AddComponent<AudioSource>();
                au.clip = clip;
                au.playOnAwake = false;
                au.spatialBlend = 1f;
                au.minDistance = 10f;
                au.maxDistance = 800f;
                au.dopplerLevel = 0.6f;

                m.SetActive(false);
                arr[k] = m.transform;
                count++;
            }
            Undo.RecordObject(c, "Flight setup");
            c.missiles = arr;
            c.maxMissiles = 16;
            c.maxGuided = 8;
            c.missileSplashFx = splash;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        }
        log.AppendLine("ミサイル: " + count + " 発ぶんの見た目を作成（" + planeNo + " 機 × 24）");
    }

    // 爆発の見た目（s = 大きさの倍率）
    static void ConfigureExplosion(ParticleSystem ex, float s, int count)
    {
        var main = ex.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f * Mathf.Pow(s, 0.7f), 22f * Mathf.Pow(s, 0.7f));
        main.startSize = new ParticleSystem.MinMaxCurve(3f * s, 8f * s);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.3f));
        main.gravityModifier = -0.1f;
        main.maxParticles = count + 20;
        var em = ex.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ex.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 2f * s;
        SetFade(ex, new Color(1f, 0.9f, 0.5f), new Color(0.6f, 0.15f, 0.05f));
        var sol = ex.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
    }

    // ---------------- 撃てる物（潜水艦・建物・戦闘機） ----------------
    static void SetupObjectTargets(Material fxMat, AudioClip boom, StringBuilder log)
    {
        int n = 0;
        var sb = new StringBuilder();

        // 動いている潜水艦
        var mover = Object.FindObjectOfType<Control_submarine>(true);
        if (mover != null && mover.submarine != null && AddObjectTarget(mover.submarine, 40, 60f, fxMat, boom)) { n++; sb.Append(" 潜水艦"); }

        // 島の建物・戦闘機（元のモデル・プレハブの種類で探す）
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path)) continue;
            int hp = 0;
            float respawn = 30f;
            if (path.StartsWith("Assets/Houses_Pack/Models/")) hp = 10;
            else if (path == "Assets/My assets/ie.fbx") hp = 6;
            else if (path == "Assets/My assets/torii.fbx") hp = 5;
            else if (path.StartsWith("Assets/Raptor3D/") && path.EndsWith(".prefab")) hp = 8;
            if (hp == 0) continue;
            if (AddObjectTarget(go, hp, respawn, fxMat, boom)) { n++; sb.Append(" " + go.name); }
        }
        log.AppendLine("撃てる物: " + n + " 個追加:" + sb);
    }

    static bool AddObjectTarget(GameObject go, int hp, float respawn, Material fxMat, AudioClip boom)
    {
        if (go.GetComponent<FlightTarget>() != null) return false;

        var rends = new System.Collections.Generic.List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            rends.Add(r);
        }
        if (rends.Count == 0) return false;
        Bounds bd = rends[0].bounds;
        foreach (var r in rends) bd.Encapsulate(r.bounds);

        var cols = new System.Collections.Generic.List<Collider>();
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
        if (cols.Count == 0)
        {
            // 当たり判定が無い物には箱を付ける（弾が当たるように）
            var bc = Undo.AddComponent<BoxCollider>(go);
            Transform tr = go.transform;
            bc.center = tr.InverseTransformPoint(bd.center);
            Vector3 sz = tr.InverseTransformVector(bd.size);
            bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            cols.Add(bc);
        }

        float s = Mathf.Clamp(bd.extents.magnitude / 8f, 1f, 20f);
        var ex = MakePS("Explosion", go.transform, fxMat);
        ex.transform.position = bd.center;
        ConfigureExplosion(ex, s, 60);
        var au = ex.gameObject.AddComponent<AudioSource>();
        au.playOnAwake = false;
        au.clip = boom;
        au.spatialBlend = 1f;
        au.minDistance = 20f * Mathf.Sqrt(s);
        au.maxDistance = 2500f;
        au.volume = 1f;

        var t = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(go);
        t.renderers = rends.ToArray();
        t.colliders = cols.ToArray();
        t.explosionFx = ex;
        t.explosionAudio = au;
        t.maxHp = hp;
        t.respawnTime = respawn;
        UdonSharpEditorUtility.CopyProxyToUdon(t);
        return true;
    }

    // ---------------- R35 をプールに（ドローンの代わり） ----------------
    const int R35PoolSize = 4;

    static bool IsR35(GameObject go)
    {
        if (go == null) return false;
        return go.transform.Find("Dropship_R35-Engine1") != null || go.name.StartsWith("Dropship_R35") || go.name.StartsWith("R35_") || IsOmega(go);
    }

    // R35 の代わりの Omega FighterG（OmegaBody という見た目を持つ機体）
    static bool IsOmega(GameObject go)
    {
        return go != null && go.transform.Find("OmegaBody") != null;
    }

    const string OmegaFbx = "Assets/Omega_fighterG/Meshes/fighter_black.FBX";
    const float OmegaLength = 14f;   // 機体の長さ（m）。R35 と同じくらい

    // プールの 1 機（R35_4）を Omega FighterG の見た目にする。
    // 見た目の向き・大きさ・椅子の位置は「最初の 1 回だけ」自動で置く（あとで手で直した物は上書きしない）
    static void SetupOmegaTrial(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.dronePool == null || putOut.dronePool.Pool == null) return;
        GameObject target = null;
        foreach (var g in putOut.dronePool.Pool) if (g != null && IsOmega(g)) { target = g; break; }
        bool fresh = target == null;
        if (fresh)
        {
            foreach (var g in putOut.dronePool.Pool) if (g != null && g.name == "R35_4") { target = g; break; }
            if (target == null)
            {
                var pl = putOut.dronePool.Pool;
                if (pl.Length > 0) target = pl[pl.Length - 1];
            }
        }
        if (target == null) { log.AppendLine("Omega: プールに機体が無い"); return; }
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(OmegaFbx);
        if (fbx == null) { log.AppendLine("Omega: " + OmegaFbx + " が見つからない"); return; }
        Transform car = target.transform;
        Undo.RecordObject(target, "Flight setup");
        target.name = "Omega_1";

        // R35 の見た目はしまう（消さない）。R35 の部品の動きも止める
        var rootMr = target.GetComponent<MeshRenderer>();
        if (rootMr != null) { Undo.RecordObject(rootMr, "Flight setup"); rootMr.enabled = false; }
        foreach (Transform ch in car)
            if (ch.name.StartsWith("Dropship")) { Undo.RecordObject(ch.gameObject, "Flight setup"); ch.gameObject.SetActive(false); }
        var parts = target.GetComponent<R35Parts>();
        if (parts != null)
        {
            UdonBehaviour pub = UdonSharpEditorUtility.GetBackingUdonBehaviour(parts);
            Undo.DestroyObjectImmediate(parts);
            if (pub != null) Undo.DestroyObjectImmediate(pub);
        }

        controller c = target.GetComponentInChildren<controller>(true);
        Transform axis = car.Find("FlightAxis");
        Vector3 fwd = axis != null ? axis.forward : car.forward;
        Vector3 up = axis != null ? axis.up : car.up;
        var bc = target.GetComponent<BoxCollider>();
        Vector3 center = bc != null ? car.TransformPoint(bc.center) : car.position;

        if (fresh)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(fbx, car);
            Undo.RegisterCreatedObjectUndo(body, "Flight setup");
            body.name = "OmegaBody";
            body.transform.localScale = Vector3.one;
            body.transform.rotation = Quaternion.identity;
            // 一番長い水平の向きを機首の向きにそろえる
            Bounds lb;
            MeshBounds(body.transform, "", out lb);
            Vector3 sz = lb.size;
            bool alongX = sz.x > sz.z;
            float len = Mathf.Max(sz.x, sz.z);
            body.transform.rotation = Quaternion.LookRotation(fwd, up) * (alongX ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity);
            float sc = len > 0.01f ? OmegaLength / len : 1f;
            body.transform.localScale = Vector3.one * sc;   // 測った長さは親の大きさ込みなので、そのまま掛ける
            Bounds wb;
            MeshBounds(body.transform, "", out wb);
            body.transform.position += center - wb.center;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // 椅子: 操縦席（Cockipt）の真ん中あたりに仮置き（あとで手で直してください）
            Transform cp = null;
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (t.name.ToLower().Contains("cockipt") || t.name.ToLower().Contains("cockpit")) { cp = t; break; }
            Bounds cb;
            if (c != null)
            {
                Undo.RecordObject(c.transform, "Flight setup");
                Vector3 seatPos = (cp != null && PartBounds(cp, out cb)) ? cb.center - up * 0.35f : wb.center + fwd * (wb.extents.magnitude * 0.25f) + up * 0.3f;
                c.transform.position = seatPos;
            }
            // 機銃を機首の先へ
            Transform gun = car.Find("Gun");
            if (gun != null)
            {
                Undo.RecordObject(gun, "Flight setup");
                gun.position = wb.center + fwd * (OmegaLength * 0.5f + 1f);
            }
            log.AppendLine("Omega: " + target.name + " を Omega FighterG にしました（元の長さ " + len.ToString("F1") + " → " + OmegaLength + "m、" + (alongX ? "X" : "Z") + " 軸を機首の向きに）。椅子は仮置きです");
        }
        else
        {
            log.AppendLine("Omega: " + target.name + "（設定済み。見た目と椅子の位置は手で直した物をそのまま使う）");
            CopyOmegaToPool(putOut, target, log);
        }
    }

    // Omega_1（手で直した見た目・椅子・砲塔の位置ごと）を複製して、プールの残りの R35 と入れ替える
    static void CopyOmegaToPool(PutOutDrone putOut, GameObject tmpl, StringBuilder log)
    {
        var pool = putOut.dronePool;
        var arr = pool.Pool;
        int made = 0;
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var g in arr) if (g != null) names.Add(g.name);
        for (int i = 0; i < arr.Length; i++)
        {
            GameObject g = arr[i];
            if (g == null || IsOmega(g)) continue;
            var copy = Object.Instantiate(tmpl, g.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Flight setup");
            copy.transform.SetPositionAndRotation(g.transform.position, g.transform.rotation);
            copy.transform.localScale = tmpl.transform.localScale;
            copy.transform.SetSiblingIndex(g.transform.GetSiblingIndex());
            int n = 2;
            while (names.Contains("Omega_" + n)) n++;
            copy.name = "Omega_" + n;
            names.Add(copy.name);
            copy.SetActive(g.activeSelf);
            log.AppendLine("Omega: " + g.name + " を " + copy.name + " に入れ替え（Omega_1 の複製）");
            names.Remove(g.name);
            Undo.DestroyObjectImmediate(g);
            arr[i] = copy;
            made++;
        }
        if (made > 0)
        {
            Undo.RecordObject(pool, "Flight setup");
            pool.Pool = arr;
            EditorUtility.SetDirty(pool);

        }
        // プールの外に残っている R35（椅子付き）も数える
        int left = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && IsR35(c.car) && !IsOmega(c.car)) left++;
        log.AppendLine("Omega: プール " + arr.Length + " 機すべて Omega（今回 " + made + " 機を入れ替え）。プール外の R35: " + left + " 機");
    }

    // Omega: 操縦士の機銃も、機体の下の旋回砲塔（2 本の砲身の先）から撃つ。
    // 操縦士の銃口は砲塔とは別に持つので、ガンナーが砲塔を回していても「＋」の方へ撃てる
    static void PlaceTurretGuns(controller c, Transform turret, StringBuilder log)
    {
        if (c == null || c.car == null || turret == null) return;
        Transform car = c.car.transform;
        Transform gun = car.Find("Gun");
        if (gun == null) return;
        Vector3 left = turret.TransformPoint(new Vector3(-0.13f, 0f, 1.6f));
        Vector3 right = turret.TransformPoint(new Vector3(0.13f, 0f, 1.6f));
        Undo.RecordObject(gun, "Flight setup");
        gun.SetPositionAndRotation(left, turret.rotation);
        Transform gunR = car.Find("Gun_R");
        if (gunR == null)
        {
            var g2 = Object.Instantiate(gun.gameObject, car);
            g2.name = "Gun_R";
            Undo.RegisterCreatedObjectUndo(g2, "Flight setup");
            gunR = g2.transform;
            Transform sight = gunR.Find("GunSight");
            if (sight != null) Object.DestroyImmediate(sight.gameObject);   // 照準は 1 つだけ
            var au2 = gunR.GetComponent<AudioSource>();
            if (au2 != null) Object.DestroyImmediate(au2);                  // 音は片方の銃から
        }
        gunR.SetPositionAndRotation(right, turret.rotation);
        Undo.RecordObject(c, "Flight setup");
        c.gunMuzzle = gun;
        c.gunMuzzle2 = gunR;
        Transform tr2 = gunR.Find("Tracer");
        c.tracerFx2 = tr2 != null ? tr2.GetComponent<ParticleSystem>() : null;
        UdonSharpEditorUtility.CopyProxyToUdon(c);
        log.AppendLine("Omega: 操縦士の機銃を機体の下の砲塔の砲身の先に");
    }
}
