using UnityModManagerNet;

namespace AdofaiHighway
{
    // Persisted to Mods/AdofaiHighway/Settings.xml by UMM.
    public class Settings : UnityModManager.ModSettings
    {
        // Scroll speed of the highway. Higher = notes spaced further apart,
        // so fast runs read as a taller cluster.
        public float pixelsPerSecond = 320f;

        // On-screen size/placement of the vertical lane, in pixels.
        public float laneWidth = 240f;
        public float laneHeightFraction = 0.9f;   // fraction of screen height
        public float rightMargin = 24f;           // gap from the right screen edge
        public float hitLineFromBottom = 90f;      // hit line height above lane bottom

        // Highway backdrop opacity (0 = invisible lane, 1 = solid black).
        public float laneOpacity = 0.45f;

        // Note colour (RGB, 0..1). Midspins keep a fixed contrasting accent so they
        // stay distinguishable whatever colour is chosen here.
        public float noteColorR = 0.30f;
        public float noteColorG = 0.85f;
        public float noteColorB = 1.00f;

        // Beat/measure grid lines — fixed reference points so the moving notes aren't
        // the only thing on screen. Every beat gets a faint line; every Nth beat (a
        // "measure") gets a brighter one. The game has no time signature, so N is set
        // here. (Beat times come from each tile's entryBeat — real musical beats.)
        public bool showBeatLines = true;
        public int beatsPerMeasure = 4;
        public float beatLineOpacity = 0.22f;

        // Manual alignment nudge on top of auto-calibration, for personal taste
        // (some players like the line to lead the beat slightly). Positive = later.
        public float offsetMs = 0f;

        // Auto-measured calibration offset (ms), seeded from the last session. The
        // mod refines this live by watching real tile landings (see HighwayBehaviour),
        // so the very first notes of a fresh level are already close. Not user-edited.
        public float autoOffsetMs = 0f;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
