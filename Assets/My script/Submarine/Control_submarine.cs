
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Control_submarine : UdonSharpBehaviour
{
    public GameObject submarine;
    public Vector3 Offset;
    public int speed;
    void Start()
    {
        submarine.transform.position = Offset;

        
    }
    void Update()
    {
        submarine.transform.Translate(speed * Time.deltaTime, 0, 0);
        if(submarine.transform.position.x <= -2208.0f)
        {
            submarine.transform.position = Offset;
        }
    }
}
