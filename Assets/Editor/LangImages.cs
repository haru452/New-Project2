using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using UdonSharp;
using UdonSharpEditor;

// main room の説明の画像を、言語選択（English / 日本語 / 中文）に合わせて替えるようにする
//  ・画像は Tools/make_lang_images.py で作る（Assets/My script/bridge の Tex と IslandInfo に _en / _ja / _zh）
//  ・対象: R35 の操作方法（PC・VR）、海図台の札、海図台の島の説明、世界ランキングの注意書き、モード選択ボタンの文字（GameMode）
//  ・メニュー「Tools/main room の説明を言語に合わせる」。1 回目はプログラムを作るだけのことがある（もう一度実行）
//  ・2 回目以降は、すでにあるものを作り直さずに、絵の入れ替えだけを更新する
public static class LangImages
{
    const string Dir = "Assets/My script/bridge";
    const string Scr = "Assets/My script/drone/";
    static readonly string[] Lc = { "en", "ja", "zh" };   // Subtitles の lang の順
    static readonly string[] InfoKeys = { "Aigis_island", "Astral_island", "Military_base" };
    static StringBuilder log;
    // 世界ランキング（見出し・スイッチ）の文字に使う日本語・中文の字（フォントに足す）
    const string RankChars = "世界ランキング最高記録を送る国名隠すオンフ自分得点に送ります押すと表示されますでが参加しませんすべてのインスタンス準備中読み込めませんでしたまだ記録がありません点隐藏我的国家开关不显示参加排行榜发送最高分把到按下即可你会上所有房间敬请期待加载失败暂无记录";
    // モードの文字に使う日本語・中文の字（フォントに足す）
    const string ModeChars = "普通ハード遊覧モード対空砲はありません。押すと切り替え得点船が撃ってきます敵景色を楽しもう変更できいま誰か飛んでいます全員着陸するか機体降りるまで待ってださロック中困难模式舰会用防炮射击得分×按下换为游览没有无法现在人正在请等所有降落或离开后再试已锁定尽情欣赏风景敌不开火";

