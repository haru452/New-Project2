using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using TMPro;

// 博物館: TirgamesAssets の大きい倉庫（FactoryDay の Factory1）を組み立てて、「博物館」の中のアイギス島に置く
//  ・持ってくる物: 建物（壁・床・屋根・柱・階段）と扉。瓦礫・ドラム缶・配電盤・クレーン・ファン・汚れなどは持ってこない
//  ・天井の照明（LampCeiling01、光源なしの見た目だけ）を並べる
//  ・置き場所: アイギス島の一番平らで海より上の場所を自動で探し、建物の下の地面を平らにならす
// メニュー「Tools/博物館の倉庫を組み立てる」から実行できる。
// Backup_flight/run_museum.flag があれば、Unity の再読み込み時に一度だけ自動実行する。
// 作り直すときは 博物館/Warehouse を消してから作る（手で動かした位置・向きはそのまま使う）。
[InitializeOnLoad]
public static partial class MuseumSetup
{
    const string FlagPath = "Backup_flight/run_museum.flag";
    const string ReportPath = "Backup_flight/museum_report.txt";
    const string DemoScene = "Assets/TirgamesAssets/Factory/Scenes/FactoryDay.unity";
    const string LampPrefab = "Assets/TirgamesAssets/Factory/Prefabs/LampCeiling01.prefab";
    const string MuseumName = "博物館";
    const string IslandName = "アイギス島";
    const float SeaY = -27.7f;
    const string DoorPrefab = "Assets/TirgamesAssets/Factory/Prefabs/DoorIndustrial01_1.prefab";
    const string DoorScript = "Assets/My script/transport/DoorTeleport.cs";
    const string DoorAsset = "Assets/My script/transport/DoorTeleport.asset";
    static bool retried;

