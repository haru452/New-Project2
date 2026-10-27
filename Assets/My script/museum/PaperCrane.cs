using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

// =============================================================
//  折り鶴（手に取れる。自分の画面だけで動く）
//  ・箱の中に入れると納めたことになり、少しして台の上に戻る
//  ・箱の外に置いたままにすると、しばらくして台の上に戻る
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PaperCrane : UdonSharpBehaviour
{
    public MemorialBox box;
    public float returnAfter = 8f;
    private Vector3 homePos;
    private Quaternion homeRot;
    private VRCPickup pickup;
    private Rigidbody rb;
    private float droppedAt = -1f;
    private float offeredAt = -1f;
    private Renderer[] rends;

    void Start()
    {
        homePos = transform.position;
        homeRot = transform.rotation;
        pickup = (VRCPickup)GetComponent(typeof(VRCPickup));
        rb = GetComponent<Rigidbody>();
        rends = GetComponentsInChildren<Renderer>();
    }

    public override void OnPickup() { droppedAt = -1f; offeredAt = -1f; }
    public override void OnDrop() { droppedAt = Time.time; }

    void Update()
    {
        if (pickup != null && pickup.IsHeld) return;
        if (offeredAt >= 0f)
        {
            // 通信は 5 秒に 1 回までなので、箱が「次をどうぞ」になってから台の上に戻る
            if (Time.time - offeredAt > 1.5f && (box == null || box._Ready())) GoHome();
            return;
        }
        if (droppedAt < 0f) return;
        if (box != null && box._Inside(transform.position))
        {
            box._Offer();
            offeredAt = Time.time;
            SetVisible(false);   // 箱の中に消える
            return;
        }
        if (Time.time - droppedAt > returnAfter) GoHome();
    }

    void GoHome()
    {
        offeredAt = -1f; droppedAt = -1f;
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        transform.SetPositionAndRotation(homePos, homeRot);
        SetVisible(true);
    }

    void SetVisible(bool v)
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++) if (rends[i] != null) rends[i].enabled = v;
    }
}
