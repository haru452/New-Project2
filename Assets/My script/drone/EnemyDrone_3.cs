using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// EnemyDrone.cs の続き（長いので分けたファイル。中身は同じクラス EnemyDrone）
public partial class EnemyDrone
{
    void ParkNow()
    {
        lSmooth = Vector3.zero;
        cState = 0; cTimer = 0f; cNoPlayer = 0f;
        RequestSerialization();
        chasing = null; cachedPrey = null;
        SetFiring(false);
        gearForceDown = true;
        Vector3 p = carrierRef.TransformPoint(parkLocal);
        Quaternion r = carrierRef.rotation * parkRotLocal;
        rb.isKinematic = true;
        rb.position = p;
        rb.rotation = r;
        rb.transform.SetPositionAndRotation(p, r);
    }

    // 空母から一番近い、飛んでいるプレイヤーまでの距離（1 秒ごと）
    void CarrierScan()
    {
        if (Time.time < nextCarrierCheck) return;
        nextCarrierCheck = Time.time + 1f;
        cNearest = 1e9f;
        if (planes == null) return;
        Vector3 cp = carrierRef.position;
        bool q = gameMode != null && (gameMode._Quiet() || (baseAirfield && gameMode._IsSightseeing()));
        for (int i = 0; i < planes.Length; i++)
        {
            controller c = planes[i];
            if (c == null || c.car == null || q) continue;
            VRCPlayerApi p = c._GetPilot();
            if (p == null || !Utilities.IsValid(p) || c._InBriefing()) continue;
            if (c.car.transform.position.y - seaLevel < takeoffAltitude) continue;   // 離陸前は数えない
            Vector3 d = c.car.transform.position - cp;
            d.y = 0f;
            cNearest = Mathf.Min(cNearest, d.magnitude);
        }
    }

    // 待機・発艦（true = この中で動かしたので、普通の飛び方はしない）
    bool CarrierStep(float dt)
    {
        CarrierScan();
        if (cState == 0)
        {
            // 待機: 甲板に止まったまま。プレイヤーが近づいたら、順番に発艦
            Vector3 p = carrierRef.TransformPoint(parkLocal);
            if (!rb.isKinematic) ParkNow();
            else { rb.MovePosition(p); rb.MoveRotation(carrierRef.rotation * parkRotLocal); }   // 動いている空母の甲板に乗ったまま
            bool present = carrierShip == null || carrierShip._Present();
            if (cNearest < carrierWarnRange && present) cTimer += dt; else cTimer = 0f;
            if (cTimer >= launchDelay)
            {
                cState = 1; cTimer = 0f;
                RequestSerialization();
                Vector3 sp = carrierRef.TransformPoint(deckStartLocal);
                Vector3 dd = carrierRef.TransformDirection(deckDirLocal);
                Quaternion r = LookFor(dd, Vector3.up);
                rb.position = sp; rb.rotation = r;
                rb.transform.SetPositionAndRotation(sp, r);
                rb.isKinematic = false;
                rb.velocity = Vector3.zero;
            }
            return true;
        }
        if (cState == 1)
        {
            // 発艦: 甲板の上をまっすぐ加速して、端で飛び立つ
            cTimer += dt;
            Vector3 dd = carrierRef.TransformDirection(deckDirLocal);
            Vector3 sp = carrierRef.TransformPoint(deckStartLocal);
            float run = Vector3.Dot(rb.position - sp, dd);
            float acc0 = carrierShip != null ? Mathf.Max(takeoffAcc, 26f) : takeoffAcc;   // 空母はカタパルトで押す
            float spd = 4f + cTimer * acc0;
            float vr = stallSpeed * 1.25f;   // 機首を上げる速さ
            if (run < deckRun && spd < vr)
            {
                rb.velocity = dd * spd;
                rb.MoveRotation(LookFor(dd, Vector3.up));
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                cState = 2; cTimer = 0f; cNoPlayer = 0f;
                RequestSerialization();
                gearForceDown = false;
                hasHome = true;
                home = carrierRef.position;
                waypoint = home + dd * 1500f + Vector3.up * 200f;
                nextWaypointTime = Time.time + 20f;
                // 浮く速さに届いていれば、機首を少し上げて飛び立つ（足りなければ、そのまま押し出す）
                float lv = Mathf.Max(spd, vr);
                rb.velocity = (dd + Vector3.up * 0.12f).normalized * lv;
                rb.MoveRotation(LookFor((dd + Vector3.up * 0.15f).normalized, Vector3.up));
            }
            return true;
        }
        // 飛行中: 範囲の中に誰もいない状態が 15 秒続いたら着艦に向かう
        if (cState == 2)
        {
            if (cNearest > carrierRange) cNoPlayer += dt; else cNoPlayer = 0f;
            if (cNoPlayer > 15f) { cState = 3; RequestSerialization(); }
        }
        else if (cState == 3 && cNearest < carrierWarnRange) { cState = 2; cNoPlayer = 0f; gearForceDown = false; RequestSerialization(); }
        return false;
    }
    public bool _Alive() { return rb != null && rb.gameObject.activeInHierarchy && (target == null || !target._IsDestroyed()); }
    public Vector3 _Pos() { return rb != null ? rb.position : transform.position; }
    public Vector3 _Vel() { return rb != null ? rb.velocity : Vector3.zero; }

