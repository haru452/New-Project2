using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// WarShip.cs の続き: 空母の艦隊が作戦範囲の外から入ってくる
//  ・普通・ハード: 作戦範囲の外のランダムな海に現れ、まっすぐ範囲内へ向かい、範囲内の輪のコースを回り続ける
//  ・遊覧モード: 範囲内の輪のコースの入口で止まっている（着陸できるように）
//  ・コースは「艦隊の幅」ぶん周りにも島が無い所だけ使う（大きいので、島にぶつからないように）
//  ・位置は全員の画面で、同じ種・同じサーバー時刻から計算する（通信なし）
public partial class WarShip
{
    [Header("空母: 作戦範囲の外から入ってくる（AreaSetup が設定）")]
    public bool carrierApproach;
    public Vector2 opCenter;              // 作戦範囲の中心（x, z）
    public float opHalf = 8000f;          // 作戦範囲のたて・よこの半分 (m)
    public float opInset = 1200f;         // 輪のコースは範囲の端からこれより内側
    public float approachMin = 2500f;     // 現れる場所: 範囲の端からこれ以上外
    public float approachMax = 4500f;     //             これ以下
    [UdonSynced] private double spawnTime;   // 沈められて別の場所に現れた時刻（0 = 戦闘が始まって出てくる時）
    private float approachLen, ringLen;

    void GenerateCarrierPath(int seed)
    {
        builtSeed = seed;
        phaseShift = 0f; waiting = false;
        rng = seed % 2147483647;
        if (rng <= 0) rng = 1;
        float y = transform.position.y;
        for (int tries = 0; tries < 60; tries++)
        {
            // 範囲内の輪のコース
            float R = 900f + NextF() * 1300f;
            float avail = Mathf.Max(opHalf - opInset - R * 1.25f - pathMargin, 100f);
            Vector3 c = new Vector3(opCenter.x + (NextF() * 2f - 1f) * avail, y, opCenter.y + (NextF() * 2f - 1f) * avail);
            Vector3[] pts = new Vector3[6];
            float a0 = NextF() * Mathf.PI * 2f;
            for (int i = 0; i < 6; i++)
            {
                float a = a0 + i * Mathf.PI / 3f + (NextF() - 0.5f) * 0.5f;
                float r = R * (0.7f + NextF() * 0.5f);
                pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
            }
            // 範囲の外の現れる場所（輪のある側を中心に、ばらつかせた向きで、範囲の端を越えた所）
            float ang = Mathf.Atan2(c.z - opCenter.y, c.x - opCenter.x) + (NextF() - 0.5f) * 2.4f;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            float toEdge = opHalf / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.z));
            float out1 = approachMin + NextF() * (approachMax - approachMin);
            Vector3 start = new Vector3(opCenter.x, y, opCenter.y) + dir * (toEdge + out1);

            path = pts;
            BuildPath();          // 輪だけの点（中心と半径もここで決まる）
            JoinApproach(start);  // 前に「外からまっすぐ来る道」を足す
            if (CarrierClear()) return;
        }
        // 見つからなければ止まったまま
        path = new Vector3[0];
        sampPos = null;
        Debug.Log("[WarShip] 空母のコースが見つからなかった（島が多すぎる）");
    }

    // 輪のいちばん近い点まで、外からまっすぐの道をつなぐ。道の終わりから輪を 1 周して、またそこに戻る
    void JoinApproach(Vector3 start)
    {
        int n = sampPos.Length - 1;   // 輪の点の数（最後の点 = 最初の点）
        int m = 0; float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Vector3 d = sampPos[i] - start; d.y = 0f;
            if (d.sqrMagnitude < best) { best = d.sqrMagnitude; m = i; }
        }
        Vector3 entry = sampPos[m];
        float aLen = Mathf.Sqrt(best);
        int na = Mathf.Max(2, Mathf.CeilToInt(aLen / 150f));
        Vector3[] np = new Vector3[na + n + 1];
        float[] nd = new float[na + n + 1];
        for (int i = 0; i < na; i++) np[i] = Vector3.Lerp(start, entry, i / (float)na);
        for (int j = 0; j <= n; j++) np[na + j] = sampPos[(m + j) % n];
        float d0 = 0f;
        nd[0] = 0f;
        for (int k = 1; k < np.Length; k++) { d0 += Vector3.Distance(np[k - 1], np[k]); nd[k] = d0; }
        sampPos = np;
        sampDist = nd;
        approachLen = nd[na];
        pathLen = d0;
        ringLen = Mathf.Max(pathLen - approachLen, 1f);
        sampIdx = 0;
    }

    // 艦隊の幅（pathMargin）の中に島・陸・止まっている船が無いか。外から来る道も含めて全部の点で調べる
    bool CarrierClear()
    {
        if (sampPos == null) return false;
        if (avoidRadius > 0f)
            for (int k = 0; k < sampPos.Length; k++)
            {
                Vector3 fa = sampPos[k] - avoidCenter; fa.y = 0f;
                if (fa.magnitude < avoidRadius + 300f) return false;   // 出撃地点の近くは通らない
            }
        for (int k = 0; k < sampPos.Length; k++)
        {
            Vector3 q = sampPos[k];
            if (allShips != null)
                for (int w = 0; w < allShips.Length; w++)
                {
                    WarShip o = allShips[w];
                    if (o == null || o == this || o.roaming || o.leader != null) continue;
                    Vector3 op = o.model != null ? o.model.transform.position : o.transform.position;
                    Vector3 dq = q - op; dq.y = 0f;
                    if (dq.magnitude < pathMargin + 200f) return false;
                }
            if (Land(q)) return false;
            for (int a = 0; a < 8; a++)
            {
                float an = a * Mathf.PI / 4f;
                Vector3 off = new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an));
                if (Land(q + off * pathMargin)) return false;
                if ((a & 1) == 0 && Land(q + off * (pathMargin * 0.5f))) return false;
            }
        }
        return true;
    }

    // その場所の海面より上に、島・陸があるか
    bool Land(Vector3 q)
    {
        RaycastHit[] hm = Physics.RaycastAll(new Vector3(q.x, seaLevel + 400f, q.z), Vector3.down, 430f, groundMask, QueryTriggerInteraction.Ignore);
        for (int h = 0; h < hm.Length; h++)
        {
            Collider col = hm[h].collider;
            if (col == null || col.transform.IsChildOf(transform) || col.attachedRigidbody != null) continue;
            if (hm[h].point.y > seaLevel + 1.5f) return true;
        }
        return false;
    }

    // コース上の今の位置（m）。外から来る道を走り終えたら、あとは輪を回り続ける
    float CarrierS(float gap)
    {
        float s;
        if (gameMode != null && gameMode._SightMode()) s = approachLen - gap;   // 遊覧モード: 輪の入口で止まる
        else
        {
            double now = waiting ? waitStart : Networking.GetServerTimeInSeconds();
            double t0 = spawnTime;
            float we = gameMode != null ? gameMode._WarElapsed() : -1f;
            if (we >= 0f)
            {
                double appear = Networking.GetServerTimeInSeconds() - we + appearDelay;   // 戦闘が始まって appearDelay 秒後
                if (appear > t0) t0 = appear;
            }
            else if (t0 == 0.0) t0 = now;   // まだ戦闘になっていない: 外の現れる場所で待つ
            s = (float)((now - t0) * pathSpeed) + phaseShift - gap;
        }
        if (s < 0f) s = 0f;
        if (s > approachLen) s = approachLen + (s - approachLen) % ringLen;
        return s;
    }
}
