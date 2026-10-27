using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

// 弔いの箱を「加護」の仕組みにする（手で動かした位置はそのまま）
//  ・数の表示（あなたの数・みんなの数）を消す
//  ・碑の上に「次に飛行機に乗ったときに 10 ポイントもらえます」の札（納めた後だけ出る）
//  ・折り鶴は 1 羽だけ
// メニュー「Tools/折り鶴の箱を更新する」。Backup_flight/run_crane.flag があれば再読み込み時に一度だけ自動実行。
[InitializeOnLoad]
public static class CraneBoxUpdate
{
    const string Flag = "Backup_flight/run_crane.flag";
    static CraneBoxUpdate() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }

    [MenuItem("Tools/折り鶴の箱を更新する")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("CraneBoxUpdate " + System.DateTime.Now);
        try
        {
            var mb = Object.FindObjectOfType<MemorialBox>(true);
            if (mb == null) { log.AppendLine("ERROR: MemorialBox が無い"); return; }
            Transform mem = mb.transform.parent;   // Memorial
            Undo.RecordObject(mb, "Crane");
            // 数の表示を消す
            int removed = 0;
            foreach (var n in new[] { "AllLabel", "AllCount", "CountLabel", "Count", "BlessMsg" })
            {
                var o = mem.Find(n);
                if (o != null) { Undo.DestroyObjectImmediate(o.gameObject); removed++; }
            }
            log.AppendLine("数の表示を消しました（" + removed + " 個）");
            // 碑の上の札
            Transform plaque = mem.Find("Plaque");
            float top = 2.9f;
            if (plaque != null)
            {
                Transform face = plaque.Find("Face");
                if (face != null) top = mem.InverseTransformPoint(face.position).y + face.lossyScale.y * 0.5f;
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/My script/museum/Panels2/bless_msg.png");
            GameObject msg = null;
            if (tex != null)
            {
                string mp = "Assets/My script/museum/Materials/BlessMsg.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(m, mp); }
                m.mainTexture = tex; EditorUtility.SetDirty(m);
                msg = GameObject.CreatePrimitive(PrimitiveType.Quad);
                msg.name = "BlessMsg"; Object.DestroyImmediate(msg.GetComponent<Collider>());
                msg.transform.SetParent(mem, false);
                msg.transform.localPosition = new Vector3(0f, top + 0.35f, -0.86f);
                msg.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                msg.transform.localScale = new Vector3(1.8f, 0.405f, 1f);
                msg.GetComponent<MeshRenderer>().sharedMaterial = m;
                msg.SetActive(false);
                Undo.RegisterCreatedObjectUndo(msg, "Crane");
            }
            mb.blessedMessage = msg;
            mb.joinLog = Object.FindObjectOfType<JoinLog>(true);
            if (mb.joinLog == null) log.AppendLine("注意: JoinLog が無い（点がもらえない）");
            // 折り鶴は 1 羽だけ
            int kept = 0;
            foreach (var pc in Object.FindObjectsOfType<PaperCrane>(true))
            {
                bool keep = pc.name == "PaperCrane_1";
                if (pc.gameObject.activeSelf != keep) { Undo.RecordObject(pc.gameObject, "Crane"); pc.gameObject.SetActive(keep); }
                if (keep) kept++;
            }
            log.AppendLine("折り鶴: " + kept + " 羽だけ出す");
            UdonSharpEditorUtility.CopyProxyToUdon(mb);
            var sc = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally { File.WriteAllText("Backup_flight/crane_report.txt", log.ToString()); }
    }
}
