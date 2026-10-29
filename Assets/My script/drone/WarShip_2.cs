using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// WarShip.cs の続き（長いので分けたファイル。中身は同じクラス WarShip）
public partial class WarShip
{
    bool PathClear()
    {
        if (sampPos == null) return false;
        if (OverlapsCarrier()) return false;   // 空母の艦隊とコースが重なるなら使わない
        for (int k = 0; k < sampPos.Length; k++)   // 全部の点で確かめる（小さな島も避ける）
        {
            Vector3 q = sampPos[k];
            Vector3 fa = q - avoidCenter;
            fa.y = 0f;
            if (fa.magnitude < avoidRadius + 300f) return false;
            // 艦隊の幅: まわり 8 方向にも島が無いこと
            if (pathMargin > 0f)
                for (int a8 = 0; a8 < 8; a8++)
                {
                    float an = a8 * Mathf.PI / 4f;
                    Vector3 qm = q + new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an)) * pathMargin;
                    RaycastHit[] hm = Physics.RaycastAll(new Vector3(qm.x, seaLevel + 400f, qm.z), Vector3.down, 430f, groundMask, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < hm.Length; h++)
                    {
                        Collider col = hm[h].collider;
                        if (col == null || col.transform.IsChildOf(transform) || col.attachedRigidbody != null) continue;
                        if (hm[h].point.y > seaLevel + 1.5f) return false;
                    }
                }
            // 止まっている船（空母・艦隊）の近くは通らない（ぶつからないように）
            if (allShips != null)
                for (int w = 0; w < allShips.Length; w++)
                {
                    WarShip o = allShips[w];
                    if (o == null || o == this || o.roaming || o.leader != null) continue;
                    Vector3 op = o.model != null ? o.model.transform.position : o.transform.position;
                    Vector3 dq = q - op;
                    dq.y = 0f;
                    if (dq.sqrMagnitude < shipClearance * shipClearance) return false;
                }
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(q.x, seaLevel + 400f, q.z), Vector3.down, 430f, groundMask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < hits.Length; h++)
            {
                Collider col = hits[h].collider;
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (col.attachedRigidbody != null) continue;   // 飛行機・動く船
                if (hits[h].point.y > seaLevel + 1.5f) return false;   // 島・陸
            }
        }
        return true;
    }

    // コースをなめらかな曲線（Catmull-Rom）にして、細かい点に分けておく
    void BuildPath()
    {
        if (path == null || path.Length < 3) return;
        int n = path.Length;
        int per = 12;
        sampPos = new Vector3[n * per + 1];
        sampDist = new float[n * per + 1];
        int k = 0;
        float d = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = path[(i - 1 + n) % n], p1 = path[i], p2 = path[(i + 1) % n], p3 = path[(i + 2) % n];
            for (int j = 0; j < per; j++)
            {
                float t = j / (float)per;
                Vector3 q = CatmullRom(p0, p1, p2, p3, t);
                if (k > 0) d += Vector3.Distance(sampPos[k - 1], q);
                sampPos[k] = q;
                sampDist[k] = d;
                k++;
            }
        }
        d += Vector3.Distance(sampPos[k - 1], sampPos[0]);
        sampPos[k] = sampPos[0];
        sampDist[k] = d;
        pathLen = d;
        // コースの輪のだいたいの中心と半径（空母の艦隊とぶつからないかを調べるため）
        Vector3 c = Vector3.zero;
        for (int i = 0; i < k; i++) c += sampPos[i];
        c /= Mathf.Max(k, 1);
        float r = 0f;
        for (int i = 0; i < k; i++) { Vector3 dq = sampPos[i] - c; dq.y = 0f; r = Mathf.Max(r, dq.magnitude); }
        pathCenter = c;
        pathRadius = r;
    }
    private Vector3 pathCenter;
    private float pathRadius;
    public Vector3 _PathCenter() { return pathCenter; }
    public float _PathRadius() { return pathRadius; }

    // 空母の艦隊のコースと重なっているか（輪どうしが離れていない）
    bool OverlapsCarrier()
    {
        if (noRelocate || allShips == null || sampPos == null) return false;
        for (int i = 0; i < allShips.Length; i++)
        {
            WarShip o = allShips[i];
            if (o == null || o == this || !o.noRelocate || !o._HasPath()) continue;
            Vector3 dc = pathCenter - o._PathCenter();
            dc.y = 0f;
            if (dc.magnitude < pathRadius + o._PathRadius() + o.pathMargin + 250f) return true;
        }
        return false;
    }

    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // サーバー時刻からコース上の位置を出す（全員同じ）
    void UpdateMove()
    {
        if (sampPos == null || pathLen <= 1f) return;
        float s = PathS(0f);
        int last = sampPos.Length - 1;
        if (sampIdx >= last || sampDist[sampIdx] > s) sampIdx = 0;
        while (sampIdx < last - 1 && sampDist[sampIdx + 1] < s) sampIdx++;
        float seg = sampDist[sampIdx + 1] - sampDist[sampIdx];
        float f = seg > 0.001f ? (s - sampDist[sampIdx]) / seg : 0f;
        Vector3 a = sampPos[sampIdx], b = sampPos[sampIdx + 1];
        Vector3 p = Vector3.Lerp(a, b, f);
        Vector3 dir = b - a;
        dir.y = 0f;
        transform.position = new Vector3(p.x, transform.position.y, p.z);
        if (dir.sqrMagnitude > 0.0001f)
        {
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, yaw - yawOffset, 0f);
        }
    }

    void Update()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;
        float dt = Time.deltaTime;

        // 沈められたら、次は別の場所に出る（持ち主が新しいコースの種を決める）
        if (roaming && target != null && Networking.IsOwner(gameObject))
        {
            bool dead = target._IsDestroyed();
            if (dead && !wasDead) relocPending = true;
            if (!dead) relocPending = false;
            // 護衛: 貨物船が残っている間は移らない（貨物船が同じコースを走り続けるため）
            bool folAlive = follower != null && follower.target != null && !follower.target._IsDestroyed();
            // 空母: 生き残った艦隊の船か、沈んだ場所が誰かに見えている間は移らない（復活もしない）
            if (noRelocate && relocPending && dead && CarrierGroupSeen()) folAlive = true;
            if (noRelocate && target != null) target.holdRespawn = relocPending && dead;
            if (relocPending && dead && !folAlive)
            {
                relocPending = false;
                // 次に出る場所: 近くの船が上限に達していないプレイヤーの近く（無理なら、ランダムな海）。空母はランダムな海
                if (noRelocate || !TryRelocate())
                {
                    pathSeed = Random.Range(1, 2000000000);
                    anchor = Vector3.zero;
                    RequestSerialization();
                    GeneratePath(pathSeed);
                }
            }
            wasDead = dead;
            if (!dead) UpdateAvoid();   // ほかの戦艦とぶつからないように
            if (!noRelocate) UpdateRelocate();
            else if (gameMode != null && gameMode._WorldStart() > 0.0 && gameMode._WorldStart() != seenWorldStart)
            {
                // 空母: ワールドの始まり・モードの切り替えのたびに、出てくる場所（コース）を決め直す
                bool first = seenWorldStart == 0.0;
                seenWorldStart = gameMode._WorldStart();
                if (!first)
                {
                    pathSeed = Random.Range(1, 2000000000);
                    anchor = Vector3.zero;
                    RequestSerialization();
                    GeneratePath(pathSeed);
                }
            }
        }

        // 貨物船: 護衛の後ろを付いて行く
        if (leader != null)
        {
            if (visible || Time.time >= nextMoveUpdate)
            {
                nextMoveUpdate = Time.time + 1f;
                if (leader._HasPath() && useFormation)
                {
                    // 艦隊: 空母の今の位置と向きから、決まった位置へ
                    Vector3 leadPos = leader._PosBehind(0f);
                    Vector3 leadDir = leadPos - leader._PosBehind(20f);
                    leadDir.y = 0f;
                    if (leadDir.sqrMagnitude < 0.0001f) leadDir = Vector3.forward;
                    Quaternion leadRot = Quaternion.LookRotation(leadDir.normalized, Vector3.up);
                    Vector3 fpos = leadPos + leadRot * formationOffset;
                    transform.position = new Vector3(fpos.x, transform.position.y, fpos.z);
                    transform.rotation = Quaternion.Euler(0f, leadRot.eulerAngles.y - yawOffset, 0f);
                }
                else if (leader._HasPath())
                {
                    Vector3 fp = leader._PosBehind(followGap);
                    Vector3 fd = leader._PosBehind(followGap - 20f) - fp;
                    transform.position = new Vector3(fp.x, transform.position.y, fp.z);
                    fd.y = 0f;
                    if (fd.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(fd.x, fd.z) * Mathf.Rad2Deg - yawOffset, 0f);
                }
            }
        }
        // 航行（見えている時は毎フレーム、遠い時は 1 秒ごと = 軽量化）
        else if (sampPos != null && (visible || Time.time >= nextMoveUpdate))
        {
            nextMoveUpdate = Time.time + 1f;
            UpdateMove();
        }

        // 遠いときは見た目ごと止める
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            bool v = (head - transform.position).sqrMagnitude < showDistance * showDistance;
            // 「偵察任務」の無線の間は、プレイヤーの近く（5km 以内）には船を出さない（撃たない）
            if (v && gameMode != null && gameMode._Quiet()) v = false;
            // まだ出てくる時間になっていない（空母）
            bool present = _Present();
            if (target != null) target._SetHidden(!present);
            if (!present) v = false;
            if (v != visible && model != null) model.SetActive(v);
            visible = v;
        }

        UpdateShells(dt);
        UpdateRockets(dt);
        if (!visible) return;

        // 航跡とダメージの煙・炎（0.5 秒ごと）
        if (Time.time >= nextFxCheck)
        {
            nextFxCheck = Time.time + 0.5f;
            float hr = target != null ? target._HpRatio() : 1f;
            bool alive = hr > 0f;
            SetRates(wakeFx, alive && sampPos != null ? 1f : 0f, true);
            SetRates(dmgSmoke, !alive ? 0f : (hr < 0.4f ? 30f : (hr < 0.75f ? 12f : 0f)), false);
            SetRates(dmgFire, !alive ? 0f : (hr < 0.25f ? 45f : (hr < 0.5f ? 20f : 0f)), false);
        }

        // 波でゆっくりゆれる
        if (model != null && bobAmount > 0f)
        {
            float t = Time.time;
            model.transform.localPosition = modelBasePos + Vector3.up * Mathf.Sin(t * 0.7f) * bobAmount;
            model.transform.localRotation = modelBaseRot * Quaternion.Euler(Mathf.Sin(t * 0.5f) * 2f, 0f, Mathf.Sin(t * 0.63f) * 3f);
        }

        if (target != null && target._IsDestroyed()) return;
        if (unarmed) return;   // 貨物船は撃たない

        // 自分が操縦しているドローンを探す
        controller mine = null;
        if (planes != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && p.isLocal) { mine = planes[i]; break; }
            }
        }
        if (mine == null || mine.car == null) return;
        if (!mine._IsHostile()) return;   // 警告の間は撃たない（攻撃開始になってから）

        // 追尾しないミサイル: 艦隊に近づいたら（飛んでいる時だけ）
        if (Time.time >= nextRocket && rocketLauncher != null)
        {
            Vector3 mpos = mine.car.transform.position;
            if (Vector3.Distance(mpos, rocketLauncher.position) <= rocketRange && mpos.y - seaLevel > 25f)
            {
                FireRocket(mine);
                nextRocket = Time.time + rocketInterval * Random.Range(0.8f, 1.3f);
            }
        }
        // 対空機銃: ハードモードの時だけ。1km 以内なら連射（撃つ・休むをくり返す）
        if (gameMode != null && gameMode._IsHard()) UpdateAA(mine, dt);

        if (Time.time < nextFire) return;

        Vector3 tp = mine.car.transform.position;
        Vector3 d = tp - transform.position;
        d.y = 0f;
        bool inRange = d.magnitude <= range && tp.y - seaLevel <= maxAltitude;
        if (Time.time >= nextLog)
        {
            nextLog = Time.time + 3f;
            Debug.Log("[WarShip] " + gameObject.name + " 距離 " + d.magnitude.ToString("F0") + "m 高さ " + (tp.y - seaLevel).ToString("F0") + "m → " + (inRange ? "砲撃" : "範囲外"));
        }
        if (!inRange) return;

        Fire(mine);
        Debug.Log("[WarShip] " + gameObject.name + " 発射");
        nextFire = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
    }

    void Fire(controller mine)
    {
        if (cannonMuzzles == null || cannonMuzzles.Length == 0 || shells == null) return;
        Vector3 tp = mine.car.transform.position;
        Rigidbody trb = mine.car.GetComponent<Rigidbody>();
        Vector3 tv = trb != null ? trb.velocity : Vector3.zero;

        // ドローンに一番近い砲門
        Transform muzzle = null;
        float best = float.MaxValue;
        for (int i = 0; i < cannonMuzzles.Length; i++)
        {
            if (cannonMuzzles[i] == null) continue;
            float dd = (cannonMuzzles[i].position - tp).sqrMagnitude;
            if (dd < best) { best = dd; muzzle = cannonMuzzles[i]; }
        }
        if (muzzle == null) return;

        // 空いている砲弾
        int slot = -1;
        for (int i = 0; i < shells.Length; i++) if (shells[i] != null && !sActive[i]) { slot = i; break; }
        if (slot < 0) return;

        // 先読み（届くまでの時間ぶん先 + 重力で落ちる分を上に）
        Vector3 mp = muzzle.position;
        float t = Vector3.Distance(mp, tp) / shellSpeed;
        Vector3 aim = tp + tv * t + Vector3.up * (0.5f * 9.81f * t * t);
        Vector3 dir = (aim - mp).normalized;
        dir = Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg), Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg), Vector3.Cross(dir, Vector3.up).normalized) * dir;

        sPos[slot] = mp;
        sVel[slot] = dir * shellSpeed;
        sAge[slot] = 0f;
        sActive[slot] = true;
        shells[slot].position = mp;
        shells[slot].gameObject.SetActive(true);
        TrailRenderer tr = shells[slot].GetComponent<TrailRenderer>();
        if (tr != null) tr.Clear();

        if (muzzleFx != null)
        {
            muzzleFx.transform.position = mp;
            muzzleFx.transform.rotation = Quaternion.LookRotation(dir);
            muzzleFx.Play(true);
        }
        if (cannonAudio != null && cannonAudio.clip != null)
        {
            cannonAudio.pitch = Random.Range(1.2f, 1.45f);
            cannonAudio.PlayOneShot(cannonAudio.clip, 0.8f);
        }
    }

    void UpdateShells(float dt)
    {
        if (sActive == null) return;
        controller mine = null;
        Vector3 tp = Vector3.zero;
        if (planes != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && p.isLocal) { mine = planes[i]; break; }
            }
        }
        if (mine != null && mine.car != null) tp = mine.car.transform.position;

        for (int i = 0; i < shells.Length; i++)
        {
            if (!sActive[i]) continue;
            sAge[i] += dt;
            Vector3 v = sVel[i] + Vector3.down * 9.81f * dt;
            Vector3 p0 = sPos[i];
            Vector3 p1 = p0 + v * dt;

            // ドローンの近くを通ったら空中で爆発
            if (mine != null && DistToSegment(tp, p0, p1) < hitRadius)
            {
                Explode(i, p1, false);
                mine._ExternalDamage(damage);
                continue;
            }
            // 海面に落ちたら水柱と爆発
            if (p1.y <= seaLevel)
            {
                Vector3 hp = Vector3.Lerp(p0, p1, Mathf.Clamp01((p0.y - seaLevel) / Mathf.Max(p0.y - p1.y, 0.0001f)));
                Explode(i, new Vector3(hp.x, seaLevel, hp.z), true);
                continue;
            }
            if (sAge[i] > 12f) { sActive[i] = false; shells[i].gameObject.SetActive(false); continue; }
            sPos[i] = p1;
            sVel[i] = v;
            shells[i].position = p1;
        }
    }

    // ---------------- 追尾しないミサイル ----------------
    void FireRocket(controller mine)
    {
        if (rockets == null || rActive == null) return;
        int slot = -1;
        for (int i = 0; i < rockets.Length; i++) if (rockets[i] != null && !rActive[i]) { slot = i; break; }
        if (slot < 0) return;
        Vector3 lp = rocketLauncher.position;
        Vector3 tp = mine.car.transform.position;
        Rigidbody trb = mine.car.GetComponent<Rigidbody>();
        Vector3 tv = trb != null ? trb.velocity : Vector3.zero;
        // 発射の瞬間に、届くころの位置を一度だけ予想して狙う（あとは曲がらない）
        float t = Vector3.Distance(lp, tp) / 220f;   // 平均の速さ（加速して 260 m/s）
        Vector3 aim = tp + tv * t;
        Vector3 dir = (aim - lp).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        dir = Quaternion.AngleAxis(Random.Range(-rocketSpread, rocketSpread), Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-rocketSpread, rocketSpread), side.normalized) * dir;
        rPos[slot] = lp;
        rVel[slot] = dir * rocketLaunchSpeed;
        rAge[slot] = 0f;
        rActive[slot] = true;
        Transform r = rockets[slot];
        r.position = lp;
        r.rotation = Quaternion.LookRotation(dir);
        r.gameObject.SetActive(true);
        TrailRenderer tr = r.GetComponent<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }
        Transform flame = r.Find("Flame");
        if (flame != null) flame.gameObject.SetActive(true);
        AudioSource au = r.GetComponent<AudioSource>();
        if (au != null && au.clip != null) au.PlayOneShot(au.clip, 1f);
        mine._MissileWarning(lp);   // 「Missile launch! N o'clock!」
    }

    void UpdateRockets(float dt)
    {
        if (rActive == null) return;
        controller mine = null;
        if (planes != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && p.isLocal) { mine = planes[i]; break; }
            }
        }
        Vector3 tp = (mine != null && mine.car != null) ? mine.car.transform.position : Vector3.zero;
        for (int i = 0; i < rockets.Length; i++)
        {
            if (!rActive[i]) continue;
            rAge[i] += dt;
            Vector3 v = rVel[i];
            float sp = v.magnitude;
            Vector3 dir = sp > 0.1f ? v / sp : rockets[i].forward;
            bool burning = rAge[i] < rocketBurnTime;
            if (burning && sp < rocketMaxSpeed) v += dir * rocketAccel * dt;
            if (!burning) v += Vector3.down * 9.81f * dt;   // 燃え尽きてから落ち始める
            Vector3 p0 = rPos[i];
            Vector3 p1 = p0 + v * dt;
            if (mine != null && DistToSegment(tp, p0, p1) < rocketHitRadius)
            {
                EndRocket(i, p1, false);
                mine._ExternalDamage(rocketDamage);
                continue;
            }
            // 建物・島・ほかの物に当たったら爆発（壊せる物ならダメージ）
            Vector3 seg = p1 - p0;
            float len = seg.magnitude;
            if (len > 0.001f)
            {
                RaycastHit[] hits = Physics.RaycastAll(p0, seg / len, len, groundMask, QueryTriggerInteraction.Ignore);
                int best = -1;
                float bestD = float.MaxValue;
                for (int k = 0; k < hits.Length; k++)
                {
                    Collider hc = hits[k].collider;
                    if (hc == null || hc.transform.IsChildOf(transform)) continue;   // 撃った船自身は除く
                    if (hits[k].distance < bestD) { bestD = hits[k].distance; best = k; }
                }
                if (best >= 0)
                {
                    Collider hc = hits[best].collider;
                    EndRocket(i, hits[best].point, false);
                    if (mine != null && mine.car != null && hc.transform.IsChildOf(mine.car.transform)) mine._ExternalDamage(rocketDamage);
                    else
                    {
                        FlightTarget ft = FindTarget(hc);
                        // 船どうしは壊さない（建物・標的・敵ドローンなどは壊れる）
                        if (ft != null && ft.GetComponent<WarShip>() == null) ft._HitMissile();
                    }
                    continue;
                }
            }
            if (p1.y <= seaLevel)
            {
                Vector3 hp = Vector3.Lerp(p0, p1, Mathf.Clamp01((p0.y - seaLevel) / Mathf.Max(p0.y - p1.y, 0.0001f)));
                EndRocket(i, new Vector3(hp.x, seaLevel, hp.z), true);
                continue;
            }
            if (rAge[i] > 14f) { EndRocket(i, p1, false); continue; }
            rPos[i] = p1;
            rVel[i] = v;
            rockets[i].position = p1;
            if (v.sqrMagnitude > 0.01f) rockets[i].rotation = Quaternion.LookRotation(v);
            if (!burning)
            {
                Transform flame = rockets[i].Find("Flame");
                if (flame != null && flame.gameObject.activeSelf) flame.gameObject.SetActive(false);
            }
        }
    }

    // ---------------- 対空機銃 ----------------
}
