using AdofaiHighway;

// Dependency-free algorithm tests. This executable does not reference Unity,
// the game, a keyboard library, or the mod's runtime entry point.
var cases = new (string Name, Action Run)[]
{
    ("4/6/8 lanes handle real chords directly", ChordsUseEveryLane),
    ("inner roll keeps the last note in its group", LastNoteInGroup),
    ("dense groups change hands; sparse passages reset", HandTransitions),
    ("hold lanes remain occupied until release", HoldOccupation),
    ("consecutive slow holds alternate first hand lanes", SlowHolds),
    ("touching holds prefer another available key", HoldRestart),
    ("multitap overflow preserves unmet requirements", MultitapOverflow),
    ("simultaneous source events use different lanes", SimultaneousSources),
    ("playback pitch changes density only when enabled", PlaybackDensity),
    ("visual merging preserves hit and hold timing", MergeKeepsTiming),
    ("visual groups cannot enter an earlier hold", MergeRespectsOldHold),
    ("real simultaneous groups remain atomic at merge boundaries", AtomicVisualGroups),
    ("overlong visual candidates are rejected as a whole", LongCandidate),
    ("merge angle and gap thresholds", MergeThresholds),
    ("visual capacity overflow stays explicit", VisualOverflow),
    ("alternating mode uses both hands deterministically", Alternating),
    ("invalid parameters and nonfinite data are normalized", InvalidInput),
    ("stable sorting retains source indices and input", StableInput),
    ("generated scenarios conserve notes and avoid hold conflicts", PropertyScenarios)
};
int failures = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} tests passed.");
return failures == 0 ? 0 : 1;

static RhythmEvent Tap(double time, double angle = 10, double bpm = 120, int keys = 1)
    => new RhythmEvent(time, time, angle, bpm, keys);
