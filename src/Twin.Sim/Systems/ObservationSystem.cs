using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Sim.Units;

namespace Twin.Sim.Systems;

/// <summary>
/// Headings and observation sectors (backlog 0004, S2c; data only until the observation model arrives). Every unit faces
/// the way it last moved. A member of a squad that is moving or deployed gets sectors, each
/// <see cref="SquadDoctrine.SectorWidthDegrees"/> wide, and faces the centre of its first:
/// <list type="bullet">
/// <item>On the move (directions along the leader's path at its slot): the point observes ahead and the others their own
/// side. In a file the point is the first and the others alternate, odd places left and even places right; in a wedge
/// the point is the apex and each arm observes its own side. The two at the back (the last two of a file, the ends of a
/// wedge's arms) also observe the rear and the air.</item>
/// <item>Deployed at the forest edge: everyone observes out of the forest; the two ends of the first line also observe
/// their flank, the rear and the air.</item>
/// </list>
/// Runs last, after everyone has moved.
/// </summary>
public sealed class ObservationSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var unit in simulation.Units)
        {
            if (unit.Velocity.Xy.Length > 1e-9)
            {
                unit.Heading = unit.Velocity.Xy.Normalized();
            }

            unit.ObservationSectors = [];
            unit.WatchesAir = false;
        }

        var width = simulation.Config.SquadDoctrine.SectorWidthDegrees;
        foreach (var squad in simulation.Squads)
        {
            if (SquadSlots.Layout(simulation, squad) is not { } layout)
            {
                continue;
            }

            var file = FormationSlots.FormationOrder(squad.Definition).Select(m => m.UnitId).ToList();
            var line = FormationSlots.LineOrder(squad.Definition);
            foreach (var (unitId, forward) in layout.Forward)
            {
                var unit = simulation.GetUnit(unitId);
                if (unit.Health == Combat.Health.OutOfAction)
                {
                    continue; // observes nothing
                }

                var (sectors, air) = layout.Line is { } edge && edge.Acted.Contains(unitId)
                    ? AtTheEdge(edge, unitId)
                    : OnTheMove(layout.Effective[unitId] == SquadFormation.File ? file : line, layout.Effective[unitId], unitId, forward);
                unit.ObservationSectors = sectors.Select(c => new ObservationSector(c, width)).ToList();
                unit.WatchesAir = air;
                unit.Heading = sectors[0];
            }
        }
    }

    private static Vec2 Left(Vec2 direction) => new(-direction.Y, direction.X);

    /// <summary>The sector centres and air watch of a member on the move, by its place in the file or the line.</summary>
    private static (List<Vec2> Sectors, bool Air) OnTheMove(IReadOnlyList<string> order, SquadFormation formation, string unitId, Vec2 forward)
    {
        var i = IndexOf(order, unitId);
        var n = order.Count;
        var left = Left(forward);
        Vec2 own;
        bool back;
        if (formation == SquadFormation.File)
        {
            own = i == 0 ? forward : i % 2 == 1 ? left : left * -1;
            back = i >= n - 2;
        }
        else
        {
            var apex = FormationSlots.ApexIndex(n);
            own = i == apex ? forward : i < apex ? left : left * -1;
            back = i == 0 || i == n - 1;
        }

        return back ? ([own, forward * -1], true) : ([own], false);
    }

    /// <summary>The sector centres and air watch of a member in the open line at the forest edge.</summary>
    private static (List<Vec2> Sectors, bool Air) AtTheEdge(EdgeLine edge, string unitId)
    {
        if (unitId == edge.LeftEnd)
        {
            return ([edge.Outward, edge.Left, edge.Outward * -1], true);
        }

        if (unitId == edge.RightEnd)
        {
            return ([edge.Outward, edge.Left * -1, edge.Outward * -1], true);
        }

        return ([edge.Outward], false);
    }

    private static int IndexOf(IReadOnlyList<string> order, string unitId)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], unitId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
