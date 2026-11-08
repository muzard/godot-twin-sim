namespace Twin.Sim.Geometry;

/// <summary>2D segment and polyline geometry for corridors and boundaries.</summary>
public static class Segments
{
    /// <summary>Shortest distance from a point to a polyline (a single point counts as a polyline).</summary>
    public static double DistanceToPolyline(Vec2 point, IReadOnlyList<Vec2> polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        if (polyline.Count == 1)
        {
            return point.DistanceTo(polyline[0]);
        }

        var best = double.PositiveInfinity;
        for (var i = 0; i + 1 < polyline.Count; i++)
        {
            best = Math.Min(best, DistanceToSegment(point, polyline[i], polyline[i + 1]));
        }

        return best;
    }

    /// <summary>Shortest distance from a point to the segment a–b.</summary>
    public static double DistanceToSegment(Vec2 point, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.Dot(ab);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((point - a).Dot(ab) / lengthSquared, 0, 1);
        return point.DistanceTo(a + (ab * t));
    }

    /// <summary>
    /// Where segment p1–p2 meets segment q1–q2, touching included (an end point on the other segment counts), or null.
    /// Collinear overlaps return the first overlapping point of p1–p2. Conservative on purpose: a route that only
    /// touches a hard boundary is treated as crossing it.
    /// </summary>
    public static Vec2? Intersection(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var d1 = Cross(q1, q2, p1);
        var d2 = Cross(q1, q2, p2);
        var d3 = Cross(p1, p2, q1);
        var d4 = Cross(p1, p2, q2);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return p1 + ((p2 - p1) * (d1 / (d1 - d2)));
        }

        if (d1 == 0 && OnSegment(q1, q2, p1))
        {
            return p1;
        }

        if (d3 == 0 && OnSegment(p1, p2, q1))
        {
            return q1;
        }

        if (d4 == 0 && OnSegment(p1, p2, q2))
        {
            return q2;
        }

        if (d2 == 0 && OnSegment(q1, q2, p2))
        {
            return p2;
        }

        return null;
    }

    /// <summary>The first point where segment a–b meets the polyline, or null.</summary>
    public static Vec2? FirstIntersection(Vec2 a, Vec2 b, IReadOnlyList<Vec2> polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        Vec2? best = null;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i + 1 < polyline.Count; i++)
        {
            if (Intersection(a, b, polyline[i], polyline[i + 1]) is { } hit && a.DistanceTo(hit) < bestDistance)
            {
                best = hit;
                bestDistance = a.DistanceTo(hit);
            }
        }

        return best;
    }

    private static double Cross(Vec2 origin, Vec2 a, Vec2 b) =>
        ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));

    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
}
