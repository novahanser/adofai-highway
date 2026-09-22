using System;
using System.Collections.Generic;

namespace AdofaiHighway
{
    // A snapshot of game-computed timing. Keeping this independent of Unity lets us
    // verify input semantics without starting the game or copying game assemblies.
    internal readonly struct FloorTiming
    {
        internal readonly double Time, TravelDegrees, Bpm;
        internal readonly bool Fake, Auto, FreeRoam, MidSpin, Hold;
        internal readonly int RequiredKeys;

        internal FloorTiming(double time, double travelDegrees = 180, double bpm = 120,
            bool fake = false, bool auto = false, bool freeRoam = false,
            bool midSpin = false, bool hold = false, int requiredKeys = 1)
        {
            Time = time;
            TravelDegrees = travelDegrees;
            Bpm = bpm;
            Fake = fake;
            Auto = auto;
            FreeRoam = freeRoam;
            MidSpin = midSpin;
            Hold = hold;
            RequiredKeys = Math.Max(1, requiredKeys);
        }

        internal bool NeedsInput => !Fake && !Auto && !FreeRoam && !MidSpin;
    }

    internal static class ChartEventBuilder
    {
        internal static RhythmEvent[] Build(IReadOnlyList<FloorTiming> floors)
        {
            var result = new List<RhythmEvent>();
            if (floors == null) return result.ToArray();

            // Floor zero is the starting position, never an extra key press.
            for (int i = 1; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (!floor.NeedsInput || double.IsNaN(floor.Time) || double.IsInfinity(floor.Time)) continue;

                int previous = i - 1;
                while (previous > 0 && floors[previous].MidSpin) previous--;
                var before = floors[previous];
                int required = floor.RequiredKeys;

                // OttoHoldHit automatically finishes even a multitap endpoint of
                // an automatic hold. The following ordinary tile is manual again.
                if (!floor.Hold && before.Hold && before.Auto && !before.Fake && !before.FreeRoam)
                    continue;

                // Releasing a hold supplies the next tile's input. A consecutive
                // hold instead needs a new press. Midspin tiles are zero-time
                // transitions handled by the game and never add a manual tap.
                if (!floor.Hold && before.Hold && before.NeedsInput)
                {
                    required--;
                    if (required == 0) continue;
                }

                double release = floor.Time;
                if (floor.Hold)
                {
                    int next = i + 1;
                    while (next < floors.Count && floors[next].MidSpin) next++;
                    if (next < floors.Count && floors[next].Time > release)
                        release = floors[next].Time;
                }

                result.Add(new RhythmEvent(floor.Time, release,
                    result.Count == 0 ? double.NaN : before.TravelDegrees,
                    before.Bpm, required));
            }
            return result.ToArray();
        }
    }
}
