using System;
using System.Collections.Generic;

namespace AdofaiHighway
{
    // All timestamps are song-clock seconds. A tap has ReleaseTime == Time.
    internal readonly struct RhythmEvent
    {
        public double Time { get; }
        public double ReleaseTime { get; }
        public double TravelDegrees { get; }
        public double Bpm { get; }
        public int RequiredKeys { get; }

        public RhythmEvent(double time, double releaseTime, double travelDegrees,
            double bpm, int requiredKeys = 1)
        {
            Time = time;
            ReleaseTime = releaseTime;
            TravelDegrees = travelDegrees;
            Bpm = bpm;
            RequiredKeys = requiredKeys;
        }
    }

    internal sealed class LaneOptions
    {
        public int LaneCount = 1;
        public int Mode = 0;
        public double SingleKps = 7.3;
        public bool MainHandRight = true;
        public bool FollowPlaybackSpeed = true;
        public bool MergeNearby = false;
    }

    internal readonly struct LaneNote
    {
        public int SourceIndex { get; }
        public int Lane { get; }
        public int GroupSize { get; }
        public int OverflowCount { get; }
        public double Time { get; }
        public double ReleaseTime { get; }
        public double DisplayTime { get; }

        public LaneNote(int sourceIndex, int lane, double time, double releaseTime,
            double displayTime, int groupSize = 1, int overflowCount = 0)
        {
            SourceIndex = sourceIndex;
            Lane = lane;
            Time = time;
            ReleaseTime = releaseTime;
            DisplayTime = displayTime;
            GroupSize = groupSize;
            OverflowCount = overflowCount;
        }
    }

    /// <summary>
    /// Allocates real input requirements directly to 1/4/6/8 lanes. It neither
    /// sends input nor changes hit timing. Lane == -1 is an explicit unassigned
    /// requirement marker; OverflowCount says how many keys could not fit.
    /// It must be rendered as a warning, not projected onto an occupied lane.
    /// </summary>
    internal static class LaneAllocator
    {
        private sealed class Item
        {
            internal int Source, Keys, VisualGroup, GroupSize;
            internal double Time, Release, Angle, Bpm, Display;
            internal bool SlowHold, SlowHoldRight;
        }

