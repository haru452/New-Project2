using UdonSharp;
using UnityEngine;

// アチーブメントの板の ◀ ▶ ボタン（押した人だけ切り替わる）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AchievementButton : UdonSharpBehaviour
{
    public Achievements achievements;
    public int dir = 1;   // 1 = ▶（次）, -1 = ◀（前）
    public override void Interact()
    {
        if (achievements != null) achievements._Step(dir);
    }
}
