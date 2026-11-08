using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// controller.cs の続き: 魚雷（武器 4 番）
//  ・機体から落とす → 海に着いたら、海面のすぐ下をまっすぐ走る（白い航跡を残す）
//  ・高い所（torpedoMaxDropAlt より上）から落とすと、海に着いた時に壊れて走らない（水柱だけ）
//  ・島・基地・船の上に落ちたら、その場で爆発（爆弾と同じ）
//  ・走っている間に島や船に当たったら、爆発と水柱。ダメージは FlightTarget.torpedoDamage（ふつうの船を 2 発）
//  ・ダメージは撃った人の画面でだけ与える。ほかの人の画面には見た目だけ飛ぶ
public partial class controller
{
    [Header("魚雷（TorpedoSetup が作る）")]
    public Transform[] torpedoes;            // 魚雷の見た目（この機体専用のプール）
    public int maxTorpedoes = 8;
    public float torpedoMaxDropAlt = 150f;   // 海面からこの高さより上で落とすと、海に着いた時に壊れる (m)
    public float torpedoSpeed = 25f;         // 走る速さ (m/s、約 50 ノット)
    public float torpedoRange = 3000f;       // 走る距離 (m)
    public float torpedoDepth = 0.8f;        // 海面からの深さ (m)
    public float torpedoCooldown = 1f;

    [Header("機械の音（魚雷を落とす・脚の出し入れ。全員に聞こえる）")]
    public AudioSource mechAudio;            // 機体に付けた音（立体音）
    public AudioClip torpedoDropClip;
    public AudioClip gearUpClip, gearDownClip;

    void PlayMech(AudioClip c)
    {
        if (mechAudio != null && c != null) mechAudio.PlayOneShot(c, 1f);
    }

    private int torpedoLeft;
    private float lastTorpedoTime = -10f;
    private Vector3[] tPos, tVel, tDir;
    private float[] tAge, tDropAlt, tRun;
    private bool[] tActive, tReal, tRunning;

    void InitTorpedoes()
    {
        torpedoLeft = maxTorpedoes;
        int n = torpedoes != null ? torpedoes.Length : 0;
        tPos = new Vector3[n]; tVel = new Vector3[n]; tDir = new Vector3[n];
        tAge = new float[n]; tDropAlt = new float[n]; tRun = new float[n];
        tActive = new bool[n]; tReal = new bool[n]; tRunning = new bool[n];
        for (int i = 0; i < n; i++) if (torpedoes[i] != null) torpedoes[i].gameObject.SetActive(false);
    }

    // レーダーに出すため（controller_7.cs の UpdateMissileBlips）
    public int _TrpCount() { return torpedoes != null && tActive != null ? torpedoes.Length : 0; }
    public bool _TrpActive(int i) { return tActive != null && i < tActive.Length && tActive[i]; }
    public Vector3 _TrpPos(int i) { return tPos[i]; }

    void RefillTorpedoes() { torpedoLeft = maxTorpedoes; }
    void AddTorpedo() { torpedoLeft = Mathf.Min(maxTorpedoes, torpedoLeft + 1); }
    bool NeedsTorpedo() { return torpedoLeft < maxTorpedoes; }
    bool TorpedoAltOk() { return altitude <= torpedoMaxDropAlt; }

