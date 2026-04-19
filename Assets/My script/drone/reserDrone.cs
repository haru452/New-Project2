
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class reserDrone : UdonSharpBehaviour
{
    public GameObject car;
    public GameObject resetMaker;
  
    void Start()
    {
        
    }
    public override void Interact()
    {
        Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;        // 移動速度を0に
                rb.angularVelocity = Vector3.zero; // 回転速度を0に
            }
        car.transform.rotation = Quaternion.identity;
         car.transform.position = resetMaker.transform.position;
    }
}
