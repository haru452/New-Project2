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
    void UpdateCockpit()
    {
        if (cockpitRoot == null || rb == null) return;
        Transform ct = car.transform;
        Vector3 fwd, up, right;
        if (axisReference != null) { fwd = axisReference.forward; up = axisReference.up; right = axisReference.right; }
        else { fwd = -ct.right; up = ct.up; right = ct.forward; }

        float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;     // 右翼下げ +
        float hdg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (hdg < 0f) hdg += 360f;
        float vs = rb.velocity.y;
        float stallLimit = stallAngle - flapStallReduction * flapPos;

        if (cpSpeed != null) cpSpeed.text = (airspeed * 3.6f).ToString("F0");
        if (cpAlt != null) cpAlt.text = altitude.ToString("F0");
        if (cpVs != null) cpVs.text = "VS " + (vs >= 0f ? "+" : "") + vs.ToString("F1") + " m/s";
        if (cpAtt != null) cpAtt.text = "P " + (pitch >= 0f ? "+" : "") + pitch.ToString("F0") + "°  R " + (roll >= 0f ? "+" : "") + roll.ToString("F0") + "°";
        if (cpHdg != null)
        {
            string hc = hp > 60f ? "#7fff9f" : (hp > 30f ? "#ffc040" : "#ff3030");
            cpHdg.text = "HDG " + Mathf.RoundToInt(hdg % 360f).ToString("000") + " " + CompassName(hdg);
        }
        // 時計のようなメーター（針は時計回り）
        if (cpSpdNeedle != null)
        {
            float kmh = Mathf.Clamp(airspeed * 3.6f, 0f, 300f);
            cpSpdNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-135f + kmh / 300f * 270f));
        }
        float altC = Mathf.Max(altitude, 0f);
        if (cpAltNeedle != null) cpAltNeedle.localRotation = Quaternion.Euler(0f, 0f, -(altC % 1000f) / 1000f * 360f);
        if (cpAltNeedleK != null) cpAltNeedleK.localRotation = Quaternion.Euler(0f, 0f, -(altC % 10000f) / 10000f * 360f);
        if (cpVsiNeedle != null) cpVsiNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-90f + Mathf.Clamp(vs, -10f, 10f) * 17f));
        if (cpThrNeedle != null) cpThrNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-120f + Mathf.Clamp01(throttle) * 240f));
        if (cpFuelNeedle != null) cpFuelNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-60f + (fuelKg < 0f || fuelCapKg <= 0f ? 1f : Mathf.Clamp01(fuelKg / fuelCapKg)) * 120f));
        if (cpAoaNeedle != null) cpAoaNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-120f + Mathf.Clamp(aoaDeg, 0f, 25f) / 25f * 240f));
        if (cpHdgCard != null) cpHdgCard.localRotation = Quaternion.Euler(0f, 0f, hdg);
        if (cpHdgNum != null) cpHdgNum.text = Mathf.RoundToInt(hdg % 360f).ToString("000") + "°";
        if (cpVsNum != null) cpVsNum.text = (vs >= 0f ? "+" : "") + vs.ToString("F1");
        if (cpFuelPct != null) cpFuelPct.text = Mathf.RoundToInt((fuelKg < 0f || fuelCapKg <= 0f ? 1f : Mathf.Clamp01(fuelKg / fuelCapKg)) * 100f).ToString() + "%";
        if (cpTurnPlane != null)
        {
            // 旋回の速さ（度/秒）。1 秒に 3° で目盛り（20°）まで傾く
            float now = Time.time, rate = 0f;
            if (lastHdgT > 0f && now > lastHdgT) rate = Mathf.DeltaAngle(lastHdg, hdg) / (now - lastHdgT);
            lastHdgT = now; lastHdg = hdg;
            float cur = -cpTurnPlane.localEulerAngles.z;
            if (cur < -180f) cur += 360f;
            if (cur > 180f) cur -= 360f;
            float want = Mathf.Clamp(rate / 3f * 20f, -35f, 35f);
            cpTurnPlane.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Lerp(cur, want, 0.15f));
        }
        UpdateHud(pitch, roll, hdg, vs);
        if (cpAttDisc != null)
        {
            if (attMat == null) attMat = cpAttDisc.material;
            attMat.SetFloat("_Pitch", pitch);
            attMat.SetFloat("_Roll", roll);
        }

        // HP・弾数・武器
        if (cpHp != null)
        {
            string hc2 = hp > 60f ? "#7fff9f" : (hp > 30f ? "#ffc040" : "#ff3030");
            cpHp.text = "<color=" + hc2 + ">" + Mathf.CeilToInt(hp).ToString() + "</color><size=50%> / " + Mathf.CeilToInt(maxHp).ToString() + "</size>";
        }
        SetBar(cpHpBar, maxHp > 0f ? hp / maxHp : 0f, false);
        if (cpWeapons != null)
        {
            cpWeapons.text = WeaponLine(0, "1  GUN", ammo.ToString())
                + "\n" + WeaponLine(1, "2  RKT", missileLeft.ToString() + " / " + maxMissiles.ToString())
                + "\n" + WeaponLine(2, "3  MSL", guidedLeft.ToString() + " / " + maxGuided.ToString())
                + "\n<size=80%>   X  FLR  " + flares.ToString() + "</size>";
        }
        if (cpLock != null)
        {
            string lk = "";
            if (Time.time < noLockMsgUntil) lk = "<color=#ff3030>NO LOCK</color>";
            else if (weapon == 2)
            {
                if (guidedLeft <= 0) lk = "<color=#ff3030>MSL EMPTY</color>";
                else if (locked) lk = "<color=#ff3030>LOCK ON  " + (Vector3.Distance(radarTracks[lockCand].position, car.transform.position) / 1000f).ToString("F1") + "km</color>";
                else if (lockCand >= 0) lk = "<color=#ffc040>LOCKING...</color>";
                else lk = "<color=#3a8a98>SEARCH</color>";
            }
            cpLock.text = lk;
        }

        // 入室・退室の記録（0.5 秒ごと）
        if (joinLog != null && Time.time >= nextLogUpdate)
        {
            nextLogUpdate = Time.time + 0.5f;
            if (cpLog != null) cpLog.text = joinLog._GetText();
            if (cpPlayers != null) cpPlayers.text = "ONLINE  " + joinLog._GetPlayerCount().ToString();
            if (cpKills != null)
            {
                // 今回の出撃（撃墜されたら 0 から）の記録。2 人乗りなら 2 人の記録
                bool duoNow = HasCrewGunner();
                bool hardNow = gameMode != null && gameMode._IsHard();
                cpKills.text = (duoNow ? "<size=70%>DUO </size>" : "") + (hardNow ? "<size=70%><color=#ff4040>HARD </color></size>" : "")
                    + "<color=#ffd040>" + joinLog._RunScore(duoNow).ToString() + "pt</color>"
                    + "  <size=75%><color=#ff6a5a>AIR " + joinLog._RunAir(duoNow).ToString() + "</color>"
                    + " <color=#ffa030>SHIP " + joinLog._RunShips(duoNow).ToString() + "</color>"
                    + " <color=#ffe070>CV " + joinLog._RunCarriers(duoNow).ToString() + "</color></size>";
            }
        }

        if (cpAoa != null) cpAoa.text = aoaDeg.ToString("F1") + "°";
        if (cpFuel != null)
        {
            if (fuelKg < 0f) InitFuel();
            float fr = fuelKg / Mathf.Max(fuelCapKg, 0.0001f);
            float endur = fuelFlow > 0.00001f ? fuelKg / fuelFlow : 0f;
            int mm = Mathf.FloorToInt(endur / 60f), ss2 = Mathf.FloorToInt(endur % 60f);
            string fc = fr > 0.4f ? "#7fff9f" : (fr > 0.2f ? "#ffc040" : "#ff3030");
            cpFuel.text = "FUEL <color=" + fc + ">" + Mathf.CeilToInt(fr * 100f).ToString() + "%</color>"
                + "<size=75%>  " + (fuelKg / 0.72f).ToString("F1") + "L  " + (fuelFlow > 0.00001f ? mm.ToString() + ":" + ss2.ToString("00") : "--:--") + "</size>";
        }
        if (cpThr != null) cpThr.text = (throttle * 100f).ToString("F0") + "%";
        if (cpGun != null) cpGun.text = ammo.ToString() + "  <size=70%><color=#ffc040>MSL " + missileLeft.ToString() + "</color></size>";
        if (cpFlags != null)
        {
            string f = flapsDown ? "<color=#ffc040>FLAP</color>" : "<color=#9ab8a2>FLAP</color>";
            string brakeName = grounded ? "BRAKE" : (floating ? "W-BRAKE" : "AIR BRAKE");
            f += "\n" + (braking ? "<color=#ffc040>" + brakeName + "</color>" : "<color=#9ab8a2>BRAKE</color>");
            f += "\n" + (boosting ? "<color=#ff7040>BOOST</color>" : "<color=#9ab8a2>BOOST</color>");
            f += "\n" + (voiceEnabled ? "<color=#7fff9f>VOICE [M]</color>" : "<color=#9ab8a2>VOICE OFF [M]</color>");
            if (_HasGear())
            {
                if (gearAnim >= 1f) f += "\n<color=#7fff9f>GEAR DOWN [G]</color>";
                else if (gearAnim <= 0f) f += "\n<color=#9ab8a2>GEAR UP [G]</color>";
                else f += "\n<color=#ffc040>GEAR " + (gearDown ? "DOWN..." : "UP...") + "</color>";
            }
            cpFlags.text = f;
        }

        SetBar(cpThrBar, throttle, true);
        SetBar(cpAoaBar, Mathf.Clamp01(aoaDeg / (stallLimit + 5f)), true);
        SetBar(cpGunBar, maxAmmo > 0 ? (float)ammo / maxAmmo : 0f, false);

        if (cpHorizon != null)
        {
            cpHorizon.localRotation = Quaternion.Euler(0f, 0f, roll);
            Vector3 hp = cpHorizon.localPosition;
            hp.y = Mathf.Clamp(-pitch * 0.003f, -0.055f, 0.055f);
            cpHorizon.localPosition = hp;
        }

        if (cpWarn != null)
        {
            bool blink = (Time.time * 3f) % 2f < 1.2f;
            string w = "";
            if (Time.time - lastDamageTime < 1.5f) w = "<color=#ff3030>DAMAGE -" + Mathf.CeilToInt(lastDamage).ToString() + "</color>";
            else if (burnLevel >= 3) w = blink ? "<color=#ff3030>CRITICAL DAMAGE</color>" : "";
            else if (stallAmount > 0.05f) w = blink ? "<color=#ff3030>STALL</color>" : "";
            else if (burnLevel == 2) w = blink ? "<color=#ff7020>ENGINE FIRE</color>" : "";
            else if (fuelKg == 0f) w = blink ? "<color=#ff3030>NO FUEL</color>" : "";
            else if (fuelKg > 0f && fuelKg < fuelCapKg * 0.2f) w = "<color=#ffc040>LOW FUEL</color>";
            else if (!grounded && airspeed > 3f && aoaDeg > stallLimit - 3f) w = "<color=#ffc040>STALL WARNING</color>";
            else if (ammo <= 0 && weapon == 0) w = "<color=#ff3030>GUN EMPTY</color>";
            else if (!grounded && altitude < 15f && vs < -3f) w = blink ? "<color=#ff3030>PULL UP</color>" : "";
            cpWarn.text = w;
        }

        UpdateRadar();
        UpdateCockpitDamage();

        // 起動演出
        float bt = Time.time - bootStart;
        bool booting = bt < bootDuration;
        if (booting)
        {
            ApplyBootPanels(bt);
            // 2.4 秒まで真っ暗、そこから 3.9 秒にかけて外とコックピットが明るくなる
            SetBlackout(0f);   // 乗った時に真っ暗にしない
        }
        else if (!bootDone) { ApplyBootPanels(100f); SetBlackout(0f); bootDone = true; QueueVoice(voiceWelcome); }

        if (cpCenter != null && booting)
        {
            cpCenter.text = BootText(bt);
        }
        else if (cpCenter != null)
        {
            string cm = "";
            if (catapult != null)
            {
                if (catLaunching) cm = "<color=#ffc040><size=150%>LAUNCH!</size></color>";
                else if (catHold > 0f)
                    cm = "<color=#ffc040>LAUNCH IN\n<size=180%>" + Mathf.Max(catapult.holdTime - catHold, 0f).ToString("F1") + "</size></color>";
                else
                    cm = "<color=#66ccff>CATAPULT READY</color>\n"
                       + (isVR ? "<size=70%>Hold the LEFT GRIP and push your hand forward\nto raise the throttle to 100%. The catapult launches at 100%.</size>\n"
                               : "<size=70%>Hold SHIFT to raise the throttle to 100%.\nThe catapult launches at 100%.</size>\n")
                       + "<size=80%>THROTTLE  " + (throttle * 100f).ToString("F0") + "% / 100%</size>";
            }
            else if (boosting) cm = "<color=#ff7040>" + (boostTimer < boostLiftTime ? "BOOST  UP" : "BOOST  FORWARD") + "</color>";
            else if (gunnerMode) cm = "<size=60%><color=#7fff9f>GUNNER MODE  -  AUTOPILOT\nPress P to fly</color></size>";
            cpCenter.text = cm;
        }

        if (cpStatus != null)
        {
            string st = "";
            if (repairing) st = "<color=#7fff9f>REPAIR / REARM</color>";
            else if (radioCount > 0) st = "<color=#7fff9f>RADIO  " + radioCount.ToString() + " PILOT" + (radioCount > 1 ? "S" : "") + "</color>";
            cpStatus.text = st;
        }
    }

    // ---------------- 被弾時のコックピット ----------------
    // 段階 1: 小さく揺れる / 2: 赤い点滅・警報音・画面がちらつく / 3: さらに強く・中に煙
    // HUD の更新（線の位置は毎フレーム、文字は 1 秒に 10 回）
    void UpdateHud(float pitch, float roll, float hdg, float vs)
    {
        if (hudFpm == null && hudSpd == null) return;
        Transform cr = cockpitRoot.transform;
        float dt = Time.deltaTime;
        // G（機体の上方向にかかる力）
        Vector3 v = rb.velocity;
        if (!hudGInit || dt <= 0f) { hudLastVel = v; hudGInit = true; }
        else
        {
            Vector3 acc = (v - hudLastVel) / dt + Vector3.up * 9.81f;
            hudLastVel = v;
            float g = Vector3.Dot(acc, cr.up) / 9.81f;
            hudGNow = Mathf.Lerp(hudGNow, g, Mathf.Clamp01(dt * 8f));
            if (!grounded && hudGNow > hudGMax) hudGMax = hudGNow;
        }
        // 飛んでいる方向
        if (hudFpm != null)
        {
            Vector3 lv = cr.InverseTransformDirection(v);
            if (v.magnitude > 3f && lv.z > 0.1f)
            {
                float x = Mathf.Clamp(lv.x / lv.z, -0.36f, 0.36f), y = Mathf.Clamp(lv.y / lv.z, -0.2f, 0.36f);
                hudFpm.localPosition = new Vector3(x, y, 1f);
            }
            else hudFpm.localPosition = new Vector3(0f, 0f, 1f);
        }
        // 角度のはしご: 世界の水平・機首の方位に合わせる。前から外れた線と計器盤に重なる線は隠す
        if (hudLadder != null && hudLadderLines != null)
        {
            Vector3 f = cr.forward;
            hudLadder.rotation = Quaternion.Euler(0f, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f);
            for (int i = 0; i < hudLadderLines.Length; i++)
            {
                Transform ln = hudLadderLines[i];
                if (ln == null) continue;
                bool show = Mathf.Abs(hudLadderDeg[i] - pitch) < 25f;
                if (show)
                {
                    Vector3 ld = cr.InverseTransformDirection(ln.position - cr.position);
                    show = ld.z > 0.1f && ld.y / ld.z > -0.03f && ld.y / ld.z < 0.3f;
                }
                if (ln.gameObject.activeSelf != show) ln.gameObject.SetActive(show);
            }
        }
        if (hudRollPtr != null) hudRollPtr.localRotation = Quaternion.Euler(0f, 0f, roll);
        // 方位のテープ
        if (hudTapeTicks != null)
        {
            int n = hudTapeTicks.Length;
            float baseDeg = Mathf.Floor(hdg / 5f) * 5f;
            for (int i = 0; i < n; i++)
            {
                float td = baseDeg + (i - n / 2) * 5f;
                if (hudTapeTicks[i] == null) continue;
                hudTapeTicks[i].localPosition = new Vector3((td - hdg) * hudTapeScale, 0f, 0f);
                if (hudTapeLabels != null && hudTapeLabels[i] != null)
                {
                    int dd = Mathf.RoundToInt(Mathf.Repeat(td, 360f));
                    string want = dd % 10 == 0 ? (dd == 0 ? "N" : dd == 90 ? "E" : dd == 180 ? "S" : dd == 270 ? "W" : (dd / 10).ToString("00")) : "";
                    if (hudTapeLabels[i].text != want) hudTapeLabels[i].text = want;
                }
            }
        }
        // 棒と操縦桿
        if (hudThrFill != null) hudThrFill.localScale = new Vector3(Mathf.Clamp01(throttle), 1f, 1f);
        if (hudHpFill != null) hudHpFill.localScale = new Vector3(maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f, 1f, 1f);
        if (hudStickDot != null) hudStickDot.localPosition = new Vector3(Mathf.Clamp(inRoll, -1f, 1f) * hudStickSize, Mathf.Clamp(inPitch, -1f, 1f) * hudStickSize, 0f);
        if (hudYawDot != null) hudYawDot.localPosition = new Vector3(Mathf.Clamp(inYaw, -1f, 1f) * hudStickSize, -hudStickSize - 0.015f, 0f);

        // 文字
        if (Time.time < hudTextT) return;
        hudTextT = Time.time + 0.1f;
        if (hudHdg != null) hudHdg.text = Mathf.RoundToInt(hdg % 360f).ToString("000");
        if (hudSpd != null) hudSpd.text = (airspeed * 3.6f).ToString("F0");
        if (hudAoa != null)
        {
            float lim = stallAngle - flapStallReduction * flapPos;
            hudAoa.text = aoaDeg > lim - 3f ? "<color=#ff4030>" + aoaDeg.ToString("F1") + "</color>" : aoaDeg.ToString("F1");
        }
        if (hudMach != null) hudMach.text = (airspeed / 340f).ToString("F2");
        if (hudG != null) hudG.text = hudGNow.ToString("F1");
        if (hudMaxG != null) hudMaxG.text = hudGMax.ToString("F1");
        if (hudGun != null) hudGun.text = ammo.ToString();
        if (hudRkt != null) hudRkt.text = missileLeft.ToString();
        if (hudMsl != null) hudMsl.text = guidedLeft.ToString();
        if (hudAlt != null) hudAlt.text = altitude.ToString("F0");
        if (hudVs != null) hudVs.text = (vs >= 0f ? "+" : "") + vs.ToString("F1");
        if (hudFlags != null && hudFlags.Length >= 5)
        {
            HudFlag(hudFlags[0], braking);
            HudFlag(hudFlags[1], flapsDown);
            HudFlag(hudFlags[2], boosting);
            HudFlag(hudFlags[3], locked);
            HudFlag(hudFlags[4], voiceEnabled);
        }
    }
    void HudFlag(TextMeshPro t, bool on)
    {
        if (t == null) return;
        string nm = t.gameObject.name.Substring(1);
        string s = on ? "[ " + nm + " ]" : nm;
        if (t.text == s) return;
        t.text = s;
        t.color = on ? new Color(0.4f, 1f, 0.5f, 1f) : new Color(0.4f, 1f, 0.5f, 0.3f);
    }

    void UpdateCockpitDamage()
    {
        int lv = burnLevel;
        float t = Time.time;
        // 揺れ（コックピット全体を細かく震わせる）
        float amp = lv >= 3 ? 0.008f : (lv == 2 ? 0.004f : (lv == 1 ? 0.0015f : 0f));
        if (cockpitRoot != null)
            cockpitRoot.transform.localPosition = cockpitBasePos + (amp > 0f ? new Vector3(Random.Range(-amp, amp), Random.Range(-amp, amp), Random.Range(-amp, amp) * 0.5f) : Vector3.zero);
        // 赤い点滅
        if (damageTint != null && damageTintMat != null)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * (lv >= 3 ? 9f : 5f));
            float a = lv >= 3 ? 0.12f + 0.16f * pulse : (lv == 2 ? 0.04f + 0.09f * pulse : 0f);
            bool on = a > 0.001f;
            if (damageTint.enabled != on) damageTint.enabled = on;
            if (on) damageTintMat.SetColor("_Color", new Color(1f, 0.05f, 0.02f, a));
        }
        // 警報音
        if (alarmAudio != null)
        {
            if (lv >= 2 && !alarmAudio.isPlaying) alarmAudio.Play();
            else if (lv < 2 && alarmAudio.isPlaying) alarmAudio.Stop();
            alarmAudio.pitch = lv >= 3 ? 1.3f : 1f;
        }
        // 中の煙
        if (cabinSmoke != null)
        {
            var em = cabinSmoke.emission;
            em.rateOverTimeMultiplier = lv >= 3 ? 5f : 0f;
            if (lv >= 3 && !cabinSmoke.isPlaying) cabinSmoke.Play();
        }
        UpdatePanelStatic(lv, t);

        // 画面のちらつき（起動演出が終わっている時だけ）
        if (bootPanels != null && bootDone)
        {
            if (flickerPanel >= 0 && t >= flickerUntil)
            {
                if (bootPanels[flickerPanel] != null) bootPanels[flickerPanel].gameObject.SetActive(true);
                flickerPanel = -1;
            }
            if (flickerPanel < 0 && lv >= 2 && Random.value < Time.deltaTime * (lv >= 3 ? 4f : 1.2f))
            {
                flickerPanel = Random.Range(0, bootPanels.Length);
                flickerUntil = t + Random.Range(0.05f, 0.18f);
                if (bootPanels[flickerPanel] != null) bootPanels[flickerPanel].gameObject.SetActive(false);
            }
        }
    }

    // HP 25 未満: ときどきパネルが赤い砂嵐になる
    void UpdatePanelStatic(int lv, float t)
    {
        if (staticQuads == null) return;
        if (staticPanel >= 0 && (t >= staticUntil || lv < 3))
        {
            if (staticQuads[staticPanel] != null) staticQuads[staticPanel].gameObject.SetActive(false);
            staticPanel = -1;
        }
        if (staticPanel < 0 && lv >= 3 && bootDone && Random.value < Time.deltaTime * 0.8f)
        {
            int k = Random.Range(0, staticQuads.Length);
            if (staticQuads[k] != null)
            {
                staticPanel = k;
                staticUntil = t + Random.Range(0.25f, 0.9f);
                staticQuads[k].gameObject.SetActive(true);
            }
        }
        // 砂嵐を動かす（模様の位置を毎フレームでたらめにずらす）
        if (staticPanel >= 0 && staticMat != null)
            staticMat.SetTextureOffset("_MainTex", new Vector2(Random.value, Random.value));
    }

    void ClearCockpitDamage()
    {
        if (staticPanel >= 0 && staticQuads != null && staticQuads[staticPanel] != null) staticQuads[staticPanel].gameObject.SetActive(false);
        staticPanel = -1;
        if (cockpitRoot != null) cockpitRoot.transform.localPosition = cockpitBasePos;
        if (damageTint != null) damageTint.enabled = false;
        if (alarmAudio != null) alarmAudio.Stop();
        if (flickerPanel >= 0 && bootPanels != null && bootPanels[flickerPanel] != null) bootPanels[flickerPanel].gameObject.SetActive(true);
        flickerPanel = -1;
    }

    // 画面が「横線 → 上下に開く」ように点灯する（明滅あり）
    void ApplyBootPanels(float bt)
    {
        if (bootPanels == null || bootDelays == null) return;
        for (int i = 0; i < bootPanels.Length; i++)
        {
            if (bootPanels[i] == null) continue;
            float k = Mathf.Clamp01((bt - bootDelays[i]) / 0.35f);
            float sx = Mathf.Clamp01(k * 2f);
            float sy = k <= 0f ? 0f : Mathf.Clamp01(k * 2f - 1f) * 0.96f + 0.04f;
            bool flicker = k > 0f && k < 1f && Random.value < 0.25f;
            bootPanels[i].localScale = flicker ? Vector3.zero : new Vector3(sx, sy, 1f);
        }
    }

    // 周りの暗さ（1 = 真っ暗、0 = 普通に見える）
    void SetBlackout(float a)
    {
        if (blackout == null || blackoutMat == null) return;
        bool on = a > 0.001f;
        if (blackout.enabled != on) blackout.enabled = on;
        if (on) blackoutMat.SetColor("_Color", new Color(0f, 0f, 0f, a));
    }

    // 目の前に流れるシステムチェック（最初の 0.6 秒は真っ暗で何も出ない）
    string BootText(float bt)
    {
        bt -= 0.6f;
        if (bt < 0f) return "";
        if (bt > 2.2f)
        {
            bool blink = (bt * 6f) % 2f < 1.3f;
            return blink ? "<color=#7fff9f><size=120%>ALL SYSTEMS GO</size></color>" : "";
        }
        string t = "<color=#66ccff><size=80%>FLIGHT CONTROL SYSTEM  v2.6</size></color>\n";
        for (int i = 0; i < 5; i++)
        {
            float ti = 0.35f + i * 0.28f;
            if (bt < ti) break;
            string name = i == 0 ? "POWER" : (i == 1 ? "AVIONICS" : (i == 2 ? "ENGINE" : (i == 3 ? "WEAPONS" : "RADIO LINK")));
            bool ok = bt > ti + 0.15f;
            t += "<size=60%>" + name + "  ........  " + (ok ? "<color=#7fff9f>OK</color>" : "<color=#ffc040>CHECK</color>") + "</size>\n";
        }
        return t;
    }

    // 飛んでいるミサイルをレーダーに出す（速いので光を待たずに毎フレーム。点滅）
    void UpdateMissileBlips(Vector3 me, Vector3 f, Vector3 r)
    {
        if (radarMslDots == null) return;
        int used = 0;
        bool blinkOn = (Time.time * 6f) % 2f < 1.4f;
        if (radarPlanes != null)
        {
            for (int p = 0; p < radarPlanes.Length; p++)
            {
                controller pc = radarPlanes[p];
                if (pc == null) continue;
                int n = pc._MslCount();
                for (int i = 0; i < n && used < radarMslDots.Length; i++)
                {
                    if (!pc._MslActive(i)) continue;
                    if (PlaceMslBlip(used, pc._MslPos(i), pc == this ? 0 : 1, me, f, r, true)) used++;
                }
            }
        }
        if (radarShips != null)
        {
            for (int s = 0; s < radarShips.Length; s++)
            {
                WarShip ws = radarShips[s];
                if (ws == null) continue;
                int n = ws._RocketCount();
                for (int i = 0; i < n && used < radarMslDots.Length; i++)
                {
                    if (!ws._RocketActive(i)) continue;
                    if (PlaceMslBlip(used, ws._RocketPos(i), 2, me, f, r, blinkOn)) used++;
                }
            }
        }
        if (radarFighters != null)
        {
            for (int f2 = 0; f2 < radarFighters.Length && used < radarMslDots.Length; f2++)
            {
                EnemyDrone ef = radarFighters[f2];
                if (ef == null || !ef._MslActive()) continue;
                if (PlaceMslBlip(used, ef._MslPos(), ef._MslGuided() ? 3 : 2, me, f, r, blinkOn)) used++;   // 追尾ミサイルは紫
            }
        }
        for (int i = used; i < radarMslDots.Length; i++)
            if (radarMslDots[i] != null && radarMslDots[i].gameObject.activeSelf) radarMslDots[i].gameObject.SetActive(false);
    }

    bool PlaceMslBlip(int k, Vector3 pos, int kind, Vector3 me, Vector3 f, Vector3 r, bool show)
    {
        Transform dot = radarMslDots[k];
        if (dot == null) return false;
        Vector3 d = pos - me;
        d.y = 0f;
        if (d.magnitude > radarRange) return false;
        dot.localPosition = new Vector3(Vector3.Dot(d, r) / radarRange * radarRadius, Vector3.Dot(d, f) / radarRange * radarRadius, dot.localPosition.z);
        if (radarMslMats != null && kind < radarMslMats.Length && radarMslRenderers != null && k < radarMslRenderers.Length && radarMslRenderers[k] != null)
            radarMslRenderers[k].sharedMaterial = radarMslMats[kind];
        if (dot.gameObject.activeSelf != show) dot.gameObject.SetActive(show);
        return true;
    }

    // レーダー: 光が回り、光が通った所の点だけ新しい位置に更新される（本物のレーダーと同じ）
}
