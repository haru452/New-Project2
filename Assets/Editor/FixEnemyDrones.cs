#if UNITY_EDITOR
using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =============================================================
//  敵ドローン自動セットアップ
//  使い方: このファイルを Assets/Editor/ フォルダに入れる
//         → メニュー Tools > Fix Enemy Drones
//  ・何かを選択している時: 選択した物（EnemyBrain 本体か、ドローン本体）だけ処理
//  ・何も選択していない時: シーン内の "EnemyBrain" という名前の物を全部処理
//  やること:
//   1. EnemyBrain に EnemyDrone と FlightTarget を付ける（あれば再利用）
//   2. EnemyDrone.rb に親の Rigidbody、target に FlightTarget を設定
//   3. EnemyDrone.planes が空ならシーン内の controller を全部入れる
//   4. FlightTarget の renderers / colliders が空なら、ドローン本体の見た目と当たり判定を入れる
//      （body は設定しない: 設定すると復活予約が動かなくなるため）
// =============================================================
public static class FixEnemyDrones
{
    const string BrainName = "EnemyBrain";

    [MenuItem("Tools/Fix Enemy Drones")]
    public static void Run()
    {
        List<Transform> brains = CollectBrains();
        if (brains.Count == 0)
        {
            EditorUtility.DisplayDialog("Fix Enemy Drones",
                "\"" + BrainName + "\" という名前のオブジェクトが見つかりませんでした。\n" +
                "EnemyBrain か、ドローン本体を選択してから実行してください。", "OK");
            return;
        }

        controller[] allPlanes = Object.FindObjectsOfType<controller>(true);
        int fixedCount = 0;
        string log = "";

        foreach (Transform brain in brains)
        {
            Rigidbody rb = brain.GetComponentInParent<Rigidbody>();
            if (rb == null)
            {
                log += "× " + brain.name + ": 親に Rigidbody がありません（スキップ）\n";
                continue;
            }

            // --- FlightTarget ---
            FlightTarget ft = brain.GetComponent<FlightTarget>();
            if (ft == null) ft = UdonSharpUndo.AddComponent<FlightTarget>(brain.gameObject);
            ft.UpdateProxy();
            if (ft.renderers == null || ft.renderers.Length == 0) ft.renderers = CollectRenderers(rb.transform, brain);
            if (ft.colliders == null || ft.colliders.Length == 0) ft.colliders = CollectColliders(rb.transform, brain);
            ft.ApplyProxyModifications();
            EditorUtility.SetDirty(ft);

            // --- EnemyDrone ---
            EnemyDrone drone = brain.GetComponent<EnemyDrone>();
            if (drone == null) drone = UdonSharpUndo.AddComponent<EnemyDrone>(brain.gameObject);
            drone.UpdateProxy();
            drone.rb = rb;
            drone.target = ft;
            if (drone.planes == null || drone.planes.Length == 0) drone.planes = allPlanes;
            drone.ApplyProxyModifications();
            EditorUtility.SetDirty(drone);

            fixedCount++;
            log += "○ " + brain.name + "（" + rb.name + "）: 設定しました\n";
        }

        if (fixedCount > 0) EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[Fix Enemy Drones]\n" + log);
        EditorUtility.DisplayDialog("Fix Enemy Drones",
            fixedCount + " 機を設定しました。\n\n" +
            "muzzle / tracer / missile などはこのツールでは設定しません。\n" +
            "必要なら Inspector で指定してください。\n\n" + log, "OK");
    }

    static List<Transform> CollectBrains()
    {
        List<Transform> list = new List<Transform>();
        GameObject[] sel = Selection.gameObjects;
        if (sel != null && sel.Length > 0)
        {
            foreach (GameObject go in sel)
            {
                if (go.name == BrainName) { AddUnique(list, go.transform); continue; }
                // ドローン本体を選んだ時は、その子の EnemyBrain を探す
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == BrainName) AddUnique(list, t);
            }
            return list;
        }
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == BrainName) AddUnique(list, t);
        return list;
    }

    static void AddUnique(List<Transform> list, Transform t)
    {
        if (!list.Contains(t)) list.Add(t);
    }

    static bool UnderBrain(Component c, Transform brain)
    {
        return c.transform == brain || c.transform.IsChildOf(brain);
    }

    // 見た目: パーティクル・軌跡・EnemyBrain 以下（ミサイル等）は除く
    static Renderer[] CollectRenderers(Transform root, Transform brain)
    {
        List<Renderer> list = new List<Renderer>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (UnderBrain(r, brain)) continue;
            list.Add(r);
        }
        return list.ToArray();
    }

    // 当たり判定: トリガーと EnemyBrain 以下は除く
    static Collider[] CollectColliders(Transform root, Transform brain)
    {
        List<Collider> list = new List<Collider>();
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            if (UnderBrain(c, brain)) continue;
            list.Add(c);
        }
        return list.ToArray();
    }
}
#endif