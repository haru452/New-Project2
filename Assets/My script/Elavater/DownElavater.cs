
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DownElavater : UdonSharpBehaviour
{
    public GameObject elavater;
    public Transform target;
    public float speed = 100.0f;
    private bool condention;
    void Start()
    {
        condention = false;
    }
    public override void Interact()
    {
        condention = true;
    }
    void Update()
    {
        if (condention)
        {
            elavater.transform.Translate(0, -speed * Time.deltaTime, 0);
         if(elavater.transform.position.y <=64.0f){
            elavater.transform.position = target.position;
             condention = false;
        }
    }
    }
}
