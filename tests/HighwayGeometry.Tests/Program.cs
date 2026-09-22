using AdofaiHighway;

var cases = new (string Name, Action Run)[]
{
    ("pixel mode places the center relative to the bottom", PixelPosition),
    ("percentage mode measures a 0..1 fraction from the top", PercentPosition),
    ("4320px lanes retain the entire adjustable position range", EightKPosition),
    ("thick lines remain within either viewport edge", ThickLineEdges),
    ("tiny and empty viewports produce legal clipped geometry", TinyViewports),
    ("4K, 6K and 8K gaps preserve an 8px note where possible", MinimumLaneWidth),
    ("thickness uses 2..80px and clips to the actual viewport", ThicknessBounds),
    ("nonfinite inputs cannot escape as invalid geometry", NonfiniteInputs),
    ("both modes scale correctly across screen sizes", DifferentScreens),
    ("finite extremes and unsupported modes remain bounded", ExtremeInputs),
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
Console.WriteLine($"{cases.Length - failed}/{cases.Length} geometry cases passed.");
return failed == 0 ? 0 : 1;

static void PixelPosition()
{
    Near(810, HighwayGeometry.HitLineY(900, 0, 90, 0.1f, 6), "bottom offset");
    Near(3, HighwayGeometry.HitLineY(900, 0, 900, 0.1f, 6), "top clamp");
    Near(897, HighwayGeometry.HitLineY(900, 0, 0, 0.1f, 6), "bottom clamp");
    Near(3, HighwayGeometry.HitLineY(900, 0, 5000, 0.1f, 6), "oversized bottom offset");
    Near(897, HighwayGeometry.HitLineY(900, 0, -5000, 0.1f, 6), "negative bottom offset");
}

static void PercentPosition()
{
    Near(750, HighwayGeometry.HitLineY(1000, 1, 999, 0.75f, 6), "fraction uses top origin");
    Near(500, HighwayGeometry.HitLineY(1000, 1, 999, 0.5f, 6), "half height");
    Near(3, HighwayGeometry.HitLineY(1000, 1, 999, -1, 6), "negative fraction clamps");
    Near(997, HighwayGeometry.HitLineY(1000, 1, 999, 2, 6), "fraction over one clamps");
}

static void EightKPosition()
{
    Near(4230, HighwayGeometry.HitLineY(4320, 0, 90, 0, 8), "8K bottom offset");
    Near(320, HighwayGeometry.HitLineY(4320, 0, 4000, 0, 8), "position farther than 300px from bottom");
    Near(3240, HighwayGeometry.HitLineY(4320, 1, 0, 0.75f, 80), "8K percentage");
    Near(40, HighwayGeometry.HitLineY(4320, 1, 0, 0, 80), "thick line at 8K top");
    Near(4280, HighwayGeometry.HitLineY(4320, 1, 0, 1, 80), "thick line at 8K bottom");
}

static void ThickLineEdges()
{
    foreach (float height in new[] { 1f, 20f, 79f, 80f, 100f, 4320f })
    foreach (float requested in new[] { 2f, 3f, 40f, 80f })
    foreach (float percent in new[] { -10f, 0f, 0.01f, 0.5f, 0.99f, 1f, 10f })
    {
        float thickness = HighwayGeometry.Thickness(requested, height);
        float center = HighwayGeometry.HitLineY(height, 1, 0, percent, thickness);
        Inside(height, center, thickness, $"height={height}, thickness={requested}, percent={percent}");
    }
    Near(10, HighwayGeometry.HitLineY(20, 1, 0, 0, 500), "oversized line is centered");
    Near(10, HighwayGeometry.HitLineY(20, 1, 0, 1, 500), "oversized line clips symmetrically");
}

static void TinyViewports()
{
    foreach (float height in new[] { 0f, 0.001f, 0.1f, 1f, 1.9f })
    {
        float thickness = HighwayGeometry.Thickness(80, height);
        Near(height, thickness, "thickness cannot exceed tiny viewport", 1e-6);
        foreach (int mode in new[] { 0, 1 })
        {
            float center = HighwayGeometry.HitLineY(height, mode, 500, 1, thickness);
            Near(height * 0.5f, center, "tiny viewport line center", 1e-6);
            Inside(height, center, thickness, "tiny viewport");
        }
    }
    Near(0, HighwayGeometry.HitLineY(-10, 0, 0, 1, 6), "negative height");
    Near(0, HighwayGeometry.Thickness(6, -10), "negative available height");
}

static void MinimumLaneWidth()
{
    foreach (int lanes in new[] { 4, 6, 8 })
    {
        float exactMinimum = lanes * 8;
        Near(0, HighwayGeometry.LaneGap(exactMinimum, lanes, 100), $"{lanes}K minimum width");
        Near(0, HighwayGeometry.LaneGap(exactMinimum - 1, lanes, 100), $"{lanes}K narrow width");
        Near(4, HighwayGeometry.LaneGap(lanes * 12, lanes, 100), $"{lanes}K maximum gap");
        Near(2, HighwayGeometry.LaneGap(lanes * 12, lanes, 2), $"{lanes}K requested gap");
        foreach (float width in new[] { 0.1f, 40f, 64f, 80f, 240f, 1920f, 7680f })
        {
            float gap = HighwayGeometry.LaneGap(width, lanes, 10000);
            Require(gap >= 0, "gap is nonnegative");
            if (width / lanes >= 8)
                Require(width / lanes - gap >= 8 - 0.0001f, "gap leaves an 8px note");
            else
                Near(0, gap, "unavoidably narrow cells have no extra gap");
        }
    }
    Near(0, HighwayGeometry.LaneGap(240, 4, -4), "negative gap");
    Near(0, HighwayGeometry.LaneGap(240, 0, 4), "zero lanes");
    Near(0, HighwayGeometry.LaneGap(240, -4, 4), "negative lanes");
    Near(4, HighwayGeometry.LaneGap(240, 1, 4), "one lane follows the same cell-width rule");
}

static void ThicknessBounds()
{
    Near(2, HighwayGeometry.Thickness(-1, 1000), "negative thickness");
    Near(2, HighwayGeometry.Thickness(0, 1000), "zero thickness");
    Near(2, HighwayGeometry.Thickness(1, 1000), "minimum thickness");
    Near(6, HighwayGeometry.Thickness(6, 1000), "ordinary thickness");
    Near(80, HighwayGeometry.Thickness(8000, 1000), "maximum thickness");
    Near(20, HighwayGeometry.Thickness(80, 20), "available-height clipping");
    Near(1, HighwayGeometry.Thickness(2, 1), "available height overrides minimum");
}

static void NonfiniteInputs()
{
    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    {
        Near(0, HighwayGeometry.HitLineY(invalid, 1, 0, 0.5f, 6), "invalid viewport height");
        Near(50, HighwayGeometry.HitLineY(100, 1, 0, invalid, 6), "invalid percentage centers safely");
        Near(50, HighwayGeometry.HitLineY(100, 0, invalid, 0.5f, 6), "invalid offset centers safely");
        Inside(100, HighwayGeometry.HitLineY(100, 1, 0, 0, invalid), 2, "invalid line thickness");
        Near(6, HighwayGeometry.Thickness(invalid, 100), "invalid requested note thickness");
        Near(0, HighwayGeometry.Thickness(6, invalid), "invalid available height");
        Near(0, HighwayGeometry.LaneGap(invalid, 4, 4), "invalid width");
        Near(0, HighwayGeometry.LaneGap(240, 4, invalid), "invalid gap");
    }
}

static void DifferentScreens()
{
    foreach (float screenHeight in new[] { 480f, 720f, 1080f, 1440f, 2160f, 4320f })
    foreach (float fraction in new[] { 0.3f, 0.9f, 1f })
    {
        float height = screenHeight * fraction;
        float percentCenter = HighwayGeometry.HitLineY(height, 1, 123, 0.75f, 8);
        Near(height * 0.75f, percentCenter, "percentage adapts to viewport height", 0.001);
        float pixelCenter = HighwayGeometry.HitLineY(height, 0, 90, 0, 8);
        Near(height - 90, pixelCenter, "pixel mode keeps its bottom distance", 0.001);
        Inside(height, percentCenter, 8, "percentage across screen sizes");
        Inside(height, pixelCenter, 8, "pixels across screen sizes");
    }
}

static void ExtremeInputs()
{
    Near(997, HighwayGeometry.HitLineY(1000, 1, 0, float.MaxValue, 6), "finite percentage overflow");
    Near(3, HighwayGeometry.HitLineY(1000, 0, float.MaxValue, 0, 6), "finite pixel overflow");
    Near(910, HighwayGeometry.HitLineY(1000, -1, 90, 0, 6), "unknown mode falls back to pixels");
    Near(910, HighwayGeometry.HitLineY(1000, int.MaxValue, 90, 0, 6), "large unknown mode");
    Require(float.IsFinite(HighwayGeometry.HitLineY(float.MaxValue, 1, 0, float.MaxValue, 80)),
        "huge finite inputs do not overflow");
    Require(float.IsFinite(HighwayGeometry.LaneGap(float.MaxValue, 1, float.MaxValue)),
        "huge width/gap stays finite");
    Near(0, HighwayGeometry.LaneGap(1000, int.MaxValue, 100), "huge lane count");
}

static void Inside(float height, float center, float thickness, string because)
{
    Require(float.IsFinite(center), $"{because}: finite center");
    Require(center - thickness * 0.5f >= -0.0001f, $"{because}: top edge stays inside");
    Require(center + thickness * 0.5f <= height + 0.0001f, $"{because}: bottom edge stays inside");
}
static void Near(double expected, double actual, string because, double tolerance = 0.0001)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        throw new Exception($"{because}: expected {expected:R}, got {actual:R}");
}
static void Require(bool condition, string because)
{
    if (!condition) throw new Exception(because);
}
