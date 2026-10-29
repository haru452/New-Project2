using UdonSharp;
using UnityEngine;

// 海図台の島の名前: 押すと、その島の説明を地図の上に出す（押した人の画面だけ）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class IslandInfo : UdonSharpBehaviour
{
    public ChartMapLive live;
    public int index;
    public override void Interact() { if (live != null) live._ShowInfo(index); }
}
