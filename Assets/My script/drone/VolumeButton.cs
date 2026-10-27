using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 音の大きさのパネルの「－」「＋」ボタン
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VolumeButton : UdonSharpBehaviour
{
    public VolumeSettings settings;
    public int cat;
    public int delta = 1;

    public override void Interact()
    {
        if (settings != null) settings._Step(cat, delta);
    }
}
