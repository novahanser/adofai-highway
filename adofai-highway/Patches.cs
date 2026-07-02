using HarmonyLib;

namespace AdofaiHighway
{
    // The game computes a precise, sub-frame angular error for each hit and passes it
    // to its own error meter; we read it from there instead of timing inputs against
    // our frame-quantized clock.
    //
    // But the meter is fed for EVERY press, including ones the game swallows without
    // scoring (a too-early press just after a hold, the first taps of a multipress,
    // over-presses). The scoreboard tracker is only fed for presses that were really
    // judged, and both run inside the same Hit() call, so the two hooks are paired:
    // the tracker's margin arms a readout that the meter hook fills with measured ms.
    [HarmonyPatch(typeof(scrMarginTracker), nameof(scrMarginTracker.AddHit))]
    internal static class JudgmentPatch
    {
        private static void Postfix(HitMargin hit)
        {
            HighwayBehaviour.RecordJudgment(hit);
        }
    }

    // angleDiff (radians) is how far past the target the planet swept: positive =
    // late, negative = early. hitFloor (or the planet's previous floor when null)
    // gives the tile speed for the ms conversion. Harmony matches parameters by name.
    [HarmonyPatch(typeof(scrHitErrorMeter), nameof(scrHitErrorMeter.AddHit))]
    internal static class HitErrorPatch
    {
        private static void Postfix(float angleDiff, scrPlanet planet, scrFloor hitFloor)
        {
            HighwayBehaviour.RecordHit(angleDiff, planet, hitFloor);
        }
    }
}
