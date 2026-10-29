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
    static void BuildHud(controller c, Transform root)
    {
        Transform old = root.Find("FlightHud");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        hudLineMat = HudLine();
        hudFontMat = null;
        var hud = new GameObject("FlightHud").transform;
        hud.SetParent(root, false);
        Undo.RecordObject(c, "Flight setup");
        const float LW = 0.0025f;          // 線の太さ
        const float TS = 0.17f;            // 数字の大きさ
        const float LS = 0.11f;            // 名前の大きさ

        // 機首の向き（W 字）
        var bore = new GameObject("Boresight").transform;
        bore.SetParent(hud, false);
        bore.localPosition = HudAt(0f, 0f);
        HudSeg(bore, "W1", new Vector3(-0.022f, 0f, 0f), 0.02f, LW, 0f);
        HudSeg(bore, "W2", new Vector3(-0.009f, -0.006f, 0f), 0.014f, LW, 60f);
        HudSeg(bore, "W3", new Vector3(0.009f, -0.006f, 0f), 0.014f, LW, -60f);
        HudSeg(bore, "W4", new Vector3(0.022f, 0f, 0f), 0.02f, LW, 0f);

        // 飛んでいる方向（丸に翼）
        var fpm = new GameObject("FlightPath").transform;
        fpm.SetParent(hud, false);
        fpm.localPosition = HudAt(0f, 0f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f;
            HudSeg(fpm, "C" + i, new Vector3(Mathf.Cos((a + 22.5f) * Mathf.Deg2Rad) * 0.009f, Mathf.Sin((a + 22.5f) * Mathf.Deg2Rad) * 0.009f, 0f), 0.0075f, LW, a + 112.5f);
        }
        HudSeg(fpm, "WingL", new Vector3(-0.02f, 0f, 0f), 0.022f, LW, 0f);
        HudSeg(fpm, "WingR", new Vector3(0.02f, 0f, 0f), 0.022f, LW, 0f);
        HudSeg(fpm, "Fin", new Vector3(0f, 0.014f, 0f), 0.01f, LW, 90f);
        c.hudFpm = fpm;

        // 角度のはしご（世界の水平に合わせて毎フレーム回す）
        var lad = new GameObject("PitchLadder").transform;
        lad.SetParent(hud, false);
        var lines = new System.Collections.Generic.List<Transform>();
        var degs = new System.Collections.Generic.List<float>();
        for (int d = -80; d <= 80; d += 10)
        {
            var ln = new GameObject("P" + d).transform;
            ln.SetParent(lad, false);
            float th = d * Mathf.Deg2Rad;
            ln.localPosition = new Vector3(0f, Mathf.Sin(th), Mathf.Cos(th)) * HudL;
            ln.localRotation = Quaternion.Euler(-d, 0f, 0f);
            if (d == 0)
            {
                HudSeg(ln, "L", new Vector3(-0.15f, 0f, 0f), 0.22f, LW, 0f);
                HudSeg(ln, "R", new Vector3(0.15f, 0f, 0f), 0.22f, LW, 0f);
            }
            else
            {
                float tick = d > 0 ? -0.012f : 0.012f;
                if (d > 0)
                {
                    HudSeg(ln, "L", new Vector3(-0.07f, 0f, 0f), 0.08f, LW, 0f);
                    HudSeg(ln, "R", new Vector3(0.07f, 0f, 0f), 0.08f, LW, 0f);
                }
                else
                {
                    // 下向きは破線
                    for (int k = 0; k < 3; k++)
                    {
                        HudSeg(ln, "L" + k, new Vector3(-0.04f - k * 0.03f, 0f, 0f), 0.018f, LW, 0f);
                        HudSeg(ln, "R" + k, new Vector3(0.04f + k * 0.03f, 0f, 0f), 0.018f, LW, 0f);
                    }
                }
                HudSeg(ln, "TL", new Vector3(-0.11f, tick * 0.5f, 0f), 0.012f, LW, 90f);
                HudSeg(ln, "TR", new Vector3(0.11f, tick * 0.5f, 0f), 0.012f, LW, 90f);
                HudTxt(ln, "NL", Mathf.Abs(d).ToString(), new Vector3(-0.135f, 0f, 0f), 0.04f, 0.1f, TextAlignmentOptions.Right);
                HudTxt(ln, "NR", Mathf.Abs(d).ToString(), new Vector3(0.135f, 0f, 0f), 0.04f, 0.1f, TextAlignmentOptions.Left);
            }
            lines.Add(ln); degs.Add(d);
        }
        c.hudLadder = lad;
        c.hudLadderLines = lines.ToArray();
        c.hudLadderDeg = degs.ToArray();

        // 傾き（上の円弧の目盛りと、空の方向を指す三角）
        var rollS = new GameObject("RollScale").transform;
        rollS.SetParent(hud, false);
        rollS.localPosition = HudAt(0f, 0f);
        float rr = Mathf.Tan(11f * Mathf.Deg2Rad) * HudL;
        foreach (float tk in new[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f, 45f, -45f, 60f, -60f })
        {
            float a = (90f + tk) * Mathf.Deg2Rad;
            float len = (tk == 0f || Mathf.Abs(tk) == 30f || Mathf.Abs(tk) == 60f) ? 0.022f : 0.012f;
            HudSeg(rollS, "T" + tk, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (rr + len * 0.5f), len, LW, 90f + tk);
        }
        var rp = new GameObject("RollPointer").transform;
        rp.SetParent(hud, false);
        rp.localPosition = HudAt(0f, 0f);
        HudSeg(rp, "A", new Vector3(-0.004f, rr - 0.008f, 0f), 0.011f, LW, 60f);
        HudSeg(rp, "B", new Vector3(0.004f, rr - 0.008f, 0f), 0.011f, LW, -60f);
        HudSeg(rp, "C", new Vector3(0f, rr - 0.0125f, 0f), 0.011f, LW, 0f);
        c.hudRollPtr = rp;

        // 方位のテープ（上）
        float tapeY = Mathf.Tan(19f * Mathf.Deg2Rad) * HudL;
        var tape = new GameObject("HeadingTape").transform;
        tape.SetParent(hud, false);
        tape.localPosition = new Vector3(0f, tapeY, HudL);
        int nT = 13;
        var ticks = new Transform[nT];
        var labs = new TextMeshPro[nT];
        for (int i = 0; i < nT; i++)
        {
            var tk = new GameObject("Tick" + i).transform;
            tk.SetParent(tape, false);
            HudSeg(tk, "Bar", new Vector3(0f, 0.008f, 0f), 0.016f, LW, 90f);
            ticks[i] = tk;
            labs[i] = HudTxt(tk, "Num", "", new Vector3(0f, 0.03f, 0f), 0.06f, 0.1f, TextAlignmentOptions.Center);
        }
        HudSeg(tape, "CaretA", new Vector3(-0.005f, -0.008f, 0f), 0.012f, LW, -60f);
        HudSeg(tape, "CaretB", new Vector3(0.005f, -0.008f, 0f), 0.012f, LW, 60f);
        c.hudTapeTicks = ticks;
        c.hudTapeLabels = labs;
        c.hudTapeScale = 0.006f;
        c.hudHdg = HudTxt(tape, "Hdg", "000", new Vector3(0f, -0.035f, 0f), 0.08f, TS, TextAlignmentOptions.Center);
        HudTxt(tape, "HdgLab", "HDG", new Vector3(-0.07f, -0.035f, 0f), 0.06f, LS, TextAlignmentOptions.Right);

        // 左の列: 名前（左寄せ）と数字（右寄せ）
        float xl0 = Mathf.Tan(-36f * Mathf.Deg2Rad) * HudL, xl1 = Mathf.Tan(-21f * Mathf.Deg2Rad) * HudL;
        string[] ln2 = { "SPEED km/h", "AoA °", "MACH", "G", "MAX G", "GUN", "ROCKET", "MISSILE" };
        var vals = new TextMeshPro[ln2.Length];
        for (int i = 0; i < ln2.Length; i++)
        {
            float y = Mathf.Tan((16f - i * 2.6f) * Mathf.Deg2Rad) * HudL;
            HudTxt(hud, "LL" + i, ln2[i], new Vector3(xl0 + 0.05f, y, HudL), 0.1f, LS, TextAlignmentOptions.Left);
            vals[i] = HudTxt(hud, "LV" + i, "", new Vector3(xl1 - 0.04f, y, HudL), 0.08f, i == 0 ? TS * 1.25f : TS, TextAlignmentOptions.Right);
        }
        c.hudSpd = vals[0]; c.hudAoa = vals[1]; c.hudMach = vals[2]; c.hudG = vals[3]; c.hudMaxG = vals[4];
        c.hudGun = vals[5]; c.hudRkt = vals[6]; c.hudMsl = vals[7];
        // スロットル（横の棒）
        float yThr = Mathf.Tan(-5.5f * Mathf.Deg2Rad) * HudL;
        HudTxt(hud, "ThrLab", "THROTTLE", new Vector3(xl0 + 0.05f, yThr + 0.025f, HudL), 0.1f, LS, TextAlignmentOptions.Left);
        float bw = xl1 - xl0;
        var thrBg = new GameObject("ThrFrame").transform;
        thrBg.SetParent(hud, false);
        thrBg.localPosition = new Vector3(xl0, yThr, HudL);
        HudSeg(thrBg, "Top", new Vector3(bw / 2f, 0.008f, 0f), bw, LW, 0f);
        HudSeg(thrBg, "Bot", new Vector3(bw / 2f, -0.008f, 0f), bw, LW, 0f);
        HudSeg(thrBg, "L", new Vector3(0f, 0f, 0f), 0.016f, LW, 90f);
        HudSeg(thrBg, "R", new Vector3(bw, 0f, 0f), 0.016f, LW, 90f);
        var thrFill = new GameObject("ThrFill").transform;
        thrFill.SetParent(thrBg, false);
        Quad(thrFill, "Fill", new Vector3(bw / 2f, 0f, 0f), new Vector2(bw, 0.011f), hudLineMat);
        c.hudThrFill = thrFill;
        // 操縦桿（四角の中の点）・ラダー（下の棒の点）
        float yStk = Mathf.Tan(-14f * Mathf.Deg2Rad) * HudL, xs = (xl0 + xl1) * 0.5f, hs = 0.045f;
        HudTxt(hud, "StkLab", "STICK", new Vector3(xs - hs - 0.03f, yStk + hs - 0.01f, HudL), 0.06f, LS, TextAlignmentOptions.Right);
        var box = new GameObject("StickBox").transform;
        box.SetParent(hud, false);
        box.localPosition = new Vector3(xs, yStk, HudL);
        HudSeg(box, "T", new Vector3(0f, hs, 0f), hs * 2f, LW, 0f);
        HudSeg(box, "B", new Vector3(0f, -hs, 0f), hs * 2f, LW, 0f);
        HudSeg(box, "L", new Vector3(-hs, 0f, 0f), hs * 2f, LW, 90f);
        HudSeg(box, "R", new Vector3(hs, 0f, 0f), hs * 2f, LW, 90f);
        HudSeg(box, "H", Vector3.zero, hs * 2f, LW * 0.5f, 0f);
        HudSeg(box, "V", Vector3.zero, hs * 2f, LW * 0.5f, 90f);
        var dot = Quad(box, "Dot", Vector3.zero, new Vector2(0.012f, 0.012f), hudLineMat).transform;
        c.hudStickDot = dot;
        HudSeg(box, "YawBar", new Vector3(0f, -hs - 0.015f, 0f), hs * 2f, LW, 0f);
        var yd = Quad(box, "YawDot", new Vector3(0f, -hs - 0.015f, 0f), new Vector2(0.006f, 0.014f), hudLineMat).transform;
        c.hudYawDot = yd;
        c.hudStickSize = hs;

        // 右の列
        float xr0 = Mathf.Tan(21f * Mathf.Deg2Rad) * HudL, xr1 = Mathf.Tan(36f * Mathf.Deg2Rad) * HudL;
        string[] rn = { "ALTITUDE m", "CLIMB m/s" };
        var rv = new TextMeshPro[2];
        for (int i = 0; i < 2; i++)
        {
            float y = Mathf.Tan((16f - i * 2.6f) * Mathf.Deg2Rad) * HudL;
            HudTxt(hud, "RL" + i, rn[i], new Vector3(xr0 + 0.05f, y, HudL), 0.1f, LS, TextAlignmentOptions.Left);
            rv[i] = HudTxt(hud, "RV" + i, "", new Vector3(xr1 - 0.04f, y, HudL), 0.08f, i == 0 ? TS * 1.25f : TS, TextAlignmentOptions.Right);
        }
        c.hudAlt = rv[0]; c.hudVs = rv[1];
        // HP（横の棒）
        float yHp = Mathf.Tan(10.2f * Mathf.Deg2Rad) * HudL, bw2 = xr1 - xr0;
        HudTxt(hud, "HpLab", "HP", new Vector3(xr0 + 0.05f, yHp + 0.025f, HudL), 0.1f, LS, TextAlignmentOptions.Left);
        var hpBg = new GameObject("HpFrame").transform;
        hpBg.SetParent(hud, false);
        hpBg.localPosition = new Vector3(xr0, yHp, HudL);
        HudSeg(hpBg, "Top", new Vector3(bw2 / 2f, 0.008f, 0f), bw2, LW, 0f);
        HudSeg(hpBg, "Bot", new Vector3(bw2 / 2f, -0.008f, 0f), bw2, LW, 0f);
        HudSeg(hpBg, "L", Vector3.zero, 0.016f, LW, 90f);
        HudSeg(hpBg, "R", new Vector3(bw2, 0f, 0f), 0.016f, LW, 90f);
        var hpFill = new GameObject("HpFill").transform;
        hpFill.SetParent(hpBg, false);
        Quad(hpFill, "Fill", new Vector3(bw2 / 2f, 0f, 0f), new Vector2(bw2, 0.011f), hudLineMat);
        c.hudHpFill = hpFill;
        // 機能の状態（使っている時は明るく・枠付き）
        string[] fn = { "BRAKE", "FLAPS", "BOOST", "LOCK", "VOICE" };
        var fl = new TextMeshPro[fn.Length];
        for (int i = 0; i < fn.Length; i++)
        {
            float y = Mathf.Tan((6f - i * 2.8f) * Mathf.Deg2Rad) * HudL;
            fl[i] = HudTxt(hud, "F" + fn[i], "", new Vector3((xr0 + xr1) * 0.5f, y, HudL), 0.12f, TS, TextAlignmentOptions.Center);
        }
        c.hudFlags = fl;
        foreach (var t in hud.GetComponentsInChildren<TextMeshPro>(true)) t.fontStyle = FontStyles.Bold;
        foreach (var r in hud.GetComponentsInChildren<MeshRenderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // Omega の緑の HUD の整理: 速度計・高度計の絵は下の丸い計器に任せて消し、数字を上に詰める
    static void OmegaHudTidy(controller c, Transform root)
    {
        Transform main = root.Find("MainPanel");
        if (main != null)
        {
            foreach (var nm in new[] { "SpdDial", "AltDial" })
            {
                Transform o = main.Find(nm);
                if (o != null) Object.DestroyImmediate(o.gameObject);
            }
            void Move(string nm, float x, float y)
            {
                Transform t = main.Find(nm);
                if (t != null) t.localPosition = new Vector3(x, y, t.localPosition.z);
            }
            Move("SpdLabel", -0.15f, 0.09f);
            Move("AltLabel", 0.15f, 0.09f);
            Move("Speed", -0.15f, 0.03f);
            Move("Alt", 0.15f, 0.03f);
            if (c.cpAtt != null) { var at = c.cpAtt.transform; at.localPosition = new Vector3(0f, -0.01f, at.localPosition.z); }
            Move("Warn", 0f, -0.05f);
            Move("Status", 0f, -0.085f);
        }
        // 横の表示は機体（翼・胴体）に重ならないよう、水平線より上（空の方）へ
        void Place(string nm, Vector3 pos)
        {
            Transform t = root.Find(nm);
            if (t == null) return;
            t.localPosition = pos;
            t.localRotation = Quaternion.LookRotation(pos.normalized, Vector3.up);   // 目の方を向ける
        }
        Place("LeftPanel", new Vector3(-0.40f, 0.10f, 0.66f));     // 迎角・フラップ・ブレーキなど
        Place("WeaponPanel", new Vector3(-0.62f, 0.10f, 0.48f));   // HP・武器
        Place("RightPanel", new Vector3(0.62f, 0.10f, 0.48f));     // スロットル・上昇率・燃料（レーダーの右）
        Place("LogPanel", new Vector3(0.62f, 0.38f, 0.48f));       // 入室記録（右上）
        Undo.RecordObject(c, "Flight setup");
        c.cpSpdNeedle = null; c.cpAltNeedle = null; c.cpAltNeedleK = null;
        // 前に作った線の HUD は使わない
        c.hudFpm = null; c.hudLadder = null; c.hudLadderLines = null; c.hudLadderDeg = null; c.hudRollPtr = null;
        c.hudTapeTicks = null; c.hudTapeLabels = null; c.hudHdg = null; c.hudSpd = null; c.hudAoa = null; c.hudMach = null;
        c.hudG = null; c.hudMaxG = null; c.hudGun = null; c.hudRkt = null; c.hudMsl = null; c.hudAlt = null; c.hudVs = null;
        c.hudThrFill = null; c.hudHpFill = null; c.hudStickDot = null; c.hudYawDot = null; c.hudFlags = null;
        c.cpHdgNum = null; c.cpVsNum = null; c.cpFuelPct = null;
    }

    // 戦闘機の四角い画面（多機能表示装置）: 液晶の周りに押しボタンが並ぶ
    static Transform Mfd(Transform p, string name, Vector2 pos, Vector2 size, Material lcd, Material frame, Material btn)
    {
        Transform t = Lcd(p, name, pos, size, lcd, frame);
        float bw = size.x * 0.13f, bh = 0.006f;
        for (int i = 0; i < 5; i++)
        {
            float x = (i - 2) * size.x * 0.2f;
            Quad(t, "OsbT" + i, new Vector3(x, size.y / 2f + 0.0075f, -0.0005f), new Vector2(bw, bh), btn);
            Quad(t, "OsbB" + i, new Vector3(x, -size.y / 2f - 0.0075f, -0.0005f), new Vector2(bw, bh), btn);
        }
        return t;
    }

    // 計器の黒い縁（円柱）
    static void Bezel(Transform p, Vector2 pos, float dia, Material m)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(b.GetComponent<Collider>());
        b.name = "Bezel";
        b.transform.SetParent(p, false);
        b.transform.localPosition = new Vector3(pos.x, pos.y, 0.00175f);   // 文字盤（-0.001）より奥、計器盤から少し出る
        b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        b.transform.localScale = new Vector3(dia * 1.14f, 0.00225f, dia * 1.14f);
        var mr = b.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 小さな液晶画面（黒い縁＋暗い緑の画面）。子に文字を入れる
    static Transform Lcd(Transform p, string name, Vector2 pos, Vector2 size, Material lcd, Material frame)
    {
        var t = new GameObject(name).transform;
        t.SetParent(p, false);
        t.localPosition = new Vector3(pos.x, pos.y, -0.001f);
        var fr = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(fr.GetComponent<Collider>());
        fr.name = "Frame";
        fr.transform.SetParent(t, false);
        fr.transform.localPosition = new Vector3(0f, 0f, 0.005f);
        fr.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.008f);
        fr.GetComponent<MeshRenderer>().sharedMaterial = frame;
        Quad(t, "Screen", Vector3.zero, size, lcd);
        return t;
    }

    // 丸い計器の文字盤: 黒地に白い目盛り。色の帯（arcs: 始め,終わり の組・0〜1）を内側に
    static Color AnalogPix(float u, float v, float start, float sweep, int majors, int minorsPer, float[] arcs, Color[] arcCols, float[] arcInner)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        Color face = new Color(0.05f, 0.05f, 0.055f, 1f);
        Color c = face;
        int total = majors * minorsPer;
        float step = sweep / total;
        float rel = (ClockAngle(u, v) - start + 720f) % 360f;
        bool full = sweep >= 359f;
        // 色の帯
        if (arcs != null && rel <= sweep + 0.5f)
        {
            float f = rel / sweep;
            for (int i = 0; i + 1 < arcs.Length; i += 2)
            {
                float inner = arcInner != null && i / 2 < arcInner.Length ? arcInner[i / 2] : 0.70f;
                if (f >= arcs[i] && f <= arcs[i + 1] && r > inner && r < 0.79f) c = arcCols[i / 2];
            }
        }
        int k = Mathf.RoundToInt(rel / step);
        if (full) k %= total;
        if (k >= 0 && (full || k <= total))
        {
            float dAng = rel - k * step;
            if (dAng > 180f) dAng -= 360f;
            float perp = Mathf.Abs(r * Mathf.Sin(dAng * Mathf.Deg2Rad));
            bool major = k % minorsPer == 0;
            float inner = major ? 0.78f : 0.87f;
            if (r > inner && r < 0.97f && perp < (major ? 0.022f : 0.011f)) c = new Color(0.95f, 0.95f, 0.92f, 1f);
        }
        if (r > 0.975f) c = new Color(0.12f, 0.12f, 0.13f, 1f);   // 外周
        return c;
    }

    // 姿勢指示器の球: 上半分が空（青）、下半分が地面（茶）。白い地平線と 10° ごとの線（縦 1 枚 = 180°）
    static Color AttitudeBallPix(float u, float v)
    {
        float t = (v + 1f) * 0.5f;                  // 0（下）〜1（上）
        float deg = (t - 0.5f) * 180f;              // 地平線からの角度
        Color c = deg >= 0f ? new Color(0.22f, 0.55f, 0.88f) : new Color(0.50f, 0.33f, 0.18f);
        float px = 2f / 256f * 180f;                // 1 ピクセルの角度
        if (Mathf.Abs(deg) < px * 1.5f) return Color.white;
        float m = Mathf.Repeat(deg + 5f, 10f) - 5f;
        float w = Mathf.Abs(deg) % 20f < 1f || Mathf.Abs(deg) % 20f > 19f ? 0.35f : 0.2f;   // 20° ごとは長い線
        if (Mathf.Abs(m) < px * 0.8f && Mathf.Abs(u) < w && Mathf.Abs(deg) < 85f) return Color.white;
        return c;
    }

    // 姿勢指示器の縁: 外側は黒、上半分に傾きの目盛り（0・10・20・30・60°）、中は透明
    static Color AttitudeMaskPix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        if (r < 0.80f) return new Color(0f, 0f, 0f, 0f);
        Color c = new Color(0.05f, 0.05f, 0.055f, 1f);
        float ang = ClockAngle(u, v);
        if (ang > 180f) ang -= 360f;
        foreach (float tk in new[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f, 60f, -60f })
        {
            float perp = Mathf.Abs(r * Mathf.Sin((ang - tk) * Mathf.Deg2Rad));
            float len = (tk == 30f || tk == -30f || tk == 60f || tk == -60f) ? 0.86f : 0.90f;
            if (Mathf.Abs(ang - tk) < 20f && r > len && r < 0.98f && perp < (tk == 0f ? 0.03f : 0.016f)) c = tk == 0f ? new Color(1f, 0.55f, 0.1f) : new Color(0.95f, 0.95f, 0.92f);
        }
        return c;
    }

    // 旋回計の文字盤: 黒地に、水平と「1 秒 3°」の目盛り（±20°）
    static Color TurnFacePix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        Color c = new Color(0.05f, 0.05f, 0.055f, 1f);
        float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg;   // 右 = 0、上 = 90
        foreach (float tk in new[] { 0f, -20f, 180f, 200f })
        {
            float dd = Mathf.DeltaAngle(ang, tk);
            float perp = Mathf.Abs(r * Mathf.Sin(dd * Mathf.Deg2Rad));
            if (Mathf.Abs(dd) < 30f && r > 0.72f && r < 0.95f && perp < 0.025f) c = new Color(0.95f, 0.95f, 0.92f, 1f);
        }
        if (r > 0.975f) c = new Color(0.12f, 0.12f, 0.13f, 1f);
        return c;
    }

    static bool fontOvOutlined;
    static void ConvertToHud(controller c, Transform root)
    {
        fontOvOutlined = false;
        // パネルの飾り（背景・箱・角・線・砂嵐）を消す
        var kill = new System.Collections.Generic.List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n == "BG" || n == "Housing" || n == "Corner" || n == "TopEdge" || n == "BottomEdge" || n == "Static" || n == "Sep" || n == "Sep2" || n.EndsWith("BarBG") || n == "Hub")
                kill.Add(t.gameObject);
        }
        foreach (var g in kill) if (g != null) Object.DestroyImmediate(g);

        // 文字: 緑・何にも隠れない（オーバーレイ）
        Material fontOv = null;
        foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (fontOv == null)
            {
                string fp = FxDir + "/HUD_Font.mat";
                fontOv = AssetDatabase.LoadAssetAtPath<Material>(fp);
                if (fontOv == null)
                {
                    fontOv = new Material(tmp.fontSharedMaterial);
                    Shader ov = Shader.Find("TextMeshPro/Distance Field Overlay");
                    if (ov != null) fontOv.shader = ov;
                    fontOv.renderQueue = 3100;
                    AssetDatabase.CreateAsset(fontOv, fp);
                }
            }
            if (!fontOvOutlined)
            {
                // 黒い縁取り: 明るい空・暗い機体のどちらに重なっても読める
                fontOvOutlined = true;
                fontOv.EnableKeyword("OUTLINE_ON");
                if (fontOv.HasProperty("_OutlineWidth")) fontOv.SetFloat("_OutlineWidth", 0.22f);
                if (fontOv.HasProperty("_OutlineColor")) fontOv.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.85f));
                if (fontOv.HasProperty("_FaceDilate")) fontOv.SetFloat("_FaceDilate", 0.1f);
                EditorUtility.SetDirty(fontOv);
            }
            tmp.fontSharedMaterial = fontOv;
            tmp.color = new Color(HudGreen.r, HudGreen.g, HudGreen.b, tmp.color.a);
        }

        // 計器・バー・レーダーなどの絵: 緑・オーバーレイ版のマテリアルに
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TextMeshPro>() != null) continue;
            Material m = r.sharedMaterial;
            if (m == null) continue;
            if (m.name == "CockpitBlackout" || m.name == "CockpitDamageTint" || m.name.StartsWith("LockRed") || m.name.StartsWith("HUD_") || m.name.StartsWith("RadarMap")) continue;
            r.sharedMaterial = HudMat(m);
        }
        Undo.RecordObject(c, "Flight setup");
        if (c.radarMats != null) { var a = new Material[c.radarMats.Length]; for (int i = 0; i < a.Length; i++) a[i] = HudMat(c.radarMats[i]); c.radarMats = a; }
        if (c.radarMslMats != null) { var a = new Material[c.radarMslMats.Length]; for (int i = 0; i < a.Length; i++) a[i] = HudMat(c.radarMslMats[i]); c.radarMslMats = a; }
        c.staticMat = null;
    }

    // 元のマテリアルを、何にも隠れない（ZTest Always）・透ける・緑（色付きの記号はそのままの色）の HUD 用にしたもの
    static Material HudMat(Material m)
    {
        if (m == null) return null;
        string path = FxDir + "/HUD_" + m.name + ".mat";
        var h = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = h == null;
        if (create) h = new Material(Shader.Find("FlightUI/UnlitTint"));
        Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        // 計器の文字盤とレーダーの画面は、暗い塗りつぶしを薄くした版を使う
        if (m.name == "DialSpeed") tex = GenTex("dial_speed_hud.png", 256, (u, v) => HudFade(DialPix(u, v, -135f, 270f, 6, 5)));
        else if (m.name == "DialAlt") tex = GenTex("dial_alt_hud.png", 256, (u, v) => HudFade(DialPix(u, v, 0f, 360f, 10, 5)));
        else if (m.name == "RadarScope") tex = GenTex("radar_scope_hud.png", 512, (u, v) => HudFade(RadarScopePix(u, v)));
        Color src = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        bool keepColor = m.name.StartsWith("RadarIcon") || m.name.StartsWith("RadarMsl") || m.name == "CockpitRed" || m.name == "ModeHard";
        Color col = keepColor ? src : new Color(HudGreen.r, HudGreen.g, HudGreen.b, Mathf.Clamp01(src.a) * 0.95f);
        if (tex != null) h.SetTexture("_MainTex", tex);
        h.SetColor("_Color", col);
        h.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        h.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        h.SetFloat("_ZWrite", 0f);
        h.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        h.SetOverrideTag("RenderType", "Transparent");
        h.renderQueue = 3000 + Mathf.Clamp(m.renderQueue - 2985, 1, 99);   // 元の重なり順を保つ
        if (create) AssetDatabase.CreateAsset(h, path);
        else EditorUtility.SetDirty(h);
        return h;
    }

    // 暗い塗りつぶしの部分だけ、ほとんど透明にする（線や目盛りは残す）
    static Color HudFade(Color c)
    {
        float bright = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (bright < 0.25f) c.a *= 0.12f;
        return c;
    }

    static Material TexMat(string name, Texture2D tex, Color col, int queue)
    {
        Material m = UnlitMat(name, col, true, queue);
        m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", col);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = queue;
        EditorUtility.SetDirty(m);
        return m;
    }

    static float Line(float dist, float hw) { return 1f - Mathf.Clamp01((Mathf.Abs(dist) - hw) / 0.006f); }

    // 時計回りに上から測った角度（度, 0〜360）
    static float ClockAngle(float u, float v) { float a = Mathf.Atan2(u, v) * Mathf.Rad2Deg; return a < 0f ? a + 360f : a; }

    static Color DialPix(float u, float v, float start, float sweep, int majors, int minorsPer)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        Color face = new Color(0.01f, 0.05f, 0.09f, 0.92f);
        Color cyan = new Color(0.35f, 0.95f, 1f, 1f);
        float ink = Line(r - 0.965f, 0.012f) * 0.9f;   // 外周の輪
        float ink2 = 0f;
        int total = majors * minorsPer;
        float step = sweep / total;
        float rel = (ClockAngle(u, v) - start + 720f) % 360f;
        int k = Mathf.RoundToInt(rel / step);
        bool full = sweep >= 359f;
        if (full) k %= total;
        if (k >= 0 && (full || k <= total))
        {
            float dAng = (rel - k * step);
            if (dAng > 180f) dAng -= 360f;
            float perp = Mathf.Abs(r * Mathf.Sin(dAng * Mathf.Deg2Rad));
            bool major = k % minorsPer == 0;
            float inner = major ? 0.76f : 0.86f;
            if (r > inner && r < 0.95f) { float l = Line(perp, major ? 0.016f : 0.008f); if (major) ink2 = Mathf.Max(ink2, l); else ink = Mathf.Max(ink, l * 0.8f); }
        }
        Color c = Color.Lerp(face, cyan, ink);
        c = Color.Lerp(c, Color.white, ink2);
        float edge = 1f - Mathf.Clamp01((r - 0.99f) / 0.01f);
        c.a = Mathf.Max(face.a, Mathf.Max(ink, ink2)) * edge;
        return c;
    }
}
