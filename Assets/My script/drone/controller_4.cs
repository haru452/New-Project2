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
    public void NetCrash()
    {
        SetBurn(0);   // 格納庫に戻ると修理済み
        sentBurnLevel = 0;
        if (gunner != null) gunner._OnCrash();   // ガンナーも一緒に格納庫へ
        Vector3 p = car.transform.position;
        if (Time.time - lastSinkTime < 1f) return;   // 海に落ちた時は爆発せず、水しぶきだけ
        if (crashFx != null)
        {
            crashFx.transform.position = p;
            crashFx.Play(true);
        }
        if (crashAudio != null)
        {
            crashAudio.transform.position = p;
            crashAudio.Play();
        }
    }

    public void _TeleportPilotHome()
    {
        if (respawnPoint == null) return;
        Networking.LocalPlayer.TeleportTo(respawnPoint.position + Vector3.up * 1f, respawnPoint.rotation);
    }

    public void _AfterCrash()
    {
        crashed = false;
        sinking = false;
        floating = false;
        hp = maxHp;   // 格納庫に戻ったら修理済み
        fuelKg = -1f;  // 燃料も満タン（次に使う時に満たす）
        missileLeft = maxMissiles; guidedLeft = maxGuided; flares = maxFlares;
        ammo = maxAmmo;
        velValid = false;
        if (!Networking.IsOwner(car)) return;
        throttle = 0f;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (homePool != null)
        {
            // 格納庫のプールに戻す（また「出す」ボタンで出せる）
            Networking.SetOwner(Networking.LocalPlayer, homePool.gameObject);
            homePool.Return(car);
        }
        else if (respawnPoint != null)
        {
            gearDown = true; gearAnim = 1f; ApplyGear();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetGearDown));
            rb.position = respawnPoint.position + Vector3.up * (1.5f + _GearLift());
            car.transform.position = rb.position;
        }
    }

    // ---------------- ミサイル ----------------
    // ---------------- 武器の選択・発射 ----------------
    void UpdateWeapons(float dt)
    {
        if (!isVR)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) weapon = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) weapon = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) weapon = 2;
        }
        else
        {
            // 左トリガー: 短く押す（離した時）と武器の切り替え、1 秒長押しで音声案内のオン/オフ
            if (cycleInput && !cyclePrev) { cycleStart = Time.time; voiceToggleDone = false; }
            if (cycleInput && !voiceToggleDone && Time.time - cycleStart >= 1f) { voiceToggleDone = true; ToggleVoice(); }
            if (!cycleInput && cyclePrev && !voiceToggleDone && Time.time - cycleStart < 0.6f) weapon = (weapon + 1) % 3;
            cyclePrev = cycleInput;
        }
        bool trig = useHeld || (!isVR && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)));
        bool down = trig && !trigPrev;
        trigPrev = trig;
        fireInput = weapon == 0 && trig;
        if (down && weapon == 1) TryFireMissile(false);
        if (down && weapon == 2) TryFireMissile(true);
        UpdateLock(dt);
    }

    // ---------------- 燃料 ----------------
    float FuelFlow(float thr, float v, float sigma)
    {
        float ratio = Mathf.Clamp(v / Mathf.Max(fuelRefSpeed, 1f), 0f, 1.5f);
        float p = enginePowerMax * (idlePowerFrac + (1f - idlePowerFrac) * thr) * sigma * (0.55f + 0.45f * ratio);
        return bsfc * p / 3600f;   // kg/s
    }

    void InitFuel()
    {
        fuelCapKg = FuelFlow(1f, fuelRefSpeed, 1f) * fullThrottleMinutes * 60f;
        fuelKg = fuelCapKg;
        lowFuelSaid = false;
        emptySaid = false;
    }

    void BurnFuel(float dt, float sigma)
    {
        if (fuelKg < 0f) InitFuel();
        if (fuelKg <= 0f) { fuelFlow = 0f; return; }
        fuelFlow = FuelFlow(throttle, airspeed, sigma);
        if (boosting) fuelFlow += FuelFlow(1f, fuelRefSpeed, 1f) * 4f;
        fuelKg = Mathf.Max(fuelKg - fuelFlow * dt, 0f);
        float frac = fuelKg / Mathf.Max(fuelCapKg, 0.0001f);
        if (frac < 0.2f && !lowFuelSaid) { lowFuelSaid = true; QueueVoice(voiceLowFuel); }
        if (fuelKg <= 0f && !emptySaid) { emptySaid = true; PlayVoiceNow(voiceFuelEmpty); }
    }

    void Refuel(float dt)
    {
        if (fuelKg < 0f) InitFuel();
        if (fuelKg >= fuelCapKg) return;
        fuelKg = Mathf.Min(fuelCapKg, fuelKg + fuelCapKg / Mathf.Max(refuelSeconds, 1f) * dt);
        if (fuelKg / fuelCapKg > 0.25f) lowFuelSaid = false;
        emptySaid = false;
    }

    public bool _NeedsFuel() { return fuelKg >= 0f && fuelKg < fuelCapKg; }

    // ---------------- 照準 ----------------
    void AimGun()
    {
        Transform eye = cockpitRoot.transform;
        Vector3 o = eye.position;
        Vector3 f = eye.forward;
        if (Time.time >= nextAimRay)
        {
            nextAimRay = Time.time + 0.1f;
            aimPoint = o + f * gunRange;
            RaycastHit[] hits = Physics.RaycastAll(o, f, gunRange, groundMask, QueryTriggerInteraction.Ignore);
            float best = gunRange;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(car.transform)) continue;
                if (hits[i].distance < best && hits[i].distance > 20f) { best = hits[i].distance; aimPoint = hits[i].point; }
            }
            // 海面
            if (f.y < -0.0001f)
            {
                float t = (seaLevelY - o.y) / f.y;
                if (t > 20f && t < best) aimPoint = o + f * t;
            }
        }
        Vector3 d = aimPoint - gunMuzzle.position;
        if (d.sqrMagnitude > 1f) gunMuzzle.rotation = Quaternion.LookRotation(d.normalized, eye.up);
        if (gunMuzzle2 != null)
        {
            Vector3 d2 = aimPoint - gunMuzzle2.position;
            if (d2.sqrMagnitude > 1f) gunMuzzle2.rotation = Quaternion.LookRotation(d2.normalized, eye.up);
        }
    }

    // ---------------- 後ろの席のレーダー・警告音 ----------------
    public void _UseGunnerRadar(bool on)
    {
        if (on == usingGunnerRadar || radarDotsG == null) return;
        if (on)
        {
            pDots = radarDots; pDotR = radarDotRenderers; pSweep = radarSweep; pRose = radarRose; pMslDots = radarMslDots; pMslR = radarMslRenderers;
            radarDots = radarDotsG; radarDotRenderers = radarDotRenderersG; radarSweep = radarSweepG; radarRose = radarRoseG; radarMslDots = radarMslDotsG; radarMslRenderers = radarMslRenderersG;
        }
        else
        {
            radarDots = pDots; radarDotRenderers = pDotR; radarSweep = pSweep; radarRose = pRose; radarMslDots = pMslDots; radarMslRenderers = pMslR;
        }
        radarPrev = null;
        usingGunnerRadar = on;
    }

    public int _CrewStallState() { return stallState; }
    public int _BurnLevel() { return burnLevel; }
    public void NetStall0() { stallState = 0; }
    public void NetStall1() { stallState = 1; }
    public void NetStall2() { stallState = 2; }

    // ---------------- AMA の警告と攻撃 ----------------
    public bool _IsHostile() { return hostile; }
    public void NetHostileOn() { hostile = true; }
    // 最初の出撃の「これは偵察任務である…」が流れている間（全員の画面）: 敵はこの機体を探さない・追わない・撃たない
    private bool briefing;
    private float briefingUntil = -1f;
    private bool reconPlayed;
    private bool iDidRecon;   // 自分が「偵察任務」を聞いた 1 人目
    public bool _InBriefing() { return briefing; }
    public void NetBriefingOn() { briefing = true; }
    public void NetBriefingOff() { briefing = false; }
    public void NetHostileOff() { hostile = false; }

    // 近くに敵（戦闘機 3km・船 2.5km 以内）がいる = 見つかった
    bool EnemyNearby()
    {
        if (radarTracks == null || car == null) return false;
        Vector3 me = car.transform.position;
        for (int i = 0; i < radarTracks.Length; i++)
        {
            int ty = TrackType(i);
            if ((ty != 0 && ty != 1) || !TrackAlive(i)) continue;
            float d = Vector3.Distance(radarTracks[i].position, me);
            if ((ty == 0 && d < 3000f) || (ty == 1 && d < 2500f)) return true;
        }
        return false;
    }

    public bool _IsSpotted() { return spotted; }
    public void NetSpottedOn() { spotted = true; }
    public void NetSpottedOff() { spotted = false; }
    // 警戒機に見つかった（ハードモード）: 船に見つかった時と同じように、全部の戦闘機が向かって来る
    private float awacsSeenTime = -100f;
    public void NetAwacsSpot() { awacsSeenTime = Time.time; }

    // 敵の船の 2.5km 以内に入ったら「見つかった」を全員に知らせる。離れて 60 秒で解除
    bool Armed() { return armedAt >= 0f && Time.time >= armedAt; }

    void UpdateSpotted()
    {
        if (!Armed()) return;
        if (Time.time < briefingUntil || Quiet()) return;   // 偵察任務の無線の間は、船にも見つからない
        if (Time.time < nextSpotScan || radarTracks == null || car == null) return;
        nextSpotScan = Time.time + 1f;
        bool near = false;
        if (hasTakenOff)
        {
            Vector3 me = car.transform.position;
            for (int i = 0; i < radarTracks.Length; i++)
            {
                if (TrackType(i) != 1 || !TrackAlive(i)) continue;
                if (Vector3.Distance(radarTracks[i].position, me) < 2500f) { near = true; break; }
            }
        }
        if (Time.time - awacsSeenTime < 3f) near = true;
        if (near)
        {
            lastShipSeen = Time.time;
            if (!sentSpotted) { sentSpotted = true; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSpottedOn)); }
        }
        else if (sentSpotted && Time.time - lastShipSeen > 60f)
        {
            sentSpotted = false;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSpottedOff));
        }
    }

    // 2 人乗りなら、後ろの席の人にも同じ音声を送る
    void RelayVoice(AudioClip c, int ch)
    {
        if (voiceRelay != null && isPiloting && HasCrewGunner()) voiceRelay._Send(c, ch);
    }

    void PlayRadio(AudioClip c)
    {
        if (enemyRadio == null || c == null) return;
        RelayVoice(c, 1);
        enemyRadio.Stop();
        enemyRadio.clip = c;
        enemyRadio.Play();
    }

    void SetHostile()
    {
        if (hostile) return;
        hostile = true;
        if (gameMode != null) gameMode._ReportWar();   // ワールドの全員、次の出撃からは警告なし
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetHostileOn));
    }

    void ClearHostile()
    {
        warnStage = -1;
        ainStage = ainWarnHeard ? 2 : 0;   // 2 回目からの出撃: 警告は流さない（入ったらそのまま攻撃開始）
        escortRequested = false; escortGreeted = false; escortByeDone = false; ainRegretDone = false;
        carrierWarned = false;
        reconPlayed = false;
        briefingUntil = -1f;
        if (briefing) { briefing = false; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBriefingOff)); }
        if (sentSpotted) { sentSpotted = false; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSpottedOff)); }
        if (!hostile) return;
        hostile = false;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetHostileOff));
    }

    // 見つかったら放送 → 警告 3 回 → 最後の通告 → 攻撃開始（その前に撃ったら即攻撃）
    bool Quiet() { return gameMode != null && gameMode._Quiet(); }

    void UpdateHostility()
    {
        if (hostile || !hasTakenOff || !Armed()) return;
        // 遊覧モード: 平和な間は警告も攻撃開始もない。裏切りの後は全員がすぐ攻撃される
        if (gameMode != null && gameMode._SightMode())
        {
            if (gameMode._Betrayed()) { PlayRadio(amaEngage); SetHostile(); }
            else if (!exercisePlayed)
            {
                // 「これは合同訓練である。武器の使用を禁止する」（ノーマルの「偵察任務」の代わり。最初の 1 回だけ）
                exercisePlayed = true;
                if (gaurExercise != null && gaurExercise.Length > 0) PlayRadio(gaurExercise[Random.Range(0, gaurExercise.Length)]);
            }
            return;
        }
        if (Quiet() && reconPlayed) return;   // 「偵察任務」の無線の間は、警告も攻撃開始もしない
        // 2 回目からの出撃（一度戦闘になった後）: 潜水艦から飛び立って 3 秒後に、警告なしで攻撃開始
        // 誰かがもう戦闘になっていたら（ワールド全体）: 潜水艦から飛び立って 3 秒後に、警告なしで攻撃開始
        //   ガウリア帝国の司令部から: 味方が撃墜されていたら「我々の戦闘機が撃破された…」、まだなら「攻撃を受けた…」
        if (gameMode != null && gameMode._WarStarted() && !gameMode._IsSightseeing())
        {
            AudioClip[] lines = gameMode._OwnLost() ? amaHeavy : amaAttacked;
            if (lines != null && lines.Length > 0) PlayRadio(lines[Random.Range(0, lines.Length)]);
            SetHostile();
            return;
        }
        // まだ戦闘になっていない時の出撃: 飛び立ったら司令部から「これは偵察任務である…」。流れている間は敵に見つからない
        //   ワールドで最初に飛び立った 1 人だけ（その人が戦闘の前に墜落・降りたら、次に飛び立つ人にまた流す）
        if (!reconPlayed && (gameMode == null || !gameMode._IsSightseeing()))
        {
            reconPlayed = true;
            if (gameMode != null && gameMode._ReconDone()) return;   // もう誰かが聞いた
            int ri = (gaurRecon != null && gaurRecon.Length > 0) ? Random.Range(0, gaurRecon.Length) : -1;
            AudioClip rc = ri >= 0 ? gaurRecon[ri] : null;
            if (gameMode != null) gameMode._ReportRecon();
            iDidRecon = true;
            if (rc != null)
            {
                // ワールドの全員に流す（自分もこれで聞く）。ゲームモードが無い時だけ自分の無線で
                if (gameMode != null && gameMode.radio != null) gameMode._BroadcastRecon(ri);
                else PlayRadio(rc);
                briefingUntil = Time.time + rc.length + 1f;
                briefing = true;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBriefingOn));
            }
            return;
        }
        if (Time.time < briefingUntil) return;
        if (briefing) { briefing = false; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBriefingOff)); }
        // ペラゴス基地に近づいた: アイン中将の警告（船の警告とはどちらか一方だけ）
        //   10 秒以内に基地から 3km より外へ離脱しなければ攻撃開始。離脱したら「感謝する」。また入ったら警告なしで攻撃
        if (baseCenter != null && !(gameMode != null && gameMode._IsSightseeing()))
        {
            Vector3 bd = car.transform.position - baseCenter.position;
            bd.y = 0f;
            bool inBase = bd.magnitude < baseWarnRadius;
            if (ainStage == 0 && inBase && warnStage < 0)
            {
                AudioClip w = (ainWarn != null && ainWarn.Length > 0) ? ainWarn[Random.Range(0, ainWarn.Length)] : null;
                PlayRadio(w);
                ainStage = 1;
                ainWarnHeard = true;
                ainDeadline = Time.time + (w != null ? w.length : 12f) + 10f;
                return;
            }
            if (ainStage == 1)
            {
                if (!inBase)
                {
                    ainStage = 2;
                    if (ainThanks != null && ainThanks.Length > 0) PlayRadio(ainThanks[Random.Range(0, ainThanks.Length)]);
                    return;
                }
                if (Time.time >= ainDeadline) SetHostile();
                return;
            }
            if (ainStage == 2)
            {
                if (inBase) SetHostile();   // 2 度目の警告はない
                return;                     // アインの警告を受けたら、船の警告はしない
            }
        }
        if (Time.time >= nextDetectScan)
        {
            nextDetectScan = Time.time + 1f;
            detected = EnemyNearby();
            if (detected) lastDetectedTime = Time.time;
        }
        if (warnStage < 0)
        {
            if (detected) { warnStage = 0; PlayRadio(amaBroadcast); nextWarnTime = Time.time + 18f; }
            return;
        }
        // 離れて 60 秒たったら、警告は最初から
        if (!detected && Time.time - lastDetectedTime > 60f) { warnStage = -1; return; }
        if (!detected || Time.time < nextWarnTime) return;   // 範囲の外にいる間は進めない
        warnStage++;
        if (warnStage == 1) { PlayRadio(amaWarn1); nextWarnTime = Time.time + 15f; }
        else if (warnStage == 2) { PlayRadio(amaWarn2); nextWarnTime = Time.time + 14f; }
        else if (warnStage == 3) { PlayRadio(amaWarn3); nextWarnTime = Time.time + 13f; }
        else if (warnStage == 4) { PlayRadio(amaFinal); nextWarnTime = Time.time + 10f; }
        else { PlayRadio(amaEngage); SetHostile(); }
    }

    // 自分（またはガンナー）が撃った: 見つかっている間なら問答無用で攻撃開始
    void OnPlayerFired()
    {
        if (!isPiloting || hostile || !Armed()) return;   // 射出前（潜水艦の上・乗る時のクリック）は数えない
        if (gameMode != null && gameMode._SightMode()) return;   // 遊覧モード: 撃つだけでは何も起きない（敵に当てて HP が減ったら裏切り）
        if (Quiet()) return;   // 「偵察任務」の無線の間は、撃っても攻撃してこない
        if (warnStage < 0 && !EnemyNearby()) return;   // 敵のいない所での射撃（標的の練習など）
        PlayRadio(amaFired);
        SetHostile();
    }

    public void _GunnerFired()
    {
        if (isPiloting) { OnPlayerFired(); return; }
        if (hostile || Time.time < nextCrewFiredSend) return;
        nextCrewFiredSend = Time.time + 2f;
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetCrewFired));
    }

    public void NetCrewFired() { OnPlayerFired(); }

    // ---------------- ガンナーモード ----------------
    void SetGunnerMode(bool on)
    {
        gunnerMode = on;
        fireInput = false;
        useHeld = false;
        trigPrev = false;
        lockCand = -1;
        locked = false;
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (gunSight != null) gunSight.SetActive(!on);
        if (gunner != null) gunner._SetSolo(on);
        apNextChange = Time.time + 4f;   // 最初の数秒はまっすぐ
        apTargetRoll = 0f;
        if (isPiloting) PlayVoiceNow(on ? voiceApOn : voiceApOff);
    }

    // ---------------- 音声案内 ----------------
    void UpdateVoice()
    {
        if (!isVR && Input.GetKeyDown(KeyCode.M)) ToggleVoice();
        if (!isVR && Input.GetKeyDown(KeyCode.H)) ToggleGaugeOnly();
        if (!isVR && Input.GetKeyDown(KeyCode.G)) ToggleGear();
        if (!isVR && Input.GetKeyDown(KeyCode.X)) DropFlares();

        if (!hasTakenOff && catapult == null && !grounded && !floating && altitude > 30f) hasTakenOff = true;
        // カタパルト以外（水面から）で飛び立った時も、3 秒後から
        if (armedAt < 0f && hasTakenOff && catapult == null) armedAt = Time.time + 3f;

        if (Time.time >= nextVoiceScan && radarTracks != null && car != null && bootDone)
        {
            nextVoiceScan = Time.time + 0.5f;
            int n = radarTracks.Length;
            if (vAnnounced == null || vAnnounced.Length != n) { vAnnounced = new bool[n]; vDead = new bool[n]; vInit = false; }
            Vector3 me = car.transform.position;
            Vector3 f = axisReference != null ? axisReference.forward : -car.transform.right;
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                if (TrackType(i) != 0 || radarTracks[i] == null) continue;
                Vector3 d = radarTracks[i].position - me;
                float dist = d.magnitude;
                if (!TrackAlive(i))
                {
                    // 落ちた瞬間（近くにいた時だけ）
                    if (!vDead[i] && vInit && dist < voiceKillRange) QueueVoice(voiceKill);
                    vDead[i] = true;
                    vAnnounced[i] = false;
                    continue;
                }
                vDead[i] = false;
                if (!hasTakenOff) continue;   // 離陸するまでは敵ドローンの方向を知らせない
                if (!vAnnounced[i] && dist < voiceDetectRange)
                {
                    vAnnounced[i] = true;
                    d.y = 0f;
                    float rel = Vector3.SignedAngle(f, d, Vector3.up);   // 時計回りが +
                    int clock = ((Mathf.RoundToInt(rel / 30f) % 12) + 12) % 12;
                    if (voiceClock != null && clock < voiceClock.Length) QueueVoice(voiceClock[clock]);
                }
                else if (vAnnounced[i] && dist > voiceDetectRange * 1.3f) vAnnounced[i] = false;
            }
            vInit = true;
        }

        // 自分が当てた物が壊れたら（敵ドローンは「Splash one」が流れるので除く）
        if (lastHitFt != null)
        {
            if (Time.time - lastHitTime > 4f) lastHitFt = null;
            else if (lastHitFt._IsDestroyed())
            {
                if (!IsEnemyTarget(lastHitFt)) QueueVoice(voiceDestroyed);
                lastHitFt = null;
            }
        }

        // 順番に流す（重ならないように）
        if (voiceAudio != null && vQCount > 0 && !voiceAudio.isPlaying)
        {
            voiceAudio.clip = vQueue[0];
            for (int k = 1; k < vQCount; k++) vQueue[k - 1] = vQueue[k];
            vQCount--;
            if (voiceAudio.clip != null) voiceAudio.Play();
        }
    }

    // 今流れている音声を止めてすぐに流す（射出の瞬間など）
    void PlayVoiceNow(AudioClip c)
    {
        if (!voiceEnabled || c == null || voiceAudio == null) return;
        RelayVoice(c, 2);
        vQCount = 0;
        voiceAudio.Stop();
        voiceAudio.clip = c;
        voiceAudio.Play();
    }

    // ---------------- プレイヤー同士 ----------------
    // 自分の弾・ミサイルが、ほかのプレイヤーが乗っている機体に当たった → その機体の操縦者の画面でダメージ
    public bool _PvpHit(Collider col, bool missile)
    {
        if (col == null || car == null) return false;
        Rigidbody arb = col.attachedRigidbody;
        if (arb == null || arb.transform == car.transform) return false;
        controller other = arb.GetComponentInChildren<controller>();
        if (other == null || other == this || other._GetPilot() == null) return false;
        other.SendCustomNetworkEvent(NetworkEventTarget.Owner, missile ? nameof(NetPlayerMissileHit) : nameof(NetPlayerGunHit));
        return true;
    }

    public void NetPlayerGunHit()
    {
        if (!isPiloting) return;
        Damage(pvpGunDamage);
        TakingFireVoice(3);
    }

    public void NetPlayerMissileHit()
    {
        if (!isPiloting) return;
        Damage(pvpMissileDamage);
        TakingFireVoice(3);
    }

    // 船がこちらへミサイルを撃った（その船の方向を言う。急ぐので今の音声を止めてすぐ流す）
    // 空母の戦闘機の追尾ミサイル: 「誘導ミサイル接近」
    public AudioClip[] voiceGuided;
}
