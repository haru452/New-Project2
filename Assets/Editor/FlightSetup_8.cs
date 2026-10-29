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
    static void SetupWarShips(StringBuilder log)
    {
        var ships = new System.Collections.Generic.List<GameObject>();
        var tankers = new System.Collections.Generic.List<GameObject>();
        var tankerWs = new System.Collections.Generic.List<WarShip>();
        var escortCand = new System.Collections.Generic.List<WarShip>();
        var halfLens = new System.Collections.Generic.Dictionary<WarShip, float>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path)) continue;
            if (path.Contains("Nokobot/ColonialShip/02_Prefabs/Colonial Ship"))
            {
                // 前の敵の船（Colonial Ship）はしまう
                Transform w0 = go.transform.parent;
                GameObject hide = (w0 != null && w0.GetComponent<WarShip>() != null) ? w0.gameObject : go;
                Undo.RecordObject(hide, "Flight setup");
                hide.SetActive(false);
                continue;
            }
            if (path.Contains("Simple Boats and Tankers/Prefabs/Tanker ship"))
            {
                // 貨物船。別のプレハブの中に入っている物は中から動かせないので、同じ場所に新しく置いて元は非表示
                Transform par = go.transform.parent;
                if (par != null && PrefabUtility.IsPartOfPrefabInstance(par.gameObject))
                {
                    if (!go.activeInHierarchy) continue;   // 置き換え済み
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var nt = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    Undo.RegisterCreatedObjectUndo(nt, "Flight setup");
                    nt.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                    nt.transform.localScale = go.transform.lossyScale;
                    nt.name = go.name + " cargo";
                    Undo.RecordObject(go, "Flight setup");
                    go.SetActive(false);
                    ships.Add(nt);
                    tankers.Add(nt);
                    log.AppendLine("貨物船: " + go.name + " はプレハブの中にあったので、同じ場所に " + nt.name + " を置いて元は非表示に");
                    continue;
                }
                ships.Add(go);
                tankers.Add(go);
                continue;
            }
            if (!path.Contains("Military vehicles (Sea)/Prefab/")) continue;
            ships.Add(go);
        }
        if (ships.Count == 0) { log.AppendLine("船: シーンに Military vehicles (Sea) の船が見つからない"); return; }

        // 空母 = 名前に carrier / 空母 があるもの。無ければ一番長い種類の船
        string carrierPrefab = null;
        float longest = 0f;
        foreach (var sh in ships)
        {
            Bounds sb2;
            if (!MeshBounds(sh.transform, "", out sb2)) continue;
            float L = Mathf.Max(sb2.size.x, sb2.size.z);
            log.AppendLine("  船の種類: " + sh.name + " (" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sh).Replace("Assets/Military vehicles (Sea)/Prefab/", "") + ") 長さ " + L.ToString("F0") + "m");
            if (L > longest) { longest = L; carrierPrefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sh); }
        }
        var putOutS = Object.FindObjectOfType<PutOutDrone>(true);
        Vector3 avoidPos = (putOutS != null && putOutS.catapultPoint != null) ? putOutS.catapultPoint.position : new Vector3(254f, 0f, 297f);
        var rnd = new System.Random(12345);

        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        var splashGo = GameObject.Find("MissileSplash");
        var crashGo = GameObject.Find("CrashExplosion");
        var planes = Object.FindObjectsOfType<controller>(true);
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;

        // 空母（S10）の場所: まわりの艦隊（2km 以内）は空母と一緒に動く
        Vector3 cvPos0 = new Vector3(1e9f, 0f, 1e9f);
        foreach (var sh in ships) if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sh).EndsWith("/S10.prefab")) { cvPos0 = sh.transform.position; break; }
        int n = 0;
        foreach (var ship in ships)
        {
            n++;
            // 船を包む親を作る（耐久値・大砲・表示の切り替えはこの親に付ける。見た目は子の船）
            Transform wrap = ship.transform.parent;
            if (wrap == null || wrap.GetComponent<WarShip>() == null)
            {
                var w = new GameObject("WarShip_" + n);
                Undo.RegisterCreatedObjectUndo(w, "Flight setup");
                w.transform.SetParent(ship.transform.parent, false);
                w.transform.position = ship.transform.position;
                w.transform.rotation = Quaternion.identity;
                Undo.SetTransformParent(ship.transform, w.transform, "Flight setup");
                wrap = w.transform;
            }
            else
            {
                // 作り直し（自分で付けた部品だけ消す）
                foreach (var name in new[] { "Muzzles", "Shells", "CannonFx", "Rockets", "RocketLauncher", "Explosion", "AAGun" })
                {
                    Transform old;
                    int guard = 0;
                    while ((old = wrap.Find(name)) != null && guard++ < 50) Undo.DestroyObjectImmediate(old.gameObject);   // 以前の実行で重なった分も消す
                }
                foreach (var c in wrap.GetComponents<Component>())
                {
                    if (c is Transform) continue;
                    if (c is UdonBehaviour || c is UdonSharpBehaviour || c is AudioSource) Undo.DestroyObjectImmediate(c);
                }
            }

            // 戦艦の大きさは全部 scale 2 にそろえる（貨物船は除く）
            if (!tankers.Contains(ship) && (ship.transform.localScale - Vector3.one * 2f).sqrMagnitude > 0.0001f)
            {
                Undo.RecordObject(ship.transform, "Flight setup");
                log.AppendLine("船の大きさ: " + ship.name + "（" + wrap.name + "）scale " + ship.transform.localScale.x.ToString("0.##") + " → 2");
                ship.transform.localScale = Vector3.one * 2f;
            }

            // 見た目の大きさ
            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) rends.Add(r);
            if (rends.Count == 0) { log.AppendLine("船: 見た目が見つからない " + ship.name); continue; }
            Bounds bd = rends[0].bounds;
            foreach (var r in rends) bd.Encapsulate(r.bounds);

            // 当たり判定（無ければ船体の箱。帆の部分は除いて低めにする）
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var c in ship.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
            if (cols.Count == 0)
            {
                var bc = Undo.AddComponent<BoxCollider>(ship);
                Transform st = ship.transform;
                Vector3 hullCenter = new Vector3(bd.center.x, bd.min.y + bd.size.y * 0.3f, bd.center.z);
                Vector3 hullSize = new Vector3(bd.size.x, bd.size.y * 0.6f, bd.size.z);
                bc.center = st.InverseTransformPoint(hullCenter);
                Vector3 sz = st.InverseTransformVector(hullSize);
                bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                cols.Add(bc);
            }

            // 船の長い向きと横向き
            bool alongX = bd.size.x >= bd.size.z;
            Vector3 lengthDir = alongX ? Vector3.right : Vector3.forward;
            Vector3 sideDir = alongX ? Vector3.forward : Vector3.right;
            float halfLen = (alongX ? bd.size.x : bd.size.z) * 0.5f;
            float halfWidth = (alongX ? bd.size.z : bd.size.x) * 0.5f * 0.45f;   // 帆桁を除いた船体の幅（およそ）
            float deckY = Mathf.Max(seaY + 3f, bd.min.y + 2f);

            // 砲門（左右に 3 門ずつ）
            var muzzles = new GameObject("Muzzles").transform;
            muzzles.SetParent(wrap, false);
            var mlist = new System.Collections.Generic.List<Transform>();
            for (int side = -1; side <= 1; side += 2)
            for (int k = 0; k < 3; k++)
            {
                float f = -0.5f + k * 0.5f;   // 前・中・後ろ
                Vector3 p = new Vector3(bd.center.x, deckY, bd.center.z) + lengthDir * (f * halfLen * 0.8f) + sideDir * (side * halfWidth);
                var m = new GameObject("Cannon_" + (side < 0 ? "L" : "R") + (k + 1)).transform;
                m.SetParent(muzzles, false);
                m.position = p;
                m.rotation = Quaternion.LookRotation(sideDir * side, Vector3.up);
                mlist.Add(m);
            }

            // 砲弾（6 発ぶん）
            var shellRoot = new GameObject("Shells").transform;
            shellRoot.SetParent(wrap, false);
            Material ballMat = LoadOrCreateMat("CannonBall", Shader.Find("Standard"));
            ballMat.SetColor("_Color", new Color(0.12f, 0.12f, 0.12f));
            EditorUtility.SetDirty(ballMat);
            var shells = new Transform[6];
            for (int k = 0; k < shells.Length; k++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(b.GetComponent<Collider>());
                b.name = "Shell_" + (k + 1);
                b.transform.SetParent(shellRoot, false);
                b.transform.localScale = Vector3.one * 0.6f;
                var br = b.GetComponent<MeshRenderer>();
                br.sharedMaterial = ballMat;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var tr = b.AddComponent<TrailRenderer>();
                tr.sharedMaterial = smokeMat;
                tr.time = 0.6f;
                tr.minVertexDistance = 1f;
                tr.widthCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 0f), new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 1f) },
                          new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.SetActive(false);
                shells[k] = b.transform;
            }

            // 追尾しないミサイル（3 発ぶん）と発射位置（甲板の中央の少し上）
            var launcher = new GameObject("RocketLauncher").transform;
            launcher.SetParent(wrap, false);
            launcher.position = new Vector3(bd.center.x, deckY + 8f, bd.center.z);
            var rocketRoot = new GameObject("Rockets").transform;
            rocketRoot.SetParent(wrap, false);
            Material rocketMat = LoadOrCreateMat("ShipRocket", Shader.Find("Standard"));
            rocketMat.SetColor("_Color", new Color(0.55f, 0.15f, 0.12f));
            EditorUtility.SetDirty(rocketMat);
            AudioClip mslClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/missile.wav");
            var rockets = new Transform[3];
            for (int k = 0; k < rockets.Length; k++)
            {
                var rk = new GameObject("Rocket_" + (k + 1));
                rk.transform.SetParent(rocketRoot, false);
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Body";
                cyl.transform.SetParent(rk.transform, false);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cyl.transform.localScale = new Vector3(0.35f, 1.2f, 0.35f);   // 長さ 2.4m
                var cr = cyl.GetComponent<MeshRenderer>();
                cr.sharedMaterial = rocketMat;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var flame = MakePS("Flame", rk.transform, fxMat);
                flame.transform.localPosition = new Vector3(0f, 0f, -1.3f);
                var flm = flame.main;
                flm.loop = true;
                flm.playOnAwake = true;
                flm.simulationSpace = ParticleSystemSimulationSpace.Local;
                flm.startLifetime = 0.12f;
                flm.startSpeed = 0f;
                flm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
                flm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.45f));
                flm.maxParticles = 30;
                var fle = flame.emission;
                fle.rateOverTime = 80f;
                var fls = flame.shape;
                fls.enabled = false;
                var rtr = rk.AddComponent<TrailRenderer>();
                rtr.sharedMaterial = smokeMat;
                rtr.time = 3f;
                rtr.minVertexDistance = 2f;
                rtr.widthCurve = AnimationCurve.Linear(0f, 0.9f, 1f, 4f);
                var rg = new Gradient();
                rg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1f) },
                           new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
                rtr.colorGradient = rg;
                rtr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var rau = rk.AddComponent<AudioSource>();
                rau.clip = mslClip;
                rau.playOnAwake = false;
                rau.spatialBlend = 1f;
                rau.minDistance = 15f;
                rau.maxDistance = 1500f;
                rk.SetActive(false);
                rockets[k] = rk.transform;
            }

            // 対空機銃（艦の上・ミサイル発射位置の少し上。赤い曳光弾）
            var aa = new GameObject("AAGun").transform;
            aa.SetParent(wrap, false);
            aa.position = new Vector3(bd.center.x, deckY + 12f, bd.center.z);
            var aaTr = MakePS("Tracer", aa, fxMat);
            var aam = aaTr.main;
            aam.startLifetime = 2f;
            aam.startSpeed = 600f;
            aam.startSize = 0.5f;
            aam.startColor = new Color(1f, 0.3f, 0.2f, 1f);
            aam.maxParticles = 200;
            var aash = aaTr.shape;
            aash.enabled = false;
            var aar = aaTr.GetComponent<ParticleSystemRenderer>();
            aar.renderMode = ParticleSystemRenderMode.Stretch;
            aar.velocityScale = 0.02f;
            aar.lengthScale = 2f;
            var aaAu = aa.gameObject.AddComponent<AudioSource>();
            aaAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
            aaAu.playOnAwake = false;
            aaAu.spatialBlend = 1f;
            aaAu.minDistance = 20f;
            aaAu.maxDistance = 1500f;

            // 発射の閃光と煙
            var fxRoot = new GameObject("CannonFx").transform;
            fxRoot.SetParent(wrap, false);
            var flash = MakePS("Flash", fxRoot, fxMat);
            var fm = flash.main;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 15f);
            fm.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.9f, 0.5f));
            fm.maxParticles = 40;
            var fe = flash.emission;
            fe.SetBursts(new[] { new ParticleSystem.Burst(0f, 15) });
            var fs = flash.shape;
            fs.enabled = true;
            fs.shapeType = ParticleSystemShapeType.Cone;
            fs.angle = 15f;
            fs.radius = 0.3f;
            var smoke = MakePS("Smoke", flash.transform, smokeMat);
            var sm = smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 10f);
            sm.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.8f, 0.8f, 0.7f), new Color(0.6f, 0.6f, 0.6f, 0.8f));
            sm.gravityModifier = -0.05f;
            sm.maxParticles = 60;
            var se = smoke.emission;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
            var ss = smoke.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 20f;
            ss.radius = 0.5f;
            var sol = smoke.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            SetFade(smoke, new Color(0.85f, 0.85f, 0.85f), new Color(0.6f, 0.6f, 0.6f));

            // 発射音
            var au = wrap.gameObject.AddComponent<AudioSource>();
            au.clip = boom;
            au.playOnAwake = false;
            au.spatialBlend = 1f;
            au.minDistance = 30f;
            au.maxDistance = 1500f;

            // 耐久値（機銃・ミサイルで壊せる）
            var target = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(wrap.gameObject);
            target.renderers = rends.ToArray();
            target.colliders = cols.ToArray();
            target.maxHp = 40;
            target.respawnTime = 60f;
            target.missileDamage = 15;
            var ex = MakePS("Explosion", wrap, fxMat);
            ex.transform.position = bd.center;
            ConfigureExplosion(ex, Mathf.Clamp(bd.extents.magnitude / 8f, 1f, 12f), 60);
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 40f;
            exAu.maxDistance = 2500f;
            target.explosionFx = ex;
            target.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(target);

            // 大砲・表示の切り替え
            var ws = UdonSharpEditor.UdonSharpUndo.AddComponent<WarShip>(wrap.gameObject);
            ws.planes = planes;
            ws.model = ship;
            ws.target = target;
            ws.cannonMuzzles = mlist.ToArray();
            ws.shells = shells;
            ws.rockets = rockets;
            ws.aaMuzzle = aa;
            ws.aaTracer = aaTr;
            ws.aaAudio = aaAu;
            ws.rocketLauncher = launcher;
            ws.muzzleFx = flash;
            ws.cannonAudio = au;
            ws.splashFx = splashGo != null ? splashGo.GetComponent<ParticleSystem>() : null;
            ws.impactFx = crashGo != null ? crashGo.GetComponent<ParticleSystem>() : null;
            ws.impactAudio = crashGo != null ? crashGo.GetComponent<AudioSource>() : null;
            ws.seaLevel = seaY;

            // 種類（空母か）と、航行（名前に fleet が付いた船・艦隊は止まったまま）
            string lname = ship.name.ToLower();
            bool hinted = lname.Contains("carrier") || ship.name.Contains("空母");
            string spath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ship);
            bool isCarrier = hinted || spath.EndsWith("/S10.prefab");   // S10 が空母
            target.kind = isCarrier ? 2 : 1;
            if (isCarrier) target.maxHp = 80;
            ws.appearDelay = isCarrier ? CarrierAppearDelay : 0f;   // 空母はワールドが始まって 10 分後に出てくる
            UdonSharpEditorUtility.CopyProxyToUdon(target);
            bool fleet = false;
            for (Transform a = ship.transform; a != null; a = a.parent) if (a.name.ToLower().Contains("fleet")) { fleet = true; break; }
            ws.path = new Vector3[0];
            bool isTanker = tankers.Contains(ship);
            ws.roaming = !fleet && !isTanker;   // fleet 以外は、ワールドが始まるたび・沈むたびにランダムな海へ（貨物船は護衛に付いて行く）
            halfLens[ws] = halfLen;
            if (isTanker)
            {
                target.kind = 3;            // 貨物船（1 点）
                target.maxHp = 25;
                target.respawnTime = 120f;  // 撃破されたら 2 分後に復活（護衛が残っていれば、その後ろに）
                ws.unarmed = true;
                tankerWs.Add(ws);
                // 貨物船（タンカー）は海の上に置いてあって浮いていたので、船底を海面の 12m 下に（喫水）
                Bounds tb;
                if (MeshBounds(ship.transform, "", out tb))
                {
                    float dy = (ws.seaLevel - 12f) - tb.min.y;
                    if (Mathf.Abs(dy) > 0.5f)
                    {
                        Undo.RecordObject(ws.transform, "Flight setup");
                        ws.transform.position += Vector3.up * dy;
                        log.AppendLine("貨物船: " + ship.name + " を " + dy.ToString("F1") + "m 動かして海に浮かべました");
                    }
                }
                UdonSharpEditorUtility.CopyProxyToUdon(target);
            }
            else if (ws.roaming && !isCarrier && spath.EndsWith("/S2.prefab")) escortCand.Add(ws);
            ws.avoidCenter = avoidPos;
            ws.avoidRadius = ShipAvoid;
            ws.pathSpeed = isCarrier ? 6f : 8f;
            ws.yawOffset = Mathf.Atan2(lengthDir.x, lengthDir.z) * Mathf.Rad2Deg;
            Vector3 dcv = ship.transform.position - cvPos0; dcv.y = 0f;
            bool cvGroup = fleet && dcv.magnitude < 2000f;   // 空母の艦隊（空母と一緒に動く）
            if (!fleet || cvGroup)
            {
                var wrb = wrap.GetComponent<Rigidbody>();
                if (wrb == null) wrb = Undo.AddComponent<Rigidbody>(wrap.gameObject);
                wrb.isKinematic = true;   // 動く当たり判定は kinematic の方が軽い
                wrb.useGravity = false;
            }
            foreach (var r in rends) { Undo.RecordObject(r, "Flight setup"); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }   // 軽量化

            // 演出（船の見た目の子に入れるので、遠くて船が非表示の時は一緒に止まる）
            foreach (var nm in new[] { "ShipWake", "ShipDamage" })
            {
                Transform oldF; int g2 = 0;
                while ((oldF = ship.transform.Find(nm)) != null && g2++ < 10) Undo.DestroyObjectImmediate(oldF.gameObject);
            }
            Material foamMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/WaterFx.mat");
            if (foamMat == null) foamMat = smokeMat;
            var wakeList = new System.Collections.Generic.List<ParticleSystem>();
            if (ws.roaming || isTanker || cvGroup)
            {
                var wr = new GameObject("ShipWake").transform;
                wr.SetParent(ship.transform, false);
                // 船の後ろの航跡（海面に平らに広がる白い泡）
                var wake = MakePS("Wake", wr, foamMat);
                wake.transform.position = new Vector3(bd.center.x, seaY + 0.6f, bd.center.z) - lengthDir * halfLen * 0.95f;
                var wm = wake.main;
                wm.loop = true;
                wm.startLifetime = new ParticleSystem.MinMaxCurve(7f, 10f);
                wm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
                wm.startSize = halfWidth * 2.2f;
                wm.startColor = new Color(1f, 1f, 1f, 0.55f);
                wm.maxParticles = 400;
                var we = wake.emission;
                we.rateOverTime = 14f;
                var wsh = wake.shape;
                wsh.enabled = true;
                wsh.shapeType = ParticleSystemShapeType.Sphere;
                wsh.radius = halfWidth * 0.6f;
                var wsol = wake.sizeOverLifetime;
                wsol.enabled = true;
                wsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.8f));
                SetFade(wake, Color.white, new Color(0.85f, 0.92f, 1f));
                wake.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                wakeList.Add(wake);
                // 船首の波（左右に白く立つ）
                var bow = MakePS("BowWave", wr, foamMat);
                bow.transform.position = new Vector3(bd.center.x, seaY + 0.8f, bd.center.z) + lengthDir * halfLen * 0.9f;
                var bm = bow.main;
                bm.loop = true;
                bm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
                bm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
                bm.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
                bm.startColor = new Color(1f, 1f, 1f, 0.7f);
                bm.gravityModifier = 0.4f;
                bm.maxParticles = 200;
                var be = bow.emission;
                be.rateOverTime = 18f;
                var bsh = bow.shape;
                bsh.enabled = true;
                bsh.shapeType = ParticleSystemShapeType.Cone;
                bsh.angle = 60f;
                bsh.radius = halfWidth * 0.5f;
                bow.transform.rotation = Quaternion.LookRotation(Vector3.up);
                SetFade(bow, Color.white, new Color(0.9f, 0.95f, 1f));
                wakeList.Add(bow);
            }
            // ダメージの煙と炎（甲板の前と後ろ）
            var dr2 = new GameObject("ShipDamage").transform;
            dr2.SetParent(ship.transform, false);
            var smokes = new System.Collections.Generic.List<ParticleSystem>();
            var fires = new System.Collections.Generic.List<ParticleSystem>();
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 sp = new Vector3(bd.center.x, deckY + 4f, bd.center.z) + lengthDir * (k * halfLen * 0.3f);
                var sm2 = MakePS("Smoke", dr2, smokeMat);
                sm2.transform.position = sp;
                var smm = sm2.main;
                smm.loop = true;
                smm.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                smm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
                smm.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
                smm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.12f, 0.12f, 0.7f), new Color(0.3f, 0.3f, 0.3f, 0.6f));
                smm.gravityModifier = -0.05f;
                smm.maxParticles = 300;
                var smsh = sm2.shape; smsh.enabled = true; smsh.shapeType = ParticleSystemShapeType.Sphere; smsh.radius = 3f;
                var smsol = sm2.sizeOverLifetime; smsol.enabled = true; smsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
                SetFade(sm2, new Color(0.25f, 0.25f, 0.25f), new Color(0.55f, 0.55f, 0.55f));
                smokes.Add(sm2);
                var fi2 = MakePS("Fire", dr2, fxMat);
                fi2.transform.position = sp - Vector3.up * 2f;
                var fim = fi2.main;
                fim.loop = true;
                fim.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                fim.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
                fim.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
                fim.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
                fim.gravityModifier = -0.3f;
                fim.maxParticles = 300;
                var fish = fi2.shape; fish.enabled = true; fish.shapeType = ParticleSystemShapeType.Sphere; fish.radius = 3f;
                SetFade(fi2, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));
                fires.Add(fi2);
            }
            ws.wakeFx = wakeList.ToArray();
            ws.dmgSmoke = smokes.ToArray();
            ws.dmgFire = fires.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(ws);
            log.AppendLine("  → " + (isCarrier ? "空母" : "船") + (fleet ? "（fleet: 止まったまま）" : "（ランダムな海を航行）"));

            log.AppendLine("船: " + ship.name + " 位置 " + ship.transform.position.ToString("F0") + " 大きさ " + bd.size.ToString("F0") + " 砲門 " + mlist.Count + " / 当たり判定 " + cols.Count);
        }
        // 貨物船に護衛の戦艦（S2）を 1 隻ずつ付ける。大きさ（scale）が 2 の S2 を優先
        escortCand.Sort((x, y) => y.model.transform.localScale.x.CompareTo(x.model.transform.localScale.x));
        for (int i = 0; i < tankerWs.Count; i++)
        {
            WarShip tk = tankerWs[i];
            if (i >= escortCand.Count) { log.AppendLine("貨物船: " + tk.model.name + " に付ける S2 が足りない（止まったまま）"); continue; }
            WarShip es = escortCand[i];
            Undo.RecordObject(tk, "Flight setup");
            Undo.RecordObject(es, "Flight setup");
            tk.leader = es;
            tk.followGap = halfLens[es] + halfLens[tk] + 120f;
            es.follower = tk;
            if (es.target != null)
            {
                Undo.RecordObject(es.target, "Flight setup");
                es.target.respawnAfter = tk.target;   // 護衛は、貨物船が撃破されるまで復活しない
                UdonSharpEditorUtility.CopyProxyToUdon(es.target);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(tk);
            UdonSharpEditorUtility.CopyProxyToUdon(es);
            log.AppendLine("貨物船: " + tk.model.name + " ← 護衛 " + es.model.name + "（" + es.gameObject.name + "、scale " + es.model.transform.localScale.x.ToString("0.##") + "、" + tk.followGap.ToString("F0") + "m 後ろを付いて行く）");
        }

        // 全部の船を互いに知らせる（1 人のプレイヤーの近くに出る船の数を数えるため）
        var allWs = Object.FindObjectsOfType<WarShip>(true);
        // 空母のまわりに止まっている艦隊も、空母と一緒に 10 分後に出てくる
        WarShip cvShip = null;
        foreach (var w in allWs) if (w.target != null && w.target.kind == 2) { cvShip = w; break; }
        int cvFleet = 0;
        float fleetR = 200f;
        var cvEsc = new System.Collections.Generic.List<WarShip>();
        if (cvShip != null)
        {
            Vector3 cvPos = cvShip.model != null ? cvShip.model.transform.position : cvShip.transform.position;
            foreach (var w in allWs)
            {
                if (w == cvShip || w.roaming || w.leader != null) continue;
                Vector3 wp = w.model != null ? w.model.transform.position : w.transform.position;
                Vector3 dw = wp - cvPos; dw.y = 0f;
                if (dw.magnitude > 2000f) continue;
                Undo.RecordObject(w, "Flight setup");
                w.appearDelay = CarrierAppearDelay;
                // 艦隊: 空母の向きから見た今の位置を覚えて、その位置を保って一緒に走る
                Quaternion hd = Quaternion.Euler(0f, cvShip.transform.eulerAngles.y + cvShip.yawOffset, 0f);
                Vector3 off = Quaternion.Inverse(hd) * (w.transform.position - cvShip.transform.position);
                off.y = 0f;
                w.leader = cvShip;
                w.useFormation = true;
                w.formationOffset = off;
                fleetR = Mathf.Max(fleetR, off.magnitude);
                cvEsc.Add(w);
                cvFleet++;
            }
            // 空母: ランダムな海に現れて、ランダムな輪のコースを走る（プレイヤーの近くへ移ったりはしない）
            Undo.RecordObject(cvShip, "Flight setup");
            cvShip.roaming = true;
            cvShip.noRelocate = true;
            cvShip.pathMargin = fleetR + 250f;
            cvShip.escorts = cvEsc.ToArray();
            cvShip.path = new Vector3[0];
            log.AppendLine("空母: 戦闘が始まって " + (CarrierAppearDelay / 60f).ToString("F0") + " 分後に、まわりの艦隊 " + cvFleet + " 隻と一緒に出てくる（ランダムな海に現れ、艦隊を組んで走る。艦隊の幅 " + fleetR.ToString("F0") + "m）");
        }
        int roamN = 0;
        foreach (var w in allWs)
        {
            Undo.RecordObject(w, "Flight setup");
            w.allShips = allWs;
            if (w.roaming) roamN++;
            UdonSharpEditorUtility.CopyProxyToUdon(w);
        }
        log.AppendLine("船: 動く船 " + roamN + " 隻（1 人の近くに最大 " + ShipsPerPlayer + " 隻まで）");
    }
}
