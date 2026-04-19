
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ajsutGravity : UdonSharpBehaviour
{
     public Vector3 localGravity;

    private Rigidbody rb;
    void Start()
    {
         rb = this.GetComponent<Rigidbody>();
        rb.useGravity = false;
    }
    void FixedUpdate () {
        setLocalGravity ();
    }

    void setLocalGravity(){
        rb.AddForce (localGravity, ForceMode.Acceleration);
    }
}
