using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: natsuki.vrm の中身（骨・表情・部品）と、歩き・待機のアニメ
[InitializeOnLoad]
public static class NatsukiInfo
{
    const string FlagPath = "Backup_flight/run_natsukiinfo.flag";
    static NatsukiInfo() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/My assets/natsuki.vrm"))
            sb.AppendLine("ASSET " + (o != null ? o.GetType().Name + " " + o.name : "null"));
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/My assets/natsuki.vrm");
        if (go != null)
        {
            var an = go.GetComponent<Animator>();
            sb.AppendLine("Animator " + (an != null) + " avatar " + (an != null && an.avatar != null ? an.avatar.name + " human " + an.avatar.isHuman : "-"));
            foreach (var c in go.GetComponentsInChildren<Component>(true)) if (!(c is Transform)) sb.AppendLine("COMP " + c.gameObject.name + " : " + c.GetType().FullName);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh; if (m == null) continue;
                sb.Append("MESH " + smr.name + " shapes " + m.blendShapeCount + ": ");
                for (int i = 0; i < m.blendShapeCount; i++) { string n = m.GetBlendShapeName(i); if (n.Contains("Joy") || n.Contains("Fun") || n.Contains("Smile") || n.Contains("Blink")) sb.Append(i + "=" + n + " "); }
                sb.AppendLine();
            }
            Bounds b = new Bounds(); bool h = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (!h) { b = r.bounds; h = true; } else b.Encapsulate(r.bounds); }
            sb.AppendLine("bounds " + b.size.ToString("F2"));
        }
        foreach (var p in new[] { "Assets/StarterAssets/ThirdPersonController/Character/Animations/Locomotion--Walk_N.anim.fbx", "Assets/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx" })
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p)) if (o is AnimationClip && !o.name.StartsWith("__")) sb.AppendLine("CLIP " + p + " :: " + o.name + " human " + ((AnimationClip)o).humanMotion + " len " + ((AnimationClip)o).length);
        File.WriteAllText("Backup_flight/natsukiinfo.txt", sb.ToString());
    }
}
