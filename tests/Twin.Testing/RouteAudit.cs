using System.Globalization;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Testing;

/// <summary>
/// Checks a route against acceptance criterion 2 of Smoke test I: it never enters an impassable cell, never
/// leaves the corridor's outer limit, and never crosses a hard boundary without a valid permit. The route can be
/// a planned path (cell centers) or an agent's trail (its position after every tick).
/// Independent of the simulation's own checks; geometry comes from <see cref="TestGeometry"/>.
/// </summary>
public static class RouteAudit
{
    /// <summary>
    /// Slack in meters when matching a forest-edge crossing to a permit. A route sample only shows that the
    /// edge was crossed somewhere between two samples, and a planned path's arc can be one cell long.
    /// </summary>
    public const double ForestEdgeSlackMeters = SpecV01.CellSize;

    /// <summary>Every violation found, as readable text. Empty means the route is valid.</summary>
    public static IReadOnlyList<string> Violations(
        IWorldQuery world, Corridor corridor, IReadOnlyList<Vec2> route, IReadOnlyList<CrossingPermit> permits)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(permits);
        var violations = new List<string>();
        var outerLimit = (corridor.Width / 2) + corridor.SoftMargin;

        for (var i = 0; i < route.Count; i++)
        {
            var p = route[i];
            if (!world.IsInside(p.X, p.Y))
            {
                violations.Add($"point {i} {p} is outside the world");
                continue;
            }

            var terrain = world.ClassAt(p.X, p.Y);
            if (terrain is TerrainClass.Water or TerrainClass.Building)
            {
                violations.Add($"point {i} {p} is in an impassable cell ({terrain})");
            }

            var d = TestGeometry.DistanceToPolyline(p, corridor.Centerline);
            if (d > outerLimit + 1e-6)
            {
                violations.Add(Format($"point {i} {p} is {d:0.##} m from the centerline, outside the outer limit {outerLimit:0.##} m"));
            }
        }

        foreach (var boundary in corridor.HardBoundaries)
        {
            foreach (var crossing in TestGeometry.Crossings(route, boundary.Line))
            {
                if (!Permitted(boundary.Id, crossing, permits, slack: 1e-6))
                {
                    violations.Add($"crosses hard boundary '{boundary.Id}' at {crossing} without a permit");
                }
            }
        }

        for (var i = 0; i + 1 < route.Count; i++)
        {
            var from = route[i];
            var to = route[i + 1];
            if (world.IsInside(from.X, from.Y) && world.IsInside(to.X, to.Y) &&
                world.ClassAt(from.X, from.Y) == TerrainClass.Forest && world.ClassAt(to.X, to.Y) != TerrainClass.Forest &&
                !Permitted(HardBoundary.ForestEdgeId, to, permits, ForestEdgeSlackMeters))
            {
                violations.Add($"leaves forest between {from} and {to} without a permit");
            }
        }

        return violations;
    }

    /// <summary>
    /// The permits the agent actually held: one for each <see cref="PermissionRequested"/> that was answered
    /// with <see cref="Twin.Sim.Command.LeaderAnswer.Grant"/>, valid within <paramref name="radiusMeters"/> of the requested point.
    /// </summary>
    public static IReadOnlyList<CrossingPermit> GrantedPermits(IReadOnlyList<SimEvent> events, double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(events);
        var permits = new List<CrossingPermit>();
        PermissionRequested? pending = null;
        foreach (var e in events)
        {
            switch (e)
            {
                case PermissionRequested request:
                    pending = request;
                    break;
                case PermissionAnswered { Answer: Twin.Sim.Command.LeaderAnswer.Grant } answer
                    when pending is not null && pending.BoundaryId == answer.BoundaryId:
                    permits.Add(new CrossingPermit(pending.BoundaryId, pending.At, radiusMeters));
                    pending = null;
                    break;
                default:
                    break;
            }
        }

        return permits;
    }

    private static bool Permitted(string boundaryId, Vec2 at, IReadOnlyList<CrossingPermit> permits, double slack) =>
        permits.Any(p => p.BoundaryId == boundaryId && at.DistanceTo(p.At) <= p.Radius + slack);

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