        public static LaneNote[] Assign(IReadOnlyList<RhythmEvent> events,
            LaneOptions options, double playbackSpeed = 1)
        {
            if (events == null || events.Count == 0)
                return new LaneNote[0];
            options = options ?? new LaneOptions();
            int lanes = ValidLaneCount(options.LaneCount);
            int half = Math.Max(1, lanes / 2);
            double kps = Positive(options.SingleKps, 7.3);
            double speed = options.FollowPlaybackSpeed ? Positive(playbackSpeed, 1) : 1;
            // Bound pathological values without modifying the caller's options.
            kps = Math.Max(0.01, Math.Min(10000, kps));
            speed = Math.Max(0.01, Math.Min(100, speed));
            double groupWindow = speed / (2 * kps);
            var items = Normalize(events);
            SetVisualGroups(items, options.MergeNearby);
            if (options.Mode != 1)
                SetSlowHoldHands(items, speed / kps, options.MainHandRight);

            var output = new List<LaneNote>();
            var occupiedUntil = new double[lanes];
            var atThisTime = new bool[lanes];
            var inVisualGroup = new bool[lanes];
            var lastWasHold = new bool[lanes];
            for (int lane = 0; lane < lanes; lane++)
                occupiedUntil[lane] = double.NegativeInfinity;
            double lastTime = double.NaN;
            int lastVisualGroup = -1;
            bool previousHandRight = options.MainHandRight;
            long alternateSlot = 0;

            int start = 0;
            while (start < items.Count)
            {
                int end = start + 1;
                long demand = items[start].Keys;
                while (end < items.Count && items[end].Time - items[start].Time < groupWindow)
                {
                    demand += items[end].Keys;
                    end++;
                }

                // A separated passage returns to the chosen main hand. Dense
                // successive groups alternate from the previous group's last hand.
                bool startRight = start == 0 ||
                    items[start].Time - items[start - 1].Time >= groupWindow
                    ? options.MainHandRight : !previousHandRight;
                long slot = 0;
                for (int index = start; index < end; index++)
                {
                    Item item = items[index];
                    if (item.Time != lastTime)
                    {
                        Array.Clear(atThisTime, 0, lanes);
                        lastTime = item.Time;
                    }
                    if (item.VisualGroup != lastVisualGroup)
                    {
                        Array.Clear(inVisualGroup, 0, lanes);
                        lastVisualGroup = item.VisualGroup;
                    }

                    int allocated = 0;
                    // An impossible RequiredKeys cannot cause an enormous loop.
                    // One counted overflow marker preserves all unmet demand.
                    for (int key = 0; key < Math.Min(item.Keys, lanes); key++)
                    {
                        int preferred;
                        if (lanes == 1)
                            preferred = 0;
                        else if (item.SlowHold)
                            preferred = item.SlowHoldRight ? half : half - 1;
                        else if (options.Mode == 1)
                        {
                            long keySlot = alternateSlot + key;
                            bool right = (keySlot & 1) == 0
                                ? options.MainHandRight : !options.MainHandRight;
                            preferred = right ? half + (int)(keySlot / 2 % half)
                                : half - 1 - (int)(keySlot / 2 % half);
                        }
                        else
                            preferred = InnerRollLane(slot + key, demand, half, startRight);

                        int lane = FindLane(preferred, lanes, half, item.Time,
                            Math.Min(item.Time, item.Display), item.Release > item.Time,
                            occupiedUntil, atThisTime, inVisualGroup, lastWasHold);
                        if (lane < 0)
                            break;
                        occupiedUntil[lane] = item.Release;
                        lastWasHold[lane] = item.Release > item.Time;
                        atThisTime[lane] = true;
                        inVisualGroup[lane] = true;
                        output.Add(new LaneNote(item.Source, lane, item.Time,
                            item.Release, item.Display, item.GroupSize));
                        allocated++;
                    }
                    if (allocated < item.Keys)
                        output.Add(new LaneNote(item.Source, -1, item.Time, item.Release,
                            item.Display, item.GroupSize, item.Keys - allocated));
                    slot += item.Keys;
                    alternateSlot += item.Keys;
                }

                previousHandRight = ((demand - 1) / half & 1) == 0
                    ? startRight : !startRight;
                start = end;
            }
            return output.ToArray();
        }

        private static int InnerRollLane(long slot, long total, int half, bool startRight)
        {
            long block = slot / half;
            int index = (int)(slot % half);
            int count = (int)Math.Min(half, total - block * half);
            bool right = (block & 1) == 0 ? startRight : !startRight;
            return right ? half + count - 1 - index : half - count + index;
        }

        private static int FindLane(int preferred, int lanes, int half, double time,
            double displayTime, bool isHold, double[] occupiedUntil, bool[] atThisTime,
            bool[] inVisualGroup, bool[] lastWasHold)
        {
            // Prefer a new key for consecutive holds sharing a release/press
            // boundary. If it is the only available lane, reuse is still legal.
            for (int restartPass = 0; restartPass < (isHold ? 2 : 1); restartPass++)
            {
                bool avoidRestart = isHold && restartPass == 0;
                if (IsFree(preferred, time, displayTime, avoidRestart,
                    occupiedUntil, atThisTime, inVisualGroup, lastWasHold))
                    return preferred;
                bool preferRight = preferred >= half;
                // Retain the intended hand if possible, then borrow the other
                // hand. There is no modulo folding onto an occupied hold.
                for (int pass = 0; pass < 2; pass++)
                {
                    bool right = pass == 0 ? preferRight : !preferRight;
                    for (int finger = 0; finger < half; finger++)
                    {
                        int lane = right ? half + finger : half - 1 - finger;
                        if (lane < lanes && IsFree(lane, time, displayTime, avoidRestart,
                            occupiedUntil, atThisTime, inVisualGroup, lastWasHold))
                            return lane;
                    }
                }
            }
            return -1;
        }

