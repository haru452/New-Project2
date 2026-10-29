using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// WarShip.cs の続き（長いので分けたファイル。中身は同じクラス WarShip）
public partial class WarShip
{
    void UpdateAA(controller mine, float dt)
    {
        if (aaMuzzle == null) return;
        // ハードモード: プレイヤーがミサイルを撃ったら、機体ではなくミサイルを狙う（撃ち落とせることがある）
        int msl = -1;
        float md = aaRange;
        int mn = mine._MslCount();
        for (int i = 0; i < mn; i++)
        {
            if (!mine._MslActive(i) || !mine._MslReal(i)) continue;
            float d = Vector3.Distance(mine._MslPos(i), aaMuzzle.position);
            if (d < md) { md = d; msl = i; }
        }
        if (msl >= 0) { AAMissile(mine, msl, dt); return; }
        Vector3 tp = mine.car.transform.position;
        float dist = Vector3.Distance(tp, aaMuzzle.position);
        if (dist > aaRange || tp.y - seaLevel < 2f) { aaCycle = 0f; return; }
        aaCycle += dt;
        if (aaCycle > aaBurst + aaPause) aaCycle = 0f;
        if (aaCycle > aaBurst) return;   // 休み中
        aaTimer -= dt;
        if (aaTimer > 0f) return;
        aaTimer = 1f / Mathf.Max(aaFireRate, 1f);

        // 先読みして狙う（弾の速さ 600 m/s）。機体そのものより、前や後ろをなめるように撃つ
        Rigidbody trb = mine.car.GetComponent<Rigidbody>();
        Vector3 tv = trb != null ? trb.velocity : Vector3.zero;
        Vector3 mp = aaMuzzle.position;
        Vector3 aimTrue = tp + tv * (dist / 600f);       // 当たり判定に使う、機体が来る位置
        Vector3 along = tv.sqrMagnitude > 1f ? tv.normalized : mine.car.transform.forward;
        float pick = Random.value;
        Vector3 aim = aimTrue;
        if (pick < 0.4f) aim += along * Random.Range(15f, 60f);        // 前
        else if (pick < 0.8f) aim -= along * Random.Range(15f, 60f);   // 後ろ
        // 海面すれすれ（30m 以下）の時は少し下を撃つ → 弾が機体の前後の海に落ちて水柱
        if (tp.y - seaLevel < 30f) aim.y -= Random.Range(2f, 10f);
        Vector3 dir = (aim - mp).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        dir = Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, side.normalized) * dir;
        aaMuzzle.rotation = Quaternion.LookRotation(dir);
        if (aaTracer != null) aaTracer.Emit(1);
        if (aaAudio != null && aaAudio.clip != null)
        {
            aaAudio.pitch = Random.Range(1.15f, 1.3f);
            aaAudio.PlayOneShot(aaAudio.clip, 0.55f);
        }
        // 弾の通り道が機体の近くを通ったら当たり
        float reach = aaRange * 1.6f;
        if (DistToSegment(aimTrue, mp, mp + dir * reach) < aaHitRadius)
        {
            mine._ExternalDamage(aaDamage);
            return;
        }
        // 外れて海に落ちたら水柱
        if (dir.y < -0.0001f)
        {
            float t = (seaLevel - mp.y) / dir.y;
            if (t > 0f && t < reach) mine._Splash(mp + dir * t);
        }
    }

    // 対空機銃でミサイルを狙う（1 発ごとに少しの確率で撃ち落とす）
    void AAMissile(controller mine, int i, float dt)
    {
        aaCycle += dt;
        if (aaCycle > aaBurst + aaPause) aaCycle = 0f;
        if (aaCycle > aaBurst) return;
        aaTimer -= dt;
        if (aaTimer > 0f) return;
        aaTimer = 1f / Mathf.Max(aaFireRate, 1f);
        Vector3 mp = aaMuzzle.position;
        Vector3 p = mine._MslPos(i);
        Vector3 aim = p + mine._MslVel(i) * (Vector3.Distance(p, mp) / 600f);
        Vector3 dir = (aim - mp).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        dir = Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, side.normalized) * dir;
        aaMuzzle.rotation = Quaternion.LookRotation(dir);
        if (aaTracer != null) aaTracer.Emit(1);
        if (aaAudio != null && aaAudio.clip != null)
        {
            aaAudio.pitch = Random.Range(1.15f, 1.3f);
            aaAudio.PlayOneShot(aaAudio.clip, 0.55f);
        }
        if (Random.value < 0.06f) mine._InterceptMissile(i);
    }

    // relative = true: 作った時の量に掛ける（0 か 1）/ false: その量にする
    void SetRates(ParticleSystem[] list, float r, bool relative)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            ParticleSystem ps = list[i];
            if (ps == null) continue;
            var em = ps.emission;
            if (relative) em.enabled = r > 0f;
            else em.rateOverTimeMultiplier = r;
            if ((relative ? r > 0f : r > 0f) && !ps.isPlaying) ps.Play();
        }
    }

    // レーダー表示用
    public int _RocketCount() { return rockets != null ? rockets.Length : 0; }
    public bool _RocketActive(int i) { return rActive != null && i < rActive.Length && rActive[i]; }
    public Vector3 _RocketPos(int i) { return rPos[i]; }

    // 当たった物の標的を探す: 物理の付いた物（敵機など）はその中から、それ以外は親をたどる
    FlightTarget FindTarget(Collider col)
    {
        if (col == null) return null;
        Rigidbody arb = col.attachedRigidbody;
        if (arb != null)
        {
            FlightTarget ft = arb.GetComponentInChildren<FlightTarget>();
            if (ft != null) return ft;
        }
        Transform ht = col.transform;
        for (int k = 0; k < 12 && ht != null; k++)
        {
            FlightTarget t2 = ht.GetComponent<FlightTarget>();
            if (t2 != null) return t2;
            ht = ht.parent;
        }
        return null;
    }

    void EndRocket(int i, Vector3 p, bool water)
    {
        rActive[i] = false;
        rockets[i].gameObject.SetActive(false);
        if (water && splashFx != null)
        {
            splashFx.transform.position = p;
            splashFx.Emit(60);
        }
        if (impactFx != null)
        {
            impactFx.transform.position = p;
            impactFx.Play(true);
        }
        if (impactAudio != null)
        {
            impactAudio.transform.position = p;
            impactAudio.Play();
        }
    }

    float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float l2 = ab.sqrMagnitude;
        if (l2 < 0.0001f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
        return Vector3.Distance(p, a + ab * t);
    }

    void Explode(int i, Vector3 p, bool water)
    {
        sActive[i] = false;
        shells[i].gameObject.SetActive(false);
        if (water && splashFx != null)
        {
            splashFx.transform.position = p;
            splashFx.Emit(45);
        }
        if (impactFx != null)
        {
            impactFx.transform.position = p;
            impactFx.Play(true);
        }
        if (impactAudio != null)
        {
            impactAudio.transform.position = p;
            impactAudio.Play();
        }
    }
}
