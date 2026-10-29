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
    static void EnsureBaseFighters(Transform root, StringBuilder log)
    {
        Transform src = null;
        float bestOff = float.MaxValue;
        foreach (Transform ch in root)
        {
            if (!ch.name.StartsWith("EnemyFighter_")) continue;
            float off = (EnemyModelOf(ch.gameObject).position - ch.position).magnitude;
            if (off < bestOff) { bestOff = off; src = ch; }
        }
        if (src == null) { log.AppendLine("基地の戦闘機: 元にする敵の戦闘機が無い"); return; }
        Transform srcModel = EnemyModelOf(src.gameObject);
        GameObject asset = PrefabUtility.GetCorrespondingObjectFromSource(srcModel.gameObject) as GameObject;
        string ap = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
        int made = 0;
        for (int i = 1; i <= BaseFighterCount; i++)
        {
            if (root.Find("BASE_" + i) != null) continue;
            var go = new GameObject("BASE_" + i);
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(root, false);
            go.transform.position = root.position + new Vector3(-i * 200f, 400f, 300f);
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
        if (made > 0) log.AppendLine("基地の戦闘機: " + made + " 機を作りました（" + (ap != "" ? ap : srcModel.name) + "）");
    }

    // 地面の高さ（戦闘機・自分の機体には当たらないように）
    static float GroundY(Vector3 p, float fallback)
    {
        float best = float.MinValue;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, 400f, p.z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<Rigidbody>() != null) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return best > float.MinValue ? best : fallback;
    }

    static void ConfigureBaseFighters(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var bf = new System.Collections.Generic.List<EnemyDrone>();
        foreach (var g in fighters)
        {
            if (!IsBaseFighter(g)) continue;
            var ed = g.GetComponentInChildren<EnemyDrone>(true);
            if (ed != null) bf.Add(ed);
        }
        if (bf.Count == 0) return;
        bf.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        GameObject baseGo = GameObject.Find("military base");
        if (baseGo == null) { log.AppendLine("基地の戦闘機: military base が見つからない"); return; }
        Transform bt = baseGo.transform;
        Physics.SyncTransforms();
        // 基準: military base の中の「BaseAirfield」（最初の 1 回だけ作る。手で動かした位置・向きを使う。青い矢印 = 飛び立つ向き）
        Transform af = bt.Find("BaseAirfield");
        if (af == null)
        {
            var afGo = new GameObject("BaseAirfield");
            Undo.RegisterCreatedObjectUndo(afGo, "Flight setup");
            af = afGo.transform;
            af.SetParent(bt, false);
            Vector3 p0 = new Vector3(2620f, 0f, 411f);
            p0.y = GroundY(p0, -23f);
            af.SetPositionAndRotation(p0, Quaternion.LookRotation(Vector3.left, Vector3.up));
            log.AppendLine("基地の戦闘機: 発進の走り始め BaseAirfield を military base の中に作りました（手で動かせます。Local の青い矢印 = 飛び立つ向き）");
        }
        float hOff = 3f;
        Transform lg = bf[0].rb.transform.Find("LandingGear");
        if (lg != null)
        {
            float lowest = float.MaxValue;
            foreach (var r in lg.GetComponentsInChildren<Renderer>(true)) lowest = Mathf.Min(lowest, r.bounds.min.y);
            if (lowest < float.MaxValue) hOff = bf[0].rb.transform.position.y - lowest;
        }
        Vector3 dir = af.forward; dir.y = 0f;   // 青い矢印（Local の Z）の向きに飛び立つ
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.left;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 start = af.position; start.y = GroundY(start, af.position.y) + hOff + 0.3f;
        // 走る道の高さを調べて知らせる（でこぼこなら BaseAirfield を動かしてもらう）
        float gmin = float.MaxValue, gmax = float.MinValue;
        for (int k = 0; k <= 10; k++) { float gy = GroundY(af.position + dir * (25f * k), -999f); gmin = Mathf.Min(gmin, gy); gmax = Mathf.Max(gmax, gy); }
        int idx = 0;
        foreach (var ed in bf)
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.carrierBased = true;
            ed.baseAirfield = true;
            ed.carrierRef = af;
            ed.carrierShip = null;
            ed.guidedMissile = false;
            ed.carrierWarnRange = 3000f;
            ed.carrierRange = 6000f;
            ed.launchDelay = 3f + idx * 5f;
            ed.deckRun = 250f;
            Transform ft = ed.rb.transform;
            if (ft.Find("ParkSet") == null)
            {
                // 走り始めの後ろに 2 列（手で動かせば、その位置を使う）
                Vector3 park = af.position - dir * (30f + 25f * (idx / 2)) + side * (idx % 2 == 0 ? -14f : 14f);
                park.y = GroundY(park, af.position.y) + hOff + 0.3f;
                Undo.RecordObject(ft, "Flight setup");
                ft.position = park;
                var mk = new GameObject("ParkSet");
                Undo.RegisterCreatedObjectUndo(mk, "Flight setup");
                mk.transform.SetParent(ft, false);
            }
            // 機首を飛び立つ向きへ（この機体は機首が -X なので 90° 回す）
            Undo.RecordObject(ft, "Flight setup");
            ft.rotation = ed.noseIsZ ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            ed.parkLocal = af.InverseTransformPoint(ft.position);
            ed.parkRotLocal = Quaternion.Inverse(af.rotation) * ft.rotation;
            { Vector3 fw = ed.noseIsZ ? ft.forward : -ft.right; fw.y = 0f; log.AppendLine("基地の戦闘機: " + ft.name + " 止まる位置 " + ft.position.ToString("F0") + "・飛び立つ向きとの差 " + Vector3.Angle(fw, dir).ToString("F0") + "°"); }
            ed.deckStartLocal = af.InverseTransformPoint(start);
            ed.deckDirLocal = af.InverseTransformDirection(dir);
            if (ed.target != null)
            {
                Undo.RecordObject(ed.target, "Flight setup");
                ed.target.kind = 0;
                UdonSharpEditorUtility.CopyProxyToUdon(ed.target);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
            idx++;
        }
        log.AppendLine("基地の戦闘機: " + bf.Count + " 機を基地に配置（走り始め " + start.ToString("F0") + "・向き " + dir.ToString("F2") + "。3km で発進・6km の中だけ飛ぶ・観光モードでは飛ばない）");
        log.AppendLine("基地の戦闘機: 滑走する 250m の地面の高さ " + gmin.ToString("F1") + "〜" + gmax.ToString("F1") + (gmax - gmin > 3f ? "  ※ でこぼこがあります。BaseAirfield を平らな所へ動かしてください" : "（平ら）"));
    }

    // もともと飛んでいる敵の戦闘機を RegularFighterCount 機に（残りはしまう。編隊の戦闘機は残す）
    static void LimitRegularFighters(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var reg = fighters.FindAll(g => g.name.StartsWith("EnemyFighter_"));
        reg.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // 編隊（ConfigureAwacs）と同じ順。先頭が編隊になる
        int on = 0, off = 0;
        for (int i = 0; i < reg.Count; i++)
        {
            bool keep = i < RegularFighterCount;
            Undo.RecordObject(reg[i], "Flight setup");
            reg[i].SetActive(keep);
            if (keep) on++; else off++;
        }
        log.AppendLine("戦闘機: もともと飛んでいる戦闘機を " + on + " 機に（" + off + " 機はしまいました）");
    }

    // 建物を壊せるようにする（壊れたら燃えるがれき・復活しない）。kind: 6 = 基地の建物（全部で 100 点）, -1 = 得点なし
    static FlightTarget MakeBuildingTarget(GameObject go, int kind, int hp, Material fxMat, Material smokeMat, AudioClip boom, Material charred, StringBuilder log)
    {
        Transform tr = go.transform;
        foreach (var nm in new[] { "Wreck", "BuildingBoom" })
        {
            Transform old; int guard = 0;
            while ((old = tr.Find(nm)) != null && guard++ < 10) Undo.DestroyObjectImmediate(old.gameObject);
        }
        var rends = new System.Collections.Generic.List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) rends.Add(r);
        if (rends.Count == 0) return null;
        // 大きさ: スキンメッシュの bounds は大きすぎることがあるので、メッシュそのものの大きさから測る
        bool hasB = false; Bounds b = new Bounds(tr.position, Vector3.zero);
        foreach (var r in rends)
        {
            Bounds rb2 = r.bounds;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null && smr.sharedMesh != null) rb2 = XformBounds(smr.transform, smr.sharedMesh.bounds);
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) rb2 = XformBounds(r.transform, mf.sharedMesh.bounds);
            if (!hasB) { b = rb2; hasB = true; } else b.Encapsulate(rb2);
        }
        // 前に付けた大きすぎる箱の当たり判定は付け直す
        foreach (var oldBc in go.GetComponents<BoxCollider>()) { Vector3 ws = Vector3.Scale(oldBc.size, tr.lossyScale); if (ws.x > b.size.x * 1.5f || ws.z > b.size.z * 1.5f) Undo.DestroyObjectImmediate(oldBc); }
        bool big = Mathf.Max(b.size.x, b.size.z) > 25f;
        var cols = new System.Collections.Generic.List<Collider>();
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
        if (cols.Count == 0)
        {
            // 当たり判定が無ければ、大きさに合わせた箱を付ける（弾・ミサイルが当たるように）
            var bc = Undo.AddComponent<BoxCollider>(go);
            bc.center = tr.InverseTransformPoint(b.center);
            Vector3 ls = tr.InverseTransformVector(b.size);
            bc.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            cols.Add(bc);
        }
        var ft = go.GetComponent<FlightTarget>();
        if (ft == null) ft = UdonSharpUndo.AddComponent<FlightTarget>(go);
        Undo.RecordObject(ft, "Flight setup");
        ft.kind = kind;
        ft.maxHp = hp;
        ft.missileDamage = 15;
        ft.noRespawn = true;
        ft.respawnTime = 99999f;
        ft.body = null;
        ft.renderers = rends.ToArray();
        ft.colliders = cols.ToArray();

        // 爆発（壊れた瞬間）
        var boomPs = MakePS("BuildingBoom", tr, fxMat);
        boomPs.transform.position = b.center;
        var bm = boomPs.main;
        bm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        bm.startSpeed = new ParticleSystem.MinMaxCurve(8f, 25f);
        bm.startSize = new ParticleSystem.MinMaxCurve(6f, 14f);
        bm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.4f));
        bm.maxParticles = 200;
        var be = boomPs.emission;
        be.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
        var bsh = boomPs.shape; bsh.enabled = true; bsh.shapeType = ParticleSystemShapeType.Sphere; bsh.radius = Mathf.Max(b.extents.x, b.extents.z) * 0.5f;
        SetFade(boomPs, new Color(1f, 0.8f, 0.4f), new Color(0.3f, 0.1f, 0.05f));
        ft.explosionFx = boomPs;
        var au = Undo.AddComponent<AudioSource>(boomPs.gameObject);
        au.clip = boom; au.playOnAwake = false; au.spatialBlend = 1f; au.minDistance = 40f; au.maxDistance = 2500f; au.volume = 1f;
        au.rolloffMode = AudioRolloffMode.Linear;
        ft.explosionAudio = au;

        // がれき・炎・黒煙（壊れた後だけ出る）
        var wreck = new GameObject("Wreck");
        Undo.RegisterCreatedObjectUndo(wreck, "Flight setup");
        wreck.transform.SetParent(tr, false);
        wreck.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        wreck.transform.rotation = Quaternion.identity;
        SetWorldScaleOne(wreck.transform);
        var rnd = new System.Random(go.name.GetHashCode());
        float sx = b.size.x, sz = b.size.z, hy = b.size.y;
        int pieces = big ? 14 : 7;
        for (int k = 0; k < pieces; k++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Rubble";
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(wreck.transform, false);
            float fx = (float)rnd.NextDouble() - 0.5f, fz = (float)rnd.NextDouble() - 0.5f;
            bool beam = k % 4 == 0;
            Vector3 sc = beam
                ? new Vector3(0.6f, 0.6f, Mathf.Max(sx, sz) * (0.3f + 0.3f * (float)rnd.NextDouble()))
                : new Vector3(sx * (0.12f + 0.2f * (float)rnd.NextDouble()), hy * (0.05f + 0.12f * (float)rnd.NextDouble()), sz * (0.12f + 0.2f * (float)rnd.NextDouble()));
            c.transform.localScale = sc;
            c.transform.localPosition = new Vector3(fx * sx * 0.8f, sc.y * 0.35f, fz * sz * 0.8f);
            c.transform.localRotation = Quaternion.Euler((float)rnd.NextDouble() * (beam ? 40f : 15f) - (beam ? 20f : 7f), (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 20f - 10f);
            var mr = c.GetComponent<MeshRenderer>();
            mr.sharedMaterial = charred;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        int fires = big ? 3 : 1;
        float fireSize = Mathf.Clamp(Mathf.Max(sx, sz) * 0.12f, 3f, 9f);
        for (int k = 0; k < fires; k++)
        {
            Vector3 lp = new Vector3(((float)rnd.NextDouble() - 0.5f) * sx * 0.6f, 1f, ((float)rnd.NextDouble() - 0.5f) * sz * 0.6f);
            var fire = MakePS("Fire" + k, wreck.transform, fxMat);
            fire.transform.localPosition = lp;
            var fm = fire.main;
            fm.loop = true; fm.playOnAwake = true;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            fm.startSize = new ParticleSystem.MinMaxCurve(fireSize * 0.6f, fireSize);
            fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
            fm.gravityModifier = -0.3f;
            fm.maxParticles = 200;
            var fe = fire.emission; fe.rateOverTime = 25f;
            var fsh = fire.shape; fsh.enabled = true; fsh.shapeType = ParticleSystemShapeType.Sphere; fsh.radius = fireSize * 0.4f;
            var fsol = fire.sizeOverLifetime; fsol.enabled = true; fsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            SetFade(fire, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));

            var smoke = MakePS("Smoke" + k, wreck.transform, smokeMat);
            smoke.transform.localPosition = lp + Vector3.up * fireSize;
            var sm = smoke.main;
            sm.loop = true; sm.playOnAwake = true;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            sm.startSize = new ParticleSystem.MinMaxCurve(fireSize * 1.5f, fireSize * 2.5f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.08f, 0.08f, 0.08f, 0.75f), new Color(0.25f, 0.25f, 0.25f, 0.6f));
            sm.gravityModifier = -0.05f;
            sm.maxParticles = 250;
            var se = smoke.emission; se.rateOverTime = 6f;
            var ssh = smoke.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = fireSize * 0.5f;
            var ssol = smoke.sizeOverLifetime; ssol.enabled = true; ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
            SetFade(smoke, new Color(0.2f, 0.2f, 0.2f), new Color(0.5f, 0.5f, 0.5f));
        }
        wreck.SetActive(false);
        ft.wreck = wreck;
        UdonSharpEditorUtility.CopyProxyToUdon(ft);
        if (log != null) log.AppendLine("建物: " + go.name + "（大きさ " + b.size.ToString("F0") + "・耐久 " + ft.maxHp + "・当たり判定 " + cols.Count + " 個）");
        return ft;
    }

    // ---------------- 基地の建物（House_2・Old Hangar）: 壊れたら燃えるがれき・復活しない ----------------
    static System.Collections.Generic.List<FlightTarget> s_baseBuildings = new System.Collections.Generic.List<FlightTarget>();
    static void SetupBaseBuildings(StringBuilder log)
    {
        s_baseBuildings.Clear();
        GameObject baseGo = GameObject.Find("military base");
        if (baseGo == null) { log.AppendLine("基地の建物: military base が見つからない"); return; }
        Vector3 bp = baseGo.transform.position;
        var list = new System.Collections.Generic.List<GameObject>();
        Transform house = FindDeepName(baseGo.transform, "House_2");
        if (house != null) list.Add(house.gameObject);
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (!t.name.StartsWith("Old Hangar")) continue;
            if (t.parent != null && t.parent.name.StartsWith("Old Hangar")) continue;
            Vector3 d = t.position - bp; d.y = 0f;
            if (d.magnitude > 1500f) continue;
            list.Add(t.gameObject);
        }
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (smokeMat == null) smokeMat = fxMat;
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        Material charred = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/Rubble.mat");
        if (charred == null)
        {
            charred = new Material(Shader.Find("Standard"));
            charred.color = new Color(0.16f, 0.14f, 0.13f);
            charred.SetFloat("_Glossiness", 0.05f);
            AssetDatabase.CreateAsset(charred, FxDir + "/Rubble.mat");
        }
        foreach (var go in list)
        {
            var ft = MakeBuildingTarget(go, 6, 30, fxMat, smokeMat, boom, charred, log);   // どちらもミサイル 2 発
            if (ft != null) s_baseBuildings.Add(ft);
        }
        log.AppendLine("基地の建物: " + s_baseBuildings.Count + " 棟を壊せるように（壊れたら燃えるがれき・復活しない・全部壊すと 100 点）");
    }

    // 人が住む島・アストラル島の建物: 壊せる（得点なし・復活しない）。敵がいない所で撃っても攻撃開始にはならず、警告は戦艦などに見つかった時に始まる
    static void SetupTownBuildings(StringBuilder log)
    {
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (smokeMat == null) smokeMat = fxMat;
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        Material charred = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/Rubble.mat");
        foreach (var isl in new[] { new[] { "人がいる島", "人が住む島" }, new[] { "Astral island(アストラル）", "アストラル島" } })
        {
            GameObject root = GameObject.Find(isl[0]);
            if (root == null) { log.AppendLine("建物: " + isl[0] + " が見つからない"); continue; }
            int n = 0, small = 0;
            foreach (Transform ch in root.transform)
            {
                string nm = ch.name;
                bool house = nm.StartsWith("BigHousePrefab") || nm.StartsWith("OldHousePrefab") || (nm.StartsWith("House") && nm.Length > 5 && char.IsDigit(nm[5]));
                if (!house) continue;
                Bounds bb;
                bool isBig = MeshBounds(ch, "", out bb) && Mathf.Max(bb.size.x, bb.size.z) > 25f;
                var ft = MakeBuildingTarget(ch.gameObject, -1, isBig ? 30 : 15, fxMat, smokeMat, boom, charred, null);   // 大きい家はミサイル 2 発、小さい家は 1 発
                if (ft == null) continue;
                n++; if (!isBig) small++;
            }
            log.AppendLine("建物: " + isl[1] + " の建物 " + n + " 棟を壊せるように（小さい家 " + small + " 棟はミサイル 1 発、大きい家は 2 発。得点なし・復活しない）");
        }
    }

    static Bounds XformBounds(Transform t, Bounds lb)
    {
        Vector3 c = lb.center, e = lb.extents;
        Bounds w = new Bounds(t.TransformPoint(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
            w.Encapsulate(t.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return w;
    }

    static Transform FindDeepName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform ch in root) { var r = FindDeepName(ch, name); if (r != null) return r; }
        return null;
    }

    // ---------------- 遊覧モード・フレア ----------------
    static void SetupSightseeing(StringBuilder log)
    {
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip pop = LoadClip("flare_pop.wav");
        AudioClip[] ew = { LoadClip("escort_welcome1.wav"), LoadClip("escort_welcome2.wav") };
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.ainWelcome = LoadClip("ain_welcome.wav");
            c.ainRegret = LoadClip("ain_regret.wav");
            c.guanWelcome = LoadClip("guan_welcome.wav");
            c.escortBye = LoadClip("escort_bye.wav");
            c.escortWelcome = ew;
            c.voiceFlare = LoadClip("voice_flare.wav");
            c.gaurExercise = new[] { LoadClip("gaur_exercise1.wav"), LoadClip("gaur_exercise2.wav") };
            c.elcGreet = new[] { LoadClip("elc_greet1.wav"), LoadClip("elc_greet2.wav") };
            // フレアの光（機体から少し後ろに、明るい玉がばらまかれて落ちていく）
            Transform car = c.car.transform;
            Transform old; int guard = 0;
            while ((old = car.Find("FlareFx")) != null && guard++ < 10) Undo.DestroyObjectImmediate(old.gameObject);
            var ps = MakePS("FlareFx", car, fxMat);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(10f, 22f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.75f, 1f), new Color(1f, 0.7f, 0.35f, 1f));
            m.gravityModifier = 0.35f;
            m.maxParticles = 200;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.5f;
            ps.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var trl = ps.trails; trl.enabled = true; trl.ratio = 1f; trl.lifetime = 0.35f; trl.widthOverTrail = 0.6f;
            var pr = ps.GetComponent<ParticleSystemRenderer>(); pr.trailMaterial = fxMat;
            SetFade(ps, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.5f, 0.2f));
            c.flareFx = ps;
            var au = ps.gameObject.AddComponent<AudioSource>();
            au.clip = pop; au.playOnAwake = false; au.spatialBlend = 1f; au.minDistance = 20f; au.maxDistance = 800f; au.volume = 0.8f;
            c.flareAudio = au;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        // 歓迎の編隊: 普通の戦闘機のうち、警戒機の編隊に入っていない物（最大 5 機）
        var list = new System.Collections.Generic.List<EnemyDrone>();
        var wingSet = new System.Collections.Generic.HashSet<EnemyDrone>();   // 警戒機の編隊の戦闘機は入れない
        foreach (var aw in Object.FindObjectsOfType<EnemyDrone>(true)) if (aw.isAwacs && aw.wingmen != null) foreach (var w in aw.wingmen) if (w != null) wingSet.Add(w);
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.welcomeSlot = -1;
            if (ed.rb == null || !ed.rb.gameObject.activeInHierarchy) continue;
            GameObject g = ed.rb.gameObject;
            if (!g.name.StartsWith("EnemyFighter_") || ed.isAwacs || ed.carrierBased || ed.formationLeader != null || wingSet.Contains(ed)) continue;
            list.Add(ed);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        // 警戒機の編隊は、名前順の先頭から 2 機ずつ（ConfigureAwacs と同じ順）。その分を飛ばす
        int awN = 0;
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true)) if (ed.rb != null && ed.rb.gameObject.activeInHierarchy && ed.rb.name.StartsWith("AWACS")) awN++;
        int skip = Mathf.Min(awN * 2, list.Count);
        list.RemoveRange(0, skip);
        if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
        for (int i = 0; i < list.Count; i++) list[i].welcomeSlot = i;
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true)) UdonSharpEditorUtility.CopyProxyToUdon(ed);
        var gm = Object.FindObjectOfType<GameMode>(true);
        if (gm != null) { Undo.RecordObject(gm, "Flight setup"); gm.welcomeFlight = list.ToArray(); UdonSharpEditorUtility.CopyProxyToUdon(gm); }
        var names = new System.Collections.Generic.List<string>(); foreach (var e in list) names.Add(e.rb.name);
        log.AppendLine("遊覧モード: " + n + " 機に歓迎の無線・フレア（X キー・24 発）を設定。歓迎の編隊 " + list.Count + " 機（" + string.Join("・", names.ToArray()) + "）");
    }

    // ---------------- アストラル空港 ----------------
    static void SetupAstralAirport(StringBuilder log)
    {
        GameObject isl = GameObject.Find("Astral island(アストラル）");
        if (isl == null) { log.AppendLine("アストラル空港: Astral island(アストラル） が見つからない"); return; }
        Transform ap = isl.transform.Find("AstralAirport");
        if (ap == null)
        {
            var g = new GameObject("AstralAirport");
            Undo.RegisterCreatedObjectUndo(g, "Flight setup");
            ap = g.transform;
            ap.SetParent(isl.transform, false);
        }
        // 置いてある飛行機（ComPlaneV1）のまん中 = エプロン。島を動かしても合うよう、毎回置き直す
        {
            Vector3 sum = Vector3.zero; int k = 0;
            foreach (var t in isl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("ComPlaneV1") && (t.parent == null || !t.parent.name.StartsWith("ComPlaneV1"))) { sum += t.position; k++; }
            Vector3 p0 = k > 0 ? sum / k : isl.transform.position;
            Undo.RecordObject(ap, "Flight setup");
            ap.position = p0;
            log.AppendLine("アストラル空港: 空港の中心 AstralAirport を " + p0.ToString("F0") + " に（置いてある旅客機 " + k + " 機のまん中。ここから 900m 以内に止まると補給）");
        }
        AudioClip[] args = new AudioClip[7];
        for (int i = 0; i < 7; i++) args[i] = LoadClip("astral_arg" + (i + 1) + ".wav");
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.astralAirport = ap;
            c.astralIntro = LoadClip("astral_intro.wav");
            c.astralClear = LoadClip("astral_clear.wav");
            c.astralWelcome = LoadClip("astral_welcome.wav");
            c.astralArgs = args;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        var gm = Object.FindObjectOfType<GameMode>(true);
        if (gm != null) { Undo.RecordObject(gm, "Flight setup"); gm.astralAirport = ap; UdonSharpEditorUtility.CopyProxyToUdon(gm); }
        log.AppendLine("アストラル空港: " + n + " 機に設定（5km で管制室の無線・2.5km 以内でフラップを下げてゆっくり飛ぶと着陸の無線・言い合い 7 種）" + (args[0] == null ? "  ※ 音声が見つからない" : "") + (gm == null ? "  ※ GameMode が無い" : ""));
    }

    // ---------------- 速さ（本物の飛行機に近づける） ----------------
    // プレイヤー: ジェット機風（速くても推力が落ちない）。最高速度 約 170m/s（約 600km/h）
    //   最高速度 = 推力と空気抵抗が釣り合う速さ: 0.5 × 1.225 × 170² × 16m² × CD0 0.035 ≒ 9900N
    // 敵: 約 140m/s（約 500km/h）。警戒機 約 100m/s。旋回・ミサイルも速さに合わせる
}
