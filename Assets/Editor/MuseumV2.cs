using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using TMPro;

// 博物館（アイギス島 記録館）を新しい構成にする（aegis_museum.md / aegis_panels.md）
//  ・倉庫を 2 棟つなげて広くする（西側に 1 棟足し、間の壁を外す）
//  ・入口ホール → A 帝国の時代 → B 崩壊の時代 → C ペラゴスの時代（島のミニチュア）→ D 分岐の間 → E 慰霊の間
//  ・展示物: ①地図 ②S11（SM_aircraft_） ③ミニチュア ④銃のショーケース（VintageRifle） ⑤条約書 2 枚
// メニュー「Tools/博物館を新しい構成にする」。Backup_flight/run_museum2.flag があれば再読み込み時に一度だけ自動実行。
[InitializeOnLoad]
public static class MuseumV2Trigger
{
    const string Flag = "Backup_flight/run_museum2.flag";
    static MuseumV2Trigger() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        // 手で並べ直した展示を壊さないように、自動実行ではミニチュアだけ作り直す（全部作り直すのはメニューから）
        if (MuseumSetup.HasExhibits()) MuseumSetup.RebuildMiniature();
        else MuseumSetup.RunV2();
    }
    public static void Again() { File.WriteAllText(Flag, "again"); EditorApplication.delayCall += Try; }
}

