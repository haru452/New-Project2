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

}
