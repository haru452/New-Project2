using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using TMPro;

// controller.cs の続き（長いので分けたファイル。中身は同じクラス controller）
public partial class controller
{
    public override void PostLateUpdate()
    {
        if (lockBox == null) return;
        bool show = isPiloting && lockCand >= 0 && TrackAlive(lockCand) && (locked || (Time.time * 8f) % 2f < 1.3f);
        if (lockBox.gameObject.activeSelf != show) lockBox.gameObject.SetActive(show);
        if (!show || localPlayer == null) return;
        VRCPlayerApi.TrackingData head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 d = LockCenter(lockCand) - head.position;
        float dist = d.magnitude;
        if (dist < 1f) return;
        Vector3 dir = d / dist;
        // 5m 先に、標的と同じ見た目の大きさで置く
        float size = Mathf.Clamp(TrackRadius(lockCand) * 2.4f * 5f / dist, 0.18f, 2.5f);
        if (!locked) size *= 1f + 1.2f * (1f - Mathf.Clamp01(lockTimer / lockTime));   // ロック中は縮みながら近づく
        lockBox.position = head.position + dir * 5f;
        lockBox.rotation = Quaternion.LookRotation(dir, head.rotation * Vector3.up);
        lockBox.localScale = new Vector3(size, size, size);
    }

    void TryFireMissile(bool guided)
    {
        if (missiles == null || missiles.Length == 0 || gunMuzzle == null) return;
        if (Time.time - lastMissileTime < missileCooldown) return;
        if (guided)
        {
            if (guidedLeft <= 0) return;
            if (!locked) { noLockMsgUntil = Time.time + 1.2f; return; }
            guidedLeft--;
        }
        else
        {
            if (missileLeft <= 0) return;
            missileLeft--;
        }
        lastMissileTime = Time.time;
        OnPlayerFired();
        LaunchMissile(true, guided ? lockCand : -1);
        QueueVoice(voiceMissile);
        // 他の人の画面にも飛ばす（見た目だけ）
        SendCustomNetworkEvent(NetworkEventTarget.All, guided ? nameof(NetMissileGuided) : nameof(NetMissile));
    }

    public void NetMissile()
    {
        if (isPiloting) return;   // 自分の分は発射済み
        LaunchMissile(false, -1);
    }

    public void NetMissileGuided()
    {
        if (isPiloting) return;
        LaunchMissile(false, FindLockCandidate());   // 見ている人の画面でも、機首の前の標的を追う
    }

    void LaunchMissile(bool real, int tgt)
    {
        if (gunMuzzle == null) return;
        _LaunchMissileFrom(real, tgt, gunMuzzle.position - gunMuzzle.up * 0.6f, gunMuzzle.forward);
    }

