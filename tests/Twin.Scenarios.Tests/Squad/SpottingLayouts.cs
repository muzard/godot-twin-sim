using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The reference squad and one hidden enemy for the spotting tests (backlog 0004 S5a1). Red lies prone behind a 0.6 m
/// mound that hides it prone but not standing; a test makes it stand up when the squad is where it wants it, and the
/// squad then sees it. Interval 12 m (not the 10 m of <see cref="SpecSquad"/>), so that no squad mate is exactly the
/// 30 m voice range away from another in file or wedge: with 10 m, the wedge's apex and its third-rank corners are.
/// </summary>
internal static class SpottingLayouts
{
    /// <summary>Interval between neighbours in these layouts.</summary>
    public const double IntervalMeters = 12;

    public static readonly Vec2 East = new(1, 0);

    public static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    /// <summary>Members of the wedge's left arm (backlog 0004 S2a): k1, j2, kk.</summary>
    public static readonly string[] WedgeLeftArm = ["k1", "j2", "kk"];

    /// <summary>Members of the wedge's right arm: k2, j1, vj, t1.</summary>
    public static readonly string[] WedgeRightArm = ["k2", "j1", "vj", "t1"];

    public static IEnumerable<string> Members => SpecSquad.Reference().Members.Select(m => m.UnitId);

    private static Scenario Squad(SquadFormation formation, Vec2 at, Func<TestMap, TestMap> mound, Vec2 red) =>
        Scenario.ForSquadSpotting()
            .WithSquadDoctrine(SpecSpotting.Doctrine with { IntervalMeters = IntervalMeters })
            .WithTerrain(mound(TestMap.Create(widthMeters: 500, heightMeters: 200)).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, at)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire);

    /// <summary>
    /// The wedge has moved east from (80, 101) to (200, 101) and settled there, red unseen. Red lies at (185, 41), south
    /// of the squad and a little behind its leader: outside the field of view of the apex, the leader and the left arm,
    /// in the focus of vj (watching right, 35 m away). From vj's report point, rj, j1, k2 and t1 are within 30 m; pj, k1,
    /// j2 and kk are not.
    /// </summary>
    public static ScenarioRun FlankAtRest()
    {
        var sim = Squad(SquadFormation.Wedge, new Vec2(80, 101),
                m => m.WithMound(fromX: 165, fromY: 45, toX: 205, toY: 47, height: 0.6), new Vec2(185, 41))
            .Build()
            .Order("red", new SetPosture(Posture.Prone));
        sim.Order("rj", new MoveTo(new Vec2(200, 101))).RunUntilSquadSettled("r1", TenMinutes);
        return sim;
    }

    /// <summary>
    /// The file has moved east from (300, 101) to (400, 101) and settled there, red unseen: t1 at x = 340 watches back,
    /// kk 352, vj 364, j2 376, j1 388, rj 400. Red lies at (201, 91), behind the squad and 10 m to the right of its axis:
    /// in the focus of t1 only (139 m), outside the field of view of everyone watching left or ahead.
    /// </summary>
    public static ScenarioRun RearInFile()
    {
        var sim = Squad(SquadFormation.File, new Vec2(300, 101),
                m => m.WithMound(fromX: 205, fromY: 81, toX: 207, toY: 101, height: 0.6), new Vec2(201, 91))
            .Build()
            .Order("red", new SetPosture(Posture.Prone));
        sim.Order("rj", new MoveTo(new Vec2(400, 101))).RunUntilSquadSettled("r1", TenMinutes);
        return sim;
    }

    /// <summary>
    /// The file of <see cref="RearInFile"/> (t1 x = 340, kk 352, vj 364, j2 376, j1 388, rj 400, k2 412, k1 424, pj 436)
    /// with red at (361, 41), 60 m south of vj: in vj's focus (watching right), in the field of view of j1 and k1 (also
    /// watching right, farther off), outside everyone else's. With <paramref name="voiceRangeMeters"/> below the 12 m
    /// interval nobody hears anybody.
    /// </summary>
    public static ScenarioRun RightOfFile(double voiceRangeMeters)
    {
        var sim = Scenario.ForSquadSpotting()
            .WithSquadDoctrine(SpecSpotting.Doctrine with { IntervalMeters = IntervalMeters, VoiceRangeMeters = voiceRangeMeters })
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).WithMound(fromX: 341, fromY: 45, toX: 381, toY: 47, height: 0.6).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.File }, new Vec2(300, 101))
            .WithFighter("red", new Vec2(361, 41), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red", new SetPosture(Posture.Prone));
        sim.Order("rj", new MoveTo(new Vec2(400, 101))).RunUntilSquadSettled("r1", TenMinutes);
        return sim;
    }

    /// <summary>
    /// The wedge moves east from (80, 101) toward <see cref="Destination"/>; red lies at (285, 41), ahead and to the right
    /// of the route. When the leader passes x = 200 red stands up, and the squad sees it while on the move.
    /// </summary>
    public static ScenarioRun AheadOnTheMove(ulong seed = 1)
    {
        var sim = Squad(SquadFormation.Wedge, new Vec2(80, 101),
                m => m.WithMound(fromX: 225, fromY: 45, toX: 301, toY: 47, height: 0.6), new Vec2(285, 41))
            .WithSeed(seed)
            .Build()
            .Order("red", new SetPosture(Posture.Prone));
        sim.Order("rj", new MoveTo(Destination))
            .RunUntil(s => s.Unit("rj").Position.X > 200, TenMinutes, "rj past x = 200");
        return sim;
    }

    /// <summary>Where <see cref="AheadOnTheMove"/> is going.</summary>
    public static readonly Vec2 Destination = new(420, 101);

    /// <summary>Red stands up; it is visible once the posture change is done.</summary>
    public static ScenarioRun RedStandsUp(this ScenarioRun sim) => sim.Order("red", new SetPosture(Posture.Standing));
}
