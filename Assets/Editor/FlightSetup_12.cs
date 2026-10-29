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
    static bool SetupCockpit(controller c, StringBuilder log)
    {
        // 内装のマテリアル（暗い金属）
        s_consoleMat = LoadOrCreateMat("CockpitConsole", Shader.Find("Standard"));
        s_consoleMat.SetColor("_Color", new Color(0.07f, 0.08f, 0.1f));
        s_consoleMat.SetFloat("_Metallic", 0.4f);
        s_consoleMat.SetFloat("_Glossiness", 0.45f);
        EditorUtility.SetDirty(s_consoleMat);
        // 砂嵐の模様（白黒のノイズ＋横すじ）を赤く表示する
        Texture2D noise = GenTex("static_noise.png", 128, (u, v) =>
        {
            float h = Mathf.Abs(Mathf.Sin(u * 1273.13f + v * 7841.77f) * 43758.5453f) % 1f;
            float band = (Mathf.Abs(Mathf.Sin(v * 91.7f) * 1531.3f) % 1f) > 0.85f ? 0.6f : 0f;
            float g = Mathf.Clamp01(h * 0.9f + band);
            return new Color(g, g, g, 0.35f + 0.6f * g);
        });
        var nimp = AssetImporter.GetAtPath(FxDir + "/static_noise.png") as TextureImporter;
        if (nimp != null && (nimp.wrapMode != TextureWrapMode.Repeat || nimp.filterMode != FilterMode.Point))
        {
            nimp.wrapMode = TextureWrapMode.Repeat;
            nimp.filterMode = FilterMode.Point;
            nimp.mipmapEnabled = false;
            nimp.SaveAndReimport();
            noise = AssetDatabase.LoadAssetAtPath<Texture2D>(FxDir + "/static_noise.png");
        }
        s_staticMat = TexMat("PanelStatic", noise, new Color(1f, 0.12f, 0.08f, 0.9f), 3001);
        s_staticMat.SetTextureScale("_MainTex", new Vector2(0.5f, 0.5f));
        EditorUtility.SetDirty(s_staticMat);
        Transform seat = c.transform;
        var st = c.GetComponent<VRCStation>();

        // 古い文字だけの HUD は消す（非表示にして参照を外す）
        if (c.hudText != null)
        {
            Undo.RecordObject(c.hudText.gameObject, "Flight setup");
            c.hudText.gameObject.SetActive(false);
            Undo.RecordObject(c, "Flight setup");
            c.hudText = null;
        }

        // 作り直す（レイアウト変更を反映するため毎回）
        // 以前は座席の子に置いていたが、座席の「選択中」ハイライトが画面にかかるので機体本体の子にする
        Transform old = seat.Find("Cockpit");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        if (c.car != null)
        {
            Transform old2 = c.car.transform.Find("Cockpit");
            if (old2 != null) Undo.DestroyObjectImmediate(old2.gameObject);
        }

        Material bg = UnlitMat("CockpitPanel", new Color(0.02f, 0.07f, 0.13f, 0.72f), true, 2990);
        Material frame = UnlitMat("CockpitFrame", CCyan, false, 2000);
        Material barBg = UnlitMat("CockpitBarBG", new Color(0.35f, 0.95f, 1f, 0.15f), true, 2995);
        Material amber = UnlitMat("CockpitAmber", CAmber, false, 2000);
        Material red = UnlitMat("CockpitRed", new Color(1f, 0.2f, 0.15f, 1f), false, 2000);

        Transform hip = (st != null && st.stationEnterPlayerLocation != null) ? st.stationEnterPlayerLocation : seat;
        var rootGo = new GameObject("Cockpit");
        Undo.RegisterCreatedObjectUndo(rootGo, "Flight setup");
        Transform root = rootGo.transform;
        root.SetParent(c.car != null ? c.car.transform : seat, false);
        // 原点 = 座った時の目の位置、+Z = 前
        root.position = hip.position + seat.up * (0.72f + CockpitRaise);
        root.rotation = seat.rotation;
        SetWorldScaleOne(root);
        root.localScale = root.localScale * CockpitScale;
        // 椅子の見た目を小さく（元の大きさを子の印に覚えておき、何回実行しても同じ大きさ）
        foreach (var chairR in seat.GetComponentsInChildren<MeshRenderer>(true))
        {
            Transform ct = chairR.transform;
            if (ct.IsChildOf(root)) continue;
            Transform mark = ct.Find("ChairBaseScale");
            if (mark == null)
            {
                var mk = new GameObject("ChairBaseScale");
                mk.transform.SetParent(ct, false);
                mk.transform.localPosition = ct.localScale;
                mark = mk.transform;
            }
            Undo.RecordObject(ct, "Flight setup");
            ct.localScale = mark.localPosition * ChairScale;
        }

        // 中央: 速度計・高度計（時計のようなメーター）と水平儀
        bool hudStyle = c.car != null && IsOmega(c.car);   // Omega: 機首に重ねる HUD なので、計器を少し上（目の高さ寄り）に
        Transform main = Panel(root, "MainPanel", new Vector3(0f, hudStyle ? -0.02f : -0.17f, 0.80f), 0.46f, 0.32f, bg, frame);
        Material white = UnlitMat("CockpitNeedle", CWhite, false, 2000);
        Material spdFace = TexMat("DialSpeed", GenTex("dial_speed.png", 256, (u, v) => DialPix(u, v, -135f, 270f, 6, 5)), Color.white, 2992);
        Material altFace = TexMat("DialAlt", GenTex("dial_alt.png", 256, (u, v) => DialPix(u, v, 0f, 360f, 10, 5)), Color.white, 2992);
        const float DR = 0.078f;
        Undo.RecordObject(c, "Flight setup");
        Txt(main, "SpdLabel", "SPEED  km/h", new Vector2(-0.15f, 0.135f), 0.16f, 0.12f, CCyanDim, TextAlignmentOptions.Center);
        Txt(main, "AltLabel", "ALTITUDE  m", new Vector2(0.15f, 0.135f), 0.16f, 0.12f, CCyanDim, TextAlignmentOptions.Center);
        var spdLabels = new string[7];
        var spdAngles = new float[7];
        for (int i = 0; i < 7; i++) { spdLabels[i] = (i * 50).ToString(); spdAngles[i] = -135f + i * 45f; }
        var altLabels = new string[10];
        var altAngles = new float[10];
        for (int i = 0; i < 10; i++) { altLabels[i] = i.ToString(); altAngles[i] = i * 36f; }
        c.cpSpdNeedle = Dial(main, "SpdDial", new Vector2(-0.15f, 0.03f), DR, spdFace, spdLabels, spdAngles, white, DR * 0.86f, 0.0045f);
        Transform altDial;
        c.cpAltNeedle = Dial(main, "AltDial", new Vector2(0.15f, 0.03f), DR, altFace, altLabels, altAngles, white, DR * 0.86f, 0.0035f);
        altDial = c.cpAltNeedle.parent;
        // 高度計の短い針（1周 = 10000m）
        var kp = new GameObject("AltNeedleK").transform;
        kp.SetParent(altDial, false);
        kp.localPosition = new Vector3(0.15f, 0.03f, -0.0045f);
        Quad(kp, "Needle", new Vector3(0f, DR * 0.22f, 0f), new Vector2(0.008f, DR * 0.56f), amber);
        c.cpAltNeedleK = kp;
        Txt(altDial, "Unit", "x100", new Vector2(0.15f, 0.03f - DR * 0.42f), 0.05f, 0.07f, CCyanDim, TextAlignmentOptions.Center);
        // 数字でも小さく表示
        c.cpSpeed = Txt(main, "Speed", "0", new Vector2(-0.15f, -0.072f), 0.14f, 0.26f, CWhite, TextAlignmentOptions.Center);
        c.cpAlt = Txt(main, "Alt", "0", new Vector2(0.15f, -0.072f), 0.14f, 0.26f, CWhite, TextAlignmentOptions.Center);

        // 水平儀（傾きに合わせて回る線）と機体マーク（中央の列）
        var att = new GameObject("Attitude").transform;
        att.SetParent(main, false);
        att.localPosition = new Vector3(0f, 0.03f, 0f);
        var hz = new GameObject("Horizon").transform;
        hz.SetParent(att, false);
        hz.localPosition = new Vector3(0f, 0f, -0.0015f);
        Quad(hz, "Line", Vector3.zero, new Vector2(0.11f, 0.003f), frame);
        Quad(hz, "TickL", new Vector3(-0.055f, -0.006f, 0f), new Vector2(0.003f, 0.012f), frame);
        Quad(hz, "TickR", new Vector3(0.055f, -0.006f, 0f), new Vector2(0.003f, 0.012f), frame);
        c.cpHorizon = hz;
        Quad(att, "MarkL", new Vector3(-0.028f, 0f, -0.003f), new Vector2(0.03f, 0.005f), amber);
        Quad(att, "MarkR", new Vector3(0.028f, 0f, -0.003f), new Vector2(0.03f, 0.005f), amber);
        Quad(att, "MarkC", new Vector3(0f, 0f, -0.003f), new Vector2(0.008f, 0.008f), amber);
        c.cpAtt = Txt(main, "Attitude", "", new Vector2(0f, -0.045f), 0.14f, 0.1f, CCyanDim, TextAlignmentOptions.Center);

        c.cpWarn = Txt(main, "Warn", "", new Vector2(0f, -0.105f), 0.42f, 0.3f, CWhite, TextAlignmentOptions.Center);
        c.cpStatus = Txt(main, "Status", "", new Vector2(0f, -0.138f), 0.44f, 0.13f, CCyan, TextAlignmentOptions.Center);

        // 左: 迎角・フラップ・ブレーキ
        Transform left = Panel(root, "LeftPanel", new Vector3(-0.40f, -0.15f, 0.66f), 0.28f, 0.26f, bg, frame);
        Txt(left, "Title", "FLIGHT", new Vector2(0f, 0.105f), 0.26f, 0.15f, CCyan, TextAlignmentOptions.Center);
        c.cpAoaBar = Bar(left, "AoaBar", new Vector2(-0.10f, -0.015f), new Vector2(0.03f, 0.17f), true, barBg, amber);
        float stallFrac = Mathf.Clamp01(c.stallAngle / (c.stallAngle + 5f));
        Quad(left, "StallMark", new Vector3(-0.10f, -0.015f - 0.085f + stallFrac * 0.17f, -0.003f), new Vector2(0.045f, 0.004f), red);
        Txt(left, "AoaLabel", "AoA", new Vector2(0.03f, 0.07f), 0.18f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpAoa = Txt(left, "Aoa", "0.0°", new Vector2(0.03f, 0.028f), 0.18f, 0.40f, CWhite, TextAlignmentOptions.Left);
        c.cpFlags = Txt(left, "Flags", "", new Vector2(0.03f, -0.065f), 0.18f, 0.15f, CCyan, TextAlignmentOptions.Left, 0.09f);

        // さらに左: HP・弾数・武器の選択・ロックオン
        Transform wp = Panel(root, "WeaponPanel", new Vector3(-0.62f, -0.15f, 0.48f), 0.28f, 0.30f, bg, frame);
        Txt(wp, "Title", "STATUS / WEAPON", new Vector2(0f, 0.125f), 0.26f, 0.14f, CCyan, TextAlignmentOptions.Center);
        Txt(wp, "HpLabel", "HP", new Vector2(-0.065f, 0.085f), 0.12f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpHp = Txt(wp, "Hp", "100", new Vector2(0.03f, 0.075f), 0.2f, 0.42f, CWhite, TextAlignmentOptions.Right);
        c.cpHpBar = Bar(wp, "HpBar", new Vector2(0f, 0.037f), new Vector2(0.24f, 0.014f), false, barBg, frame);
        Quad(wp, "Sep", new Vector3(0f, 0.017f, -0.002f), new Vector2(0.24f, 0.002f), frame);
        Txt(wp, "WpLabel", "WEAPON   [1] [2] [3]", new Vector2(0f, 0.0f), 0.24f, 0.11f, CCyanDim, TextAlignmentOptions.Left);
        c.cpWeapons = Txt(wp, "Weapons", "", new Vector2(0f, -0.055f), 0.24f, 0.17f, CWhite, TextAlignmentOptions.Left, 0.09f);
        c.cpLock = Txt(wp, "Lock", "", new Vector2(0f, -0.122f), 0.26f, 0.16f, CWhite, TextAlignmentOptions.Center);

        // 右: スロットル・昇降率
        Transform rightP = Panel(root, "RightPanel", new Vector3(0.40f, -0.15f, 0.66f), 0.28f, 0.26f, bg, frame);
        Txt(rightP, "Title", "ENGINE", new Vector2(0f, 0.105f), 0.26f, 0.15f, CCyan, TextAlignmentOptions.Center);
        c.cpThrBar = Bar(rightP, "ThrBar", new Vector2(-0.10f, -0.015f), new Vector2(0.03f, 0.17f), true, barBg, frame);
        Txt(rightP, "ThrLabel", "THR", new Vector2(0.03f, 0.07f), 0.18f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpThr = Txt(rightP, "Thr", "0%", new Vector2(0.03f, 0.028f), 0.18f, 0.40f, CWhite, TextAlignmentOptions.Left);
        Txt(rightP, "VsLabel", "CLIMB", new Vector2(0.03f, -0.03f), 0.18f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpVs = Txt(rightP, "VS", "", new Vector2(0.03f, -0.06f), 0.18f, 0.2f, CWhite, TextAlignmentOptions.Left);
        c.cpFuel = Txt(rightP, "Fuel", "FUEL", new Vector2(0f, -0.112f), 0.27f, 0.21f, CWhite, TextAlignmentOptions.Center);   // 燃料は大きめの文字
        c.voiceLowFuel = LoadClip("voice_lowfuel.wav");
        c.voiceFuelEmpty = LoadClip("voice_fuelempty.wav");
        c.cpGun = null;
        c.cpGunBar = null;

        // さらに右: 入室・退室の記録
        Transform lp = Panel(root, "LogPanel", new Vector3(0.62f, -0.15f, 0.48f), 0.28f, 0.30f, bg, frame);
        Txt(lp, "Title", "PLAYERS / LOG / KILLS", new Vector2(0f, 0.125f), 0.26f, 0.14f, CCyan, TextAlignmentOptions.Center);
        c.cpPlayers = Txt(lp, "Online", "ONLINE  1", new Vector2(0f, 0.095f), 0.24f, 0.13f, CWhite, TextAlignmentOptions.Left);
        Quad(lp, "Sep", new Vector3(0f, 0.077f, -0.002f), new Vector2(0.24f, 0.002f), frame);
        c.cpLog = Txt(lp, "Log", "", new Vector2(0f, -0.02f), 0.25f, 0.1f, CWhite, TextAlignmentOptions.TopLeft, 0.18f);
        // 撃墜数（いちばん下）
        Quad(lp, "Sep2", new Vector3(0f, -0.108f, -0.002f), new Vector2(0.24f, 0.002f), frame);
        Txt(lp, "KillsLabel", "KILLS", new Vector2(0f, -0.12f), 0.24f, 0.09f, CCyanDim, TextAlignmentOptions.Left);
        c.cpKills = Txt(lp, "Kills", "", new Vector2(0.02f, -0.134f), 0.22f, 0.15f, CWhite, TextAlignmentOptions.Right);

        // ロックオンの赤い四角（標的に重ねて表示。何にも隠れないように一番手前に描く）
        Material lockMat = UnlitMat("LockRed", new Color(1f, 0.12f, 0.1f, 1f), true, 3100);
        lockMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        lockMat.renderQueue = 3100;
        EditorUtility.SetDirty(lockMat);
        var lb = new GameObject("LockBox").transform;
        lb.SetParent(root, false);
        const float LT = 0.05f;
        Quad(lb, "Top", new Vector3(0f, 0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
        Quad(lb, "Bottom", new Vector3(0f, -0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
        Quad(lb, "Left", new Vector3(-0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
        Quad(lb, "Right", new Vector3(0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
        lb.gameObject.SetActive(false);
        c.lockBox = lb;

        // 下: 方位
        // 方位は中央の計器パネルの中に入れる（下の細いパネルは無くして、その分レーダーを上げる）

        // レーダー（中央の下）
        BuildRadar(c, root, bg, frame, amber);

        // コックピットの内装（コンソール・ボタン・操縦桿・窓枠）
        if (c.car == null || !IsOmega(c.car)) BuildInterior(root, s_consoleMat);   // Omega は本物の操縦席があるので、画面だけ出す
        c.cpHdg = Txt(main, "Heading", "HDG 000", new Vector2(0f, -0.074f), 0.14f, 0.12f, CCyan, TextAlignmentOptions.Center);

        // 目の前（目の高さ・約0.9m先）に大きく出すメッセージ
        var center = new GameObject("CenterMessage").transform;
        center.SetParent(root, false);
        center.localPosition = new Vector3(0f, 0.17f, 0.9f);   // カウントダウンなどは少し上に
        center.localRotation = Quaternion.identity;
        c.cpCenter = Txt(center, "Text", "", Vector2.zero, 0.9f, 0.42f, CWhite, TextAlignmentOptions.Center, 0.3f);

        // 起動時の暗転（周りを包む黒い球。内側から見える。雲より後・画面より先に描く）
        var bo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(bo.GetComponent<Collider>());
        bo.name = "Blackout";
        bo.transform.SetParent(root, false);
        bo.transform.localPosition = Vector3.zero;
        bo.transform.localScale = Vector3.one * 3f;   // 半径 1.5m（内装も画面も中に入る）
        Material boMat = UnlitMat("CockpitBlackout", new Color(0f, 0f, 0f, 1f), true, 2987);
        // 内側も描く・手前の物（内装）も含めて全部黒で覆う。画面（2990 以降）だけは上に光って見える
        boMat.SetFloat("_Cull", 0f);
        boMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        boMat.renderQueue = 2987;
        EditorUtility.SetDirty(boMat);
        var bor = bo.GetComponent<MeshRenderer>();
        bor.sharedMaterial = boMat;
        bor.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bor.enabled = false;
        c.blackout = bor;

        // 被弾時に視界を赤く点滅させる球（暗転の球と同じ作り。画面より先に描くので画面は読める）
        var dt2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(dt2.GetComponent<Collider>());
        dt2.name = "DamageTint";
        dt2.transform.SetParent(root, false);
        dt2.transform.localScale = Vector3.one * 3f;
        Material dtMat = UnlitMat("CockpitDamageTint", new Color(1f, 0.05f, 0.02f, 0f), true, 2988);
        dtMat.SetFloat("_Cull", 0f);
        dtMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        dtMat.renderQueue = 2988;
        EditorUtility.SetDirty(dtMat);
        var dtr = dt2.GetComponent<MeshRenderer>();
        dtr.sharedMaterial = dtMat;
        dtr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dtr.enabled = false;
        c.damageTint = dtr;
        // 警報音（ループ）
        var alGo = new GameObject("AlarmAudio");
        alGo.transform.SetParent(root, false);
        var alAu = alGo.AddComponent<AudioSource>();
        alAu.clip = LoadClip("alarm.wav");
        alAu.loop = true;
        alAu.playOnAwake = false;
        alAu.spatialBlend = 0f;
        alAu.volume = 0.22f;
        c.alarmAudio = alAu;
        // コックピットの中の煙（コックピットと一緒に動く）
        Material cabinSmokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (cabinSmokeMat == null) cabinSmokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        var cs = MakePS("CabinSmoke", root, cabinSmokeMat);
        cs.transform.localPosition = new Vector3(0f, -0.5f, 0.55f);
        var csm = cs.main;
        csm.loop = true;
        csm.simulationSpace = ParticleSystemSimulationSpace.Local;
        csm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        csm.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        csm.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        csm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.2f, 0.2f, 0.35f), new Color(0.35f, 0.35f, 0.35f, 0.25f));
        csm.gravityModifier = -0.02f;
        csm.maxParticles = 60;
        var css = cs.shape;
        css.enabled = true;
        css.shapeType = ParticleSystemShapeType.Box;
        css.scale = new Vector3(1.2f, 0.1f, 0.4f);
        SetFade(cs, new Color(0.3f, 0.3f, 0.3f), new Color(0.45f, 0.45f, 0.45f));
        c.cabinSmoke = cs;
        c.voiceEngineFire = LoadClip("voice_enginefire.wav");
        // 敵（AMA）の無線（本人にだけ聞こえる。自分の音声案内とは別のスピーカー）
        var erGo = new GameObject("EnemyRadio");
        erGo.transform.SetParent(root, false);
        var erAu = erGo.AddComponent<AudioSource>();
        erAu.playOnAwake = false;
        erAu.spatialBlend = 0f;
        erAu.volume = 0.85f;
        erAu.priority = 5;
        c.enemyRadio = erAu;
        c.amaBroadcast = LoadClip("ama_broadcast.wav");
        c.amaWarn1 = LoadClip("ama_warn1.wav");
        c.amaWarn2 = LoadClip("ama_warn2.wav");
        c.amaWarn3 = LoadClip("ama_warn3.wav");
        c.amaFinal = LoadClip("ama_final.wav");
        c.amaEngage = LoadClip("ama_engage.wav");
        c.amaHeavy = new[] { LoadClip("gaur_lost1.wav"), LoadClip("gaur_lost2.wav") };              // ガウリア帝国の司令部（AMA とは別の声）
        c.amaAttacked = new[] { LoadClip("gaur_attacked1.wav"), LoadClip("gaur_attacked2.wav") };
        c.gaurRecon = new[] { LoadClip("gaur_recon1.wav"), LoadClip("gaur_recon2.wav") };
        c.guanWarn = new[] { LoadClip("guan_warn1.wav"), LoadClip("guan_warn2.wav") };   // 空母の艦長（AMA・司令部とは別の声）
        c.voiceGuided = new[] { LoadClip("voice_guided1.wav"), LoadClip("voice_guided2.wav") };
        c.carrier = null;
        c.carrierShip = null;
        foreach (var cws in Object.FindObjectsOfType<WarShip>(true)) if (cws.target != null && cws.target.kind == 2) { c.carrier = cws.transform; c.carrierShip = cws; break; }
        c.amaFired = LoadClip("ama_fired.wav");
        c.voiceCritical = LoadClip("voice_critical.wav");
        c.staticMat = s_staticMat;

        // 起動音（本人にだけ聞こえる 2D の音）
        var bootAu = rootGo.AddComponent<AudioSource>();
        bootAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/boot.wav");
        bootAu.playOnAwake = false;
        bootAu.spatialBlend = 0f;
        bootAu.volume = 0.5f;
        c.cockpitAudio = bootAu;

        // 音声案内（英語・本人にだけ聞こえる 2D の音）
        var voGo = new GameObject("VoiceAudio");
        voGo.transform.SetParent(root, false);
        var voAu = voGo.AddComponent<AudioSource>();
        voAu.playOnAwake = false;
        voAu.spatialBlend = 0f;
        voAu.volume = 0.9f;
        voAu.priority = 10;
        c.voiceAudio = voAu;
        var clocks = new AudioClip[12];
        for (int i = 0; i < 12; i++) clocks[i] = LoadClip("voice_clock" + i + ".wav");
        c.voiceClock = clocks;
        c.voiceKill = LoadClip("voice_kill.wav");
        c.voiceOn = LoadClip("voice_on.wav");
        c.voiceOff = LoadClip("voice_off.wav");
        c.voiceTakingFire = LoadClip("voice_takingfire.wav");
        var fireClocks = new AudioClip[12];
        for (int i = 0; i < 12; i++) fireClocks[i] = LoadClip("voice_fire_clock" + i + ".wav");
        c.voiceFireClock = fireClocks;
        var mslClocks = new AudioClip[12];
        for (int i = 0; i < 12; i++) mslClocks[i] = LoadClip("voice_msl_clock" + i + ".wav");
        c.voiceMissileClock = mslClocks;
        c.voiceMissile = LoadClip("voice_missile.wav");
        c.voiceTargetHit = LoadClip("voice_targethit.wav");
        c.voiceImpact = LoadClip("voice_impact.wav");
        c.voiceDestroyed = LoadClip("voice_destroyed.wav");
        c.voiceWelcome = LoadClip("voice_welcome.wav");
        c.voiceLaunching = LoadClip("voice_launching.wav");
        c.voiceGo = LoadClip("voice_go.wav");
        c.voiceApOn = LoadClip("voice_ap_on.wav");
        c.voiceApOff = LoadClip("voice_ap_off.wav");

        // 失速の警告音（本人にだけ聞こえる・ループ）
        var warnGo = new GameObject("StallWarnAudio");
        warnGo.transform.SetParent(root, false);
        var warnAu = warnGo.AddComponent<AudioSource>();
        warnAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/stall_warn.wav");
        warnAu.loop = true;
        warnAu.playOnAwake = false;
        warnAu.spatialBlend = 0f;
        warnAu.volume = 0.35f;
        c.stallWarningAudio = warnAu;
        var stallGo = new GameObject("StallAudio");
        stallGo.transform.SetParent(root, false);
        var stallAu = stallGo.AddComponent<AudioSource>();
        stallAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/stall.wav");
        stallAu.loop = true;
        stallAu.playOnAwake = false;
        stallAu.spatialBlend = 0f;
        stallAu.volume = 0.4f;
        c.stallAudio = stallAu;

        c.cockpitRoot = rootGo;
        // Omega: 機首で画面が隠れるので、パネルはやめて「ヘッドアップディスプレイ」風に
        // （緑の文字と計器だけ。機体の上に重ねて描くので、機首やガラスに隠れない）
        if (c.car != null && IsOmega(c.car))
        {
            // Omega: R35 の表示を緑の文字だけ（HUD）にして、下に丸い計器
            ConvertToHud(c, root);
            OmegaHudTidy(c, root);
            BuildAnalogPanel(c, root, true);
        }   // Omega: 本物の飛行機のような丸い計器の計器盤
        rootGo.SetActive(false);   // 普段は非表示。座った本人の画面だけで表示される

        UdonSharpEditorUtility.CopyProxyToUdon(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
        if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        return true;
    }

    // ---------------- レーダー画面 ----------------
    // 濃い青の円に格子と同心円、外周に方位の目盛り（機首が上になるよう回る）、回る光、飛行機の形のアイコン
    const float RadarScope = 0.74f;   // テクスチャ内の画面の半径（外側は方位の目盛り）

    const int RadarDotCount = 140;

    // ---- レーダーの島の地図: ワールドを上から調べて陸の形の画像を作る（最初の 1 回だけ。作り直す時は画像を消す） ----
    const string RadarMapPng = FxDir + "/radar_map.png";
    const int RadarMapRes = 1024;
    const float RadarMapSize = 28000f;   // 地図の 1 辺（m）。出撃地点が真ん中
    static Vector4 radarMapRect;
    static Material radarMapMatN, radarMapMatH;
    static Material RadarMapMat(bool hud)
    {
        if (hud && radarMapMatH != null) return radarMapMatH;
        if (!hud && radarMapMatN != null) return radarMapMatN;
        Shader sh = Shader.Find("FlightUI/RadarMap");
        if (sh == null) return null;
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Vector3 c0 = (putOut != null && putOut.catapultPoint != null) ? putOut.catapultPoint.position : new Vector3(254f, 0f, 297f);
        radarMapRect = new Vector4(c0.x, c0.z, RadarMapSize, 0f);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RadarMapPng);
        if (tex == null) tex = BakeRadarMap(c0);
        string path = FxDir + (hud ? "/RadarMapHUD.mat" : "/RadarMap.mat");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", hud ? new Color(0.45f, 1f, 0.55f, 0.9f) : new Color(0.55f, 0.95f, 0.6f, 0.9f));
        m.SetFloat("_ZTest", hud ? (float)UnityEngine.Rendering.CompareFunction.Always : (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        m.renderQueue = hud ? 3007 : 2992;   // 画面の背景の上・掃く光の下
        EditorUtility.SetDirty(m);
        if (hud) radarMapMatH = m; else radarMapMatN = m;
        return m;
    }

    static Texture2D BakeRadarMap(Vector3 c0)
    {
        Physics.SyncTransforms();
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        int N = RadarMapRes;
        float half = RadarMapSize / 2f, cell = RadarMapSize / N;
        int mask = ~((1 << 4) | (1 << 9) | (1 << 10) | (1 << 18));   // 水・プレイヤー・ミラーは除く
        var land = new bool[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            Vector3 o = new Vector3(c0.x - half + (x + 0.5f) * cell, seaY + 1500f, c0.z - half + (y + 0.5f) * cell);
            RaycastHit h;
            if (Physics.Raycast(o, Vector3.down, out h, 1600f, mask, QueryTriggerInteraction.Ignore)
                && h.collider.attachedRigidbody == null && h.point.y > seaY + 0.5f)
                land[y * N + x] = true;
        }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        int cnt = 0;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int i = y * N + x;
            if (!land[i]) { px[i] = new Color32(0, 0, 0, 0); continue; }
            cnt++;
            bool edge = x == 0 || y == 0 || x == N - 1 || y == N - 1 || !land[i - 1] || !land[i + 1] || !land[i - N] || !land[i + N];
            px[i] = edge ? new Color32(255, 255, 255, 255) : new Color32(200, 255, 210, 110);   // 海岸線ははっきり、陸の中は薄く
        }
        tex.SetPixels32(px);
        Directory.CreateDirectory(FxDir);
        File.WriteAllBytes(RadarMapPng, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RadarMapPng);
        var imp = AssetImporter.GetAtPath(RadarMapPng) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        Debug.Log("[FlightSetup] レーダーの島の地図: 陸 " + cnt + " マス / " + (N * N));
        return AssetDatabase.LoadAssetAtPath<Texture2D>(RadarMapPng);
    }
}
