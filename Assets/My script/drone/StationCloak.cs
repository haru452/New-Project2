using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  宇宙ステーション（エレベーター一式）を、低い所・遠くからは雲で隠す
//  ・自分の頭が showAltitude より下 かつ エレベーターから showRadius 以上
//    離れているときは、エレベーターの描画を止めて、雲のかたまりを出す
//  ・雲より上に出るか、近づくと普通に見える
//  ・ただし hideDistance より遠いときは、高さに関係なく見えない
//  ・自分の画面だけの処理（同期なし）。当たり判定や動きはそのまま
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class StationCloak : UdonSharpBehaviour
{
    public Renderer[] stationRenderers;   // 隠す物
    public GameObject cloudBank;          // 隠している間に出す雲
    public Vector3 center;                // エレベーターの位置
    public float showAltitude = 430f;     // この高さより上なら見える
    public float showRadius = 350f;       // これより近ければ見える
    public float hideDistance = 2000f;    // これより遠ければ、高さに関係なく見えない
    public float interval = 0.25f;

    private bool hidden;
    private bool first = true;

    void Start()
    {
        SendCustomEventDelayedSeconds(nameof(_Tick), 0.1f);
    }

    public void _Tick()
    {
        SendCustomEventDelayedSeconds(nameof(_Tick), interval);
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;

        Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        float dx = head.x - center.x;
        float dz = head.z - center.z;
        float d2 = dx * dx + dz * dz;
        bool hide = (head.y < showAltitude && d2 > showRadius * showRadius)
                 || d2 > hideDistance * hideDistance;
        if (hide == hidden && !first) return;
        first = false;
        hidden = hide;

        if (stationRenderers != null)
        {
            for (int i = 0; i < stationRenderers.Length; i++)
            {
                if (stationRenderers[i] != null) stationRenderers[i].enabled = !hide;
            }
        }
        if (cloudBank != null) cloudBank.SetActive(hide);
    }
}
