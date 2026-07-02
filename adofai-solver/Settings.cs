using UnityModManagerNet;

namespace AdofaiHighway
{
    // Persisted by UMM to Mods/AdofaiHighway/Settings.xml.
    public class Settings : UnityModManager.ModSettings
    {
        public float pixelsPerSecond = 320f;

        public float laneWidth = 240f;
        public float laneHeightFraction = 0.9f;    // of screen height
        public float rightMargin = 24f;
        public float hitLineFromBottom = 90f;      // px above the lane's bottom edge

        public float laneOpacity = 0.45f;

        // 0..1 RGB. Midspins keep a fixed accent so they stay distinct from this.
        public float noteColorR = 0.30f;
        public float noteColorG = 0.85f;
        public float noteColorB = 1.00f;

        // ADOFAI has no time signature, so the measure size is chosen here.
        public bool showBeatLines = true;
        public int beatsPerMeasure = 4;
        public float beatLineOpacity = 0.22f;

        // Manual nudge on top of auto-calibration, to taste. Positive = notes later.
        public float offsetMs = 0f;

        // Auto-measured calibration, seeded from the last session; not user-edited.
        public float autoOffsetMs = 0f;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
