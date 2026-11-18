using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The reference squad and one hidden enemy for the spotting tests (backlog 0004 S5a1). Red lies prone behind a 0.6 m
/// mound that hides it prone but not standing; a test makes it stand up when the squad is where it wants it, and the
/// squad then sees it. Interval 12 m (not the 10 m of <see cref="SpecSquad"/>), so that no squad mate is exactly the 30 m
/// voice range away from another in file or wedge.
/// <para>
/// Squad members watch their S2c observation sectors (backlog 0004, decision 20): on the move east the wedge's apex j1
/// watches ahead, its left arm (t1, vj, kk, rj) north and its right arm (j2, pj, k1, k2) south, t1 and k2 also back
/// (west). In the file pj watches ahead, then the places alternate left and right: k1 left, k2 right, rj left, j1 right,
/// j2 left, vj right, kk left, t1 right, and kk and t1 also back.
/// </para>
/// </summary>
internal static class SpottingLayouts
{
    /// <summary>Interval between neighbours in these layouts.</summary>
    public const double IntervalMeters = 12;

    public static readonly Vec2 East = new(1, 0);

    public static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    /// <summary>The wedge's left arm (backlog 0004, S2c): t1, vj, kk and the squad leader rj, observing left.</summary>
    public static readonly string[] WedgeLeftArm = ["t1", "vj", "kk", "rj"];

    /// <summary>The wedge's right arm: j2, pj, k1, k2, observing right. (j1 is the apex.)</summary>
    public static readonly string[] WedgeRightArm = ["j2", "pj", "k1", "k2"];

    public static IEnumerable<string> Members => SpecSquad.Reference().Members.Select(m => m.UnitId);

    private static Scenario Squad(
        SquadFormation formation, Vec2 at, Func<TestMap, TestMap> terrain, Vec2 red, double voiceRangeMeters = SpecSpotting.VoiceRangeMeters) =>
        Scenario.ForSquadSpotting()
            .WithSquadDoctrine(SpecSpotting.Doctrine with { IntervalMeters = IntervalMeters, VoiceRangeMeters = voiceRangeMeters })
            .WithTerrain(terrain(TestMap.Create(widthMeters: 500, heightMeters: 200)).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, at)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire);

    private static ScenarioRun SettledAt(this Scenario scenario, Vec2 destination)
    {
        var sim = scenario.Build().Order("red", new SetPosture(Posture.Prone));
        sim.Order("rj", new MoveTo(destination)).RunUntilSquadSettled("r1", TenMinutes);
        return sim;
    }

    /// <summary>
    /// The wedge has moved east from (80, 101) to (200, 101) and settled there, red unseen: j1 (apex) at (208.5, 92.5),
    /// rj (200, 101), j2 (200, 84), pj (191.5, 75.5), k1 (183, 67), k2 (174.5, 58.6), and the left arm north-west of rj.
    /// Red lies at (215, 21), south of the squad: in the focus of j2 (watching south, 65 m away), elsewhere in the field
    /// of view of pj, k1, k2 and the apex, outside that of the left arm. From j2, rj, j1, pj, kk and k1 are within 30 m;
    /// vj, k2 and t1 are not.
    /// </summary>
    public static ScenarioRun FlankAtRest() =>
        Squad(SquadFormation.Wedge, new Vec2(80, 101),
                m => m.WithMound(fromX: 195, fromY: 25, toX: 235, toY: 27, height: 0.6), new Vec2(215, 21))
            .SettledAt(new Vec2(200, 101));

    /// <summary>
    /// The file has moved east from (300, 101) to (400, 101) and settled there, red unseen: t1 at x = 340, kk 352,
    /// vj 364, j2 376, j1 388, rj 400. Red lies at (201, 91), behind the squad: in the focus of the rear sectors of t1 and
    /// kk, the last two, 139 m and 151 m away; outside the field of view of everyone watching left or ahead.
    /// </summary>
    public static ScenarioRun BehindTheFile() =>
        Squad(SquadFormation.File, new Vec2(300, 101),
                m => m.WithMound(fromX: 205, fromY: 81, toX: 207, toY: 101, height: 0.6), new Vec2(201, 91))
            .SettledAt(new Vec2(400, 101));

    /// <summary>
    /// The file has moved east from (300, 181) to (400, 181) and settled there, red unseen: t1 at x = 340, kk 352,
    /// vj 364, j2 376, j1 388, rj 400, k2 412. Red lies at (327, 61), 120 m south of the file's tail: in the focus of t1
    /// (watching right), elsewhere in the field of view of vj (right) and kk (rear), slower. A building block near red
    /// hides it from k2 and j1, which would otherwise spot and report it themselves before the relays below reach rj:
    /// t1's report is heard by kk and vj; vj, the nearer to rj, repeats it to j2 and j1; j1 repeats it to rj.
    /// </summary>
    public static ScenarioRun BesideTheFileTail() =>
        Squad(SquadFormation.File, new Vec2(300, 181),
                m => m.WithMound(fromX: 319, fromY: 63, toX: 339, toY: 65, height: 0.6)
                    .Fill(TerrainClass.Building, fromX: 330, fromY: 66, toX: 334, toY: 68),
                new Vec2(327, 61))
            .SettledAt(new Vec2(400, 181));

    /// <summary>
    /// The file of <see cref="BehindTheFile"/> (t1 x = 340, kk 352, vj 364, j2 376, j1 388, rj 400, k2 412, k1 424,
    /// pj 436) with red at (361, 41), 60 m south of vj: in vj's focus (watching right), in the field of view of the
    /// others watching right, farther off, outside the field of view of those watching left or ahead. With
    /// <paramref name="voiceRangeMeters"/> below the 12 m interval nobody hears anybody.
    /// </summary>
    public static ScenarioRun RightOfFile(double voiceRangeMeters) =>
        Squad(SquadFormation.File, new Vec2(300, 101),
                m => m.WithMound(fromX: 341, fromY: 45, toX: 381, toY: 47, height: 0.6), new Vec2(361, 41), voiceRangeMeters)
            .SettledAt(new Vec2(400, 101));

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
