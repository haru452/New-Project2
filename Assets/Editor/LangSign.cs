using UnityEditor;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using TMPro;

// 言語選択の看板の見た目（LangSetup から呼ぶ）。SF コックピット風の青いパネル + 言語ごとの色の光る枠
// 看板の中身だけを作る。字幕の仕組みや LangButton の動きは変えない
public static class LangSign
{
    const string Dir = "Assets/My script/drone/Subtitles";

    // フォントに足しておく文字（小さい英字の見出し用）
    public const string ExtraChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ ";

    static readonly Color Cyan = new Color(0.30f, 0.90f, 1.00f);

    // 透明度つきの光らない色（FlightUI/UnlitTint）。alpha=false なら不透明
    static Material M(string name, Color c, bool alpha)
    {
        string p = Dir + "/Font/Sign_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        bool create = m == null;
        var sh = Shader.Find("FlightUI/UnlitTint");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (create) m = new Material(sh);
        m.shader = sh;
        m.color = c;
        if (m.HasProperty("_SrcBlend"))
        {
            m.SetFloat("_SrcBlend", alpha ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1f);
            m.SetFloat("_DstBlend", alpha ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
            m.SetFloat("_ZWrite", alpha ? 0f : 1f);
        }
        m.renderQueue = alpha ? 3000 : 2000;
        if (create) AssetDatabase.CreateAsset(m, p); else EditorUtility.SetDirty(m);
        return m;
    }

    // 看板の前面（-Z 側）を向いた板。z が小さいほど手前
    static GameObject Q(Transform p, string name, float x, float y, float z, float w, float h, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name; Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(p, false);
        q.transform.localPosition = new Vector3(x, y, z); q.transform.localScale = new Vector3(w, h, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
        return q;
    }

    // 四角い枠（太さ t）
    static void Frame(Transform p, string name, float cx, float cy, float z, float w, float h, float t, Material m)
    {
        Q(p, name + "_T", cx, cy + h / 2f - t / 2f, z, w, t, m);
        Q(p, name + "_B", cx, cy - h / 2f + t / 2f, z, w, t, m);
        Q(p, name + "_L", cx - w / 2f + t / 2f, cy, z, t, h, m);
        Q(p, name + "_R", cx + w / 2f - t / 2f, cy, z, t, h, m);
    }

    // 四隅の L 字（明るい色）
    static void Corners(Transform p, string name, float cx, float cy, float z, float w, float h, float len, float t, Material m)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float px = cx + sx * w / 2f, py = cy + sy * h / 2f;
                Q(p, name + "_h", px - sx * len / 2f, py - sy * t / 2f, z, len, t, m);
                Q(p, name + "_v", px - sx * t / 2f, py - sy * len / 2f, z, t, len, m);
            }
    }

    static TextMeshPro Txt(Transform p, string name, float x, float y, float z, string s, float size, Vector2 box, TMP_FontAsset font, Color col, float spacing, bool bold)
    {
        var g = new GameObject(name);
        g.transform.SetParent(p, false); g.transform.localPosition = new Vector3(x, y, z);
        var t = g.AddComponent<TextMeshPro>();
        t.font = font; t.text = s; t.fontSize = size; t.color = col; t.richText = true;
        t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = true;
        t.characterSpacing = spacing;
        if (bold) t.fontStyle = FontStyles.Bold;
        t.rectTransform.sizeDelta = box;
        return t;
    }

    public static void Build(GameObject sign, TMP_FontAsset font, Subtitles sub, string title, string notice)
    {
        var tf = sign.transform;
        var navy = M("Navy", new Color(0.02f, 0.04f, 0.09f), false);
        var panel = M("Panel", new Color(0.05f, 0.09f, 0.17f), false);
        var cyanDim = M("CyanDim", new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f), true);
        var cyan = M("Cyan", Cyan, false);
        var strip = M("Strip", new Color(0.02f, 0.03f, 0.07f, 0.85f), true);