    static bool EnsureProg(string name)
    {
        string asset = Scr + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) return true;
        if (AssetDatabase.LoadAssetAtPath<Object>(asset) != null) AssetDatabase.DeleteAsset(asset);
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(Scr + name + ".cs");
        if (ms == null) return true;
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, asset);
        AssetDatabase.SaveAssets();
        return false;
    }

    // 画像から、言語ごとのマテリアルを作る（from の見た目の設定を引き継ぐ）
    static Material[] MakeMats(string baseName, string folder, Material from)
    {
        var res = new Material[3];
        for (int i = 0; i < 3; i++)
        {
            string png = Dir + "/" + folder + "/" + baseName + "_" + Lc[i] + ".png";
            AssetDatabase.ImportAsset(png);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
            if (tex == null) { log.AppendLine("画像が無い: " + png + "（Tools/make_lang_images.py を実行してください）"); return null; }
            string mp = Dir + "/Materials/Lang_" + baseName + "_" + Lc[i] + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null)
            {
                m = from != null ? new Material(from) : new Material(Shader.Find("Unlit/Texture"));
                AssetDatabase.CreateAsset(m, mp);
            }
            m.mainTexture = tex;
            EditorUtility.SetDirty(m);
            res[i] = m;
        }
        return res;
    }

    static LangSwap Swap(GameObject g, Subtitles sub)
    {
        var ls = g.GetComponent<LangSwap>();
        if (ls == null) ls = UdonSharpUndo.AddComponent<LangSwap>(g);
        ls.subtitles = sub;
        return ls;
    }

    static GameObject FindObj(string name, string under)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name != name) continue;
            bool hist = false;   // 歴史の間（main room の艦橋のコピー）の中の物は使わない
            for (var h = t; h != null; h = h.parent) if (h.name == "HistoryRoom") hist = true;
            if (hist) continue;
            if (under == null) return t.gameObject;
            for (var p = t.parent; p != null; p = p.parent) if (p.name == under) return t.gameObject;
        }
        return null;
    }

    [MenuItem("Tools/main room の説明を言語に合わせる")]
    public static void Run()
    {
        log = new StringBuilder();
        if (!EnsureProg("LangSwap"))
        {
            EditorUtility.DisplayDialog("言語に合わせる", "LangSwap のプログラムを作りました。もう一度、同じメニューを実行してください。", "OK");
            return;
        }
        var sub = Object.FindObjectOfType<Subtitles>(true);
        if (sub == null) { EditorUtility.DisplayDialog("言語に合わせる", "Subtitles が見つかりません。先に「Tools/言語選択と字幕を設定する」を実行してください。", "OK"); return; }
        Directory.CreateDirectory(Dir + "/Materials");

        // 1) R35 の操作方法（PC・VR）
        foreach (string n in new[] { "controls_pc", "controls_vr" })
        {
            var g = FindObj(n, "ControlBoards");
            if (g == null) { log.AppendLine("操作方法の板が見つからない: " + n); continue; }
            var r = g.GetComponent<Renderer>();
            var mats = MakeMats(n, "Tex", r.sharedMaterial);
            if (mats == null) continue;
            var ls = Swap(g, sub);
            ls.target = r; ls.mats = mats;
            r.sharedMaterial = mats[0];
            UdonSharpEditorUtility.CopyProxyToUdon(ls);
            log.AppendLine("操作方法: " + n + "（English / 日本語 / 中文）");
        }

        // 2) 海図台の札（手前と奥の 2 枚）
        foreach (string n in new[] { "SignFront", "SignBack" })
        {
            var g = FindObj(n, "ChartTable");
            if (g == null) { log.AppendLine("札が見つからない: " + n); continue; }
            var r = g.GetComponent<Renderer>();
            var mats = MakeMats("chart_sign", "IslandInfo", r.sharedMaterial);
            if (mats == null) continue;
            var ls = Swap(g, sub);
            ls.target = r; ls.mats = mats;
            r.sharedMaterial = mats[0];
            UdonSharpEditorUtility.CopyProxyToUdon(ls);
            log.AppendLine("海図台の札: " + n);
        }

        // 3) 海図台の島の説明（名前を押すと出る）
        var live = Object.FindObjectOfType<ChartMapLive>(true);
        if (live != null && live.infoMats != null)
        {
            int n = live.infoMats.Length;
            var en = new Material[n]; var ja = new Material[n]; var zh = new Material[n];
            var cache = new Dictionary<string, Material[]>();
            int done = 0;
            for (int i = 0; i < n; i++)
            {
                var cur = live.infoMats[i];
                if (cur == null) continue;
                string key = null;
                foreach (var k in InfoKeys) if (cur.name.Contains(k)) key = k;
                if (key == null) { log.AppendLine("島の説明: 知らない画像 " + cur.name + "（そのまま）"); en[i] = cur; continue; }
                if (!cache.ContainsKey(key)) cache[key] = MakeMats("info_" + key, "IslandInfo", cur);
                var m3 = cache[key];
                if (m3 == null) { en[i] = cur; continue; }
                en[i] = m3[0]; ja[i] = m3[1]; zh[i] = m3[2];
                done++;
            }
            live.infoMats = en; live.infoMatsJa = ja; live.infoMatsZh = zh; live.subtitles = sub;
            UdonSharpEditorUtility.CopyProxyToUdon(live);
            log.AppendLine("島の説明: " + done + " 枚（English / 日本語 / 中文）");
        }
        else log.AppendLine("海図台（ChartMapLive）が見つからない");

        // 4) 世界ランキングの注意書き（3 枚が並んでいる → 選んだ言語の 1 枚だけ出す）
        var nEn = FindObj("Notice_en", "RankOptIn"); var nJa = FindObj("Notice_ja", "RankOptIn"); var nZh = FindObj("Notice_zh", "RankOptIn");
        if (nEn != null && nJa != null && nZh != null)
        {
            var ls = Swap(nEn.transform.parent.gameObject, sub);
            ls.objs = new[] { nEn, nJa, nZh };
            UdonSharpEditorUtility.CopyProxyToUdon(ls);
            log.AppendLine("世界ランキングの注意書き: 選んだ言語の 1 枚だけ（真ん中の位置に出す）");
        }
        else log.AppendLine("世界ランキングの注意書きが見つからない");

        // 5) モード選択ボタンの文字（NORMAL / HARD / SIGHTSEEING）を、選んだ言語で出す（日本語・中文が出せるフォントにする）
        var gm = Object.FindObjectOfType<GameMode>(true);
        if (gm != null)
        {
            gm.subtitles = sub;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
            if (font != null && gm.label != null)
            {
                font.TryAddCharacters(ModeChars);
                gm.label.font = font;
                EditorUtility.SetDirty(gm.label);
                foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
                EditorUtility.SetDirty(font); EditorUtility.SetDirty(font.material);
                log.AppendLine("モード選択ボタンの文字: English / 日本語 / 中文");
            }
            else log.AppendLine("字幕のフォントか、モードの文字（label）が見つからない");
            UdonSharpEditorUtility.CopyProxyToUdon(gm);
        }
        else log.AppendLine("GameMode が見つからない");

        // 6) 世界ランキングの見出し・「SEND MY BEST」のスイッチの文字を、選んだ言語で出す
        var wr = Object.FindObjectOfType<WorldRanking>(true);
        if (wr != null)
        {
            wr.subtitles = sub;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
            if (font != null)
            {
                font.TryAddCharacters(RankChars);
                foreach (var lb in new[] { wr.text, wr.switchLabel })
                {
                    if (lb == null) continue;
                    lb.font = font;
                    EditorUtility.SetDirty(lb);
                }
                foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
                EditorUtility.SetDirty(font); EditorUtility.SetDirty(font.material);
                log.AppendLine("世界ランキング・SEND MY BEST: English / 日本語 / 中文");
            }
            else log.AppendLine("字幕のフォントが見つからない（世界ランキング）");
            UdonSharpEditorUtility.CopyProxyToUdon(wr);
        }
        else log.AppendLine("WorldRanking が見つからない");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        string msg = log.ToString();
        Debug.Log("[LangImages]\n" + msg);
        File.WriteAllText("Backup_flight/lang_images_log.txt", msg);
        EditorUtility.DisplayDialog("言語に合わせる", msg + "\nシーンを保存してください。", "OK");
    }
}
