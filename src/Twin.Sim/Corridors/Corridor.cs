using Twin.Sim.Geometry;

namespace Twin.Sim.Corridors;

/// <summary>Where a hard boundary comes from.</summary>
public enum BoundarySource
{
    /// <summary>A line given in the order, for example "do not cross the road".</summary>
    Order,

    /// <summary>Derived from the terrain, for example the forest edge (docs/adr/0009).</summary>
    Terrain,
}

/// <summary>
/// A named line the agent may not cross without the leader's permission (spec v0.1, "Käytävä").
/// </summary>
/// <param name="Id">Name used in permission requests and permits.</param>
/// <param name="Line">The line as a polyline in local meters.</param>
/// <param name="Source">Where the boundary comes from.</param>
public sealed record HardBoundary(string Id, IReadOnlyList<Vec2> Line, BoundarySource Source)
{
    /// <summary>
    /// Id of the terrain-derived boundary crossed when leaving forest (docs/adr/0009). It needs no line: any move from a
    /// forest cell to a passable cell of another class crosses it.
    /// </summary>
    public const string ForestEdgeId = "forest-edge";
}

/// <summary>
/// The corridor of an order: a centerline, an inner zone of width <paramref name="Width"/> with no penalty, a soft
/// zone of width <paramref name="SoftMargin"/> on both sides with cost factor k(d) = 1 + α((d − w/2)/m)², nothing beyond
/// the outer limit w/2 + m, and hard boundaries.
/// </summary>
/// <param name="Centerline">Order points; the agent's destination is the last one.</param>
/// <param name="Width">Inner zone width w in meters.</param>
/// <param name="SoftMargin">Soft zone width m in meters on each side.</param>
/// <param name="SoftPenalty">Penalty coefficient α; 0 makes the soft zone a free widening.</param>
/// <param name="HardBoundaries">Lines that need a permit to cross.</param>
public sealed record Corridor(
    IReadOnlyList<Vec2> Centerline, double Width, double SoftMargin, double SoftPenalty, IReadOnlyList<HardBoundary> HardBoundaries)
{
    /// <summary>Distance from the centerline beyond which the corridor ends.</summary>
    public double OuterLimit => (Width / 2) + SoftMargin;

    /// <summary>
    /// The direction of movement at <paramref name="point"/>: the unit direction of the centerline segment nearest to it
    /// (the first such segment on a tie); zero for a centerline of a single point.
    /// </summary>
    public Vec2 DirectionAt(Vec2 point)
    {
        var best = Vec2.Zero;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i + 1 < Centerline.Count; i++)
        {
            var d = Segments.DistanceToSegment(point, Centerline[i], Centerline[i + 1]);
            if (d < bestDistance && Centerline[i + 1] != Centerline[i])
            {
                bestDistance = d;
                best = (Centerline[i + 1] - Centerline[i]).Normalized();
            }
        }

        return best;
    }

    /// <summary>
    /// True if the move <paramref name="from"/>–<paramref name="to"/>, crossing at <paramref name="at"/>, is in the direction
    /// of movement: within <paramref name="degrees"/> of <see cref="DirectionAt"/> there (a move of no length, or a
    /// centerline of a single point, counts as forward).
    /// </summary>
    public bool IsForward(Vec2 at, Vec2 from, Vec2 to, double degrees)
    {
        var forward = DirectionAt(at);
        if (forward == Vec2.Zero || to == from)
        {
            return true;
        }

        var cos = Math.Clamp((to - from).Normalized().Dot(forward), -1, 1);
        return Math.Acos(cos) * 180 / Math.PI <= degrees + 1e-9;
    }

    /// <summary>Cost factor at distance <paramref name="d"/> from the centerline; infinity beyond the outer limit.</summary>
    public double CostFactor(double d)
    {
        if (d <= Width / 2)
        {
            return 1;
        }

        if (d > OuterLimit)
        {
            return double.PositiveInfinity;
        }

        var x = (d - (Width / 2)) / SoftMargin;
        return 1 + (SoftPenalty * x * x);
    }
}

/// <summary>
/// The leader's permission to cross one hard boundary within <paramref name="Radius"/> of a point. For a drawn line the
/// distance is measured to the crossing point; for the forest edge, to the end of the move out of the forest.
/// </summary>
/// <param name="BoundaryId">The boundary the permit opens.</param>
/// <param name="At">The requested crossing point.</param>
/// <param name="Radius">Radius in meters; <see cref="double.PositiveInfinity"/> for a squad's permit to leave the forest,
/// which covers every forest edge for the rest of the order.</param>
public sealed record CrossingPermit(string BoundaryId, Vec2 At, double Radius)
{
    /// <summary>True if the permit covers crossing <paramref name="boundaryId"/> at <paramref name="point"/>.</summary>
    public bool Covers(string boundaryId, Vec2 point) => BoundaryId == boundaryId && point.DistanceTo(At) <= Radius;
}
