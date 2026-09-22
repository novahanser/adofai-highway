using UnityModManagerNet;

namespace AdofaiHighway
{
    // Persisted by UMM to Mods/AdofaiHighway/Settings.xml. Field names are the XML
    // element names, so renaming one silently discards the user's saved value.
    public class Settings : UnityModManager.ModSettings
    {
        // New fields have defaults so existing Settings.xml files remain compatible.
        public int language = 0; // 0: Simplified Chinese; 1: English
        public int laneCount = 1;
        public int splitMode = 0; // 0: inward roll; 1: alternating hands
        public float singleKps = 7.3f;
        public bool rightHandPrimary = true;
        public bool followPlaybackSpeed = true;
        public bool mergeNearbyNotes = false;
        public bool showLaneLabels = true;
        public bool showLaneDividers = true;
        public bool colorByHand = true;
        public float laneGap = 4f;

        public float pixelsPerSecond = 320f;
        public float noteThickness = 6f;
        public float hitLineThickness = 3f;

        public float laneWidth = 240f;
        public float laneHeightFraction = 0.9f; // of screen height
        public float rightMargin = 24f;
        public float hitLineFromBottom = 90f; // px above the lane's bottom edge
        public int hitLinePositionMode = 0; // 0: bottom pixels; 1: percentage from the top
        public float hitLinePercent = 0.85f;

        public float laneOpacity = 0.45f;

        // 0..1 RGB per channel. Multitaps use their own color; midspins add no input.
        public float noteColorR = 0.30f;
        public float noteColorG = 0.85f;
        public float noteColorB = 1.00f;

        public float leftNoteColorR = 0.95f;
        public float leftNoteColorG = 0.55f;
        public float leftNoteColorB = 0.65f;

        public float multitapColorR = 0.75f;
        public float multitapColorG = 0.40f;
        public float multitapColorB = 1.00f;

        // As far as I'm aware, ADOFAI has no time signature, so the measure size is chosen here.
        public bool showBeatLines = true;
        public int beatsPerMeasure = 4;
        public float beatLineOpacity = 0.22f;

        // Manual nudge on top of auto-calibration, to taste. Positive = notes later.
        public float offsetMs = 0f;

        // Exact game input-vs-visual clock correction; legacy XML name retained.
        public float autoOffsetMs = 0f;

        public void Normalize()
        {
            language = language == 1 ? 1 : 0;
            if (laneCount != 1 && laneCount != 4 && laneCount != 6 && laneCount != 8)
            {
                laneCount = 1;
            }
            splitMode = splitMode == 1 ? 1 : 0;
            hitLinePositionMode = hitLinePositionMode == 1 ? 1 : 0;
            singleKps = FiniteClamp(singleKps, 1f, 30f, 7.3f);
            pixelsPerSecond = FiniteClamp(pixelsPerSecond, 10f, 10000f, 320f);
            noteThickness = FiniteClamp(noteThickness, 2f, 80f, 6f);
            hitLineThickness = FiniteClamp(hitLineThickness, 1f, 20f, 3f);
            laneWidth = FiniteClamp(laneWidth, 80f, 1000f, 240f);
            laneHeightFraction = FiniteClamp(laneHeightFraction, 0.3f, 1f, 0.9f);
            rightMargin = FiniteClamp(rightMargin, 0f, 1000f, 24f);
            hitLineFromBottom = FiniteClamp(hitLineFromBottom, 0f, 4320f, 90f);
            hitLinePercent = FiniteClamp(hitLinePercent, 0f, 1f, 0.85f);
            float maxGap = laneCount > 1
                ? System.Math.Min(24f, laneWidth / laneCount - 8f)
                : 24f;
            laneGap = FiniteClamp(laneGap, 0f, maxGap, 4f);
            laneOpacity = FiniteClamp(laneOpacity, 0f, 1f, 0.45f);
            noteColorR = FiniteClamp(noteColorR, 0f, 1f, 0.30f);
            noteColorG = FiniteClamp(noteColorG, 0f, 1f, 0.85f);
            noteColorB = FiniteClamp(noteColorB, 0f, 1f, 1f);
            leftNoteColorR = FiniteClamp(leftNoteColorR, 0f, 1f, 0.95f);
            leftNoteColorG = FiniteClamp(leftNoteColorG, 0f, 1f, 0.55f);
            leftNoteColorB = FiniteClamp(leftNoteColorB, 0f, 1f, 0.65f);
            multitapColorR = FiniteClamp(multitapColorR, 0f, 1f, 0.75f);
            multitapColorG = FiniteClamp(multitapColorG, 0f, 1f, 0.40f);
            multitapColorB = FiniteClamp(multitapColorB, 0f, 1f, 1f);
            beatsPerMeasure = System.Math.Max(1, System.Math.Min(8, beatsPerMeasure));
            beatLineOpacity = FiniteClamp(beatLineOpacity, 0f, 1f, 0.22f);
            offsetMs = FiniteClamp(offsetMs, -300f, 300f, 0f);
            autoOffsetMs = FiniteClamp(autoOffsetMs, -60000f, 60000f, 0f);
        }

        private static float FiniteClamp(float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = fallback;
            }
            return System.Math.Max(minimum, System.Math.Min(maximum, value));
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Normalize();
            Save(this, modEntry);
        }
    }
}
