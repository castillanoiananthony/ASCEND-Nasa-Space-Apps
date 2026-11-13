// ═════════════════════════════════════════════════════════════════════════════
//  Ascend Rocket Trajectory Calculator  (placeholder - built on another day)
//  The heavy physics + mission types will live here.
//
//  What AscendCAD already hands over:
//    AscendLaunchSite.Current                       -> the rocket GameObject on the pad
//    AscendLaunchSite.Current.GetComponent<AscendRocket>()
//        .rocket / .payload                         -> full designs (parts, positions, colors)
//        .stats                                     -> mass, thrust, stages, delta-v, max payload
//    AscendMail.Send / AscendMail.Deliver           -> email the board, receive approval
//    AscendRunner.After(seconds, action)            -> delayed replies
// ═════════════════════════════════════════════════════════════════════════════
using UnityEngine;

public enum AscendMissionType
{
    LowEarthOrbit,          // ~400-600 km, ~9.4 km/s delta-v
    SunSynchronousOrbit,    // ~500-800 km polar, Earth observation (Diwata-style)
    GeostationaryTransfer,  // ~12 km/s
    LunarTransfer,          // ~12.5 km/s
}

public static class AscendTrajectoryCalculator
{
    // Rocket currently waiting on the launch pad (null if none approved yet)
    public static AscendRocket CurrentRocket =>
        AscendLaunchSite.Current ? AscendLaunchSite.Current.GetComponent<AscendRocket>() : null;
}
