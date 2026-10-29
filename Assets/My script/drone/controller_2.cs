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
    private float nextTakingFireVoice;
    private float nextLaunchVoice;
    private float nextMissileWarn;
    private int burnLevel;              // 0 = 無事, 1 = 煙, 2 = 炎, 3 = 激しい炎（全員の画面）
    private int sentBurnLevel;          // 操縦者が最後に知らせた段階
    private bool repairing;
    private float fuelKg = -1f, fuelCapKg, fuelFlow;
    private bool lowFuelSaid, emptySaid;
    private float rearmTimer;
    private Material damageTintMat;
    private Vector3 cockpitBasePos;
    private int flickerPanel = -1;
    private float flickerUntil;
    private Transform[] staticQuads;
    private int staticPanel = -1;
    private float staticUntil;
    private bool hasTakenOff;
    private bool gunnerMode;            // P キー: 操縦席に座ったまま砲塔を操作（自動操縦）
    // AMA（敵）が攻撃してくるか（全員の画面で同じ。撃墜されたら解除）
    private bool hostile;
    private int warnStage = -1;
    // ペラゴス基地（アイン・エメラルド中将）の警告: 0 = まだ, 1 = 警告中（10 秒以内に離脱）, 2 = 離脱した（2 度目の警告はない）
    public Transform baseCenter;
    public float baseWarnRadius = 3000f;
    public AudioClip[] ainWarn;            // 「こちら海軍、アイン・エメラルド中将である…直ちに離脱願う。2 度目の警告はない」
    public AudioClip[] ainThanks;          // 「感謝する」
    private int ainStage;
    private float ainDeadline;         // -1 = まだ見つかっていない, 0 = 放送, 1〜3 = 警告, 4 = 最後の通告
    private float nextWarnTime, lastDetectedTime, nextDetectScan, nextCrewFiredSend;
    private bool detected;
    private float armedAt = -1f;        // この時刻から警告・攻撃の仕組みが動く（射出の 3 秒後）
    private bool usingGunnerRadar;
    private bool gunSide;               // 左右の翼から交互に撃つ
    private Transform[] pDots, pMslDots;
    private Renderer[] pDotR, pMslR;
    private Transform pSweep, pRose;
    private int stallState, sentStallState;   // 0 = 普通, 1 = 失速しそう, 2 = 失速（後ろの席にも鳴らす）
    private float nextAimRay;
    private Vector3 aimPoint;
    // 船に見つかっている（ハードモードでは戦闘機が遠くからでも向かってくる。全員の画面で同じ）
    private bool spotted, sentSpotted;
    private float lastShipSeen, nextSpotScan;
    private float apTargetRoll;         // 自動操縦: 今の目標の傾き（0 = まっすぐ、+ = 右旋回）
    private float apNextChange;           // 離陸するまでは敵ドローンの方向を知らせない
    private FlightTarget lastHitFt;     // 最後に自分が当てた標的（壊れたら知らせる）
    private float lastHitTime;
    private Vector3[] radarPrev;
    private bool[] radarSeen;

    // ---- 機銃 ----
    private bool fireInput, localFiring, remoteFiring;
    private float fireTimer;
    private int ammo;

    void Start()
    {
        station = GetComponent<VRCStation>();
        localPlayer = Networking.LocalPlayer;
        isVR = localPlayer != null && localPlayer.IsUserInVR();
        noiseSeed = Random.Range(0f, 100f);
        // Player / PlayerLocal / MirrorReflection を地面判定から除外
        groundMask = ~((1 << 9) | (1 << 10) | (1 << 18));
        ammo = maxAmmo;
        hp = maxHp;
        missileLeft = maxMissiles; guidedLeft = maxGuided; flares = maxFlares;
        int mn = missiles != null ? missiles.Length : 0;
        mPos = new Vector3[mn];
        mVel = new Vector3[mn];
        mAge = new float[mn];
        mActive = new bool[mn];
        mReal = new bool[mn];
        mBurning = new bool[mn];
        mTgt = new int[mn];
        mTgtPrev = new Vector3[mn];
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (oceanMaterial != null)
        {
            waveA = oceanMaterial.GetVector("_WaveA");
            waveB = oceanMaterial.GetVector("_WaveB");
            waveC = oceanMaterial.GetVector("_WaveC");
            waveScale = oceanMaterial.GetFloat("_WaveScale");
            haveWaves = true;
        }
        if (gunSight != null) gunSight.SetActive(false);
        // 照準の「＋」は目の正面（コックピットの基準）60m 先に置く。弾はそこを通る線上に集まる
        if (gunSight != null && cockpitRoot != null)
        {
            gunSight.transform.SetParent(cockpitRoot.transform, false);
            gunSight.transform.localPosition = new Vector3(0f, 0f, 60f);
            gunSight.transform.localRotation = Quaternion.identity;
        }
        if (cockpitRoot != null)
        {
            cockpitRoot.SetActive(false);
            // 計器だけのモード（H キー）で隠す、緑の文字の表示（Omega のみ）
            if (cockpitRoot.transform.Find("InstrumentPanel") != null)
            {
                string[] gn = { "MainPanel", "LeftPanel", "RightPanel", "WeaponPanel", "LogPanel", "RadarPanel" };
                greenPanels = new GameObject[gn.Length];
                for (int gi = 0; gi < gn.Length; gi++)
                {
                    Transform gt = cockpitRoot.transform.Find(gn[gi]);
                    greenPanels[gi] = gt != null ? gt.gameObject : null;
                }
            }
            bootPanels = new Transform[7];
            bootPanels[0] = cockpitRoot.transform.Find("MainPanel");
            bootPanels[1] = cockpitRoot.transform.Find("LeftPanel");
            bootPanels[2] = cockpitRoot.transform.Find("RightPanel");
            bootPanels[3] = cockpitRoot.transform.Find("CompassPanel");
            bootPanels[4] = cockpitRoot.transform.Find("RadarPanel");
            bootPanels[5] = cockpitRoot.transform.Find("WeaponPanel");
            bootPanels[6] = cockpitRoot.transform.Find("LogPanel");
            bootDelays = new float[7];
            // 真っ暗な状態から、中央 → 左 → 右 → 方位 → レーダーの順に点灯
            bootDelays[0] = 0.8f; bootDelays[1] = 1.5f; bootDelays[2] = 1.7f; bootDelays[3] = 2.0f; bootDelays[4] = 2.2f; bootDelays[5] = 1.3f; bootDelays[6] = 1.9f;
            if (blackout != null) { blackoutMat = blackout.material; blackout.enabled = false; }
            if (damageTint != null) { damageTintMat = damageTint.material; damageTint.enabled = false; }
            cockpitBasePos = cockpitRoot.transform.localPosition;
            staticQuads = new Transform[bootPanels.Length];
            for (int i = 0; i < bootPanels.Length; i++)
            {
                if (bootPanels[i] == null) continue;
                staticQuads[i] = bootPanels[i].Find("Static");
                if (staticQuads[i] != null) staticQuads[i].gameObject.SetActive(false);
            }
        }

        if (car == null)
        {
            Debug.LogError("[controller] car が未設定です");
            return;
        }
        rb = car.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("[controller] car に Rigidbody がありません");
            return;
        }
        rb.mass = mass;
        rb.drag = 0f;
        rb.angularDrag = 0.05f;
        rb.useGravity = false;   // 重力はこのスクリプトで計算する
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // ドローン用の重力スクリプトが付いていると二重に重力がかかるので止める
        ajsutGravity g = car.GetComponent<ajsutGravity>();
        if (g != null) g.enabled = false;

        // 機体コライダーの摩擦をゼロにする（地面との摩擦はスクリプトで計算）
        // ※摩擦があると球コライダーがボールのように転がって機体が回ってしまう
        Collider[] cols = car.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || cols[i].isTrigger) continue;
            PhysicMaterial pm = cols[i].material;   // 個別インスタンスが作られる
            if (pm == null) continue;
            pm.dynamicFriction = 0f;
            pm.staticFriction = 0f;
            pm.frictionCombine = PhysicMaterialCombine.Minimum;
            pm.bounciness = 0f;
        }
    }

    // ---------------- 入力・表示 ----------------
    void Update()
    {
        float dt = Time.deltaTime;
        // HP に合わせて燃え方を全員に知らせる（操縦者の画面で HP を持っているので）
        if (isPiloting)
        {
            // 出発地点の近く（repairRadius 以内）にいると少しずつ修理される
            repairing = false;
            if (car != null && !crashed && hp < maxHp
                && ((respawnPoint != null && Vector3.Distance(car.transform.position, respawnPoint.position) < repairRadius) || AtAstralPad() || AtFriendlyPad()))
            {
                hp = Mathf.Min(maxHp, hp + repairRate * dt);
                repairing = true;
            }
            // 燃料も補給
            if (car != null && !crashed && _NeedsFuel()
                && ((respawnPoint != null && Vector3.Distance(car.transform.position, respawnPoint.position) < repairRadius) || AtAstralPad() || AtFriendlyPad()))
            {
                Refuel(dt);
                repairing = true;
            }
            // 弾とミサイルも少しずつ補充（1 秒ごとに 機銃 60 発・無誘導 2 発・ロックオン 1 発）
            if (car != null && !crashed
                && ((respawnPoint != null && Vector3.Distance(car.transform.position, respawnPoint.position) < repairRadius) || AtAstralPad() || AtFriendlyPad())
                && (ammo < maxAmmo || missileLeft < maxMissiles || guidedLeft < maxGuided))
            {
                repairing = true;
                rearmTimer += dt;
                if (rearmTimer >= 1f)
                {
                    rearmTimer = 0f;
                    ammo = Mathf.Min(maxAmmo, ammo + 60);
                    missileLeft = Mathf.Min(maxMissiles, missileLeft + 2);
                    guidedLeft = Mathf.Min(maxGuided, guidedLeft + 1);
                    flares = Mathf.Min(maxFlares, flares + 4);
                }
            }
            int lv = hp < 25f ? 3 : (hp < 50f ? 2 : (hp < 75f ? 1 : 0));
            if (lv != sentBurnLevel)
            {
                sentBurnLevel = lv;
                if (lv == 0) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn0));
                else if (lv == 1) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn1));
                else if (lv == 2) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn2));
                else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn3));
            }
        }

        UpdateKills();

        // 後ろの席に座っている自分: 操縦席の背中のレーダーを動かす
        if (!isPiloting && usingGunnerRadar) UpdateRadar();

        // 照準: 「＋」の先（目の位置から機首方向）に弾とミサイルが集まるよう、銃口をそこへ向ける
        if (isPiloting && !gunnerMode && gunMuzzle != null && cockpitRoot != null) AimGun();

        if (isPiloting)
        {
            // P: ガンナーモード（2 人目の席が空いている時だけ）
            if (!isVR && Input.GetKeyDown(KeyCode.P) && gunner != null && bootDone)
            {
                if (gunnerMode) SetGunnerMode(false);
                else if (gunner._GetGunner() == null) SetGunnerMode(true);
            }
            // 誰かが 2 人目の席に座ったら、ガンナーはその人に任せる
            if (gunnerMode && gunner != null && gunner._GetGunner() != null) SetGunnerMode(false);
            if (!gunnerMode) UpdateWeapons(dt);
            UpdateVoice();
            UpdateHostility();
            UpdateAstral();
            UpdateSight();
            UpdateSpotted();
            UpdateCarrierWarning();
        }
        UpdateGun(dt);
        UpdateBetray();
        UpdateEngineSound();
        UpdateMissiles(dt);
        UpdateGear(dt);
        if (!isPiloting) return;

        if (isVR)
        {
            // ---- SaccFlight 風の VR 操作 ----
            VRCPlayerApi lp = Networking.LocalPlayer;
            Transform refT = cockpitRoot != null ? cockpitRoot.transform : car.transform;
            Quaternion refInv = Quaternion.Inverse(refT.rotation);
            float stickYaw = Mathf.Abs(vrLookX) > 0.15f ? vrLookX : 0f;
            // 右グリップ = 操縦桿: 握った時の手の向きから、前後に傾ける = 機首の上げ下げ、左右に傾ける = 横転、ひねる = ヨー
            if (vrGrip && lp != null)
            {
                Quaternion rel = refInv * lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation;
                if (!vrStickHeld) { vrStickHeld = true; vrStickRef = rel; }
                Quaternion dq = rel * Quaternion.Inverse(vrStickRef);
                Vector3 hf = dq * Vector3.forward, hu = dq * Vector3.up;
                inPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(hf.y, -1f, 1f)) * Mathf.Rad2Deg / vrStickMaxDeg, -1f, 1f);
                inRoll = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(hu.x, -1f, 1f)) * Mathf.Rad2Deg / vrStickMaxDeg, -1f, 1f);
                float tw = Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg / vrTwistMaxDeg;
                if (Mathf.Abs(tw) < 0.15f) tw = 0f;
                inYaw = Mathf.Clamp(tw + stickYaw, -1f, 1f);
            }
            else
            {
                vrStickHeld = false;
                inPitch = 0f; inRoll = 0f;
                inYaw = stickYaw;
            }
            // 左グリップ = スロットルレバー: 握ったまま手を前に出す = 上げる、引く = 下げる
            inThrottleCmd = 0f;
            if (vrLeftGrip && lp != null)
            {
                Vector3 hp2 = refInv * (lp.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position - refT.position);
                if (!vrLeverHeld) { vrLeverHeld = true; vrLeverRefZ = hp2.z; vrLeverRefThr = throttle; }
                throttle = Mathf.Clamp01(vrLeverRefThr + (hp2.z - vrLeverRefZ) / vrLeverTravel);
            }
            else
            {
                vrLeverHeld = false;
                inThrottleCmd = Mathf.Abs(vrLookY) > 0.25f ? vrLookY : 0f;   // 右スティックの上下でも動かせる
            }
            // 左スティック: 下 = ブレーキ / 上 = 短く倒すとフラップ、1 秒倒し続けるとブースト / 左 = 計器だけのモード / 右 = フレア（後で）
            braking = vrLsY < -0.5f;
            bool up = vrLsY > 0.6f;
            if (up && !vrLsUpPrev) { vrLsUpStart = Time.time; vrBoostDone = false; }
            if (up && !vrBoostDone && Time.time - vrLsUpStart >= 1f)
            {
                vrBoostDone = true;
                if (boostTimer < 0f) boostTimer = 0f;
            }
            if (!up && vrLsUpPrev && !vrBoostDone && Time.time - vrLsUpStart < 0.6f) flapsDown = !flapsDown;
            vrLsUpPrev = up;
            // 左: 短く倒す = 脚の出し入れ、1 秒倒し続ける = 計器だけのモード
            bool lft = vrLsX < -0.6f;
            if (lft && !vrLsLeftPrev) { vrLsLeftStart = Time.time; vrLsLeftDone = false; }
            if (lft && !vrLsLeftDone && Time.time - vrLsLeftStart >= 1f) { vrLsLeftDone = true; ToggleGaugeOnly(); }
            if (!lft && vrLsLeftPrev && !vrLsLeftDone && Time.time - vrLsLeftStart < 0.6f) ToggleGear();
            vrLsLeftPrev = lft;
            boosting = false;
        }
        else
        {
            inThrottleCmd = 0f;
            if (Input.GetKey(KeyCode.LeftShift)) inThrottleCmd += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) inThrottleCmd -= 1f;
            inYaw = 0f;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.R)) inYaw += 1f;
            if (Input.GetKey(KeyCode.Q)) inYaw -= 1f;
            braking = Input.GetKey(KeyCode.B);
            // T を 1 回押すと自動で「上昇 → 前へ加速」（使っている間は押し直しても無視）
            if (Input.GetKeyDown(KeyCode.T) && boostTimer < 0f) boostTimer = 0f;
            if (Input.GetKeyDown(KeyCode.F)) flapsDown = !flapsDown;
        }
        throttle = Mathf.Clamp01(throttle + inThrottleCmd * throttleRate * dt);
        if (fuelKg == 0f) throttle = 0f;   // 燃料切れ: エンジンが止まる

        UpdateHud();
        UpdateAudio();
    }

    // ---------------- 物理 ----------------
    void FixedUpdate()
    {
        if (rb == null) return;
        // 物理計算は機体のオーナーだけが行う（他の人は ObjectSync で同期）
        if (!Networking.IsOwner(car)) { velValid = false; return; }
        if (rb.isKinematic) { velValid = false; return; }
        if (crashed)
        {
            if (sinking)
            {
                // ゆっくり機首から沈んでいく
                rb.velocity = Vector3.down * sinkSpeed;
                Vector3 rt = axisReference != null ? axisReference.right : car.transform.forward;
                rb.angularVelocity = rt * 0.08f;
            }
            else
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            velValid = false;
            return;
        }

        // 衝突の検出: 前の物理ステップの速度から急に変わっていたら、何かにぶつかった
        // （自分でかけている力では 1 ステップに 1m/s も変わらない）
        if (velValid && isPiloting)
        {
            float dv = (rb.velocity - lastVel).magnitude;
            if (dv > impactThreshold) Damage((dv - impactThreshold) * impactDamagePerMS);
        }
        if (crashed) return;

        Simulate(Time.fixedDeltaTime);
        lastVel = rb.velocity;
        velValid = !crashed;
    }

    // ---------------- HP ----------------
    // 船の大砲など、外からのダメージ（操縦している本人の画面から呼ぶ）
    // 敵の戦闘機のミサイルが当たった（狙われた本人の画面で判定）
    public void _EnemyMissileHit(float amount)
    {
        if (!isPiloting) return;
        Damage(amount);
        TakingFireVoice(0);
    }

    public void _ExternalDamage(float amount)
    {
        if (!isPiloting) return;
        Damage(amount);
        TakingFireVoice(1);   // 船の大砲
    }

    void Damage(float amount)
    {
        if (crashed || amount <= 0f) return;
        hp -= amount;
        lastDamage = amount;
        lastDamageTime = Time.time;
        if (hp <= 0f)
        {
            hp = 0f;
            Crash();
        }
    }
}
