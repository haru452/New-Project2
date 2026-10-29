using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// EnemyDrone.cs の続き（長いので分けたファイル。中身は同じクラス EnemyDrone）
public partial class EnemyDrone
{
    void FixedUpdate()
    {
        if (rb == null || !Networking.IsOwner(rb.gameObject)) return;
        // 撃っているかどうかの同期のため、この頭脳も本体と同じ人が持つ
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        float dt = Time.fixedDeltaTime;

        bool dead = target != null && target._IsDestroyed();
        if (dead)
        {
            wasDestroyed = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            SetFiring(false);
            return;
        }
        if (wasDestroyed && carrierBased && carrierRef != null)
        {
            // 空母の戦闘機: 復活したら甲板で待機（また近づいたら発艦）
            wasDestroyed = false;
            ParkNow();
            return;
        }
        if (carrierBased && carrierRef != null && CarrierStep(dt)) return;
        // 飛び立った後に、遅いまま低い所にいる（ぶつかって地面でもがいている）: 3 秒たったら止まる場所へ戻す（また順番に飛び立つ）
        if (carrierBased && carrierRef != null && cState >= 2 && !rb.isKinematic)
        {
            if (rb.velocity.magnitude < stallSpeed * 0.6f && rb.position.y - seaLevel < 150f) stuckT += dt; else stuckT = 0f;
            if (stuckT > 3f) { stuckT = 0f; ParkNow(); return; }
        }
        if (wasDestroyed)
        {
            // 復活: 出撃地点から離れた、広い範囲のランダムな場所に新しい持ち場を決めて再出発
            wasDestroyed = false;
            hasHome = false;
            PickWaypoint();
            Vector3 p = RandomSpawnPoint();
            if (InFormation())
            {
                // 編隊の戦闘機: 警戒機のそば（誰にも見えない時だけ）
                RefreshPlayers();
                Vector3 sl = FormationSlot();
                if (playerCount == 0 || NearestPlayerDist(sl) > minSpawnFromPlayers) p = sl;
            }
            Vector3 d0 = (waypoint - p);
            d0.y = 0f;
            Quaternion r0 = LookFor(d0.normalized, Vector3.up);
            rb.position = p;
            rb.rotation = r0;
            rb.transform.SetPositionAndRotation(p, r0);
            rb.velocity = d0.normalized * speed;
            return;
        }

        Vector3 pos = rb.position;
        // 重力は自分で足しているので、Unity の重力は切る（両方かかると機体を支えきれずに沈み続ける）
        if (rb.useGravity) rb.useGravity = false;
        // 海に落ちた・速すぎる（何かにぶつかってはじかれた）: 持ち場の空へ出し直す（誰にも見えない所で）
        if ((!carrierBased || cState >= 2) && (pos.y < seaLevel - 3f || rb.velocity.sqrMagnitude > 600f * 600f))
        {
            chasing = null; cachedPrey = null; lSmooth = Vector3.zero;
            if (!hasHome) PickWaypoint();
            Vector3 sp2 = carrierBased && carrierRef != null ? carrierRef.position + Vector3.up * 300f : RandomSpawnPoint();
            sp2.y = Mathf.Max(sp2.y, seaLevel + minAlt + 100f);
            Vector3 d2 = waypoint - sp2; d2.y = 0f;
            if (d2.sqrMagnitude < 1f) d2 = Vector3.forward;
            Quaternion r2 = LookFor(d2.normalized, Vector3.up);
            rb.position = sp2; rb.rotation = r2;
            rb.transform.SetPositionAndRotation(sp2, r2);
            rb.velocity = d2.normalized * speed;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // プレイヤー全員から遠すぎる（誰にも見えない）ドローンは、プレイヤーの近くへ出し直す
        if (!carrierBased && Time.time >= nextFarCheck)
        {
            nextFarCheck = Time.time + Random.Range(4f, 6f);
            RefreshPlayers();
            if (InFormation() && chasing == null)
            {
                // 編隊の戦闘機: 警戒機から離れすぎていて、誰にも見えていなければ、編隊の位置へ移る
                Vector3 slotF = FormationSlot();
                if ((slotF - pos).magnitude > 2000f && NearestPlayerDist(pos) > 4000f && NearestPlayerDist(slotF) > 4000f)
                {
                    Vector3 lvF = formationLeader._Vel();
                    if (lvF.sqrMagnitude < 1f) lvF = Vector3.forward * speed;
                    Quaternion rF = LookFor(lvF.normalized, Vector3.up);
                    rb.position = slotF;
                    rb.rotation = rF;
                    rb.transform.SetPositionAndRotation(slotF, rF);
                    rb.velocity = lvF;
                    return;
                }
            }
            else if (playerCount > 0 && NearestPlayerDist(pos) > despawnDistance)
            {
                chasing = null;
                cachedPrey = null;
                hasHome = false;
                PickWaypoint();
                Vector3 np = RandomSpawnPoint();
                Vector3 d1 = waypoint - np;
                d1.y = 0f;
                if (d1.sqrMagnitude < 1f) d1 = Vector3.forward;
                Quaternion r1 = LookFor(d1.normalized, Vector3.up);
                rb.position = np;
                rb.rotation = r1;
                rb.transform.SetPositionAndRotation(np, r1);
                rb.velocity = d1.normalized * speed;
                return;
            }
        }

        Vector3 v = rb.velocity;
        if (v.sqrMagnitude < 1f) v = Forward() * speed;

        // 目標: 近くのパイロット、いなければ目的地
        // 軽量化: 相手探しと前方の障害物チェックは 0.5 秒ごと
        if (Time.time >= nextThink)
        {
            nextThink = Time.time + 0.5f;
            quiet = gameMode != null && gameMode._Quiet();
            if (quiet) { cachedPrey = null; chasing = null; }   // 「偵察任務」の無線の間は誰も探さない・追わない
            else if (isAwacs) { AwacsScan(pos); cachedPrey = null; }   // 警戒機は追いかけない・撃たない
            else cachedPrey = FindPrey(pos);
            obstacleAhead = Physics.Raycast(pos, v.normalized, Mathf.Max(250f, v.magnitude * 4f), groundMask, QueryTriggerInteraction.Ignore);
        }
        controller prey = cachedPrey;
        // 遊覧モードの歓迎の編隊: 飛び立った人のまわりを一緒に飛ぶ（撃たない）
        if (welcomeSlot >= 0 && gameMode != null && !carrierBased)
        {
            int ep = gameMode._EscortPlane();
            if (ep >= 0 && planes != null && ep < planes.Length && planes[ep] != null && planes[ep].car != null)
            {
                Transform pc = planes[ep].car.transform;
                Rigidbody prb2 = planes[ep].car.GetComponent<Rigidbody>();
                Vector3 pv = prb2 != null ? prb2.velocity : Vector3.zero;
                Vector3 pf = pv; pf.y = 0f;
                if (pf.sqrMagnitude < 25f) { pf = pc.forward; pf.y = 0f; }
                pf.Normalize();
                Vector3 pr = Vector3.Cross(Vector3.up, pf);
                // ブルーインパルスのような、翼がふれ合いそうな近さの矢じり形（デルタ）。プレイヤーが先頭の真ん中（1 番機）
                //   2・3 番機 = すぐ後ろの左右、4・5 番機 = その外側の後ろ（少しずつ下がる）
                //   機体の翼幅 28m・長さ 42m に合わせて、横 34m・前後 42m おき
                float sx = welcomeSlot == 0 ? -34f : welcomeSlot == 1 ? 34f : welcomeSlot == 2 ? -68f : welcomeSlot == 3 ? 68f : 0f;
                float sz = welcomeSlot < 2 ? -42f : (welcomeSlot < 4 ? -84f : -126f);
                float sy = welcomeSlot < 2 ? -2f : (welcomeSlot < 4 ? -4f : -6f);
                Vector3 slot = pc.position + pr * sx + pf * sz + Vector3.up * sy;
                Vector3 toSlot = slot - pos;
                Vector3 wv;
                if (toSlot.magnitude > 1500f) wv = toSlot.normalized * topSpeed;   // まだ遠い: 全速で向かう
                else { wv = pv + toSlot * 0.35f; if (wv.magnitude > topSpeed) wv = wv.normalized * topSpeed; if (wv.magnitude < stallSpeed * 1.2f) wv = wv.normalized * stallSpeed * 1.2f; }
                chasing = null; cachedPrey = null;
                SetFiring(false);
                FlyPhysics(v, wv, dt);
                return;
            }
        }
        Vector3 goal;
        shadowing = false;
        if (prey != null)
        {
            Rigidbody prb = prey.car.GetComponent<Rigidbody>();
            Vector3 tp = prey.car.transform.position;
            Vector3 tv = prb != null ? prb.velocity : Vector3.zero;
            float lead = Vector3.Distance(pos, tp) / Mathf.Max(speed + 300f, 1f);
            float dP = Vector3.Distance(pos, tp);
            if (!prey._IsHostile())
            {
                // 警告の間: 相手の斜め後ろ 500m・横 250m の所についていく（近づきすぎてまわりをぐるぐる回らない）
                Vector3 pf2 = tv; pf2.y = 0f;
                if (pf2.sqrMagnitude < 25f) { pf2 = prey.car.transform.forward; pf2.y = 0f; }
                if (pf2.sqrMagnitude < 0.01f) pf2 = Vector3.forward;
                pf2.Normalize();
                Vector3 pr2 = Vector3.Cross(Vector3.up, pf2);
                float side = Vector3.Dot(pos - tp, pr2) >= 0f ? 1f : -1f;   // 今いる側のまま
                goal = tp - pf2 * 500f + pr2 * side * 250f + Vector3.up * 60f;
                shadowing = true;
            }
            else if (Time.time < extendUntil) goal = extendGoal;   // 追い越した後: いったん離れてから向き直る
            else if (dP < 250f && Vector3.Dot(Forward(), tp - pos) < 0f)
            {
                extendUntil = Time.time + 6f;
                extendGoal = pos + Forward() * 2000f;
                goal = extendGoal;
            }
            else goal = tp + tv * lead;
        }
        else
        {
            if (Time.time > nextWaypointTime || (waypoint - pos).sqrMagnitude < 200f * 200f) PickWaypoint();
            goal = waypoint;
        }

        // 「偵察任務」の無線の間: プレイヤーの近く（5km 以内）にいたら、一番近いプレイヤーから離れる方へ飛ぶ
        if (quiet)
        {
            RefreshPlayers();
            Vector3 away = Vector3.zero;
            float nd = float.MaxValue;
            for (int i = 0; i < playerCount; i++)
            {
                VRCPlayerApi pl = players[i];
                if (pl == null || !Utilities.IsValid(pl)) continue;
                Vector3 dp = pos - pl.GetPosition();
                dp.y = 0f;
                if (dp.magnitude < nd) { nd = dp.magnitude; away = dp; }
            }
            if (nd < 5000f && away.sqrMagnitude > 1f) goal = pos + away.normalized * 2000f + Vector3.up * (waypoint.y - pos.y);
        }
        // 空母の戦闘機: 着艦に向かう（甲板の後ろから入って、止まる場所の上で降りる）
        bool landing = false;
        if (carrierBased && carrierRef != null && cState == 3)
        {
            prey = null;
            Vector3 dd = carrierRef.TransformDirection(deckDirLocal);
            Vector3 td = carrierRef.TransformPoint(deckStartLocal);
            Vector3 approach = td - dd * 1200f + Vector3.up * 120f;   // 甲板の後ろ 1.2km・高さ 120m
            Vector3 toTd = td - pos;
            float alongD = Vector3.Dot(pos - td, dd);
            Vector3 flat = toTd; flat.y = 0f;
            if (alongD < -150f && Vector3.Angle(dd, flat) < 25f) goal = td + Vector3.up * 2f;   // 進入: 甲板へまっすぐ降りる
            else goal = approach;
            landing = flat.magnitude < 2500f;
            gearForceDown = flat.magnitude < 3000f;
            if (baseAirfield && flat.magnitude < 1500f) { ParkNow(); return; }   // 基地: 近くまで戻ったら駐機場へ（誰も見ていない時だけ着陸に向かう）
            if (toTd.magnitude < 60f || (alongD > -20f && alongD < deckRun && Mathf.Abs(pos.y - td.y) < 12f)) { ParkNow(); return; }
        }
        if (carrierBased && carrierRef != null && cState == 2 && prey == null)
        {
            // 空母のまわりを巡回（範囲の外へは行かない）
            Vector3 fc = pos - carrierRef.position; fc.y = 0f;
            if (fc.magnitude > carrierRange * 0.8f) goal = carrierRef.position + Vector3.up * (waypoint.y - carrierRef.position.y);
        }
        Vector3 dir = (goal - pos).normalized;
        float alt = pos.y - seaLevel;
        if (alt < minAlt && !landing) dir.y = Mathf.Max(dir.y, 0.3f);
        if (alt > maxAlt) dir.y = Mathf.Min(dir.y, -0.25f);
        dir.y = Mathf.Clamp(dir.y, -0.35f, 0.35f);   // 急上昇・急降下はしない
        // 前に島などがあれば上へよける（着艦の時は空母があるので、よけない）
        if (obstacleAhead && !landing) dir = (dir + Vector3.up * 1.5f).normalized;
        // 追いかけていない時に持ち場から離れすぎたら、持ち場へ戻る
        Vector3 fromC = pos - (hasHome ? home : areaCenter);
        fromC.y = 0f;
        if (prey == null && fromC.magnitude > homeRadius * 2.5f) { dir = (-fromC.normalized + Vector3.up * dir.y).normalized; }
        // 出撃する潜水艦のまわり 1km には、追いかけていない時は入らない（外へ向きを変える）
        if (prey == null && noFlyRadius > 0f && !landing)
        {
            Vector3 nc = noFlyRef != null ? noFlyRef.position : avoidCenter;
            Vector3 fn = pos - nc; fn.y = 0f;
            float fm = fn.magnitude;
            float edge = noFlyRadius + 600f;
            if (fm < edge)
            {
                Vector3 outD = fm > 1f ? fn / fm : Forward();
                outD.y = 0f; outD.Normalize();
                Vector3 hd = new Vector3(dir.x, 0f, dir.z);
                float inward = Vector3.Dot(hd, -outD);
                if (inward > 0f) hd += outD * inward;            // 中へ向かう分を消す
                hd += outD * Mathf.Clamp01((edge - fm) / 600f) * 1.5f;   // 近いほど強く外へ
                if (hd.sqrMagnitude < 0.01f) hd = outD;
                hd.Normalize();
                dir = new Vector3(hd.x, dir.y, hd.z).normalized;
            }
        }
        dir.Normalize();

        // 欲しい速度との差から加速度を出し、物理的な上限で切る
        Vector3 wantV = dir * speed;
        if (prey == null && InFormation())
        {
            // 編隊飛行: 警戒機の速さに合わせ、決まった位置へ寄っていく
            Vector3 slot = FormationSlot();
            wantV = formationLeader._Vel() + (slot - pos) * 0.3f;
            if (wantV.magnitude > speed * 1.4f) wantV = wantV.normalized * speed * 1.4f;
            if (obstacleAhead) wantV += Vector3.up * speed * 0.5f;
        }
        if (landing) wantV = wantV.normalized * stallSpeed * 1.3f;   // 着艦・着陸はゆっくり
        else if (prey != null && shadowing)
        {
            // 警告の間: 相手の速さに合わせる（離れていれば少し速く）
            Rigidbody sprb = prey.car.GetComponent<Rigidbody>();
            float ps = sprb != null ? sprb.velocity.magnitude : speed;
            float want = Mathf.Clamp(ps + (goal - pos).magnitude * 0.08f, stallSpeed * 1.2f, topSpeed);
            wantV = wantV.normalized * want;
        }
        else if (prey != null) wantV = wantV.normalized * Mathf.Min(speed * 1.15f, topSpeed);   // 追いかける時は少し速く
        FlyPhysics(v, wantV, dt);

        // 攻撃
        bool fire = false;
        if (prey != null)
        {
            Vector3 to = prey.car.transform.position - pos;
            // 警告の間は後ろについて来るだけ。攻撃開始（hostile）になってから撃つ
            fire = prey._IsHostile() && to.magnitude < fireRange && Vector3.Angle(Forward(), to) < fireAngle;
        }
        SetFiring(fire);

        // ミサイル（攻撃開始後・前方の少し離れた相手に、12 秒くらいに 1 発）
        if (prey != null && prey._IsHostile() && missile != null && !mActive && Time.time >= nextMissile)
        {
            Vector3 tm = prey.car.transform.position - pos;
            float dm = tm.magnitude;
            if (dm > missileMinRange && dm < missileMaxRange && Vector3.Angle(Forward(), tm) < missileCone)
            {
                nextMissile = Time.time + missileInterval * Random.Range(0.8f, 1.3f);
                int idx = -1;
                for (int i = 0; i < planes.Length; i++) if (planes[i] == prey) { idx = i; break; }
                if (idx >= 0)
                {
                    missileTarget = idx;
                    missileSeq++;
                    RequestSerialization();
                    LaunchMissile(idx);   // 自分（持ち主）の画面
                    seenMissileSeq = missileSeq;
                }
            }
        }
        if (fire && Time.time >= nextHitCheck)
        {
            nextHitCheck = Time.time + hitCheckInterval;
            Vector3 o = muzzle != null ? muzzle.position : pos;
            Vector3 aim = Quaternion.AngleAxis(Random.Range(-1.5f, 1.5f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-1.5f, 1.5f), rb.transform.forward) * Forward();
            RaycastHit[] hits = Physics.RaycastAll(o, aim, fireRange, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider bestCol = null;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(rb.transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; bestCol = hits[i].collider; }
            }
            if (bestCol != null && bestCol.transform.IsChildOf(prey.car.transform))
                prey.SendCustomNetworkEvent(NetworkEventTarget.Owner, "NetEnemyHit");
        }
    }

    // 出現場所: 範囲の中のランダムな場所（パイロットのすぐ近くは避ける）
    Vector3 RandomSpawnPoint()
    {
        Vector3 p = home;
        for (int tries = 0; tries < 8; tries++)
        {
            Vector2 c = Random.insideUnitCircle * homeRadius;
            p = new Vector3(home.x + c.x, seaLevel + Random.Range(minAlt + 50f, maxAlt - 50f), home.z + c.y);
            bool near = false;
            if (planes != null)
            {
                for (int i = 0; i < planes.Length; i++)
                {
                    controller pc = planes[i];
                    if (pc == null || pc.car == null || pc._GetPilot() == null) continue;
                    if (Vector3.Distance(p, pc.car.transform.position) < detectRange * 1.5f) { near = true; break; }
                }
            }
            if (!near) break;
        }
        return p;
    }

    // ドローンの機首は -X（muzzle があればその向き）
    // ---------------- 飛行の物理 ----------------
    // 本物の飛行機と同じ力で飛ぶ: 揚力（機体の上向き）・推力（前向き）・空気抵抗（後ろ向き）・重力（下向き）。
    //   曲がる時は、まず機体を傾けて（ロール）、揚力の向きを変えて曲がる。横すべりはしない
    //   揚力の上限 = 速さの 2 乗に比例（失速速度で 1G）、かつ G の上限まで。遅いと支えきれずに沈む
    //   上昇すると速さが落ち、降下すると速くなる（重力が速さの向きにも効く）
    //   wantV = 自動操縦が向かいたい速度（向き × 速さ）
    private Vector3 lSmooth;
    void FlyPhysics(Vector3 v, Vector3 wantV, float dt)
    {
        float G = 9.81f;
        float V = v.magnitude;
        if (V < 1f) { v = Forward() * Mathf.Max(speed, 1f); V = v.magnitude; }
        Vector3 vd = v / V;
        Vector3 grav = Vector3.down * G;

        // 欲しい加速度 → 速さの向きに直角な分を揚力で出す（重力の分も揚力で支える）
        Vector3 aDes = (wantV - v) * 0.8f;
        // 後ろへ向かいたい時: 減速ではなく、思いきり曲がって向きを変える
        Vector3 wd = wantV.sqrMagnitude > 1f ? wantV.normalized : vd;
        if (Vector3.Dot(wd, vd) < 0f)
        {
            Vector3 tp = wd - vd * Vector3.Dot(wd, vd);
            if (tp.sqrMagnitude < 0.01f) tp = Vector3.Cross(Vector3.up, vd);
            aDes = tp.normalized * gLimit * G * 2f;
        }
        Vector3 lReq = aDes - grav;
        lReq -= vd * Vector3.Dot(lReq, vd);
        // 水平飛行の時の上向き
        Vector3 wu = Vector3.up - vd * vd.y;
        if (wu.sqrMagnitude < 0.0001f) wu = rb.rotation * Vector3.up; else wu.Normalize();
        // 下向きの揚力（背面・マイナス G）は使わない: 上向きに最低 0.2G は残す（降りたい時は揚力を減らして沈む）
        float upComp = Vector3.Dot(lReq, wu);
        if (upComp < 0.2f * G) lReq += wu * (0.2f * G - upComp);
        // 急な変化をならす（相手探し・障害物の判定が 0.5 秒ごとに切り替わっても、機体がばたつかない）
        if (lSmooth.sqrMagnitude < 0.0001f) lSmooth = lReq;
        lSmooth = Vector3.Lerp(lSmooth, lReq, Mathf.Clamp01(dt * 4f));
        lReq = lSmooth - vd * Vector3.Dot(lSmooth, vd);
        // 出せる揚力の上限（失速と G）。足りない時は、まず機体を支える分（重力）を優先して、曲がる分を減らす
        float nAvail = Mathf.Min(gLimit, (V * V) / (stallSpeed * stallSpeed));
        float lMax = nAvail * G;
        if (lReq.magnitude > lMax)
        {
            Vector3 gp = -grav - vd * Vector3.Dot(-grav, vd);
            Vector3 ap = lReq - gp;
            float qa = Vector3.Dot(ap, ap), qb = 2f * Vector3.Dot(gp, ap), qc = Vector3.Dot(gp, gp) - lMax * lMax;
            float k = 0f;
            if (qc < 0f && qa > 0.0001f) k = Mathf.Min(1f, (-qb + Mathf.Sqrt(Mathf.Max(qb * qb - 4f * qa * qc, 0f))) / (2f * qa));
            lReq = gp + ap * k;
        }

        // 機体の上向き（速さの向きに直角）
        Vector3 cu = rb.rotation * Vector3.up;
        cu -= vd * Vector3.Dot(cu, vd);
        if (cu.sqrMagnitude < 0.0001f) cu = Vector3.up - vd * vd.y;
        cu.Normalize();

        // どちらへ傾けるか: 揚力を向けたい方向（いつも上寄り）
        float lm = lReq.magnitude;
        Vector3 upDes = lm > 0.3f ? lReq / lm : wu;

        // ロール: 決まった速さでしか傾けられない
        float maxRoll = rollRateDeg * Mathf.Deg2Rad * dt;
        Vector3 nu = Vector3.RotateTowards(cu, upDes, maxRoll, 0f);
        nu -= vd * Vector3.Dot(nu, vd);
        if (nu.sqrMagnitude < 0.0001f) nu = cu; else nu.Normalize();

        // 揚力: 今の傾きの向きに出せる分だけ。傾けている途中で引きすぎて上へ跳ね上がらないよう、上向きの分は欲しい分まで
        float lift = Vector3.Dot(lReq, nu);
        if (nu.y > 0.05f && lift * nu.y > lReq.y + 2f) lift = (lReq.y + 2f) / nu.y;
        lift = Mathf.Clamp(lift, 0f, lMax);
        float n = lift / G;

        // 推力と空気抵抗（有害抗力 + 揚力を出すことでの誘導抗力）
        float wantSpd = wantV.magnitude;
        float throttle = Mathf.Clamp01(0.6f + (wantSpd - V) * 0.08f);
        float k0 = thrustAcc / (topSpeed * topSpeed);
        float sv = stallSpeed / Mathf.Max(V, 1f);
        float drag = k0 * V * V + 0.12f * G * n * n * sv * sv;
        float thrust = thrustAcc * throttle;

        Vector3 a = vd * (thrust - drag) + nu * lift + grav;
        rb.AddForce(a, ForceMode.Acceleration);

        // 機体の向き: 進む向きから、揚力を出すための迎角だけ機首を上げる
        float aoa = Mathf.Clamp(12f * n * sv * sv, -4f, 15f) * Mathf.Deg2Rad;
        Vector3 fwd = vd * Mathf.Cos(aoa) + nu * Mathf.Sin(aoa);
        Vector3 upB = nu * Mathf.Cos(aoa) - vd * Mathf.Sin(aoa);
        rb.MoveRotation(LookFor(fwd, upB));
        rb.angularVelocity = Vector3.zero;
    }

    Vector3 Forward()
    {
        if (muzzle != null) return muzzle.forward;
        return noseIsZ ? rb.transform.forward : -rb.transform.right;
    }

    // 「機首を f、上を u」に向ける回転（ドローンは機首が -X なので補正）
    Quaternion LookFor(Vector3 f, Vector3 u)
    {
        if (noseIsZ) return Quaternion.LookRotation(f, u);
        return Quaternion.LookRotation(f, u) * Quaternion.Euler(0f, 90f, 0f);
    }

    controller FindPrey(Vector3 pos)
    {
        if (planes == null) return null;
        if (gameMode != null && gameMode._IsSightseeing()) { chasing = null; return null; }   // 遊覧モード（平和な間）は追いかけない
        // 編隊の戦闘機: 警戒機が見つけた相手を攻撃しに行く
        if (InFormation())
        {
            int ap = formationLeader._AlertPlane();
            if (ap >= 0 && ap < planes.Length && planes[ap] != null && planes[ap].car != null)
            {
                VRCPlayerApi ppl = planes[ap]._GetPilot();
                if (ppl != null && Utilities.IsValid(ppl) && !planes[ap]._InBriefing() && !(gameMode != null && gameMode._AstralSafe(planes[ap].car.transform.position))) { chasing = planes[ap]; return chasing; }
            }
        }
        // 追いかけている相手は、loseRange まで離れるまで追い続ける
        if (chasing != null)
        {
            VRCPlayerApi cp = chasing._GetPilot();
            bool keep = !chasing._InBriefing() && chasing.car != null
                && (!carrierBased || carrierRef == null || Vector3.Distance(carrierRef.position, chasing.car.transform.position) < carrierRange) && (Vector3.Distance(pos, chasing.car.transform.position) < loseRange || SpottedHard(chasing));
            if (keep && gameMode != null && gameMode._AstralSafe(chasing.car.transform.position)) { keep = false; BreakAwayFromAstral(pos); }   // アストラル空港: 攻撃をやめて離れる
            if (cp != null && Utilities.IsValid(cp) && keep) return chasing;
            chasing = null;
        }
        controller best = null;
        float bd = float.MaxValue;
        for (int i = 0; i < planes.Length; i++)
        {
            controller c = planes[i];
            if (c == null || c.car == null) continue;
            VRCPlayerApi p = c._GetPilot();
            if (p == null || !Utilities.IsValid(p)) continue;
            // 離陸するまで（潜水艦の甲板・水面にいる間）は襲わない
            if (c.car.transform.position.y - seaLevel < takeoffAltitude) continue;
            if (c._InBriefing()) continue;   // 偵察任務の無線が流れている間は探さない
            if (gameMode != null && gameMode._AstralSafe(c.car.transform.position)) continue;   // アストラル空港のまわり（言い合いの後）
            if (carrierBased && carrierRef != null && Vector3.Distance(carrierRef.position, c.car.transform.position) > carrierRange) continue;   // 空母の戦闘機は範囲の中だけ
            float d = Vector3.Distance(pos, c.car.transform.position);
            bool sh = SpottedHard(c);
            if (!sh && d >= detectRange) continue;
            float key = sh ? d - 1000000f : d;   // 船に見つかった機体を優先
            if (key < bd) { bd = key; best = c; }
        }
        chasing = best;
        return best;
    }

    // ハードモードで、船に見つかっている機体
    bool SpottedHard(controller c)
    {
        return gameMode != null && gameMode._IsHard() && c != null && c._IsSpotted();
    }

    // アストラル空港から 4km 離れた所へ向かう
    void BreakAwayFromAstral(Vector3 pos)
    {
        if (gameMode == null || gameMode.astralAirport == null) return;
        Vector3 aw = pos - gameMode.astralAirport.position; aw.y = 0f;
        if (aw.sqrMagnitude < 1f) aw = Vector3.forward;
        Vector3 w = gameMode.astralAirport.position + aw.normalized * (gameMode.astralSafeRadius + 1500f);
        waypoint = new Vector3(w.x, waypoint.y, w.z);
        nextWaypointTime = Time.time + 40f;
        SetFiring(false);
    }

    public bool _IsFiring() { return firing; }
    public int _AlertPlane() { return alertPlane; }

    // ---- 空母の戦闘機 ----
    // 甲板の止まる場所に置く（物理は止める）
}
