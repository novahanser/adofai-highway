using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdofaiHighway
{
    // Draws the note highway as an IMGUI overlay, scrolled by the game's conductor
    // clock. Because that clock is the only time source, pauses, deaths, scrubs and
    // speed changes need no special handling — the highway just follows it.
    internal sealed class HighwayBehaviour : MonoBehaviour
    {
        internal static HighwayBehaviour Instance;

        // One note per tile — every tile in ADOFAI is exactly one tap. midSpin tiles
        // are a rapid double-tap and get their own colour.
        private double[] noteTimes = new double[0];
        private bool[] noteMidspin = new bool[0];

        // Parallel arrays for the beat grid: beat number beatNumber[i] falls at song
        // time beatTimes[i].
        private double[] beatTimes = new double[0];
        private int[] beatNumber = new int[0];

        // listFloors is rebuilt as a new List per level, so a changed reference or
        // count means a new level is loaded.
        private object lastFloorsRef;
        private int lastFloorsCount = -1;

        // The conductor clock and a tile's entryTime differ by an unknown but constant
        // calibration offset. Rather than guess it, we measure it from real landings
        // (see TrackLandings) so the hit line sits on the true beat.
        private double autoOffsetSeconds;
        private bool hasCalibrated;
        private int lastSeenSeqID = SeqUnset;
        private const int SeqUnset = -999;

        // Most recent hit's timing error, set by RecordHit. Positive = late, negative = early.
        private static float lastErrorMs;
        private static float lastErrorAt = -999f;   // Time.unscaledTime of that hit
        private const float ErrorDisplaySeconds = 1.3f;

        private Texture2D pixel;
        private GUIStyle errorStyle;

        // Called from HitErrorPatch on every judged input.
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

            // Mirrors scrMisc.AngleToTime (one beat = pi radians = 60/bpm seconds) but
            // omits its mod(angle, 2pi): that wrap would turn a small early (negative)
            // error into nearly a full beat. Dividing by pitch gives wall-clock time.
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

            // Seed from the last session so the first notes of a level are already close.
            autoOffsetSeconds = Startup.Settings.autoOffsetMs / 1000.0;
            hasCalibrated = Startup.Settings.autoOffsetMs != 0f;
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
            autoOffsetSeconds = 0.0;
            hasCalibrated = false;
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
                lastSeenSeqID = SeqUnset;   // don't measure calibration across a level change
            }

            TrackLandings(floors);
        }

        private void RebuildNotes(List<scrFloor> floors)
        {
            var times = new List<double>(floors.Count);
            var midspins = new List<bool>(floors.Count);

            // Skip floor 0 (the planet starts there — no tap) and fake/decorative tiles.
            for (int i = 1; i < floors.Count; i++)
            {
                var f = floors[i];
                if (f == null || f.isFake)
                {
                    continue;
                }

                times.Add(f.entryTime);
                midspins.Add(f.midSpin);
            }

            noteTimes = times.ToArray();
            noteMidspin = midspins.ToArray();
        }

        // entryBeat is cumulative musical beats (it already bakes in speed changes and
        // pauses), and within a tile-to-tile segment beat maps linearly to time, so we
        // interpolate to place a marker on every integer beat. Floor 0's entryBeat is a
        // -1 sentinel and the last floor is left unset, so only interior floors qualify.
        private void RebuildBeats(List<scrFloor> floors)
        {
            var times = new List<double>();
            var numbers = new List<int>();

            for (int i = 1; i < floors.Count - 2; i++)
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
                    continue;
                }

                // Half-open [beatA, beatB) so a beat landing on a tile boundary is
                // emitted once, by the segment that starts on it.
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

        // Measure the calibration offset from the game's real landing events.
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

            // Only a plain forward step is a real landing. Big or backward jumps are
            // scrubs, checkpoints or restarts, where the half-frame estimate is invalid.
            double delta = conductor.deltaSongPos;
            bool normalAdvance = prev != SeqUnset && cur > prev && (cur - prev) <= 4
                                 && delta > 0.0 && delta < 0.5;
            if (!normalAdvance || cur < 0 || cur >= floors.Count)
            {
                return;
            }

            // The landing is noticed up to a frame late, so it truly happened about
            // half a frame's worth of song-time before this clock reading.
            double half = delta > 0.0 ? 0.5 * delta : 0.0;
            double measured = floors[cur].entryTime - (conductor.songposition_minusv - half);

            // Snap on the first sample, then lerp to smooth per-frame jitter.
            autoOffsetSeconds = hasCalibrated ? autoOffsetSeconds + (measured - autoOffsetSeconds) * 0.25 : measured;
            hasCalibrated = true;
            Startup.Settings.autoOffsetMs = (float)(autoOffsetSeconds * 1000.0);
        }

        private void OnGUI()
        {
            // Draw-only overlay: skip the layout/input IMGUI passes.
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
            double now = conductor.songposition_minusv + autoOffsetSeconds + s.offsetMs / 1000.0;

            float laneHeight = Screen.height * s.laneHeightFraction;
            float laneTop = (Screen.height - laneHeight) * 0.5f;
            float laneBottom = laneTop + laneHeight;
            float laneLeft = Screen.width - s.laneWidth - s.rightMargin;
            float hitLineY = laneBottom - s.hitLineFromBottom;

            DrawRect(laneLeft, laneTop, s.laneWidth, laneHeight, new Color(0f, 0f, 0f, s.laneOpacity));

            float lookaheadPixels = hitLineY - laneTop;
            double leadSeconds = lookaheadPixels / s.pixelsPerSecond;

            // Beat grid, drawn under the notes and only above the hit line.
            if (s.showBeatLines)
            {
                int perMeasure = Mathf.Max(1, s.beatsPerMeasure);
                for (int i = 0; i < beatTimes.Length; i++)
                {
                    double secondsUntil = beatTimes[i] - now;
                    if (secondsUntil < 0.0 || secondsUntil > leadSeconds)
                    {
                        continue;
                    }

                    float y = hitLineY - (float)(secondsUntil * s.pixelsPerSecond);
                    bool measure = beatNumber[i] % perMeasure == 0;
                    float alpha = Mathf.Clamp01(s.beatLineOpacity * (measure ? 2f : 1f));
                    DrawRect(laneLeft, y, s.laneWidth, measure ? 2f : 1f, new Color(1f, 1f, 1f, alpha));
                }
            }

            // A note reaches the hit line at its time; past that it pins to the line and
            // fades quickly (rather than sliding on past) while the line flashes.
            const double fadeSeconds = 0.12;
            double secondsSinceLanding = double.PositiveInfinity;

            for (int i = 0; i < noteTimes.Length; i++)
            {
                double secondsUntil = noteTimes[i] - now;
                if (secondsUntil > leadSeconds)
                {
                    continue;
                }

                float y;
                float alpha;
                if (secondsUntil >= 0.0)
                {
                    y = hitLineY - (float)(secondsUntil * s.pixelsPerSecond);
                    alpha = 0.95f;
                }
                else
                {
                    double pastBy = -secondsUntil;
                    if (pastBy > fadeSeconds)
                    {
                        continue;
                    }
                    if (pastBy < secondsSinceLanding)
                    {
                        secondsSinceLanding = pastBy;
                    }
                    y = hitLineY;
                    alpha = 0.95f * (1f - (float)(pastBy / fadeSeconds));
                }

                Color c = noteMidspin[i]
                    ? new Color(1f, 0.55f, 0.1f, alpha)
                    : new Color(s.noteColorR, s.noteColorG, s.noteColorB, alpha);
                DrawRect(laneLeft, y - 2f, s.laneWidth, 4f, c);
            }

            if (secondsSinceLanding < fadeSeconds)
            {
                float glow = 1f - (float)(secondsSinceLanding / fadeSeconds);
                DrawRect(laneLeft, hitLineY - 8f, s.laneWidth, 16f, new Color(1f, 0.95f, 0.4f, 0.55f * glow));
            }

            // Hit line last, so it stays crisp over the notes and glow.
            DrawRect(laneLeft, hitLineY - 1.5f, s.laneWidth, 3f, new Color(1f, 1f, 1f, 0.9f));

            DrawErrorReadout(laneLeft, hitLineY, s.laneWidth);
        }

        // The last hit's early/late error beside the hit line, fading out over time.
        private void DrawErrorReadout(float laneLeft, float hitLineY, float laneWidth)
        {
            float age = Time.unscaledTime - lastErrorAt;
            if (age < 0f || age > ErrorDisplaySeconds)
            {
                return;
            }

            int ms = Mathf.RoundToInt(lastErrorMs);
            float mag = Mathf.Abs(lastErrorMs);
            Color c = mag <= 15f ? new Color(0.4f, 1f, 0.5f)         // tight: green
                    : lastErrorMs < 0f ? new Color(0.4f, 0.75f, 1f)  // early: blue
                    : new Color(1f, 0.5f, 0.4f);                     // late: red
            c.a = 1f - age / ErrorDisplaySeconds;

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
