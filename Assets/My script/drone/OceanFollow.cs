using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 海のメッシュを、自分（ローカルプレイヤー）の頭の真下に動かす。
// 波の模様はワールド座標で計算しているので、メッシュが動いても波はずれない。
// 同期はしない（各プレイヤーが自分の真下に海を持つ）。
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class OceanFollow : UdonSharpBehaviour
{
    public float seaLevel = -27.7f;
    private VRCPlayerApi lp;
    private int timeId;

    void Start()
    {
        lp = Networking.LocalPlayer;
        timeId = VRCShader.PropertyToID("_Udon_OceanTime");
    }

    public override void PostLateUpdate()
    {
        // サーバー時刻を波の時刻にする（全員の画面で同じ波・機体の揺れとも一致）
        VRCShader.SetGlobalFloat(timeId, (float)(Networking.GetServerTimeInSeconds() % 3600.0) + 1f);
        if (lp == null) return;
        Vector3 h = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        transform.position = new Vector3(h.x, seaLevel, h.z);
    }
}
