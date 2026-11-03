using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: R35（Omega）の操縦席の中の広さ（目から各方向に、機体の内側の壁までの距離）
[InitializeOnLoad]
public static class TubProbe
{
    const string Flag = "Backup_flight/tub_probe.flag";
    static TubProbe() { EditorApplication.delayCall += Run; }
    static void Run()
    {
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        controller c = null;
        foreach (var x in Object.FindObjectsOfType<controller>(true))
            if (x.car != null && x.car.name.StartsWith("Omega") && x.car.activeInHierarchy && x.cockpitRoot != null) { c = x; break; }
        if (c == null) { File.WriteAllText("Backup_flight/tub_probe.txt", "動いている Omega が無い"); return; }
        Transform root = c.cockpitRoot.transform;
        Transform body = c.car.transform.Find("OmegaBody");
        var added = new System.Collections.Generic.List<MeshCollider>();
        foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true))
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh; added.Add(mc);
        }
        Physics.SyncTransforms();
        sb.AppendLine("car " + c.car.name + "  目（Cockpit）carLocal " + c.car.transform.InverseTransformPoint(root.position).ToString("F3"));
        sb.AppendLine("下の角度 / 左右の角度 → 当たった所（目から見た位置 x,y,z）");
        for (int pitch = -80; pitch <= 20; pitch += 10)
        {
            sb.Append("pitch " + pitch + ":");
            for (int yaw = -75; yaw <= 75; yaw += 15)
            {
                Vector3 dirL = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                Vector3 dir = root.TransformDirection(dirL);
                RaycastHit h;
                string v = "-";
                foreach (var mc in added) { }
                if (Physics.Raycast(root.position, dir, out h, 5f))
                {
                    if (h.collider is MeshCollider && added.Contains((MeshCollider)h.collider))
                    { Vector3 lp = root.InverseTransformPoint(h.point); v = "(" + lp.x.ToString("F2") + "," + lp.y.ToString("F2") + "," + lp.z.ToString("F2") + ")"; }
                    else v = "x:" + h.collider.name;
                }
                sb.Append(" y" + yaw + "=" + v);
            }
            sb.AppendLine();
        }
        foreach (var mc in added) Object.DestroyImmediate(mc);
        File.WriteAllText("Backup_flight/tub_probe.txt", sb.ToString());
        Debug.Log("[TubProbe] 書きました");
    }
}
