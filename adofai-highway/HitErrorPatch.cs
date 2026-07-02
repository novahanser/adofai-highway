using HarmonyLib;

namespace AdofaiHighway
{
    // The game computes a precise, sub-frame angular error for each hit and passes it
    // to its own error meter; we read it from there instead of timing inputs against
    // our frame-quantized clock.
    [HarmonyPatch(typeof(scrHitErrorMeter), nameof(scrHitErrorMeter.AddHit))]
    internal static class HitErrorPatch
    {
        // angleDiff (radians) is how far past the target the planet swept: positive =
        // late, negative = early. hitFloor gives the tile speed for the ms conversion.
        // Harmony matches parameters by name, so AddHit's other two are omitted.
        private static void Postfix(float angleDiff, scrFloor hitFloor)
        {
            HighwayBehaviour.RecordHit(angleDiff, hitFloor);
        }
    }
}
