using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

// 格納庫のボタン: プールから機体を 1 機出して、カタパルトの上に置く
//  ・カタパルトの上（blockRadius 以内）にもう機体がいる間は出さない
//  ・全部出ている時は何もしない（飛んでいる機体を勝手に戻さない）
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PutOutDrone : UdonSharpBehaviour
{
    [Tooltip("カタパルトの甲板の上にいる機体だけを数える範囲（水平 m）。上空や海で修理中の機体は数えない")]
    public float deckRadius = 14f;
    public float deckHeight = 8f;        // 甲板からこの高さまで（m）
    public float cooldown = 5f;          // 出してからこの秒数は、誰が押しても次を出さない（全員共通）
    [UdonSynced] private double lastSpawnTime = -100.0;
    public VRCObjectPool dronePool;
    public GameObject reportPoint;          // 出す場所（カタパルトが無い時に使う）
    public Transform catapultPoint;         // カタパルトの開始点（ここに機首をそろえて置く）
    public float blockRadius = 30f;         // この範囲に機体がいる間は次を出さない
    public float lifeTime = 5.0f;           // （未使用・互換のため）
    public int NumberOFDrone = 10;          // （未使用・互換のため）

    public override void Interact()
    {
        if (dronePool == null) return;
        Vector3 spawnPos = catapultPoint != null ? catapultPoint.position + Vector3.up * 0.5f
                         : (reportPoint != null ? reportPoint.transform.position : transform.position);

        // 1) 出したばかり（全員共通の時刻）なら出さない（2 回押しで 2 機出るのを防ぐ）
        if (Networking.GetServerTimeInSeconds() - lastSpawnTime < cooldown) { Debug.Log("[PutOutDrone] 出さない: 前に出してから " + cooldown + " 秒たっていない"); return; }

        // 2) カタパルトの甲板の上に機体がいたら出さない（距離: 水平 deckRadius・高さ deckHeight 以内）
        GameObject[] all = dronePool.Pool;
        for (int i = 0; i < all.Length; i++)
        {
            GameObject o = all[i];
            if (o == null || !o.activeSelf) continue;
            Vector3 d = o.transform.position - spawnPos;
            float h = d.y;
            d.y = 0f;
            if (d.magnitude < deckRadius && h > -3f && h < deckHeight) { Debug.Log("[PutOutDrone] 出さない: 甲板の上に " + o.name + " がいる（" + d.magnitude.ToString("F1") + "m）"); return; }
        }
        // 3) 当たり判定でも確認（機体の当たり判定がカタパルトの上に触れていたら出さない）
        Collider[] cols = Physics.OverlapSphere(spawnPos + Vector3.up * 2f, deckRadius * 0.7f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < cols.Length; i++)
        {
            Rigidbody arb = cols[i] != null ? cols[i].attachedRigidbody : null;
            if (arb == null) continue;
            for (int k = 0; k < all.Length; k++) if (all[k] != null && all[k] == arb.gameObject) { Debug.Log("[PutOutDrone] 出さない: 甲板の近くに " + arb.name + " の当たり判定"); return; }
        }

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        lastSpawnTime = Networking.GetServerTimeInSeconds();
        RequestSerialization();

        Networking.SetOwner(Networking.LocalPlayer, dronePool.gameObject);
        GameObject drone = dronePool.TryToSpawn();
        if (drone == null) { Debug.Log("[PutOutDrone] 出さない: プールの機体が全部出ている（" + all.Length + " 機）"); return; }
        Debug.Log("[PutOutDrone] " + drone.name + " を出しました");
        Networking.SetOwner(Networking.LocalPlayer, drone);

        // 機首をカタパルトの向きにそろえる（FlightAxis がある機体）
        Quaternion rot = drone.transform.rotation;
        if (catapultPoint != null)
        {
            Transform axis = drone.transform.Find("FlightAxis");
            Vector3 f = catapultPoint.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(f.normalized, Vector3.up);
                if (axis != null)
                {
                    Quaternion local = Quaternion.Inverse(drone.transform.rotation) * axis.rotation;
                    rot = look * Quaternion.Inverse(local);
                }
            }
        }
        // 脚（タイヤ）を出した状態で出てくるので、その分だけ上に
        controller dc = drone.GetComponentInChildren<controller>();
        if (dc != null) spawnPos += Vector3.up * dc._GearLift();
        drone.transform.SetPositionAndRotation(spawnPos, rot);
        Rigidbody rb = drone.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = spawnPos;
            rb.rotation = rot;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    public void ReturnToPool()
    {
        if (dronePool != null) dronePool.Return(this.gameObject);
        else this.gameObject.SetActive(false);
    }

    public void ResetAllDrones()
    {
        foreach (GameObject obj in dronePool.Pool)
        {
            if (obj != null) dronePool.Return(obj);
        }
    }
}
