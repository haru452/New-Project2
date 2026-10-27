using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 博物館の馬の像の横に、説明の札（展示物⑥）を置く。
// 条約書の札（Cap_Treaty）を複製して、絵だけ差し替える。一度置いた後は手で動かした位置をそのまま使う。
// メニュー「Tools/馬の像の札を置く」。Backup_flight/run_horse.flag があれば再読み込み時に一度だけ自動実行。
[InitializeOnLoad]
public static class HorseCaption
{
    const string Flag = "Backup_flight/run_horse.flag";
    const string Report = "Backup_flight/horse_report.txt";
    const string TexPath = "Assets/My script/museum/Panels2/cap_horse.png";
    const string MatPath = "Assets/My script/museum/Materials/V2_cap_horse.mat";
    static HorseCaption() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }

    [MenuItem("Tools/馬の像の札を置く")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("HorseCaption " + System.DateTime.Now);
        try
        {
            // 絵
            var ti = AssetImporter.GetAtPath(TexPath) as TextureImporter;
            if (ti == null) { AssetDatabase.ImportAsset(TexPath); ti = AssetImporter.GetAtPath(TexPath) as TextureImporter; }
            if (ti == null) { log.AppendLine("ERROR: " + TexPath + " が無い"); return; }
            ti.maxTextureSize = 2048; ti.mipmapEnabled = true; ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null) { mat = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(mat, MatPath); }
            mat.shader = Shader.Find("Unlit/Texture"); mat.mainTexture = tex; EditorUtility.SetDirty(mat);

            // 馬の像（Assets/Horse Statue のメッシュを使っている物）
            Transform horse = null;
            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith("Assets/Horse Statue")) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(mf.gameObject);
                horse = root != null ? root.transform : mf.transform;
                break;
            }
            if (horse == null) { log.AppendLine("ERROR: 馬の像が見つからない"); return; }
            Bounds hb = new Bounds(horse.position, Vector3.zero); bool any = false;
            foreach (var r in horse.GetComponentsInChildren<Renderer>(true)) { if (!any) { hb = r.bounds; any = true; } else hb.Encapsulate(r.bounds); }
            log.AppendLine("馬の像: " + horse.name + "  中心 " + hb.center.ToString("F2") + "  大きさ " + hb.size.ToString("F2"));

            GameObject treaty = GameObject.Find("Cap_Treaty");
            if (treaty == null) { log.AppendLine("ERROR: Cap_Treaty が見つからない"); return; }
            Transform P = treaty.transform.parent;
            Transform cap = P.Find("Cap_Horse");
            if (cap == null)
            {
                var g = Object.Instantiate(treaty, P);
                g.name = "Cap_Horse";
                Undo.RegisterCreatedObjectUndo(g, "Horse caption");
                cap = g.transform;
                // 馬の像の横（通路の真ん中 = 親の中の x=0 に近い側）
                Vector3 hl = P.InverseTransformPoint(hb.center);
                Vector3 ext = P.InverseTransformVector(hb.extents);
                float side = Mathf.Abs(ext.x) + 0.9f;
                Vector3 lp = new Vector3(hl.x > 0f ? hl.x - side : hl.x + side, treaty.transform.localPosition.y, hl.z);
                cap.localPosition = lp;
                cap.localRotation = treaty.transform.localRotation;
                log.AppendLine("札を置きました: " + cap.position.ToString("F2") + "（条約書の札と同じ向き。ずれていたら手で動かしてください）");
            }
            else log.AppendLine("札はもうあるので、位置はそのまま。絵だけ差し替えます");
            int n = 0;
            foreach (var mr in cap.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name != "Face") continue;
                Undo.RecordObject(mr, "Horse caption");
                mr.sharedMaterial = mat; n++;
            }
            log.AppendLine("絵を差し替え: " + n + " 枚");
            // 馬の像のまわりでも、慰霊の間（折り鶴）と同じ曲を流す範囲
            var maud = Object.FindObjectOfType<MuseumAudio>(true);
            if (maud != null)
            {
                Transform hz = P.Find("HorseZone");
                if (hz == null)
                {
                    var hzg = new GameObject("HorseZone");
                    Undo.RegisterCreatedObjectUndo(hzg, "Horse caption");
                    hz = hzg.transform; hz.SetParent(P, false);
                    hz.localRotation = Quaternion.identity;
                    Vector3 c = hb.center; c.y = hb.min.y + 2.5f;
                    hz.position = c;
                    Vector3 sz = P.InverseTransformVector(hb.size);
                    hz.localScale = new Vector3(Mathf.Abs(sz.x) + 8f, 6f, Mathf.Abs(sz.z) + 8f);   // 像のまわり 4m まで
                    log.AppendLine("曲の範囲 HorseZone を作りました（大きさ " + hz.localScale.ToString("F1") + "。手で広げられます）");
                }
                Undo.RecordObject(maud, "Horse caption");
                maud.horseZone = hz;
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(maud);
            }
            else log.AppendLine("MuseumAudio が見つからない");
            EditorSceneManager.MarkSceneDirty(cap.gameObject.scene);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveOpenScenes() ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        finally { File.WriteAllText(Report, log.ToString()); }
    }
}