public static partial class MuseumSetup
{
    const string P2 = MusDir + "/Panels2/";
    static bool retried2;

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }
    // under の見た目の範囲を space の座標で
    static Bounds BoundsIn(Transform space, Transform under)
    {
        bool has = false; Bounds b = new Bounds();
        foreach (var r in under.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                Vector3 lp = space.InverseTransformPoint(r.transform.TransformPoint(p));
                if (!has) { b = new Bounds(lp, Vector3.zero); has = true; } else b.Encapsulate(lp);
            }
        }
        return b;
    }

    public static bool HasExhibits()
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == MuseumName && FindDeep(go.transform, "Exhibits") != null) return true;
        return false;
    }

    // ミニチュアだけ作り直す（位置・向きはそのまま）
    [MenuItem("Tools/博物館のミニチュアだけ作り直す")]
    public static void RebuildMiniature()
    {
        var log = new StringBuilder();
        log.AppendLine("MiniatureOnly " + System.DateTime.Now);
        try
        {
            GameObject museum = null;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == MuseumName) museum = go;
            Transform R = FindDeep(museum != null ? museum.transform : null, "Exhibits");
            Transform old = FindDeep(R, "Miniature");
            if (R == null || old == null) { log.AppendLine("ERROR: Exhibits か Miniature が無い"); return; }
            Transform par = old.parent; Vector3 wp = old.position; Quaternion wr = old.rotation; Vector3 ls = old.localScale;
            Undo.DestroyObjectImmediate(old.gameObject);
            BuildMiniature(R, Vector3.zero, 3.0f, log);
            Transform nm = R.Find("Miniature");
            if (nm != null) { nm.SetParent(par, true); nm.SetPositionAndRotation(wp, wr); nm.localScale = ls; }
            var sc = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally { File.WriteAllText("Backup_flight/museum2_report.txt", log.ToString()); }
    }

    [MenuItem("Tools/博物館を新しい構成にする")]
    public static void RunV2()
    {
        var log = new StringBuilder();
        log.AppendLine("MuseumV2 " + System.DateTime.Now);
        Scene demo = default(Scene); bool demoOpened = false;
        try
        {
            if (EditorApplication.isPlaying) { log.AppendLine("ERROR: 再生中"); return; }
            bool ok = EnsureProg(MusDir + "/ModePanel.cs", MusDir + "/ModePanel.asset");
            if (!ok && !retried2)
            {
                retried2 = true;
                AssetDatabase.Refresh();
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
                MuseumV2Trigger.Again();
                log.AppendLine("ModePanel のプログラムを作りました。もう一度実行します");
                return;
            }
            Scene main = SceneManager.GetActiveScene();
            GameObject museum = null;
            foreach (var go in main.GetRootGameObjects()) if (go.name == MuseumName) museum = go;
            if (museum == null) { log.AppendLine("ERROR: 博物館が無い"); return; }
            Transform wh = FindDeep(museum.transform, "Warehouse");
            Transform door = FindDeep(museum.transform, "ReturnDoor");
            if (wh == null || door == null) { log.AppendLine("ERROR: Warehouse か ReturnDoor が無い"); return; }

            // ---------- 1) 倉庫を最初の 1 棟に戻す（2 棟にした場合は作り直す） ----------
            if (wh.Find("West") != null)
            {
                Transform par = wh.parent; Vector3 wp = wh.position; Quaternion wr = wh.rotation; int si = wh.GetSiblingIndex();
                Undo.DestroyObjectImmediate(wh.gameObject);
                demo = EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Additive); demoOpened = true;
                GameObject src = null;
                foreach (var go in demo.GetRootGameObjects()) if (go.name == "Factory1Items") src = go;
                if (src == null) { log.AppendLine("ERROR: Factory1Items が無い"); return; }
                var wgo = new GameObject("Warehouse");
                Undo.RegisterCreatedObjectUndo(wgo, "Museum2");
                SceneManager.MoveGameObjectToScene(wgo, main);
                wgo.transform.SetParent(par, false);
                wgo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                wgo.transform.localScale = Vector3.one;
                int copied = 0, skipped = 0;
                CopyChildren(src.transform, wgo.transform, ref copied, ref skipped);
                EditorSceneManager.CloseScene(demo, true); demoOpened = false;
                AddLamps(wgo.transform, LocalBounds(wgo.transform), log);
                wgo.transform.SetPositionAndRotation(wp, wr);
                wgo.transform.SetSiblingIndex(si);
                wh = wgo.transform;
                log.AppendLine("倉庫: 最初の 1 棟の大きさに戻しました");
            }
            // ② S11 は外に戻す
            Transform s11 = FindDeep(museum.transform, "S11");
            if (s11 != null)
            {
                Undo.RecordObject(s11, "Museum2");
                s11.name = "SM_aircraft_ (1)";
                s11.position = new Vector3(-5120f, -10f, -4377f);
                s11.rotation = Quaternion.identity;
                log.AppendLine("S11: 外の元の場所 (-5120, -10, -4377) に戻しました（向きは 0°。違っていたら手で直してください）");
            }
            Bounds all = LocalBounds(wh);

            // ---------- 2) 展示の根元（入口のドア。+z = 奥） ----------
            Transform oldEx = FindDeep(museum.transform, "Exhibits");
            if (oldEx != null) Undo.DestroyObjectImmediate(oldEx.gameObject);
            var rgo = new GameObject("Exhibits");
            Undo.RegisterCreatedObjectUndo(rgo, "Museum2");
            rgo.transform.SetParent(door.parent, true);
            Quaternion rr = Quaternion.Euler(0f, door.eulerAngles.y, 0f);
            Vector3 hallC = wh.TransformPoint(all.center);
            if (Vector3.Dot(rr * Vector3.forward, hallC - door.position) < 0f) rr *= Quaternion.Euler(0f, 180f, 0f);
            rgo.transform.SetPositionAndRotation(door.position, rr);
            Transform R = rgo.transform;
            float xmin = float.MaxValue, xmax = float.MinValue, zmax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? all.min.x : all.max.x, all.min.y, (i & 4) == 0 ? all.min.z : all.max.z);
                Vector3 l = R.InverseTransformPoint(wh.TransformPoint(c));
                xmin = Mathf.Min(xmin, l.x); xmax = Mathf.Max(xmax, l.x); zmax = Mathf.Max(zmax, l.z);
            }
            float far = zmax - 0.6f;
            float side = Mathf.Min(3.6f, (xmax - xmin) * 0.5f - 2.2f);
            log.AppendLine("広さ: 幅 " + (xmax - xmin).ToString("F1") + " 奥 " + far.ToString("F1"));

            Material frame = Mat("StandFrame", null, new Color(0.13f, 0.12f, 0.11f));
            float PW = 1.6f, PH = 1.6f * 2880f / 2048f;
            // 通路の左右に交互に。少し入口側に傾ける（最初と同じ並べ方）
            System.Func<int, float> sx = k => (k % 2 == 0 ? -1f : 1f) * side;
            System.Func<int, float> sy = k => (k % 2 == 0 ? 90f + 25f : -90f - 25f);

            // ---------- 入口 ----------
            Panel(R, "Sign", P2 + "sign.png", new Vector3(-side * 0.75f, 0f, 3.4f), 180f + 20f, 2.0f, 1.25f, 0.9f, frame);
            SetupAudioKiosk(R, wh, all, new Vector3(side * 0.75f, 0f, 3.2f), 180f - 20f, frame, log);

            // ---------- 順番に並べる ----------
            float z = 8f, gap = Mathf.Min(4.5f, (far - 23f) / 11.8f);
            int k2 = 0;
            // ① 地図
            var mapTex = Tex(MusDir + "/Panels/map.png", 2048);
            if (mapTex != null)
                Stand("Map", R, R.TransformPoint(new Vector3(sx(k2), 0f, z)), R.rotation * Quaternion.Euler(0f, sy(k2), 0f), Mat("Map", mapTex, Color.white), 2.8f, 2.8f * mapTex.height / mapTex.width, 0.8f, frame);
            Lectern(R, "Cap_Map", P2 + "cap_map.png", new Vector3(sx(k2) * 0.55f, 0f, z - 1.2f), sy(k2), frame);
            k2++; z += gap;
            // 7, 1, 2, 3
            foreach (var key in new[] { "p07", "p01", "p02", "p03" })
            {
                Panel(R, "Panel_" + key, P2 + key + ".png", new Vector3(sx(k2), 0f, z), sy(k2), PW, PH, 0.5f, frame);
                if (key == "p03")
                {
                    Showcase(R, new Vector3(sx(k2) * 0.45f, 0f, z + 1.4f), sy(k2) - 25f * Mathf.Sign(sx(k2)), frame, log);
                    Lectern(R, "Cap_Rifle", P2 + "cap_rifle.png", new Vector3(sx(k2) * 0.45f, 0f, z - 0.4f), sy(k2), frame);
                }
                k2++; z += gap;
            }
            // 4, 5（崩壊の時代）
            foreach (var key in new[] { "p04", "p05" }) { Panel(R, "Panel_" + key, P2 + key + ".png", new Vector3(sx(k2), 0f, z), sy(k2), PW, PH, 0.5f, frame); k2++; z += gap; }
            // 6 とミニチュア
            Panel(R, "Panel_p06", P2 + "p06.png", new Vector3(sx(k2), 0f, z), sy(k2), PW, PH, 0.5f, frame); k2++;
            float zMini = z + gap * 0.9f;
            BuildMiniature(R, new Vector3(0f, 0f, zMini), 3.0f, log);
            Lectern(R, "Cap_Mini", P2 + "cap_mini.png", new Vector3(0f, 0f, zMini - 2.3f), 180f, frame);
            z = zMini + gap * 1.1f;
            // ⑤ 条約書 → 8
            TreatyTable(R, new Vector3(-0.9f, 0f, z), frame);
            Lectern(R, "Cap_Treaty", P2 + "cap_treaty.png", new Vector3(1.5f, 0f, z), 180f, frame);
            z += gap * 0.8f;
            Panel(R, "Panel_p08", P2 + "p08.png", new Vector3(sx(k2), 0f, z), sy(k2), PW, PH, 0.5f, frame); k2++; z += gap;
            Panel(R, "Panel_p09", P2 + "p09.png", new Vector3(sx(k2), 0f, z), sy(k2), PW, PH, 0.5f, frame); k2++; z += gap;
            float zEndLine = z - gap;

            // 床の年表の線（通路の真ん中。崩壊の時代で枝分かれ、第9パネルで終わる）
            var tl = new GameObject("Timeline").transform; tl.SetParent(R, false);
            Material lineM = Mat("TimelineLine", null, new Color(0.9f, 0.8f, 0.55f));
            lineM.EnableKeyword("_EMISSION"); lineM.SetColor("_EmissionColor", new Color(1f, 0.82f, 0.5f) * 0.8f);
            float zB = 8f + gap * 4.6f;
            Seg(tl, new Vector3(0f, 0f, 8f + gap), new Vector3(0f, 0f, zMini - 2.8f), 0.07f, lineM);
            for (int b = 1; b <= 3; b++) Seg(tl, new Vector3(0f, 0f, zB + b * 0.5f), new Vector3((b % 2 == 0 ? 1f : -1f) * 0.45f * b, 0f, zB + gap * 1.8f), 0.035f, lineM);
            Seg(tl, new Vector3(0f, 0f, zMini + 1.8f), new Vector3(0f, 0f, zEndLine), 0.07f, lineM);
            Seg(tl, new Vector3(-0.5f, 0f, zEndLine), new Vector3(0.5f, 0f, zEndLine), 0.09f, lineM);

            // ---------- D 分岐の間 ----------
            float zD0 = z + 1.0f, zD1 = Mathf.Min(zD0 + 5.5f, far - 6.6f);
            Panel(R, "Panel_p10", P2 + "p10.png", new Vector3(0f, 0f, zD0 - 1.0f), 180f, PW, PH, 0.5f, frame);
            Material part = Mat("Partition", null, new Color(0.25f, 0.27f, 0.3f));
            var dz = new GameObject("Fork").transform; dz.SetParent(R, false);
            foreach (float px in new[] { -side - 0.2f, 0f, side + 0.2f })
                Box("Partition", dz, new Vector3(px, 1.8f, (zD0 + zD1) * 0.5f), new Vector3(0.12f, 3.6f, zD1 - zD0), part, true);
            float zm = (zD0 + zD1) * 0.5f;
            Panel(dz, "Panel_Left", P2 + "pL.png", new Vector3(-side - 0.12f, 0f, zm), 90f, PW, PH, 0.6f, frame, true);
            Panel(dz, "Panel_Right", P2 + "pR.png", new Vector3(side + 0.12f, 0f, zm), -90f, PW, PH, 0.6f, frame, true);
            PassLight(dz, new Vector3(-side * 0.5f, 3.4f, zm), new Color(0.45f, 0.65f, 1f), "LightLeft");
            PassLight(dz, new Vector3(side * 0.5f, 3.4f, zm), new Color(1f, 0.55f, 0.25f), "LightRight");

            // ---------- E 慰霊の間 ----------
            float zE0 = zD1 + 0.7f;
            var ez = new GameObject("Remembrance").transform; ez.SetParent(R, false);
            Material low = Mat("LowPanel", null, new Color(0.1f, 0.1f, 0.11f));
            float ex = side + 1.6f;
            Box("Low", ez, new Vector3(-ex, 0.65f, (zE0 + far) * 0.5f), new Vector3(0.1f, 1.3f, far - zE0), low, true);
            Box("Low", ez, new Vector3(ex, 0.65f, (zE0 + far) * 0.5f), new Vector3(0.1f, 1.3f, far - zE0), low, true);
            Box("Low", ez, new Vector3(-(ex + side + 0.2f) * 0.5f, 0.65f, zE0), new Vector3(ex - side - 0.2f, 1.3f, 0.1f), low, true);
            Box("Low", ez, new Vector3((ex + side + 0.2f) * 0.5f, 0.65f, zE0), new Vector3(ex - side - 0.2f, 1.3f, 0.1f), low, true);
            var p11 = Panel(ez, "Panel_p11", P2 + "p11.png", new Vector3(0f, 0f, far - 0.2f), 180f, 2.4f, 2.4f * 2880f / 2048f, 0.35f, frame);
            ModeSwitch(p11, log);
            SetupMemorialAt(ez, new Vector3(0f, 0f, zE0 + 1.5f), 180f, P2 + "p12.png", 1.9f, 1.9f * 1280f / 2048f, null, frame, log);
            // 慰霊の間に入ると曲が替わる範囲（分岐の通路の出口から奥まで）
            var mz = new GameObject("MemorialZone").transform; mz.SetParent(R, false);
            mz.localPosition = new Vector3(0f, 2.5f, (zD1 - 0.5f + far + 0.6f) * 0.5f);
            mz.localScale = new Vector3((xmax - xmin) + 1f, 6f, far + 0.6f - (zD1 - 0.5f));
            var maud = R.GetComponentInChildren<MuseumAudio>(true);
            if (maud != null) { maud.memorialZone = mz; UdonSharpEditorUtility.CopyProxyToUdon(maud); }
            log.AppendLine("曲: 館内は museum_bgm、慰霊の間（" + (zD1 - 0.5f).ToString("F1") + "m〜奥）に入ると shinkai に替わる");
            log.AppendLine("並べ方: 通路の左右に交互（間隔 " + gap.ToString("F1") + "m）。分岐 " + zD0.ToString("F1") + "〜" + zD1.ToString("F1") + "、慰霊の間 " + zE0.ToString("F1") + "〜" + far.ToString("F1"));

            // ② S11 のキャプション（外の機体の前）
            Transform plane = FindDeep(museum.transform, "SM_aircraft_");
            Transform oldCap = FindDeep(museum.transform, "Cap_S11_Outside");
            if (oldCap != null) Undo.DestroyObjectImmediate(oldCap.gameObject);
            if (plane != null)
            {
                Bounds pb = new Bounds(plane.position, Vector3.zero); bool hp = false;
                foreach (var r in plane.GetComponentsInChildren<Renderer>(true)) { if (!hp) { pb = r.bounds; hp = true; } else pb.Encapsulate(r.bounds); }
                var cg = new GameObject("CapHolder").transform; cg.SetParent(plane.parent, false);
                cg.position = new Vector3(pb.max.x + 1.8f, door.position.y, pb.center.z); cg.rotation = Quaternion.identity;
                Lectern(cg, "Cap_S11_Outside", P2 + "cap_plane.png", Vector3.zero, 90f, frame);
                var cap = cg.Find("Cap_S11_Outside"); cap.SetParent(plane.parent, true); Object.DestroyImmediate(cg.gameObject);
                log.AppendLine("S11: 外の SM_aircraft_ の東側にキャプションを置きました " + cap.position.ToString("F0"));
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(main);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(main) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally
        {
            if (demoOpened && demo.IsValid()) EditorSceneManager.CloseScene(demo, true);
            File.WriteAllText("Backup_flight/museum2_report.txt", log.ToString());
            Debug.Log(log.ToString());
        }
    }

    static float YawTo(Vector3 from, Vector3 to) { Vector3 d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

    // パネル（画像の大きさのまま）。yaw = R の中で板の表が向く向き
    static Transform Panel(Transform R, string name, string tex, Vector3 lp, float yaw, float w, float h, float bottom, Material frame, bool noLegs = false)
    {
        var t = Tex(tex, 2048);
        if (t == null) return null;
        var st = Stand(name, R, R.TransformPoint(lp), R.rotation * Quaternion.Euler(0f, yaw, 0f), Mat("V2_" + Path.GetFileNameWithoutExtension(tex), t, Color.white), w, h, bottom, frame);
        if (noLegs) foreach (Transform c in st) if (c.name == "Leg" || c.name == "Foot") c.gameObject.SetActive(false);
        return st;
    }

    // 斜めの書見台（キャプション用）
    static void Lectern(Transform R, string name, string tex, Vector3 lp, float yaw, Material frame)
    {
        var t = Tex(tex, 2048); if (t == null) return;
        var g = new GameObject(name).transform;
        g.SetParent(R, false); g.localPosition = lp; g.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Box("Post", g, new Vector3(0f, 0.5f, -0.1f), new Vector3(0.12f, 1.0f, 0.12f), frame, false);
        Box("Foot", g, new Vector3(0f, 0.02f, -0.1f), new Vector3(0.6f, 0.04f, 0.5f), frame, false);
        var top = new GameObject("Top").transform;
        top.SetParent(g, false); top.localPosition = new Vector3(0f, 1.08f, 0f); top.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        Box("Plate", top, new Vector3(0f, 0f, -0.02f), new Vector3(1.32f, 0.86f, 0.03f), frame, true);
        Quad("Face", top, new Vector3(0f, 0f, 0.0f), new Vector2(1.24f, 0.775f), Mat("V2_" + Path.GetFileNameWithoutExtension(tex), t, Color.white));
    }

    static void Seg(Transform p, Vector3 a, Vector3 b, float w, Material m)
    {
        Vector3 d = b - a; if (d.sqrMagnitude < 0.0001f) return;
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Line"; g.transform.SetParent(p, false);
        g.transform.localPosition = (a + b) * 0.5f + Vector3.up * 0.006f;
        g.transform.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        g.transform.localScale = new Vector3(w, 0.012f, d.magnitude);
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
    }

    static void PassLight(Transform p, Vector3 lp, Color c, string name)
    {
        var g = new GameObject(name); g.transform.SetParent(p, false); g.transform.localPosition = lp;
        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 7f; l.intensity = 1.3f; l.color = c; l.shadows = LightShadows.None;
    }

    // ② S11: 外にある SM_aircraft_ (1) を中へ（無ければ SM_aircraft_ を複製）
    static void PlaceS11(Transform museum, Transform R, Vector3 lp, float yaw, StringBuilder log)
    {
        Transform s = FindDeep(museum, "S11");
        bool moved = s != null;
        if (s == null) s = FindDeep(museum, "SM_aircraft_ (1)");
        if (s == null)
        {
            var src = FindDeep(museum, "SM_aircraft_");
            if (src == null) { log.AppendLine("S11: SM_aircraft_ が見つからない"); return; }
            var cp = Object.Instantiate(src.gameObject, src.parent);
            Undo.RegisterCreatedObjectUndo(cp, "Museum2");
            s = cp.transform;
        }
        if (moved) { log.AppendLine("S11: もう中にあります（位置はそのまま）"); return; }
        Undo.RecordObject(s, "Museum2");
        s.name = "S11";
        s.rotation = R.rotation * Quaternion.Euler(0f, yaw, 0f);
        Bounds b = BoundsIn(R, s);   // 回したあとの範囲
        Vector3 want = lp;
        Vector3 cur = new Vector3(b.center.x, b.min.y, b.center.z);
        s.position += R.TransformVector(want - cur);
        log.AppendLine("S11: 入口ホールに斜め " + yaw + "° で置きました（範囲 " + BoundsIn(R, s).size.ToString("F1") + "）");
    }

    // ④ 銃のショーケース
    static void Showcase(Transform R, Vector3 lp, float yaw, Material frame, StringBuilder log)
    {
        var g = new GameObject("Showcase").transform;
        g.SetParent(R, false); g.localPosition = lp; g.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Box("Pedestal", g, new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.7f), frame, true);
        Material cloth = Mat("ShowcaseCloth", null, new Color(0.35f, 0.08f, 0.07f));
        Box("Cloth", g, new Vector3(0f, 0.905f, 0f), new Vector3(1.5f, 0.01f, 0.6f), cloth, false);
        var glass = Mat("ShowcaseGlass", null, new Color(0.85f, 0.92f, 1f, 0.12f));
        glass.SetFloat("_Mode", 3f); glass.SetOverrideTag("RenderType", "Transparent");
        glass.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); glass.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glass.SetInt("_ZWrite", 0); glass.DisableKeyword("_ALPHATEST_ON"); glass.DisableKeyword("_ALPHABLEND_ON"); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        glass.renderQueue = 3000; glass.SetFloat("_Glossiness", 0.95f);
        Box("Glass", g, new Vector3(0f, 1.2f, 0f), new Vector3(1.6f, 0.6f, 0.7f), glass, true);
        var pre = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VintageRifle/Prefab/VintageRifle.prefab");
        if (pre == null) { log.AppendLine("ショーケース: VintageRifle が無い"); return; }
        var rf = (GameObject)PrefabUtility.InstantiatePrefab(pre, g);
        rf.transform.localPosition = Vector3.zero; rf.transform.localRotation = Quaternion.identity; rf.transform.localScale = Vector3.one;
        Bounds b = BoundsIn(g, rf.transform);
        // 一番長い向きを横（x）に、2 番目に長い向きを奥行き（寝かせる）
        Vector3 sz = b.size;
        Quaternion q = Quaternion.identity;
        if (sz.z >= sz.x && sz.z >= sz.y) q = Quaternion.Euler(0f, 90f, 0f);
        else if (sz.y >= sz.x && sz.y >= sz.z) q = Quaternion.Euler(0f, 0f, 90f);
        rf.transform.localRotation = q;
        b = BoundsIn(g, rf.transform);
        if (b.size.y > b.size.z) { rf.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * q; b = BoundsIn(g, rf.transform); }
        float k = 1.3f / Mathf.Max(b.size.x, 0.001f);
        rf.transform.localScale = Vector3.one * k;
        b = BoundsIn(g, rf.transform);
        rf.transform.localPosition += new Vector3(-b.center.x, 0.92f - b.min.y, -b.center.z);
        foreach (var c in rf.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        log.AppendLine("ショーケース: VintageRifle（長さ 1.3m に合わせた）");
    }

    // ⑤ 条約書 2 枚（左 = 歴120年、右 = 破棄された同じ文書）
    static void TreatyTable(Transform R, Vector3 lp, Material frame)
    {
        var g = new GameObject("TreatyTable").transform;
        g.SetParent(R, false); g.localPosition = lp; g.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Box("Base", g, new Vector3(0f, 0.45f, 0f), new Vector3(2.0f, 0.9f, 0.8f), frame, true);
        var top = new GameObject("Top").transform;
        top.SetParent(g, false); top.localPosition = new Vector3(0f, 1.0f, 0f); top.localRotation = Quaternion.Euler(-28f, 0f, 0f);
        Box("Plate", top, new Vector3(0f, 0f, -0.03f), new Vector3(2.0f, 1.12f, 0.04f), frame, true);
        string[] docs = { "treaty_120", "treaty_136" };
        for (int i = 0; i < 2; i++)
        {
            var t = Tex(P2 + docs[i] + ".png", 2048);
            if (t != null) Quad(docs[i], top, new Vector3(i == 0 ? -0.48f : 0.48f, 0f, 0f), new Vector2(0.8f, 0.8f * 2200f / 1700f), Mat("V2_" + docs[i], t, Color.white));
        }
    }

    // 第11パネル: モードで絵を替える
    static void ModeSwitch(Transform panel, StringBuilder log)
    {
        if (panel == null) return;
        var face = panel.Find("Face"); if (face == null) return;
        var gm = Object.FindObjectOfType<GameMode>(true);
        var tEx = Tex(P2 + "p11_ex.png", 2048); var tRc = Tex(P2 + "p11_rc.png", 2048);
        var mp = UdonSharpUndo.AddComponent<ModePanel>(panel.gameObject);
        mp.gameMode = gm;
        mp.target = face.GetComponent<Renderer>();
        mp.exercise = Mat("V2_p11_ex", tEx, Color.white);
        mp.recon = Mat("V2_p11_rc", tRc, Color.white);
        UdonSharpEditorUtility.CopyProxyToUdon(mp);
        log.AppendLine("第11パネル: モードで切り替え（遊覧 = 合同演習 / 普通・ハード = 偵察）" + (gm == null ? " ※GameMode が無い" : ""));
    }

    // 音（BGM・音声ガイド）と範囲・音声ガイドの台
    static void SetupAudioKiosk(Transform R, Transform wh, Bounds lb, Vector3 kp, float kyaw, Material frame, StringBuilder log)
    {
        var audioGo = new GameObject("MuseumAudio");
        audioGo.transform.SetParent(R, false);
        var bgm = audioGo.AddComponent<AudioSource>();
        bgm.clip = Clip(MusDir + "/museum_bgm.mp3", true);
        bgm.loop = true; bgm.playOnAwake = false; bgm.spatialBlend = 0f; bgm.volume = 0f; bgm.priority = 64;
        var bgm2 = new GameObject("MemorialBgm").AddComponent<AudioSource>(); bgm2.transform.SetParent(audioGo.transform, false);
        bgm2.clip = Clip(MusDir + "/shinkai.mp3", true);
        bgm2.loop = true; bgm2.playOnAwake = false; bgm2.spatialBlend = 0f; bgm2.volume = 0f; bgm2.priority = 64;
        var gJa = new GameObject("GuideJa").AddComponent<AudioSource>(); gJa.transform.SetParent(audioGo.transform, false); gJa.clip = Clip(MusDir + "/guide_ja.ogg", true);
        var gEn = new GameObject("GuideEn").AddComponent<AudioSource>(); gEn.transform.SetParent(audioGo.transform, false); gEn.clip = Clip(MusDir + "/guide_en.ogg", true);
        foreach (var g in new[] { gJa, gEn }) { g.playOnAwake = false; g.loop = false; g.spatialBlend = 0f; g.priority = 32; }
        var zone = new GameObject("MuseumZone").transform;
        zone.SetParent(R, true);
        zone.position = wh.TransformPoint(lb.center); zone.rotation = wh.rotation; zone.localScale = Vector3.one;
        Vector3 zs = lb.size + new Vector3(1f, 2f, 1f); Vector3 ps = zone.parent.lossyScale;
        zone.localScale = new Vector3(zs.x / ps.x, zs.y / ps.y, zs.z / ps.z);
        var kiosk = new GameObject("AudioGuide").transform;
        kiosk.SetParent(R, false); kiosk.localPosition = kp; kiosk.localRotation = Quaternion.Euler(0f, kyaw, 0f);
        var signTex = Tex(MusDir + "/Panels/guide_sign.png", 1024);
        Box("Base", kiosk, new Vector3(0f, 0.5f, 0f), new Vector3(1.3f, 1.0f, 0.45f), frame, true);
        Box("SignFrame", kiosk, new Vector3(0f, 1.62f, -0.12f), new Vector3(1.32f, 0.9f, 0.05f), frame, false);
        if (signTex != null) Quad("Sign", kiosk, new Vector3(0f, 1.62f, -0.09f), new Vector2(1.2f, 0.8f), Mat("GuideSign", signTex, Color.white));
        var mats = new[] { Mat("GuideJa", null, new Color(0.6f, 0.16f, 0.14f)), Mat("GuideEn", null, new Color(0.14f, 0.28f, 0.56f)) };
        var onMat = Mat("GuideOn", null, new Color(0.2f, 1f, 0.3f));
        onMat.EnableKeyword("_EMISSION"); onMat.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.3f) * 1.5f);
        var marks = new GameObject[2];
        var ma = UdonSharpUndo.AddComponent<MuseumAudio>(audioGo);
        for (int i = 0; i < 2; i++)
        {
            float bx = i == 0 ? -0.33f : 0.33f;
            var b = Box(i == 0 ? "ButtonJa" : "ButtonEn", kiosk, new Vector3(bx, 1.06f, 0.02f), new Vector3(0.42f, 0.12f, 0.3f), mats[i], true);
            marks[i] = Box("Playing", kiosk, new Vector3(bx, 1.15f, 0.12f), new Vector3(0.1f, 0.05f, 0.05f), onMat, false);
            marks[i].SetActive(false);
            var gb = UdonSharpUndo.AddComponent<MuseumGuideButton>(b);
            gb.museum = ma; gb.lang = i;
            UdonSharpEditorUtility.CopyProxyToUdon(gb);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(gb);
            if (ub != null) { ub.interactText = i == 0 ? "音声ガイド（日本語）再生／停止" : "Audio guide (English) play / stop"; ub.proximity = 2.5f; }
        }
        ma.zone = zone; ma.bgm = bgm; ma.bgm2 = bgm2; ma.guides = new[] { gJa, gEn }; ma.playingMarks = marks;
        ma.volume = Object.FindObjectOfType<VolumeSettings>(true);
        UdonSharpEditorUtility.CopyProxyToUdon(ma);
        foreach (var gmode in Object.FindObjectsOfType<GameMode>(true)) { Undo.RecordObject(gmode, "Museum2"); gmode.museum = ma; UdonSharpEditorUtility.CopyProxyToUdon(gmode); }
        log.AppendLine("音声ガイド・BGM: 倉庫全体（2 棟）で流れる");
    }

    // ③ ペラゴス諸島のミニチュア（Terrain から小さな地形を作る）
    static readonly Color[] LayerCol = { new Color(0.36f, 0.42f, 0.2f), new Color(0.45f, 0.36f, 0.28f), new Color(0.43f, 0.43f, 0.42f), new Color(0.84f, 0.75f, 0.55f), new Color(0.52f, 0.52f, 0.5f) };
    static void BuildMiniature(Transform R, Vector3 lp, float size, StringBuilder log)
    {
        var g = new GameObject("Miniature").transform;
        g.SetParent(R, false); g.localPosition = lp; g.localRotation = Quaternion.identity;
        Material wood = Mat("MiniTable", null, new Color(0.25f, 0.17f, 0.1f));
        Box("Table", g, new Vector3(0f, 0.42f, 0f), new Vector3(size + 0.4f, 0.84f, size + 0.4f), wood, true);
        Material sea = Mat("MiniSea", null, new Color(0.1f, 0.27f, 0.42f));
        sea.SetFloat("_Glossiness", 0.85f);
        Box("Sea", g, new Vector3(0f, 0.85f, 0f), new Vector3(size, 0.02f, size), sea, false);
        float seaTop = 0.861f;
        // 島
        var src = new List<Terrain>(); var labels = new List<string>(); var roots = new List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Terrain>(true))
        {
            string n = t.name; Transform root = t.transform;
            if (n.StartsWith("Astral")) { labels.Add("ASTRAL"); }
            else if (n == "人がいる島") { labels.Add("SMALL ISLANDS"); }
            else if (n == IslandName) { labels.Add("AEGIS"); }
            else if (t.transform.parent != null && t.transform.parent.name == "military base") { labels.Add("PELAGOS"); root = t.transform.parent; }
            else continue;
            src.Add(t); roots.Add(root);
        }
        if (src.Count == 0) { log.AppendLine("ミニチュア: 島が見つからない"); return; }
        // 四角い Terrain の台（一番多い高さ）は海に沈める: 台の高さ - 1m が海面になるように下げる
        var drop = new float[src.Count];
        for (int i = 0; i < src.Count; i++)
        {
            var td = src[i].terrainData; float py = src[i].transform.position.y;
            var hist = new Dictionary<int, int>();
            for (int y = 0; y <= 64; y++) for (int x = 0; x <= 64; x++)
                {
                    int bin = Mathf.FloorToInt(py + td.GetInterpolatedHeight(x / 64f, y / 64f));
                    int c; hist.TryGetValue(bin, out c); hist[bin] = c + 1;
                }
            int best = 0, bc = -1;
            foreach (var kv in hist) if (kv.Value > bc) { bc = kv.Value; best = kv.Key; }
            // 本物の島の四角い台はもう海に沈めてあるので、ここでは下げない。ペラゴス島（海面すぐ上の基地）が沈まないよう、全部 1.5m 上げる
            drop[i] = -1.5f;
            log.AppendLine("ミニチュア: " + labels[i] + " は 1.5m 上げる（一番多い高さ " + best + "m）");
        }
        // 陸のある範囲から縮尺を決める
        Bounds land = new Bounds(); bool has = false;
        for (int ti = 0; ti < src.Count; ti++)
        {
            var t = src[ti];
            var td = t.terrainData; Vector3 p = t.transform.position;
            for (int y = 0; y <= 32; y++) for (int x = 0; x <= 32; x++)
                {
                    float h = p.y + td.GetInterpolatedHeight(x / 32f, y / 32f) - drop[ti];
                    if (h < SeaY + 0.5f) continue;
                    Vector3 w = new Vector3(p.x + x / 32f * td.size.x, 0f, p.z + y / 32f * td.size.z);
                    if (!has) { land = new Bounds(w, Vector3.zero); has = true; } else land.Encapsulate(w);
                }
        }
        float span = Mathf.Max(land.size.x, land.size.z) * 1.1f;
        float s = (size * 0.92f) / Mathf.Max(span, 1f);
        float v = s * 3f;   // 高さは 3 倍
        Vector3 c0 = land.center;
        Directory.CreateDirectory(MusDir + "/Mini");
        Material bld = Mat("MiniBuilding", null, new Color(0.62f, 0.6f, 0.56f));
        for (int i = 0; i < src.Count; i++)
        {
            var t = src[i]; var td = t.terrainData; Vector3 p = t.transform.position;
            int N = 110;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var hgt = new float[N + 1, N + 1];
            for (int y = 0; y <= N; y++) for (int x = 0; x <= N; x++)
                {
                    float u = x / (float)N, w = y / (float)N;
                    float h = p.y + td.GetInterpolatedHeight(u, w) - drop[i];
                    hgt[x, y] = h;
                    float wx = p.x + u * td.size.x, wz = p.z + w * td.size.z;
                    verts.Add(new Vector3((wx - c0.x) * s, seaTop + Mathf.Max(h - SeaY, -2f) * v, (wz - c0.z) * s));
                    uvs.Add(new Vector2(u, w));
                }
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    if (Mathf.Max(Mathf.Max(hgt[x, y], hgt[x + 1, y]), Mathf.Max(hgt[x, y + 1], hgt[x + 1, y + 1])) < SeaY + 0.2f) continue;
                    int a = y * (N + 1) + x, b = a + 1, cI = a + N + 1, d = cI + 1;
                    tris.Add(a); tris.Add(cI); tris.Add(b); tris.Add(b); tris.Add(cI); tris.Add(d);
                }
            string mp = MusDir + "/Mini/Mini_" + labels[i].Replace(" ", "_") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, mp); }
            mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            // 色（今の塗りから上から見た絵を作る）
            int res = 256; var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            int ar = td.alphamapResolution; float[,,] am = td.GetAlphamaps(0, 0, ar, ar); int L = Mathf.Min(td.alphamapLayers, LayerCol.Length);
            var layers = td.terrainLayers;
            for (int y = 0; y < res; y++) for (int x = 0; x < res; x++)
                {
                    int ax = x * ar / res, ay = y * ar / res; Color col = Color.black;
                    for (int k = 0; k < td.alphamapLayers; k++)
                    {
                        Color lc = k < LayerCol.Length && layers[k] != null && layers[k].name.StartsWith("Real_") ? LayerCol[k] : new Color(0.4f, 0.42f, 0.3f);
                        col += lc * am[ay, ax, k];
                    }
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            string tp = MusDir + "/Mini/Mini_" + labels[i].Replace(" ", "_") + ".png";
            File.WriteAllBytes(tp, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(tp);
            var it = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            Material im = Mat("Mini_" + labels[i].Replace(" ", "_"), null, Color.white);
            im.mainTexture = it; EditorUtility.SetDirty(im);
            var ig = new GameObject("Island_" + labels[i]); ig.transform.SetParent(g, false);
            ig.AddComponent<MeshFilter>().sharedMesh = mesh; ig.AddComponent<MeshRenderer>().sharedMaterial = im;
            // 建物（小さな箱）
            int nb = 0;
            foreach (Transform ch in roots[i])
            {
                string n = ch.name;
                if (ch.GetComponent<Terrain>() != null || n.StartsWith("Cube") || n.StartsWith("solider") || n == "大砲" || n == "Exhibits" || n == "ReturnDoor" || n.StartsWith("SM_") || n == "S11") continue;
                var rs = ch.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) continue;
                Bounds bb = new Bounds(); bool hb = false;
                foreach (var r in rs) { if (r is ParticleSystemRenderer) continue; if (!hb) { bb = r.bounds; hb = true; } else bb.Encapsulate(r.bounds); }
                if (!hb || bb.size.x > 250f || bb.size.z > 250f) continue;
                float by = seaTop + Mathf.Max(bb.min.y - drop[i] - SeaY, 0f) * v;
                Vector3 bs = new Vector3(Mathf.Max(bb.size.x * s, 0.008f), Mathf.Max(bb.size.y * v * 0.5f, 0.006f), Mathf.Max(bb.size.z * s, 0.008f));
                Box("Bldg", ig.transform, new Vector3((bb.center.x - c0.x) * s, by + bs.y * 0.5f, (bb.center.z - c0.z) * s), bs, bld, false);
                nb++;
            }
            // 名札
            Bounds mb = mesh.bounds;
            var lab = new GameObject("Label"); lab.transform.SetParent(ig.transform, false);
            lab.transform.localPosition = new Vector3(mb.center.x, Mathf.Max(mb.max.y, seaTop) + 0.12f, mb.center.z);
            var tm = lab.AddComponent<TextMeshPro>();
            tm.text = labels[i]; tm.fontSize = 0.18f * size; tm.alignment = TextAlignmentOptions.Center; tm.color = new Color(1f, 0.95f, 0.8f);
            tm.rectTransform.sizeDelta = new Vector2(0.35f * size, 0.05f * size);
            log.AppendLine("ミニチュア: " + labels[i] + "（建物 " + nb + " 個）");
        }
        log.AppendLine("ミニチュア: 縮尺 1/" + (1f / s).ToString("F0") + "（高さは 3 倍）");
    }
}
