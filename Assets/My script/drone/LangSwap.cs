using UdonSharp;
using UnityEngine;

// 言語選択（Subtitles の lang: 0 = English, 1 = 日本語, 2 = 中文）に合わせて、板の絵を替える（自分の画面だけ）
//  ・target + mats: 1 枚の板のマテリアルを、言語の順（English / 日本語 / 中文）に替える
//  ・objs: 言語ごとに別々の板があるとき、選んだ言語の板だけ出す（順は English / 日本語 / 中文）。
//          出す板は、objs[0] の位置（真ん中）に寄せる
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LangSwap : UdonSharpBehaviour
{
    public Subtitles subtitles;
    public Renderer target;
    public Material[] mats;
    public GameObject[] objs;

    private int shown = -1;
    private float next;
    private Vector3 center;
    private bool hasCenter;

    void Start()
    {
        if (objs != null && objs.Length > 0 && objs[0] != null)
        {
            center = objs[0].transform.localPosition;
            hasCenter = true;
        }
        Apply(0);   // 言語を選ぶ前は English
    }

    void Update()
    {
        if (subtitles == null || Time.time < next) return;
        next = Time.time + 0.3f;
        int l = subtitles._Lang();
        if (l != shown) Apply(l);
    }

    void Apply(int l)
    {
        shown = l;
        if (target != null && mats != null && l >= 0 && l < mats.Length && mats[l] != null) target.sharedMaterial = mats[l];
        if (objs != null)
        {
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                objs[i].SetActive(i == l);
                if (i == l && hasCenter) objs[i].transform.localPosition = center;
            }
        }
    }
}