    // 編隊での自分の位置（警戒機の進む向きから見て）
    Vector3 FormationSlot()
    {
        Vector3 lv = formationLeader._Vel();
        lv.y = 0f;
        Vector3 lf = lv.sqrMagnitude > 1f ? lv.normalized : Vector3.forward;
        Vector3 lr = Vector3.Cross(Vector3.up, lf);
        return formationLeader._Pos() + lr * formationOffset.x + Vector3.up * formationOffset.y + lf * formationOffset.z;
    }
    bool InFormation() { return formationLeader != null && formationLeader._Alive(); }

    // 警戒機: 広い範囲でプレイヤーを探して、見つけたら知らせる（持ち主だけ）
    void AwacsScan(Vector3 pos)
    {
        if (planes == null) return;
        int best = -1;
        float bd = detectRange;
        for (int i = 0; i < planes.Length; i++)
        {
            controller c = planes[i];
            if (c == null || c.car == null) continue;
            VRCPlayerApi p = c._GetPilot();
            if (p == null || !Utilities.IsValid(p)) continue;
            if (c.car.transform.position.y - seaLevel < takeoffAltitude) continue;   // 離陸前は見ない
            if (c._InBriefing()) continue;   // 偵察任務の無線が流れている間は見ない
            float d = Vector3.Distance(pos, c.car.transform.position);
            if (d < bd) { bd = d; best = i; }
        }
        if (best >= 0)
        {
            alertLastSeen = Time.time;
            if (alertPlane != best) { alertPlane = best; RequestSerialization(); }
            // ハードモード: 全部の戦闘機に知らせる（船に見つかった時と同じ扱い）
            if (gameMode != null && gameMode._IsHard() && Time.time >= nextSpotSend)
            {
                nextSpotSend = Time.time + 2f;
                planes[best].SendCustomNetworkEvent(NetworkEventTarget.Owner, "NetAwacsSpot");
            }
        }
        else if (alertPlane >= 0 && Time.time - alertLastSeen > 10f)
        {
            alertPlane = -1;
            RequestSerialization();
        }
    }

