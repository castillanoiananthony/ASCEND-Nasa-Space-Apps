// ═════════════════════════════════════════════════════════════════════════════
//  DevShortcuts.cs  -  developer shortcuts (Editor and Development builds only)
//
//   Ctrl + Shift + L   jump straight to the launch: skips the office + briefing,
//                      builds a Falcon 9 with Diwata-1 on the pad (Earth Watcher),
//                      puts you at the Launch Computer and opens it.
// ═════════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class DevShortcuts : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (FindAnyObjectByType<DevShortcuts>()) return;
        var go = new GameObject("Dev Shortcuts");
        DontDestroyOnLoad(go);
        go.AddComponent<DevShortcuts>();
    }

    void Update()
    {
        if (ComboL()) JumpToLaunch();
    }

    static bool ComboL()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && k.ctrlKey.isPressed && k.shiftKey.isPressed && k.lKey.wasPressedThisFrame;
#else
        return (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
               (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.L);
#endif
    }

    static void JumpToLaunch()
    {
        var lc = FindAnyObjectByType<AscendLaunchController>();
        if (!lc || GameFlow.PlayTrailer) { Debug.Log("DEV: Ctrl+Shift+L only works in the Universe scene."); return; }
        if (FindAnyObjectByType<AscendOrbitDirector>()) { Debug.Log("DEV: already in orbit."); return; }

        // 1. story done: Earth Watcher picked, fund in the bank
        MissionBriefing.DevSkipBriefing(1);
        AscendGuide.DevSkipTours();
        var ci = FindAnyObjectByType<ComputerInteraction>();
        if (ci && ci.IsOpen) ci.Close();

        // 2. Falcon 9 + Diwata-1 on the pad (approved + trajectory skipped)
        var data = AscendCADData.Resolve(ci ? ci.ascendCAD : lc.ascendCADData);
        var rocket = AscendTemplates.Build(1, data);     // Falcon 9
        var sat = AscendTemplates.BuildSat(1, data);     // Diwata-1
        var ps = CADStats.Payload(sat, data);
        var st = CADStats.Rocket(rocket, data, ps.wetMass, AscendMissionGuide.RequiredDeltaV(data));
        AscendLaunchSite.Spawn(data, rocket, sat, st, AscendEconomy.SelectedPad);

        // 3. stand in front of the Launch Computer and open it
        var pc = PlayerController.Instance;
        if (pc)
        {
            if (pc.Seated) pc.StandUp();
            var cc = pc.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            Vector3 c = lc.transform.position;
            Vector3 p = new Vector3(c.x - 2f, c.y, c.z + 4f);
            pc.transform.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(c.x - p.x, 0f, c.z - p.z)));
            if (cc) cc.enabled = true;
            Physics.SyncTransforms();
        }
        lc.DevOpenConsole();
        Debug.Log("DEV: Falcon 9 + Diwata-1 ready on the pad. Launch Computer open.");
    }
}
#endif