    void TryDropTorpedo()
    {
        if (torpedoes == null || torpedoes.Length == 0 || tActive == null || car == null) return;
        if (torpedoLeft <= 0 || Time.time - lastTorpedoTime < torpedoCooldown) return;
        torpedoLeft--;
        lastTorpedoTime = Time.time;
        OnPlayerFired();
        LaunchTorpedo(true);
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetTorpedo));   // ほかの人の画面にも（見た目だけ）
    }

    public void NetTorpedo()
    {
        if (isPiloting) return;   // 自分の分は落とし済み
        LaunchTorpedo(false);
    }

    void LaunchTorpedo(bool real)
    {
        if (tActive == null || car == null) return;
        int slot = -1;
        float oldest = -1f;
        for (int i = 0; i < torpedoes.Length; i++)
        {
            if (torpedoes[i] == null) continue;
            if (!tActive[i]) { slot = i; break; }
            if (tAge[i] > oldest) { oldest = tAge[i]; slot = i; }
        }
        if (slot < 0) return;
        Transform ct = car.transform;
        Vector3 p = ct.position - ct.up * 1.6f;
        Vector3 v = rb != null ? rb.velocity : Vector3.zero;
        tPos[slot] = p;
        tVel[slot] = v + Vector3.down * 2f;
        Vector3 h = new Vector3(ct.forward.x, 0f, ct.forward.z);
        tDir[slot] = h.sqrMagnitude > 0.001f ? h.normalized : Vector3.forward;
        tAge[slot] = 0f;
        tDropAlt[slot] = p.y - seaLevelY;
        tRun[slot] = 0f;
        tActive[slot] = true;
        tReal[slot] = real;
        tRunning[slot] = false;
        Transform t = torpedoes[slot];
        t.position = p;
        t.rotation = Quaternion.LookRotation(ct.forward, Vector3.up);
        t.gameObject.SetActive(true);
        SetWake(slot, false, true);
        PlayMech(torpedoDropClip);   // 切り離しの「ガコン」
    }

    // 航跡（白い尾）。clear = 前の尾を消す
    void SetWake(int i, bool on, bool clear)
    {
        Transform w = torpedoes[i].Find("Wake");
        if (w == null) return;
        TrailRenderer tr = w.GetComponent<TrailRenderer>();
        if (tr == null) return;
        if (clear) tr.Clear();
        tr.emitting = on;
    }

    void UpdateTorpedoes(float dt)
    {
        if (torpedoes == null || tActive == null) return;
        for (int i = 0; i < torpedoes.Length; i++)
        {
            if (!tActive[i]) continue;
            tAge[i] += dt;
            if (tRunning[i]) { RunTorpedo(i, dt); continue; }

            // 落ちている間: 重力と空気の抵抗
            Vector3 v = tVel[i];
            v += (Vector3.down * 9.81f - v * v.magnitude * 0.0015f) * dt;
            Vector3 p0 = tPos[i];
            Vector3 p1 = p0 + v * dt;
            Vector3 seg = p1 - p0;
            float len = seg.magnitude;
            float hitDist = float.MaxValue;
            Vector3 hitPoint = p1;
            Collider hitCol = null;
            if (len > 0.0001f)
            {
                RaycastHit[] hits = Physics.RaycastAll(p0, seg / len, len, groundMask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < hits.Length; k++)
                {
                    Collider c = hits[k].collider;
                    if (c == null || c.transform.IsChildOf(car.transform)) continue;
                    if (hits[k].distance < hitDist) { hitDist = hits[k].distance; hitPoint = hits[k].point; hitCol = c; }
                }
            }
            // 海面に着いた
            if (p1.y < seaLevelY && p0.y >= seaLevelY)
            {
                float t = Mathf.Clamp01((seaLevelY - p0.y) / (p1.y - p0.y));
                if (len * t <= hitDist)
                {
                    Vector3 wp = p0 + seg * t;
                    Splash(wp, 45);
                    if (tReal[i]) Debug.Log("[TorpedoDebug] 着水 " + wp.ToString("F0") + " 落とした高さ " + tDropAlt[i].ToString("F0") + "m" + (tDropAlt[i] > torpedoMaxDropAlt ? " → 高すぎて壊れた" : " → 走り出す"));
                    if (tDropAlt[i] > torpedoMaxDropAlt) { EndTorpedo(i); continue; }   // 高すぎた: 壊れて走らない
                    Vector3 h = new Vector3(v.x, 0f, v.z);
                    if (h.sqrMagnitude > 1f) tDir[i] = h.normalized;
                    tRunning[i] = true;
                    tPos[i] = new Vector3(wp.x, seaLevelY - torpedoDepth, wp.z);
                    torpedoes[i].position = tPos[i];
                    torpedoes[i].rotation = Quaternion.LookRotation(tDir[i], Vector3.up);
                    SetWake(i, true, true);
                    continue;
                }
            }
            // 島・基地・船の上に落ちた: その場で爆発（爆弾と同じ）
            if (hitCol != null) { TorpedoBlast(i, hitPoint, hitCol, false); continue; }
            if (tAge[i] > 40f) { EndTorpedo(i); continue; }

            tPos[i] = p1;
            tVel[i] = v;
            torpedoes[i].position = p1;
            if (v.sqrMagnitude > 0.01f) torpedoes[i].rotation = Quaternion.LookRotation(Vector3.Lerp(tDir[i], v.normalized, 0.5f));
        }
    }

    void RunTorpedo(int i, float dt)
    {
        float step = torpedoSpeed * dt;
        Vector3 p0 = tPos[i];
        Vector3 dir = tDir[i];
        // 当たり判定は喫水線の高さで（船体・島の岸に当たる）
        Vector3 from = new Vector3(p0.x, seaLevelY + 0.5f, p0.z);
        RaycastHit[] hits = Physics.RaycastAll(from, dir, step + 2f, groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Collider col = null;
        Vector3 hp = Vector3.zero;
        for (int k = 0; k < hits.Length; k++)
        {
            Collider c = hits[k].collider;
            if (c == null || (car != null && c.transform.IsChildOf(car.transform))) continue;
            if (hits[k].distance < best) { best = hits[k].distance; col = c; hp = hits[k].point; }
        }
        if (col != null) { TorpedoBlast(i, new Vector3(hp.x, seaLevelY, hp.z), col, true); return; }
        // 船: 当たり判定が喫水線より上にしか無い船があるので、縦に長いカプセル（海面の 3m 下〜20m 上）で調べる
        //   （標的 FlightTarget を持つ物だけ。海そのものの当たり判定などは無視）
        Vector3 ahead = p0 + dir * Mathf.Min(step + 1f, 6f);
        Collider[] ov = Physics.OverlapCapsule(new Vector3(ahead.x, seaLevelY - 3f, ahead.z), new Vector3(ahead.x, seaLevelY + 20f, ahead.z), 2.5f, groundMask, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < ov.Length; k++)
        {
            Collider c = ov[k];
            if (c == null || (car != null && c.transform.IsChildOf(car.transform))) continue;
            if (FindTarget(c) == null) continue;
            TorpedoBlast(i, new Vector3(ahead.x, seaLevelY, ahead.z), c, true);
            return;
        }
        tRun[i] += step;
        if (tRun[i] > torpedoRange) { if (tReal[i]) Debug.Log("[TorpedoDebug] 何にも当たらず " + torpedoRange.ToString("F0") + "m 走り終えた"); EndTorpedo(i); return; }
        Vector3 p1 = p0 + dir * step;
        tPos[i] = p1;
        torpedoes[i].position = p1;
    }

    void Splash(Vector3 p, int n)
    {
        if (missileSplashFx == null) return;
        missileSplashFx.transform.position = new Vector3(p.x, seaLevelY, p.z);
        missileSplashFx.Emit(n);
    }

    // 爆発。water = 海の中で当たった（水柱も立てる）
    void TorpedoBlast(int i, Vector3 p, Collider col, bool water)
    {
        if (crashFx != null) { crashFx.transform.position = p; crashFx.Play(true); }
        if (crashAudio != null) { crashAudio.transform.position = p; crashAudio.Play(); }
        if (water) Splash(p, 90);
        if (tReal[i])   // 調査用（魚雷のダメージが入らない件。原因が分かったら消す）
        {
            FlightTarget dt0 = col != null ? FindTarget(col) : null;
            Debug.Log("[TorpedoDebug] 爆発 " + p.ToString("F0") + (water ? " 走っていて" : " 落ちて") + " 当たり判定: " + (col != null ? col.name + "（" + col.transform.root.name + "）" : "なし")
                + " / 標的: " + (dt0 != null ? dt0.name + " kind " + dt0.kind + " HP " + dt0._HpRatio().ToString("F2") : "なし"));
        }
        if (tReal[i] && col != null)
        {
            FlightTarget tg = FindTarget(col);
            if (tg != null) { tg._HitTorpedo(); NoteHit(tg); if (!IsEnemyTarget(tg)) QueueVoice(voiceTargetHit); }
            else if (_PvpHit(col, true)) QueueVoice(voiceTargetHit);   // ほかのプレイヤーの機体
            else QueueVoice(voiceImpact);   // 島・建物など
        }
        EndTorpedo(i);
    }

    void EndTorpedo(int i)
    {
        tActive[i] = false;
        tRunning[i] = false;
        if (torpedoes[i] != null) { SetWake(i, false, true); torpedoes[i].gameObject.SetActive(false); }
    }

    // コックピットの武器の行
    string TorpedoLine()
    {
        return WeaponLine(3, "4  TRP", torpedoLeft.ToString() + " / " + maxTorpedoes.ToString());
    }

    // 魚雷を選んでいる時の案内（落とせる高さか）
    string TorpedoStatus()
    {
        if (torpedoLeft <= 0) return "<color=#ff3030>TRP EMPTY</color>";
        if (!TorpedoAltOk()) return "<color=#ff3030>TOO HIGH  " + torpedoMaxDropAlt.ToString("F0") + "m</color>";
        return "<color=#7fff9f>TRP READY</color>";
    }
}
