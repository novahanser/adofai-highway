using System;

namespace AdofaiHighway
{
    // Pure viewport geometry. Positions are local to the lane area; percent is
    // a 0..1 fraction of its height, measured from the top.
    internal static class HighwayGeometry
    {
        public static float HitLineY(float height, int positionMode, float fromBottom,
            float percent, float lineThickness)
        {
            height = Available(height);
            if (height == 0f) return 0f;

            // The caller clips the drawn line to the viewport as well. A line
            // taller than the viewport can only be centered, even below 2 px.
            double thickness = Finite(lineThickness) ? Math.Max(0f, lineThickness) : 2f;
            double half = Math.Min(height, thickness) * 0.5;
            double center;
            if (positionMode == 1)
                center = (double)height * (Finite(percent) ? percent : 0.5f);
            else
                center = (double)height - (Finite(fromBottom) ? fromBottom : height * 0.5f);

            // Double intermediates avoid overflow for finite but extreme settings.
            return (float)Math.Max(half, Math.Min((double)height - half, center));
        }

        public static float LaneGap(float totalWidth, int laneCount, float requestedGap)
        {
            totalWidth = Available(totalWidth);
            if (totalWidth == 0f || laneCount <= 0 || !Finite(requestedGap) || requestedGap <= 0f)
                return 0f;

            // The renderer removes one gap from each cell, including half a gap
            // at both outer edges. Keep at least 8 px of each cell for its note.
            double maximum = Math.Max(0.0, (double)totalWidth / laneCount - 8.0);
            return (float)Math.Min(requestedGap, maximum);
        }

        public static float Thickness(float requested, float availableHeight)
        {
            availableHeight = Available(availableHeight);
            if (availableHeight == 0f) return 0f;
            float desired = Finite(requested) ? requested : 6f;
            return Math.Min(availableHeight, Math.Max(2f, Math.Min(80f, desired)));
        }

        private static float Available(float value) => Finite(value) && value > 0f ? value : 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
