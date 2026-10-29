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
    static void SetupSpeeds(StringBuilder log)
    {
        int n = 0;
        {
            // すべての敵の戦闘機: 出撃する潜水艦のまわり 1km には、追いかけていない時は入らない
            var po = Object.FindObjectOfType<PutOutDrone>(true);
            int nf = 0;
            if (po != null && po.catapultPoint != null)
                foreach (var nfE in Object.FindObjectsOfType<EnemyDrone>(true))
                {
                    Undo.RecordObject(nfE, "Flight setup");
                    nfE.noFlyRef = po.catapultPoint; nfE.noFlyRadius = 1000f;
                    UdonSharpEditorUtility.CopyProxyToUdon(nfE); nf++;
                }
            // 同じ滑走路・空母から飛び立つ戦闘機どうしは、ぶつからないように
            var groups = new System.Collections.Generic.Dictionary<Transform, System.Collections.Generic.List<EnemyDrone>>();
            foreach (var gE in Object.FindObjectsOfType<EnemyDrone>(true))
            {
                if (!gE.carrierBased || gE.carrierRef == null) { if (gE.noCollide != null && gE.noCollide.Length > 0) { Undo.RecordObject(gE, "Flight setup"); gE.noCollide = new EnemyDrone[0]; UdonSharpEditorUtility.CopyProxyToUdon(gE); } continue; }
                if (!groups.ContainsKey(gE.carrierRef)) groups[gE.carrierRef] = new System.Collections.Generic.List<EnemyDrone>();
                groups[gE.carrierRef].Add(gE);
            }
            int ng = 0;
            foreach (var kv in groups)
            {
                var arr = kv.Value.ToArray();
                foreach (var gE in kv.Value) { Undo.RecordObject(gE, "Flight setup"); gE.noCollide = arr; UdonSharpEditorUtility.CopyProxyToUdon(gE); }
                ng++;
            }
            log.AppendLine("空母・基地の戦闘機: 同じ所から飛び立つ仲間どうしはぶつからない（" + ng + " グループ）");
            log.AppendLine("潜水艦のまわり 1km の飛行禁止: 敵機 " + nf + " 機に設定" + (po == null || po.catapultPoint == null ? "（潜水艦の出撃地点が見つからない）" : ""));
        }
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.propMaxSpeed = 0f;          // ジェット扱い
            // 本物のジェット練習機くらいの重さ（翼面荷重 約 225kg/m²）: 失速速度 約 180km/h（フラップで 約 155km/h）
            //   遅すぎると揚力が足りずに落ちる。最高速度は今までどおり約 600km/h（推力と抗力を両方上げた）
            c.mass = 2400f;
            c.maxThrust = 20000f;
            c.parasiteDrag = 0.07f;
            c.boostLift = 0f;             // T は前へだけ（上へは押さない）
            c.boostForward = 30000f;      // T: 4 秒間、機首の向きへ約 0.85G の追加加速
            c.fuelRefSpeed = 170f;
            c.pitchRefSpeed = 122f;       // これより速いと G を一定（約 9G）に。遅い時に操縦桿をいっぱい引くと失速する
            c.rollRefSpeed = 90f;
            c.missileAccel = 150f;        // ミサイルは約 350m/s まで加速
            c.missileBurnTime = 3f;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        int e = 0;
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            if (ed.rb != null && ed.rb.useGravity) { Undo.RecordObject(ed.rb, "Flight setup"); ed.rb.useGravity = false; }   // 重力は EnemyDrone が自分で足す
            Undo.RecordObject(ed, "Flight setup");
            ed.speed = ed.isAwacs ? 100f : 140f;
            ed.maxLateralAcc = ed.isAwacs ? 12f : 40f;    // 旋回の半径 約 500m（戦闘機）
            ed.maxVerticalAcc = ed.isAwacs ? 6f : 15f;
            ed.maxAlongAcc = ed.isAwacs ? 6f : 15f;
            ed.rollRate = 3f;
            // 飛行の物理（揚力・推力・抗力・重力）
            ed.stallSpeed = ed.isAwacs ? 50f : 60f;
            ed.gLimit = ed.isAwacs ? 2.5f : 7f;
            ed.thrustAcc = ed.isAwacs ? 4f : 12f;
            ed.topSpeed = ed.isAwacs ? 125f : 175f;
            ed.rollRateDeg = ed.isAwacs ? 30f : 150f;
            ed.takeoffAcc = 12f;
            ed.fireRange = 700f;
            ed.missileMaxSpeed = 450f;
            ed.missileAccel = 150f;
            ed.missileMinRange = 600f;
            ed.missileMaxRange = 3500f;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
            e++;
        }
        log.AppendLine("速さ: プレイヤーの機体 " + n + " 機をジェット機風に（最高 約 600km/h・G リミッター）。敵 " + e + " 機を 約 500km/h（警戒機 約 360km/h）に。敵は揚力・推力・抗力・重力で飛ぶ（失速 60m/s・7G・ロール 150°/秒）");
    }

    // 基地の守り: アインの警告・ミサイル迎撃の声・基地の大砲を対空砲に
    static void SetupBaseDefense(StringBuilder log)
    {
        GameObject baseGo = GameObject.Find("military base");
        AudioClip[] warn = { LoadClip("ain_warn1.wav"), LoadClip("ain_warn2.wav") };
        AudioClip[] thanks = { LoadClip("ain_thanks1.wav"), LoadClip("ain_thanks2.wav") };
        AudioClip[] icpt = { LoadClip("voice_intercepted1.wav"), LoadClip("voice_intercepted2.wav") };
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.baseCenter = baseGo != null ? baseGo.transform : null;
            c.baseWarnRadius = 3000f;
            c.ainWarn = warn;
            c.ainThanks = thanks;
            c.voiceIntercepted = icpt;
            c.baseBuildings = s_baseBuildings.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("基地: アインの警告（3km）・迎撃の声を " + n + " 機に設定" + (warn[0] == null || icpt[0] == null ? "  ※ 音声が見つからない" : ""));
        if (baseGo == null) { log.AppendLine("基地: military base が見つからない"); return; }
        GameMode gm = Object.FindObjectOfType<GameMode>(true);
        int aa = 0;
        foreach (var fc in baseGo.GetComponentsInChildren<FixedCannon>(true))
        {
            Undo.RecordObject(fc, "Flight setup");
            fc.antiAir = true;
            fc.targetMissiles = true;
            fc.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(fc);
            aa++;
        }
        log.AppendLine("基地: 大砲 " + aa + " 門を対空砲に（ミサイルも狙う・観光モードでは撃たない）");
    }

    // ---------------- 空母の戦闘機 ----------------
    const int CarrierFighterCount = 4;
    static bool IsCarrierFighter(GameObject g) { return g != null && g.name.StartsWith("CARRIER_"); }

    // 空母の戦闘機 4 機（enemy aircraft の中に CARRIER_1〜4）。見た目は敵の戦闘機と同じ FA_N26 を水色に
    static void EnsureCarrierFighters(Transform root, StringBuilder log)
    {
        // 元にする戦闘機: 根元と見た目が同じ場所にある物（EnemyFighter_1 は見た目が根元から 6km 離れているので使わない）
        Transform src = null;
        float bestOff = float.MaxValue;
        foreach (Transform ch in root)
        {
            if (!ch.name.StartsWith("EnemyFighter_")) continue;
            float off = (EnemyModelOf(ch.gameObject).position - ch.position).magnitude;
            if (off < bestOff) { bestOff = off; src = ch; }
        }
        if (src == null) { log.AppendLine("空母の戦闘機: 元にする敵の戦闘機が無い"); return; }
        Transform srcModel = EnemyModelOf(src.gameObject);
        // 前に作った空母の戦闘機の見た目が、根元から遠くにずれていたら直す（甲板の上に置き直す）
        for (int i = 1; i <= CarrierFighterCount; i++)
        {
            Transform cf = root.Find("CARRIER_" + i);
            if (cf == null) continue;
            Transform cm = EnemyModelOf(cf.gameObject);
            if (cm == cf || (cm.position - cf.position).magnitude < 50f) continue;
            Undo.RecordObject(cm, "Flight setup");
            cm.localPosition = Quaternion.Inverse(src.rotation) * (srcModel.position - src.position);
            cm.localRotation = Quaternion.Inverse(src.rotation) * srcModel.rotation;
            Transform mk = cf.Find("ParkSet");
            if (mk != null) Undo.DestroyObjectImmediate(mk.gameObject);   // 甲板の上に置き直す
            log.AppendLine("空母の戦闘機: " + cf.name + " の見た目が根元から離れていたので直しました");
        }
        GameObject asset = PrefabUtility.GetCorrespondingObjectFromSource(srcModel.gameObject) as GameObject;
        string ap = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
        int made = 0;
        for (int i = 1; i <= CarrierFighterCount; i++)
        {
            if (root.Find("CARRIER_" + i) != null) continue;
            var go = new GameObject("CARRIER_" + i);
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(root, false);
            go.transform.position = root.position + new Vector3(-i * 200f, 300f, 0f);
            go.transform.rotation = Quaternion.identity;
            SetWorldScaleOne(go.transform);
            go.transform.SetAsLastSibling();
            GameObject model = !string.IsNullOrEmpty(ap)
                ? (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ap), go.transform)
                : Object.Instantiate(srcModel.gameObject, go.transform);
            model.transform.localPosition = Quaternion.Inverse(src.rotation) * (srcModel.position - src.position);
            model.transform.localRotation = Quaternion.Inverse(src.rotation) * srcModel.rotation;
            model.transform.localScale = srcModel.localScale;
            made++;
        }
        if (made > 0) log.AppendLine("空母の戦闘機: " + made + " 機を作りました（" + (ap != "" ? ap : srcModel.name) + "）");
    }

    // 水色に塗る・空母の甲板の位置・追尾ミサイル
    static void ConfigureCarrierFighters(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var cv = new System.Collections.Generic.List<EnemyDrone>();
        foreach (var g in fighters)
        {
            if (!IsCarrierFighter(g)) continue;
            var ed = g.GetComponentInChildren<EnemyDrone>(true);
            if (ed != null) cv.Add(ed);
        }
        if (cv.Count == 0) return;
        cv.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        WarShip carrier = null;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true)) if (ws.target != null && ws.target.kind == 2) { carrier = ws; break; }
        if (carrier == null || carrier.model == null) { log.AppendLine("空母の戦闘機: 空母（S10）が見つからない"); return; }

        // 甲板の高さ: 見た目に一時的にメッシュの当たり判定を付けて、上から調べる
        Transform cm = carrier.model.transform;
        Bounds bd;
        if (!MeshBounds(cm, "", out bd)) return;
        bool alongX = bd.size.x >= bd.size.z;
        Vector3 L = alongX ? Vector3.right : Vector3.forward;
        Vector3 Sd = alongX ? Vector3.forward : Vector3.right;
        float len = alongX ? bd.size.x : bd.size.z, wid = alongX ? bd.size.z : bd.size.x;
        var temp = new System.Collections.Generic.List<MeshCollider>();
        foreach (var mf in cm.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            temp.Add(mf.gameObject.AddComponent<MeshCollider>());
        }
        Physics.SyncTransforms();
        var hs = new System.Collections.Generic.List<float>();
        for (int k = -4; k <= 4; k++)
        {
            Vector3 o = bd.center + L * (k / 10f * len) + Vector3.up * 200f;
            foreach (var h in Physics.RaycastAll(o, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider is MeshCollider && temp.Contains((MeshCollider)h.collider)) { hs.Add(h.point.y); break; }
        }
        foreach (var t in temp) Object.DestroyImmediate(t);
        hs.Sort();
        float deckY = hs.Count > 0 ? hs[hs.Count / 2] : bd.min.y + bd.size.y * 0.4f;   // 真ん中の値（艦橋の上は除く）

        // 機体の中心からタイヤの下までの高さ（甲板の上に置く高さ）
        float hOff = 3f;
        var ed0 = cv[0];
        Transform lg = ed0.rb.transform.Find("LandingGear");
        if (lg != null)
        {
            float lowest = float.MaxValue;
            foreach (var r in lg.GetComponentsInChildren<Renderer>(true)) lowest = Mathf.Min(lowest, r.bounds.min.y);
            if (lowest < float.MaxValue) hOff = ed0.rb.transform.position.y - lowest;
        }

        Transform cr = carrier.transform;
        Vector3 deckStart = bd.center - L * len * 0.35f;
        deckStart.y = deckY + hOff + 0.3f;
        // 発艦の走り始め: 空母の中の「LaunchStart」（最初の 1 回だけ作る。手で動かした位置・向きを使う。青い矢印 = 飛び立つ向き）
        Transform ls = cr.Find("LaunchStart");
        if (ls == null)
        {
            var lsGo = new GameObject("LaunchStart");
            Undo.RegisterCreatedObjectUndo(lsGo, "Flight setup");
            ls = lsGo.transform;
            ls.SetParent(cr, false);
            ls.SetPositionAndRotation(deckStart, Quaternion.LookRotation(L, Vector3.up));
            log.AppendLine("空母の戦闘機: 発艦の走り始め LaunchStart を空母（" + cr.name + "）の中に作りました（手で動かせます）");
        }
        deckStart = ls.position;
        Vector3 launchDir = ls.forward; launchDir.y = 0f;
        if (launchDir.sqrMagnitude < 0.001f) launchDir = L;
        launchDir.Normalize();
        Material blue = CarrierBlueMat(fighters);
        int idx = 0;
        foreach (var ed in cv)
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.carrierBased = true;
            ed.carrierRef = cr;
            ed.carrierShip = carrier;
            ed.guidedMissile = true;
            ed.missileTurnRate = 40f;        // よく曲がる追尾ミサイル（ほかの戦闘機は 14）
            ed.missileBurn = 8f;
            ed.missileInterval = 10f;
            ed.carrierWarnRange = 4000f;
            ed.carrierRange = 6000f;
            ed.launchDelay = idx * 4f;      // 4 秒おきに順番に発艦
            ed.deckRun = len * 0.6f;
            // 止まる場所: 最初の 1 回だけ甲板の前半に左右互い違いで置く。その後は、シーンで手で動かした位置・向きをそのまま使う
            Transform ft = ed.rb.transform;
            if (ft.Find("ParkSet") == null)
            {
                Vector3 park = bd.center + L * len * (0.05f + 0.1f * idx) + Sd * wid * (idx % 2 == 0 ? -0.22f : 0.22f);
                park.y = deckY + hOff + 0.3f;
                Undo.RecordObject(ft, "Flight setup");
                ft.SetPositionAndRotation(park, Quaternion.LookRotation(L, Vector3.up));
                var mk = new GameObject("ParkSet");   // 置いた印（これがあれば手で動かした位置を使う）
                Undo.RegisterCreatedObjectUndo(mk, "Flight setup");
                mk.transform.SetParent(ft, false);
            }
            ed.parkLocal = cr.InverseTransformPoint(ft.position);
            ed.parkRotLocal = Quaternion.Inverse(cr.rotation) * ft.rotation;
            ed.deckStartLocal = cr.InverseTransformPoint(deckStart);
            ed.deckDirLocal = cr.InverseTransformDirection(launchDir);
            if (ed.target != null)
            {
                Undo.RecordObject(ed.target, "Flight setup");
                ed.target.kind = 5;          // 空母の戦闘機（3 点）
                ed.target.respawnTime = 40f;
                UdonSharpEditorUtility.CopyProxyToUdon(ed.target);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
            // 水色に塗る（脚は除く）
            if (blue != null)
                foreach (var r in EnemyModelOf(ed.rb.gameObject).GetComponentsInChildren<MeshRenderer>(true))
                {
                    var ms = r.sharedMaterials;
                    for (int m = 0; m < ms.Length; m++) ms[m] = blue;
                    Undo.RecordObject(r, "Flight setup");
                    r.sharedMaterials = ms;
                }
            idx++;
        }
        log.AppendLine("空母の戦闘機: " + cv.Count + " 機を空母 " + carrier.model.name + " に配置（甲板の高さ " + deckY.ToString("F1") + "・長さ " + len.ToString("F0") + "m。4km で発艦・6km の中だけ飛ぶ・誰もいなければ着艦・追尾ミサイル）");
        if (hs.Count == 0) log.AppendLine("空母の戦闘機: ※ 甲板の高さを調べられなかったので推定です。甲板に埋まる・浮く場合は教えてください");
    }

    // 敵の戦闘機の色を水色にしたマテリアル
    static Material CarrierBlueMat(System.Collections.Generic.List<GameObject> fighters)
    {
        Material srcM = null;
        foreach (var g in fighters)
        {
            if (IsAwacs(g) || IsCarrierFighter(g) || IsBaseFighter(g)) continue;
            var r = EnemyModelOf(g).GetComponentInChildren<MeshRenderer>(true);
            if (r != null && r.sharedMaterial != null) { srcM = r.sharedMaterial; break; }
        }
        if (srcM == null) return null;
        string path = FxDir + "/CarrierFighterBlue.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(srcM); AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(srcM);
        m.shader = srcM.shader;
        Color blue = new Color(0.55f, 0.82f, 1f, 1f);   // 日本の F-2 のような水色っぽい青
        if (m.HasProperty("_Color")) m.SetColor("_Color", blue);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", blue);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------- 警戒機 ----------------
    const string AwacsPrefab = "Assets/Alstra Infinite/Planes LowPoly/Prefabs/ComPlaneV2.prefab";
    const float AwacsScale = 15f;   // 基地の警戒機と同じ大きさ
    static bool IsAwacs(GameObject g) { return g != null && g.name.StartsWith("AWACS_"); }

    // 飛ぶ警戒機を 3 機（enemy aircraft の中に AWACS_1〜3）。無ければ作る
    static void EnsureFlyingAwacs(Transform root, StringBuilder log)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AwacsPrefab);
        if (asset == null) { log.AppendLine("警戒機: " + AwacsPrefab + " が見つからない"); return; }
        int made = 0;
        for (int i = 1; i <= 3; i++)
        {
            if (root.Find("AWACS_" + i) != null) continue;
            var go = new GameObject("AWACS_" + i);
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(root, false);
            go.transform.position = root.position + new Vector3(i * 300f, 300f, 0f);
            go.transform.rotation = Quaternion.identity;
            SetWorldScaleOne(go.transform);
            go.transform.SetAsLastSibling();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * AwacsScale;
            made++;
        }
        if (made > 0) log.AppendLine("警戒機: 飛ぶ警戒機を " + made + " 機作りました（機首が +Z の向きの前提。逆なら教えてください）");
    }

    // 飛ぶ警戒機: 攻撃しない・ゆっくり・探す範囲 2 倍・戦闘機 2 機と編隊
    static void ConfigureAwacs(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var awacs = new System.Collections.Generic.List<EnemyDrone>();
        var wings = new System.Collections.Generic.List<EnemyDrone>();
        foreach (var g in fighters)
        {
            var ed = g.GetComponentInChildren<EnemyDrone>(true);
            if (ed == null) continue;
            Undo.RecordObject(ed, "Flight setup");
            ed.isAwacs = false; ed.formationLeader = null; ed.wingmen = null;
            if (IsAwacs(g)) awacs.Add(ed); else if (!IsCarrierFighter(g) && !IsBaseFighter(g) && g.activeSelf) wings.Add(ed);   // 空母の戦闘機は編隊に入れない
        }
        wings.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        int wi = 0;
        foreach (var aw in awacs)
        {
            aw.isAwacs = true;
            aw.speed = 32f;                     // 戦闘機（44）よりゆっくり
            aw.detectRange = 6000f;             // 戦闘機（3000）の 2 倍
            aw.loseRange = 8000f;
            aw.minAlt = 260f;
            aw.maxAlt = 420f;
            aw.maxLateralAcc = 2.5f;            // 大きな機体なのでゆるく曲がる
            if (aw.target != null)
            {
                Undo.RecordObject(aw.target, "Flight setup");
                aw.target.kind = 4;             // 警戒機（4 点・飛行機として数える）
                aw.target.maxHp = 30;
                aw.target.respawnTime = 60f;
                UdonSharpEditorUtility.CopyProxyToUdon(aw.target);
            }
            // 編隊の位置: 警戒機の大きさから（左右に翼幅くらい・後ろに機体の長さの 0.6 倍）
            Bounds b;
            float side = 60f, back = 50f;
            // ブルーインパルスのような近い矢じり形: 翼の端どうしが数 m になる横の間隔、機首が先頭の翼の後ろに来る前後の間隔
            if (MeshBounds(EnemyModelOf(aw.rb.gameObject), "", out b)) { float big = Mathf.Max(b.size.x, b.size.z); side = big * 0.5f + 18f; back = big * 0.6f + 12f; }
            var wm = new System.Collections.Generic.List<EnemyDrone>();
            for (int k = 0; k < 2 && wi < wings.Count; k++, wi++)
            {
                EnemyDrone w = wings[wi];
                w.formationLeader = aw;
                w.formationOffset = new Vector3(k == 0 ? -side : side, -3f, -back);   // 少し下がる段差
                wm.Add(w);
                UdonSharpEditorUtility.CopyProxyToUdon(w);
            }
            aw.wingmen = wm.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(aw);
            log.AppendLine("警戒機: " + aw.rb.name + " ← 編隊の戦闘機 " + (wm.Count > 0 ? wm[0].rb.name : "なし") + (wm.Count > 1 ? "・" + wm[1].rb.name : "") + "（左右 " + side.ToString("F0") + "m・後ろ " + back.ToString("F0") + "m）");
        }
        foreach (var w in wings) UdonSharpEditorUtility.CopyProxyToUdon(w);
    }

    // 基地に止まっている警戒機: 機銃・ミサイルで壊せる（2 点・60 秒で復活）
    static void SetupParkedAwacs(StringBuilder log)
    {
        Transform grp = FindByNameContains("警戒機");
        if (grp == null) { log.AppendLine("警戒機: 基地の「警戒機」が見つからない"); return; }
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        int n = 0;
        foreach (Transform pl in grp)
        {
            if (pl.GetComponentInChildren<Renderer>(true) == null) continue;
            var ft = pl.GetComponent<FlightTarget>();
            if (ft == null) ft = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(pl.gameObject);
            Undo.RecordObject(ft, "Flight setup");
            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in pl.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) rends.Add(r);
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var c in pl.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
            if (cols.Count == 0)
            {
                Bounds wb;
                if (MeshBounds(pl, "", out wb))
                {
                    var bc = Undo.AddComponent<BoxCollider>(pl.gameObject);
                    bc.center = pl.InverseTransformPoint(wb.center);
                    Vector3 sz = pl.InverseTransformVector(wb.size);
                    bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                    cols.Add(bc);
                }
            }
            ft.renderers = rends.ToArray();
            ft.colliders = cols.ToArray();
            ft.body = null;
            ft.kind = 0;            // 飛行機（2 点）
            ft.maxHp = 15;
            ft.missileDamage = 15;
            ft.respawnTime = 60f;   // しばらくして復活
            Transform oldEx = pl.Find("Explosion");
            if (oldEx != null) Undo.DestroyObjectImmediate(oldEx.gameObject);
            Bounds eb;
            var ex = MakePS("Explosion", pl, fxMat);
            if (MeshBounds(pl, "", out eb)) ex.transform.position = eb.center;
            ConfigureExplosion(ex, 6f, 90);
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 30f;
            exAu.maxDistance = 2500f;
            ft.explosionFx = ex;
            ft.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(ft);
            n++;
        }
        log.AppendLine("警戒機: 基地の警戒機 " + n + " 機を壊せるように（2 点・60 秒で復活）");
    }

    // ---------------- レーダーに出す物 ----------------
    static void SetupRadarTracks(StringBuilder log)
    {
        var tracks = new System.Collections.Generic.List<Transform>();
        var types = new System.Collections.Generic.List<int>();
        var fts = new System.Collections.Generic.List<FlightTarget>();
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            if (ed.rb == null || !ed.rb.gameObject.activeInHierarchy) continue;
            tracks.Add(ed.rb.transform); types.Add(0); fts.Add(ed.target);
        }
        var activeShips = new System.Collections.Generic.List<WarShip>();
        // 基地のそばに止まっている艦隊は、レーダーに出さない（5 = 出さない。撃墜は数える）
        GameObject baseGo = GameObject.Find("military base");
        Vector3 basePos = baseGo != null ? baseGo.transform.position : new Vector3(2719f, 0f, 441f);
        int hiddenShips = 0;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            if (!ws.gameObject.activeInHierarchy) continue;   // しまった船（Colonial Ship など）は出さない
            Vector3 db = (ws.model != null ? ws.model.transform.position : ws.transform.position) - basePos; db.y = 0f;   // 見た目の船の位置で測る
            bool baseFleet = !ws.roaming && ws.leader == null && db.magnitude < 2500f;
            if (baseFleet) hiddenShips++;
            tracks.Add(ws.transform); types.Add(baseFleet ? 5 : 1); fts.Add(ws.target); activeShips.Add(ws);
        }
        int hiddenPark = 0;
        foreach (var ft in Object.FindObjectsOfType<FlightTarget>(true))
        {
            bool parked = ft.name.StartsWith("ComPlaneV2") || ft.kind == 6;   // 6 = 基地の建物（レーダーには出さない・壊したら数える）   // 基地に止まっている警戒機（レーダーには出さない・撃墜は数える）
            if (ft.body == null && !parked) continue;   // 気球の標的（body 付き）
            if (parked) hiddenPark++;
            tracks.Add(ft.transform); types.Add(parked ? 5 : 2); fts.Add(ft);
        }
        log.AppendLine("レーダー: 基地のそばの艦隊 " + hiddenShips + " 隻・止まっている警戒機 " + hiddenPark + " 機は表示しない");
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && IsR35(c.car)) { tracks.Add(c.car.transform); types.Add(3); fts.Add(null); }

        var r35s = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && IsR35(c.car)) r35s.Add(c);
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.radarTracks = tracks.ToArray();
            c.radarTypes = types.ToArray();
            c.radarTrackTargets = fts.ToArray();
            c.radarPlanes = r35s.ToArray();
            c.radarShips = activeShips.ToArray();
            var fighters = new System.Collections.Generic.List<EnemyDrone>();
            foreach (var ed2 in Object.FindObjectsOfType<EnemyDrone>(true))
                if (ed2.rb != null && ed2.rb.gameObject.activeInHierarchy) fighters.Add(ed2);
            c.radarFighters = fighters.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("レーダー: " + tracks.Count + " 個を表示対象に（" + n + " 機）" + (tracks.Count > RadarDotCount ? "  ※ 点の数 " + RadarDotCount + " より多いので、後ろの " + (tracks.Count - RadarDotCount) + " 個は出ません" : ""));
    }

    // ---------------- 機体本体をクリックして乗る ----------------
    static bool boardRetried;
    const string BoardScriptPath = "Assets/My script/drone/BoardPlane.cs";
    const string BoardAssetPath = "Assets/My script/drone/BoardPlane.asset";
}
