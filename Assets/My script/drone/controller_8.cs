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
    void UpdateRadar()
    {
        if (radarDots == null || radarTracks == null || car == null) return;
        float dt = Time.deltaTime;
        float s0 = sweepAng;
        float step = radarSweepSpeed * dt;
        sweepAng = (sweepAng + step) % 360f;
        if (radarSweep != null) radarSweep.localRotation = Quaternion.Euler(0f, 0f, -sweepAng);

        Vector3 me = car.transform.position;
        Vector3 f = axisReference != null ? axisReference.forward : -car.transform.right;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
        f.Normalize();
        Vector3 r = new Vector3(f.z, 0f, -f.x);
        float hdg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        if (radarRose != null) radarRose.localRotation = Quaternion.Euler(0f, 0f, hdg);   // 機首が常に上

        UpdateMissileBlips(me, f, r);

        // 島の地図: 自分の位置と機首の向きに合わせる
        Renderer mr = usingGunnerRadar ? radarMapG : radarMap;
        if (mr != null && radarMapRect.z > 1f)
        {
            Material mm;
            if (usingGunnerRadar) { if (mapMatG == null) mapMatG = mr.material; mm = mapMatG; }
            else { if (mapMatP == null) mapMatP = mr.material; mm = mapMatP; }
            float sz = radarMapRect.z;
            mm.SetVector("_Center", new Vector4((me.x - (radarMapRect.x - sz * 0.5f)) / sz, (me.z - (radarMapRect.y - sz * 0.5f)) / sz, 0f, 0f));
            mm.SetVector("_Dir", new Vector4(f.x, f.z, 0f, 0f));
            mm.SetFloat("_Scale", radarRange / sz);
        }

        int n = Mathf.Min(radarTracks.Length, radarDots.Length);
        if (radarPrev == null || radarPrev.Length != n) { radarPrev = new Vector3[n]; radarSeen = new bool[n]; }
        for (int i = 0; i < n; i++)
        {
            Transform dot = radarDots[i];
            if (dot == null) continue;
            Transform t = radarTracks[i];
            if (t == car.transform || !TrackAlive(i) || TrackType(i) == 5)   // 5 = レーダーに出さない物（基地の艦隊・止まっている警戒機）
            {
                if (dot.gameObject.activeSelf) dot.gameObject.SetActive(false);
                radarSeen[i] = false;
                continue;
            }
            Vector3 d = t.position - me;
            d.y = 0f;
            float x = Vector3.Dot(d, r) / radarRange * radarRadius;
            float y = Vector3.Dot(d, f) / radarRange * radarRadius;
            float bAng = Mathf.Atan2(x, y) * Mathf.Rad2Deg;
            float rel = (bAng - s0 + 720f) % 360f;
            if (rel > step + 0.001f) continue;   // まだ光が通っていない
            if (d.magnitude > radarRange)
            {
                if (dot.gameObject.activeSelf) dot.gameObject.SetActive(false);
                continue;
            }
            dot.localPosition = new Vector3(x, y, dot.localPosition.z);
            // 進んでいる向きにアイコンを回す
            Vector3 mv = t.position - radarPrev[i];
            mv.y = 0f;
            if (radarSeen[i] && mv.sqrMagnitude > 0.5f)
                dot.localRotation = Quaternion.Euler(0f, 0f, -Vector3.SignedAngle(f, mv, Vector3.up));
            radarPrev[i] = t.position;
            radarSeen[i] = true;
            int ty = TrackType(i);
            if (radarMats != null && ty < radarMats.Length && radarDotRenderers != null && i < radarDotRenderers.Length && radarDotRenderers[i] != null)
                radarDotRenderers[i].sharedMaterial = radarMats[ty];
            if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        }
    }

    string WeaponLine(int w, string name, string val)
    {
        if (weapon == w) return "<color=#ffc040>> " + name + "   " + val + "</color>";
        return "<color=#3a8a98>  " + name + "   " + val + "</color>";
    }

    void SetBar(Transform bar, float f, bool vertical)
    {
        if (bar == null) return;
        f = Mathf.Clamp(f, 0.001f, 1f);
        bar.localScale = vertical ? new Vector3(1f, f, 1f) : new Vector3(f, 1f, 1f);
    }

    string CompassName(float h)
    {
        int i = Mathf.RoundToInt(h / 45f) % 8;
        if (i == 0) return "N";
        if (i == 1) return "NE";
        if (i == 2) return "E";
        if (i == 3) return "SE";
        if (i == 4) return "S";
        if (i == 5) return "SW";
        if (i == 6) return "W";
        return "NW";
    }

    void UpdateHud()
    {
        UpdateCockpit();
        if (hudText == null) return;
        string t = "SPD " + (airspeed * 3.6f).ToString("F0") + " km/h\n"
                 + "ALT " + altitude.ToString("F0") + " m\n"
                 + "VS  " + (rb != null ? rb.velocity.y : 0f).ToString("F1") + " m/s\n"
                 + "AoA " + aoaDeg.ToString("F1") + "°\n"
                 + "THR " + (throttle * 100f).ToString("F0") + "%"
                 + (flapsDown ? "  FLAP" : "")
                 + (braking && grounded ? "  BRK" : "")
                 + "\nGUN " + ammo.ToString() + (ammo <= 0 ? " <color=red>EMPTY</color>" : "");
        if (stallAmount > 0.05f) t += "\n<color=red>STALL</color>";
        else if (aoaDeg > stallAngle - flapStallReduction * flapPos - 3f && !grounded) t += "\n<color=yellow>STALL WARNING</color>";
        if (catapult != null)
        {
            if (catLaunching) t += "\n<color=orange>LAUNCH!</color>";
            else if (catHold > 0f) t += "\n<color=orange>LAUNCH IN " + Mathf.Max(catapult.holdTime - catHold, 0f).ToString("F1") + "</color>";
            else t += "\n<color=#66ccff>CATAPULT READY\nFULL THROTTLE TO LAUNCH</color>";
        }
        hudText.text = t;
    }

    // エンジン音: 誰かが乗っている間は全員の画面で鳴る
    // 操縦している本人はスロットルで、ほかの人は機体の速さで音の高さ・大きさを決める
    void UpdateEngineSound()
    {
        if (engineAudio == null) return;
        bool running = pilotPlayer != null && Utilities.IsValid(pilotPlayer) && !crashed;
        if (running && !engineAudio.isPlaying) engineAudio.Play();
        else if (!running && engineAudio.isPlaying) engineAudio.Stop();
        if (!running) return;
        float spd = rb != null ? rb.velocity.magnitude : 0f;
        float thr = isPiloting ? throttle : Mathf.Clamp01(spd / 55f);
        float target = 0.72f + thr * 0.55f + Mathf.Clamp01(spd / 60f) * 0.12f;
        engineAudio.pitch = Mathf.Lerp(engineAudio.pitch, target, Time.deltaTime * 3f);
        float vm = volume != null ? volume._Engine() : 1f;
        engineAudio.volume = Mathf.Lerp(engineAudio.volume, Mathf.Clamp01(engineVolume * vm * (0.35f + thr * 0.65f)), Time.deltaTime * 3f);
    }

    void UpdateAudio()
    {
        bool inAir = !grounded && !floating && airspeed > 3f;
        bool stalled = inAir && stallAmount > 0.05f;
        bool warn = inAir && !stalled && aoaDeg > stallAngle - flapStallReduction * flapPos - 3f;
        // 後ろの席の人にも鳴らすため、状態が変わった時だけ全員に知らせる
        int ss = stalled ? 2 : (warn ? 1 : 0);
        if (ss != sentStallState && isPiloting)
        {
            sentStallState = ss;
            if (ss == 0) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStall0));
            else if (ss == 1) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStall1));
            else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStall2));
        }
        if (stallWarningAudio != null)
        {
            if (warn && !stallWarningAudio.isPlaying) stallWarningAudio.Play();
            else if (!warn && stallWarningAudio.isPlaying) stallWarningAudio.Stop();
        }
        if (stallAudio != null)
        {
            if (stalled && !stallAudio.isPlaying) stallAudio.Play();
            else if (!stalled && stallAudio.isPlaying) stallAudio.Stop();
        }
    }

    // ---------------- 乗り降り ----------------
    public VRCPlayerApi _GetPilot() { return pilotPlayer; }
    public bool _IsFiring() { return localFiring || remoteFiring; }
    public bool _IsBoosting() { return boosting; }

    void SetExteriorVisible(bool v)
    {
        if (exteriorRenderers == null) return;
        for (int i = 0; i < exteriorRenderers.Length; i++) if (exteriorRenderers[i] != null) exteriorRenderers[i].enabled = v;
    }

    // 敵機の機銃が当たった（敵機を動かしている人の画面から、この機体の持ち主＝操縦者に届く）
    public void NetEnemyHit()
    {
        if (!isPiloting) return;
        Damage(5f);
        TakingFireVoice(0);   // 敵ドローンの機銃
    }

    // 座席に付いている全部の Udon（座るためのものを含む）のクリックを止める / 戻す（自分の画面だけ）
    void SetSeatInteractive(bool on)
    {
        Component[] ubs = GetComponents(typeof(UdonBehaviour));
        for (int i = 0; i < ubs.Length; i++)
        {
            UdonBehaviour ub = (UdonBehaviour)ubs[i];
            if (ub != null) ub.DisableInteractive = !on;
        }
    }
    void SetBoardClickable(bool on)
    {
        if (boarder == null) return;
        Collider[] cs = boarder.GetComponents<Collider>();
        for (int i = 0; i < cs.Length; i++)
            if (cs[i] != null && cs[i].isTrigger) cs[i].enabled = on;   // 機体の物理の当たり判定（トリガーでない物）は触らない
    }

    public void _SetRadioCount(int n) { radioCount = n; }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        DisableInteractive = true;
        pilotPlayer = player;
        if (boarder != null) boarder._Refresh();   // 2 席とも埋まったら誰も乗れない
        if (player == null || !player.isLocal) return;
        // 座っている間は、座席の「クリックで座る」を止める（射撃の左クリックで座り直さないように）
        SetSeatInteractive(false);
        // 「クリックで乗る」の当たり判定も自分の画面では消す（視線が当たって機体全体が水色に光るのを防ぐ）
        SetBoardClickable(false);
        if (isPiloting) return;
        // 降りた直後（0.3 秒以内）にまた座った = クリックによる座り直し。状態を戻して起動演出はしない
        if (Time.time - lastExitTime < 0.3f)
        {
            isPiloting = true;
            throttle = savedThrottle;
            flapsDown = savedFlaps;
            if (gunSight != null) gunSight.SetActive(true);
            if (cockpitRoot != null) cockpitRoot.SetActive(true);
            SetExteriorVisible(false);
            if (gunAudio != null) gunAudio.spatialBlend = 0f;
            return;
        }

        Networking.SetOwner(player, car);
        Networking.SetOwner(player, gameObject);
        if (gunAudio != null) gunAudio.spatialBlend = 0f;   // 操縦している本人には銃の音を耳元で（機首から遠くても必ず聞こえる）
        if (voiceRelay != null) Networking.SetOwner(player, voiceRelay.gameObject);
        isVR = player.IsUserInVR();
        isPiloting = true;
        remoteFiring = false;
        fireInput = false;
        if (joinLog != null) joinLog._UseBlessing();   // 折り鶴の加護: 乗った時に 1 回だけ +10 点
        if (gunSight != null) gunSight.SetActive(true);
        if (cockpitRoot != null) cockpitRoot.SetActive(true);
        SetExteriorVisible(false);
        // 起動演出を始める
        bootStart = Time.time;
        bootDone = false;
        ApplyBootPanels(0f);
        SetBlackout(0f);   // 乗った時に真っ暗にしない
        if (cockpitAudio != null && cockpitAudio.clip != null) cockpitAudio.PlayDelayed(0.6f);
        inPitch = 0f; inRoll = 0f; inYaw = 0f; inThrottleCmd = 0f;
        vrStickHeld = false; vrLeverHeld = false; vrLsX = 0f; vrLsY = 0f; vrLsUpPrev = false; vrLsLeftPrev = false;
        throttle = 0f;
        flapsDown = false;
        hasTakenOff = false;
        armedAt = -1f;
        ClearHostile();
        hudGMax = 1f; hudGNow = 1f; hudGInit = false;
    }

    // 「偵察任務」を聞いた 1 人目が、戦闘の前に墜落・降りた（前は次の人にまた流していた）
    void ReconAbort()
    {
        if (!iDidRecon) return;
        iDidRecon = false;
        // 何度も流れないよう、降りても「もう聞いた」のまま（モードを切り替えた時だけ、また流れる）
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        DisableInteractive = false;
        remoteFiring = false;
        if (pilotPlayer == player) pilotPlayer = null;
        if (boarder != null) boarder._Refresh();
        radioCount = 0;
        if (player == null || !player.isLocal) return;
        ReconAbort();
        lastExitTime = Time.time;
        savedThrottle = throttle;
        savedFlaps = flapsDown;
        SetSeatInteractive(true);
        SetBoardClickable(true);

        if (gunnerMode) SetGunnerMode(false);
        ClearCockpitDamage();
        if (gunAudio != null) gunAudio.spatialBlend = 1f;
        isPiloting = false;
        fireInput = false;
        useHeld = false;
        trigPrev = false;
        lockCand = -1;
        locked = false;
        vQCount = 0;
        vInit = false;
        vAnnounced = null;
        if (voiceAudio != null) voiceAudio.Stop();
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (gunSight != null) gunSight.SetActive(false);
        if (cockpitRoot != null) cockpitRoot.SetActive(false);
        SetExteriorVisible(true);
        SetBlackout(0f);
        throttle = 0f;   // 降りたらアイドル（機体はそのまま物理で滑空・落下する）
        inPitch = 0f; inRoll = 0f; inYaw = 0f;
        if (hudText != null) hudText.text = "";
        if (engineAudio != null) engineAudio.volume = 0.3f;
        if (stallWarningAudio != null && stallWarningAudio.isPlaying) stallWarningAudio.Stop();
        if (stallAudio != null && stallAudio.isPlaying) stallAudio.Stop();
    }

    // ---------------- 入力イベント ----------------
    // VR では左スティックは機能のボタン（操縦は右グリップの操縦桿）。デスクトップは WASD で操縦
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { if (isVR) vrLsX = value; else inRoll = value; }
    public override void InputMoveVertical(float value, UdonInputEventArgs args) { if (isVR) vrLsY = value; else inPitch = value; }
    public override void InputLookHorizontal(float value, UdonInputEventArgs args) { vrLookX = value; }
    public override void InputLookVertical(float value, UdonInputEventArgs args) { vrLookY = value; }
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        // VR: 左手のトリガー = ミサイル、右手のトリガー = 機銃。デスクトップの左クリック = 機銃
        // VR: 左手のトリガー = 武器の切り替え、右手のトリガー = 撃つ。デスクトップの左クリック = 撃つ（Enter と同じ）
        if (isVR && args.handType == HandType.LEFT) cycleInput = value;
        else useHeld = value;
    }
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        // VR: 右グリップ = 操縦桿、左グリップ = スロットルレバー（SaccFlight 風）
        if (isVR && args.handType == HandType.LEFT) vrLeftGrip = value;
        else vrGrip = value;
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        // 入ってきた人の準備ができた頃に、機体のオーナーが脚の状態を送る
        if (player != null && !player.isLocal) SendCustomEventDelayedSeconds(nameof(_SendGearState), 3f);
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        remoteFiring = false;
        if (pilotPlayer == player) pilotPlayer = null;
    }

    // 座席は Disable Station Exit をオンにしている（オフだとWASDで降りてしまう）ので、ジャンプで自分で降りる
    public override void InputJump(bool value, UdonInputEventArgs args)
    {
        if (!value || !isPiloting || station == null) return;
        station.ExitStation(Networking.LocalPlayer);
    }
}