        private static bool IsFree(int lane, double time, double displayTime, bool avoidRestart,
            double[] occupiedUntil, bool[] atThisTime, bool[] inVisualGroup, bool[] lastWasHold)
        {
            return lane >= 0 && lane < occupiedUntil.Length &&
                occupiedUntil[lane] <= displayTime && !atThisTime[lane] && !inVisualGroup[lane] &&
                !(avoidRestart && lastWasHold[lane] && occupiedUntil[lane] == time);
        }

        private static void SetSlowHoldHands(List<Item> items, double interval, bool mainRight)
        {
            int start = 0;
            while (start < items.Count)
            {
                if (items[start].Release <= items[start].Time)
                {
                    start++;
                    continue;
                }
                int end = start + 1;
                while (end < items.Count && items[end].Release > items[end].Time &&
                    items[end].Time - items[end - 1].Time > interval)
                    end++;
                if (end - start >= 2)
                {
                    for (int i = start; i < end; i++)
                    {
                        items[i].SlowHold = true;
                        items[i].SlowHoldRight = ((i - start) & 1) == 0 ? mainRight : !mainRight;
                    }
                }
                start = end;
            }
        }

        private static List<Item> Normalize(IReadOnlyList<RhythmEvent> events)
        {
            var items = new List<Item>(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                RhythmEvent value = events[i];
                // Finite negative song times are valid pre-roll positions.
                double time = Finite(value.Time) ? value.Time : 0;
                double release = Finite(value.ReleaseTime) && value.ReleaseTime > time
                    ? value.ReleaseTime : time;
                items.Add(new Item
                {
                    Source = i, Time = time, Release = release,
                    Keys = Math.Max(1, value.RequiredKeys),
                    Angle = Finite(value.TravelDegrees) && value.TravelDegrees >= 0
                        ? value.TravelDegrees : double.NaN,
                    Bpm = Positive(value.Bpm, 120)
                });
            }
            items.Sort((a, b) =>
            {
                int compare = a.Time.CompareTo(b.Time);
                return compare != 0 ? compare : a.Source.CompareTo(b.Source);
            });
            return items;
        }

        private static void SetVisualGroups(List<Item> items, bool merge)
        {
            // Even with optional merging disabled, a real simultaneous chord
            // needs distinct lanes and an accurate total requirement count.
            int start = 0, group = 0;
            while (start < items.Count)
            {
                int end = start + 1;
                while (end < items.Count && items[end].Time == items[start].Time)
                    end++;
                SetGroup(items, start, end, group++, items[start].Time);
                start = end;
            }
            if (!merge)
                return;

            start = 0;
            while (start < items.Count)
            {
                int end = start + 1;
                while (end < items.Count && IsNearby(items[end - 1], items[end]))
                    end++;
                // Deliberately reject the whole overlong candidate. Splitting
                // it would invent several chords absent from the original rule.
                if (end - start >= 2 && items[end - 1].Time - items[start].Time <= 0.1)
                    SetGroup(items, start, end, group++, items[start].Time);
                start = end;
            }
        }

        private static bool IsNearby(Item before, Item after)
        {
            double gap = after.Time - before.Time;
            if (gap == 0)
                return true; // Never split a real simultaneous chord by its angles.
            return gap >= 0 && gap < 0.05 && Finite(after.Angle) &&
                after.Angle != 999 && after.Angle <= (after.Bpm >= 300 ? 30 : 15);
        }

        private static void SetGroup(List<Item> items, int start, int end, int group, double display)
        {
            long demand = 0;
            for (int i = start; i < end; i++)
                demand += items[i].Keys;
            int size = (int)Math.Min(int.MaxValue, demand);
            for (int i = start; i < end; i++)
            {
                items[i].VisualGroup = group;
                items[i].Display = display;
                items[i].GroupSize = size;
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Positive(double value, double fallback) => Finite(value) && value > 0 ? value : fallback;
        private static int ValidLaneCount(int count) => count == 4 || count == 6 || count == 8 ? count : 1;
    }
}
