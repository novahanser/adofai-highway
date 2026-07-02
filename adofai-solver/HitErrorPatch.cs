using HarmonyLib;

namespace AdofaiHighway
{
    // The game already computes a precise, async-timestamped angular error for every
    // hit and feeds it to its own hit-error meter. We piggyback on that same call so
    // our readout is exactly as accurate as the game's judgment (sub-frame), rather
    // than sampling the frame-quantized song clock ourselves.
    [HarmonyPatch(typeof(scrHitErrorMeter), nameof(scrHitErrorMeter.AddHit))]
    internal static class HitErrorPatch
    {
        // angleDiff (radians) = cachedAngle - targetExitAngle, normalized for CCW, so
        // >0 means the planet swept past the target = late; <0 = early. hitFloor gives
        // the tile speed for the angle->time conversion. (Harmony injects by name; the
        // other AddHit parameters are simply omitted.)
        private static void Postfix(float angleDiff, scrFloor hitFloor)
        {
            HighwayBehaviour.RecordHit(angleDiff, hitFloor);
        }
    }
}
