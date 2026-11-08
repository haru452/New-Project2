using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// サムネイル用の写真: ミリタリーベースの上を飛ぶ機体（Omega_111 の複製。脚はしまう）を、いくつかの角度から撮る
//  Tools/サムネイルの写真を撮る、または Backup_flight/run_thumb.flag。写真は Thumbnails/ に（VRChat 用 1200x900 と 1920x1080）
//  撮り終わったら複製とカメラは消す（シーンは変えない）
[InitializeOnLoad]
public static class ThumbnailShot
{
    const string FlagPath = "Backup_flight/run_thumb.flag";
    const string OutDir = "Thumbnails";

    // 角度ごとの設定: 機体が基地へ向かう向き（度）。機体は基地の手前 dist m・海面から alt m を飛び、カメラは機体の後ろ・横・上から基地の方を見る
    struct Shot { public string name; public float heading, dist, alt, back, right, up, toward, bank; }
    // dist がマイナス = 基地を通り過ぎて、カメラの方へ飛んでくる（back がマイナス = カメラは機体の前。機首がこっちを向く）
    static readonly Shot[] Shots =
    {
        new Shot { name = "f1", heading = 90f,  dist = -250f, alt = 400f, back = -26f, right = 8f,  up = 4f, toward = 0.25f, bank = 15f },
        new Shot { name = "f2", heading = 90f,  dist = -250f, alt = 400f, back = -24f, right = -9f, up = 6f, toward = 0.25f, bank = -15f },
        new Shot { name = "f3", heading = 105f, dist = -200f, alt = 380f, back = -30f, right = 10f, up = 2f, toward = 0.2f,  bank = 25f },
        new Shot { name = "f4", heading = 75f,  dist = -300f, alt = 420f, back = -22f, right = -6f, up = 8f, toward = 0.3f,  bank = -8f },
    };

    static ThumbnailShot() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/サムネイルの写真を撮る")]
    public static void Run()
    {
        var log = new StringBuilder();
        GameObject plane = null, camGo = null;
        try
        {
            // 基地の真ん中（島の名前 → 無ければ海図の場所）
            Vector3 baseC = new Vector3(2927f, 0f, 504f);
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
                if (t.name == "Military base" && t.GetComponentInParent<ChartMapLive>(true) == null) { baseC = t.position; break; }
            baseC.y = Ground(baseC);
            log.AppendLine("基地: " + baseC.ToString("F0"));

            GameObject tmpl = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Omega_111" && t.parent == null) tmpl = t.gameObject;
            if (tmpl == null) { log.AppendLine("ERROR Omega_111 が無い"); Save(log); return; }
            plane = Object.Instantiate(tmpl);
            plane.name = "ThumbPlane(消してよい)";
            plane.hideFlags = HideFlags.DontSave;
            plane.SetActive(true);
            GearUp(plane);
            Transform axis = plane.transform.Find("FlightAxis");
            Quaternion axisLocal = axis != null ? Quaternion.Inverse(plane.transform.rotation) * axis.rotation : Quaternion.identity;

            camGo = new GameObject("ThumbCam(消してよい)");
            camGo.hideFlags = HideFlags.DontSave;
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 30000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowMSAA = true;

            Directory.CreateDirectory(OutDir);
            foreach (var s in Shots)
            {
                Vector3 fwd = Quaternion.Euler(0f, s.heading, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, s.heading, 0f) * Vector3.right;
                Vector3 p = baseC - fwd * s.dist; p.y = s.alt;
                // 機首は基地の方へ少し下げる
                Vector3 nose = (baseC - p).normalized;
                plane.transform.rotation = Quaternion.LookRotation(s.dist > 0f ? Vector3.Lerp(fwd, nose, 0.4f) : (fwd - Vector3.up * 0.06f).normalized, Vector3.up) * Quaternion.Euler(0f, 0f, s.bank) * Quaternion.Inverse(axisLocal);
                plane.transform.position = p;
                Vector3 cp = p - fwd * s.back + right * s.right + Vector3.up * s.up;
                // 前から撮る時は、機体を見て、その奥に基地（低めに）が入るように
                Vector3 look = s.dist > 0f ? Vector3.Lerp(p, baseC, s.toward) : Vector3.Lerp(p, new Vector3(baseC.x, p.y - 120f, baseC.z), s.toward);
                camGo.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look - cp, Vector3.up));
                Render(cam, 1200, 900, OutDir + "/thumb_" + s.name + "_1200x900.png");
                Render(cam, 1920, 1080, OutDir + "/thumb_" + s.name + "_1920x1080.png");
                log.AppendLine("撮影: " + s.name);
            }
            log.AppendLine("DONE  写真: " + Path.GetFullPath(OutDir));
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
        finally
        {
            if (plane != null) Object.DestroyImmediate(plane);
            if (camGo != null) Object.DestroyImmediate(camGo);
        }
        Save(log);
    }

    static void GearUp(GameObject plane)
    {
        foreach (var c in plane.GetComponentsInChildren<controller>(true))
        {
            if (c.gearRoot != null) c.gearRoot.SetActive(false);
            if (c.gearVisuals != null) foreach (var g in c.gearVisuals) if (g != null) g.SetActive(false);
        }
    }

    static float Ground(Vector3 p)
    {
        float y = 0f;
        foreach (var t in Terrain.activeTerrains)
        {
            var b = new Bounds(t.transform.position + t.terrainData.size / 2f, t.terrainData.size);
            if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z) y = Mathf.Max(y, t.SampleHeight(p) + t.transform.position.y);
        }
        if (Physics.Raycast(new Vector3(p.x, 3000f, p.z), Vector3.down, out RaycastHit hit, 6000f)) y = Mathf.Max(y, hit.point.y);
        return y;
    }

    static void Render(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.aspect = (float)w / h;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release(); Object.DestroyImmediate(rt);
    }

    static void Save(StringBuilder log) { Directory.CreateDirectory("Backup_flight"); File.WriteAllText("Backup_flight/thumb_report.txt", log.ToString()); }
}