    void SetRate(ParticleSystem ps, float r)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTimeMultiplier = r;
        if (r > 0f && !ps.isPlaying) ps.Play();
    }

    void SetFiring(bool f)
    {
        if (firing == f) return;
        firing = f;
        RequestSerialization();
    }

    private float nextDmgCheck;

    // 他の人の画面: 新しいミサイルが撃たれたら、同じように飛ばす
    public override void OnDeserialization()
    {
        if (missileSeq != seenMissileSeq)
        {
            bool first = seenMissileSeq == 0 && !mActive && Time.timeSinceLevelLoad < 5f;
            seenMissileSeq = missileSeq;
            if (!first && missileTarget >= 0) LaunchMissile(missileTarget);
        }
    }

    // レーダー表示用
    public bool _MslActive() { return mActive; }
    public Vector3 _MslPos() { return mPos; }
    public bool _MslGuided() { return Guided(); }

    void LaunchMissile(int idx)
    {
        if (missile == null || planes == null || idx < 0 || idx >= planes.Length) return;
        Transform from = muzzle != null ? muzzle : rb.transform;
        mPos = from.position + Forward() * 4f - rb.transform.up * 1.5f;
        mVel = Forward() * (speed + 25f);
        mAge = 0f;
        mTarget = idx;
        mActive = true;
        mDecoy = false;
        mFlareSeen = Time.time;
        missile.position = mPos;
        missile.rotation = Quaternion.LookRotation(mVel);
        missile.gameObject.SetActive(true);
        TrailRenderer tr = missile.GetComponent<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }
        Transform flame = missile.Find("Flame");
        if (flame != null) flame.gameObject.SetActive(true);
        AudioSource au = missile.GetComponent<AudioSource>();
        if (au != null && au.clip != null) au.PlayOneShot(au.clip, 1f);
        // 狙われた本人に「Missile launch! N o'clock!」（追尾ミサイルなら「誘導ミサイル接近」）
        controller tgt = planes[idx];
        if (tgt != null)
        {
            if (Guided()) tgt._GuidedMissileWarning();
            else tgt._MissileWarning(mPos);
        }
    }

    private bool mDecoy;
    private Vector3 mDecoyPos;
    private float mFlareSeen;
    // 追尾ミサイルか（空母の戦闘機 / 遊覧モードの裏切りの後は全機）
    bool Guided() { return guidedMissile || (gameMode != null && gameMode._Betrayed()); }

    void UpdateMissile(float dt)
    {
        if (!mActive) return;
        mAge += dt;
        controller tgt = (planes != null && mTarget >= 0 && mTarget < planes.Length) ? planes[mTarget] : null;
        Vector3 v = mVel;
        float sp = v.magnitude;
        bool burning = mAge < missileBurn;
        // フレア: 狙われた機体がフレアをまいたら、75% でそちらへそれる（もう狙わない）
        if (tgt != null && !mDecoy && tgt._FlareTime() > mFlareSeen)
        {
            mFlareSeen = tgt._FlareTime();
            if (Random.value < 0.75f) { mDecoy = true; mDecoyPos = tgt._FlarePos(); }
        }
        if (mDecoy) tgt = null;
        float turn = Guided() ? Mathf.Max(missileTurnRate, 40f) : missileTurnRate;
        if (tgt != null && tgt.car != null && burning && sp > 1f)
        {
            Vector3 want = tgt.car.transform.position - mPos;
            v = Vector3.RotateTowards(v, want.normalized * sp, turn * Mathf.Deg2Rad * dt, 0f);
        }
        else if (mDecoy && burning && sp > 1f)
        {
            v = Vector3.RotateTowards(v, (mDecoyPos - mPos).normalized * sp, turn * 0.5f * Mathf.Deg2Rad * dt, 0f);
        }
        if (burning && sp < missileMaxSpeed) v += v.normalized * missileAccel * dt;
        v += Vector3.down * 9.81f * (burning ? 0.1f : 1f) * dt;
        Vector3 p0 = mPos;
        Vector3 p1 = p0 + v * dt;
        // 近くを通ったら爆発（ダメージは狙われた本人の画面で）
        if (tgt != null && tgt.car != null)
        {
            Vector3 tp = tgt.car.transform.position;
            Vector3 ab = p1 - p0;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(tp - p0, ab) / l2) : 0f;
            if (Vector3.Distance(tp, p0 + ab * t) < 10f)
            {
                EndMissile(p0 + ab * t, false);
                if (tgt._IsPilotLocal()) tgt._EnemyMissileHit(missileDamage);
                return;
            }
        }
        if (p1.y <= seaLevel) { EndMissile(new Vector3(p1.x, seaLevel, p1.z), true); return; }
        if (mAge > 14f) { EndMissile(p1, false); return; }
        mPos = p1;
        mVel = v;
        missile.position = p1;
        if (v.sqrMagnitude > 0.01f) missile.rotation = Quaternion.LookRotation(v);
        if (!burning)
        {
            Transform flame = missile.Find("Flame");
            if (flame != null && flame.gameObject.activeSelf) flame.gameObject.SetActive(false);
        }
    }

    void EndMissile(Vector3 p, bool water)
    {
        mActive = false;
        missile.gameObject.SetActive(false);
        if (water && missileSplash != null) { missileSplash.transform.position = p; missileSplash.Emit(50); }
        if (missileBoom != null) { missileBoom.transform.position = p; missileBoom.Play(true); }
        if (missileBoomAudio != null) { missileBoomAudio.transform.position = p; missileBoomAudio.Play(); }
    }

    // 脚: 0.5 秒ごとに真下を調べて、出す・しまうを決める（全員の画面で同じように動く）
    void UpdateGear(float dt)
    {
        if (gearPivots == null || gearPivots.Length == 0 || rb == null) return;
        if (Time.time >= nextGearCheck)
        {
            nextGearCheck = Time.time + 0.5f;
            bool down = gearForceDown;
            if (!down && rb.gameObject.activeInHierarchy && (target == null || !target._IsDestroyed()))
            {
                int mask = ~((1 << 9) | (1 << 10) | (1 << 18));   // プレイヤー・ミラーは除く
                RaycastHit hit;
                if (Physics.Raycast(rb.position, Vector3.down, out hit, gearDownHeight, mask, QueryTriggerInteraction.Ignore)
                    && hit.point.y > seaLevel + 0.5f)
                    down = true;   // 海の上ではなく、地面・甲板の近く
            }
            gearWant = down;
        }
        float want = gearWant ? 1f : 0f;
        if (gearAnim != want) gearAnim = Mathf.MoveTowards(gearAnim, want, dt / Mathf.Max(gearTime, 0.1f));
        bool vis = (gearWant || gearAnim > 0f) && (target == null || (!target._IsDestroyed() && !target._Culled()));   // しまい切ったら脚ごと非表示。撃墜された時・まだ出ていない時も非表示
        if (gearRoot != null && gearRoot.activeSelf != vis) gearRoot.SetActive(vis);
        if (gearAnim == gearAnimShown) return;
        gearAnimShown = gearAnim;
        float k = gearAnim * gearAnim * (3f - 2f * gearAnim);
        for (int i = 0; i < gearPivots.Length; i++)
        {
            if (gearPivots[i] == null) continue;
            Vector3 fe = (gearFold != null && i < gearFold.Length) ? gearFold[i] : new Vector3(-90f, 0f, 0f);
            Quaternion br = (gearBaseRot != null && i < gearBaseRot.Length) ? gearBaseRot[i] : Quaternion.identity;
            gearPivots[i].localRotation = br * Quaternion.Euler(fe * (1f - k));
        }
        if (gearVisuals != null)
            for (int i = 0; i < gearVisuals.Length; i++)
                if (gearVisuals[i] != null && gearVisuals[i].activeSelf != vis) gearVisuals[i].SetActive(vis);
    }

    // 曳光弾と音（全員の画面）
    void Update()
    {
        UpdateMissile(Time.deltaTime);
        UpdateGear(Time.deltaTime);
        // 空母がまだ出てきていない間は、甲板の戦闘機も見えない（全員の画面）
        // 空母が沈んだ・まだ出ていない → 甲板の上の戦闘機も消える（飛んでいる機体はそのまま飛び続ける）
        // 空母が遠くて見た目が消えている → 甲板の上の戦闘機も見た目だけ消す
        if (carrierBased && carrierShip != null && target != null)
        {
            bool onDeck = cState == 0 || cState == 1;
            bool cvPresent = carrierShip._Present();
            bool cvSunk = carrierShip.target != null && carrierShip.target._IsDestroyed();
            target._SetHidden(!cvPresent || (onDeck && cvSunk));
            target._SetCulled(onDeck && !carrierShip._Shown());
        }
        // ダメージの煙と炎（0.5 秒ごと）
        if (Time.time >= nextDmgCheck && target != null)
        {
            nextDmgCheck = Time.time + 0.5f;
            float hr = target._HpRatio();
            SetRate(dmgSmoke, hr <= 0f ? 0f : (hr < 0.4f ? 35f : (hr < 0.75f ? 14f : 0f)));
            SetRate(dmgFire, hr <= 0f ? 0f : (hr < 0.4f ? 40f : 0f));
        }
        if (!firing) { fireTimer = 0f; return; }
        if (target != null && target._IsDestroyed()) return;
        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f) return;
        fireTimer = 1f / Mathf.Max(fireRate, 1f);
        if (tracer != null) tracer.Emit(1);
        if (gunAudio != null && gunAudio.clip != null)
        {
            gunAudio.pitch = Random.Range(1.0f, 1.15f);
            gunAudio.PlayOneShot(gunAudio.clip, 0.6f);
        }
    }
}
