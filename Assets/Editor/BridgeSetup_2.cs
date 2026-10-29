using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using TMPro;

// BridgeSetup.cs の続き（長いので分けたファイル。中身は同じクラス BridgeSetup）
public static partial class BridgeSetup
{
    static void Rearrange(GameObject mainRoom, bool first)
    {
        // 潜水艦へのハッチ（東の隔壁）: 古い「main to submarine」の行き先を使う
        // 潜水艦の中の「main room へ」の丸い扉（古い立方体の場所。2 回目からは手で動かした位置のまま）
        Transform backTarget = null;   // 艦橋に戻ってきた時に立つ場所
        foreach (var sm in Object.FindObjectsOfType<SubToMain>(true)) if (sm.target != null) { backTarget = sm.target; break; }
        Transform subCube = null;
        foreach (var sm in Object.FindObjectsOfType<SubToMain>(true)) { subCube = sm.transform; break; }
        Transform subArrive = null;
        if (subCube != null)
        {
            Transform par = subCube.parent;
            Transform oldH = par != null ? par.Find("HatchToMainRoom") : null;
            if (oldH == null) foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "HatchToMainRoom") { oldH = t; break; }
            Vector3 hp = subCube.position; Quaternion hr = Quaternion.Euler(0f, subCube.eulerAngles.y, 0f);
            if (oldH != null) { hp = oldH.position; hr = oldH.rotation; Undo.DestroyObjectImmediate(oldH.gameObject); }
            else hp.y -= subCube.lossyScale.y * 0.5f;   // 立方体の下の面 = 床
            var sh = MakeHatch("HatchToMainRoom", par, hp, hr, "MAIN ROOM", "main room（艦橋）へ / To the main room", backTarget);
            subArrive = sh.transform.Find("Arrive");
            if (subCube.gameObject.activeSelf) { Undo.RecordObject(subCube.gameObject, "Bridge"); subCube.gameObject.SetActive(false); }
            log.AppendLine("潜水艦の扉: 古い立方体（" + subCube.name + "）の場所 " + hp.ToString("F1") + " に置きました（立方体は非表示）");
        }
        else log.AppendLine("潜水艦の扉: SubToMain（古い立方体）が見つからない");
        // 艦橋の扉 → 潜水艦の扉の前へ
        Transform hatchTarget = subArrive;
        if (hatchTarget == null) foreach (var m in Object.FindObjectsOfType<MainToSUb>(true)) if (m.target != null) { hatchTarget = m.target; break; }
        var hatch = BuildHatch(hatchTarget);

        if (!first) { log.AppendLine("並べ直し: 2 回目なので、ランキング・ドアなどは今の位置のまま"); return; }

        // ランキング（モード切替・音量を含む）: 南の壁の前。根元の前（forward）は壁の方
        var rk = mainRoom.transform.Find("Rankings");
        if (rk == null) { var r2 = Root("Rankings"); if (r2 != null) rk = r2.transform; }
        if (rk != null)
        {
            Undo.RecordObject(rk, "Bridge");
            rk.position = new Vector3(B.position.x + 0.6f, rk.position.y, B.position.z - 4.15f);
            rk.rotation = Quaternion.Euler(0f, 180f, 0f);
            log.AppendLine("並べ直し: ランキング → " + rk.position.ToString("F1"));
        }
        else log.AppendLine("並べ直し: Rankings が見つからない");

        // 博物館のドア（西の隔壁）
        Place(mainRoom.transform.Find("MuseumDoor"), new Vector3(-L / 2f + 0.02f, 0f, 0f), 90f, "博物館のドア");

        // 鏡（北の壁の西側）とスイッチ
        var mirT = mainRoom.transform.Find("VRCMirror");
        if (mirT != null)
        {
            Undo.RecordObject(mirT, "Bridge");
            mirT.position = B.position + new Vector3(-9.6f, 1.45f, 4.86f);
            mirT.rotation = Quaternion.identity;
            Vector3 ps = mirT.parent != null ? mirT.parent.lossyScale : Vector3.one;
            mirT.localScale = new Vector3(2.8f / ps.x, 2.4f / ps.y, 1f / ps.z);
            log.AppendLine("並べ直し: 鏡（2.8m × 2.4m、北の壁）");
        }
        foreach (var mi in Object.FindObjectsOfType<mirror>(true))
        {
            var t = mi.transform;
            Undo.RecordObject(t, "Bridge");
            t.position = B.position + new Vector3(-7.75f, 1.2f, 4.7f);
            t.rotation = Quaternion.identity;
            Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(0.25f / ps.x, 0.25f / ps.y, 0.25f / ps.z);
            var lab = new GameObject("MirrorLabel");
            lab.transform.SetParent(B, false);
            lab.transform.localPosition = new Vector3(-7.75f, 1.55f, 4.75f);
            lab.transform.localRotation = Quaternion.identity;   // 南から北の壁を見て読める向き
            var tm = lab.AddComponent<TextMeshPro>();
            tm.text = "MIRROR"; tm.fontSize = 1.4f; tm.alignment = TextAlignmentOptions.Center; tm.color = new Color(1f, 0.85f, 0.5f);
            tm.rectTransform.sizeDelta = new Vector2(1f, 0.3f);
            log.AppendLine("並べ直し: 鏡のスイッチ");
        }

