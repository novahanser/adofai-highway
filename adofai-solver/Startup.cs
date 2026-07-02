using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace AdofaiHighway
{
    // UMM entry point (Info.json's EntryMethod points at Load).
    internal static class Startup
    {
        internal static UnityModManager.ModEntry ModEntry;
        internal static UnityModManager.ModEntry.ModLogger Logger;
        internal static Settings Settings;

        private static HighwayBehaviour behaviour;
        private static Harmony harmony;
        private static Texture2D whiteTex;   // 1x1, tinted for the note-colour swatch

        internal static void Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Logger = modEntry.Logger;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            whiteTex = new Texture2D(1, 1);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            if (enabled)
            {
                // Hook the game's hit-error signal for the timing readout.
                harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                // Own the overlay from a persistent object so it survives scene loads.
                var go = new GameObject("AdofaiHighway");
                Object.DontDestroyOnLoad(go);
                behaviour = go.AddComponent<HighwayBehaviour>();
                Logger.Log("Highway overlay enabled.");
            }
            else
            {
                if (behaviour != null)
                {
                    Object.Destroy(behaviour.gameObject);
                    behaviour = null;
                }
                if (harmony != null)
                {
                    harmony.UnpatchAll(modEntry.Info.Id);
                    harmony = null;
                }
                Logger.Log("Highway overlay disabled.");
            }

            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            var s = Settings;

            GUILayout.Label($"Scroll speed: {s.pixelsPerSecond:0} px/s");
            s.pixelsPerSecond = GUILayout.HorizontalSlider(s.pixelsPerSecond, 80f, 800f);

            GUILayout.Label($"Lane width: {s.laneWidth:0} px");
            s.laneWidth = GUILayout.HorizontalSlider(s.laneWidth, 80f, 500f);

            GUILayout.Label($"Lane height: {s.laneHeightFraction * 100f:0}% of screen");
            s.laneHeightFraction = GUILayout.HorizontalSlider(s.laneHeightFraction, 0.3f, 1f);

            GUILayout.Label($"Hit line height: {s.hitLineFromBottom:0} px from bottom");
            s.hitLineFromBottom = GUILayout.HorizontalSlider(s.hitLineFromBottom, 0f, 300f);

            GUILayout.Space(8f);
            GUILayout.Label($"Highway opacity: {s.laneOpacity * 100f:0}%");
            s.laneOpacity = GUILayout.HorizontalSlider(s.laneOpacity, 0f, 1f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Note colour", GUILayout.Width(90f));
            GUILayout.Box(GUIContent.none, GUILayout.Width(60f), GUILayout.Height(16f));
            Rect swatch = GUILayoutUtility.GetLastRect();
            Color prev = GUI.color;
            GUI.color = new Color(s.noteColorR, s.noteColorG, s.noteColorB, 1f);
            GUI.DrawTexture(swatch, whiteTex);
            GUI.color = prev;
            GUILayout.EndHorizontal();
            s.noteColorR = GUILayout.HorizontalSlider(s.noteColorR, 0f, 1f);
            s.noteColorG = GUILayout.HorizontalSlider(s.noteColorG, 0f, 1f);
            s.noteColorB = GUILayout.HorizontalSlider(s.noteColorB, 0f, 1f);

            GUILayout.Space(8f);
            s.showBeatLines = GUILayout.Toggle(s.showBeatLines, " Show beat / measure lines");
            if (s.showBeatLines)
            {
                GUILayout.Label($"Beats per measure: {s.beatsPerMeasure}");
                s.beatsPerMeasure = Mathf.RoundToInt(GUILayout.HorizontalSlider(s.beatsPerMeasure, 1f, 8f));
                GUILayout.Label($"Beat line opacity: {s.beatLineOpacity * 100f:0}%");
                s.beatLineOpacity = GUILayout.HorizontalSlider(s.beatLineOpacity, 0f, 1f);
            }

            GUILayout.Space(8f);
            GUILayout.Label($"Auto-calibration: {s.autoOffsetMs:+0;-0;0} ms (measured live from tile landings)");
            if (GUILayout.Button("Reset calibration", GUILayout.Width(160f)))
            {
                HighwayBehaviour.Instance?.ResetCalibration();
            }

            GUILayout.Label($"Manual nudge: {s.offsetMs:+0;-0;0} ms");
            s.offsetMs = GUILayout.HorizontalSlider(s.offsetMs, -300f, 300f);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}