    // 発射位置と向きを指定して撃つ（ガンナー席の砲塔からも使う）
    public void _LaunchMissileFrom(bool real, int tgt, Vector3 p, Vector3 fwd)
    {
        if (missiles == null || mActive == null) return;
        // 空いているミサイルを探す（無ければ一番古いものを使い回す）
        int slot = -1;
        float oldest = -1f;
        for (int i = 0; i < missiles.Length; i++)
        {
            if (missiles[i] == null) continue;
            if (!mActive[i]) { slot = i; break; }
            if (mAge[i] > oldest) { oldest = mAge[i]; slot = i; }
        }
        if (slot < 0) return;

        // 撃った向き（fwd）へ飛ばす。速さは機体の速さ＋発射の速さ
        // （機体の速度ベクトルをそのまま足すと、横や後ろへ撃っても機体の進む方へ流れてしまう）
        float baseSpeed = rb != null ? rb.velocity.magnitude : 0f;
        Vector3 v = fwd.normalized * (baseSpeed + missileLaunchSpeed);
        mPos[slot] = p;
        mVel[slot] = v;
        mAge[slot] = 0f;
        mActive[slot] = true;
        mReal[slot] = real;
        mBurning[slot] = true;
        mTgt[slot] = tgt;
        mTgtPrev[slot] = (tgt >= 0 && radarTracks[tgt] != null) ? radarTracks[tgt].position : Vector3.zero;

        Transform m = missiles[slot];
        m.position = p;
        m.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        m.gameObject.SetActive(true);
        TrailRenderer tr = m.GetComponent<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }
        Transform flame = m.Find("Flame");
        if (flame != null) flame.gameObject.SetActive(true);
        AudioSource au = m.GetComponent<AudioSource>();
        if (au != null && au.clip != null) au.PlayOneShot(au.clip, 1f);
    }

    void UpdateMissiles(float dt)
    {
        if (missiles == null || mActive == null) return;
        for (int i = 0; i < missiles.Length; i++)
        {
            if (!mActive[i]) continue;
            mAge[i] += dt;
            Vector3 v = mVel[i];
            float sp = v.magnitude;
            Vector3 dir = sp > 0.1f ? v / sp : missiles[i].forward;
            bool guided = mTgt[i] >= 0;
            if (guided && !TrackAlive(mTgt[i])) { mTgt[i] = -1; guided = false; }
            bool burning = mAge[i] < (guided || mTgt[i] >= 0 ? guidedBurnTime : missileBurnTime);
            Vector3 tp = Vector3.zero;
            float tRad = 0f;
            if (guided)
            {
                // 誘導: 標的の動きを先読みして、曲がれる速さの範囲で向きを変える
                tp = radarTracks[mTgt[i]].position;
                tRad = TrackRadius(mTgt[i]);
                Vector3 tv = dt > 0.0001f ? (tp - mTgtPrev[i]) / dt : Vector3.zero;
                mTgtPrev[i] = tp;
                if (mAge[i] > 0.25f && sp > 1f)
                {
                    float tgo = Mathf.Min(Vector3.Distance(tp, mPos[i]) / Mathf.Max(sp, 40f), 4f);
                    Vector3 aim = tp + tv * tgo - mPos[i];
                    if (aim.sqrMagnitude > 0.01f)
                    {
                        v = Vector3.RotateTowards(v, aim.normalized * sp, guidedTurnRate * Mathf.Deg2Rad * dt, 0f);
                        dir = v / sp;
                    }
                }
            }

            // 推進中はほぼまっすぐ（重力は少しだけ）、推進が終わると重力で落ちていく
            Vector3 acc = Vector3.down * 9.81f * (burning ? (guided ? 0f : 0.15f) : 1f) - v * sp * 0.0012f;
            if (burning) acc += dir * missileAccel;
            v += acc * dt;
            Vector3 p0 = mPos[i];
            Vector3 p1 = p0 + v * dt;

            // 当たり判定（建物・標的・地面）
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
            // 海面
            bool water = false;
            if (p1.y < seaLevelY && p0.y >= seaLevelY)
            {
                float t = (seaLevelY - p0.y) / (p1.y - p0.y);
                float wd = len * Mathf.Clamp01(t);
                if (wd <= hitDist) { water = true; hitPoint = p0 + seg * Mathf.Clamp01(t); hitCol = null; }
            }
            // 近接信管: 標的の近くを通ったら爆発
            if (guided && len > 0.0001f)
            {
                float tt = Mathf.Clamp01(Vector3.Dot(tp - p0, seg) / (len * len));
                Vector3 cpt = p0 + seg * tt;
                if ((tp - cpt).magnitude < tRad + 2f && len * tt <= hitDist)
                {
                    FlightTarget dft = (radarTrackTargets != null && mTgt[i] < radarTrackTargets.Length) ? radarTrackTargets[mTgt[i]] : null;
                    MissileImpact(i, cpt, false, null, dft);
                    continue;
                }
            }
            if (water || hitCol != null)
            {
                MissileImpact(i, hitPoint, water, hitCol, null);
                continue;
            }
            if (mAge[i] > 15f || p1.y < seaLevelY - 5f) { EndMissile(i); continue; }

            mPos[i] = p1;
            mVel[i] = v;
            missiles[i].position = p1;
            if (v.sqrMagnitude > 0.01f) missiles[i].rotation = Quaternion.LookRotation(v);
            if (mBurning[i] && !burning)
            {
                // 推進が終わったら炎と煙を止める
                mBurning[i] = false;
                Transform flame = missiles[i].Find("Flame");
                if (flame != null) flame.gameObject.SetActive(false);
                TrailRenderer tr = missiles[i].GetComponent<TrailRenderer>();
                if (tr != null) tr.emitting = false;
            }
        }
    }

    void MissileImpact(int i, Vector3 p, bool water, Collider hitCol, FlightTarget direct)
    {
        if (water)
        {
            // 海に落ちたら大きな水柱
            if (missileSplashFx != null)
            {
                missileSplashFx.transform.position = new Vector3(p.x, seaLevelY, p.z);
                missileSplashFx.Emit(70);
            }
        }
        else if (crashFx != null)
        {
            crashFx.transform.position = p;
            crashFx.Play(true);
        }
        if (crashAudio != null)
        {
            crashAudio.transform.position = p;
            crashAudio.Play();
        }

        // 撃った本人の画面でだけダメージを与える
        if (mReal[i] && hitCol != null)
        {
            FlightTarget tg = FindTarget(hitCol);
            if (tg != null) { tg._HitMissile(); NoteHit(tg); if (!IsEnemyTarget(tg)) QueueVoice(voiceTargetHit); }
            else if (_PvpHit(hitCol, true)) QueueVoice(voiceTargetHit);   // ほかのプレイヤーの機体
            else QueueVoice(voiceImpact);   // 島・建物など
        }
        else if (mReal[i] && direct != null) { direct._HitMissile(); NoteHit(direct); if (!IsEnemyTarget(direct)) QueueVoice(voiceTargetHit); }
        EndMissile(i);
    }

    void EndMissile(int i)
    {
        mActive[i] = false;
        if (missiles[i] != null) missiles[i].gameObject.SetActive(false);
    }

    // ---------------- 機銃 ----------------
    void UpdateGun(float dt)
    {
        bool want = isPiloting && fireInput && ammo > 0 && gunMuzzle != null;
        if (want != localFiring)
        {
            localFiring = want;
            // 他の人の画面でも曳光弾と音を出す（当たり判定は撃った本人だけ）
            SendCustomNetworkEvent(NetworkEventTarget.All, want ? nameof(NetFireOn) : nameof(NetFireOff));
        }

        bool firing = localFiring || remoteFiring;
        if (!firing || gunMuzzle == null)
        {
            fireTimer = 0f;
            return;
        }
        fireTimer -= dt;
        int guard = 0;
        while (fireTimer <= 0f && guard < 4)
        {
            fireTimer += 1f / Mathf.Max(fireRate, 1f);
            guard++;
            FireOne(localFiring);
            if (localFiring && ammo <= 0) break;
        }
    }

    public void NetFireOn()
    {
        if (isPiloting) return;   // 自分の分は UpdateGun で処理済み
        remoteFiring = true;
    }

    public void NetFireOff()
    {
        remoteFiring = false;
    }

    void FireOne(bool real)
    {
        // 翼に 2 門あれば、左右交互に撃つ
        Transform mz = gunMuzzle;
        ParticleSystem tfx = tracerFx;
        if (gunMuzzle2 != null)
        {
            gunSide = !gunSide;
            if (gunSide) { mz = gunMuzzle2; tfx = tracerFx2; }
        }
        if (tfx != null) tfx.Emit(1);
        if (gunAudio != null && gunAudio.clip != null)
        {
            gunAudio.pitch = Random.Range(0.92f, 1.08f);
            gunAudio.PlayOneShot(gunAudio.clip, Random.Range(0.85f, 1f));
        }

        Vector3 origin = mz.position;
        Vector3 dir = mz.forward;
        if (gunSpread > 0f)
        {
            dir = Quaternion.AngleAxis(Random.Range(-gunSpread, gunSpread), mz.up)
                * Quaternion.AngleAxis(Random.Range(-gunSpread, gunSpread), mz.right) * dir;
        }

        // 海面との交点（下向きに撃った時だけ）
        float waterDist = -1f;
        if (dir.y < -0.0001f)
        {
            float t = (seaLevelY - origin.y) / dir.y;
            if (t > 0f && t < gunRange) waterDist = t;
        }

        if (!real)
        {
            // 他の人が撃った弾: 水柱だけ出す（当たり判定は撃った本人だけ）
            if (waterDist > 0f) WaterSplash(origin + dir * waterDist);
            return;
        }

        ammo--;
        OnPlayerFired();
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, gunRange, groundMask, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;
            if (col.transform.IsChildOf(car.transform)) continue;   // 自分の機体は無視
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = i; }
        }

        // 海面の方が手前（または何にも当たらない）なら水柱
        if (waterDist > 0f && (best < 0 || waterDist <= bestDist + 0.5f))
        {
            WaterSplash(origin + dir * waterDist);
            return;
        }
        if (best < 0) return;

        RaycastHit h = hits[best];
        if (impactFx != null)
        {
            impactFx.transform.position = h.point;
            impactFx.transform.rotation = Quaternion.LookRotation(h.normal);
            impactFx.Emit(8);
        }
        FlightTarget tg = FindTarget(h.collider);
        if (tg != null) { tg._Hit(); NoteHit(tg); }
        else _PvpHit(h.collider, false);   // ほかのプレイヤーの機体
    }

    // 当たった物の標的を探す: 物理の付いた物（敵機など）はその中から、それ以外は親をたどる
    FlightTarget FindTarget(Collider col)
    {
        if (col == null) return null;
        Rigidbody arb = col.attachedRigidbody;
        if (arb != null && arb.transform != car.transform)
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

    void WaterSplash(Vector3 p)
    {
        if (waterSplashFx == null) return;
        waterSplashFx.transform.position = p;
        waterSplashFx.Emit(12);
    }

    // ---------------- HUD / 音 ----------------
    // ---------------- コックピット表示 ----------------
}
