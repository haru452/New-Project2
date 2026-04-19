
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
        Networking.SetOwner(Networking.LocalPlayer, dronePool.gameObject);
        GameObject drone = dronePool.TryToSpawn();
        drone.transform.position =reportPoint.transform.position;
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
}
