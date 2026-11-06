using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UdonSharpEditor;
using TMPro;

// 空母を倒した後の部屋（「Tools/作戦範囲と広い海を設定する」から一緒に作る）
//  ・作戦範囲の中心の、海のずっと下（y = -3000）に、閉じた部屋を置く（外は見えない）
//  ・空母が沈んで 5 秒後、機体に乗っている人だけここへ移動する（CarrierVictory.cs）
//  ・部屋のボタンで main room（スポーン地点）へ戻れる（DoorTeleport.cs）
public static partial class FlightSetup
{
    const string VictoryScriptPath = "Assets/My script/drone/CarrierVictory.cs";
    const string VictoryAssetPath = "Assets/My script/drone/CarrierVictory.asset";
    const float RoomW = 14f, RoomH = 5f;

    static readonly string[] VictoryMsg =
    {
        "<size=150%><color=#FFD24D>MISSION COMPLETE</color></size>\nThe AMA flagship, the carrier Ulysses, has been sunk.\nWell done, pilot.\n<size=70%>- Gauria Imperial Command</size>",
        "<size=150%><color=#FFD24D>作戦成功</color></size>\nAMA連合艦隊の旗艦、空母ユリシーズを撃沈した。\nよくやった、パイロット。\n<size=70%>―ガウリア帝国司令部</size>",
        "<size=150%><color=#FFD24D>任务完成</color></size>\n已击沉AMA联合舰队旗舰——尤利西斯号航母。\n干得好，飞行员。\n<size=70%>——高里亚帝国司令部</size>",
    };
    static readonly string[] VictoryBtn = { "Return to main room", "main room へ戻る", "返回 main room" };

    static void BuildVictoryRoom(Vector3 areaCenter, StringBuilder log)
    {
        var old = GameObject.Find("VictoryRoom");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject("VictoryRoom");
        Undo.RegisterCreatedObjectUndo(root, "Area setup");
        root.transform.position = new Vector3(areaCenter.x, -3000f, areaCenter.z);

        var floorMat = SolidMat("VictoryFloor", new Color(0.12f, 0.13f, 0.16f));
        var wallMat = SolidMat("VictoryWall", new Color(0.22f, 0.25f, 0.30f));
        float h = RoomW * 0.5f;
        Box(root.transform, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(RoomW, 0.2f, RoomW), floorMat);
        Box(root.transform, "Ceiling", new Vector3(0f, RoomH + 0.1f, 0f), new Vector3(RoomW, 0.2f, RoomW), wallMat);
        Box(root.transform, "WallN", new Vector3(0f, RoomH * 0.5f, h + 0.1f), new Vector3(RoomW, RoomH, 0.2f), wallMat);
        Box(root.transform, "WallS", new Vector3(0f, RoomH * 0.5f, -h - 0.1f), new Vector3(RoomW, RoomH, 0.2f), wallMat);
        Box(root.transform, "WallE", new Vector3(h + 0.1f, RoomH * 0.5f, 0f), new Vector3(0.2f, RoomH, RoomW), wallMat);
        Box(root.transform, "WallW", new Vector3(-h - 0.1f, RoomH * 0.5f, 0f), new Vector3(0.2f, RoomH, RoomW), wallMat);

        var lg = new GameObject("Light");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, RoomH - 0.5f, 0f);
        var li = lg.AddComponent<Light>();
        li.type = LightType.Point; li.range = 16f; li.intensity = 1.2f; li.shadows = LightShadows.None;

        // 移動先（部屋の真ん中・北の壁の文字の方を向く）
        var pt = new GameObject("ArrivePoint");
        pt.transform.SetParent(root.transform, false);
        pt.transform.localPosition = new Vector3(0f, 0.05f, -2f);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
        if (font != null)
        {
            var chars = new StringBuilder();
            foreach (var a in new[] { VictoryMsg, VictoryBtn }) foreach (var t in a) chars.Append(t);
            font.TryAddCharacters(chars.ToString());
            foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
            EditorUtility.SetDirty(font);
        }
        var msg = WallText(root.transform, "Message", new Vector3(0f, 2.6f, h - 0.02f), new Vector2(11f, 3.2f), 1.1f, font);
        msg.text = VictoryMsg[0];

        // main room へ戻るボタン（南の壁の前）
        var btn = Box(root.transform, "ReturnButton", new Vector3(0f, 1.1f, -h + 0.6f), new Vector3(1.6f, 0.5f, 0.15f), SolidMat("VictoryButton", new Color(0.2f, 0.55f, 0.9f)));
        var bl = WallText(btn.transform.parent, "ReturnLabel", new Vector3(0f, 1.6f, -h + 0.6f), new Vector2(4f, 0.6f), 0.45f, font);
        bl.text = VictoryBtn[0] + "\n<size=70%>" + VictoryBtn[1] + " / " + VictoryBtn[2] + "</size>";
        bl.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 部屋の中（北）から見て読めるように
        var dt = UdonSharpUndo.AddComponent<DoorTeleport>(btn);
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        if (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null) dt.target = desc.spawns[0];
        else log.AppendLine("WARNING: スポーン地点が無いので、戻るボタンの行き先が空");
        UdonSharpEditorUtility.CopyProxyToUdon(dt);

        // 空母が沈んだのを見張る
        var cv = UdonSharpUndo.AddComponent<CarrierVictory>(root);
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
            if (w.noRelocate && w.roaming && w.target != null) { cv.carrier = w.target; break; }
        if (cv.carrier == null) log.AppendLine("WARNING: 空母の FlightTarget が見つからない");
        cv.planes = Object.FindObjectsOfType<controller>(true);
        cv.gunners = Object.FindObjectsOfType<GunnerStation>(true);
        cv.roomPoint = pt.transform;
        cv.roomText = msg; cv.roomMsg = VictoryMsg;
        cv.subtitles = Object.FindObjectOfType<Subtitles>(true);
        UdonSharpEditorUtility.CopyProxyToUdon(cv);
        log.AppendLine("勝利の部屋: " + root.transform.position.ToString("F0") + "（機体 " + cv.planes.Length + "・ガンナー席 " + cv.gunners.Length + "）");
    }

    static Material SolidMat(string name, Color c)
    {
        string p = FxDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("Standard"));
        m.color = c;
        if (create) AssetDatabase.CreateAsset(m, p); else EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Box(Transform parent, string name, Vector3 lp, Vector3 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    // 壁に貼る文字（-Z の側から +Z を向いて読む = 部屋の真ん中から北を見て読める）
    static TextMeshPro WallText(Transform parent, string name, Vector3 lp, Vector2 box, float size, TMP_FontAsset font)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        var t = g.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.fontSize = size; t.color = Color.white; t.richText = true;
        t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = true;
        t.rectTransform.sizeDelta = box;
        return t;
    }
}
