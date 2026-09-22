using System.Reflection;
using System.Xml.Serialization;
using AdofaiHighway;

internal static class Program
{
    private static readonly XmlSerializer Serializer = new(typeof(Settings));
    private static readonly FieldInfo[] SettingsFields = typeof(Settings).GetFields(
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    private static int assertions;

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        {
            ("Legacy 0.6 XML preserves every old field and initializes new defaults", LegacyXml),
            ("Every setting survives XML serialization and reload", RoundTripAllFields),
            ("XML NaN and both infinities normalize to finite defaults", NonFiniteXml),
            ("Only supported lanes, language and display modes survive", SupportedChoices),
            ("Finite settings are clamped at both boundaries", ClampBoundaries),
            ("Narrow multi-lane layouts retain usable column widths", NarrowLanes),
            ("An 80-pixel 8K layout limits the gap to exactly two pixels", MinimumEightKeyWidth),
            ("Normalization is idempotent after XML migration", Idempotence),
        };

        int failures = 0;
        foreach (var (name, test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed; {assertions} assertions.");
        return failures == 0 ? 0 : 1;
    }

    private static Settings LoadLegacy() => Deserialize(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Settings.0.6.0.xml")));

    private static void LegacyXml()
    {
        var actual = LoadLegacy();
        actual.Normalize();
        var expected = new Settings
        {
            pixelsPerSecond = 456f,
            laneWidth = 375f,
            laneHeightFraction = 0.75f,
            rightMargin = 36f,
            hitLineFromBottom = 128f,
            laneOpacity = 0.625f,
            noteColorR = 0.125f,
            noteColorG = 0.5f,
            noteColorB = 0.875f,
            multitapColorR = 0.25f,
            multitapColorG = 0.375f,
            multitapColorB = 0.75f,
            showBeatLines = false,
            beatsPerMeasure = 7,
            beatLineOpacity = 0.3125f,
            offsetMs = -123.5f,
            autoOffsetMs = 234.5f,
        };
        AssertFields(expected, actual);
        Equal(0, actual.language, "legacy language default");
        Equal(1, actual.laneCount, "legacy lane default");
        Equal(0, actual.splitMode, "legacy split mode default");
        Equal(7.3f, actual.singleKps, "legacy single KPS default");
        Equal(true, actual.rightHandPrimary, "legacy primary hand default");
        Equal(true, actual.followPlaybackSpeed, "legacy follow speed default");
        Equal(false, actual.mergeNearbyNotes, "legacy visual merge default");
        Equal(true, actual.showLaneLabels, "legacy lane label default");
        Equal(true, actual.showLaneDividers, "legacy divider default");
        Equal(true, actual.colorByHand, "legacy hand color default");
        Equal(4f, actual.laneGap, "legacy gap default");
        Equal(6f, actual.noteThickness, "legacy note thickness default");
        Equal(3f, actual.hitLineThickness, "legacy hit line thickness default");
        Equal(0, actual.hitLinePositionMode, "legacy hit line position mode default");
        Equal(0.85f, actual.hitLinePercent, "legacy hit line percentage default");
        Equal(0.95f, actual.leftNoteColorR, "legacy left red default");
        Equal(0.55f, actual.leftNoteColorG, "legacy left green default");
        Equal(0.65f, actual.leftNoteColorB, "legacy left blue default");
    }

    private static void RoundTripAllFields()
    {
        var original = LoadLegacy();
        original.language = 1;
        original.laneCount = 6;
        original.splitMode = 1;
        original.singleKps = 12.5f;
        original.rightHandPrimary = false;
        original.followPlaybackSpeed = false;
        original.mergeNearbyNotes = true;
        original.showLaneLabels = false;
        original.showLaneDividers = false;
        original.colorByHand = false;
        original.laneGap = 7.5f;
        original.noteThickness = 13.5f;
        original.hitLineThickness = 8.5f;
        original.hitLinePositionMode = 1;
        original.hitLinePercent = 0.625f;
        original.pixelsPerSecond = 2345f;
        original.hitLineFromBottom = 2000f;
        original.leftNoteColorR = 0.0625f;
        original.leftNoteColorG = 0.4375f;
        original.leftNoteColorB = 0.8125f;
        original.Normalize();

        // Serialize to a stream, rewind, and deserialize with the actual .NET XML serializer.
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        stream.Position = 0;
        var reloaded = (Settings)Serializer.Deserialize(stream)!;
        reloaded.Normalize();
        AssertFields(original, reloaded);
    }

    private static void NonFiniteXml()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var corrupted = new Settings();
            foreach (var field in SettingsFields.Where(field => field.FieldType == typeof(float)))
                field.SetValue(corrupted, invalid);

            // Exercise the XML forms NaN, INF and -INF as well as Normalize itself.
            var loaded = Deserialize(Serialize(corrupted));
            loaded.Normalize();
            var defaults = new Settings();
            defaults.Normalize();
            AssertFields(defaults, loaded);
            foreach (var field in SettingsFields.Where(field => field.FieldType == typeof(float)))
                Check(float.IsFinite((float)field.GetValue(loaded)!), $"{field.Name} remained non-finite");
        }
    }

    private static void SupportedChoices()
    {
        foreach (int valid in new[] { 1, 4, 6, 8 })
        {
            var settings = new Settings { laneCount = valid };
            settings.Normalize();
            Equal(valid, settings.laneCount, "supported lane count");
        }
        foreach (int invalid in new[] { int.MinValue, -1, 0, 2, 3, 5, 7, 16, int.MaxValue })
        {
            var settings = new Settings { laneCount = invalid };
            settings.Normalize();
            Equal(1, settings.laneCount, "invalid lane count resets to 1K");
        }
        foreach (int mode in new[] { int.MinValue, -1, 0, 1, 2, int.MaxValue })
        {
            var settings = new Settings
            {
                splitMode = mode,
                language = mode,
                hitLinePositionMode = mode,
            };
            settings.Normalize();
            Equal(mode == 1 ? 1 : 0, settings.splitMode, "split mode range");
            Equal(mode == 1 ? 1 : 0, settings.language, "language range");
            Equal(mode == 1 ? 1 : 0, settings.hitLinePositionMode, "hit line position mode range");
        }
    }

    private static void ClampBoundaries()
    {
        (string Name, float Minimum, float Maximum)[] ranges =
        {
            (nameof(Settings.singleKps), 1f, 30f),
            (nameof(Settings.pixelsPerSecond), 10f, 10000f),
            (nameof(Settings.laneWidth), 80f, 1000f),
            (nameof(Settings.laneHeightFraction), 0.3f, 1f),
            (nameof(Settings.rightMargin), 0f, 1000f),
            (nameof(Settings.hitLineFromBottom), 0f, 4320f),
            (nameof(Settings.noteThickness), 2f, 80f),
            (nameof(Settings.hitLineThickness), 1f, 20f),
            (nameof(Settings.hitLinePercent), 0f, 1f),
            (nameof(Settings.laneGap), 0f, 24f),
            (nameof(Settings.laneOpacity), 0f, 1f),
            (nameof(Settings.noteColorR), 0f, 1f),
            (nameof(Settings.noteColorG), 0f, 1f),
            (nameof(Settings.noteColorB), 0f, 1f),
            (nameof(Settings.leftNoteColorR), 0f, 1f),
            (nameof(Settings.leftNoteColorG), 0f, 1f),
            (nameof(Settings.leftNoteColorB), 0f, 1f),
            (nameof(Settings.multitapColorR), 0f, 1f),
            (nameof(Settings.multitapColorG), 0f, 1f),
            (nameof(Settings.multitapColorB), 0f, 1f),
            (nameof(Settings.beatLineOpacity), 0f, 1f),
            (nameof(Settings.offsetMs), -300f, 300f),
            (nameof(Settings.autoOffsetMs), -60000f, 60000f),
        };
        foreach (var (name, minimum, maximum) in ranges)
        {
            var field = typeof(Settings).GetField(name)!;
            foreach (var (input, expected) in new[]
            {
                (minimum - 1f, minimum),
                (minimum, minimum),
                ((minimum + maximum) / 2f, (minimum + maximum) / 2f),
                (maximum, maximum),
                (maximum + 1f, maximum),
            })
            {
                var settings = new Settings();
                field.SetValue(settings, input);
                settings.Normalize();
                Equal(expected, (float)field.GetValue(settings)!, $"{name} boundary");
            }
        }
        foreach (var (input, expected) in new[] { (int.MinValue, 1), (0, 1), (9, 8), (int.MaxValue, 8) })
        {
            var settings = new Settings { beatsPerMeasure = input };
            settings.Normalize();
            Equal(expected, settings.beatsPerMeasure, "beats per measure range");
        }
    }

    private static void NarrowLanes()
    {
        foreach (int laneCount in new[] { 4, 6, 8 })
        foreach (float width in new[] { 80f, 100f, 160f, 1000f })
        {
            var settings = new Settings { laneCount = laneCount, laneWidth = width, laneGap = 24f };
            settings.Normalize();
            float cellWidth = settings.laneWidth / laneCount;
            float columnWidth = cellWidth - Math.Min(settings.laneGap, cellWidth * 0.4f);
            Check(columnWidth >= 8f - 0.0001f, $"{laneCount}K at width {width}: columns became too narrow");
            Check(settings.laneGap >= 0f && float.IsFinite(settings.laneGap), "invalid gap after normalization");
        }
    }

    private static void MinimumEightKeyWidth()
    {
        foreach (float gap in new[] { 24f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var settings = new Settings { laneCount = 8, laneWidth = 80f, laneGap = gap };
            settings.Normalize();
            Equal(2f, settings.laneGap, "8K at minimum width must limit the gap to two pixels");
            float cellWidth = settings.laneWidth / settings.laneCount;
            Equal(8f, cellWidth - Math.Min(settings.laneGap, cellWidth * 0.4f),
                "8K at minimum width must retain an eight-pixel note");
        }

        var singleLane = new Settings { laneCount = 1, laneWidth = 80f, laneGap = 24f };
        singleLane.Normalize();
        Equal(24f, singleLane.laneGap, "1K retains its independent 24-pixel gap limit");
    }

    private static void Idempotence()
    {
        var migrated = LoadLegacy();
        migrated.laneCount = 8;
        migrated.laneWidth = 80f;
        migrated.laneGap = 24f;
        migrated.Normalize();
        string once = Serialize(migrated);
        migrated.Normalize();
        Equal(once, Serialize(migrated), "normalization changed valid settings on a second call");
    }

    private static string Serialize(Settings settings)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Serializer.Serialize(writer, settings);
        return writer.ToString();
    }

    private static Settings Deserialize(string xml)
    {
        using var reader = new StringReader(xml);
        return (Settings)Serializer.Deserialize(reader)!;
    }

    private static void AssertFields(Settings expected, Settings actual)
    {
        foreach (var field in SettingsFields)
            Equal(field.GetValue(expected), field.GetValue(actual), field.Name);
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected {expected}, got {actual}");
    }

    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
