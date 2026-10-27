using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  潜水艦の甲板カタパルト
//  ・このオブジェクトの位置 = 射出開始点（機体の中心が来る位置）
//  ・このオブジェクトの +Z（青矢印）= 射出方向
//  ・同じオブジェクトの Trigger コライダーに機体が入ると「接続」される
//  ・操縦席でスロットルを100%にして1秒待つと射出
//  実際の射出処理は機体側（controller）が行う
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class Catapult : UdonSharpBehaviour
{
    [Tooltip("射出終了時の速度 (m/s)。45 m/s = 162 km/h")]
    public float launchSpeed = 45f;
    [Tooltip("射出時の機首上げ角（度）。水平のままだと揚力が足りず沈む")]
    public float launchPitch = 8f;
    [Tooltip("離れる瞬間の上昇角（度）")]
    public float climbAngle = 3f;
    [Tooltip("レールの長さ (m)")]
    public float strokeLength = 25f;
    [Tooltip("スロットル100%を何秒続けたら射出するか")]
    public float holdTime = 1.0f;

    void OnTriggerEnter(Collider other)
    {
        controller c = FindController(other);
        if (c != null) c._SetCatapult(this);
    }

    // 一瞬検知を取り逃しても、範囲内にいる間は接続し直す
    void OnTriggerStay(Collider other)
    {
        controller c = FindController(other);
        if (c != null) c._SetCatapult(this);
    }

    void OnTriggerExit(Collider other)
    {
        controller c = FindController(other);
        if (c != null) c._ClearCatapult(this);
    }

    private controller FindController(Collider other)
    {
        if (other == null) return null;
        Rigidbody r = other.attachedRigidbody;
        if (r == null) return null;
        return r.GetComponentInChildren<controller>();
    }
}
