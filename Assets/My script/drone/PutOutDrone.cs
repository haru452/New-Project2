
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

public class PutOutDrone : UdonSharpBehaviour
{
    //public GameObject drone;
    public VRCObjectPool dronePool;
    public GameObject reportPoint;
    private int deadline =0;
     public float lifeTime = 5.0f;
   public int NumberOFDrone = 10;
    void Start()
    {
        
    }
    public override void Interact()
    {
        /*if (drone != null)
        {
            drone.SetActive(false);
            
        }*/
        if(dronePool == null)
        {
           ReturnToPool();

        }
        if (deadline >= NumberOFDrone)
        {
           ResetAllDrones();
            deadline = 0;
        }
        else{
        Networking.SetOwner(Networking.LocalPlayer, dronePool.gameObject);
        GameObject drone = dronePool.TryToSpawn();
        Rigidbody rb = drone.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;        // 移動速度を0に
                rb.angularVelocity = Vector3.zero; // 回転速度を0に
            }
        drone.transform.position =reportPoint.transform.position;
        deadline++;
        }
    }
    public void ReturnToPool()
    {
        if (dronePool != null)
        {
            dronePool.Return(this.gameObject);
        }
        else
        {
            // プールが設定されていない場合は単純に非表示にする
            this.gameObject.SetActive(false);
        }
    }
    public void ResetAllDrones()
    {
        // プールの「Pool」配列の中身をループしてすべて戻す
        // 注意：VRCObjectPoolの仕様上、すべての要素をReturnに回すのが安全です
        foreach (GameObject obj in dronePool.Pool)
        {
            if (obj != null)
            {
                dronePool.Return(obj);
            }
        }
    }
    
}
