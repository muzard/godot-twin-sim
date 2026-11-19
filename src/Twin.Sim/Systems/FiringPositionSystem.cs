using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Firing positions in contact (backlog 0005). A squad member in contact (it knows of an enemy, or its squad is halted)
/// that cannot fire at any enemy it should fire at (in its fire area once fire is distributed, otherwise any it knows
/// of) and is not in the drill or holding fire:
/// <list type="number">
/// <item>after its reaction time looks for a firing position of its own (<see cref="FiringPositionSearch"/>) and moves
/// there as in 0003's drill: crouching when free, crawling when lightly suppressed, not at all heavily suppressed;</item>
/// <item>finding none, or heavily suppressed, tells its team leader (<see cref="CannotFireReported"/>), once per picture;</item>
/// <item>the team leader, after the relay and its reaction time, moves the whole team (a squad command) to the line from
/// which the most of it can fire, unless at least two of the team can fire, or its machine gun can: then it asks the
/// squad leader (<see cref="TeamMoveRequested"/>), who grants when no other team is moving and the squad has fire
/// superiority (<see cref="TeamMoveAnswered"/>); denied, it asks again after the retry time;</item>
/// <item>a team leader that finds no position tells the squad leader, who searches a wider area around the team.</item>
/// </list>
/// The command team's leader is the squad leader, who decides for its own team without asking. Runs after the squad's
/// commands are delivered, so a member acts on its fire area as soon as it has it.
/// </summary>
public sealed class FiringPositionSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Config.Combat.Observation.IsOff)
        {
            return; // no picture of the enemy to fire at
        }

        foreach (var squad in simulation.Squads)
        {
            foreach (var member in squad.Members)
            {
                Fighter(simulation, squad, simulation.GetUnit(member.UnitId), dtSeconds);
            }

            Teams(simulation, squad);
        }
    }

    private static void Fighter(Simulation simulation, Squad squad, Unit unit, double dt)
    {
        if (unit.Health is Health.OutOfAction or Health.Pinned || unit.Combatant is null || unit.Profile is null ||
            unit.Drill is not null || unit.CurrentOrder is not null)
        {
            // The drill under fire, an order of its own, or being out of it ends the move and the hold.
            unit.FiringRoute = null;
            unit.HoldsFiringPosition = false;
            unit.CannotFireSince = null;
            return;
        }

        if (unit.FiringRoute is not null)
        {
            Move(simulation, unit, dt);
            return;
        }

        var targets = ShouldFireAt(simulation, unit);
        if (unit.Combatant.Rules == RulesOfEngagement.HoldFire || unit.CeasedFire || SquadNotFighting(squad) ||
            targets.Count == 0 || targets.Any(t => CanFireAt(simulation, unit, t.Enemy)) || TeamMoving(simulation, squad, unit.Id))
        {
            unit.CannotFireSince = null;
            return;
        }

        var now = simulation.TimeSeconds;
        unit.CannotFireSince ??= now;
        if (now < unit.CannotFireSince.Value + Reaction(simulation, unit) - CombatQueries.TimeEpsilon)
        {
            return;
        }

        var picture = (unit.KnownEnemies.Count, (object?)unit.FireArea);
        if (unit.ReportedCannotFire == picture)
        {
            return; // told its leader already; nothing has changed since
        }

        if (!unit.IsHeavilySuppressed &&
            FiringPositionSearch.Best(simulation, unit, targets, simulation.Config.SquadDoctrine.FiringPositions.FighterSearchMeters) is { } route)
        {
            Head(simulation, unit, route);
            return;
        }

        Report(simulation, squad, unit, picture);
    }

    /// <summary>
    /// True unless the squad is fighting: halted on contact, or its fire distributed. Members remember every enemy they
    /// have known of, so knowing of one is not enough: once the squad moves on (which ends the distribution) a member
    /// follows its squad rather than seeking a firing position.
    /// </summary>
    private static bool SquadNotFighting(Squad squad) => !squad.IsHalted && squad.Front is null;

    /// <summary>The member sets out along the route to its firing position (or, already there, only crouches).</summary>
    internal static void Head(Simulation simulation, Unit unit, List<Vec2> route)
    {
        unit.CannotFireSince = null;
        unit.HoldsFiringPosition = true;
        if (!unit.IsSuppressed)
        {
            CombatQueries.WantPosture(unit, Posture.Crouching);
        }

        if (route.Count < 2)
        {
            return;
        }

        unit.FiringRoute = route;
        unit.FiringRouteNext = 1;
        CombatQueries.SetActivity(unit, UnitActivity.MovingToFiringPosition);
    }

    /// <summary>Moves along the route as 0003's drill does: crouching when free, crawling when lightly suppressed.</summary>
    private static void Move(Simulation simulation, Unit unit, double dt)
    {
        var route = unit.FiringRoute!;
        if (unit.IsSuppressed && CombatQueries.IsLightlySuppressed(simulation, unit))
        {
            CombatQueries.WantPosture(unit, Posture.Prone); // it crawls
        }

        var mayMove = unit.IsSuppressed
            ? CombatQueries.IsLightlySuppressed(simulation, unit) && unit.PostureChangeUntil is null
            : unit.Posture == Posture.Crouching && unit.PostureChangeUntil is null;
        if (!unit.IsSuppressed && unit.Posture != Posture.Crouching)
        {
            CombatQueries.WantPosture(unit, Posture.Crouching);
        }

        if (!mayMove || simulation.Terrain is not IWorldQuery world)
        {
            unit.Velocity = Vec3.Zero;
            CombatQueries.SetActivity(unit, UnitActivity.MovingToFiringPosition);
            return;
        }

        var previous = unit.Position;
        var position = previous.Xy;
        var remaining = dt;
        while (remaining > 0 && unit.FiringRouteNext < route.Count)
        {
            var target = route[unit.FiringRouteNext];
            var speed = Speed(world, unit.Profile!, position, target) * unit.SpeedFactor;
            var needed = position.DistanceTo(target) / speed;
            if (needed <= remaining)
            {
                position = target;
                remaining -= needed;
                unit.FiringRouteNext++;
            }
            else
            {
                position += (target - position).Normalized() * (speed * remaining);
                remaining = 0;
            }
        }

        unit.Position = position.WithZ(world.GroundHeight(position.X, position.Y));
        if (unit.FiringRouteNext >= route.Count)
        {
            unit.FiringRoute = null;
            unit.Velocity = Vec3.Zero;
            CombatQueries.SetActivity(unit, UnitActivity.Idle);
            return;
        }

        unit.Velocity = (unit.Position - previous) * (1 / dt);
        CombatQueries.SetActivity(unit, UnitActivity.MovingToFiringPosition);
    }

    private static double Speed(IWorldQuery world, Movement.MovementProfile profile, Vec2 a, Vec2 b)
    {
        var sa = profile.BaseSpeed(world.ClassAt(a.X, a.Y));
        var sb = profile.BaseSpeed(world.ClassAt(b.X, b.Y));
        var baseSpeed = sa > 0 && sb > 0 ? (sa + sb) / 2 : Math.Max(sa, sb);
        var length = a.DistanceTo(b);
        var slope = length > 0 ? (world.GroundHeight(b.X, b.Y) - world.GroundHeight(a.X, a.Y)) / length : 0;
        return Math.Max(baseSpeed * profile.SlopeSpeedFactor(slope), 0.01);
    }

    /// <summary>
    /// The member tells its team leader it cannot fire; a team leader tells itself, and decides. With no team leader (one
    /// is taking over) nothing is told, and the member tries again.
    /// </summary>
    private static void Report(Simulation simulation, Squad squad, Unit unit, (int, object?) picture)
    {
        var team = squad.Member(unit.Id).Team;
        if (squad.TeamLeaderId(team) is not { } leaderId)
        {
            return;
        }

        unit.ReportedCannotFire = picture;

        var state = State(squad, team);
        state.Exhausted = false; // a new report: the team leader looks again
        var now = simulation.TimeSeconds;
        var leader = simulation.GetUnit(leaderId);
        if (leaderId == unit.Id)
        {
            state.DecideAt ??= now + Reaction(simulation, leader);
            return;
        }

        simulation.Log(new CannotFireReported(now, unit.Id, leaderId));
        state.DecideAt ??= now + simulation.Config.SquadDoctrine.RelaySeconds + Reaction(simulation, leader);
    }

    private static void Teams(Simulation simulation, Squad squad)
    {
        var now = simulation.TimeSeconds;
        foreach (var (team, state) in squad.TeamFiring)
        {
            if (state.Move is { } move && !Moving(simulation, squad, move))
            {
                state.Move = null;
            }

            if (squad.TeamLeaderId(team) is not { } leaderId)
            {
                continue;
            }

            if (Due(state.DecideAt, now))
            {
                state.DecideAt = null;
                Decide(simulation, squad, team, state, leaderId);
            }

            if (Due(state.AnswerAt, now))
            {
                state.AnswerAt = null;
                Answer(simulation, squad, team, state);
            }

            if (Due(state.MoveAt, now))
            {
                state.MoveAt = null;
                var leader = simulation.GetUnit(leaderId);
                MoveTeam(simulation, squad, team, state, leader, leader.Position.Xy,
                    simulation.Config.SquadDoctrine.FiringPositions.TeamLeaderSearchMeters, escalate: true);
            }

            if (Due(state.SquadLeaderSearchAt, now) && squad.LeaderId is { } squadLeaderId)
            {
                state.SquadLeaderSearchAt = null;
                SquadLeaderSearch(simulation, squad, team, state, simulation.GetUnit(squadLeaderId));
            }
        }
    }

    private static bool Due(double? at, double now) => at is { } time && now >= time - CombatQueries.TimeEpsilon;

    /// <summary>
    /// The team leader decides: with at least two of the team able to fire, or its machine gun, it asks the squad leader;
    /// otherwise it moves the team. The squad leader decides for its own team without asking.
    /// </summary>
    private static void Decide(Simulation simulation, Squad squad, string team, TeamFiringState state, string leaderId)
    {
        if (state.Move is not null || state.AnswerAt is not null || state.MoveAt is not null || state.SquadLeaderSearchAt is not null ||
            state.Exhausted || SquadNotFighting(squad))
        {
            return; // already moving, waiting for an answer or a search, or nothing found and nothing changed since
        }

        var members = TeamMembers(simulation, squad, team);
        var able = members.Where(m => CanFireNow(simulation, m)).ToList();
        var stillFiring = able.Count >= simulation.Config.SquadDoctrine.FiringPositions.TeamStillFiring ||
            able.Any(m => FireSystem.IsMachineGunner(simulation, m));
        var now = simulation.TimeSeconds;
        if (stillFiring && leaderId != squad.LeaderId && squad.LeaderId is { } squadLeaderId)
        {
            simulation.Log(new TeamMoveRequested(now, leaderId, team));
            state.AnswerAt = now + simulation.Config.SquadDoctrine.RelaySeconds + Reaction(simulation, simulation.GetUnit(squadLeaderId));
            return;
        }

        var leader = simulation.GetUnit(leaderId);
        MoveTeam(simulation, squad, team, state, leader, leader.Position.Xy,
            simulation.Config.SquadDoctrine.FiringPositions.TeamLeaderSearchMeters, escalate: true);
    }

    /// <summary>The squad leader grants a team move when no other team is moving and the squad has fire superiority.</summary>
    private static void Answer(Simulation simulation, Squad squad, string team, TeamFiringState state)
    {
        var now = simulation.TimeSeconds;
        if (squad.LeaderId is not { } squadLeaderId)
        {
            state.DecideAt = now + simulation.Config.SquadDoctrine.FiringPositions.RetrySeconds; // no one to answer yet
            return;
        }

        // Granted and about to move counts as moving.
        var otherMoving = squad.TeamFiring.Any(t => t.Key != team &&
            ((t.Value.Move is { } move && Moving(simulation, squad, move)) || t.Value.MoveAt is not null));
        var granted = !otherMoving && FireSuperiority(simulation, squad);
        simulation.Log(new TeamMoveAnswered(now, squadLeaderId, team, granted));
        if (granted)
        {
            state.MoveAt = now + simulation.Config.SquadDoctrine.RelaySeconds;
        }
        else
        {
            state.DecideAt = now + simulation.Config.SquadDoctrine.FiringPositions.RetrySeconds;
        }
    }

    private static void SquadLeaderSearch(Simulation simulation, Squad squad, string team, TeamFiringState state, Unit squadLeader)
    {
        var members = TeamMembers(simulation, squad, team);
        if (members.Count == 0)
        {
            return;
        }

        var centre = members.Aggregate(Vec2.Zero, (sum, m) => sum + m.Position.Xy) * (1.0 / members.Count);
        MoveTeam(simulation, squad, team, state, squadLeader, centre,
            simulation.Config.SquadDoctrine.FiringPositions.SquadLeaderSearchMeters, escalate: false);
    }

    /// <summary>
    /// <paramref name="issuer"/> looks for the team's line within <paramref name="radius"/> of <paramref name="centre"/> and
    /// commands the team there. Finding none, a team leader tells the squad leader (or, being the squad leader, searches
    /// wider at once); the squad leader finding none, the team stays.
    /// </summary>
    private static void MoveTeam(
        Simulation simulation, Squad squad, string team, TeamFiringState state, Unit issuer, Vec2 centre, double radius, bool escalate)
    {
        var members = TeamMembers(simulation, squad, team);
        var targets = new SortedDictionary<string, FiringPositionSearch.Target>(StringComparer.Ordinal);
        foreach (var target in members.SelectMany(m => ShouldFireAt(simulation, m)))
        {
            targets.TryAdd(target.Enemy.Id, target);
        }

        var ableNow = members.Count(m => CanFireNow(simulation, m));
        var spots = FiringPositionSearch.TeamSpots(simulation, squad, members, [.. targets.Values], centre, radius, moreThan: ableNow);
        if (spots is null)
        {
            if (!escalate || squad.LeaderId is not { } squadLeaderId)
            {
                state.Exhausted = true;
            }
            else if (issuer.Id == squadLeaderId)
            {
                SquadLeaderSearch(simulation, squad, team, state, issuer);
            }
            else
            {
                var now = simulation.TimeSeconds;
                simulation.Log(new CannotFireReported(now, issuer.Id, squadLeaderId));
                state.SquadLeaderSearchAt = now + simulation.Config.SquadDoctrine.RelaySeconds +
                    Reaction(simulation, simulation.GetUnit(squadLeaderId));
            }

            return;
        }

        var order = new MoveTeamToFiringPosition(team, spots);
        state.Move = order;
        SquadSystem.Command(simulation, squad, issuer, order);
    }

    /// <summary>A team is moving while its move command is on its way to a member, or a member is on its way to its spot.</summary>
    private static bool Moving(Simulation simulation, Squad squad, MoveTeamToFiringPosition move) =>
        squad.Deliveries.Any(d => ReferenceEquals(d.Command, move) && move.Spots.ContainsKey(d.UnitId)) ||
        move.Spots.Keys.Any(id => simulation.GetUnit(id).FiringRoute is not null);

    private static bool TeamMoving(Simulation simulation, Squad squad, string unitId) =>
        squad.TeamFiring.TryGetValue(squad.Member(unitId).Team, out var state) && state.Move is { } move && Moving(simulation, squad, move);

    /// <summary>
    /// Fire superiority: the squad's rounds in the window at least the superiority factor times the enemy rounds its
    /// members heard (each enemy shot counted once). With no fire on either side the squad has it.
    /// </summary>
    private static bool FireSuperiority(Simulation simulation, Squad squad)
    {
        var rules = simulation.Config.SquadDoctrine.FiringPositions;
        var from = simulation.TimeSeconds - rules.SuperiorityWindowSeconds;
        var members = new SortedSet<string>(squad.Members.Select(m => m.UnitId), StringComparer.Ordinal);
        var ours = 0;
        var enemyRounds = new SortedDictionary<string, int>(StringComparer.Ordinal); // by time and shooter
        var heard = new SortedSet<string>(StringComparer.Ordinal);
        var log = simulation.EventLog;
        for (var i = log.Count - 1; i >= 0 && log[i].Time >= from - CombatQueries.TimeEpsilon; i--)
        {
            switch (log[i])
            {
                case ShotFired shot when members.Contains(shot.AgentId):
                    ours += shot.Rounds;
                    break;
                case ShotFired shot:
                    enemyRounds[ShotKey(shot.Time, shot.AgentId)] = shot.Rounds;
                    break;
                case ShotHeard sound when members.Contains(sound.AgentId) && !members.Contains(sound.ShooterId):
                    heard.Add(ShotKey(sound.Time, sound.ShooterId));
                    break;
                default:
                    break;
            }
        }

        var theirs = heard.Sum(key => enemyRounds.GetValueOrDefault(key, 1));
        return ours >= rules.SuperiorityFactor * theirs;
    }

    private static string ShotKey(double time, string shooterId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{time:R}|{shooterId}");

    /// <summary>The enemies the member should fire at: in its fire area once it has one, otherwise every one it knows of.</summary>
    private static List<FiringPositionSearch.Target> ShouldFireAt(Simulation simulation, Unit unit)
    {
        var here = unit.Position.Xy;
        return [.. unit.KnownEnemies
            .Where(e => unit.FireArea is not { } area || area.Covers(e.Value - here))
            .Select(e => new FiringPositionSearch.Target(simulation.GetUnit(e.Key), e.Value))
            .Where(t => t.Enemy.Health != Health.OutOfAction)];
    }

    /// <summary>
    /// True if the member could fire at the enemy where it stands: in range, in sight, its fire sector clear (as
    /// <see cref="FireSystem"/>, but whether it has spotted the enemy yet does not matter: one in sight soon is); a machine
    /// gunner also if it could put suppressive fire on it.
    /// </summary>
    private static bool CanFireAt(Simulation simulation, Unit unit, Unit enemy) =>
        (enemy.Health != Health.OutOfAction && unit.EyePosition.DistanceTo(enemy.AimPoint) <= unit.Combatant!.Weapon.MaxRangeMeters &&
            CombatQueries.SectorClear(simulation, unit, unit.Position.Xy, enemy.Position.Xy) && CombatQueries.Sees(simulation, unit, enemy)) ||
        (FireSystem.IsMachineGunner(simulation, unit) && FireSystem.Suppressible(simulation, unit, enemy));

    /// <summary>
    /// True if the member could fire at any enemy it knows of where it stands: the team leader's count of who is still
    /// firing, which a fire area a step behind the enemy picture must not skew.
    /// </summary>
    private static bool CanFireNow(Simulation simulation, Unit unit) =>
        unit.KnownEnemies.Keys.Any(id => CanFireAt(simulation, unit, simulation.GetUnit(id)));

    private static List<Unit> TeamMembers(Simulation simulation, Squad squad, string team) =>
        [.. squad.Members.Where(m => m.Team == team).Select(m => simulation.GetUnit(m.UnitId)).Where(u => u.Health != Health.OutOfAction)];

    private static TeamFiringState State(Squad squad, string team)
    {
        if (!squad.TeamFiring.TryGetValue(team, out var state))
        {
            state = new TeamFiringState();
            squad.TeamFiring[team] = state;
        }

        return state;
    }

    private static double Reaction(Simulation simulation, Unit unit) =>
        simulation.Config.SquadDoctrine.ReactionSeconds(unit.Combatant!.Experience) *
        simulation.Config.SquadDoctrine.DelayFactor(unit.IsSuppressed, unit.IsHeavilySuppressed);
}
