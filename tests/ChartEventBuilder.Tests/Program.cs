using AdofaiHighway;

// These snapshots represent input requirements observed in the game's floor and
// player logic. Production files are linked, not copied; no game DLL is needed.
var cases = new (string Name, Action Run)[]
{
    ("empty charts and the starting tile produce no input", StartingTile),
    ("fake, autoplay and freeroam tiles are omitted", NonManualTiles),
    ("midspin chains produce one manual input at the shared time", Midspin),
    ("a normal hold includes its release without a second press", NormalHold),
    ("consecutive holds retain their new press", ConsecutiveHolds),
    ("a hold release crosses zero-time midspin tiles", HoldAcrossMidspin),
    ("a hold release supplies one of a multitap's inputs", HoldIntoMultitap),
    ("an automatic hold supplies its endpoint but preserves the following input", AutoHold),
    ("incoming travel and effective BPM come from the preceding tile", IncomingTravel),
    ("automatic passages never compress absolute song time", AbsoluteTiming),
    ("a multitap hold retains every held input in 4K, 6K and 8K", MultitapHold),
    ("invalid timestamps cannot produce drawable events", InvalidTimestamps),
};

int failed = 0;
foreach (var test in cases)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} chart-event cases passed.");
return failed == 0 ? 0 : 1;

static void StartingTile()
{
    Equal(0, ChartEventBuilder.Build(null).Length, "null chart");
    Equal(0, Build().Length, "empty chart");
    Equal(0, Build(new FloorTiming(0)).Length, "only starting tile");
    var events = Build(new FloorTiming(0), new FloorTiming(1), new FloorTiming(2));
    Times(events, 1, 2);
    Require(double.IsNaN(events[0].TravelDegrees), "first input has no prior manual input");
}

static void NonManualTiles()
{
    var events = Build(new FloorTiming(0), new FloorTiming(1, fake: true),
        new FloorTiming(2, auto: true), new FloorTiming(3, freeRoam: true),
        new FloorTiming(4), new FloorTiming(5));
    Times(events, 4, 5);
    Equal(1, events[0].RequiredKeys, "first manual input count");
}

static void Midspin()
{
    var events = Build(new FloorTiming(0), new FloorTiming(1),
        new FloorTiming(1.5, travelDegrees: 0, midSpin: true),
        new FloorTiming(1.5, travelDegrees: 0, midSpin: true),
        new FloorTiming(1.5), new FloorTiming(2));
    Times(events, 1, 1.5, 2);
    Equal(1, events[1].RequiredKeys, "midspin transition does not add a chord key");
    Require(events.All(e => e.ReleaseTime == e.Time), "midspin does not introduce holds");
}

static void NormalHold()
{
    var events = Build(new FloorTiming(0), new FloorTiming(1, hold: true),
        new FloorTiming(3), new FloorTiming(3.5));
    Times(events, 1, 3.5);
    Near(3, events[0].ReleaseTime, "hold release is the next tile's arrival");
    Near(3.5, events[1].ReleaseTime, "following tap is not extended");
    foreach (int keys in new[] { 4, 6, 8 })
    {
        var notes = Assign(events, keys);
        Equal(2, notes.Length, $"{keys}K note count");
        Near(1, notes[0].Time, $"{keys}K hold head");
        Near(3, notes[0].ReleaseTime, $"{keys}K hold tail");
        Require(notes.All(n => n.Time != 3), $"{keys}K must not ask for another press on release");
    }
}

static void ConsecutiveHolds()
{
    var events = Build(new FloorTiming(0), new FloorTiming(1, hold: true),
        new FloorTiming(2, hold: true), new FloorTiming(4), new FloorTiming(5));
    Times(events, 1, 2, 5);
    Near(2, events[0].ReleaseTime, "first hold boundary");
    Near(4, events[1].ReleaseTime, "second hold boundary");
    Equal(1, events[1].RequiredKeys, "second hold retains its press");
}

static void HoldAcrossMidspin()
{
    var events = Build(new FloorTiming(0), new FloorTiming(1, hold: true),
        new FloorTiming(4, travelDegrees: 0, midSpin: true),
        new FloorTiming(4, travelDegrees: 0, midSpin: true),
        new FloorTiming(4), new FloorTiming(5));
    Times(events, 1, 5);
    Near(4, events[0].ReleaseTime, "hold release across a midspin chain");

    var consecutive = Build(new FloorTiming(0), new FloorTiming(1, hold: true),
        new FloorTiming(4, travelDegrees: 0, midSpin: true),
        new FloorTiming(4, hold: true), new FloorTiming(6));
    Times(consecutive, 1, 4);
    Near(4, consecutive[0].ReleaseTime, "first chained hold tail");
    Near(6, consecutive[1].ReleaseTime, "second chained hold tail");
}

static void HoldIntoMultitap()
{
    foreach (int required in new[] { 1, 2, 3, 4 })
    {
        var events = Build(new FloorTiming(0), new FloorTiming(1, hold: true),
            new FloorTiming(3, requiredKeys: required), new FloorTiming(4));
        Near(3, events[0].ReleaseTime, "release keeps its original time");
        if (required == 1)
        {
            Times(events, 1, 4);
        }
        else
        {
            Times(events, 1, 3, 4);
            Equal(required - 1, events[1].RequiredKeys, "release consumes exactly one requirement");
            Near(3, events[1].ReleaseTime, "remaining requirements are taps");
        }
    }
}

