using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// URP 用マテリアルを Built-in（VRChat で使えるシェーダー）に変換する
//  Lit / Simple Lit / Complex Lit / Baked Lit → Standard
//  Unlit → FlightUI/UnlitTint
//  Particles → Legacy Shaders/Particles/Additive または Alpha Blended
// メニュー「Tools/マテリアルをVRChat用(Built-in)に変換」から実行できる。
// Backup_flight/run_convert.flag があれば Unity の再読み込み時に一度だけ自動実行する。
// 元のマテリアルは Backup_flight/urp_materials にコピーしてある。
[InitializeOnLoad]
public static class URPToBuiltin
{
    const string FlagPath = "Backup_flight/run_convert.flag";
    const string ReportPath = "Backup_flight/convert_report.txt";

    static URPToBuiltin()
    {
        EditorApplication.delayCall += TryAutoRun;
        // 再生中だった場合は、再生を止めたときに実行する
        EditorApplication.playModeStateChanged += st =>
        {
            if (st == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAutoRun;
        };
    }

    static void TryAutoRun()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlaying) return;   // 再生中は何もしない（止めたら上のイベントで実行）
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoRun;
            return;
        }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/マテリアルをVRChat用(Built-in)に変換")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("URPToBuiltin " + System.DateTime.Now);
        try
        {
            Shader std = Shader.Find("Standard");
            Shader unlit = Shader.Find("FlightUI/UnlitTint");
            Shader pAdd = Shader.Find("Legacy Shaders/Particles/Additive");
            Shader pAlpha = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            log.AppendLine("Standard:" + (std != null) + " UnlitTint:" + (unlit != null) + " ParticlesAdd:" + (pAdd != null) + " ParticlesAlpha:" + (pAlpha != null));
            if (std == null || unlit == null || pAdd == null || pAlpha == null)
            {
                log.AppendLine("ERROR: 変換先のシェーダーが見つからないため中止");
                return;
            }

            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string sn = mat.shader.name;
                if (!sn.StartsWith("Universal Render Pipeline/")) continue;

                if (sn.Contains("Particles")) ConvertParticle(mat, pAdd, pAlpha);
                else if (sn == "Universal Render Pipeline/Unlit") ConvertUnlit(mat, unlit);
                else if (sn.Contains("Lit")) ConvertLit(mat, std);
                else { log.AppendLine("  SKIP(" + sn + "): " + path); continue; }

                EditorUtility.SetDirty(mat);
                log.AppendLine("  " + sn.Replace("Universal Render Pipeline/", "") + " → " + mat.shader.name + ": " + path);
                n++;
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("変換: " + n + " 個");
            log.AppendLine("DONE");
        }
        catch (System.Exception e)
        {
            log.AppendLine("EXCEPTION: " + e);
        }
        finally
        {
            Directory.CreateDirectory("Backup_flight");
            File.WriteAllText(ReportPath, log.ToString(), Encoding.UTF8);
            Debug.Log("[URPToBuiltin]\n" + log);
        }
    }

    // ---- 値の読み出し ----
    static Color C(Material m, string n, Color d) { return m.HasProperty(n) ? m.GetColor(n) : d; }
    static float F(Material m, string n, float d) { return m.HasProperty(n) ? m.GetFloat(n) : d; }
    static Texture T(Material m, string n) { return m.HasProperty(n) ? m.GetTexture(n) : null; }
    static Vector2 Sc(Material m, string n) { return m.HasProperty(n) ? m.GetTextureScale(n) : Vector2.one; }
    static Vector2 Of(Material m, string n) { return m.HasProperty(n) ? m.GetTextureOffset(n) : Vector2.zero; }

    static void ConvertLit(Material m, Shader std)
    {
        Color col = C(m, "_BaseColor", Color.white);
        Texture tex = T(m, "_BaseMap");
        Vector2 sc = Sc(m, "_BaseMap"), of = Of(m, "_BaseMap");
        Texture bump = T(m, "_BumpMap");
        float bumpScale = F(m, "_BumpScale", 1f);
        float metallic = F(m, "_Metallic", 0f);
        Texture mgm = T(m, "_MetallicGlossMap");
        float smooth = F(m, "_Smoothness", 0.5f);
        Texture occ = T(m, "_OcclusionMap");
        float occS = F(m, "_OcclusionStrength", 1f);
        Texture em = T(m, "_EmissionMap");
        Color emc = C(m, "_EmissionColor", Color.black);
        bool emOn = m.IsKeywordEnabled("_EMISSION") && (emc.maxColorComponent > 0.001f || em != null);
        float surface = F(m, "_Surface", 0f);
        float alphaClip = F(m, "_AlphaClip", 0f);
        float cutoff = F(m, "_Cutoff", 0.5f);
        float cull = F(m, "_Cull", 2f);

        m.shader = std;
        m.shaderKeywords = new string[0];
        m.SetColor("_Color", col);
        m.SetTexture("_MainTex", tex);
        m.SetTextureScale("_MainTex", sc);
        m.SetTextureOffset("_MainTex", of);
        if (bump != null) { m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", bumpScale); m.EnableKeyword("_NORMALMAP"); }
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_GlossMapScale", smooth);
        if (mgm != null) { m.SetTexture("_MetallicGlossMap", mgm); m.EnableKeyword("_METALLICGLOSSMAP"); }
        if (occ != null) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", occS); }
        if (emOn)
        {
            m.SetColor("_EmissionColor", emc);
            if (em != null) m.SetTexture("_EmissionMap", em);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        if (surface >= 0.5f)
        {
            // 透明（水など）
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        else if (alphaClip >= 0.5f)
        {
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Cutoff", cutoff);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            m.SetFloat("_Mode", 0f);
            m.SetOverrideTag("RenderType", "");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.renderQueue = -1;
        }
        if (cull < 1.5f) m.doubleSidedGI = true;
    }

    static void ConvertUnlit(Material m, Shader unlit)
    {
        Color col = C(m, "_BaseColor", Color.white);
        Texture tex = T(m, "_BaseMap");
        Vector2 sc = Sc(m, "_BaseMap"), of = Of(m, "_BaseMap");
        bool transparent = F(m, "_Surface", 0f) >= 0.5f;
        float blend = F(m, "_Blend", 0f);
        float cull = F(m, "_Cull", 2f);
        int queue = m.renderQueue;

        m.shader = unlit;
        m.shaderKeywords = new string[0];
        m.SetColor("_Color", col);
        m.SetTexture("_MainTex", tex);
        m.SetTextureScale("_MainTex", sc);
        m.SetTextureOffset("_MainTex", of);
        m.SetFloat("_Cull", cull);
        if (transparent)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", blend > 1.5f && blend < 2.5f ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = queue >= 2501 ? queue : (int)RenderQueue.Transparent;
        }
        else
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.renderQueue = queue > 0 && queue < 2501 ? queue : (int)RenderQueue.Geometry;
        }
    }

    static void ConvertParticle(Material m, Shader pAdd, Shader pAlpha)
    {
        Color col = C(m, "_BaseColor", Color.white);
        Texture tex = T(m, "_BaseMap");
        float blend = F(m, "_Blend", 0f);
        bool additive = blend > 1.5f && blend < 2.5f;

        m.shader = additive ? pAdd : pAlpha;
        m.shaderKeywords = new string[0];
        m.SetTexture("_MainTex", tex);
        m.SetColor("_TintColor", col * 0.5f);   // 0.5 が「色そのまま」
        m.renderQueue = (int)RenderQueue.Transparent;
    }
}
