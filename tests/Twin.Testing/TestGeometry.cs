using Twin.Sim.Geometry;

namespace Twin.Testing;

/// <summary>
/// Plain 2D geometry for checking routes in tests. Deliberately independent of the simulation's own
/// geometry code, so a test does not confirm a bug with the same bug.
/// </summary>
public static class TestGeometry
{
    /// <summary>Shortest distance from a point to a polyline.</summary>
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
    /// The point where segment p1–p2 properly crosses segment q1–q2, or null if it does not.
    /// Touching an end point is not a crossing.
    /// </summary>
    public static Vec2? Intersection(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var d1 = Cross(q1, q2, p1);
        var d2 = Cross(q1, q2, p2);
        var d3 = Cross(p1, p2, q1);
        var d4 = Cross(p1, p2, q2);
        if (d1 == 0 || d2 == 0 || d3 == 0 || d4 == 0 || (d1 > 0) == (d2 > 0) || (d3 > 0) == (d4 > 0))
        {
            return null;
        }

        var t = d1 / (d1 - d2);
        return p1 + ((p2 - p1) * t);
    }

    /// <summary>Every point where the path crosses the polyline, in path order.</summary>
    public static IReadOnlyList<Vec2> Crossings(IReadOnlyList<Vec2> path, IReadOnlyList<Vec2> polyline)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(polyline);
        var result = new List<Vec2>();
        for (var i = 0; i + 1 < path.Count; i++)
        {
            for (var j = 0; j + 1 < polyline.Count; j++)
            {
                if (Intersection(path[i], path[i + 1], polyline[j], polyline[j + 1]) is { } hit)
                {
                    result.Add(hit);
                }
            }
        }

        return result;
    }

    private static double Cross(Vec2 origin, Vec2 a, Vec2 b) =>
        ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));
}
