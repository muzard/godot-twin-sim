using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Sim.Tests.Squads;

/// <summary>
/// Formation slots (backlog 0004, S2a: FO1; the wedge redefined in S2c, decision 20), before compression. A slot is
/// <c>(along, across)</c> relative to the squad leader: along its path (positive ahead) and across it (positive to the
/// left). Formation order (the file), front to back: the teams without the squad leader or the second-in-command (the
/// point team), then the squad leader's team, then the second-in-command's team; within a team, squad order. Reference
/// squad: pj, k1, k2, rj, j1, j2, vj, kk, t1.
/// <para>
/// The open line, left to right: the second-in-command's team (riflemen outermost, then the second-in-command, then the
/// machine gunner next to the squad leader), the squad leader and its team in squad order, then the other teams in team
/// order, each in squad order. Reference squad: t1, vj, kk, rj, j1, j2, pj, k1, k2. The wedge is that line folded back
/// at its middle member (j1), so the squad leader walks its left arm.
/// </para>
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
    public void FO1_the_open_line_puts_the_support_team_left_with_its_machine_gunner_next_to_the_leader_and_the_point_team_right() =>
        Assert.Equal(["t1", "vj", "kk", "rj", "j1", "j2", "pj", "k1", "k2"], FormationSlots.LineOrder(SpecSquad.Reference().Definition));

    [Fact]
    public void FO1_a_wedge_is_the_open_line_folded_back_at_its_middle_member()
    {
        // Rank r is r steps back from the apex (j1, the middle of the line); the leader walks the left arm at rank 1.
        //               j1
        //            rj    j2
        //         kk          pj
        //      vj                k1
        //   t1                      k2
        var expected = new (string Id, int Rank, int Side)[]
        {
            ("t1", 4, 1), ("vj", 3, 1), ("kk", 2, 1), ("rj", 1, 1), ("j1", 0, 0), ("j2", 1, -1), ("pj", 2, -1),
            ("k1", 3, -1), ("k2", 4, -1),
        };

        foreach (var (id, rank, side) in expected)
        {
            var slot = Assert.Single(Slots(SquadFormation.Wedge), s => s.UnitId == id);
            Assert.Equal((1 - rank) * S, slot.Along, precision: 9);          // relative to rj, one step back from the apex
            Assert.Equal((side * rank * S) - S, slot.Across, precision: 9);  // rj is one step left of the apex
        }
    }

    [Fact]
    public void FO1_neighbours_on_a_wedge_arm_are_one_interval_apart()
    {
        var slots = Slots(SquadFormation.Wedge).ToDictionary(s => s.UnitId);
        double Distance(string a, string b) =>
            Math.Sqrt(Math.Pow(slots[a].Along - slots[b].Along, 2) + Math.Pow(slots[a].Across - slots[b].Across, 2));

        var line = FormationSlots.LineOrder(SpecSquad.Reference().Definition);
        for (var i = 1; i < line.Count; i++)
        {
            Assert.Equal(D, Distance(line[i - 1], line[i]), precision: 9);
        }
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
