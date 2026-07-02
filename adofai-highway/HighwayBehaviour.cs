using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdofaiHighway
{
    // Draws the note highway as an IMGUI overlay, scrolled by the game's conductor clock.
    internal sealed class HighwayBehaviour : MonoBehaviour
    {
        internal static HighwayBehaviour Instance;

        // Midspin = the tile's rapid double-tap variant.
        // Multitap = several keys at once.
        // Multitaps are represented as two thinner stacked lines of a different color
        // to normal notes.
        private enum NoteKind { Normal, Midspin, Multitap }

        private readonly struct Note
        {
            public readonly double Time;
            public readonly NoteKind Kind;

            public Note(double time, NoteKind kind)
            {
                Time = time;
                Kind = kind;
            }
        }

        private readonly struct BeatMarker
        {
            public readonly double Time;
            public readonly int Number;

            public BeatMarker(double time, int number)
            {
                Time = time;
                Number = number;
            }
        }

        private Note[] notes = new Note[0];
        private BeatMarker[] beatMarkers = new BeatMarker[0];

        // listFloors is rebuilt as a new List per level, so a changed reference or
        // count means a new level is loaded.
        private List<scrFloor> lastFloors;
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

        private static readonly Color PerfectGreen = new Color(0.4f, 1f, 0.5f);
        private static readonly Color NearPerfectLime = new Color(0.75f, 1f, 0.35f);
        private static readonly Color CountedOrange = new Color(1f, 0.65f, 0.2f);
        private static readonly Color MissRed = new Color(1f, 0.4f, 0.35f);

        // How long a landed note stays pinned to the hit line while it fades.
        private const double LandingFadeSeconds = 0.12;

        private Texture2D pixel;
        private GUIStyle errorStyle;

        // Called from JudgmentPatch whenever the game records a judgment on the scoreboard.
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
            // (hold grace, multipress part, over-press).
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

            double errorRadians = WrapToHalfRevolution(angleDiff);
            double errorBeats = errorRadians / Math.PI;   // pi radians of sweep = one beat
            double wallSecondsPerBeat = 60.0 / bpmTimesSpeed / pitch;
            lastErrorMs = (float)(errorBeats * wallSecondsPerBeat * 1000.0);
            lastJudgment = JudgmentWord(pendingMargin);
            lastJudgmentColor = JudgmentColor(pendingMargin);
            lastErrorAt = Time.unscaledTime;
        }

        // The planet's angle accumulates unwrapped, so an error can arrive offset by
        // whole revolutions (hold wind-ups, a stale reference just after a restart),
        // while a scored hit's true error is always well under half a revolution.
        private static double WrapToHalfRevolution(double radians)
        {
            double twoPi = 2.0 * Math.PI;
            return radians - twoPi * Math.Round(radians / twoPi);
        }

        // Auto tiles are scored as HitMargin.Auto but land dead-centre, so they read
        // as Perfect; anything outside the counted window reads as a miss.
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
            HitMargin.Perfect or HitMargin.Auto => PerfectGreen,
            HitMargin.EarlyPerfect or HitMargin.LatePerfect => NearPerfectLime,
            HitMargin.VeryEarly or HitMargin.VeryLate => CountedOrange,
            _ => MissRed,
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

            if (!ReferenceEquals(floors, lastFloors) || floors.Count != lastFloorsCount)
            {
                RebuildNotes(floors);
                RebuildBeatMarkers(floors);
                lastFloors = floors;
                lastFloorsCount = floors.Count;
                lastSeenSeqID = SeqUnset; // don't measure calibration across a level change
            }

            TrackLandings(floors);
        }

        private void RebuildNotes(List<scrFloor> floors)
        {
            var result = new List<Note>(floors.Count);

            // Floor 0 is where the planet starts, and for fake/decorative tiles.
            for (int i = 1; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (floor == null || floor.isFake)
                {
                    continue;
                }

                result.Add(new Note(floor.entryTime, Classify(floor)));
            }

            notes = result.ToArray();
        }

        // Multitap takes priority as the most demanding input to convey.
        private static NoteKind Classify(scrFloor floor)
        {
            if (floor.tapsNeeded > 1)
            {
                return NoteKind.Multitap;
            }
            return floor.midSpin ? NoteKind.Midspin : NoteKind.Normal;
        }

        // entryBeat is cumulative musical beats (it already bakes in speed changes and
        // pauses), and within a tile-to-tile segment beat maps linearly to time, so we
        // interpolate to place a marker on every integer beat. Floor 0's entryBeat is a
        // -1 sentinel and the last floor is left unset, so only interior floors qualify.
        private void RebuildBeatMarkers(List<scrFloor> floors)
        {
            var result = new List<BeatMarker>();

            for (int i = 1; i < floors.Count - 2; i++)
            {
                var segmentStart = floors[i];
                var segmentEnd = floors[i + 1];
                if (segmentStart == null || segmentEnd == null)
                {
                    continue;
                }

                double startBeat = segmentStart.entryBeat;
                double endBeat = segmentEnd.entryBeat;
                double beatSpan = endBeat - startBeat;
                if (beatSpan <= 0.0)
                {
                    continue;
                }

                // Half-open [startBeat, endBeat) so a beat landing on a tile boundary
                // is emitted once, by the segment that starts on it.
                for (int beat = (int)Math.Ceiling(startBeat - 1e-6); beat < endBeat - 1e-6; beat++)
                {
                    double frac = (beat - startBeat) / beatSpan;
                    double time = segmentStart.entryTime + frac * (segmentEnd.entryTime - segmentStart.entryTime);
                    result.Add(new BeatMarker(time, beat));
                }
            }

            beatMarkers = result.ToArray();
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
            double frameDelta = conductor.deltaSongPos;
            bool normalAdvance = prev != SeqUnset && cur > prev && (cur - prev) <= 4
                                 && frameDelta > 0.0 && frameDelta < 0.5;
            if (!normalAdvance || cur < 0 || cur >= floors.Count)
            {
                return;
            }

            // The landing is noticed up to a frame late, so it truly happened about
            // half a frame's worth of song-time before this clock reading.
            double halfFrame = 0.5 * frameDelta;
            double measuredOffset = floors[cur].entryTime - (conductor.songposition_minusv - halfFrame);

            // Snap on the first sample, then lerp to smooth per-frame jitter.
            autoOffsetSeconds = hasCalibrated
                ? autoOffsetSeconds + (measuredOffset - autoOffsetSeconds) * 0.25
                : measuredOffset;
            hasCalibrated = true;
            Startup.Settings.autoOffsetMs = (float)(autoOffsetSeconds * 1000.0);
        }

        private void OnGUI()
        {
            // Draw-only overlay: skip the layout/input IMGUI passes.
            if (Event.current.type != EventType.Repaint || notes.Length == 0)
            {
                return;
            }

            var conductor = ADOBase.conductor;
            if (conductor == null)
            {
                return;
            }

            var settings = Startup.Settings;
            double now = conductor.songposition_minusv + autoOffsetSeconds + settings.offsetMs / 1000.0;

            float laneHeight = Screen.height * settings.laneHeightFraction;
            float laneTop = (Screen.height - laneHeight) * 0.5f;
            float laneBottom = laneTop + laneHeight;
            float laneLeft = Screen.width - settings.laneWidth - settings.rightMargin;
            float hitLineY = laneBottom - settings.hitLineFromBottom;
            double leadSeconds = (hitLineY - laneTop) / settings.pixelsPerSecond;

            DrawRect(laneLeft, laneTop, settings.laneWidth, laneHeight, new Color(0f, 0f, 0f, settings.laneOpacity));

            if (settings.showBeatLines)
            {
                DrawBeatGrid(now, leadSeconds, laneLeft, hitLineY);
            }

            double secondsSinceLanding = DrawNotes(now, leadSeconds, laneLeft, hitLineY);
            if (secondsSinceLanding < LandingFadeSeconds)
            {
                float glow = 1f - (float)(secondsSinceLanding / LandingFadeSeconds);
                DrawRect(laneLeft, hitLineY - 8f, settings.laneWidth, 16f, new Color(1f, 0.95f, 0.4f, 0.55f * glow));
            }

            DrawRect(laneLeft, hitLineY - 1.5f, settings.laneWidth, 3f, new Color(1f, 1f, 1f, 0.9f));

            DrawErrorReadout(laneLeft, hitLineY, settings.laneWidth);
        }

        // Drawn under the notes, and only above the hit line.
        private void DrawBeatGrid(double now, double leadSeconds, float laneLeft, float hitLineY)
        {
            var settings = Startup.Settings;
            int perMeasure = Mathf.Max(1, settings.beatsPerMeasure);

            for (int i = 0; i < beatMarkers.Length; i++)
            {
                double secondsUntil = beatMarkers[i].Time - now;
                if (secondsUntil < 0.0 || secondsUntil > leadSeconds)
                {
                    continue;
                }

                float y = hitLineY - (float)(secondsUntil * settings.pixelsPerSecond);
                bool measureStart = beatMarkers[i].Number % perMeasure == 0;
                float alpha = Mathf.Clamp01(settings.beatLineOpacity * (measureStart ? 2f : 1f));
                DrawRect(laneLeft, y, settings.laneWidth, measureStart ? 2f : 1f, new Color(1f, 1f, 1f, alpha));
            }
        }

        // A note reaches the hit line at its time. Past that it pins to the line and
        // fades out quickly rather than sliding on past.
        // Returns how long ago the most recent note landed.
        private double DrawNotes(double now, double leadSeconds, float laneLeft, float hitLineY)
        {
            var settings = Startup.Settings;
            double secondsSinceLanding = double.PositiveInfinity;

            for (int i = 0; i < notes.Length; i++)
            {
                double secondsUntil = notes[i].Time - now;
                if (secondsUntil > leadSeconds)
                {
                    continue;
                }

                float y;
                float alpha;
                if (secondsUntil >= 0.0)
                {
                    y = hitLineY - (float)(secondsUntil * settings.pixelsPerSecond);
                    alpha = 0.95f;
                }
                else
                {
                    double pastBy = -secondsUntil;
                    if (pastBy > LandingFadeSeconds)
                    {
                        continue;
                    }
                    if (pastBy < secondsSinceLanding)
                    {
                        secondsSinceLanding = pastBy;
                    }
                    y = hitLineY;
                    alpha = 0.95f * (1f - (float)(pastBy / LandingFadeSeconds));
                }

                if (notes[i].Kind == NoteKind.Multitap)
                {
                    Color color = new Color(settings.multitapColorR, settings.multitapColorG, settings.multitapColorB, alpha);
                    DrawRect(laneLeft, y - 3f, settings.laneWidth, 2f, color);
                    DrawRect(laneLeft, y + 1f, settings.laneWidth, 2f, color);
                }
                else
                {
                    Color color = notes[i].Kind == NoteKind.Midspin
                        ? new Color(1f, 0.55f, 0.1f, alpha)
                        : new Color(settings.noteColorR, settings.noteColorG, settings.noteColorB, alpha);
                    DrawRect(laneLeft, y - 2f, settings.laneWidth, 4f, color);
                }
            }

            return secondsSinceLanding;
        }

        private void DrawErrorReadout(float laneLeft, float hitLineY, float laneWidth)
        {
            float age = Time.unscaledTime - lastErrorAt;
            if (age < 0f || age > ErrorDisplaySeconds)
            {
                return;
            }

            Color color = lastJudgmentColor;
            color.a = 1f - age / ErrorDisplaySeconds;

            // GUI.skin is only accessible during OnGUI, so the style is built lazily here.
            if (errorStyle == null)
            {
                errorStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }
            errorStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(laneWidth * 0.11f), 14, 40);
            errorStyle.alignment = TextAnchor.MiddleRight;
            errorStyle.normal.textColor = color;

            int ms = Mathf.RoundToInt(lastErrorMs);
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