        // 背景 → 二重の枠 → 四隅
        Q(tf, "Back", 0f, 0.55f, 0.03f, 3.6f, 1.9f, navy);
        Frame(tf, "FrameOuter", 0f, 0.55f, 0.02f, 3.56f, 1.86f, 0.012f, cyanDim);
        Frame(tf, "FrameInner", 0f, 0.55f, 0.02f, 3.42f, 1.72f, 0.005f, cyanDim);
        Corners(tf, "Corner", 0f, 0.55f, 0.015f, 3.56f, 1.86f, 0.22f, 0.04f, cyan);

        // 見出し
        Txt(tf, "Caption", 0f, 1.37f, 0f, "SELECT YOUR LANGUAGE", 0.9f, new Vector2(3.0f, 0.2f), font, new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f), 28f, false);
        Txt(tf, "Title", 0f, 1.13f, 0f, title, 2.4f, new Vector2(3.4f, 0.4f), font, Color.white, 4f, true);
        Q(tf, "Divider", 0f, 0.91f, 0.01f, 3.0f, 0.008f, cyanDim);
        Q(tf, "DividerMark", 0f, 0.91f, 0.005f, 0.5f, 0.016f, cyan);

        // 言語ボタン
        string[] names = { "English", "日本語", "中文" };
        string[] codes = { "EN", "JA", "ZH" };
        float[] xs = { -1.15f, 0f, 1.15f };
        var cols = new[] { new Color(0.25f, 0.60f, 1.00f), new Color(1.00f, 0.32f, 0.38f), new Color(1.00f, 0.80f, 0.20f) };
        const float by = 0.50f;
        for (int i = 0; i < 3; i++)
        {
            Color c = cols[i];
            // 後ろのにじむ光 → 枠 → ボタン本体（当たり判定つき） → 上の太い線 → 文字
            Q(tf, "Glow_" + names[i], xs[i], by, 0.025f, 1.12f, 0.62f, M("Glow" + i, new Color(c.r, c.g, c.b, 0.22f), true));
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "Button_" + names[i]; b.transform.SetParent(tf, false);
            b.transform.localPosition = new Vector3(xs[i], by, 0f); b.transform.localScale = new Vector3(1.0f, 0.5f, 0.1f);
            b.GetComponent<MeshRenderer>().sharedMaterial = panel;
            Frame(tf, "Edge_" + names[i], xs[i], by, -0.052f, 1.0f, 0.5f, 0.01f, M("Edge" + i, new Color(c.r, c.g, c.b, 0.9f), true));
            Q(tf, "Bar_" + names[i], xs[i], by + 0.25f - 0.02f, -0.053f, 1.0f, 0.04f, M("Bar" + i, c, false));
            Txt(tf, "Label_" + names[i], xs[i], by + 0.05f, -0.06f, names[i], 3.0f, new Vector2(1.0f, 0.3f), font, Color.white, 2f, true);
            Txt(tf, "Code_" + names[i], xs[i], by - 0.16f, -0.06f, codes[i], 0.9f, new Vector2(1.0f, 0.15f), font, c, 22f, true);

            var lb = UdonSharpUndo.AddComponent<LangButton>(b);
            lb.subtitles = sub; lb.lang = i;
            UdonSharpEditorUtility.CopyProxyToUdon(lb);
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(lb);
            if (ub != null) { ub.interactText = names[i]; ub.proximity = 6f; }
        }

        // 下の注意書き
        Q(tf, "NoticeStrip", 0f, -0.14f, 0.012f, 3.3f, 0.26f, strip);
        Txt(tf, "Notice", 0f, -0.14f, 0f, notice, 0.9f, new Vector2(3.2f, 0.24f), font, new Color(1f, 0.85f, 0.4f), 0f, false);
        Q(tf, "BottomLine", 0f, -0.32f, 0.01f, 3.0f, 0.006f, cyanDim);
    }
}
