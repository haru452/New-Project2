using UdonSharp;
using UnityEngine;

// 第11パネル「あなたが選んだ道」: モードで絵を替える（遊覧 = 合同演習、普通・ハード = 偵察）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ModePanel : UdonSharpBehaviour
{
    public GameMode gameMode;
    public Renderer target;
    public Material exercise;   // 遊覧モード
    public Material recon;      // 普通・ハード
    private int shown = -1;
    private float next;

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 1f;
        if (gameMode == null || target == null) return;
        int m = gameMode._IsSightseeing() ? 1 : 0;
        if (m == shown) return;
        shown = m;
        target.sharedMaterial = m == 1 ? exercise : recon;
    }
}
