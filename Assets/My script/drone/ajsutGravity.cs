
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ajsutGravity : UdonSharpBehaviour
{
     private Vector3 localGravity;

    private Rigidbody rb;
    void Start()
    {
         rb = this.GetComponent<Rigidbody>();
        rb.useGravity = false;
    }
    void FixedUpdate () {
        setLocalGravity ();
    }

void Update()
    {
        if(this.transform.position.y >= 1200.0f)
        {
            localGravity = new Vector3(0, -0.25f, 0);
        }
        else if(this.transform.position.y <= 1200.0f&&this.transform.position.y>=1000.0f)
        {
            localGravity = new Vector3(0, -10.0f, 0);
        }
        else
        {
            localGravity = new Vector3(0, -1.0f, 0);
        }
    }
    void setLocalGravity(){
        rb.AddForce (localGravity, ForceMode.Acceleration);
    }
}