static LaneOptions Options(int lanes = 8, bool merge = false)
    => new LaneOptions { LaneCount = lanes, MergeNearby = merge };
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Equal<T>(IEnumerable<T> actual, params T[] expected)
{
    Require(actual.SequenceEqual(expected), $"Expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
}
static void ChordsUseEveryLane()
{
    foreach (int count in new[] { 1, 4, 6, 8 })
    {
        var notes = LaneAllocator.Assign(new[] { Tap(0, keys: count) }, Options(count));
        Require(notes.Length == count && notes.All(n => n.OverflowCount == 0), "Chord lost a key");
        Equal(notes.Select(n => n.Lane).OrderBy(n => n), Enumerable.Range(0, count).ToArray());
        Require(notes.All(n => n.GroupSize == count), "Incorrect chord size");
    }
}
static void LastNoteInGroup()
{
    var notes = LaneAllocator.Assign(new[] { Tap(0), Tap(.02), Tap(.04), Tap(.06) }, Options());
    Equal(notes.Select(n => n.Lane), 7, 6, 5, 4);
    Equal(notes.Select(n => n.SourceIndex), 0, 1, 2, 3);
}
static void HandTransitions()
{
    var notes = LaneAllocator.Assign(new[] { Tap(0), Tap(.02), Tap(.04), Tap(.06), Tap(.08), Tap(1) }, Options());
    Equal(notes.Select(n => n.Lane), 7, 6, 5, 4, 3, 4);
    var left = Options(); left.MainHandRight = false;
    Equal(LaneAllocator.Assign(new[] { Tap(0), Tap(.02), Tap(.04), Tap(.06) }, left).Select(n => n.Lane), 0, 1, 2, 3);
}
static void HoldOccupation()
{
    var events = new[] { new RhythmEvent(0, 1, 180, 120), Tap(.5), Tap(1) };
    var notes = LaneAllocator.Assign(events, Options(4));
    Equal(notes.Select(n => n.Lane), 2, 3, 2);
    Require(notes[0].ReleaseTime == 1, "Hold was shortened");
    var blocked = LaneAllocator.Assign(new[] { new RhythmEvent(0, 1, 180, 120, 4), Tap(.5), Tap(1) }, Options(4));
    Require(blocked.Single(n => n.SourceIndex == 1).Lane == -1, "Assigned on top of a hold");
    Require(blocked.Single(n => n.SourceIndex == 1).OverflowCount == 1, "Missing blocked-note warning");
    Require(blocked.Single(n => n.SourceIndex == 2).Lane >= 0, "Release did not free lanes");
}
static void SlowHolds()
{
    foreach (int lanes in new[] { 4, 6, 8 })
    {
        var options = Options(lanes);
        var events = new[] { new RhythmEvent(0, .1, 180, 120),
            new RhythmEvent(1, 1.1, 180, 120), new RhythmEvent(2, 2.1, 180, 120) };
        Equal(LaneAllocator.Assign(events, options).Select(n => n.Lane), lanes / 2, lanes / 2 - 1, lanes / 2);
        options.MainHandRight = false;
        Equal(LaneAllocator.Assign(events, options).Select(n => n.Lane), lanes / 2 - 1, lanes / 2, lanes / 2 - 1);
    }
    var settings = Options();
    double boundary = 1 / settings.SingleKps;
    var atBoundary = new[] { new RhythmEvent(0, .01, 180, 120), new RhythmEvent(boundary, boundary + .01, 180, 120) };
    Equal(LaneAllocator.Assign(atBoundary, settings).Select(n => n.Lane), 4, 4);
    var speedSensitive = new[] { new RhythmEvent(0, .1, 180, 120), new RhythmEvent(.3, .4, 180, 120) };
    Equal(LaneAllocator.Assign(speedSensitive, settings).Select(n => n.Lane), 4, 3);
    Equal(LaneAllocator.Assign(speedSensitive, settings, 4).Select(n => n.Lane), 4, 4);
    settings.FollowPlaybackSpeed = false;
    Equal(LaneAllocator.Assign(speedSensitive, settings, 4).Select(n => n.Lane), 4, 3);
}
static void HoldRestart()
{
    // The intervening tap excludes this from the slow-hold rule. The last
    // inner-roll preference is the first hold's lane, which has just released.
    var notes = LaneAllocator.Assign(new[] { new RhythmEvent(0, .1, 180, 120),
        Tap(.08), new RhythmEvent(.1, .3, 180, 120) }, Options(4));
    Require(notes[0].Lane != notes[2].Lane && notes[2].Lane >= 0, "Restarted hold on the old key despite an alternative");
    Require(notes[0].ReleaseTime == .1 && notes[2].Time == .1, "Moved hold boundary");
    var oneLane = LaneAllocator.Assign(new[] { new RhythmEvent(0, .1, 180, 120),
        new RhythmEvent(.1, .3, 180, 120) }, Options(1));
    Equal(oneLane.Select(n => n.Lane), 0, 0);
}
static void MultitapOverflow()
{
    var notes = LaneAllocator.Assign(new[] { Tap(0, keys: 7) }, Options(4));
    Require(notes.Count(n => n.Lane >= 0) == 4, "Wrong number of allocated keys");
    Require(notes.Single(n => n.Lane == -1).OverflowCount == 3, "Overflow count not conserved");
    Require(notes.All(n => n.GroupSize == 7), "Group size lost overflow");
    var huge = LaneAllocator.Assign(new[] { Tap(0, keys: int.MaxValue) }, Options(4));
    Require(huge.Length == 5 && huge.Last().OverflowCount == int.MaxValue - 4, "Huge demand not bounded");
}
static void SimultaneousSources()
{
    var notes = LaneAllocator.Assign(new[] { Tap(0, 180), Tap(0, 999), Tap(0, double.NaN) }, Options(4));
    Require(notes.Select(n => n.Lane).Distinct().Count() == 3, "Real simultaneous sources collided");
    Require(notes.All(n => n.GroupSize == 3), "Real simultaneous chord size wrong");
}
static void PlaybackDensity()
{
    var events = new[] { Tap(0), Tap(.1), Tap(.2) };
    var options = Options();
    Equal(LaneAllocator.Assign(events, options).Select(n => n.Lane), 4, 4, 4);
    var faster = LaneAllocator.Assign(events, options, 4);
    Equal(faster.Select(n => n.Lane), 6, 5, 4);
    Equal(faster.Select(n => n.Time), 0, .1, .2);
    options.FollowPlaybackSpeed = false;
    Equal(LaneAllocator.Assign(events, options, 4).Select(n => n.Lane), 4, 4, 4);
}
static void MergeKeepsTiming()
{
    var events = new[] { Tap(0), new RhythmEvent(.04, .4, 10, 120), Tap(.08) };
    var notes = LaneAllocator.Assign(events, Options(4, true));
    Equal(notes.Select(n => n.DisplayTime), 0, 0, 0);
    Equal(notes.Select(n => n.Time), 0, .04, .08);
    Equal(notes.Select(n => n.ReleaseTime), 0, .4, .08);
    Require(notes.Select(n => n.Lane).Distinct().Count() == 3, "Merged notes visually collide");
    Require(notes.All(n => n.GroupSize == 3), "Merged group size wrong");
}
static void MergeRespectsOldHold()
{
    var notes = LaneAllocator.Assign(new[] { new RhythmEvent(-1, .025, 180, 120), Tap(0), Tap(.04) }, Options(4, true));
    Require(notes[2].Time == .04 && notes[2].DisplayTime == 0, "Wrong merged timing");
    Require(notes[2].Lane >= 0 && notes[2].Lane != notes[0].Lane, "Merged head visually overlaps an earlier hold");
    var full = LaneAllocator.Assign(new[] { new RhythmEvent(-1, .025, 180, 120, 4), Tap(0), Tap(.04) }, Options(4, true));
    Require(full.Where(n => n.SourceIndex > 0).All(n => n.Lane == -1), "Used a hold lane before its visual release");
    Require(full.Sum(n => n.OverflowCount) == 2, "Visual hold capacity was not reported");
}
static void AtomicVisualGroups()
{
    var notes = LaneAllocator.Assign(new[] { Tap(0), Tap(.04, 999), Tap(.04, 10), Tap(.08, 10), Tap(1) }, Options(8, true));
    Equal(notes.Select(n => n.DisplayTime), 0, .04, .04, .04, 1);
    Equal(notes.Select(n => n.GroupSize), 1, 3, 3, 3, 1);
    Require(notes.Skip(1).Take(3).Select(n => n.Lane).Distinct().Count() == 3, "Simultaneous sources split across visual groups");
    var longGroup = LaneAllocator.Assign(new[] { Tap(0), Tap(.04), Tap(.04, 999), Tap(.08), Tap(.12) }, Options(8, true));
    Equal(longGroup.Select(n => n.DisplayTime), 0, .04, .04, .08, .12);
    Equal(longGroup.Select(n => n.GroupSize), 1, 2, 2, 1, 1);
    Require(longGroup[1].Lane != longGroup[2].Lane, "Rejected candidate lost its real chord");
}
static void LongCandidate()
{
    var events = new[] { Tap(0), Tap(.04), Tap(.08), Tap(.12) };
    var notes = LaneAllocator.Assign(events, Options(4, true));
    Equal(notes.Select(n => n.DisplayTime), 0, .04, .08, .12);
    Require(notes.All(n => n.GroupSize == 1), "Overlong candidate was subdivided");
}
static void MergeThresholds()
{
    Require(LaneAllocator.Assign(new[] { Tap(0), Tap(.04, 30, 300) }, Options(4, true))[1].DisplayTime == 0, "300 BPM threshold");
    Require(LaneAllocator.Assign(new[] { Tap(0), Tap(.04, 30, 299) }, Options(4, true))[1].DisplayTime == .04, "Low BPM threshold");
    foreach (double angle in new[] { 330d, 999d, double.NaN, -10d })
        Require(LaneAllocator.Assign(new[] { Tap(0), Tap(.04, angle) }, Options(4, true))[1].DisplayTime == .04, "Invalid angle merged");
    Require(LaneAllocator.Assign(new[] { Tap(0), Tap(.05) }, Options(4, true))[1].DisplayTime == .05, "50ms is a strict boundary");
}
static void VisualOverflow()
{
    var events = new[] { Tap(0), Tap(.02), Tap(.04), Tap(.06), Tap(.08) };
    var notes = LaneAllocator.Assign(events, Options(4, true));
    Require(notes.Count(n => n.Lane >= 0) == 4 && notes.Sum(n => n.OverflowCount) == 1, "Merged capacity not reported");
    Require(notes.All(n => n.DisplayTime == 0 && n.GroupSize == 5), "Overflow lost visual group");
    Equal(notes.Select(n => n.Time), 0, .02, .04, .06, .08);
}
static void Alternating()
{
    var options = Options(6); options.Mode = 1;
    var notes = LaneAllocator.Assign(Enumerable.Range(0, 6).Select(i => Tap(i)).ToArray(), options);
    Equal(notes.Select(n => n.Lane), 3, 2, 4, 1, 5, 0);
}
static void InvalidInput()
{
    Require(LaneAllocator.Assign(null, null).Length == 0, "Null events");
    var options = new LaneOptions { LaneCount = -4, SingleKps = double.NaN, Mode = -1 };
    var events = new[] { new RhythmEvent(double.NaN, double.PositiveInfinity, double.NaN, -1, -3) };
    var notes = LaneAllocator.Assign(events, options, double.NegativeInfinity);
    Require(notes.Length == 1 && notes[0].Lane == 0 && notes[0].Time == 0 && notes[0].ReleaseTime == 0, "Invalid data not normalized");
    Require(options.LaneCount == -4 && double.IsNaN(options.SingleKps), "Options mutated");
    var negative = LaneAllocator.Assign(new[] { new RhythmEvent(-1, -2, 10, 120) }, Options());
    Require(negative[0].Time == -1 && negative[0].ReleaseTime == -1, "Valid negative pre-roll lost");
}
static void StableInput()
{
    var events = new[] { Tap(2), Tap(0), Tap(1), Tap(1) };
    var notes = LaneAllocator.Assign(events, Options());
    Equal(notes.Select(n => n.SourceIndex), 1, 2, 3, 0);
    Equal(events.Select(n => n.Time), 2, 0, 1, 1);
    Equal(notes.Select(n => n.Lane), LaneAllocator.Assign(events, Options()).Select(n => n.Lane).ToArray());
}
static void PropertyScenarios()
{
    var random = new Random(613);
    foreach (int laneCount in new[] { 1, 4, 6, 8 })
    foreach (bool merge in new[] { false, true })
    {
        var events = new List<RhythmEvent>();
        double time = 0;
        for (int i = 0; i < 200; i++)
        {
            time += random.Next(0, 5) * .02;
            double release = random.Next(5) == 0 ? time + .3 : time;
            events.Add(new RhythmEvent(time, release, random.Next(2) == 0 ? 10 : 180, 120, random.Next(1, 4)));
        }
        var notes = LaneAllocator.Assign(events, Options(laneCount, merge), 1.5);
        for (int i = 0; i < events.Count; i++)
        {
            var fromSource = notes.Where(n => n.SourceIndex == i).ToArray();
            Require(fromSource.Count(n => n.Lane >= 0) + fromSource.Sum(n => n.OverflowCount) == events[i].RequiredKeys, "Lost requirement");
            Require(fromSource.All(n => n.Time == events[i].Time && n.ReleaseTime == events[i].ReleaseTime), "Changed timing");
        }
        foreach (var lane in notes.Where(n => n.Lane >= 0).GroupBy(n => n.Lane))
        {
            var ordered = lane.OrderBy(n => n.Time).ToArray();
            for (int i = 1; i < ordered.Length; i++)
            {
                Require(ordered[i].Time > ordered[i - 1].Time && ordered[i].Time >= ordered[i - 1].ReleaseTime,
                    "Lane reused at the same instant or during a hold");
                Require(ordered[i].DisplayTime >= ordered[i - 1].ReleaseTime, "Display head overlaps a preceding hold");
            }
        }
    }
}
