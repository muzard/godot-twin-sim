using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The search for a firing position in contact (backlog 0005, FP3, FP4), through the query
/// <c>Simulation.FindBestFiringPosition(agentId, enemyId, radiusMeters)</c>: the highest-scoring point within the radius
/// from which the agent sees the enemy, in range, with its fire sector clear of its own side (0003: 30° each side), and
/// that it can reach without an unpermitted crossing; null if there is none. Score, in <see cref="SpecFiringPositions"/>:
/// cover, concealment and height above the enemy count for it; the way there, and more so over open ground, against it.
/// <para>
/// blue stands at (100, 100) with red 80 m north at (100, 180). A wall (a row of building cells: impassable, opaque) runs
/// east–west just north of blue, at y 103–105, so blue cannot see red where it stands. (Changed with the product owner's
/// approval, 2026-11-19: the first version's wall was a 3 m mound, which the 2 m grid's interpolated heights turn into a
/// ramp blue can climb and see from.)
/// </para>
/// </summary>
public class FiringPositionSearchTests
{
    private static readonly Vec2 Blue = new(100, 100);
    private static readonly Vec2 Red = new(100, 180);
    private const double Radius = SpecFiringPositions.FighterSearchMeters;

    private static TestMap Field(double wallFromX, double wallToX, double width = 200, double height = 200) =>
        TestMap.Create(widthMeters: width, heightMeters: height).Fill(TerrainClass.Building, wallFromX, 103, wallToX, 105);

    private static ScenarioRun Fighters(TestMap map, WeaponProfile? blueWeapon = null, Vec2? red = null, Vec2? friend = null)
    {
        var scenario = Scenario.ForCombat()
            .WithTerrain(map.Build())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.ReturnFire, blueWeapon)
            .WithFighter("red", red ?? Red, Side.Red, RulesOfEngagement.HoldFire);
        if (friend is { } at)
        {
            scenario.WithFighter("blue-2", at, Side.Blue, RulesOfEngagement.HoldFire);
        }

        return scenario.Build();
    }

    [Fact]
    public void FP3_a_wooded_rise_8_m_above_the_enemy_25_m_off_beats_an_open_field_10_m_off()
    {
        // The wall reaches from x = 90 to 118: west of it, about 11 m from blue, the open field sees red. South-east, for
        // y 92–100, the ground rises evenly from x = 115 to a top 8 m high at x 127–131 (slope 0.67, walkable), wooded from
        // x = 123: from the rise blue sees red past the wall's end. (Changed with the product owner's approval, 2026-11-19:
        // the first version's rise was an 8 m mound, a sheer step no one can climb.)
        var map = Field(wallFromX: 90, wallToX: 118)
            .WithHeights(p => p.Y is >= 92 and <= 100 && p.X <= 131 ? Math.Clamp((p.X - 115) / 12 * 8, 0, 8) : 0)
            .Fill(TerrainClass.Forest, 123, 92, 131, 100);
        var sim = Fighters(map);

        var at = sim.Simulation.FindBestFiringPosition("blue", "red", Radius);

        Assert.NotNull(at);
        Assert.True(at.Value.X is >= 123 and <= 131 && at.Value.Y is >= 92 and <= 100, $"expected a point on the rise, got {at}");
    }

    [Fact]
    public void FP3_with_no_cover_concealment_or_height_the_nearer_position_wins()
    {
        // The wall reaches from x = 90 to 115: the field west of it is about 11 m from blue, east of it about 16 m.
        var sim = Fighters(Field(wallFromX: 90, wallToX: 115));

        var at = sim.Simulation.FindBestFiringPosition("blue", "red", Radius);

        Assert.NotNull(at);
        Assert.True(at.Value.X < 90, $"expected a point west of the wall, got {at}");
    }

    [Fact]
    public void FP4_a_position_outside_the_forest_is_not_taken_without_a_permit()
    {
        // blue in a band of forest x 80–115; red 120 m east in the open, and blue's rifle reaches 95 m: only open land
        // east of the band, beyond the forest edge, is near enough within the radius.
        var map = TestMap.Create(widthMeters: 260, heightMeters: 200).Fill(TerrainClass.Forest, 80, 0, 115, 200);
        var sim = Fighters(map, SpecFiringPositions.ShortRange(SpecShooting.Rifle, 95), red: new Vec2(220, 100));

        Assert.Null(sim.Simulation.FindBestFiringPosition("blue", "red", Radius));
    }

    [Fact]
    public void FP4_a_position_with_its_own_side_in_the_fire_sector_is_not_taken()
    {
        // The wall reaches from x = 90 to 115. Without blue-2 the search picks a point west of the wall; blue-2, standing
        // at (88, 125), is then within 30° of the line from it to red, so the search must pick another.
        var map = Field(wallFromX: 90, wallToX: 115);
        var friend = new Vec2(88, 125);
        var alone = Fighters(map).Simulation.FindBestFiringPosition("blue", "red", Radius);
        Assert.True(alone is { } a && !SectorClear(a, friend), $"precondition: without blue-2 the search picks {alone}, in its way");

        var at = Fighters(map, friend: friend).Simulation.FindBestFiringPosition("blue", "red", Radius);

        Assert.NotNull(at);
        Assert.True(SectorClear(at.Value, friend), $"blue-2 at {friend} is in the fire sector from {at}");
    }

    [Fact]
    public void FP4_no_position_brings_an_enemy_beyond_range_and_radius_into_range()
    {
        // red 200 m north; blue's rifle reaches 60 m.
        var sim = Fighters(TestMap.Create(widthMeters: 200, heightMeters: 400), SpecFiringPositions.ShortRange(SpecShooting.Rifle, 60),
            red: new Vec2(100, 300));

        Assert.Null(sim.Simulation.FindBestFiringPosition("blue", "red", Radius));
    }

    private static bool SectorClear(Vec2 from, Vec2 friend)
    {
        var toRed = (Red - from).Normalized();
        var toFriend = (friend - from).Normalized();
        return toRed.Dot(toFriend) <= Math.Cos(SpecSuppression.Drill.FireSectorHalfAngleDegrees * Math.PI / 180);
    }
}
