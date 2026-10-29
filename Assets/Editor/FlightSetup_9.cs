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
    const int ShipsPerPlayer = 5;
    const float CarrierAppearDelay = 120f;   // 戦闘が始まってから空母の艦隊が出てくるまでの秒数（2 分）
    const float ShipAvoid = 2000f;   // 動く船は出撃地点の 2km 以内に入らない

    // 船のコース: 出撃地点から 10km 以上離れ、島に乗り上げない輪
    static Vector3[] MakeShipPath(Vector3 start, float seaY, Vector3 avoid, System.Random rnd, Transform self)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            int n = 6;
            var pts = new Vector3[n];
            float R = 900f + (float)rnd.NextDouble() * 1400f;
            float ca = (float)(rnd.NextDouble() * Mathf.PI * 2f);
            Vector3 c = start + new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca)) * R * 0.6f;
            // 出撃地点の 5km 以内から始まる船は、コースごと 5km の外へ出す
            Vector3 fromA = start - avoid; fromA.y = 0f;
            if (fromA.magnitude < ShipAvoid + R * 1.3f)
            {
                Vector3 dirA = fromA.sqrMagnitude > 1f ? fromA.normalized : new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca));
                if (tries > 20) dirA = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f) * dirA;
                c = new Vector3(avoid.x, start.y, avoid.z) + dirA * (ShipAvoid + R * 1.3f + 500f + (float)rnd.NextDouble() * 2000f);
            }
            float a0 = (float)(rnd.NextDouble() * Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / n + ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                float r = R * (0.7f + (float)rnd.NextDouble() * 0.5f);
                pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, start.y, c.z + Mathf.Sin(a) * r);
            }
            if (PathOk(pts, seaY, avoid, self)) return pts;
        }
        return null;
    }

    static bool PathOk(Vector3[] pts, float seaY, Vector3 avoid, Transform self)
    {
        int n = pts.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
            for (int j = 0; j < 12; j++)
            {
                float t = j / 12f, t2 = t * t, t3 = t2 * t;
                Vector3 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                Vector3 fa = q - avoid; fa.y = 0f;
                if (fa.magnitude < ShipAvoid + 300f) return false;   // 出撃地点の 5km 以内（＋余裕）
                // 島や陸（海面より高い当たり判定）があれば ×
                foreach (var h in Physics.RaycastAll(new Vector3(q.x, seaY + 400f, q.z), Vector3.down, 430f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == null || h.collider.transform.IsChildOf(self)) continue;
                    if (h.collider.GetComponentInParent<WarShip>() != null) continue;
                    if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;   // 飛行機など
                    if (h.point.y > seaY + 1.5f) return false;
                }
            }
        }
        return true;
    }

    // ---------------- 宇宙ステーションを雲で隠す ----------------
    static bool cloakRetried;

    // ---------------- 2 人目の席（ガンナー） ----------------
    const string GunnerScriptPath = "Assets/My script/drone/GunnerStation.cs";
    const string GunnerAssetPath = "Assets/My script/drone/GunnerStation.asset";

    static void SetupGunner(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(GunnerAssetPath) == null) { log.AppendLine("ERROR: GunnerStation.asset が無い"); return; }
        Texture2D tex = GetSoftTexture();
        Material fxMat = GetFxMaterial(tex);
        AudioClip gunClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
        Material metal = LoadOrCreateMat("TurretMetal", Shader.Find("Standard"));
        metal.SetColor("_Color", new Color(0.22f, 0.24f, 0.26f));
        metal.SetFloat("_Metallic", 0.6f);
        metal.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(metal);
        Material retMat = UnlitMat("GunnerReticle", new Color(0.35f, 1f, 0.5f, 0.95f), true, 3100);
        retMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        retMat.renderQueue = 3100;
        EditorUtility.SetDirty(retMat);
        Material lockMat = UnlitMat("LockRed", new Color(1f, 0.12f, 0.1f, 1f), true, 3100);

        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car) || c.gunMuzzle == null) continue;
            Transform car = c.car.transform;
            var pst = c.GetComponent<VRCStation>();
            Transform seat = c.transform;
            Transform enter = (pst != null && pst.stationEnterPlayerLocation != null) ? pst.stationEnterPlayerLocation : seat;

            // 作り直す
            Transform old = car.Find("GunnerSeat");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Transform oldT = car.Find("GunnerTurret");
            bool hadTurret = oldT != null;
            Vector3 oldTPos = hadTurret ? oldT.position : Vector3.zero;
            Quaternion oldTRot = hadTurret ? oldT.rotation : Quaternion.identity;   // 手で動かした砲塔の位置は残す（Omega）
            if (oldT != null) Undo.DestroyObjectImmediate(oldT.gameObject);

            // 席: 操縦席の 1.4m 後ろ、同じ向き
            var gsGo = new GameObject("GunnerSeat");
            Undo.RegisterCreatedObjectUndo(gsGo, "Flight setup");
            gsGo.transform.SetParent(car, false);
            gsGo.transform.SetPositionAndRotation(enter.position - seat.forward * 1.4f + seat.up * 0.1f, seat.rotation);
            SetWorldScaleOne(gsGo.transform);
            var st = Undo.AddComponent<VRCStation>(gsGo);
            st.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle;
            st.disableStationExit = true;   // 降りるのはジャンプ
            st.seated = true;
            st.canUseStationFromStation = false;
            st.stationEnterPlayerLocation = gsGo.transform;
            st.stationExitPlayerLocation = (pst != null && pst.stationExitPlayerLocation != null) ? pst.stationExitPlayerLocation : gsGo.transform;

            // 砲塔: 機首の銃の 1.2m 下（機体の下側）
            var tu = new GameObject("GunnerTurret");
            Undo.RegisterCreatedObjectUndo(tu, "Flight setup");
            tu.transform.SetParent(car, false);
            bool omegaT = IsOmega(c.car);
            Transform axT = car.Find("FlightAxis");
            if (axT == null) axT = car;
            Bounds obd;
            if (omegaT && hadTurret)
                tu.transform.SetPositionAndRotation(oldTPos, oldTRot);
            else if (omegaT && MeshBounds(car.Find("OmegaBody"), "", out obd))
                tu.transform.SetPositionAndRotation(obd.center - axT.up * (obd.extents.y * 0.9f + 0.3f) + axT.forward * 1.5f, axT.rotation);   // Omega: 胴体の下・少し前
            else
                tu.transform.SetPositionAndRotation(c.gunMuzzle.position - car.up * 1.2f - c.gunMuzzle.forward * 1.0f, c.gunMuzzle.rotation);
            SetWorldScaleOne(tu.transform);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.name = "Ball";
            ball.transform.SetParent(tu.transform, false);
            ball.transform.localScale = Vector3.one * 0.7f;
            ball.GetComponent<MeshRenderer>().sharedMaterial = metal;
            for (int k = -1; k <= 1; k += 2)
            {
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(barrel.GetComponent<Collider>());
                barrel.name = "Barrel";
                barrel.transform.SetParent(tu.transform, false);
                barrel.transform.localPosition = new Vector3(k * 0.13f, 0f, 0.8f);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.transform.localScale = new Vector3(0.09f, 0.6f, 0.09f);
                barrel.GetComponent<MeshRenderer>().sharedMaterial = metal;
            }
            var mz = new GameObject("Muzzle");
            mz.transform.SetParent(tu.transform, false);
            mz.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            var trc = MakePS("Tracer", mz.transform, fxMat);
            var main = trc.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 500f;
            main.startSize = 0.3f;
            main.startColor = new Color(0.5f, 1f, 0.6f, 1f);   // ガンナーの弾は緑
            main.maxParticles = 300;
            var shape = trc.shape;
            shape.enabled = false;
            var iv = trc.inheritVelocity;
            iv.enabled = true;
            iv.mode = ParticleSystemInheritVelocityMode.Initial;
            iv.curve = new ParticleSystem.MinMaxCurve(1f);
            var rend = trc.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.025f;
            rend.lengthScale = 2f;
            var au = mz.AddComponent<AudioSource>();
            au.playOnAwake = false;
            au.clip = gunClip;
            au.spatialBlend = 1f;
            au.minDistance = 10f;
            au.maxDistance = 900f;
            au.volume = 0.6f;
            au.dopplerLevel = 0f;

            // ガンナー本人の照準と表示（頭に付いてくる）
            var hud = new GameObject("GunnerHUD");
            hud.transform.SetParent(gsGo.transform, false);
            float g = 0.035f, th = 0.004f, z = 2f;
            Quad(hud.transform, "R1", new Vector3(g, 0f, z), new Vector2(g, th), retMat);
            Quad(hud.transform, "R2", new Vector3(-g, 0f, z), new Vector2(g, th), retMat);
            Quad(hud.transform, "R3", new Vector3(0f, g, z), new Vector2(th, g), retMat);
            Quad(hud.transform, "R4", new Vector3(0f, -g, z), new Vector2(th, g), retMat);
            Quad(hud.transform, "Dot", new Vector3(0f, 0f, z), new Vector2(th, th), retMat);
            var panel = new GameObject("Panel").transform;
            panel.SetParent(hud.transform, false);
            panel.localPosition = new Vector3(-0.30f, -0.22f, 1.0f);
            var ht = Txt(panel, "Text", "", Vector2.zero, 0.3f, 0.22f, CWhite, TextAlignmentOptions.TopLeft, 0.2f);
            hud.SetActive(false);

            var lb = new GameObject("LockBox").transform;
            lb.SetParent(gsGo.transform, false);
            const float LT = 0.05f;
            Quad(lb, "Top", new Vector3(0f, 0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
            Quad(lb, "Bottom", new Vector3(0f, -0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
            Quad(lb, "Left", new Vector3(-0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
            Quad(lb, "Right", new Vector3(0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
            lb.gameObject.SetActive(false);

            // 操縦席の背中のレーダー（後ろの席の人にだけ表示）
            var grRoot = new GameObject("GunnerRadar");
            grRoot.transform.SetParent(gsGo.transform, false);
            grRoot.transform.localPosition = Vector3.up * (0.72f + CockpitRaise);
            grRoot.transform.localRotation = Quaternion.identity;
            Material gBg = UnlitMat("CockpitPanel", new Color(0.02f, 0.07f, 0.13f, 0.72f), true, 2990);
            Material gFrame = UnlitMat("CockpitFrame", CCyan, false, 2000);
            Material gAmber = UnlitMat("CockpitAmber", CAmber, false, 2000);
            BuildRadar(c, grRoot.transform, gBg, gFrame, gAmber, true);
            grRoot.SetActive(false);
            // 警告音（操縦席と同じ音）
            AudioSource MakeLoop(string nm, string file, float vol)
            {
                var g = new GameObject(nm);
                g.transform.SetParent(gsGo.transform, false);
                var a = g.AddComponent<AudioSource>();
                a.clip = LoadClip(file);
                a.loop = true; a.playOnAwake = false; a.spatialBlend = 0f; a.volume = vol;
                return a;
            }
            var gWarn = MakeLoop("StallWarnAudio", "stall_warn.wav", 0.35f);
            var gStall = MakeLoop("StallAudio", "stall.wav", 0.4f);
            var gAlarm = MakeLoop("AlarmAudio", "alarm.wav", 0.22f);

            var gst = UdonSharpEditor.UdonSharpUndo.AddComponent<GunnerStation>(gsGo);
            gst.radarRoot = grRoot;
            if (omegaT) PlaceTurretGuns(c, tu.transform, log);
            gst.stallWarnAudio = gWarn;
            gst.stallAudio = gStall;
            gst.alarmAudio = gAlarm;
            gst.plane = c;
            gst.station = st;
            gst.turret = tu.transform;
            gst.muzzle = mz.transform;
            gst.tracer = trc;
            gst.gunAudio = au;
            gst.hudRoot = hud;
            gst.hudText = ht;
            gst.lockBox = lb;
            UdonSharpEditorUtility.CopyProxyToUdon(gst);

            // 音声を後ろの席にも流す仕組み（操縦士が持ち主になって同期）
            Transform oldR = car.Find("VoiceRelay");
            if (oldR != null) Undo.DestroyObjectImmediate(oldR.gameObject);
            var vr = new GameObject("VoiceRelay");
            Undo.RegisterCreatedObjectUndo(vr, "Flight setup");
            vr.transform.SetParent(car, false);
            var vOut = vr.AddComponent<AudioSource>();
            vOut.playOnAwake = false; vOut.spatialBlend = 0f; vOut.volume = 0.9f; vOut.priority = 10;
            var rGo = new GameObject("Radio");
            rGo.transform.SetParent(vr.transform, false);
            var rOut = rGo.AddComponent<AudioSource>();
            rOut.playOnAwake = false; rOut.spatialBlend = 0f; rOut.volume = 0.85f; rOut.priority = 5;
            var clipList = new System.Collections.Generic.List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string ap = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileName(ap);
                if (!fn.StartsWith("voice_") && !fn.StartsWith("ama_")) continue;
                var ac = AssetDatabase.LoadAssetAtPath<AudioClip>(ap);
                if (ac != null) clipList.Add(ac);
            }
            var relay = UdonSharpEditor.UdonSharpUndo.AddComponent<VoiceRelay>(vr);
            relay.clips = clipList.ToArray();
            relay.gunner = gst;
            relay.voiceOut = vOut;
            relay.radioOut = rOut;
            UdonSharpEditorUtility.CopyProxyToUdon(relay);

            Undo.RecordObject(c, "Flight setup");
            c.gunner = gst;
            c.voiceRelay = relay;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            if (c.boarder != null)
            {
                Undo.RecordObject(c.boarder, "Flight setup");
                c.boarder.station2 = st;
                c.boarder.gunner = gst;
                UdonSharpEditorUtility.CopyProxyToUdon(c.boarder);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c.boarder);
            }
            n++;
        }
        log.AppendLine("ガンナー席: " + n + " 機に設置（操縦席の後ろ・機体の下に旋回砲塔）");
    }

    static AudioClip LoadClip(string file)
    {
        string path = AudioDir + "/" + file;
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (c == null && File.Exists(path)) { AssetDatabase.ImportAsset(path); c = AssetDatabase.LoadAssetAtPath<AudioClip>(path); }
        return c;
    }

    // ---------------- 入室・退室の記録 ----------------
    static bool joinRetried;
    const string JoinScriptPath = "Assets/My script/drone/JoinLog.cs";
    const string JoinAssetPath = "Assets/My script/drone/JoinLog.asset";

    static void SetupJoinLog(StringBuilder log)
    {
        JoinLog jl = Object.FindObjectOfType<JoinLog>(true);
        if (jl == null)
        {
            var go = new GameObject("JoinLog");
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            jl = UdonSharpEditor.UdonSharpUndo.AddComponent<JoinLog>(go);
        }
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.cockpitRoot == null) continue;
            Undo.RecordObject(c, "Flight setup");
            c.joinLog = jl;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("入室記録: " + n + " 機に接続");
    }

    // ---------------- 撃墜ランキングの掲示板（出現地点の前に 4 枚） ----------------
    // ---------------- 音の大きさの設定パネル（main room・掲示板の右） ----------------
    static void SetupVolumePanel(StringBuilder log)
    {
        GameObject rk = GameObject.Find("Rankings");
        if (rk == null) { log.AppendLine("音量: Rankings が無いので置けない"); return; }
        Material back = LoadOrCreateMat("RankingBoard", Shader.Find("Standard"));
        Material edge = UnlitMat("RankingEdge", new Color(0.35f, 0.95f, 1f, 1f), false, 2000);
        Material btnM = UnlitMat("VolumeButton", new Color(0.12f, 0.35f, 0.45f, 1f), false, 2000);

        var root = new GameObject("VolumePanel");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        root.transform.SetParent(rk.transform, false);
        const float W = 2.3f, H = 2.7f;
        root.transform.localPosition = new Vector3(2.85f + 0.35f + W * 0.5f, 1.2f + H * 0.5f, 0f);
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.name = "Plate";
        plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        plate.transform.localScale = new Vector3(W, H, 0.08f);
        plate.GetComponent<MeshRenderer>().sharedMaterial = back;
        Quad(root.transform, "EdgeT", new Vector3(0f, H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
        Quad(root.transform, "EdgeB", new Vector3(0f, -H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
        Quad(root.transform, "EdgeL", new Vector3(-W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);
        Quad(root.transform, "EdgeR", new Vector3(W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);

        var vs = UdonSharpEditor.UdonSharpUndo.AddComponent<VolumeSettings>(root);
        PanelText(root.transform, "Title", "<b>SOUND VOLUME</b>\n<size=60%><color=#7fa8b0>Only for you. Saved when you leave.</color></size>", new Vector3(0f, H * 0.5f - 0.28f, -0.01f), W - 0.2f, 1.0f, TextAlignmentOptions.Center);
        string[] labels = { "ENGINE", "RADIO  (AMA / COMMAND)", "TEAM RADIO  (VOICE CHAT)", "GUIDANCE VOICE", "WEAPONS / EXPLOSIONS", "ALARMS" };
        var vals = new TextMeshPro[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            float y = H * 0.5f - 0.75f - i * 0.33f;
            PanelText(root.transform, "Label" + i, labels[i], new Vector3(-0.42f, y, -0.01f), 1.25f, 0.62f, TextAlignmentOptions.Left);
            vals[i] = PanelText(root.transform, "Value" + i, "100%", new Vector3(0.66f, y, -0.01f), 0.4f, 0.75f, TextAlignmentOptions.Center);
            for (int k = 0; k < 2; k++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 当たり判定付き（押せる）
                b.name = k == 0 ? "Down" + i : "Up" + i;
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = new Vector3(k == 0 ? 0.33f : 0.99f, y, -0.02f);
                b.transform.localScale = new Vector3(0.24f, 0.24f, 0.08f);
                b.GetComponent<MeshRenderer>().sharedMaterial = btnM;
                PanelText(root.transform, b.name + "Mark", k == 0 ? "-" : "+", new Vector3(b.transform.localPosition.x, y, -0.07f), 0.24f, 1.1f, TextAlignmentOptions.Center);
                var vb = UdonSharpEditor.UdonSharpUndo.AddComponent<VolumeButton>(b);
                vb.settings = vs;
                vb.cat = i;
                vb.delta = k == 0 ? -1 : 1;
                UdonSharpEditorUtility.CopyProxyToUdon(vb);
                UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(vb);
                if (ub != null) { ub.interactText = k == 0 ? "音量を下げる" : "音量を上げる"; ub.proximity = 3f; }
            }
        }
        vs.valueTexts = vals;

        // 音の種類ごとに分ける
        var radio = new System.Collections.Generic.HashSet<AudioSource>();
        var voice = new System.Collections.Generic.HashSet<AudioSource>();
        var alarm = new System.Collections.Generic.HashSet<AudioSource>();
        var weapon = new System.Collections.Generic.HashSet<AudioSource>();
        var engine = new System.Collections.Generic.HashSet<AudioSource>();
        System.Action<System.Collections.Generic.HashSet<AudioSource>, AudioSource> add = (set, a) => { if (a != null) set.Add(a); };
        var ctrls = Object.FindObjectsOfType<controller>(true);
        foreach (var c in ctrls)
        {
            add(engine, c.engineAudio);
            add(radio, c.enemyRadio);
            add(voice, c.voiceAudio);
            add(alarm, c.stallWarningAudio); add(alarm, c.stallAudio); add(alarm, c.alarmAudio); add(alarm, c.cockpitAudio);
            add(weapon, c.gunAudio); add(weapon, c.crashAudio);
        }
        foreach (var vr in Object.FindObjectsOfType<VoiceRelay>(true)) { add(voice, vr.voiceOut); add(radio, vr.radioOut); }
        foreach (var gm in Object.FindObjectsOfType<GameMode>(true)) add(radio, gm.radio);
        foreach (var gs in Object.FindObjectsOfType<GunnerStation>(true)) { add(alarm, gs.stallWarnAudio); add(alarm, gs.stallAudio); add(alarm, gs.alarmAudio); add(weapon, gs.gunAudio); }
        // 機体・船・敵機・標的・共有の爆発に付いている残りの音は「武器・爆発」
        var weaponRoots = new System.Collections.Generic.List<Transform>();
        foreach (var c in ctrls) if (c.car != null) weaponRoots.Add(c.car.transform);
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true)) weaponRoots.Add(ws.transform);
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true)) { if (ed.rb != null) weaponRoots.Add(ed.rb.transform); add(weapon, ed.gunAudio); add(weapon, ed.missileBoomAudio); }
        foreach (var ft in Object.FindObjectsOfType<FlightTarget>(true)) add(weapon, ft.explosionAudio);
        foreach (var nm in new[] { "CrashExplosion", "MissileSplash", "MissileExplosion" }) { var g = GameObject.Find(nm); if (g != null) weaponRoots.Add(g.transform); }
        foreach (var t in weaponRoots)
            foreach (var a in t.GetComponentsInChildren<AudioSource>(true))
                if (!engine.Contains(a) && !radio.Contains(a) && !voice.Contains(a) && !alarm.Contains(a)) weapon.Add(a);

        Undo.RecordObject(vs, "Flight setup");
        vs.radioSources = new System.Collections.Generic.List<AudioSource>(radio).ToArray();
        vs.voiceSources = new System.Collections.Generic.List<AudioSource>(voice).ToArray();
        vs.alarmSources = new System.Collections.Generic.List<AudioSource>(alarm).ToArray();
        vs.weaponSources = new System.Collections.Generic.List<AudioSource>(weapon).ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(vs);
        foreach (var c in ctrls) { Undo.RecordObject(c, "Flight setup"); c.volume = vs; UdonSharpEditorUtility.CopyProxyToUdon(c); }
        foreach (var pr in Object.FindObjectsOfType<PilotRadio>(true)) { Undo.RecordObject(pr, "Flight setup"); pr.volume = vs; UdonSharpEditorUtility.CopyProxyToUdon(pr); }
        log.AppendLine("音量: パネルを掲示板の右に設置（エンジン " + engine.Count + " / 無線 " + radio.Count + " / 案内 " + voice.Count + " / 武器・爆発 " + weapon.Count + " / 警報 " + alarm.Count + " 個の音）");
    }

    static TextMeshPro PanelText(Transform p, string name, string text, Vector3 lp, float w, float size, TextAlignmentOptions al)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = lp;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = al;
        t.enableWordWrapping = false;
        t.richText = true;
        t.rectTransform.sizeDelta = new Vector2(w, size * 0.3f);
        return t;
    }

    // ---------------- 世界ランキング（全インスタンス共通・名前なし） ----------------
    // Backup_flight/score_urls.txt: 1 行目 = サーバーの URL（https://score-board.xxx.workers.dev）、2 行目 = Gist の scores.json の URL
    const string RankScriptPath = "Assets/My script/drone/WorldRanking.cs";
    const string RankAssetPath = "Assets/My script/drone/WorldRanking.asset";
}