static void AutoHold()
{
    // OttoHoldHit runs while the current tile is an automatic hold even when
    // its successor is manual. Repeated automatic Hits also finish multitaps.
    foreach (int required in new[] { 1, 2, 6 })
    {
        var events = Build(new FloorTiming(0), new FloorTiming(1, auto: true, hold: true),
            new FloorTiming(3, requiredKeys: required), new FloorTiming(4));
        Times(events, 4);
        Equal(1, events[0].RequiredKeys, "only the direct hold endpoint is automatic");
    }

    var throughMidspin = Build(new FloorTiming(0), new FloorTiming(1, auto: true, hold: true),
        new FloorTiming(3, travelDegrees: 0, midSpin: true), new FloorTiming(3),
        new FloorTiming(4));
    Times(throughMidspin, 4);
}

static void IncomingTravel()
{
    var events = Build(new FloorTiming(0, travelDegrees: 270, bpm: 90),
        new FloorTiming(1, travelDegrees: 15, bpm: 240),
        new FloorTiming(1.0104166666666667, travelDegrees: 300, bpm: 60));
    Near(15, events[1].TravelDegrees, "outgoing geometry of the previous floor");
    Near(240, events[1].Bpm, "effective BPM of the previous floor");

    var throughMidspin = Build(new FloorTiming(0),
        new FloorTiming(1, travelDegrees: 30, bpm: 360),
        new FloorTiming(1.0277777777777777, travelDegrees: 0, bpm: 1, midSpin: true),
        new FloorTiming(1.0277777777777777, travelDegrees: 180, bpm: 120));
    Near(30, throughMidspin[1].TravelDegrees, "midspin does not erase incoming geometry");
    Near(360, throughMidspin[1].Bpm, "midspin does not replace incoming BPM");
}

static void AbsoluteTiming()
{
    var events = Build(new FloorTiming(0), new FloorTiming(-0.25),
        new FloorTiming(0.5, auto: true), new FloorTiming(7, auto: true),
        new FloorTiming(9.875, travelDegrees: 30, bpm: 300),
        new FloorTiming(9.9));
    Times(events, -0.25, 9.875, 9.9);
    var notes = Assign(events, 6);
    Equal(3, notes.Length, "lane assignment preserves actual input count");
    for (int i = 0; i < events.Length; i++)
    {
        Near(events[i].Time, notes[i].Time, "lane timing after automatic passage");
        Near(events[i].Time, notes[i].DisplayTime, "unmerged display timing");
        Equal(i, notes[i].SourceIndex, "source event association");
    }
}

static void MultitapHold()
{
    var events = Build(new FloorTiming(0), new FloorTiming(1, hold: true, requiredKeys: 3),
        new FloorTiming(4), new FloorTiming(5));
    Times(events, 1, 5);
    Equal(3, events[0].RequiredKeys, "all hold-head inputs retained");
    foreach (int keys in new[] { 4, 6, 8 })
    {
        var notes = Assign(events, keys);
        var hold = notes.Where(n => n.SourceIndex == 0).ToArray();
        Equal(3, hold.Length, $"{keys}K hold demand");
        Equal(3, hold.Select(n => n.Lane).Distinct().Count(), $"{keys}K distinct held lanes");
        Require(hold.All(n => n.Lane >= 0 && n.OverflowCount == 0), $"{keys}K hold fits");
        Require(hold.All(n => n.Time == 1 && n.ReleaseTime == 4), $"{keys}K all held keys reach release");
    }
}

static void InvalidTimestamps()
{
    var events = Build(new FloorTiming(0), new FloorTiming(double.NaN),
        new FloorTiming(double.PositiveInfinity), new FloorTiming(2),
        new FloorTiming(3, hold: true));
    Times(events, 2, 3);
    Near(3, events[1].ReleaseTime, "terminal hold has no fabricated release");
}

static RhythmEvent[] Build(params FloorTiming[] floors) => ChartEventBuilder.Build(floors);
static LaneNote[] Assign(RhythmEvent[] events, int count) => LaneAllocator.Assign(events,
    new LaneOptions { LaneCount = count, MergeNearby = false });

static void Times(RhythmEvent[] actual, params double[] expected)
{
    Equal(expected.Length, actual.Length, "input event count");
    for (int i = 0; i < expected.Length; i++) Near(expected[i], actual[i].Time, $"event {i} time");
}
static void Equal(int expected, int actual, string because)
{
    if (expected != actual) throw new Exception($"{because}: expected {expected}, got {actual}");
}
static void Near(double expected, double actual, string because)
{
    if (double.IsNaN(actual) || Math.Abs(expected - actual) > 1e-9)
        throw new Exception($"{because}: expected {expected:R}, got {actual:R}");
}
static void Require(bool condition, string because)
{
    if (!condition) throw new Exception(because);
}
