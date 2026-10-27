using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// 調べるだけ: 指定した物の大きさ・中身を Backup_flight/objinfo.txt に書く
[InitializeOnLoad]
public static class ObjInfo
{
    const string FlagPath = "Backup_flight/run_objinfo.flag";
    static ObjInfo() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        string[] names = File.ReadAllLines(FlagPath);
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        foreach (var n0 in names)
        {
            string n = n0.Trim(); if (n == "") continue;
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name != n) continue;
                string path = t.name; for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                Bounds b = new Bounds(t.position, Vector3.zero); bool h = false; int rc = 0, tris = 0;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    rc++; if (!h) { b = r.bounds; h = true; } else b.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                }
                var terr = t.GetComponentsInChildren<Terrain>(true);
                foreach (var te in terr) { var tb = new Bounds(te.transform.position + te.terrainData.size * 0.5f, te.terrainData.size); if (!h) { b = tb; h = true; } else b.Encapsulate(tb); }
                sb.AppendLine("== " + path + (t.gameObject.activeInHierarchy ? "" : " [off]") + " pos " + t.position.ToString("F1") + " rot " + t.eulerAngles.ToString("F0") + " scale " + t.lossyScale.ToString("F2"));
                sb.AppendLine("   bounds center " + b.center.ToString("F1") + " size " + b.size.ToString("F1") + " renderers " + rc + " tris " + tris + " terrains " + terr.Length + " children " + t.childCount);
                int k = 0;
                foreach (Transform ch in t)
                {
                    if (k++ >= 25) { sb.AppendLine("   ..."); break; }
                    int cr = ch.GetComponentsInChildren<Renderer>(true).Length;
                    sb.AppendLine("   - " + ch.name + " pos " + ch.position.ToString("F0") + " renderers " + cr + (ch.GetComponent<Terrain>() != null ? " [Terrain]" : ""));
                }
            }
        }
        File.WriteAllText("Backup_flight/objinfo.txt", sb.ToString());
    }
}
