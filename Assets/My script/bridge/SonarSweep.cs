using UdonSharp;
using UnityEngine;

// ソナー画面の回る線（見た目だけ）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SonarSweep : UdonSharpBehaviour
{
    public float degPerSec = 60f;
    void Update() { transform.Rotate(0f, 0f, -degPerSec * Time.deltaTime, Space.Self); }
}
