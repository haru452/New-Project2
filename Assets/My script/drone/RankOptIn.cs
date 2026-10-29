using UdonSharp;
using UnityEngine;

// 世界ランキングに「最高記録を送るかどうか」のスイッチ（押した人だけ切り替わる）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RankOptIn : UdonSharpBehaviour
{
    public WorldRanking ranking;
    public bool countrySwitch;   // true = 国名なしで参加する赤いスイッチ
    public override void Interact()
    {
        if (ranking == null) return;
        if (countrySwitch) ranking._ToggleCountry();
        else ranking._ToggleOptIn();
    }
}
