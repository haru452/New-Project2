using UdonSharp;
using UnityEngine;

// 音声ガイドのボタン（0 = 日本語, 1 = 英語）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MuseumGuideButton : UdonSharpBehaviour
{
    public MuseumAudio museum;
    public int lang;

    public override void Interact()
    {
        if (museum != null) museum._Toggle(lang);
    }
}
