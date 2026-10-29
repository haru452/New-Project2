using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using TMPro;

// =============================================================
//  2 人目の席（ガンナー）
//  ・機体の下の旋回砲塔を、見ている方向へ向けて撃つ（デスクトップはマウス、VR は頭の向き）
//  ・1 / 2 / 3 で武器を選ぶ（機銃・無誘導ミサイル・ロックオンミサイル）。Enter か左クリックで撃つ
//    VR: 右トリガーで撃つ、左トリガーで武器の切り替え
//  ・ロックオン: 3 を選んで見ている方向（約10°以内）に標的を 1 秒入れる → 赤い四角
//  ・砲塔の向きは他の人にも同期（曳光弾・ミサイルも見える）。当たり判定は撃った本人の画面で行う
//  ・ジャンプで降りる。機体が墜落したら一緒に格納庫へ戻る
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class GunnerStation : UdonSharpBehaviour
{
    public controller plane;
    public VRCStation station;
    public Transform turret;             // 向きを変える砲塔
    public Transform muzzle;             // 砲口（+Z が撃つ方向）
    public ParticleSystem tracer;
    public AudioSource gunAudio;
    public GameObject hudRoot;           // ガンナー本人にだけ見える照準と表示（頭に付いてくる）
    public TextMeshPro hudText;
    public Transform lockBox;
    public GameObject radarRoot;         // 操縦席の背中のレーダー（後ろの席の人にだけ表示）
    public AudioSource stallWarnAudio;   // 失速しそうな時の警告音（操縦席と同じ）
    public AudioSource stallAudio;       // 失速した時
    public AudioSource alarmAudio;       // 被弾の警報

    [Tooltip("オフ: 後ろの席からは撃てない（見るだけ・砲塔を回すだけ）")]
    public bool canFire = false;
    public float fireRate = 10f;
    public float gunRange = 900f;
    public float gunSpread = 0.4f;
    public int maxAmmo = 600;
    public int maxRockets = 16;
    public int maxGuided = 8;
    public float lockCone = 10f;
    public float lockTime = 1f;
    public float missileCooldown = 0.8f;
    [Tooltip("砲塔が下・後ろを向ける限界。機体の真上には向かない（度）")]
    public float maxUpAngle = 25f;

    [UdonSynced] private Vector3 aimLocal = new Vector3(0f, 0f, 1f);
    [UdonSynced] private bool syncFiring;

    private VRCPlayerApi gunnerPlayer;
    private bool isGunner;
    private bool isVR;
    private int weapon;
    private int ammo, rockets, guided;
    private bool useHeld, trigPrev, cycleInput, cyclePrev;
    private float fireTimer;
    private float lastMissile = -10f;
    private int lockCand = -1;
    private float lockTimer;
    private bool locked;
    private float nextSync;
    private Vector3 sentAim;
    private int groundMask;
    private float noLockUntil;
    private bool solo;                   // 操縦士が P キーで砲塔を操作している（席は操縦席のまま）
    private bool soloFilled;
    private float rearmTimer;

    void Start()
    {
        groundMask = ~((1 << 9) | (1 << 10) | (1 << 18));
        if (hudRoot != null) hudRoot.SetActive(false);
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        Refill();
    }

    void Refill()
    {
        ammo = maxAmmo;
        rockets = maxRockets;
        guided = maxGuided;
    }

    public VRCPlayerApi _GetGunner() { return gunnerPlayer; }
    public bool _IsLocalGunner() { return isGunner && !solo; }   // 本物の 2 人目として自分が座っている

    // 操縦士が P キーでガンナーになる / 戻る（自分の画面だけで呼ばれる）
    public void _SetSolo(bool on)
    {
        if (on == solo) return;
        solo = on;
        isGunner = on;
        useHeld = false;
        trigPrev = false;
        lockCand = -1;
        locked = false;
        if (on)
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            isVR = Networking.LocalPlayer.IsUserInVR();
            if (!soloFilled) { Refill(); soloFilled = true; }
            weapon = 0;
        }
        else if (syncFiring) { syncFiring = false; RequestSerialization(); }
        if (hudRoot != null) hudRoot.SetActive(on);
        if (lockBox != null) lockBox.gameObject.SetActive(false);
    }

    // ---------------- 乗り降り ----------------
    public override void OnStationEntered(VRCPlayerApi player)
    {
        gunnerPlayer = player;
        if (plane != null && plane.boarder != null) plane.boarder._Refresh();
        if (player == null || !player.isLocal) return;
        solo = false;
        Networking.SetOwner(player, gameObject);
        if (plane != null && plane.voiceRelay != null) plane.voiceRelay._MarkSeen();   // 座る前の音声は流さない
        if (radarRoot != null) radarRoot.SetActive(true);
        if (plane != null) plane._UseGunnerRadar(true);
        isGunner = true;
        if (plane != null && plane.joinLog != null) plane.joinLog._UseBlessing();   // 折り鶴の加護: 乗った時に 1 回だけ +10 点
        isVR = player.IsUserInVR();
        Refill();
        weapon = 0;
        if (hudRoot != null) hudRoot.SetActive(true);
        if (plane != null) { plane._SetExteriorVisible(false); plane._SetBoardClickable(false); }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (gunnerPlayer == player) gunnerPlayer = null;
        if (plane != null && plane.boarder != null) plane.boarder._Refresh();
        if (player == null || !player.isLocal) return;
        isGunner = false;
        useHeld = false;
        trigPrev = false;
        lockCand = -1;
        locked = false;
        if (syncFiring) { syncFiring = false; RequestSerialization(); }
        if (hudRoot != null) hudRoot.SetActive(false);
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (radarRoot != null) radarRoot.SetActive(false);
        StopLoops();
        if (plane != null) plane._UseGunnerRadar(false);
        if (plane != null)
        {
            if (!plane._IsPilotLocal()) plane._SetExteriorVisible(true);
            plane._SetBoardClickable(true);
        }
    }

    void StopLoops()
    {
        if (stallWarnAudio != null) stallWarnAudio.Stop();
        if (stallAudio != null) stallAudio.Stop();
        if (alarmAudio != null) alarmAudio.Stop();
    }

    void Loop(AudioSource a, bool on)
    {
        if (a == null) return;
        if (on && !a.isPlaying) a.Play();
        else if (!on && a.isPlaying) a.Stop();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (gunnerPlayer == player) { gunnerPlayer = null; syncFiring = false; }
    }

    // 機体が墜落した（controller の NetCrash から全員の画面で呼ばれる）
    public void _OnCrash()
    {
        if (isGunner && !solo && plane != null && plane.joinLog != null) plane.joinLog._ResetRun();
        if (solo) { _SetSolo(false); soloFilled = false; return; }   // 操縦士は controller が戻す
        if (!isGunner || station == null) return;
        station.ExitStation(Networking.LocalPlayer);
        SendCustomEventDelayedSeconds(nameof(_GoHome), 0.2f);
    }

    public void _GoHome()
    {
        if (plane == null || plane.respawnPoint == null) return;
        Networking.LocalPlayer.TeleportTo(plane.respawnPoint.position + Vector3.up * 1f, plane.respawnPoint.rotation);
    }

    // ---------------- 毎フレーム ----------------
    void Update()
    {
        if (plane == null || plane.car == null || turret == null) return;
        Transform car = plane.car.transform;
        float dt = Time.deltaTime;

        if (isGunner)
        {
            VRCPlayerApi.TrackingData head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 look = head.rotation * Vector3.forward;
            // 見ている先の点（何かに当たればそこ、無ければ 600m 先）に砲塔を向ける
            Vector3 aimPoint = head.position + look * 600f;
            RaycastHit[] hits = Physics.RaycastAll(head.position, look, gunRange, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(car)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; aimPoint = hits[i].point; }
            }
            Vector3 want = aimPoint - turret.position;
            if (want.sqrMagnitude < 0.01f) want = look;
            Vector3 wl = car.InverseTransformDirection(want.normalized);
            wl = LimitUp(wl);
            aimLocal = wl;
            turret.rotation = Quaternion.LookRotation(car.TransformDirection(wl), car.up);

            if (Time.time >= nextSync && Vector3.Angle(sentAim, aimLocal) > 0.8f)
            {
                nextSync = Time.time + 0.12f;
                sentAim = aimLocal;
                RequestSerialization();
            }

            UpdateInput(dt);
            // 操縦席と同じ警告音（失速・被弾の警報）。P キーで操縦士がガンナーの時は操縦席の音が鳴っているので鳴らさない
            if (!solo)
            {
                int ss = plane._CrewStallState();
                Loop(stallWarnAudio, ss == 1);
                Loop(stallAudio, ss == 2);
                Loop(alarmAudio, plane._BurnLevel() >= 2);
            }
            // 出発地点の近くでは弾とミサイルを少しずつ補充
            if (plane.respawnPoint != null && Vector3.Distance(car.position, plane.respawnPoint.position) < plane.repairRadius)
            {
                rearmTimer += dt;
                if (rearmTimer >= 1f)
                {
                    rearmTimer = 0f;
                    ammo = Mathf.Min(maxAmmo, ammo + 60);
                    rockets = Mathf.Min(maxRockets, rockets + 2);
                    guided = Mathf.Min(maxGuided, guided + 1);
                }
            }
            UpdateLock(dt, head.position, look);
            UpdateHud();
        }
        else
        {
            // 他の人の画面: 同期された向きへなめらかに回す
            Quaternion target = Quaternion.LookRotation(car.TransformDirection(aimLocal), car.up);
            turret.rotation = Quaternion.Slerp(turret.rotation, target, Mathf.Clamp01(dt * 12f));
        }

        // 機銃（撃っている人の画面では当たり判定も、それ以外は見た目だけ）
        bool firing = isGunner ? (canFire && weapon == 0 && (useHeld || DesktopFire()) && ammo > 0) : syncFiring;
        if (isGunner && firing != syncFiring) { syncFiring = firing; RequestSerialization(); }
        if (!firing) { fireTimer = 0f; return; }
        fireTimer -= dt;
        int guard = 0;
        while (fireTimer <= 0f && guard < 4)
        {
            fireTimer += 1f / Mathf.Max(fireRate, 1f);
            guard++;
            FireOne(isGunner);
        }
    }

    // 機体の真上（自分の機体を撃ってしまう向き）には向けない
    Vector3 LimitUp(Vector3 l)
    {
        float up = Mathf.Asin(Mathf.Clamp(l.y, -1f, 1f)) * Mathf.Rad2Deg;
        if (up <= maxUpAngle) return l;
        Vector3 flat = new Vector3(l.x, 0f, l.z);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        flat.Normalize();
        float a = maxUpAngle * Mathf.Deg2Rad;
        return flat * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
    }

    bool DesktopFire()
    {
        return !isVR && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter));
    }

    void UpdateInput(float dt)
    {
        if (!isVR)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) weapon = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) weapon = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) weapon = 2;
        }
        else
        {
            if (cycleInput && !cyclePrev) weapon = (weapon + 1) % 3;
            cyclePrev = cycleInput;
        }
        bool trig = useHeld || DesktopFire();
        bool down = trig && !trigPrev;
        trigPrev = trig;
        if (!canFire) return;   // 後ろの席からは撃てない
        if (down && weapon == 1) FireMissile(false);
        if (down && weapon == 2) FireMissile(true);
    }

    void FireOne(bool real)
    {
        if (muzzle == null) return;
        if (tracer != null) tracer.Emit(1);
        if (gunAudio != null && gunAudio.clip != null)
        {
            gunAudio.pitch = Random.Range(0.95f, 1.1f);
            gunAudio.PlayOneShot(gunAudio.clip, Random.Range(0.8f, 0.95f));
        }
        Vector3 origin = muzzle.position;
        Vector3 dir = Quaternion.AngleAxis(Random.Range(-gunSpread, gunSpread), muzzle.up)
                    * Quaternion.AngleAxis(Random.Range(-gunSpread, gunSpread), muzzle.right) * muzzle.forward;
        float sea = plane.seaLevelY;
        float waterDist = -1f;
        if (dir.y < -0.0001f)
        {
            float t = (sea - origin.y) / dir.y;
            if (t > 0f && t < gunRange) waterDist = t;
        }
        if (!real)
        {
            if (waterDist > 0f) plane._Splash(origin + dir * waterDist);
            return;
        }
        ammo--;
        plane._GunnerFired();   // 見つかっている間に撃つと、敵が攻撃してくる
        Transform car = plane.car.transform;
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, gunRange, groundMask, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(car)) continue;
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = i; }
        }
        if (waterDist > 0f && waterDist < bestDist) { plane._Splash(origin + dir * waterDist); return; }
        if (best < 0) return;
        RaycastHit h = hits[best];
        if (plane.impactFx != null)
        {
            plane.impactFx.transform.position = h.point;
            plane.impactFx.transform.rotation = Quaternion.LookRotation(h.normal);
            plane.impactFx.Emit(8);
        }
        FlightTarget tg = plane._FindTarget(h.collider);
        if (tg != null) { tg._Hit(); plane._NoteHit(tg); }   // 撃墜数のため
        else plane._PvpHit(h.collider, false);   // ほかのプレイヤーの機体
    }

    // ---------------- ミサイル ----------------
    void FireMissile(bool isGuided)
    {
        if (muzzle == null) return;
        if (Time.time - lastMissile < missileCooldown) return;
        if (isGuided)
        {
            if (guided <= 0) return;
            if (!locked) { noLockUntil = Time.time + 1.2f; return; }
            guided--;
        }
        else
        {
            if (rockets <= 0) return;
            rockets--;
        }
        lastMissile = Time.time;
        plane._GunnerFired();
        plane._LaunchMissileFrom(true, isGuided ? lockCand : -1, muzzle.position + muzzle.forward * 1.5f, muzzle.forward);
        SendCustomNetworkEvent(NetworkEventTarget.All, isGuided ? nameof(NetGuided) : nameof(NetRocket));
    }

    public void NetRocket()
    {
        if (isGunner || muzzle == null) return;
        plane._LaunchMissileFrom(false, -1, muzzle.position + muzzle.forward * 1.5f, muzzle.forward);
    }

    public void NetGuided()
    {
        if (isGunner || muzzle == null) return;
        plane._LaunchMissileFrom(false, plane._FindLockFrom(muzzle.position, muzzle.forward, lockCone), muzzle.position + muzzle.forward * 1.5f, muzzle.forward);
    }

    void UpdateLock(float dt, Vector3 origin, Vector3 look)
    {
        int cand = (weapon == 2 && guided > 0) ? plane._FindLockFrom(origin, look, lockCone) : -1;
        if (cand != lockCand) { lockCand = cand; lockTimer = 0f; }
        else if (cand >= 0) lockTimer += dt;
        locked = cand >= 0 && lockTimer >= lockTime;
    }

    // ---------------- 表示 ----------------
    public override void PostLateUpdate()
    {
        if (!isGunner) return;
        VRCPlayerApi.TrackingData head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        if (hudRoot != null) hudRoot.transform.SetPositionAndRotation(head.position, head.rotation);
        if (lockBox == null) return;
        bool show = lockCand >= 0 && plane._TrackAlive(lockCand) && (locked || (Time.time * 8f) % 2f < 1.3f);
        if (lockBox.gameObject.activeSelf != show) lockBox.gameObject.SetActive(show);
        if (!show) return;
        Vector3 d = plane._TrackPos(lockCand) - head.position;
        float dist = d.magnitude;
        if (dist < 1f) return;
        Vector3 dir = d / dist;
        float size = Mathf.Clamp(plane._TrackRadius(lockCand) * 2.4f * 5f / dist, 0.18f, 2.5f);
        if (!locked) size *= 1f + 1.2f * (1f - Mathf.Clamp01(lockTimer / lockTime));
        lockBox.position = head.position + dir * 5f;
        lockBox.rotation = Quaternion.LookRotation(dir, head.rotation * Vector3.up);
        lockBox.localScale = new Vector3(size, size, size);
    }

    void UpdateHud()
    {
        if (hudText == null) return;
        string s = "<color=#5ff2ff>GUNNER</color>\n";
        s += Line(0, "1  GUN", ammo.ToString()) + "\n";
        s += Line(1, "2  RKT", rockets.ToString() + " / " + maxRockets.ToString()) + "\n";
        s += Line(2, "3  MSL", guided.ToString() + " / " + maxGuided.ToString()) + "\n";
        if (Time.time < noLockUntil) s += "<color=#ff3030>NO LOCK</color>";
        else if (weapon == 2)
        {
            if (guided <= 0) s += "<color=#ff3030>MSL EMPTY</color>";
            else if (locked) s += "<color=#ff3030>LOCK ON</color>";
            else if (lockCand >= 0) s += "<color=#ffc040>LOCKING...</color>";
            else s += "<color=#3a8a98>SEARCH</color>";
        }
        hudText.text = s;
    }

    string Line(int w, string name, string val)
    {
        if (weapon == w) return "<color=#ffc040>> " + name + "   " + val + "</color>";
        return "<color=#3a8a98>  " + name + "   " + val + "</color>";
    }

    // ---------------- 入力 ----------------
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!isGunner) return;
        if (isVR && args.handType == HandType.LEFT) cycleInput = value;
        else useHeld = value;
    }

    public override void InputJump(bool value, UdonInputEventArgs args)
    {
        if (!value || !isGunner || solo || station == null) return;
        station.ExitStation(Networking.LocalPlayer);
    }
}
