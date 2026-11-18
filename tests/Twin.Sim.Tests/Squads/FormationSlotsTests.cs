using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Sim.Tests.Squads;

/// <summary>
/// Formation slots (backlog 0004, S2a: FO1), before compression. A slot is <c>(along, across)</c> relative to the squad
/// leader: along its path (positive ahead) and across it (positive to the left). Formation order, front to back: the
/// teams without the squad leader or the second-in-command (the point team), then the squad leader's team, then the
/// second-in-command's team; within a team, squad order. Reference squad: pj, k1, k2, rj, j1, j2, vj, kk, t1.
/// </summary>
public class FormationSlotsTests
{
    private const double D = SpecSquad.IntervalMeters;
    private static readonly double S = SpecSquad.WedgeStepMeters;

    private static IReadOnlyList<FormationSlot> Slots(SquadFormation formation) =>
        FormationSlots.For(formation, SpecSquad.Reference().Definition, D);

    [Fact]
    public void FO1_the_formation_order_puts_the_point_team_first_the_command_team_next_and_the_support_team_last() =>
        Assert.Equal(["pj", "k1", "k2", "rj", "j1", "j2", "vj", "kk", "t1"], Slots(SquadFormation.File).Select(s => s.UnitId));

    [Fact]
    public void FO1_a_file_puts_everyone_on_the_leaders_path_one_interval_apart()
    {
        var expected = new (string Id, double Along)[]
        {
            ("pj", 3 * D), ("k1", 2 * D), ("k2", D), ("rj", 0), ("j1", -D), ("j2", -2 * D), ("vj", -3 * D), ("kk", -4 * D),
            ("t1", -5 * D),
        };

        foreach (var (id, along) in expected)
        {
            var slot = Assert.Single(Slots(SquadFormation.File), s => s.UnitId == id);
            Assert.Equal(along, slot.Along, precision: 9);
            Assert.Equal(0, slot.Across, precision: 9);
        }
    }

    [Fact]
    public void FO1_a_wedge_has_its_apex_at_the_first_member_and_the_leader_on_its_axis()
    {
        // Rank r is r steps back from the apex; the leader walks the axis at rank 2 (its arm slot, left, stays empty).
        //         pj
        //       k1  k2
        //     ·  rj  j1
        //   j2          vj
        // kk              t1
        var expected = new (string Id, int Rank, int Side)[]
        {
            ("pj", 0, 0), ("k1", 1, 1), ("k2", 1, -1), ("rj", 2, 0), ("j1", 2, -1), ("j2", 3, 1), ("vj", 3, -1),
            ("kk", 4, 1), ("t1", 4, -1),
        };

        foreach (var (id, rank, side) in expected)
        {
            var slot = Assert.Single(Slots(SquadFormation.Wedge), s => s.UnitId == id);
            Assert.Equal((2 - rank) * S, slot.Along, precision: 9);
            Assert.Equal(side * rank * S, slot.Across, precision: 9);
        }
    }

    [Fact]
    public void FO1_neighbours_on_a_wedge_arm_are_one_interval_apart()
    {
        var slots = Slots(SquadFormation.Wedge).ToDictionary(s => s.UnitId);
        double Distance(string a, string b) =>
            Math.Sqrt(Math.Pow(slots[a].Along - slots[b].Along, 2) + Math.Pow(slots[a].Across - slots[b].Across, 2));

        foreach (var (a, b) in new[] { ("pj", "k1"), ("j2", "kk"), ("pj", "k2"), ("k2", "j1"), ("j1", "vj"), ("vj", "t1") })
        {
            Assert.Equal(D, Distance(a, b), precision: 9);
        }

        // The leader's own arm slot (left, rank 2) stays empty, so k1 and j2 are two intervals apart.
        Assert.Equal(2 * D, Distance("k1", "j2"), precision: 9);
    }

    [Fact]
    public void FO1_the_leaders_slot_is_its_own_position()
    {
        foreach (var formation in new[] { SquadFormation.File, SquadFormation.Wedge })
        {
            var leader = Assert.Single(Slots(formation), s => s.UnitId == "rj");
            Assert.Equal((0.0, 0.0), (leader.Along, leader.Across));
        }
    }
}
