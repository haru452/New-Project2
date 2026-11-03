using UdonSharp;
using UnityEngine;

// 言語選択の看板のボタン（lang: 0 = English, 1 = 日本語, 2 = 中文）
public class LangButton : UdonSharpBehaviour
{
    public Subtitles subtitles;
    public int lang;

    public override void Interact()
    {
        if (subtitles != null) subtitles._SetLang(lang);
    }
}
