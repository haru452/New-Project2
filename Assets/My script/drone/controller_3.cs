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
    void Simulate(float dt)
    {
        Transform ct = car.transform;
        Vector3 fwd, up, right;
        if (axisReference != null)
        {
            fwd = axisReference.forward; up = axisReference.up; right = axisReference.right;
        }
        else
        {
            fwd = -ct.right; up = ct.up; right = ct.forward;
        }

        // 操縦していない時は舵中立・アイドル
        float tElev = 0f, tAil = 0f, tRud = 0f;
        if (isPiloting && gunnerMode && !grounded && !floating)
        {
            // ガンナーモード中の自動操縦: ときどき向きを変えながら飛ぶ（傾きは 25° まで＝ゆるい旋回）
            float apPitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float apRoll = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;   // 右翼下げ +
            if (Time.time >= apNextChange)
            {
                apNextChange = Time.time + Random.Range(7f, 15f);
                float r = Random.value;
                apTargetRoll = r < 0.4f ? 0f : (r < 0.7f ? Random.Range(12f, 25f) : -Random.Range(12f, 25f));
            }
            float tgtRoll = apTargetRoll;
            // 遠くへ行きすぎたら（中心から 4km）中心の方へ曲がる
            Vector3 toC = new Vector3(150f - ct.position.x, 0f, 75f - ct.position.z);
            if (toC.magnitude > 4000f)
            {
                Vector3 rf = new Vector3(right.x, 0f, right.z);
                tgtRoll = Vector3.Dot(toC, rf) > 0f ? 25f : -25f;
            }
            // 高さ: 120m より低ければ上昇、450m より高ければ降下、それ以外は高さを保つ
            float bias = altitude < 120f ? 6f : (altitude > 450f ? -3f : 0f);
            // 前に島や船があれば、翼を水平にして上昇
            if (Physics.Raycast(ct.position, fwd, 350f, groundMask, QueryTriggerInteraction.Ignore)) { bias = 12f; tgtRoll = 0f; }
            float wantPitch = Mathf.Clamp(-rb.velocity.y * 1.2f, -6f, 8f) + 2f + bias;
            if (stallAmount > 0.05f || aoaDeg > stallAngle - 3f) wantPitch = Mathf.Min(wantPitch, apPitch - 3f);   // 失速しそうなら機首を下げる
            tElev = Mathf.Clamp((wantPitch - apPitch) / 12f, -0.5f, 0.5f);
            tAil = Mathf.Clamp((tgtRoll - apRoll) / 25f, -0.6f, 0.6f);
            if (throttle < 0.6f) throttle = 0.6f;   // 自動操縦中は速度を落としすぎない
        }
        else if (isPiloting && !gunnerMode)
        {
            tElev = invertPitch ? inPitch : -inPitch;   // 引く(S/スティック手前)で機首上げ
            tAil = inRoll;
            tRud = inYaw;
        }
        // 操縦桿をなめらかに動かす（キーを押した瞬間にいっぱいまで引かない）
        // デスクトップは controlRampTime 秒でいっぱいまで。VR はスティックなのでほぼそのまま
        float rate = isVR ? 6f : 1f / Mathf.Max(controlRampTime, 0.01f);
        sElev = Ramp(sElev, tElev, rate, dt);
        sAil = Ramp(sAil, tAil, rate * 1.5f, dt);
        sRud = Ramp(sRud, tRud, rate, dt);
        float elev = sElev, ail = sAil, rud = sRud;
        flapPos = Mathf.MoveTowards(flapPos, flapsDown ? 1f : 0f, 0.4f * dt);

        // ---- カタパルト（接続中・射出中はここで処理して終わり）----
        if (catapult != null && CatapultStep(dt, fwd, up)) return;

        // ---- 大気 ----
        altitude = ct.position.y - seaLevelY;

        // ---- 着水: 強く当たったら跳ね返る、そっと降りたら波に乗って浮く ----
        bool onWater = false;
        float waveH = 0f;
        waveNormal = Vector3.up;
        if (altitude < 8f) waveH = WaveHeight(ct.position.x, ct.position.z);
        float aboveWave = altitude - waveH;
        bool touching = aboveWave < waterContactHeight;
        if (floating && aboveWave < waterContactHeight + 0.6f) onWater = true;   // 水面すれすれは水上扱い（タクシー・抵抗）
        // 着水はできない: 甲板や地面の上でなく海面に当たったら、行動不能になって沈む
        if (touching && catapult == null && !crashed && !Physics.Raycast(ct.position, -up, gearHeightDown + 0.5f, groundMask, QueryTriggerInteraction.Ignore))
        {
            CrashIntoSea();
            return;
        }
        if (false)
        {
            if (!floating)
            {
                // 着水できる条件: 降下がゆっくり・速すぎない・翼が水平・機首が下がっていない
                float sink = -rb.velocity.y;
                float pitchW = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
                float rollW = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;
                bool tooHard = sink > maxSinkRate
                            || rb.velocity.magnitude > maxWaterSpeed
                            || Mathf.Abs(rollW) > maxWaterRoll
                            || pitchW < -maxWaterNoseDown;
                if (tooHard)
                {
                    // 強く当たった分だけ HP が減る
                    Damage(Mathf.Clamp(10f + Mathf.Max(sink - maxSinkRate, 0f) * 8f
                                       + Mathf.Max(rb.velocity.magnitude - maxWaterSpeed, 0f) * 1.5f, 10f, 80f));
                    if (crashed) return;
                    // 爆発はせず、水面で跳ね返る（勢いは弱まる）
                    Vector3 bv = rb.velocity;
                    bv.y = Mathf.Max(Mathf.Abs(bv.y) * waterBounce, 1.5f);
                    bv.x *= 0.75f;
                    bv.z *= 0.75f;
                    rb.velocity = bv;
                    rb.angularVelocity *= 0.3f;
                    Vector3 bp = rb.position;
                    bp.y = seaLevelY + waveH + waterContactHeight + 0.05f;
                    rb.position = bp;
                    if (splashOnBounce && waterSplashFx != null)
                    {
                        waterSplashFx.transform.position = new Vector3(bp.x, seaLevelY, bp.z);
                        waterSplashFx.Emit(30);
                    }
                    return;
                }
            }
            floating = true;
            onWater = true;
            Vector3 fv = rb.velocity;
            if (fv.y < 0f) fv.y = 0f;
            rb.velocity = fv;
            Vector3 fp = rb.position;
            fp.y = seaLevelY + waveH + waterContactHeight;   // 波の上に乗る
            rb.position = fp;
        }
        else if (aboveWave > waterContactHeight + 0.6f) floating = false;
        float rho = 1.225f * Mathf.Exp(-Mathf.Max(altitude, 0f) / scaleHeight);
        rho *= Mathf.Clamp01((spaceAltitude - altitude) / 200f);
        float g = altitude >= spaceAltitude ? gravity * spaceGravityScale : gravity;
        rb.AddForce(Vector3.down * g, ForceMode.Acceleration);

        Vector3 vel = rb.velocity;
        float V = vel.magnitude;
        airspeed = V;
        float vf = Vector3.Dot(vel, fwd);
        float vu = Vector3.Dot(vel, up);
        float vr = Vector3.Dot(vel, right);

        float alpha = Mathf.Atan2(-vu, vf);                       // 迎角 (rad)
        float beta = V > 0.5f ? Mathf.Asin(Mathf.Clamp(vr / V, -1f, 1f)) : 0f; // 横滑り角
        aoaDeg = V > 2f ? alpha * Mathf.Rad2Deg : 0f;

        float S = wingArea;
        float b = wingSpan;
        float c = wingArea / wingSpan;
        float AR = b * b / S;
        float clAlpha = 2f * Mathf.PI * AR / (AR + 2f);
        float a0 = zeroLiftAoA * Mathf.Deg2Rad;
        float stallPos = (stallAngle - flapStallReduction * flapPos) * Mathf.Deg2Rad;
        float stallNeg = negativeStallAngle * Mathf.Deg2Rad;
        float trans = Mathf.Max(stallTransition, 0.5f) * Mathf.Deg2Rad;

        // ---- 失速の度合い 0..1 ----
        float s = 0f;
        if (alpha > stallPos) s = Mathf.Clamp01((alpha - stallPos) / trans);
        else if (alpha < stallNeg) s = Mathf.Clamp01((stallNeg - alpha) / trans);
        s = s * s * (3f - 2f * s);
        stallAmount = V > 3f ? s : 0f;

        float qbar = 0.5f * rho * V * V;

        if (V > 0.5f)
        {
            // ---- 揚力係数 ----
            float clFlap = flapLift * flapPos;
            float clMax = clAlpha * (stallPos - a0) + clFlap;
            float clMin = clAlpha * (stallNeg - a0) + clFlap;
            float clPre = Mathf.Clamp(clAlpha * (alpha - a0) + clFlap, clMin, clMax);
            float clPost = postStallLift * Mathf.Sin(2f * alpha);
            float CL = Mathf.Lerp(clPre, clPost, s);

            // ---- 抗力係数（有害抗力 + 誘導抗力 / 失速後は平板）----
            float sinA = Mathf.Sin(alpha);
            float sinB = Mathf.Sin(beta);
            float cdPre = parasiteDrag + flapDrag * flapPos + clPre * clPre / (Mathf.PI * oswaldEfficiency * AR);
            float cdPost = parasiteDrag + flapDrag * flapPos + 1.9f * sinA * sinA;
            float CD = Mathf.Lerp(cdPre, cdPost, s) + 0.5f * sinB * sinB;
            if (braking && isPiloting) CD += airBrakeDrag;   // B: 空中ではエアブレーキ
            CD += propWindmillDrag * (1f - throttle);         // 絞るとプロペラがブレーキになる

            Vector3 velDir = vel / V;
            Vector3 liftDir = Vector3.Cross(velDir, right);
            if (liftDir.sqrMagnitude > 0.0001f) liftDir.Normalize();

            Vector3 aero = liftDir * (CL * qbar * S)
                         - velDir * (CD * qbar * S)
                         + right * (-cyBeta * beta * qbar * S);
            rb.AddForce(aero, ForceMode.Force);

            // ---- モーメント ----
            Vector3 w = rb.angularVelocity;
            float p = -Vector3.Dot(w, fwd);     // ロール（右翼下げ +）※Unityでは+fwd回りが左ロール
            float q = -Vector3.Dot(w, right);   // ピッチ（機首上げ +）
            float r = Vector3.Dot(w, up);       // ヨー（機首右 +）
            float Vs = Mathf.Max(V, 5f);
            float ph = p * b / (2f * Vs);
            float qh = q * c / (2f * Vs);
            float rh = r * b / (2f * Vs);

            float ailEff = 1f - 0.6f * s;
            float clpEff = clRollDamping * (1f - 1.6f * s);   // 失速中はロール減衰が反転 → オートローテーション
            float wingDrop = stallWingDrop * s * (Mathf.PerlinNoise(Time.time * 0.6f, noiseSeed) - 0.5f) * 2f;

            // 速い時は舵の効きを弱める（G リミッター）。手を離した時は水平飛行（1G）になるように釣り合わせる
            float qRef = 0.5f * rho * pitchRefSpeed * pitchRefSpeed;
            float elevK = qbar > qRef ? qRef / qbar : 1f;
            float ailK = V > rollRefSpeed ? rollRefSpeed / V : 1f;
            float ailC = ail * ailK;
            float a1g = mass * 9.81f / (Mathf.Max(qbar, 1f) * S * clAlpha) + a0;
            float cmT = Mathf.Min(cm0, -cmAlpha * Mathf.Sin(a1g));
            float L = clBeta * beta + clAileron * ailC * ailEff + clpEff * ph + clYawRate * rh + wingDrop;
            float M = cmT + cmAlpha * Mathf.Sin(alpha) + cmElevator * elev * elevK + cmPitchDamping * qh;
            float N = cnBeta * sinB + cnRudder * rud * elevK + cnYawDamping * rh + cnAileron * ailC;

            float rollAcc = L * qbar * S * b / inertiaRoll;
            float pitchAcc = M * qbar * S * c / inertiaPitch;
            float yawAcc = N * qbar * S * b / inertiaYaw;
            rb.AddTorque(-fwd * rollAcc - right * pitchAcc + up * yawAcc, ForceMode.Acceleration);
        }

        // ---- 推力 ----
        // スロットル（Shift/Ctrl で決めた値）どおりに推力を出す。
        // 速度が上がると少し弱まり、空気が薄くても完全には止まらない（勝手にエンジンが切れたように感じないように）
        float propFactor = propMaxSpeed > 0f ? Mathf.Clamp(1f - Mathf.Max(vf, 0f) / propMaxSpeed, minEngineFactor, 1f) : 1f;
        float airFactor = Mathf.Max(rho / 1.225f, minEngineFactor);
        float thrust = maxThrust * throttle * airFactor * propFactor;

        rb.AddForce(fwd * thrust, ForceMode.Force);
        if (isPiloting) BurnFuel(dt, airFactor);

        // T: 機首の向き（前）へだけ最大加速する（上へは押さない）
        boosting = false;
        if (boostTimer >= 0f && isPiloting)
        {
            boosting = true;
            throttle = 1f;                                                       // エンジンも全開
            rb.AddForce(fwd * boostForward, ForceMode.Force);
            boostTimer += dt;
            if (boostTimer > boostLiftTime + boostForwardTime) boostTimer = -1f;
        }
        else boostTimer = -1f;

        // ---- 水上（タクシー）----
        if (onWater)
        {
            float wvf = Vector3.Dot(rb.velocity, fwd);
            float wvr = Vector3.Dot(rb.velocity, right);
            Vector3 ww = rb.angularVelocity;
            // 水の抵抗（横すべりは強く止め、前後は少しだけ）
            rb.AddForce(right * Mathf.Clamp(-wvr * 3f, -6f, 6f), ForceMode.Acceleration);
            rb.AddForce(-fwd * wvf * (braking && isPiloting ? 0.6f : 0.08f), ForceMode.Acceleration);   // B: 水上ブレーキ
            // ラダー（Q/E）でその場で向きを変える。速いほど効きを弱める
            float turnFactor = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(wvf) / 20f));
            float targetYawW = rud * waterTurnRate * Mathf.Deg2Rad * turnFactor;
            float yawErrW = targetYawW - Vector3.Dot(ww, up);
            rb.AddTorque(up * yawErrW * 6f, ForceMode.Acceleration);
            // 波の面に沿って傾く
            Vector3 alignW = Vector3.Cross(up, waveNormal);
            float lowSpeedW = 1f - Mathf.Clamp01(airspeed / 10f);
            rb.AddTorque(fwd * (Vector3.Dot(alignW, fwd) * 25f - Vector3.Dot(ww, fwd) * 8f), ForceMode.Acceleration);
            rb.AddTorque(right * ((Vector3.Dot(alignW, right) * 25f - Vector3.Dot(ww, right) * 8f) * lowSpeedW - Vector3.Dot(ww, right) * 2f), ForceMode.Acceleration);
        }

        // ---- 地上 ----
        RaycastHit hit;
        float gh = _HasGear() && gearAnim > 0.97f ? gearHeightDown : gearHeight;   // 脚を出していればタイヤの高さ、しまっていれば胴体
        grounded = Physics.Raycast(ct.position, -up, out hit, gh, groundMask, QueryTriggerInteraction.Ignore);
        // 脚を出して飛んでいる時は空気抵抗が少し増える
        if (!grounded && _HasGear() && gearAnim > 0.05f && airspeed > 1f)
            rb.AddForce(-rb.velocity.normalized * airspeed * airspeed * gearDrag * gearAnim, ForceMode.Acceleration);
        if (grounded)
        {
            vf = Vector3.Dot(rb.velocity, fwd);
            vr = Vector3.Dot(rb.velocity, right);
            Vector3 w = rb.angularVelocity;

            // タイヤの横グリップ
            float lat = Mathf.Clamp(-vr * lateralGrip, -8f, 8f);
            rb.AddForce(right * lat, ForceMode.Acceleration);

            // 転がり抵抗 + ブレーキ
            float decel = rollingResistance * g + (braking && isPiloting ? brakeDecel : 0f);
            float absVf = Mathf.Abs(vf);
            if (absVf > 0.05f)
            {
                float d = Mathf.Min(decel, absVf / dt);
                rb.AddForce(-fwd * Mathf.Sign(vf) * d, ForceMode.Acceleration);
            }

            // 前輪ステアリング（低速ほど効く）
            float steerFactor = Mathf.Clamp01(absVf / 2f) * Mathf.Clamp01(1f - absVf / 40f);
            float targetYaw = rud * groundSteerRate * Mathf.Deg2Rad * steerFactor * Mathf.Sign(vf);
            float yawErr = targetYaw - Vector3.Dot(w, up);
            rb.AddTorque(up * yawErr * 6f, ForceMode.Acceleration);

            // 脚で姿勢を保つ（転がり・ひっくり返り防止）
            // 機体の上方向を地面の法線にそろえる。ピッチは速度が出ると弱め、引き起こし（離陸）できるようにする
            float lowSpeed = 1f - Mathf.Clamp01(airspeed / 30f);
            Vector3 align = Vector3.Cross(up, hit.normal);        // 上方向を法線へ回す回転軸
            float rollErr = Vector3.Dot(align, fwd);
            float pitchErr = Vector3.Dot(align, right);
            float rollRate = Vector3.Dot(w, fwd);
            float pitchRate = Vector3.Dot(w, right);
            rb.AddTorque(fwd * (rollErr * 25f - rollRate * 8f), ForceMode.Acceleration);
            rb.AddTorque(right * ((pitchErr * 25f - pitchRate * 8f) * lowSpeed - pitchRate * 2f), ForceMode.Acceleration);
        }
    }

    // 海のシェーダーと同じ式で、その場所の波の高さと向きを求める
    float WaveHeight(float x, float z)
    {
        if (!haveWaves) return 0f;
        float t = (float)(Networking.GetServerTimeInSeconds() % 3600.0) + 1f;
        Vector3 tng = new Vector3(1f, 0f, 0f);
        Vector3 bin = new Vector3(0f, 0f, 1f);
        float h = 0f;
        for (int i = 0; i < 3; i++)
        {
            Vector4 w = i == 0 ? waveA : (i == 1 ? waveB : waveC);
            float steep = w.z * waveScale;
            float k = 6.28318f / w.w;
            float c = Mathf.Sqrt(9.8f / k);
            Vector2 d = new Vector2(w.x, w.y).normalized;
            float f = k * (d.x * x + d.y * z - c * t);
            float a = steep / k;
            float sf = Mathf.Sin(f);
            float cf = Mathf.Cos(f);
            h += a * sf;
            tng += new Vector3(-d.x * d.x * steep * sf, d.x * steep * cf, -d.x * d.y * steep * sf);
            bin += new Vector3(-d.x * d.y * steep * sf, d.y * steep * cf, -d.y * d.y * steep * sf);
        }
        waveNormal = Vector3.Cross(bin, tng).normalized;
        return h;
    }

    // 目標へ一定の速さで近づける。戻すときは 2 倍の速さ（手を離すと操縦桿はすぐ戻る）
    float Ramp(float cur, float tgt, float rate, float dt)
    {
        float r = (Mathf.Abs(tgt) < Mathf.Abs(cur) || tgt * cur < 0f) ? rate * 2f : rate;
        return Mathf.MoveTowards(cur, tgt, r * dt);
    }

    // ---------------- カタパルト ----------------
    public void _SetCatapult(Catapult c)
    {
        if (catapult == c || catLaunching || Time.time < catCooldownUntil) return;
        catapult = c;
        catHold = 0f;
    }

    public void _ClearCatapult(Catapult c)
    {
        if (catLaunching) return;
        if (catapult == c) catapult = null;
    }

    // true を返したフレームは通常の飛行計算をしない
    bool CatapultStep(float dt, Vector3 fwd, Vector3 up)
    {
        Transform cp = catapult.transform;
        Vector3 cf = cp.forward;
        Vector3 cu = cp.up;
        altitude = car.transform.position.y - seaLevelY;
        stallAmount = 0f;
        aoaDeg = 0f;

        if (!catLaunching)
        {
            // 別の場所へリセットされた等で離れていたら接続解除
            if (Vector3.Distance(rb.position, cp.position) > 8f)
            {
                catapult = null;
                return false;
            }
            ammo = maxAmmo;   // カタパルト上で弾を補充
            missileLeft = maxMissiles; guidedLeft = maxGuided; flares = maxFlares;
            // 接続中: シャトルに固定されているので前後左右には動かない
            Vector3 v = rb.velocity;
            rb.velocity = new Vector3(0f, Mathf.Min(v.y, 0f), 0f);
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            airspeed = 0f;
            grounded = true;

            if (isPiloting && throttle >= 0.99f)
            {
                if (catHold <= 0f && Time.time >= nextLaunchVoice)
                {
                    nextLaunchVoice = Time.time + 6f;
                    QueueVoice(voiceLaunching);
                }
                catHold += dt;
                if (catHold >= catapult.holdTime)
                {
                    // 射出開始: 機首を射出方向に合わせて開始点へ
                    Quaternion inv = Quaternion.Inverse(rb.rotation);
                    Vector3 lf = inv * fwd;
                    Vector3 lu = inv * up;
                    // 機首を launchPitch 度上げた姿勢でレールに乗せる（+right 回りの回転は機首下げなので負の角度）
                    Quaternion noseUp = Quaternion.AngleAxis(-catapult.launchPitch, cp.right);
                    catRot = Quaternion.LookRotation(noseUp * cf, noseUp * cu) * Quaternion.Inverse(Quaternion.LookRotation(lf, lu));
                    rb.position = cp.position + cu * _GearLift();   // 脚を出していればタイヤの分だけ上
                    rb.rotation = catRot;
                    rb.velocity = Vector3.zero;
                    catLaunching = true;
                    PlayVoiceNow(voiceGo);
                    catDist = 0f;
                    catSpeed = 0f;
                }
            }
            else catHold = 0f;
            return true;
        }

        // 射出中: レールに沿って一定加速
        float stroke = Mathf.Max(catapult.strokeLength, 1f);
        float vEnd = catapult.launchSpeed;
        float acc = vEnd * vEnd / (2f * stroke);
        catSpeed = Mathf.Min(catSpeed + acc * dt, vEnd);
        catDist += catSpeed * dt;

        Vector3 rail = cp.position + cf * catDist + cu * _GearLift();
        Vector3 err = rail - rb.position;
        rb.velocity = cf * catSpeed + err * 10f;
        rb.rotation = catRot;
        rb.angularVelocity = Vector3.zero;
        airspeed = catSpeed;
        grounded = true;

        if (catDist >= stroke)
        {
            // 射出完了: 少し上向きに放り出す。ここからは通常の飛行
            Vector3 releaseDir = Quaternion.AngleAxis(-catapult.climbAngle, cp.right) * cf;
            rb.velocity = releaseDir * vEnd;
            catLaunching = false;
            armedAt = Time.time + 3f;   // 潜水艦から射出して 3 秒後から、敵の警告が始まる
            catapult = null;
            catHold = 0f;
            catCooldownUntil = Time.time + 3f;
        }
        return true;
    }

    // ---------------- 海に落ちた: 行動不能になって沈む ----------------
    public float sinkSpeed = 1.3f;          // 沈む速さ（m/s）
    public float sinkSeconds = 9f;          // 沈んでから格納庫に戻るまで（秒）
    bool sinking;
    void CrashIntoSea()
    {
        sinking = true;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSink));
        Crash();
    }
    float lastSinkTime = -10f;
    public void NetSink()
    {
        lastSinkTime = Time.time;
        Vector3 p = car.transform.position;
        if (waterSplashFx != null)
        {
            waterSplashFx.transform.position = new Vector3(p.x, seaLevelY, p.z);
            waterSplashFx.Emit(80);
        }
    }

    // ---------------- 墜落 ----------------
    void Crash()
    {
        ReconAbort();
        if (isPiloting && hostile && gameMode != null) gameMode._ReportOwnLoss();   // 戦闘中に撃墜された（ワールド全体に）
        if (isPiloting && joinLog != null) joinLog._ResetRun();   // 撃墜された → 今回の記録は 0 から
        ClearHostile();   // 次の出撃は、また警告から
        armedAt = -1f;
        crashed = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        throttle = 0f;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetCrash));
        if (isPiloting && station != null)
        {
            station.ExitStation(Networking.LocalPlayer);
            SendCustomEventDelayedSeconds(nameof(_TeleportPilotHome), 0.2f);
        }
        SendCustomEventDelayedSeconds(nameof(_AfterCrash), sinking ? sinkSeconds : 3f);
    }

    // ---------------- 燃える演出 ----------------
    public void NetBurn0() { SetBurn(0); }
    public void NetBurn1() { SetBurn(1); }
    public void NetBurn2() { SetBurn(2); }
    public void NetBurn3() { SetBurn(3); }

    void SetBurn(int lv)
    {
        // 操縦している本人: 悪くなった時に音声で知らせる
        if (isPiloting && lv > burnLevel)
        {
            if (lv == 2) QueueVoice(voiceEngineFire);
            else if (lv >= 3) PlayVoiceNow(voiceCritical);
        }
        burnLevel = lv;
        // 炎: 段階 2 から。3 で大きく激しく
        float fireRate = lv >= 3 ? 70f : (lv == 2 ? 28f : 0f);
        float fireSize = lv >= 3 ? 1.8f : 1f;
        if (damageFire != null)
        {
            for (int i = 0; i < damageFire.Length; i++)
            {
                ParticleSystem ps = damageFire[i];
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTimeMultiplier = fireRate;
                var mn = ps.main;
                mn.startSizeMultiplier = fireSize * 2f;
                if (fireRate > 0f && !ps.isPlaying) ps.Play();
            }
        }
        // 煙: 段階 1 から。だんだん濃く
        float smokeRate = lv >= 3 ? 40f : (lv == 2 ? 22f : (lv == 1 ? 8f : 0f));
        if (damageSmoke != null)
        {
            for (int i = 0; i < damageSmoke.Length; i++)
            {
                ParticleSystem ps = damageSmoke[i];
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTimeMultiplier = smokeRate;
                var mn = ps.main;
                mn.startSizeMultiplier = lv >= 3 ? 7f : 4.5f;
                if (smokeRate > 0f && !ps.isPlaying) ps.Play();
            }
        }
    }
}
