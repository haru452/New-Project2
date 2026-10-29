using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.Dynamics;

// 博物館の VTR を作る（分岐の間の右の通路の奥のスクリーン + 離れた所の撮影所）
// メニュー「Tools/博物館の VTR を作る」。Backup_flight/run_vtr.flag があれば再読み込み時に一度だけ自動実行。
// もう一度実行すると、スクリーン・撮影所を作り直す（手で動かしたスクリーンの位置・向きは残す）
[InitializeOnLoad]
public static class VtrSetup
{
    const string Flag = "Backup_flight/run_vtr.flag";
    const string Report = "Backup_flight/vtr_report.txt";
    const string Dir = "Assets/My script/museum/VTR";
    const string ScriptPath = "Assets/My script/museum/VtrPlayer.cs";
    const string ProgPath = "Assets/My script/museum/VtrPlayer.asset";
    static readonly Vector3 StudioPos = new Vector3(0f, 0f, 24000f);   // 誰も来ない遠くの海の上

    static VtrSetup() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }

    static T Load<T>(string p) where T : Object { return AssetDatabase.LoadAssetAtPath<T>(p); }
    static Texture2D Tex(string name)
    {
        string p = Dir + "/" + name + ".png";
        var ti = AssetImporter.GetAtPath(p) as TextureImporter;
        if (ti != null)
        {
            bool ch = false;
            if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; ch = true; }
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ch = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; ch = true; }
            if (ch) ti.SaveAndReimport();
        }
        return Load<Texture2D>(p);
    }
    static Material Mat(string name, Shader sh, Texture tex, int queue)
    {
        Directory.CreateDirectory(Dir + "/Materials");
        string p = Dir + "/Materials/" + name + ".mat";
        var m = Load<Material>(p);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh;
        if (tex != null) m.mainTexture = tex;
        if (m.HasProperty("_Color")) m.color = Color.white;
        if (queue > 0) m.renderQueue = queue;
        EditorUtility.SetDirty(m);
        return m;
    }
    static Renderer Quad(string name, Transform parent, float z, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = new Vector3(0f, 0f, z);
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return r;
    }
    static AnimationClip Clip(string fbx)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is AnimationClip && !o.name.StartsWith("__")) return (AnimationClip)o;
        return null;
    }
    static void StripTo(GameObject go, params System.Type[] keep)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            bool any = false;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                bool k = false; foreach (var t in keep) if (t.IsInstanceOfType(c)) k = true;
                if (k) continue;
                try { Object.DestroyImmediate(c); any = true; } catch { }
            }
            if (!any) break;
        }
    }

    // VRM 1.0 の揺れもの（SpringBone）を、ワールドでも動く VRChat の PhysBone に置き換える
    static void ConvertSprings(GameObject root, StringBuilder log)
    {
        var joints = root.GetComponentsInChildren<UniVRM10.VRM10SpringBoneJoint>(true);
        var set = new System.Collections.Generic.HashSet<Transform>();
        foreach (var j in joints) set.Add(j.transform);
        // 当たり判定（頭・体・腕など）
        var cols = new System.Collections.Generic.List<VRCPhysBoneColliderBase>();
        foreach (var c in root.GetComponentsInChildren<UniVRM10.VRM10SpringBoneCollider>(true))
        {
            var pc = c.gameObject.AddComponent<VRCPhysBoneCollider>();
            pc.rootTransform = c.transform;
            pc.radius = c.Radius;
            bool inside = c.ColliderType == UniVRM10.VRM10SpringBoneColliderTypes.SphereInside || c.ColliderType == UniVRM10.VRM10SpringBoneColliderTypes.CapsuleInside;
            pc.insideBounds = inside;
            if (c.ColliderType == UniVRM10.VRM10SpringBoneColliderTypes.Capsule || c.ColliderType == UniVRM10.VRM10SpringBoneColliderTypes.CapsuleInside)
            {
                Vector3 d = c.Tail - c.Offset;
                pc.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
                pc.position = (c.Offset + c.Tail) * 0.5f;
                pc.height = d.magnitude + c.Radius * 2f;
                pc.rotation = d.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, d.normalized) : Quaternion.identity;
            }
            else if (c.ColliderType == UniVRM10.VRM10SpringBoneColliderTypes.Plane)
            {
                pc.shapeType = VRCPhysBoneColliderBase.ShapeType.Plane;
                pc.position = c.Offset;
                pc.rotation = Quaternion.FromToRotation(Vector3.up, c.Normal);
            }
            else { pc.shapeType = VRCPhysBoneColliderBase.ShapeType.Sphere; pc.position = c.Offset; }
            cols.Add(pc);
        }
        // 揺れものの根元（親が揺れものでない関節）ごとに 1 つ
        int n = 0;
        foreach (var j in joints)
        {
            if (j.transform.parent != null && set.Contains(j.transform.parent)) continue;
            float stiff = 0f, drag = 0f, grav = 0f, rad = 0f; int k = 0;
            foreach (var jj in j.GetComponentsInChildren<UniVRM10.VRM10SpringBoneJoint>(true)) { stiff += jj.m_stiffnessForce; drag += jj.m_dragForce; grav += jj.m_gravityPower; rad += jj.m_jointRadius; k++; }
            if (k == 0) continue;
            stiff /= k; drag /= k; grav /= k; rad /= k;
            var pb = j.gameObject.AddComponent<VRCPhysBone>();
            pb.rootTransform = j.transform;
            pb.pull = Mathf.Clamp(0.08f + 0.06f * stiff, 0.05f, 0.5f);
            pb.spring = Mathf.Clamp(0.6f * (1f - drag) + 0.1f, 0f, 0.8f);
            pb.stiffness = Mathf.Clamp(0.05f * stiff, 0f, 0.3f);
            pb.gravity = Mathf.Clamp(grav * 0.5f, 0f, 0.5f);
            pb.radius = rad;
            pb.colliders = new System.Collections.Generic.List<VRCPhysBoneColliderBase>(cols);
            n++;
        }
        log.AppendLine("なつき: 揺れもの " + joints.Length + " 関節を PhysBone " + n + " 本に（当たり判定 " + cols.Count + " 個）");
    }

    // スクリーン（黒い板 + 映像の板 + 画像 2 枚 + R35 の影 + 字幕 + 音）。stand = 床に立てる台と看板を付ける
    static Transform BuildScreen(Transform parent, string name, Vector3 defPos, Quaternion defRot, bool stand, out bool keepPose,
        out Renderer film, out Renderer layA, out Renderer layB, out Renderer pl, out Renderer cap, out AudioSource voice, out AudioSource wind)
    {
        Transform old = parent.Find(name);
        keepPose = old != null;
        Vector3 lp = keepPose ? old.localPosition : defPos;
        Quaternion lr = keepPose ? old.localRotation : defRot;
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var scr = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(scr.gameObject, "VTR");
        scr.SetParent(parent, false); scr.localPosition = lp; scr.localRotation = lr;
        string sfx = stand ? "E" : "";
        var frameM = Mat("VtrFrame", Shader.Find("Standard"), null, 0); frameM.color = new Color(0.06f, 0.06f, 0.065f);
        System.Action<string, Vector3, Vector3> box = (nm, p, sc) =>
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = nm;
            if (nm != "Post") Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(scr, false); g.transform.localPosition = p; g.transform.localScale = sc;
            g.GetComponent<MeshRenderer>().sharedMaterial = frameM;
        };
        box("Frame", new Vector3(0f, 1.75f, 0.025f), new Vector3(1.74f, 1.04f, 0.04f));
        if (stand)
        {
            box("Post", new Vector3(0f, 0.62f, 0.05f), new Vector3(0.12f, 1.24f, 0.12f));
            box("Foot", new Vector3(0f, 0.02f, 0.05f), new Vector3(0.9f, 0.04f, 0.6f));
            // 看板「南アルカディア海の説明を簡単にしたものです。」
            var top = new GameObject("Sign").transform; top.SetParent(scr, false);
            top.localPosition = new Vector3(0f, 0.95f, -0.18f); top.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            var pg = GameObject.CreatePrimitive(PrimitiveType.Cube); pg.name = "Plate"; Object.DestroyImmediate(pg.GetComponent<Collider>());
            pg.transform.SetParent(top, false); pg.transform.localPosition = new Vector3(0f, 0f, 0.012f); pg.transform.localScale = new Vector3(0.96f, 0.5f, 0.02f);
            pg.GetComponent<MeshRenderer>().sharedMaterial = frameM;
            var sq = Quad("Face", top, 0f, Mat("VtrEntranceSign", Shader.Find("Unlit/Texture"), Tex("entrance_sign"), 0));
            sq.transform.localScale = new Vector3(0.9f, 0.45f, 1f);
        }
        var disp = new GameObject("Display").transform; disp.SetParent(scr, false);
        disp.localPosition = new Vector3(0f, 1.75f, 0f); disp.localScale = new Vector3(1.6f, 0.9f, 1f);
        var black = Mat("VtrBlack", Shader.Find("Unlit/Color"), null, 0); black.color = Color.black;
        Quad("Black", disp, 0.001f, black);
        Shader spr = Shader.Find("Sprites/Default");
        var rt = Load<RenderTexture>(Dir + "/VtrFilm.renderTexture");
        if (rt == null) { rt = new RenderTexture(1280, 720, 24); rt.antiAliasing = 2; AssetDatabase.CreateAsset(rt, Dir + "/VtrFilm.renderTexture"); }
        film = Quad("Film", disp, -0.001f, Mat("VtrFilm" + sfx, spr, rt, 3000));
        layA = Quad("LayerA", disp, -0.002f, Mat("VtrLayerA" + sfx, spr, null, 3001));
        layB = Quad("LayerB", disp, -0.003f, Mat("VtrLayerB" + sfx, spr, Tex("idle"), 3002));
        pl = Quad("Plane", disp, -0.004f, Mat("VtrPlane" + sfx, spr, Tex("r35_sil"), 3003));
        pl.transform.localScale = new Vector3(0.11f * 0.5625f, 0.11f, 1f);
        pl.transform.localRotation = Quaternion.Euler(0f, 0f, -55f);
        cap = Quad("Caption", disp, -0.005f, Mat("VtrCaption" + sfx, spr, null, 3004));
        if (stand) film.enabled = false;
        voice = scr.gameObject.AddComponent<AudioSource>();
        voice.clip = Load<AudioClip>(Dir + "/natsuki_itte.wav"); voice.playOnAwake = false; voice.spatialBlend = 0.7f; voice.minDistance = 3f; voice.maxDistance = 14f; voice.volume = 1f;
        var windGo = new GameObject("Wind"); windGo.transform.SetParent(scr, false); windGo.transform.localPosition = new Vector3(0f, 1.75f, 0f);
        wind = windGo.AddComponent<AudioSource>();
        wind.clip = Load<AudioClip>(Dir + "/vtr_wind.wav"); wind.playOnAwake = false; wind.loop = false; wind.spatialBlend = 0.7f; wind.minDistance = 3f; wind.maxDistance = 14f; wind.volume = 0.35f;
        return scr;
    }

    [MenuItem("Tools/博物館の VTR を作る")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("VtrSetup " + System.DateTime.Now);
        try
        {
            // ---- Udon のプログラム ----
            if (Load<UdonSharpProgramAsset>(ProgPath) == null)
            {
                var ms = Load<MonoScript>(ScriptPath);
                if (ms == null) { log.AppendLine("ERROR: " + ScriptPath + " が無い"); return; }
                var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                prog.sourceCsScript = ms;
                AssetDatabase.CreateAsset(prog, ProgPath);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
                log.AppendLine("VtrPlayer.asset を作ってコンパイル。もう一度自動で実行します");
                File.WriteAllText(Flag, "again");
                EditorApplication.delayCall += Try;
                return;
            }

            // ---- 分岐の間の右の通路 ----
            Transform fork = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Fork" && t.Find("Panel_Right") != null) { fork = t; break; }
            if (fork == null) { log.AppendLine("ERROR: 博物館の分岐の間（Fork）が見つからない"); return; }
            Transform right = null, mid = null;
            foreach (Transform ch in fork)
            {
                if (ch.name != "Partition") continue;
                if (right == null || ch.localPosition.x > right.localPosition.x) right = ch;
                if (mid == null || Mathf.Abs(ch.localPosition.x) < Mathf.Abs(mid.localPosition.x)) mid = ch;
            }
            if (right == null || mid == null || right == mid) { log.AppendLine("ERROR: 通路の仕切りが見つからない"); return; }
            float xIn = right.localPosition.x - right.localScale.x * 0.5f;            // 右の壁の内側
            float xMid = mid.localPosition.x + mid.localScale.x * 0.5f;              // 真ん中の仕切りの右側
            float zEnd = right.localPosition.z + right.localScale.z * 0.5f;          // 通路の奥（出口）
            log.AppendLine("右の通路: x " + xMid.ToString("F2") + "〜" + xIn.ToString("F2") + "、奥 z " + zEnd.ToString("F2"));

            // ---- 右の通路のスクリーン（手で動かした位置は残す）----
            Renderer film, layA, layB, pl, cap; AudioSource voice, wind;
            bool keepPose;
            Transform scr = BuildScreen(fork, "VtrScreen", new Vector3(xIn - 0.02f, 0f, zEnd - 1.05f), Quaternion.Euler(0f, 90f, 0f), false, out keepPose, out film, out layA, out layB, out pl, out cap, out voice, out wind);
            var rt = Load<RenderTexture>(Dir + "/VtrFilm.renderTexture");
            // 近づいたら再生する範囲（通路の奥）
            var trg = new GameObject("VtrTrigger");
            trg.transform.SetParent(fork, false);
            trg.transform.localPosition = new Vector3((xMid + xIn) * 0.5f, 1.5f, zEnd - 1.6f);
            trg.transform.localRotation = Quaternion.identity;
            var bc = trg.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(Mathf.Max(xIn - xMid, 0.8f), 3f, 3f);
            trg.transform.SetParent(scr, true);

            // ---- 入口のスクリーン（歴史と地図だけ。看板付き）----
            Transform exh = fork.parent;
            Transform sign = exh != null ? exh.Find("Sign") : null;
            Vector3 ep = sign != null ? new Vector3(-sign.localPosition.x, 0f, sign.localPosition.z) : new Vector3(2.2f, 0f, 3.4f);
            Quaternion er = Quaternion.Euler(0f, 180f - 20f, 0f);
            Renderer eFilm, eA, eB, ePl, eCap; AudioSource eV, eW; bool eKeep;
            Transform escr = BuildScreen(exh != null ? exh : fork, "VtrEntrance", ep, er, true, out eKeep, out eFilm, out eA, out eB, out ePl, out eCap, out eV, out eW);
            var etrg = new GameObject("VtrTrigger");
            etrg.transform.SetParent(escr, false);
            etrg.transform.localPosition = new Vector3(0f, 1.5f, -2.2f);
            var ebc = etrg.AddComponent<BoxCollider>(); ebc.isTrigger = true; ebc.size = new Vector3(3.2f, 3f, 3f);
            var evp = UdonSharpUndo.AddComponent<VtrPlayer>(etrg);
            evp.layerA = eA; evp.layerB = eB; evp.caption = eCap; evp.film = null; evp.plane = ePl;
            evp.idleTex = Tex("idle");
            evp.cards = new Texture[] { Tex("card1"), Tex("card2"), Tex("card3"), Tex("card4"), Tex("card5") };
            evp.maps = new Texture[] { Tex("map0"), Tex("map1"), Tex("map2"), Tex("map3"), Tex("map4"), Tex("map5") };
            evp.caps = new Texture[] { Tex("cap113"), Tex("cap114"), Tex("cap116"), Tex("cap120"), Tex("cap136") };
            evp.from = 0f; evp.to = 60f;
            UdonSharpEditorUtility.CopyProxyToUdon(evp);
            log.AppendLine("入口のスクリーン: " + (eKeep ? "手で動かした位置のまま" : "入口の案内板の反対側に置きました（" + escr.position.ToString("F1") + "）") + "。0〜60 秒（歴史と地図）・看板付き");

            // ---- 撮影所 ----
            GameObject oldSt = GameObject.Find("VtrStudio");
            if (oldSt == null) foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "VtrStudio" && t.parent == null) { oldSt = t.gameObject; break; }
            if (oldSt != null) Undo.DestroyObjectImmediate(oldSt);
            var st = new GameObject("VtrStudio").transform;
            Undo.RegisterCreatedObjectUndo(st.gameObject, "VTR");
            st.position = StudioPos;
            // 地面（駐機場のコンクリート）と線
            var gnd = GameObject.CreatePrimitive(PrimitiveType.Plane); gnd.name = "Apron";
            Object.DestroyImmediate(gnd.GetComponent<Collider>());
            gnd.transform.SetParent(st, false); gnd.transform.localScale = new Vector3(400f, 1f, 400f);
            var gm = Mat("VtrApron", Shader.Find("Standard"), null, 0); gm.color = new Color(0.46f, 0.46f, 0.44f); gm.SetFloat("_Glossiness", 0.12f);
            gnd.GetComponent<MeshRenderer>().sharedMaterial = gm;
            var ym = Mat("VtrLine", Shader.Find("Standard"), null, 0); ym.color = new Color(0.85f, 0.7f, 0.15f); ym.SetFloat("_Glossiness", 0.1f);
            foreach (var seg in new[] { new Vector4(-3f, -60f, -3f, 30f), new Vector4(12f, -60f, 12f, 30f), new Vector4(-20f, 14f, 30f, 14f) })
            {
                var ln = GameObject.CreatePrimitive(PrimitiveType.Cube); ln.name = "Line"; Object.DestroyImmediate(ln.GetComponent<Collider>());
                ln.transform.SetParent(st, false);
                Vector3 a = new Vector3(seg.x, 0.01f, seg.y), b = new Vector3(seg.z, 0.01f, seg.w);
                ln.transform.localPosition = (a + b) * 0.5f; ln.transform.localRotation = Quaternion.LookRotation(b - a);
                ln.transform.localScale = new Vector3(0.25f, 0.01f, (b - a).magnitude);
                ln.GetComponent<MeshRenderer>().sharedMaterial = ym;
            }
            // R35（今の R35 = Omega の見た目だけを写す）
            GameObject omega = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name.StartsWith("Omega_") && t.Find("OmegaBody") != null) { omega = t.gameObject; break; }
            var r35 = new GameObject("R35").transform; r35.SetParent(st, false);
            if (omega != null)
            {
                foreach (Transform ch in omega.transform)
                {
                    bool body = ch.name == "OmegaBody" || ch.name.Contains("Gear") || ch.name.Contains("gear") || ch.name.Contains("Wheel");
                    if (!body || ch.GetComponentInChildren<Renderer>(true) == null) continue;
                    var cp = Object.Instantiate(ch.gameObject);
                    cp.name = ch.name;
                    cp.transform.SetParent(r35, false);
                    cp.transform.localPosition = omega.transform.InverseTransformPoint(ch.position);
                    cp.transform.localRotation = Quaternion.Inverse(omega.transform.rotation) * ch.rotation;
                    cp.transform.localScale = ch.lossyScale;
                    cp.SetActive(true);
                    foreach (var tr2 in cp.GetComponentsInChildren<Transform>(true)) tr2.gameObject.SetActive(true);
                    StripTo(cp, typeof(MeshFilter), typeof(MeshRenderer), typeof(SkinnedMeshRenderer));
                }
                Bounds rb; bool hb = false; rb = new Bounds();
                foreach (var r in r35.GetComponentsInChildren<Renderer>(true)) { if (!hb) { rb = r.bounds; hb = true; } else rb.Encapsulate(r.bounds); }
                if (hb) r35.position += new Vector3(st.position.x - rb.center.x, st.position.y - rb.min.y, st.position.z - rb.center.z);
                log.AppendLine("R35: " + omega.name + " の見た目を写しました（大きさ " + rb.size.ToString("F1") + "）");
            }
            else log.AppendLine("※ R35（Omega）が見つからない");

            // なつき
            var nAsset = Load<GameObject>("Assets/My assets/natsuki.vrm");
            Transform nat = null; Animator anim = null; SkinnedMeshRenderer face = null; int smile = -1;
            if (nAsset != null)
            {
                var ng = (GameObject)Object.Instantiate(nAsset);
                ng.name = "Natsuki";
                nat = ng.transform; nat.SetParent(st, false);
                ConvertSprings(ng, log);   // VRM の揺れもの → VRChat の PhysBone（髪・服が揺れる）
                StripTo(ng, typeof(Animator), typeof(MeshFilter), typeof(MeshRenderer), typeof(SkinnedMeshRenderer), typeof(VRCPhysBone), typeof(VRCPhysBoneCollider));
                anim = ng.GetComponent<Animator>();
                if (anim == null) anim = ng.AddComponent<Animator>();
                if (anim.avatar == null)
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/My assets/natsuki.vrm")) if (o is Avatar) { anim.avatar = (Avatar)o; break; }
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // 待機・歩きの動き
                string cp = Dir + "/VtrNatsuki.controller";
                AssetDatabase.DeleteAsset(cp);
                var ac = AnimatorController.CreateAnimatorControllerAtPath(cp);
                ac.AddParameter("walk", AnimatorControllerParameterType.Bool);
                var sm = ac.layers[0].stateMachine;
                var idle = sm.AddState("Idle"); idle.motion = Clip("Assets/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx");
                var walk = sm.AddState("Walk"); walk.motion = Clip("Assets/StarterAssets/ThirdPersonController/Character/Animations/Locomotion--Walk_N.anim.fbx");
                sm.defaultState = idle;
                var t1 = idle.AddTransition(walk); t1.hasExitTime = false; t1.duration = 0.25f; t1.AddCondition(AnimatorConditionMode.If, 0f, "walk");
                var t2 = walk.AddTransition(idle); t2.hasExitTime = false; t2.duration = 0.3f; t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "walk");
                anim.runtimeAnimatorController = ac;
                // 笑顔の表情
                string[] keys = { "Fcl_ALL_Joy", "Fcl_MTH_Joy", "Joy", "Fun", "Smile", "smile", "Happy", "笑" };
                foreach (var k in keys)
                {
                    foreach (var s in ng.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (s.sharedMesh == null) continue;
                        for (int i = 0; i < s.sharedMesh.blendShapeCount; i++) if (s.sharedMesh.GetBlendShapeName(i).Contains(k)) { face = s; smile = i; break; }
                        if (face != null) break;
                    }
                    if (face != null) break;
                }
                foreach (var s in ng.GetComponentsInChildren<Renderer>(true)) { s.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
                log.AppendLine("なつき: " + (anim.avatar != null ? "人型の骨あり" : "※ 人型の骨が無い（歩けない）") + "・笑顔 " + (face != null ? face.name + " の " + face.sharedMesh.GetBlendShapeName(smile) : "見つからない"));
            }
            else log.AppendLine("※ natsuki.vrm が見つからない");

            // 歩く道・カメラの位置（撮影所の中の位置。R35 は真ん中）
            System.Func<string, Vector3, Vector3, Transform> mk = (nm, p, look) =>
            {
                var g = new GameObject(nm).transform; g.SetParent(st, false);
                g.localPosition = p;
                Vector3 d = look - p; if (d.sqrMagnitude < 0.001f) d = Vector3.forward;
                g.localRotation = Quaternion.LookRotation(d, Vector3.up);
                return g;
            };
            var wFrom = mk("WalkFrom", new Vector3(-5f, 0f, -43.2f), new Vector3(-4.6f, 0f, -39f));
            var wStop = mk("WalkStop", new Vector3(-4.6f, 0f, -39f), new Vector3(-2.5f, 0f, -25f));
            var wTo = mk("WalkTo", new Vector3(-2.5f, 0f, -25f), new Vector3(0f, 0f, 0f));
            var s0 = mk("Shot0_R35", new Vector3(-42f, 5f, -62f), new Vector3(0f, 2f, 0f));
            var s1 = mk("Shot1_Natsuki", new Vector3(-6.4f, 1.5f, -47.6f), new Vector3(-4.6f, 1.25f, -39f));
            var s2 = mk("Shot2_Back", new Vector3(-7.5f, 2.2f, -60f), new Vector3(-2.5f, 1.3f, -25f));
            if (nat != null) nat.SetPositionAndRotation(wFrom.position, wFrom.rotation);
            // カメラ
            var camGo = new GameObject("VtrCamera"); camGo.transform.SetParent(st, false);
            camGo.transform.SetPositionAndRotation(s0.position, s0.rotation);
            var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = rt; cam.fieldOfView = 34f; cam.nearClipPlane = 0.2f; cam.farClipPlane = 6000f;
            cam.clearFlags = CameraClearFlags.Skybox; cam.enabled = false;

            // ---- 再生の仕組み ----
            var vp = UdonSharpUndo.AddComponent<VtrPlayer>(trg);
            vp.layerA = layA; vp.layerB = layB; vp.caption = cap; vp.film = film; vp.plane = pl;
            vp.idleTex = Tex("idle");
            vp.cards = new Texture[] { Tex("card1"), Tex("card2"), Tex("card3"), Tex("card4"), Tex("card5") };
            vp.maps = new Texture[] { Tex("map0"), Tex("map1"), Tex("map2"), Tex("map3"), Tex("map4"), Tex("map5") };
            vp.caps = new Texture[] { Tex("cap113"), Tex("cap114"), Tex("cap116"), Tex("cap120"), Tex("cap136") };
            vp.noticeTex = Tex("notice"); vp.finalTex = Tex("final"); vp.subtitleTex = Tex("subtitle");
            vp.cam = cam; vp.shots = new Transform[] { s0, s1, s2 }; vp.studio = st.gameObject;
            vp.natsuki = nat; vp.anim = anim; vp.face = face; vp.smileIndex = smile;
            vp.walkFrom = wFrom; vp.walkStop = wStop; vp.walkTo = wTo;
            vp.voice = voice; vp.wind = wind;
            vp.from = 60f; vp.to = 100f;
            UdonSharpEditorUtility.CopyProxyToUdon(vp);
            st.gameObject.SetActive(false);   // 再生中だけ出す

            log.AppendLine("右の通路のスクリーン: 60〜100 秒（通達・基地・最後の一枚）。" + (keepPose ? "手で動かした位置のまま" : "右の壁（奥から 1m）に新しく置きました"));
            log.AppendLine("撮影所: " + StudioPos + " に駐機場・R35・なつき・カメラ 3 か所");
            var sc = fork.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(sc);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        finally { File.WriteAllText(Report, log.ToString()); }
    }
}
