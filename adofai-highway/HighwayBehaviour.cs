using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdofaiHighway
{
    // Draws the note highway as an IMGUI overlay, scrolled by the game's conductor clock.
    internal sealed class HighwayBehaviour : MonoBehaviour
    {
        internal static HighwayBehaviour Instance;

        private enum NoteKind { Normal, Multitap }

        private readonly struct Note
        {
            public readonly double Time;
            public readonly NoteKind Kind;
            public readonly double ReleaseTime;
            public readonly double DisplayTime;
            public readonly int Lane, GroupSize, OverflowCount;

            public Note(double time, NoteKind kind, double releaseTime, double displayTime,
                int lane, int groupSize, int overflowCount = 0)
            {
                Time = time;
                Kind = kind;
                ReleaseTime = releaseTime;
                DisplayTime = displayTime;
                Lane = lane;
                GroupSize = groupSize;
                OverflowCount = overflowCount;
            }

            public bool IsHold => ReleaseTime > Time;
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

        private readonly struct OverflowMarker
        {
            internal readonly double Time, Release;
            internal readonly long Count;
            internal readonly string ShortLabel;
            internal OverflowMarker(double time, double release, long count)
            {
                Time = time;
                Release = release;
                Count = count;
                ShortLabel = $"+{count}";
            }
        }

        private OverflowMarker[] overflowMarkers = new OverflowMarker[0];
        private Note[] notes = new Note[0];
        private RhythmEvent[] sourceEvents = new RhythmEvent[0];
        private double[] lastVisiblePrefix = new double[0];
        private BeatMarker[] beatMarkers = new BeatMarker[0];
        private bool chartDirty = true;
        private int lastAssignmentSettings;
        private double lastPlaybackSpeed = -1;
        private bool lastHitOnce;

        // listFloors is rebuilt as a new List per level, so a changed reference or count means a new level is loaded.
        private List<scrFloor> lastFloors;
        private int lastFloorsCount = -1;

        // Most recent scored hit's timing error, set by RecordHit. Positive = late,
        // negative = early. The word and colour come from the judgment the game itself
        // recorded, so the readout always agrees with the hit text the game shows.
        private static float lastErrorMs;
        private static string lastJudgment = "";
        private static Color lastJudgmentColor = Color.white;
        private static float lastErrorUnscaledTime = -999f;
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
        private const double LandingFadeSeconds = 0.1;

        // Fill opacity of a hold's body, dim enough that beat lines and the end bars still read through it.
        private const float HoldBodyAlpha = 0.35f;

        private Texture2D pixel;
        private GUIStyle errorStyle;
        private GUIStyle laneLabelStyle, overflowStyle;
        private static readonly string[] LaneLabels = { "1", "2", "3", "4", "5", "6", "7", "8" };

        internal void InvalidateChart() => chartDirty = true;

        internal static void RecordJudgment(HitMargin margin)
        {
            pendingMargin = margin;
            pendingMarginFrame = Time.frameCount;
        }

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

            // Same tile-speed source as the game's AddHit: the hit floor, or the floor the planet just left when the caller passed none.
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
            double errorBeats = errorRadians / Math.PI; // pi radians of sweep = one beat
            double wallSecondsPerBeat = 60.0 / bpmTimesSpeed / pitch;

            lastErrorMs = (float)(errorBeats * wallSecondsPerBeat * 1000.0);
            lastJudgment = JudgmentWord(pendingMargin);
            lastJudgmentColor = JudgmentColor(pendingMargin);
            lastErrorUnscaledTime = Time.unscaledTime;
        }

        // The planet's angle accumulates unwrapped, so an error can arrive offset by
        // whole revolutions (hold wind-ups, a stale reference just after a restart),
        // while a scored hit's true error is always well under half a revolution.
        private static double WrapToHalfRevolution(double radians)
        {
            double twoPi = 2.0 * Math.PI;
            return radians - twoPi * Math.Round(radians / twoPi);
        }

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

            RefreshClockCalibration();
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
            RefreshClockCalibration();
        }

        private static void RefreshClockCalibration()
        {
            // The game itself compares entryTime to songposition_minusi for hold
            // progress. Display its exact input-vs-visual correction in the menu;
            // never estimate it from a player's early/late landings.
            var conductor = ADOBase.conductor;
            Startup.Settings.autoOffsetMs = conductor != null
                ? (float)((conductor.songposition_minusi - conductor.songposition_minusv) * 1000.0)
                : 0f;
        }

        private void Update()
        {
            var lm = ADOBase.lm;
            var floors = lm != null ? lm.listFloors : null;
            if (floors == null)
            {
                if (lastFloors != null)
                {
                    notes = new Note[0];
                    sourceEvents = new RhythmEvent[0];
                    overflowMarkers = new OverflowMarker[0];
                    beatMarkers = new BeatMarker[0];
                    lastVisiblePrefix = new double[0];
                    lastFloors = null;
                    lastFloorsCount = -1;
                    chartDirty = true;
                }
                return;
            }

            bool hitOnce = Persistence.multiTapTileBehavior == MultitapTileBehavior.HitOnce;
            bool rebuild = chartDirty || !ReferenceEquals(floors, lastFloors)
                || floors.Count != lastFloorsCount || hitOnce != lastHitOnce;
            if (rebuild)
            {
                RebuildNotes(floors);
                RebuildBeatMarkers(floors);
                lastFloors = floors;
                lastFloorsCount = floors.Count;
                lastHitOnce = hitOnce;
                chartDirty = false;
            }

            var settings = Startup.Settings;
            var conductor = ADOBase.conductor;
            double speed = settings.followPlaybackSpeed && conductor != null && conductor.song != null
                ? conductor.song.pitch : 1.0;
            if (speed <= 0 || double.IsNaN(speed) || double.IsInfinity(speed)) speed = 1.0;
            int signature = AssignmentSignature(settings);
            if (rebuild || signature != lastAssignmentSettings || Math.Abs(speed - lastPlaybackSpeed) > 0.0001)
            {
                RebuildAssignments(settings, speed);
                lastAssignmentSettings = signature;
                lastPlaybackSpeed = speed;
            }

            RefreshClockCalibration();
        }

        private void RebuildNotes(List<scrFloor> floors)
        {
            var snapshot = new FloorTiming[floors.Count];
            var conductor = ADOBase.conductor;
            double baseBpm = conductor != null ? conductor.bpm : 120;
            bool hitOnce = Persistence.multiTapTileBehavior == MultitapTileBehavior.HitOnce;
            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (floor == null)
                {
                    snapshot[i] = new FloorTiming(0, fake: true);
                    continue;
                }
                // angleLength is OUTGOING travel in radians (already includes holds
                // and planet/direction changes); entryTime is the game's song clock.
                snapshot[i] = new FloorTiming(floor.entryTime,
                    floor.angleLength * 180.0 / Math.PI + floor.extraBeats * 180.0,
                    baseBpm * floor.speed, floor.isFake, floor.auto, floor.freeroam,
                    floor.midSpin, floor.holdLength > -1, hitOnce ? 1 : floor.tapsNeeded);
            }
            sourceEvents = ChartEventBuilder.Build(snapshot);
        }

        private static int AssignmentSignature(Settings s)
        {
            unchecked
            {
                int hash = s.laneCount * 397 + s.splitMode;
                hash = hash * 397 + s.singleKps.GetHashCode();
                hash = hash * 397 + (s.rightHandPrimary ? 1 : 0);
                hash = hash * 397 + (s.followPlaybackSpeed ? 1 : 0);
                return hash * 397 + (s.mergeNearbyNotes ? 1 : 0);
            }
        }

        private void RebuildAssignments(Settings settings, double speed)
        {
            var result = new List<Note>();
            if (settings.laneCount == 1)
            {
                foreach (var source in sourceEvents)
                    result.Add(new Note(source.Time, source.RequiredKeys > 1 ? NoteKind.Multitap : NoteKind.Normal,
                        source.ReleaseTime, source.Time, 0, source.RequiredKeys));
            }
            else
            {
                var options = new LaneOptions
                {
                    LaneCount = settings.laneCount, Mode = settings.splitMode,
                    SingleKps = settings.singleKps, MainHandRight = settings.rightHandPrimary,
                    FollowPlaybackSpeed = settings.followPlaybackSpeed, MergeNearby = settings.mergeNearbyNotes,
                };
                foreach (var assigned in LaneAllocator.Assign(sourceEvents, options, speed))
                {
                    var source = sourceEvents[assigned.SourceIndex];
                    var kind = source.RequiredKeys > 1 || assigned.GroupSize > 1 ? NoteKind.Multitap : NoteKind.Normal;
                    result.Add(new Note(assigned.Time, kind, assigned.ReleaseTime, assigned.DisplayTime,
                        assigned.Lane, assigned.GroupSize, assigned.OverflowCount));
                }
            }
            result.Sort((a, b) =>
            {
                int time = a.DisplayTime.CompareTo(b.DisplayTime);
                return time != 0 ? time : a.Lane.CompareTo(b.Lane);
            });
            notes = result.ToArray();
            var overflow = new List<OverflowMarker>();
            foreach (var note in notes)
            {
                if (note.OverflowCount <= 0) continue;
                int last = overflow.Count - 1;
                if (last >= 0 && overflow[last].Time == note.DisplayTime)
                {
                    var previous = overflow[last];
                    overflow[last] = new OverflowMarker(previous.Time,
                        Math.Max(previous.Release, note.ReleaseTime), previous.Count + note.OverflowCount);
                }
                else overflow.Add(new OverflowMarker(note.DisplayTime, note.ReleaseTime, note.OverflowCount));
            }
            overflowMarkers = overflow.ToArray();
            lastVisiblePrefix = new double[notes.Length];
            double end = double.NegativeInfinity;
            for (int i = 0; i < notes.Length; i++)
            {
                end = Math.Max(end, Math.Max(notes[i].ReleaseTime, notes[i].DisplayTime) + LandingFadeSeconds);
                lastVisiblePrefix[i] = end;
            }
        }

        // entryBeat is cumulative geometry (including pauses); within each tile segment we
        // interpolate to place a marker on every integer beat. Floor 0's entryBeat is a -1 sentinel and the last floor is left unset, so only interior floors qualify.
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

                // Half-open [startBeat, endBeat) so a beat landing on a tile boundary is emitted once, by the segment that starts on it.
                for (int beat = (int)Math.Ceiling(startBeat - 1e-6); beat < endBeat - 1e-6; beat++)
                {
                    double frac = (beat - startBeat) / beatSpan;
                    double time = segmentStart.entryTime + frac * (segmentEnd.entryTime - segmentStart.entryTime);
                    result.Add(new BeatMarker(time, beat));
                }
            }

            beatMarkers = result.ToArray();
        }

        private void OnGUI()
        {
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
            // Same song-time coordinate as the game's own hold progress and input
            // judgment. Positive manual nudge makes notes reach the line later.
            double now = conductor.songposition_minusi - settings.offsetMs / 1000.0;

            float laneHeight = Screen.height * settings.laneHeightFraction;
            float laneTop = (Screen.height - laneHeight) * 0.5f;
            float width = Mathf.Min(settings.laneWidth, Mathf.Max(40f, Screen.width - 8f));
            float laneLeft = Mathf.Clamp(Screen.width - width - settings.rightMargin, 0f, Screen.width - width);
            float hitLineThickness = Mathf.Min(settings.hitLineThickness, laneHeight);
            float hitLineY = HighwayGeometry.HitLineY(laneHeight, settings.hitLinePositionMode,
                settings.hitLineFromBottom, settings.hitLinePercent, hitLineThickness);
            double leadSeconds = hitLineY / settings.pixelsPerSecond;

            int previousDepth = GUI.depth;
            GUI.depth = 100; // Keep the UMM settings window in front of the overlay.
            GUI.BeginGroup(new Rect(laneLeft, laneTop, width, laneHeight));
            try
            {
                DrawRect(0, 0, width, laneHeight, new Color(0f, 0f, 0f, settings.laneOpacity));
                float cellWidth = width / settings.laneCount;
                if (settings.showLaneDividers && settings.laneCount > 1)
                {
                    for (int lane = 1; lane < settings.laneCount; lane++)
                    {
                        bool handBoundary = lane == settings.laneCount / 2;
                        DrawRect(lane * cellWidth - 0.5f, 0, handBoundary ? 2 : 1, laneHeight,
                            new Color(1, 1, 1, handBoundary ? 0.4f : 0.15f));
                    }
                }
                if (settings.showBeatLines) DrawBeatGrid(now, leadSeconds, width, hitLineY);
                DrawNotes(now, leadSeconds, width, hitLineY, laneHeight);
                DrawRect(0, hitLineY - hitLineThickness / 2f, width,
                    hitLineThickness, new Color(1f, 1f, 1f, 0.9f));
                if (settings.showLaneLabels && settings.laneCount > 1)
                {
                    if (laneLabelStyle == null)
                        laneLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                    laneLabelStyle.fontSize = Mathf.Clamp((int)(cellWidth * 0.3f), 11, 20);
                    laneLabelStyle.normal.textColor = Color.white;
                    for (int lane = 0; lane < settings.laneCount; lane++)
                        GUI.Label(new Rect(lane * cellWidth, Mathf.Min(hitLineY + 6, laneHeight - 24), cellWidth, 22),
                            LaneLabels[lane], laneLabelStyle);
                }
                // Draw warnings last so ordinary chord bars and hold bodies cannot
                // hide them. Multiple shortages at one display time are aggregated.
                DrawOverflowWarnings(now, leadSeconds, width, hitLineY);
            }
            finally
            {
                GUI.EndGroup();
                GUI.depth = previousDepth;
            }
            DrawErrorReadout(laneLeft, laneTop + hitLineY, width);
        }

        private void DrawBeatGrid(double now, double leadSeconds, float width, float hitLineY)
        {
            var settings = Startup.Settings;
            int perMeasure = Mathf.Max(1, settings.beatsPerMeasure);

            int low = 0, high = beatMarkers.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (beatMarkers[middle].Time < now) low = middle + 1;
                else high = middle;
            }
            for (int i = low; i < beatMarkers.Length; i++)
            {
                double secondsUntil = beatMarkers[i].Time - now;
                if (secondsUntil > leadSeconds) break;

                float y = hitLineY - (float)(secondsUntil * settings.pixelsPerSecond);
                bool measureStart = beatMarkers[i].Number % perMeasure == 0;
                float alpha = Mathf.Clamp01(settings.beatLineOpacity * (measureStart ? 2f : 1f));
                DrawRect(0, y, width, measureStart ? 2f : 1f, new Color(1f, 1f, 1f, alpha));
            }
        }

        private void DrawNotes(double now, double leadSeconds, float width, float hitLineY, float availableHeight)
        {
            var settings = Startup.Settings;
            float cellWidth = width / settings.laneCount;
            float gap = settings.laneCount > 1 ? HighwayGeometry.LaneGap(width, settings.laneCount, settings.laneGap) : 0;
            float thickness = HighwayGeometry.Thickness(settings.noteThickness, availableHeight);

            for (int i = FirstVisibleNote(now); i < notes.Length; i++)
            {
                var note = notes[i];
                double secondsUntil = note.DisplayTime - now;
                if (secondsUntil > leadSeconds + thickness / (2f * settings.pixelsPerSecond))
                {
                    break;
                }

                if (note.OverflowCount > 0) continue;
                if (note.Lane < 0 || note.Lane >= settings.laneCount) continue;

                float left = note.Lane * cellWidth + gap * 0.5f;
                float noteWidth = cellWidth - gap;
                if (note.IsHold) DrawHoldBody(note, now, leadSeconds, left, noteWidth, hitLineY, thickness);

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
                    y = hitLineY;
                    alpha = 0.95f * (1f - (float)(pastBy / LandingFadeSeconds));
                    DrawRect(left, hitLineY - 8f, noteWidth, 16f,
                        new Color(1f, 0.95f, 0.4f, 0.55f * (alpha / 0.95f)));
                }

                if (note.Kind == NoteKind.Multitap)
                {
                    Color color = BarColor(note, settings, alpha);
                    float middleGap = Mathf.Min(3f, thickness / 3f);
                    float band = (thickness - middleGap) / 2f;
                    DrawRect(left, y - thickness / 2f, noteWidth, band, color);
                    DrawRect(left, y + middleGap / 2f, noteWidth, band, color);
                }
                else
                {
                    DrawRect(left, y - thickness / 2f, noteWidth, thickness, BarColor(note, settings, alpha));
                }
            }
        }

        private int FirstVisibleNote(double now)
        {
            int low = 0, high = lastVisiblePrefix.Length;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (lastVisiblePrefix[mid] < now) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private void DrawOverflowWarnings(double now, double leadSeconds, float width, float hitLineY)
        {
            if (overflowStyle == null)
                overflowStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            overflowStyle.normal.textColor = Color.white;
            foreach (var warning in overflowMarkers)
            {
                if (warning.Time - now > leadSeconds) break;
                if (now - warning.Time > LandingFadeSeconds && warning.Release <= now) continue;
                float y = Mathf.Min(hitLineY, hitLineY - (float)((warning.Time - now) * Startup.Settings.pixelsPerSecond));
                DrawRect(0, y - 10, width, 20, new Color(0.65f, 0.06f, 0.04f, 0.9f));
                string label = width < 250 ? warning.ShortLabel
                    : Startup.Text($"轨道容量不足：还需 {warning.Count} 键", $"+{warning.Count} key(s): lane capacity exceeded");
                GUI.Label(new Rect(0, y - 10, width, 20), label, overflowStyle);
            }
        }

        private void DrawHoldBody(in Note note, double now, double leadSeconds, float laneLeft, float width, float hitLineY, float thickness)
        {
            var settings = Startup.Settings;
            double untilRelease = note.ReleaseTime - now;
            if (untilRelease <= 0.0)
            {
                return;
            }

            float top = hitLineY - (float)(Math.Min(untilRelease, leadSeconds) * settings.pixelsPerSecond);
            double untilPress = note.DisplayTime - now;
            float bottom = untilPress > 0.0
                ? hitLineY - (float)(untilPress * settings.pixelsPerSecond)
                : hitLineY;
            if (bottom <= top)
            {
                return;
            }

            DrawRect(laneLeft, top, width, bottom - top, BarColor(note, settings, HoldBodyAlpha));
            float tailThickness = Mathf.Max(1f, thickness / 2f);
            if (untilRelease <= leadSeconds)
                DrawRect(laneLeft, top - tailThickness / 2f, width, tailThickness, BarColor(note, settings, 0.95f));
            if (untilPress <= 0)
                DrawRect(laneLeft, hitLineY - thickness / 2f, width, thickness, BarColor(note, settings, 0.95f));
        }

        private static Color BarColor(in Note note, Settings settings, float alpha)
        {
            if (note.Kind == NoteKind.Multitap)
                return new Color(settings.multitapColorR, settings.multitapColorG, settings.multitapColorB, alpha);
            if (settings.colorByHand && settings.laneCount > 1 && note.Lane < settings.laneCount / 2)
                return new Color(settings.leftNoteColorR, settings.leftNoteColorG, settings.leftNoteColorB, alpha);
            return new Color(settings.noteColorR, settings.noteColorG, settings.noteColorB, alpha);
        }

        private void DrawErrorReadout(float laneLeft, float hitLineY, float laneWidth)
        {
            float age = Time.unscaledTime - lastErrorUnscaledTime;
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
