using System.Collections.Generic;
using System.Globalization;
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
        private static Texture2D swatchTexture;
        private static int selectedTab;
        private static readonly int[] LaneCounts = { 1, 4, 6, 8 };
        private static readonly string[] LaneChoices = { "1K", "4K", "6K", "8K" };
        private static readonly string[] Languages = { "简体中文", "English" };
        private static readonly Dictionary<string, NumberEdit> NumberEdits = new Dictionary<string, NumberEdit>();

        private sealed class NumberEdit
        {
            internal string Text;
            internal bool Dirty;
            internal System.Func<float> Read;
            internal System.Action<float> Write;
            internal float Minimum, Maximum;
            internal Rect FieldRect;
        }

        internal static string Text(string zh, string en) => Settings != null && Settings.language == 1 ? en : zh;

        internal static void Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Logger = modEntry.Logger;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
            Settings.Normalize();
            NumberEdits.Clear();
            EnsureSwatchTexture();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            if (enabled)
            {
                if (behaviour != null && harmony != null)
                {
                    return true;
                }
                CleanupOverlay(modEntry);
                GameObject go = null;
                try
                {
                    Settings.Normalize();
                    harmony = new Harmony(modEntry.Info.Id);
                    harmony.PatchAll(Assembly.GetExecutingAssembly());

                    go = new GameObject("AdofaiHighway");
                    Object.DontDestroyOnLoad(go);
                    behaviour = go.AddComponent<HighwayBehaviour>();
                    Logger.Log("Highway overlay enabled.");
                }
                catch (System.Exception exception)
                {
                    if (go != null && behaviour == null)
                    {
                        Object.Destroy(go);
                    }
                    CleanupOverlay(modEntry);
                    Logger.Error($"Could not enable highway overlay: {exception}");
                    return false;
                }
            }
            else
            {
                CleanupOverlay(modEntry);
                Logger.Log("Highway overlay disabled.");
            }

            return true;
        }

        private static void CleanupOverlay(UnityModManager.ModEntry modEntry)
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
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            CommitNumberEdits();
            CleanupOverlay(modEntry);
            if (swatchTexture != null)
            {
                Object.Destroy(swatchTexture);
                swatchTexture = null;
            }
            NumberEdits.Clear();
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            var s = Settings;
            CommitUnfocusedNumbers();
            // Buttons and sliders do not always take keyboard focus in IMGUI.
            // A click outside the active text box still finishes its edit.
            string focused = GUI.GetNameOfFocusedControl();
            if (Event.current.type == EventType.MouseDown && NumberEdits.TryGetValue(focused, out var active)
                && !active.FieldRect.Contains(Event.current.mousePosition))
            {
                CommitNumber(active);
                GUI.FocusControl(null);
            }
            s.Normalize();
            int language = GUILayout.Toolbar(s.language, Languages, GUILayout.MinHeight(28f));
            if (language != s.language)
            {
                CommitNumberEdits();
                GUI.FocusControl(null);
                s.language = language;
            }
            GUILayout.Space(8f);
            int tab = GUILayout.Toolbar(selectedTab,
                new[] { Text("分轨", "Lanes"), Text("布局", "Layout"), Text("颜色与同步", "Color & Sync") },
                GUILayout.MinHeight(28f));
            if (tab != selectedTab)
            {
                CommitNumberEdits();
                GUI.FocusControl(null);
                selectedTab = tab;
            }
            GUILayout.Space(10f);
            switch (selectedTab)
            {
                case 0:
                    DrawLaneSettings(s);
                    break;
                case 1:
                    DrawLayoutSettings(s);
                    break;
                default:
                    DrawColorAndSyncSettings(s);
                    break;
            }
            CommitUnfocusedNumbers();
            s.Normalize();
        }

        private static void DrawLaneSettings(Settings s)
        {
            GUILayout.Label(Text("轨道数", "Lane count"));
            int laneIndex = System.Array.IndexOf(LaneCounts, s.laneCount);
            laneIndex = GUILayout.Toolbar(laneIndex, LaneChoices);
            s.laneCount = LaneCounts[laneIndex];
            Help(Text("1K 保留原单轨视图；4K / 6K / 8K 为读谱分配轨道。", "Keep the original 1K view or distribute upcoming notes across 4K, 6K or 8K."));

            GUILayout.Space(8f);
            GUILayout.Label(Text("分轨方式", "Split mode"));
            s.splitMode = GUILayout.Toolbar(s.splitMode, new[] { Text("内轮", "Inward roll"), Text("左右交替", "Alternate hands") });

            GUILayout.Space(8f);
            GUILayout.Label(Text("主手", "Primary hand"));
            s.rightHandPrimary = GUILayout.Toolbar(s.rightHandPrimary ? 1 : 0, new[] { Text("左手", "Left"), Text("右手", "Right") }) == 1;

            if (s.splitMode == 0)
            {
                s.singleKps = Slider(Text("单指速度", "Single-finger speed"), s.singleKps, 1f, 30f, "0.0", Text(" 次/秒", " KPS"));
                Help(Text("根据击打密度决定内轮分组。", "The speed threshold controls inward-roll grouping."));
                s.followPlaybackSpeed = GUILayout.Toggle(s.followPlaybackSpeed, Text(" 跟随播放倍速", " Follow playback speed"));
            }

            GUILayout.Space(8f);
            s.mergeNearbyNotes = GUILayout.Toggle(s.mergeNearbyNotes, Text(" 小角多押视觉对齐", " Align nearby notes visually"));
            Help(Text("将符合条件的密集小角音符画在同一时刻，不改变真实游戏的击打时刻或判定。", "Align qualifying nearby notes visually. Actual hit times and judgement stay unchanged."));
        }

        private static void DrawLayoutSettings(Settings s)
        {
            NumberSlider(nameof(s.pixelsPerSecond), Text("下落速度", "Scroll speed"),
                () => s.pixelsPerSecond, value => s.pixelsPerSecond = value, 10f, 10000f,
                Text("像素/秒", "px/s"), logarithmic: true);
            Help(Text("范围 10–10000，滑条按比例调整。数字支持小数；按回车、离开输入框或保存时应用，超出范围会自动限制，无效输入恢复原值。",
                "Range: 10–10000, with a logarithmic slider. Decimals are supported. Press Enter, leave the field or save to apply. Out-of-range values are clamped; invalid text restores the previous value."));
            NumberSlider(nameof(s.noteThickness), Text("音符厚度", "Note thickness"),
                () => s.noteThickness, value => s.noteThickness = value, 2f, 80f, Text("像素", "px"));
            NumberSlider(nameof(s.hitLineThickness), Text("判定线厚度", "Hit line thickness"),
                () => s.hitLineThickness, value => s.hitLineThickness = value, 1f, 20f, Text("像素", "px"));

            GUILayout.Space(8f);
            GUILayout.Label(Text("判定线位置模式", "Hit line position mode"));
            int positionMode = GUILayout.Toolbar(s.hitLinePositionMode,
                new[] { Text("距底部像素", "Pixels from bottom"), Text("距顶部百分比", "Percentage from top") });
            if (positionMode != s.hitLinePositionMode)
            {
                CommitNumberEdits();
                GUI.FocusControl(null);
                s.hitLinePositionMode = positionMode;
            }
            if (s.hitLinePositionMode == 1)
            {
                NumberSlider(nameof(s.hitLinePercent), Text("判定线距轨道区顶部", "Hit line from highway top"),
                    () => s.hitLinePercent * 100f, value => s.hitLinePercent = value / 100f, 0f, 100f, "%");
                Help(Text("0% 为轨道区顶部，100% 为底部；判定线会保持在可见范围内。",
                    "0% is the highway top; 100% is the bottom. The hit line stays within the visible area."));
            }
            else
            {
                NumberSlider(nameof(s.hitLineFromBottom), Text("判定线距轨道区底部", "Hit line from highway bottom"),
                    () => s.hitLineFromBottom, value => s.hitLineFromBottom = value, 0f, 4320f, Text("像素", "px"));
                Help(Text("保留原版位置设置。超出当前轨道区的距离会在绘制时限制到可见边缘。",
                    "Preserves the original position setting. Distances outside the current highway are drawn at its visible edge."));
            }

            GUILayout.Space(8f);
            s.laneWidth = Slider(Text("整个轨道区宽度", "Total highway width"), s.laneWidth, 80f, 1000f, "0", Text(" 像素", " px"));
            Help(Text("此宽度由所有轨道共同使用。", "This is the width of the entire lane area."));
            s.laneGap = Slider(Text("轨道间距", "Lane gap"), s.laneGap, 0f, 24f, "0.0", Text(" 像素", " px"));
            s.laneHeightFraction = Slider(Text("轨道区高度", "Highway height"), s.laneHeightFraction * 100f, 30f, 100f, "0", "%") / 100f;
            s.rightMargin = Slider(Text("右侧边距", "Right margin"), s.rightMargin, 0f, 1000f, "0", Text(" 像素", " px"));
            s.laneOpacity = Slider(Text("背景不透明度", "Background opacity"), s.laneOpacity * 100f, 0f, 100f, "0", "%") / 100f;

            GUILayout.Space(8f);
            s.showLaneDividers = GUILayout.Toggle(s.showLaneDividers, Text(" 显示轨道分隔线", " Show lane dividers"));
            s.showLaneLabels = GUILayout.Toggle(s.showLaneLabels, Text(" 显示轨道编号", " Show lane numbers"));
            s.showBeatLines = GUILayout.Toggle(s.showBeatLines, Text(" 显示节拍与小节线", " Show beat and measure lines"));
            if (s.showBeatLines)
            {
                s.beatsPerMeasure = Mathf.RoundToInt(Slider(Text("每小节拍数", "Beats per measure"), s.beatsPerMeasure, 1f, 8f));
                s.beatLineOpacity = Slider(Text("节拍线不透明度", "Beat line opacity"), s.beatLineOpacity * 100f, 0f, 100f, "0", "%") / 100f;
            }
        }

        private static void DrawColorAndSyncSettings(Settings s)
        {
            s.colorByHand = GUILayout.Toggle(s.colorByHand, Text(" 按左右手区分音符颜色", " Color notes by hand"));
            if (s.colorByHand)
            {
                ColorField(Text("左手", "Left hand"), ref s.leftNoteColorR, ref s.leftNoteColorG, ref s.leftNoteColorB);
            }
            ColorField(s.colorByHand ? Text("右手", "Right hand") : Text("普通音符", "Notes"), ref s.noteColorR, ref s.noteColorG, ref s.noteColorB);
            ColorField(Text("多押", "Multitap"), ref s.multitapColorR, ref s.multitapColorG, ref s.multitapColorB);

            GUILayout.Space(12f);
            GUILayout.Label($"{Text("游戏时钟校准", "Game clock correction")}: {s.autoOffsetMs:+0;-0;0}{Text(" 毫秒", " ms")}");
            Help(Text("使用游戏输入时钟，校准不会随玩家早按或晚按漂移。", "Uses the game's input clock, without learning player timing errors."));
            if (GUILayout.Button(Text("读取游戏校准", "Refresh correction"), GUILayout.Width(280f)))
            {
                s.autoOffsetMs = 0f;
                HighwayBehaviour.Instance?.ResetCalibration();
            }
            s.offsetMs = Slider(Text("手动偏移", "Manual nudge"), s.offsetMs, -300f, 300f, "+0;-0;0", Text(" 毫秒", " ms"));
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            CommitNumberEdits();
            Settings.Normalize();
            Settings.Save(modEntry);
        }

        private static void NumberSlider(string key, string label, System.Func<float> read,
            System.Action<float> write, float minimum, float maximum, string unit, bool logarithmic = false)
        {
            string name = "AdofaiHighway.Number." + key;
            if (!NumberEdits.TryGetValue(name, out var edit))
            {
                edit = new NumberEdit { Text = FormatNumber(read()) };
                NumberEdits.Add(name, edit);
            }
            edit.Read = read;
            edit.Write = write;
            edit.Minimum = minimum;
            edit.Maximum = maximum;
            if (!edit.Dirty && GUI.GetNameOfFocusedControl() != name)
                edit.Text = FormatNumber(read());

            bool enter = GUI.GetNameOfFocusedControl() == name && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(true));
            GUI.SetNextControlName(name);
            string input = GUILayout.TextField(edit.Text, GUILayout.Width(110f));
            if (input != edit.Text)
            {
                edit.Text = input;
                edit.Dirty = true;
            }
            if (Event.current.type == EventType.Repaint)
                edit.FieldRect = GUILayoutUtility.GetLastRect();
            GUILayout.Label(unit, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            if (enter)
            {
                CommitNumber(edit);
                GUI.FocusControl(null);
                Event.current.Use();
            }

            float current = Mathf.Clamp(read(), minimum, maximum);
            float position = logarithmic ? Mathf.Log10(current) : current;
            float next = GUILayout.HorizontalSlider(position,
                logarithmic ? Mathf.Log10(minimum) : minimum,
                logarithmic ? Mathf.Log10(maximum) : maximum);
            if (next != position)
            {
                write(Mathf.Clamp(logarithmic ? Mathf.Pow(10f, next) : next, minimum, maximum));
                edit.Text = FormatNumber(read());
                edit.Dirty = false;
            }
        }

        private static string FormatNumber(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static void CommitNumber(NumberEdit edit)
        {
            if (!edit.Dirty) return;
            if ((float.TryParse(edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.TryParse(edit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                && !float.IsNaN(value) && !float.IsInfinity(value))
                edit.Write(Mathf.Clamp(value, edit.Minimum, edit.Maximum));
            edit.Text = FormatNumber(edit.Read());
            edit.Dirty = false;
        }

        private static void CommitNumberEdits()
        {
            foreach (var edit in NumberEdits.Values) CommitNumber(edit);
        }

        private static void CommitUnfocusedNumbers()
        {
            string focused = GUI.GetNameOfFocusedControl();
            foreach (var pair in NumberEdits)
                if (pair.Key != focused) CommitNumber(pair.Value);
        }

        private static float Slider(string label, float value, float minimum, float maximum, string format = "0", string unit = "")
        {
            GUILayout.Label($"{label}: {value.ToString(format)}{unit}");
            return GUILayout.HorizontalSlider(value, minimum, maximum);
        }

        private static void Help(string text)
        {
            GUILayout.Label(text, new GUIStyle(GUI.skin.label) { wordWrap = true });
        }

        private static void EnsureSwatchTexture()
        {
            if (swatchTexture != null)
            {
                return;
            }
            swatchTexture = new Texture2D(1, 1);
            swatchTexture.SetPixel(0, 0, Color.white);
            swatchTexture.Apply();
        }

        private static void ColorField(string label, ref float r, ref float g, ref float b)
        {
            EnsureSwatchTexture();
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160f));
            GUILayout.Box(GUIContent.none, GUILayout.Width(60f), GUILayout.Height(16f));
            Rect swatch = GUILayoutUtility.GetLastRect();
            Color prev = GUI.color;
            GUI.color = new Color(r, g, b, 1f);
            GUI.DrawTexture(swatch, swatchTexture);
            GUI.color = prev;
            GUILayout.EndHorizontal();

            r = ChannelSlider(Text("红", "R"), r);
            g = ChannelSlider(Text("绿", "G"), g);
            b = ChannelSlider(Text("蓝", "B"), b);
        }

        private static float ChannelSlider(string name, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name} {Mathf.RoundToInt(value * 255f),3}", GUILayout.Width(48f));
            value = GUILayout.HorizontalSlider(value, 0f, 1f);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