        // アバター台（北東）とポータル（南東）
        int k = 0;
        foreach (var ped in Object.FindObjectsOfType<VRC.SDK3.Components.VRCAvatarPedestal>(true))
        {
            Place(ped.transform, new Vector3(7.4f + k * 1.5f, 0f, 3.9f), 180f, "アバター台 " + ped.name);
            k++;
        }
        var portal = Root("VRCPortalMarker");
        if (portal != null) Place(portal.transform, new Vector3(10.2f, 0f, -3.9f), 0f, "ポータル");

        // 潜水艦から戻ってきた時の場所（ハッチの前）
        foreach (var s in Object.FindObjectsOfType<SubToMain>(true))
            if (s.target != null) Place(s.target, new Vector3(L / 2f - 2.0f, 0.05f, 0f), -90f, "潜水艦から戻る場所（" + s.target.name + "）");
    }

    // 丸いハッチ（東の隔壁）。触ると潜水艦へ
    static GameObject BuildHatch(Transform target)
    {
        return MakeHatch("HatchToSubmarine", B, B.TransformPoint(new Vector3(L / 2f - 0.05f, 0f, 0f)), Quaternion.LookRotation(Vector3.left),
            "TO HANGAR  /  I-402", "潜水艦（格納庫）へ / To the hangar", target);
    }

    // 丸い扉（触ると target へ）。forward = 扉の前（人が立つ側）。前 1.6m に「Arrive」（ここへ来た人が立つ場所）
    static GameObject MakeHatch(string name, Transform parent, Vector3 pos, Quaternion rot, string labelText, string interact, Transform target)
    {
        var h = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(h.gameObject, "Bridge");
        h.SetParent(parent, true);
        h.SetPositionAndRotation(pos, rot);
        Vector3 pls = parent != null ? parent.lossyScale : Vector3.one;
        h.localScale = new Vector3(1f / pls.x, 1f / pls.y, 1f / pls.z);
        var arr = new GameObject("Arrive").transform;
        arr.SetParent(h, false);
        arr.localPosition = new Vector3(0f, 0.05f, 1.6f);
        Material steel = M("HatchSteel", new Color(0.4f, 0.45f, 0.42f), 0.6f, 0.45f);
        Material dark = M("ConsoleDark", new Color(0.09f, 0.1f, 0.11f), 0.5f, 0.45f);
        Material valve = M("ValveRed", new Color(0.62f, 0.08f, 0.06f), 0.4f, 0.5f);
        // 楕円の枠（縦 2.0m、横 1.2m）
        int seg = 20;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg, a2 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 p0 = new Vector3(Mathf.Cos(a) * 0.68f, 1.15f + Mathf.Sin(a) * 1.05f, 0.06f);
            Vector3 p1 = new Vector3(Mathf.Cos(a2) * 0.68f, 1.15f + Mathf.Sin(a2) * 1.05f, 0.06f);
            Pipe("Frame", h, p0, p1, 0.07f, steel);
        }
        Prim(PrimitiveType.Cylinder, "Door", h, new Vector3(0f, 1.15f, 0.06f), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.24f, 0.04f, 2.0f), dark, false);
        Wheel("HatchWheel", h, new Vector3(0f, 1.15f, 0.16f), Quaternion.identity, 0.32f, valve);
        Box("Hinge", h, new Vector3(-0.66f, 1.6f, 0.12f), new Vector3(0.1f, 0.25f, 0.12f), steel);
        Box("Hinge", h, new Vector3(-0.66f, 0.7f, 0.12f), new Vector3(0.1f, 0.25f, 0.12f), steel);
        var lab = new GameObject("Label");
        lab.transform.SetParent(h, false);
        lab.transform.localPosition = new Vector3(0f, 2.55f, 0.1f);
        lab.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = lab.AddComponent<TextMeshPro>();
        tm.text = labelText; tm.fontSize = 2.2f; tm.alignment = TextAlignmentOptions.Center; tm.color = new Color(1f, 0.85f, 0.5f);
        tm.rectTransform.sizeDelta = new Vector2(3f, 0.5f);
        var bc = h.gameObject.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 1.15f, 0.15f);
        bc.size = new Vector3(1.4f, 2.2f, 0.3f);
        var dt = UdonSharpUndo.AddComponent<DoorTeleport>(h.gameObject);
        dt.target = target;
        UdonSharpEditorUtility.CopyProxyToUdon(dt);
        var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(dt);
        if (ub != null) { ub.interactText = interact; ub.proximity = 3f; }
        log.AppendLine("扉 " + name + "（行き先 " + (target != null ? target.name + " " + target.position.ToString("F1") : "無し！") + "）");
        return h.gameObject;
    }
}
