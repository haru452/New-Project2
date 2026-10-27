using UdonSharp;
using UnityEngine;

// BGM のボタン: 触ると target の _ToggleBgm を呼ぶ（自分だけ）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BgmToggle : UdonSharpBehaviour
{
    public UdonSharpBehaviour target;
    public override void Interact()
    {
        if (target != null) target.SendCustomEvent("_ToggleBgm");
    }
}
