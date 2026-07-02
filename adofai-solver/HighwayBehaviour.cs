using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdofaiHighway
{
    // Draws the constant-speed note highway as an IMGUI overlay, scrolled by the
    // game's own conductor clock so it stays in perfect sync (including through
    // pauses, deaths, scrubs and speed changes — the clock handles all of those).
    internal sealed class HighwayBehaviour : MonoBehaviour
    {
        internal static HighwayBehaviour Instance;

        // One tap per tile. We keep when it happens, whether it is a
        // midspin, and its seqID so we can relate notes to the game's landing events.
        private double[] noteTimes = new double[0];
        private bool[] noteMidspin = new bool[0];
        private int[] noteSeqID = new int[0];

        // Integer musical beats mapped to song-time, for the grid lines. Built by
        // interpolating each tile's entryBeat/entryTime (see RebuildBeats).
        private double[] beatTimes = new double[0];
        private int[] beatNumber = new int[0];

        // Rebuild the note list only when the loaded level changes. listFloors is a
        // fresh List instance per level build, so identity + count is a cheap guard.
        private object lastFloorsRef;
        private int lastFloorsCount = -1;

        // Auto-calibration. songposition_minusv and entryTime differ by the game's
        // (input/visual) calibration constants, which we can't read reliably — so we
        // *measure* the offset from reality: each time the planet lands on a tile
        // (scrController.currentSeqID increments), we compare that tile's entryTime to
        // the clock at the landing frame. That aligns the hit line with the true beat
        // regardless of the player's calibration settings or song pitch.
        private double autoOffset;        // seconds; add to the clock
        private bool haveOffset;
        private int lastSeenSeqID = SeqUnset;
        private const int SeqUnset = -999;

        // Last hit's timing error, fed by HitErrorPatch. >0 = late, <0 = early (ms).
        private static float lastErrorMs;
        private static float lastErrorAt = -999f;   // Time.unscaledTime of the hit
        private const float ErrorDisplaySeconds = 1.3f;

        private Texture2D pixel;
        private GUIStyle errorStyle;

        // Called from the AddHit Harmony postfix on every judged input.
        internal static void RecordHit(float angleDiff, scrFloor hitFloor)
        {
            var conductor = ADOBase.conductor;
            if (conductor == null)
            {
                return;
            }

            double bpmTimesSpeed = conductor.bpm * (hitFloor != null ? hitFloor.speed : 1.0);
            double pitch = conductor.song != null ? conductor.song.pitch : 1.0;
            if (bpmTimesSpeed <= 0.0 || pitch <= 0.0)
            {
                return;
            }

            // scrMisc.AngleToTime but WITHOUT its mod(angle, 2pi): the wrap would turn a
            // small "early" (negative) error into nearly a full beat. One beat = pi rad
            // = 60/bpm seconds; divide by pitch for real (wall-clock) time.
            double errSeconds = angleDiff / Math.PI * (60.0 / bpmTimesSpeed) / pitch;
            lastErrorMs = (float)(errSeconds * 1000.0);
            lastErrorAt = Time.unscaledTime;
        }

        private void Awake()
        {
            Instance = this;
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();

            // Seed from last session's measurement so early notes are already close.
            autoOffset = Startup.Settings.autoOffsetMs / 1000.0;
            haveOffset = Startup.Settings.autoOffsetMs != 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (pixel != null)
            {
                Destroy(pixel);
            }
        }

        internal void ResetCalibration()
        {
            autoOffset = 0.0;
            haveOffset = false;
            Startup.Settings.autoOffsetMs = 0f;
        }

        private void Update()
        {
            var lm = ADOBase.lm;
            var floors = lm != null ? lm.listFloors : null;
            if (floors == null)
            {
                return;
            }

            if (!ReferenceEquals(floors, lastFloorsRef) || floors.Count != lastFloorsCount)
            {
                RebuildNotes(floors);
                RebuildBeats(floors);
                lastFloorsRef = floors;
                lastFloorsCount = floors.Count;
                lastSeenSeqID = SeqUnset;   // don't measure across a level change
            }

            TrackLandings(floors);
        }

        private void RebuildNotes(List<scrFloor> floors)
        {
            var times = new List<double>(floors.Count);
            var midspins = new List<bool>(floors.Count);
            var ids = new List<int>(floors.Count);

            // Floor 0 is the starting tile — the planet begins there, so there is no
            // input for it. The first tap lands on floor 1. Decorative/fake tiles are
            // not inputs either.
            for (int i = 1; i < floors.Count; i++)
            {
                var f = floors[i];
                if (f == null || f.isFake)
                {
                    continue;
                }

                times.Add(f.entryTime);
                midspins.Add(f.midSpin);
                ids.Add(f.seqID);
            }

            noteTimes = times.ToArray();
            noteMidspin = midspins.ToArray();
            noteSeqID = ids.ToArray();
        }

        // Place a marker at every integer beat by interpolating between tiles. entryBeat
        // already bakes in speed changes/pauses, so linear interpolation within each
        // tile-to-tile segment (constant bpm there) gives musically-correct beat times.
        // Floor 0's entryBeat is a -1 sentinel and the final floor is left unset, so we
        // only walk the valid interior floors.
        private void RebuildBeats(List<scrFloor> floors)
        {
            var times = new List<double>();
            var numbers = new List<int>();

            for (int i = 1; i + 1 < floors.Count - 1; i++)
            {
                var a = floors[i];
                var b = floors[i + 1];
                if (a == null || b == null)
                {
                    continue;
                }

                double beatA = a.entryBeat;
                double beatB = b.entryBeat;
                double span = beatB - beatA;
                if (span <= 0.0)
                {
                    continue;   // no forward progress (sentinel/degenerate) — skip
                }

                // Each integer beat that falls in [beatA, beatB).
                for (int beat = (int)Math.Ceiling(beatA - 1e-6); beat < beatB - 1e-6; beat++)
                {
                    double frac = (beat - beatA) / span;
                    times.Add(a.entryTime + frac * (b.entryTime - a.entryTime));
                    numbers.Add(beat);
                }
            }

            beatTimes = times.ToArray();
            beatNumber = numbers.ToArray();
        }

        // Refine autoOffset from the game's real landing events.
        private void TrackLandings(List<scrFloor> floors)
        {
            var controller = ADOBase.controller;
            var conductor = ADOBase.conductor;
            if (controller == null || conductor == null)
            {
                return;
            }

            int cur = controller.currentSeqID;
            if (cur == lastSeenSeqID)
            {
                return;
            }

            int prev = lastSeenSeqID;
            lastSeenSeqID = cur;

            // Only trust a plain forward step for measurement — big/backward jumps are
            // scrubs, checkpoints or restarts, where the half-frame estimate is invalid.
            double delta = conductor.deltaSongPos;
            bool normalAdvance = prev != SeqUnset && cur > prev && (cur - prev) <= 4
                                 && delta > 0.0 && delta < 0.5;
            if (!normalAdvance || cur < 0 || cur >= floors.Count)
            {
                return;
            }

            // We notice the landing up to one frame late, so the true landing happened
            // roughly half a frame's worth of song-time before the current clock read.
            double half = delta > 0.0 ? 0.5 * delta : 0.0;
            double measured = floors[cur].entryTime - (conductor.songposition_minusv - half);

            // Snap on the first sample, then lerp to filter per-frame quantization jitter.
            autoOffset = haveOffset ? autoOffset + (measured - autoOffset) * 0.25 : measured;
            haveOffset = true;
            Startup.Settings.autoOffsetMs = (float)(autoOffset * 1000.0);
        }

        private void OnGUI()
        {
            // We only draw (no interactive controls), so skip the Layout/input passes.
            if (Event.current.type != EventType.Repaint || noteTimes.Length == 0)
            {
                return;
            }

            var conductor = ADOBase.conductor;
            if (conductor == null)
            {
                return;
            }

            var s = Startup.Settings;
            double now = conductor.songposition_minusv + autoOffset + s.offsetMs / 1000.0;

            float laneHeight = Screen.height * s.laneHeightFraction;
            float laneTop = (Screen.height - laneHeight) * 0.5f;
            float laneBottom = laneTop + laneHeight;
            float laneLeft = Screen.width - s.laneWidth - s.rightMargin;
            float hitLineY = laneBottom - s.hitLineFromBottom;

            // Lane backdrop.
            DrawRect(laneLeft, laneTop, s.laneWidth, laneHeight, new Color(0f, 0f, 0f, s.laneOpacity));

            float visibleAbove = hitLineY - laneTop;               // px of lookahead
            double leadSeconds = visibleAbove / s.pixelsPerSecond;

            // Beat grid, under the notes: faint line per beat, brighter per measure.
            if (s.showBeatLines)
            {
                int perMeasure = Mathf.Max(1, s.beatsPerMeasure);
                for (int i = 0; i < beatTimes.Length; i++)
                {
                    double dt = beatTimes[i] - now;
                    if (dt < 0.0 || dt > leadSeconds)
                    {
                        continue;   // only the approaching grid, above the hit line
                    }

                    float y = hitLineY - (float)(dt * s.pixelsPerSecond);
                    bool measure = beatNumber[i] % perMeasure == 0;
                    float a = Mathf.Clamp01(s.beatLineOpacity * (measure ? 2f : 1f));
                    DrawRect(laneLeft, y, s.laneWidth, measure ? 2f : 1f, new Color(1f, 1f, 1f, a));
                }
            }

            // A note lands exactly when it reaches the hit line; instead of drifting on
            // past (which blurred the moment of impact), a landed note pins to the line
            // and fades out fast, and the line flashes — a crisp "tap now" cue.
            const double fadeSeconds = 0.12;
            double mostRecentLanding = double.PositiveInfinity;

            for (int i = 0; i < noteTimes.Length; i++)
            {
                double dt = noteTimes[i] - now;   // >0 upcoming, <=0 already landed
                if (dt > leadSeconds)
                {
                    continue;
                }

                float y;
                float alpha;
                if (dt >= 0.0)
                {
                    y = hitLineY - (float)(dt * s.pixelsPerSecond);
                    alpha = 0.95f;
                }
                else
                {
                    double pastBy = -dt;
                    if (pastBy > fadeSeconds)
                    {
                        continue;   // fully faded — cull
                    }
                    if (pastBy < mostRecentLanding)
                    {
                        mostRecentLanding = pastBy;
                    }
                    y = hitLineY;
                    alpha = 0.95f * (1f - (float)(pastBy / fadeSeconds));
                }

                Color c = noteMidspin[i]
                    ? new Color(1f, 0.55f, 0.1f, alpha)                       // midspin: fixed orange accent
                    : new Color(s.noteColorR, s.noteColorG, s.noteColorB, alpha);  // normal: user colour
                DrawRect(laneLeft, y - 2f, s.laneWidth, 4f, c);
            }

            // Flash the hit line for a moment after any note lands.
            if (mostRecentLanding < fadeSeconds)
            {
                float glow = 1f - (float)(mostRecentLanding / fadeSeconds);
                DrawRect(laneLeft, hitLineY - 8f, s.laneWidth, 16f, new Color(1f, 0.95f, 0.4f, 0.55f * glow));
            }

            // Hit line drawn last so it stays sharp on top of notes and glow.
            DrawRect(laneLeft, hitLineY - 1.5f, s.laneWidth, 3f, new Color(1f, 1f, 1f, 0.9f));

            DrawErrorReadout(laneLeft, hitLineY, s.laneWidth);
        }

        // Small "12 ms" readout beside the hit line showing how early/late the last
        // input was: negative/blue = early, positive/red = late, tight = green.
        private void DrawErrorReadout(float laneLeft, float hitLineY, float laneWidth)
        {
            float age = Time.unscaledTime - lastErrorAt;
            if (age < 0f || age > ErrorDisplaySeconds)
            {
                return;
            }

            int ms = Mathf.RoundToInt(lastErrorMs);
            float mag = Mathf.Abs(lastErrorMs);
            Color c = mag <= 15f ? new Color(0.4f, 1f, 0.5f)     // tight: green
                    : lastErrorMs < 0f ? new Color(0.4f, 0.75f, 1f)  // early: blue
                    : new Color(1f, 0.5f, 0.4f);                 // late: red
            c.a = 1f - age / ErrorDisplaySeconds;                // fade out

            if (errorStyle == null)
            {
                errorStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }
            errorStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(laneWidth * 0.11f), 14, 40);
            errorStyle.alignment = TextAnchor.MiddleRight;
            errorStyle.normal.textColor = c;

            float w = 130f;
            float h = errorStyle.fontSize + 12f;
            string txt = ms == 0 ? "perfect" : $"{ms:+0;-0} ms";
            GUI.Label(new Rect(laneLeft - w - 8f, hitLineY - h * 0.5f, w, h), txt, errorStyle);
        }

        private void DrawRect(float x, float y, float w, float h, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), pixel);
            GUI.color = prev;
        }
    }
}