    static MuseumSetup()
    {
        EditorApplication.delayCall += TryAutoRun;
        EditorApplication.playModeStateChanged += st =>
        {
            if (st == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAutoRun;
        };
    }

    static void TryAutoRun()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoRun;
            return;
        }
        File.Delete(FlagPath);
        Run();
    }

    // 持ってくる物か（名前で判断）
    static bool Keep(string n)
    {
        if (n.StartsWith("Factory1Column") || n.StartsWith("Factory1Floor") || n.StartsWith("Factory1Roof")
            || n.StartsWith("Factory1Stairs") || n.StartsWith("Factory1Wall")) return true;
        if (n.StartsWith("LargeGates") || n.StartsWith("DoorIndustrial") || n.StartsWith("LampCeiling")) return true;
        return false;
    }
    static bool KeepGroup(string n) { return n == "Factory1Modular" || n == "Gates"; }

    [MenuItem("Tools/博物館の倉庫を組み立てる")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("MuseumSetup " + System.DateTime.Now);
        Scene demo = default(Scene);
        bool demoOpened = false;
        try
        {
            if (EditorApplication.isPlaying) { log.AppendLine("ERROR: 再生中は実行できません"); return; }
            Scene main = SceneManager.GetActiveScene();
            bool ok = EnsureProgram(log);
            ok &= EnsureProg("Assets/My script/museum/MuseumAudio.cs", "Assets/My script/museum/MuseumAudio.asset");
            ok &= EnsureProg("Assets/My script/museum/MuseumGuideButton.cs", "Assets/My script/museum/MuseumGuideButton.asset");
            ok &= EnsureProg("Assets/My script/museum/MemorialBox.cs", "Assets/My script/museum/MemorialBox.asset");
            ok &= EnsureProg("Assets/My script/museum/PaperCrane.cs", "Assets/My script/museum/PaperCrane.asset");
            if (!ok && !retried)
            {
                AssetDatabase.Refresh();
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
                retried = true;
                File.WriteAllText(FlagPath, "door");
                log.AppendLine("DoorTeleport: プログラムを作りました。もう一度自動実行します");
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            // 博物館とアイギス島
            GameObject museum = null;
            foreach (var go in main.GetRootGameObjects()) if (go.name == MuseumName) { museum = go; break; }
            if (museum == null)
            {
                museum = new GameObject(MuseumName);
                Undo.RegisterCreatedObjectUndo(museum, "Museum setup");
                log.AppendLine("博物館: 空のゲームオブジェクトを作りました");
            }
            Terrain island = null;
            foreach (var t in Object.FindObjectsOfType<Terrain>(true)) if (t.name == IslandName) { island = t; break; }
            if (island == null) { log.AppendLine("ERROR: アイギス島（Terrain）が見つからない"); return; }
            if (island.transform.parent != museum.transform)
                log.AppendLine("注意: アイギス島が博物館の中にありません（倉庫は博物館の中に作ります）");

            // 前に作った倉庫（手で動かした位置・向きは使う）
            Transform old = museum.transform.Find("Warehouse");
            bool hadOld = old != null;
            Vector3 oldPos = hadOld ? old.position : Vector3.zero;
            Quaternion oldRot = hadOld ? old.rotation : Quaternion.identity;
            if (hadOld) Undo.DestroyObjectImmediate(old.gameObject);

            // デモシーンを一時的に開く
            demo = EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Additive);
            demoOpened = true;
            GameObject src = null;
            foreach (var go in demo.GetRootGameObjects()) if (go.name == "Factory1Items") { src = go; break; }
            if (src == null) { log.AppendLine("ERROR: FactoryDay に Factory1Items が無い"); return; }

            var wh = new GameObject("Warehouse");
            Undo.RegisterCreatedObjectUndo(wh, "Museum setup");
            SceneManager.MoveGameObjectToScene(wh, main);
            wh.transform.SetParent(museum.transform, false);
            wh.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            wh.transform.localScale = Vector3.one;
            int copied = 0, skipped = 0;
            CopyChildren(src.transform, wh.transform, ref copied, ref skipped);
            log.AppendLine("倉庫: " + copied + " 個の部品を持ってきました（持ってこなかった物 " + skipped + " 個）");
            EditorSceneManager.CloseScene(demo, true);
            demoOpened = false;

            // 建物の大きさ（倉庫の向きの中で）
            Bounds lb = LocalBounds(wh.transform);
            log.AppendLine("倉庫の大きさ: " + lb.size.ToString("F1") + "  中心 " + lb.center.ToString("F1"));

            // 天井の照明
            int lamps = AddLamps(wh.transform, lb, log);
            log.AppendLine("天井の照明: " + lamps + " 個");

            if (hadOld)
            {
                wh.transform.SetPositionAndRotation(oldPos, oldRot);
                log.AppendLine("置き場所: 前の倉庫と同じ位置・向き " + oldPos.ToString("F1"));
            }
            else
            {
                float yaw; Vector3 c; float floorY; float range;
                if (!FindFlat(island, lb, out c, out yaw, out floorY, out range, log)) return;
                Quaternion r = Quaternion.Euler(0f, yaw, 0f);
                // 床の高さ = 地面。建物の中心（倉庫の向きの中の lb.center の xz）を c に合わせる
                Vector3 off = r * new Vector3(lb.center.x, 0f, lb.center.z);
                wh.transform.SetPositionAndRotation(new Vector3(c.x - off.x, floorY, c.z - off.z), r);
                log.AppendLine("置き場所: " + wh.transform.position.ToString("F1") + " 向き " + yaw + "°  地面の高低差 " + range.ToString("F1") + "m");
                Flatten(island, wh.transform, lb, floorY, log);
            }

            SetupDoors(museum.transform, wh.transform, log);
            SetupExhibits(museum.transform, wh.transform, log);

            EditorSceneManager.MarkSceneDirty(main);
            bool saved = EditorSceneManager.SaveScene(main);
            log.AppendLine("シーン保存: " + (saved ? "OK" : "失敗"));
            Selection.activeGameObject = wh;
            log.AppendLine("DONE");
        }
        catch (System.Exception e)
        {
            log.AppendLine("EXCEPTION: " + e);
        }
        finally
        {
            if (demoOpened && demo.IsValid()) EditorSceneManager.CloseScene(demo, true);
            Directory.CreateDirectory("Backup_flight");
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log(log.ToString());
        }
    }

    // デモの階層を、プレハブのつながりを保ったまま写す
    static void CopyChildren(Transform from, Transform to, ref int copied, ref int skipped)
    {
        foreach (Transform ch in from)
        {
            GameObject g = ch.gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(g))
            {
                if (!Keep(g.name)) { skipped++; continue; }
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(g);
                if (asset == null) { skipped++; continue; }
                var cp = (GameObject)PrefabUtility.InstantiatePrefab(asset, to);
                cp.name = g.name;
                cp.transform.localPosition = ch.localPosition;
                cp.transform.localRotation = ch.localRotation;
                cp.transform.localScale = ch.localScale;
                cp.SetActive(g.activeSelf);
                copied++;
            }
            else if (KeepGroup(g.name))
            {
                var grp = new GameObject(g.name);
                grp.transform.SetParent(to, false);
                grp.transform.localPosition = ch.localPosition;
                grp.transform.localRotation = ch.localRotation;
                grp.transform.localScale = ch.localScale;
                CopyChildren(ch, grp.transform, ref copied, ref skipped);
            }
            else skipped++;
        }
    }

    static Bounds LocalBounds(Transform root)
    {
        bool has = false;
        Bounds b = new Bounds();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 mn = mb.min, mx = mb.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                Vector3 lp = root.InverseTransformPoint(r.transform.TransformPoint(p));
                if (!has) { b = new Bounds(lp, Vector3.zero); has = true; } else b.Encapsulate(lp);
            }
        }
        return b;
    }

    // 天井の照明を 2 列に並べる（上に向けて調べ、天井に付ける）
    static int AddLamps(Transform wh, Bounds lb, StringBuilder log)
    {
        var lamp = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefab);
        if (lamp == null) { log.AppendLine("天井の照明: LampCeiling01 が無い"); return 0; }
        // ランプの上端（根元からの高さ）
        float top = 0f;
        var lmf = lamp.GetComponentInChildren<MeshFilter>(true);
        if (lmf != null && lmf.sharedMesh != null) top = lmf.transform.localPosition.y + lmf.sharedMesh.bounds.max.y * lmf.transform.localScale.y;
        var grp = new GameObject("Lamps");
        grp.transform.SetParent(wh, false);
        Physics.SyncTransforms();
        bool longZ = lb.size.z >= lb.size.x;
        float len = longZ ? lb.size.z : lb.size.x, wid = longZ ? lb.size.x : lb.size.z;
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 9f));
        int made = 0;
        for (int row = -1; row <= 1; row += 2)
        {
            for (int i = 0; i < n; i++)
            {
                float a = -len * 0.5f + len * (i + 0.5f) / n;
                float s = row * wid * 0.22f;
                Vector3 lp = lb.center + (longZ ? new Vector3(s, 0f, a) : new Vector3(a, 0f, s));
                lp.y = lb.min.y + 1.5f;
                Vector3 wp = wh.TransformPoint(lp);
                float best = float.MaxValue;
                foreach (var h in Physics.RaycastAll(wp, wh.up, lb.size.y + 5f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.transform.IsChildOf(wh) && h.distance < best && h.distance > 2.5f) best = h.distance;
                if (best == float.MaxValue) continue;
                var cp = (GameObject)PrefabUtility.InstantiatePrefab(lamp, grp.transform);
                cp.transform.position = wp + wh.up * (best - top);
                cp.transform.rotation = wh.rotation;
                made++;
            }
        }
        return made;
    }

    // 一番平らで海より上の場所を探す（建物の床の下の地面の高低差が一番小さい所。同じくらいなら島の中心に近い所）
    static bool FindFlat(Terrain t, Bounds lb, out Vector3 center, out float yaw, out float floorY, out float range, StringBuilder log)
    {
        center = Vector3.zero; yaw = 0f; floorY = 0f; range = 0f;
        TerrainData td = t.terrainData;
        int res = td.heightmapResolution;
        float[,] hs = td.GetHeights(0, 0, res, res);
        Vector3 tp = t.transform.position, sz = td.size;
        float cell = sz.x / (res - 1);
        // 島の中心（海より上の場所の平均）
        Vector3 ic = Vector3.zero; int icn = 0;
        for (int z = 0; z < res; z += 2)
            for (int x = 0; x < res; x += 2)
            {
                float h = tp.y + hs[z, x] * sz.y;
                if (h > SeaY + 1f) { ic += new Vector3(tp.x + x * cell, 0f, tp.z + z * (sz.z / (res - 1))); icn++; }
            }
        if (icn == 0) { log.AppendLine("ERROR: アイギス島に海より上の地面が無い"); return false; }
        ic /= icn;
        float halfA = lb.size.x * 0.5f + 3f, halfB = lb.size.z * 0.5f + 3f;   // 周り 3m の余裕
        float bestScore = float.MaxValue;
        float step = Mathf.Max(3f, cell);
        for (int yi = 0; yi < 2; yi++)
        {
            float hx = yi == 0 ? halfA : halfB, hz = yi == 0 ? halfB : halfA;   // 90° 回すと縦横が入れ替わる
            for (float cz = tp.z + hz; cz <= tp.z + sz.z - hz; cz += step)
                for (float cx = tp.x + hx; cx <= tp.x + sz.x - hx; cx += step)
                {
                    float mn = float.MaxValue, mx = float.MinValue, sum = 0f; int cnt = 0;
                    bool bad = false;
                    for (float dz = -hz; dz <= hz && !bad; dz += 3f)
                        for (float dx = -hx; dx <= hx; dx += 3f)
                        {
                            int ix = Mathf.Clamp(Mathf.RoundToInt((cx + dx - tp.x) / cell), 0, res - 1);
                            int iz = Mathf.Clamp(Mathf.RoundToInt((cz + dz - tp.z) / (sz.z / (res - 1))), 0, res - 1);
                            float h = tp.y + hs[iz, ix] * sz.y;
                            if (h < SeaY + 1.5f) { bad = true; break; }
                            mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h); sum += h; cnt++;
                        }
                    if (bad || cnt == 0) continue;
                    float d = new Vector2(cx - ic.x, cz - ic.z).magnitude;
                    float score = (mx - mn) + d * 0.003f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        center = new Vector3(cx, 0f, cz);
                        yaw = yi == 0 ? 0f : 90f;
                        floorY = sum / cnt;
                        range = mx - mn;
                    }
                }
        }
        if (bestScore == float.MaxValue) { log.AppendLine("ERROR: アイギス島に倉庫が入る広さの陸地が無い"); return false; }
        return true;
    }

    // 建物の下の地面を床の高さにならす（周り 10m でなめらかにつなぐ）
    static void Flatten(Terrain t, Transform wh, Bounds lb, float floorY, StringBuilder log)
    {
        TerrainData td = t.terrainData;
        string ap = AssetDatabase.GetAssetPath(td);
        if (!string.IsNullOrEmpty(ap))
        {
            Directory.CreateDirectory("Backup_flight/museum");
            string bk = "Backup_flight/museum/" + Path.GetFileName(ap);
            if (!File.Exists(bk)) File.Copy(ap, bk);
        }
        Undo.RegisterCompleteObjectUndo(td, "Museum setup");
        int res = td.heightmapResolution;
        float[,] hs = td.GetHeights(0, 0, res, res);
        Vector3 tp = t.transform.position, sz = td.size;
        float target = (floorY - 0.05f - tp.y) / sz.y;
        float hx = lb.size.x * 0.5f + 1f, hz = lb.size.z * 0.5f + 1f, blend = 10f;
        int changed = 0;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Vector3 w = new Vector3(tp.x + x * sz.x / (res - 1), floorY, tp.z + z * sz.z / (res - 1));
                Vector3 l = wh.InverseTransformPoint(w) - new Vector3(lb.center.x, 0f, lb.center.z);
                float ox = Mathf.Max(0f, Mathf.Abs(l.x) - hx), oz = Mathf.Max(0f, Mathf.Abs(l.z) - hz);
                float o = Mathf.Sqrt(ox * ox + oz * oz);
                if (o >= blend) continue;
                float k = o <= 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, o / blend);
                hs[z, x] = Mathf.Lerp(hs[z, x], target, k);
                changed++;
            }
        td.SetHeights(0, 0, hs);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        log.AppendLine("地面をならしました: " + changed + " 点（元の地形は Backup_flight/museum に保存）");
    }
    static bool EnsureProgram(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(DoorAsset) != null) return true;
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(DoorScript);
        if (ms == null) { log.AppendLine("ERROR: " + DoorScript + " が見つからない"); return true; }
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, DoorAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        return false;
    }

    // ---------------- ドア（main room ⇔ 博物館） ----------------
    // 手で動かしたドアは、その位置・向きのまま作り直す
    static void SetupDoors(Transform museum, Transform wh, StringBuilder log)
    {
        GameObject mainRoom = null;
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == "mainRoom") { mainRoom = go; break; }
        if (mainRoom == null) { log.AppendLine("ドア: mainRoom が見つからない"); return; }
        Physics.SyncTransforms();

        // main room のドア: 西の壁（部屋の中心から -x に向けて調べる）
        Vector3 mPos; Quaternion mRot;
        Transform oldM = mainRoom.transform.Find("MuseumDoor");
        if (oldM != null) { mPos = oldM.position; mRot = oldM.rotation; Undo.DestroyObjectImmediate(oldM.gameObject); }
        else if (!WallSpot(new Vector3(-5f, 1.5f, -12f), Vector3.left, mainRoom.transform, out mPos, out mRot, log, "main room")) return;

        // 博物館のドア: 倉庫の端の壁（倉庫の中心から倉庫の後ろ向きに調べる）
        Bounds lb = LocalBounds(wh);
        Vector3 rPos; Quaternion rRot;
        Transform oldR = museum.Find("ReturnDoor");
        if (oldR != null) { rPos = oldR.position; rRot = oldR.rotation; Undo.DestroyObjectImmediate(oldR.gameObject); }
        else
        {
            Vector3 c = wh.TransformPoint(new Vector3(lb.center.x, lb.min.y + 1.5f, lb.center.z));
            if (!WallSpot(c, -wh.forward, wh, out rPos, out rRot, log, "博物館")) return;
        }

        var mDoor = MakeDoor("MuseumDoor", mainRoom.transform, mPos, mRot, "MUSEUM", "博物館へ / To Museum", log);
        var rDoor = MakeDoor("ReturnDoor", museum, rPos, rRot, "MAIN ROOM", "main room へ / To Main Room", log);
        if (mDoor == null || rDoor == null) return;
        // 着く場所: 相手のドアの 2m 前、ドアに背を向ける
        Transform mArr = MakeArrive(mDoor.transform, "Arrive");
        Transform rArr = MakeArrive(rDoor.transform, "Arrive");
        SetTarget(mDoor, rArr);
        SetTarget(rDoor, mArr);
        log.AppendLine("ドア: main room " + mPos.ToString("F1") + " ⇔ 博物館 " + rPos.ToString("F1"));
    }

    // 中から dir の向きに調べて、当たった壁の手前の床の上（ドアの正面 = 壁から部屋の内側）
    static bool WallSpot(Vector3 from, Vector3 dir, Transform within, out Vector3 pos, out Quaternion rot, StringBuilder log, string label)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        float best = float.MaxValue; RaycastHit bh = new RaycastHit();
        foreach (var h in Physics.RaycastAll(from, dir, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!h.collider.transform.IsChildOf(within)) continue;
            if (Mathf.Abs(h.normal.y) > 0.3f) continue;
            if (h.distance < best) { best = h.distance; bh = h; }
        }
        if (best == float.MaxValue) { log.AppendLine("ドア: " + label + " の壁が見つからない"); return false; }
        Vector3 n = bh.normal; n.y = 0f; n.Normalize();
        Vector3 p = bh.point + n * 0.15f;
        // 床の高さ
        RaycastHit fh;
        if (Physics.Raycast(bh.point + n * 1f + Vector3.up * 1f, Vector3.down, out fh, 20f, ~0, QueryTriggerInteraction.Ignore)) p.y = fh.point.y;
        else p.y = from.y - 1.5f;
        pos = p;
        rot = Quaternion.LookRotation(n, Vector3.up);
        return true;
    }

    static DoorTeleport MakeDoor(string name, Transform parent, Vector3 pos, Quaternion rot, string sign, string interact, StringBuilder log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefab);
        if (prefab == null) { log.AppendLine("ドア: DoorIndustrial01_1 が無い"); return null; }
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Museum setup");
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        // 親の大きさに左右されないように、世界での大きさを 1 に
        Vector3 ls = root.transform.lossyScale;
        root.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        root.transform.SetPositionAndRotation(pos, rot);
        var door = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        door.transform.localPosition = Vector3.zero;
        door.transform.localRotation = Quaternion.identity;
        Bounds b = LocalBounds(root.transform);
        // ドアの真ん中を根元に、下端を床に、厚みの中心を根元に
        door.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        b = LocalBounds(root.transform);
        // 触る所（ドア全体）
        var bc = root.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, b.size.y * 0.5f, 0.12f);
        bc.size = new Vector3(b.size.x, b.size.y, 0.3f);
        // 看板
        var sg = new GameObject("Sign");
        sg.transform.SetParent(root.transform, false);
        sg.transform.localPosition = new Vector3(0f, b.size.y + 0.35f, 0.2f);
        sg.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 部屋の内側から読める向き
        var t = sg.AddComponent<TextMeshPro>();
        t.text = sign;
        t.fontSize = 3f;
        t.color = new Color(1f, 0.85f, 0.4f, 1f);
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.rectTransform.sizeDelta = new Vector2(4f, 0.8f);
        // ドアの音
        AudioSource au = null;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/My script/drone/Audio/door.wav");
        if (clip != null)
        {
            au = root.AddComponent<AudioSource>();
            au.clip = clip; au.playOnAwake = false; au.spatialBlend = 1f; au.minDistance = 2f; au.maxDistance = 20f;
        }
        var dt = UdonSharpUndo.AddComponent<DoorTeleport>(root);
        dt.sound = au;
        UdonSharpEditorUtility.CopyProxyToUdon(dt);
        UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(dt);
        if (ub != null) { ub.interactText = interact; ub.proximity = 3f; }
        return dt;
    }

    static Transform MakeArrive(Transform door, string name)
    {
        var a = new GameObject(name);
        a.transform.SetParent(door, false);
        a.transform.position = door.position + door.forward * 2f + Vector3.up * 0.05f;
        a.transform.rotation = Quaternion.LookRotation(door.forward, Vector3.up);
        return a.transform;
    }

    static void SetTarget(DoorTeleport d, Transform t)
    {
        Undo.RecordObject(d, "Museum setup");
        d.target = t;
        UdonSharpEditorUtility.CopyProxyToUdon(d);
    }
    static bool EnsureProg(string script, string asset)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) return true;
        if (AssetDatabase.LoadAssetAtPath<Object>(asset) != null) AssetDatabase.DeleteAsset(asset);   // 別の物（前に間違えて作った形など）があったら消す
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(script);
        if (ms == null) return true;
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, asset);
        AssetDatabase.SaveAssets();
        return false;   // まとめて後でコンパイルする
    }

    // ---------------- 展示（年表パネル・地図・音声ガイド・BGM） ----------------
    const string MusDir = "Assets/My script/museum";
    const int PanelCount = 10;

    static Texture2D Tex(string path, int max)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;
        bool ch = false;
        if (ti.maxTextureSize != max) { ti.maxTextureSize = max; ch = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; ch = true; }
        if (ti.anisoLevel != 8) { ti.anisoLevel = 8; ch = true; }
        if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
        if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
        if (ch) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static AudioClip Clip(string path, bool stream)
    {
        var ai = AssetImporter.GetAtPath(path) as AudioImporter;
        if (ai == null) return null;
        var st = ai.defaultSampleSettings;
        bool ch = false;
        var lt = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
        if (st.loadType != lt) { st.loadType = lt; ch = true; }
        if (st.compressionFormat != AudioCompressionFormat.Vorbis) { st.compressionFormat = AudioCompressionFormat.Vorbis; ch = true; }
        if (Mathf.Abs(st.quality - 0.6f) > 0.01f) { st.quality = 0.6f; ch = true; }
        if (ch) { ai.defaultSampleSettings = st; ai.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static Material Mat(string name, Texture2D tex, Color col)
    {
        string p = MusDir + "/Materials/" + name + ".mat";
        Directory.CreateDirectory(MusDir + "/Materials");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null)
        {
            m = new Material(Shader.Find(tex != null ? "Unlit/Texture" : "Standard"));
            AssetDatabase.CreateAsset(m, p);
        }
        if (tex != null) { m.shader = Shader.Find("Unlit/Texture"); m.mainTexture = tex; }
        else { m.shader = Shader.Find("Standard"); m.color = col; m.SetFloat("_Glossiness", 0.25f); }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Box(string name, Transform parent, Vector3 lp, Vector3 size, Material m, bool collider)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    static GameObject Quad(string name, Transform parent, Vector3 lp, Vector2 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 表を stand の前（+z）に向ける
        g.transform.localScale = new Vector3(size.x, size.y, 1f);
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    // 展示台: 板（幅 w・高さ h、下端 bottom）＋ 黒い枠 ＋ 脚 2 本
    static Transform Stand(string name, Transform parent, Vector3 pos, Quaternion rot, Material face, float w, float h, float bottom, Material frame)
    {
        var st = new GameObject(name).transform;
        st.SetParent(parent, false);
        st.position = pos; st.rotation = rot;
        float cy = bottom + h * 0.5f;
        Box("Frame", st, new Vector3(0f, cy, -0.04f), new Vector3(w + 0.12f, h + 0.12f, 0.06f), frame, true);
        Quad("Face", st, new Vector3(0f, cy, -0.005f), new Vector2(w, h), face);
        for (int i = -1; i <= 1; i += 2)
            Box("Leg", st, new Vector3(i * (w * 0.5f - 0.1f), bottom * 0.5f, -0.04f), new Vector3(0.08f, bottom, 0.08f), frame, false);
        Box("Foot", st, new Vector3(0f, 0.03f, -0.04f), new Vector3(w * 0.8f, 0.06f, 0.5f), frame, false);
        return st;
    }

    static void SetupExhibits(Transform museum, Transform wh, StringBuilder log)
    {
        // 前に作った物（手で動かした位置・向きは使う）
        Transform old = museum.Find("Exhibits");
        bool keep = old != null;
        Vector3 keepPos = keep ? old.position : Vector3.zero;
        Quaternion keepRot = keep ? old.rotation : Quaternion.identity;
        if (keep) Undo.DestroyObjectImmediate(old.gameObject);

        Bounds lb = LocalBounds(wh);
        Transform door = museum.Find("ReturnDoor");
        Vector3 dl = door != null ? wh.InverseTransformPoint(door.position) : new Vector3(lb.center.x, lb.min.y, lb.min.z);
        float s = dl.z < lb.center.z ? 1f : -1f;            // 入口から奥へ向かう向き（倉庫の z）
        float floorY = dl.y;
        float cx = lb.center.x;

        var root = new GameObject("Exhibits");
        Undo.RegisterCreatedObjectUndo(root, "Museum setup");
        root.transform.SetParent(museum, false);
        // 根元 = 入口のドアの位置・倉庫の向き（奥 = +z）
        root.transform.position = wh.TransformPoint(new Vector3(cx, floorY, dl.z));
        root.transform.rotation = wh.rotation * Quaternion.Euler(0f, s > 0f ? 0f : 180f, 0f);
        Transform R = root.transform;

        Material frame = Mat("StandFrame", null, new Color(0.13f, 0.12f, 0.11f));
        float len = (s > 0f ? lb.max.z - dl.z : dl.z - lb.min.z);
        int n = PanelCount + 1;   // パネル 10 枚 + 地図
        float gap = Mathf.Min(5.6f, (len - 9f) / (n - 1));
        float side = Mathf.Min(3.6f, lb.size.x * 0.5f - 2.2f);
        int made = 0;
        for (int i = 0; i < n; i++)
        {
            float z = 6f + i * gap;
            float x = (i % 2 == 0 ? -1f : 1f) * side;
            // 通路の方を向き、少し入口側に傾ける
            Quaternion r = Quaternion.Euler(0f, (i % 2 == 0 ? 90f : -90f) + (i % 2 == 0 ? 25f : -25f), 0f);
            Vector3 p = new Vector3(x, 0f, z);
            if (i == 1)
            {
                var tex = Tex(MusDir + "/Panels/map.png", 2048);
                if (tex == null) { log.AppendLine("展示: map.png が無い"); continue; }
                var st = Stand("Map", R, R.TransformPoint(p), R.rotation * r, Mat("Map", tex, Color.white), 2.6f, 2.6f * tex.height / tex.width, 0.9f, frame);
                made++;
            }
            else
            {
                int no = i == 0 ? 1 : i;   // 0 → 1, 2 → 2 ... 10 → 10
                string tp = MusDir + "/Panels/panel_" + no.ToString("00") + ".png";
                var tex = Tex(tp, 2048);
                if (tex == null) { log.AppendLine("展示: " + tp + " が無い"); continue; }
                Stand("Panel_" + no.ToString("00"), R, R.TransformPoint(p), R.rotation * r, Mat("Panel_" + no.ToString("00"), tex, Color.white), 1.6f, 1.6f * 2880f / 2048f, 0.5f, frame);
                made++;
            }
        }
        log.AppendLine("展示: パネルと地図 " + made + " 枚（間隔 " + gap.ToString("F1") + "m）");

        // 「この建物について」（入口の近く。音声ガイドの台の反対側。入口の方を向く）
        var atex = Tex(MusDir + "/Panels/panel_about.png", 2048);
        if (atex != null)
        {
            Stand("About", R, R.TransformPoint(new Vector3(-side * 0.75f, 0f, 3.2f)), R.rotation * Quaternion.Euler(0f, 180f + 20f, 0f),
                Mat("Panel_About", atex, Color.white), 1.6f, 1.6f * 2880f / 2048f, 0.5f, frame);
            made++;
        }
        else log.AppendLine("展示: panel_about.png が無い");

        // 音（BGM・音声ガイド）
        var audioGo = new GameObject("MuseumAudio");
        audioGo.transform.SetParent(R, false);
        var bgm = audioGo.AddComponent<AudioSource>();
        bgm.clip = Clip(MusDir + "/shinkai.mp3", true);
        bgm.loop = true; bgm.playOnAwake = false; bgm.spatialBlend = 0f; bgm.volume = 0f; bgm.priority = 64;
        var gJa = new GameObject("GuideJa").AddComponent<AudioSource>();
        gJa.transform.SetParent(audioGo.transform, false);
        gJa.clip = Clip(MusDir + "/guide_ja.ogg", true);
        var gEn = new GameObject("GuideEn").AddComponent<AudioSource>();
        gEn.transform.SetParent(audioGo.transform, false);
        gEn.clip = Clip(MusDir + "/guide_en.ogg", true);
        foreach (var g in new[] { gJa, gEn }) { g.playOnAwake = false; g.loop = false; g.spatialBlend = 0f; g.priority = 32; }
        if (bgm.clip == null) log.AppendLine("展示: shinkai.mp3 が無い");
        if (gJa.clip == null || gEn.clip == null) log.AppendLine("展示: 音声ガイドの音が無い");

        // 倉庫の範囲（この中にいると BGM が流れる）
        var zone = new GameObject("MuseumZone").transform;
        zone.SetParent(R, true);
        zone.position = wh.TransformPoint(lb.center);
        zone.rotation = wh.rotation;
        zone.localScale = Vector3.one;
        Vector3 zs = lb.size + new Vector3(1f, 2f, 1f);
        Vector3 ps = zone.parent.lossyScale;
        zone.localScale = new Vector3(zs.x / ps.x, zs.y / ps.y, zs.z / ps.z);

        // 音声ガイドの台（入口の近く。入口の方を向く）
        var kiosk = new GameObject("AudioGuide").transform;
        kiosk.SetParent(R, false);
        kiosk.localPosition = new Vector3(side * 0.75f, 0f, 3.2f);
        kiosk.localRotation = Quaternion.Euler(0f, 180f - 20f, 0f);
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
            gb.museum = ma;
            gb.lang = i;
            UdonSharpEditorUtility.CopyProxyToUdon(gb);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(gb);
            if (ub != null) { ub.interactText = i == 0 ? "音声ガイド（日本語）再生／停止" : "Audio guide (English) play / stop"; ub.proximity = 2.5f; }
        }
        ma.zone = zone;
        ma.bgm = bgm;
        ma.guides = new[] { gJa, gEn };
        ma.playingMarks = marks;
        ma.volume = Object.FindObjectOfType<VolumeSettings>(true);
        UdonSharpEditorUtility.CopyProxyToUdon(ma);
        // 博物館の中では無線を聞こえなくする
        foreach (var gmode in Object.FindObjectsOfType<GameMode>(true))
        {
            Undo.RecordObject(gmode, "Museum setup");
            gmode.museum = ma;
            UdonSharpEditorUtility.CopyProxyToUdon(gmode);
        }

        SetupMemorial(R, len, frame, log);

        if (keep) { R.SetPositionAndRotation(keepPos, keepRot); log.AppendLine("展示: 前と同じ位置・向き"); }
        AssetDatabase.SaveAssets();
        log.AppendLine("展示: 音声ガイド台・BGM（倉庫の中で流れる）を置きました");
    }
    // ---------------- 弔いの箱（一番奥。入口のドアから一番遠い所） ----------------
    static void SetupMemorial(Transform R, float len, Material frame, StringBuilder log)
    {
        SetupMemorialAt(R, new Vector3(0f, 0f, len - 3.2f), 180f, MusDir + "/Panels/memorial.png", 1.6f, 1.2f, null, frame, log);
    }

    static void SetupMemorialAt(Transform R, Vector3 localPos, float yaw, string plaquePath, float pw, float ph, string labelPath, Material frame, StringBuilder log)
    {
        var mem = new GameObject("Memorial").transform;
        mem.SetParent(R, false);
        mem.localPosition = localPos;
        mem.localRotation = Quaternion.Euler(0f, yaw, 0f);   // 入口の方を向く

        // 碑（説明の板）
        float pb = labelPath != null ? 1.62f : 1.4f;
        var tex = Tex(plaquePath, 2048);
        if (tex != null) Stand("Plaque", mem, mem.TransformPoint(new Vector3(0f, 0f, -0.9f)), mem.rotation, Mat("Memorial_" + Path.GetFileNameWithoutExtension(plaquePath), tex, Color.white), pw, ph, pb, frame);
        if (labelPath != null)
        {
            var lt = Tex(labelPath, 1024);
            if (lt != null) Quad("CountLabel", mem, new Vector3(0f, 1.45f, -0.86f), new Vector2(1.4f, 0.19f), Mat("CountLabel", lt, Color.white));
        }
        // 自分が納めた数
        var ct = new GameObject("Count");
        ct.transform.SetParent(mem, false);
        ct.transform.localPosition = new Vector3(0f, 1.2f, -0.86f);
        ct.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var t = ct.AddComponent<TextMeshPro>();
        t.text = "0";
        t.fontSize = 2.2f;
        t.color = new Color(0.84f, 0.73f, 0.47f);
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.rectTransform.sizeDelta = new Vector2(1.4f, 0.4f);

        // 箱（上が開いている木の箱）
        Material wood = Mat("MemorialWood", null, new Color(0.30f, 0.19f, 0.11f));
        Material cloth = Mat("MemorialCloth", null, new Color(0.08f, 0.08f, 0.09f));
        var box = new GameObject("Box").transform;
        box.SetParent(mem, false);
        box.localPosition = new Vector3(0f, 0f, 0.3f);
        Box("Table", box, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.8f, 0.7f), cloth, true);
        float bw = 0.7f, bd = 0.5f, bh = 0.4f, th = 0.03f, by = 0.8f;
        Box("Bottom", box, new Vector3(0f, by + th * 0.5f, 0f), new Vector3(bw, th, bd), wood, true);
        Box("WallF", box, new Vector3(0f, by + bh * 0.5f, bd * 0.5f), new Vector3(bw, bh, th), wood, true);
        Box("WallB", box, new Vector3(0f, by + bh * 0.5f, -bd * 0.5f), new Vector3(bw, bh, th), wood, true);
        Box("WallL", box, new Vector3(-bw * 0.5f, by + bh * 0.5f, 0f), new Vector3(th, bh, bd), wood, true);
        Box("WallR", box, new Vector3(bw * 0.5f, by + bh * 0.5f, 0f), new Vector3(th, bh, bd), wood, true);
        var inside = new GameObject("Inside").transform;
        inside.SetParent(box, false);
        inside.localPosition = new Vector3(0f, by + bh * 0.5f + 0.05f, 0f);
        inside.localScale = new Vector3(bw - 0.02f, bh + 0.1f, bd - 0.02f);

        var mbGo = box.gameObject;
        var au = mbGo.AddComponent<AudioSource>();
        au.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusDir + "/bell.wav");
        au.playOnAwake = false; au.spatialBlend = 1f; au.minDistance = 2f; au.maxDistance = 25f; au.volume = 0.7f;
        var mb = UdonSharpUndo.AddComponent<MemorialBox>(mbGo);
        mb.inside = inside;
        mb.joinLog = Object.FindObjectOfType<JoinLog>(true);
        t.gameObject.SetActive(false);   // 数は出さない（加護の仕組みに変えた）
        mb.bell = au;
        UdonSharpEditorUtility.CopyProxyToUdon(mb);

        // 折り鶴を置く台（箱の右）と折り鶴 5 羽
        var tray = Box("CraneTable", mem, new Vector3(1.0f, 0.45f, 0.3f), new Vector3(0.6f, 0.9f, 0.5f), cloth, true);
        Mesh cm = CraneMesh();
        var cols = new[] { new Color(0.85f, 0.15f, 0.15f), new Color(0.95f, 0.95f, 0.92f), new Color(0.9f, 0.75f, 0.3f), new Color(0.3f, 0.5f, 0.85f), new Color(0.95f, 0.6f, 0.7f) };
        for (int i = 0; i < 5; i++)
        {
            var c = new GameObject("PaperCrane_" + (i + 1));
            c.transform.SetParent(mem, false);
            c.transform.localPosition = new Vector3(0.82f + (i % 3) * 0.18f, 0.9f + 0.035f, 0.18f + (i / 3) * 0.22f);
            c.transform.localRotation = Quaternion.Euler(0f, -30f + i * 17f, 0f);
            c.AddComponent<MeshFilter>().sharedMesh = cm;
            c.AddComponent<MeshRenderer>().sharedMaterial = Mat("Crane" + i, null, cols[i]);
            var bc = c.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.03f, 0f);
            bc.size = new Vector3(0.16f, 0.07f, 0.14f);
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.05f; rb.drag = 2f; rb.angularDrag = 2f; rb.useGravity = true; rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var pk = c.AddComponent<VRC.SDK3.Components.VRCPickup>();
            pk.InteractionText = "折り鶴 / Paper crane";
            pk.UseText = "";
            pk.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.No;
            var pc = UdonSharpUndo.AddComponent<PaperCrane>(c);
            pc.box = mb;
            UdonSharpEditorUtility.CopyProxyToUdon(pc);
        }
        log.AppendLine("弔いの箱: 一番奥に置きました（折り鶴 5 羽、数は自分の合計）");
    }

    // 折り紙の鶴の形（両面）
    static Mesh CraneMesh()
    {
        string p = MusDir + "/PaperCraneMesh.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, p); }
        m.Clear();
        float s = 1.4f;
        var v = new System.Collections.Generic.List<Vector3>();
        var tri = new System.Collections.Generic.List<int>();
        System.Action<Vector3, Vector3, Vector3> T = (a, b, c) =>
        {
            int i = v.Count;
            v.Add(a * s); v.Add(b * s); v.Add(c * s);
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
            v.Add(a * s); v.Add(c * s); v.Add(b * s);   // 裏
            tri.Add(i + 3); tri.Add(i + 4); tri.Add(i + 5);
        };
        Vector3 F = new Vector3(0f, 0.02f, 0.035f), B = new Vector3(0f, 0.02f, -0.035f), D = new Vector3(0f, -0.005f, 0f);
        Vector3 L = new Vector3(-0.012f, 0.012f, 0f), Rt = new Vector3(0.012f, 0.012f, 0f);
        // 胴（ひし形の袋）
        T(F, L, D); T(L, B, D); T(F, D, Rt); T(D, B, Rt);
        // 翼
        T(F + Vector3.up * 0.002f, B + Vector3.up * 0.002f, new Vector3(-0.075f, 0.045f, -0.005f));
        T(B + Vector3.up * 0.002f, F + Vector3.up * 0.002f, new Vector3(0.075f, 0.045f, -0.005f));
        // 首と頭
        Vector3 nk = new Vector3(0f, 0.06f, 0.075f);
        T(new Vector3(-0.004f, 0.005f, 0.02f), new Vector3(0.004f, 0.005f, 0.02f), nk);
        T(nk, new Vector3(0f, 0.058f, 0.068f), new Vector3(0f, 0.052f, 0.09f));
        // 尾
        T(new Vector3(-0.004f, 0.005f, -0.02f), new Vector3(0.004f, 0.005f, -0.02f), new Vector3(0f, 0.065f, -0.08f));
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        EditorUtility.SetDirty(m);
        return m;
    }
}
