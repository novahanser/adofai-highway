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

        private enum NoteKind { Normal, Midspin, Multitap }

        // One note per tile. Midspin (a rapid double-tap) and multitap (two keys at
        // once) tiles each get their own colour.
        private double[] noteTimes = new double[0];
        private NoteKind[] noteKind = new NoteKind[0];

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

        // Most recent scored hit's timing error, set by RecordHit. Positive = late,
        // negative = early. The word and colour come from the judgment the game itself
        // recorded, so the readout always agrees with the hit text the game shows.
        private static float lastErrorMs;
        private static string lastJudgment = "";
        private static Color lastJudgmentColor = Color.white;
        private static float lastErrorAt = -999f;   // Time.unscaledTime of that hit
        private const float ErrorDisplaySeconds = 1.3f;

        // Judgment the game just recorded on the scoreboard, held until the error
        // meter call later in the same Hit() supplies the matching angular error.
        private static HitMargin pendingMargin;
        private static int pendingMarginFrame = -1;

        private Texture2D pixel;
        private GUIStyle errorStyle;

        // Called from JudgmentPatch whenever the game records a judgment on the
        // scoreboard — the signal that a press really counted.
        internal static void RecordJudgment(HitMargin margin)
        {
            pendingMargin = margin;
            pendingMarginFrame = Time.frameCount;
        }

        // Called from HitErrorPatch on every input that reaches the error meter.
        internal static void RecordHit(float angleDiff, scrPlanet planet, scrFloor hitFloor)
        {
            // The game scores a press just before feeding the meter, inside the same
            // Hit() call, so a judgment recorded this frame belongs to this press. No
            // recorded judgment means the game swallowed the press without scoring it
            // (hold grace, multipress part, over-press) and shows nothing for it —
            // mirror that rather than report a phantom miss.
            if (pendingMarginFrame != Time.frameCount)
            {
                return;
            }
            pendingMarginFrame = -1;

            var conductor = ADOBase.conductor;
            if (conductor == null)
            {
                return;
            }

            // Same tile-speed source as the game's AddHit: the hit floor, or the floor
            // the planet just left when the caller passed none.
            if (hitFloor == null && planet != null && planet.player != null && planet.player.currFloor != null)
            {
                hitFloor = planet.player.currFloor.prevfloor;
            }
            double bpmTimesSpeed = conductor.bpm * (hitFloor != null ? hitFloor.speed : 1.0);
            double pitch = conductor.song != null ? conductor.song.pitch : 1.0;
            if (bpmTimesSpeed <= 0.0 || pitch <= 0.0)
            {
                return;
            }

            // The planet's angle accumulates unwrapped, so angleDiff can arrive offset
            // by whole revolutions (extra spins, multi-tap presses, a stale reference
            // just after a restart). Fold into (-pi, pi] to recover the signed
            // sub-revolution error — a counted hit is always well within that window.
            // One beat = pi radians = 60/bpm seconds; divide by pitch for wall-clock ms.
            double twoPi = 2.0 * Math.PI;
            double wrapped = angleDiff - twoPi * Math.Round(angleDiff / twoPi);
            double errSeconds = wrapped / Math.PI * (60.0 / bpmTimesSpeed) / pitch;
            lastErrorMs = (float)(errSeconds * 1000.0);
            lastJudgment = JudgmentWord(pendingMargin);
            lastJudgmentColor = JudgmentColor(pendingMargin);
            lastErrorAt = Time.unscaledTime;
        }

        // Compact names for the game's HitMargin values; early/late is baked into the
        // E/L prefixes. Auto tiles are scored as Auto but hit dead-centre, and anything
        // outside the counted window reads as a miss.
        private static string JudgmentWord(HitMargin margin) => margin switch
        {
            HitMargin.Perfect or HitMargin.Auto => "Perfect",
            HitMargin.EarlyPerfect => "EPerfect",
            HitMargin.LatePerfect => "LPerfect",
            HitMargin.VeryEarly => "Early",
            HitMargin.VeryLate => "Late",
            _ => "Miss",
        };

        private static Color JudgmentColor(HitMargin margin) => margin switch
        {
            HitMargin.Perfect or HitMargin.Auto => new Color(0.4f, 1f, 0.5f),          // green
            HitMargin.EarlyPerfect or HitMargin.LatePerfect => new Color(0.75f, 1f, 0.35f), // lime
            HitMargin.VeryEarly or HitMargin.VeryLate => new Color(1f, 0.65f, 0.2f),   // orange
            _ => new Color(1f, 0.4f, 0.35f),                                           // red
        };

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
            var kinds = new List<NoteKind>(floors.Count);

            // Skip floor 0 (the planet starts there — no tap) and fake/decorative tiles.
            for (int i = 1; i < floors.Count; i++)
            {
                var f = floors[i];
                if (f == null || f.isFake)
                {
                    continue;
                }

                times.Add(f.entryTime);
                kinds.Add(Classify(f));
            }

            noteTimes = times.ToArray();
            noteKind = kinds.ToArray();
        }

        // tapsNeeded > 1 is a multitap (two keys at once); it takes priority as the
        // most demanding input to convey.
        private static NoteKind Classify(scrFloor f)
        {
            if (f.tapsNeeded > 1)
            {
                return NoteKind.Multitap;
            }
            return f.midSpin ? NoteKind.Midspin : NoteKind.Normal;
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

                if (noteKind[i] == NoteKind.Multitap)
                {
                    // Two keys at once — draw a double bar to echo that.
                    Color c = new Color(s.multitapColorR, s.multitapColorG, s.multitapColorB, alpha);
                    DrawRect(laneLeft, y - 3f, s.laneWidth, 2f, c);
                    DrawRect(laneLeft, y + 1f, s.laneWidth, 2f, c);
                }
                else
                {
                    Color c = noteKind[i] == NoteKind.Midspin
                        ? new Color(1f, 0.55f, 0.1f, alpha)   // orange
                        : new Color(s.noteColorR, s.noteColorG, s.noteColorB, alpha);
                    DrawRect(laneLeft, y - 2f, s.laneWidth, 4f, c);
                }
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
            Color c = lastJudgmentColor;
            c.a = 1f - age / ErrorDisplaySeconds;

            if (errorStyle == null)
            {
                errorStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }
            errorStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(laneWidth * 0.11f), 14, 40);
            errorStyle.alignment = TextAnchor.MiddleRight;
            errorStyle.normal.textColor = c;

            var content = new GUIContent($"{lastJudgment}  {ms:+0;-0;0} ms");
            Vector2 size = errorStyle.CalcSize(content);
            GUI.Label(new Rect(laneLeft - size.x - 8f, hitLineY - size.y * 0.5f, size.x, size.y), content, errorStyle);
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
