using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;

namespace Twin.Sim.Systems;

/// <summary>
/// Watching and spotting (backlog 0004 S5a1). First every agent's watch direction is set: toward its nearest attacker in
/// the drill under fire; while it stands still, toward the last known position of the enemy it learned of last (moving
/// on ends that); the direction of its last <see cref="Orders.Watch"/> order until its next order; while it follows a
/// squad, its S2c observation sectors (<see cref="Unit.ObservationSectors"/>, backlog 0004 decision 20): it watches the
/// centre of each, the first one being its <see cref="Unit.WatchDirection"/>; otherwise its direction of movement, or
/// unchanged. Then every fighter watches every enemy it has not spotted yet: an
/// enemy in its field of view and visible builds up spotting progress <c>dt / t_spot</c> under the conditions of the
/// moment (<see cref="ObservationRules"/>) and is spotted at 1; a muzzle flash in the field of view spots its shooter at
/// once; leaving the field of view or the line of sight starts the progress again. A spotted enemy is known
/// (<see cref="SquadAlerts.Learn"/>). Runs after movement and the drill, before firing. Does nothing while
/// <see cref="ObservationRules.IsOff"/>; then only a <see cref="Orders.Watch"/> order turns an agent.
/// </summary>
public sealed class SpottingSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var rules = simulation.Config.Combat.Observation;
        if (rules.IsOff)
        {
            return;
        }

        Watch(simulation);

        foreach (var observer in simulation.Units)
        {
            if (observer.Combatant is not { } combatant || observer.Health == Health.OutOfAction)
            {
                continue;
            }

            foreach (var target in simulation.Units)
            {
                if (target == observer || target.Combatant?.Side is not { } side || side == combatant.Side ||
                    target.Health == Health.OutOfAction)
                {
                    observer.SpotProgress.Remove(target.Id);
                    continue;
                }

                if (!observer.Spotted.Contains(target.Id))
                {
                    Observe(simulation, rules, observer, combatant, target, dtSeconds);
                }
            }
        }
    }

    private static void Observe(
        Simulation simulation, ObservationRules rules, Unit observer, Combatant combatant, Unit target, double dt)
    {
        var toTarget = target.Position.Xy - observer.Position.Xy;
        var angle = observer.WatchesSectors
            ? observer.ObservationSectors.Min(s => AngleDegrees(s.Centre, toTarget)) // the best sector counts
            : AngleDegrees(observer.WatchDirection, toTarget);
        if (angle >= rules.FieldOfViewHalfAngleDegrees)
        {
            observer.SpotProgress.Remove(target.Id); // looked away: the time starts again
            return;
        }

        var combat = simulation.Config.Combat;
        var sight = CombatQueries.Sight(simulation, observer, target);
        var flashing = simulation.TimeSeconds < target.FlashUntil - CombatQueries.TimeEpsilon;
        if (flashing && sight.IsVisible(combat, muzzleFlash: true))
        {
            Spot(simulation, observer, target);
            return;
        }

        if (!sight.IsVisible(combat))
        {
            observer.SpotProgress.Remove(target.Id); // out of sight: the time starts again
            return;
        }

        var angleFactor = angle <= rules.FocusHalfAngleDegrees ? 1 : rules.FieldOfViewFactor;
        var distance = observer.EyePosition.DistanceTo(target.AimPoint);
        var seconds = rules.BaseSeconds(combatant.Experience) * (1 + (distance / rules.ReferenceDistanceMeters)) * angleFactor /
            combat.For(target.Posture).SizeFactor / sight.Transmission;
        var progress = observer.SpotProgress.GetValueOrDefault(target.Id) + (dt / seconds);
        if (progress >= 1 - 1e-9)
        {
            Spot(simulation, observer, target);
        }
        else
        {
            observer.SpotProgress[target.Id] = progress;
        }
    }

    private static void Spot(Simulation simulation, Unit observer, Unit target)
    {
        observer.SpotProgress.Remove(target.Id);
        observer.Spotted.Add(target.Id);
        var position = target.Position.Xy;
        simulation.Log(new EnemySpotted(simulation.TimeSeconds, observer.Id, target.Id, position));
        SquadAlerts.Learn(simulation, observer, target.Id, position, spotted: true);
    }

    /// <summary>The angle in degrees between two directions; 0 when either has no length.</summary>
    private static double AngleDegrees(Vec2 a, Vec2 b)
    {
        if (a.Length <= 1e-9 || b.Length <= 1e-9)
        {
            return 0;
        }

        return Math.Acos(Math.Clamp(a.Normalized().Dot(b.Normalized()), -1, 1)) * 180 / Math.PI;
    }

    private static void Watch(Simulation simulation)
    {
        foreach (var unit in simulation.Units)
        {
            unit.WatchesSectors = false;
            if (WatchToward(simulation, unit) is { } direction && direction.Length > 1e-9)
            {
                unit.WatchDirection = direction.Normalized();
            }
        }
    }

    private static Vec2? WatchToward(Simulation simulation, Unit unit)
    {
        var here = unit.Position.Xy;
        if (unit.Drill is not null && CombatQueries.Attackers(simulation, unit)
                .OrderBy(a => a.Position.Xy.DistanceTo(here)).ThenBy(a => a.Id, StringComparer.Ordinal)
                .FirstOrDefault() is { } attacker)
        {
            return attacker.Position.Xy - here;
        }

        var moving = unit.Velocity.Xy.Length > 1e-9;
        if (moving)
        {
            unit.WatchTarget = null; // it watches a known enemy until it moves on
        }
        else if (unit.WatchTarget is { } target)
        {
            return target - here;
        }

        if (unit.HoldsWatch)
        {
            return null; // ordered to watch this way
        }

        if (simulation.SquadOf(unit.Id) is { } squad && squad.IsFollowing(unit.Id) && unit.ObservationSectors.Count > 0)
        {
            unit.WatchesSectors = true;
            return unit.ObservationSectors[0].Centre;
        }

        return moving ? unit.Velocity.Xy : null;
    }
}

/// <summary>
/// Spot reports (backlog 0004 S5a1). A spotter's report falls due its command reaction time after the spotting. A report
/// is spoken (<see cref="ReportMethod.Voice"/>): every squad mate within <see cref="SquadDoctrine.VoiceRangeMeters"/> who
/// did not know of the enemy learns of it. While the leader does not know of it, the member nearest the leader of those
/// who know of it (the speaker itself, if nobody heard) repeats it <see cref="SquadDoctrine.RelaySeconds"/> later, again
/// and again, so a broken chain is tried again; a relay due when the leader knows is dropped, and one whose speaker is out
/// of action passes to the next nearest member who knows. Each step it also halts a squad whose leader knows of an enemy
/// the squad has not halted for (a successor who knew of it, for example). Contact reports from the drill under fire are
/// spoken the same way, with the class of the attacker's weapon (backlog 0004 S5a2), and the system keeps the leader's
/// <see cref="Squad.EnemyEstimate"/>. Runs after spotting, before firing.
/// </summary>
public sealed class SpotReportSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var now = simulation.TimeSeconds;
        foreach (var squad in simulation.Squads)
        {
            SquadAlerts.HaltForKnownEnemies(simulation, squad);
            bool IsDue(Squad.SpotReport r) => now >= r.Due - CombatQueries.TimeEpsilon;
            var due = squad.PendingReports.Where(IsDue).ToList();
            squad.PendingReports.RemoveAll(IsDue);
            foreach (var report in due)
            {
                Speak(simulation, squad, report);
            }

            Estimate(simulation, squad);
        }
    }

    /// <summary>The leader's enemy estimate, logged when it changes (backlog 0004 S5a2). Not kept without observation.</summary>
    private static void Estimate(Simulation simulation, Squad squad)
    {
        if (simulation.Config.Combat.Observation.IsOff)
        {
            return;
        }

        // While the leader's place is empty there is no estimate; a successor's estimate is what it knows.
        var leader = squad.LeaderId is { } leaderId && simulation.GetUnit(leaderId) is { Health: not Health.OutOfAction } unit ? unit : null;
        var estimate = leader is null ? EnemyEstimate.None : new EnemyEstimate(leader.KnownEnemies.Count, leader.KnowsHeavyMachineGun);
        if (estimate != squad.EnemyEstimate)
        {
            squad.EnemyEstimate = estimate;
            simulation.Log(new EnemyEstimateChanged(
                simulation.TimeSeconds, leader?.Id ?? squad.LeaderId ?? string.Empty, squad.Id, estimate.Fighters, estimate.HeavyMachineGun));
        }
    }

    private static void Speak(Simulation simulation, Squad squad, Squad.SpotReport report)
    {
        var now = simulation.TimeSeconds;
        if (report.IsRelay && Leader(simulation, squad) is { } knowing && knowing.KnownEnemies.ContainsKey(report.EnemyId))
        {
            return; // the leader knows: nothing left to pass on
        }

        var speaker = simulation.GetUnit(report.ReporterId);
        if (speaker.Health == Health.OutOfAction)
        {
            if (!report.IsRelay || NearestKnower(simulation, squad, report.EnemyId, speaker.Position.Xy) is not { } instead)
            {
                return; // a spotter out of action says nothing, and nobody else knows yet
            }

            speaker = instead;
        }

        speaker.Reported.Add(report.EnemyId);
        simulation.Log(new SpotReported(now, speaker.Id, report.EnemyId, report.EnemyPosition, ReportMethod.Voice));
        var range = simulation.Config.SquadDoctrine.VoiceRangeMeters;
        foreach (var hearer in squad.Members.Select(m => simulation.GetUnit(m.UnitId)))
        {
            if (hearer != speaker && hearer.Health != Health.OutOfAction && report.Class == WeaponClass.HeavyMachineGun &&
                hearer.Position.Xy.DistanceTo(speaker.Position.Xy) <= range)
            {
                hearer.KnowsHeavyMachineGun = true; // the report names the weapon (backlog 0004 S5a2)
            }

            if (hearer != speaker && hearer.Health != Health.OutOfAction && !hearer.KnownEnemies.ContainsKey(report.EnemyId) &&
                hearer.Position.Xy.DistanceTo(speaker.Position.Xy) <= range)
            {
                simulation.Log(new SpotReceived(now, hearer.Id, speaker.Id, report.EnemyId, report.EnemyPosition));
                SquadAlerts.Learn(simulation, hearer, report.EnemyId, report.EnemyPosition, spotted: false);
            }
        }

        var leader = Leader(simulation, squad);
        if (leader is not null && leader.KnownEnemies.ContainsKey(report.EnemyId) ||
            squad.PendingReports.Any(r => r.EnemyId == report.EnemyId))
        {
            return;
        }

        // While the leader's place is empty the speaker keeps repeating it where it is.
        if (NearestKnower(simulation, squad, report.EnemyId, leader?.Position.Xy ?? speaker.Position.Xy) is { } next)
        {
            squad.PendingReports.Add(new Squad.SpotReport(
                next.Id, report.EnemyId, report.EnemyPosition, report.Due + simulation.Config.SquadDoctrine.RelaySeconds, IsRelay: true,
                report.Class));
        }
    }

    /// <summary>The squad's leader if it has one in action, else null.</summary>
    private static Unit? Leader(Simulation simulation, Squad squad) =>
        squad.LeaderId is { } id && simulation.GetUnit(id) is { Health: not Health.OutOfAction } leader ? leader : null;

    /// <summary>Of the members in action who know of the enemy, the one nearest <paramref name="point"/>; null if none.</summary>
    private static Unit? NearestKnower(Simulation simulation, Squad squad, string enemyId, Vec2 point) =>
        squad.Members
            .Select(m => simulation.GetUnit(m.UnitId))
            .Where(u => u.Health != Health.OutOfAction && u.KnownEnemies.ContainsKey(enemyId))
            .OrderBy(u => u.Position.Xy.DistanceTo(point)).ThenBy(u => u.Id, StringComparer.Ordinal)
            .FirstOrDefault();
}

/// <summary>
/// What a squad does when its members learn of an enemy (backlog 0004 S5a1): the member stops and crouches, a spotter
/// reports it, and when the leader learns of an enemy it did not know of the whole squad halts at once, crouching, with
/// the leader's movement order suspended until <see cref="ResumeMovement"/>.
/// </summary>
internal static class SquadAlerts
{
    /// <summary>
    /// <paramref name="unit"/> knows of <paramref name="enemyId"/> at <paramref name="position"/> now. If it did not know
    /// of it before, it watches there, and in a squad it stops and crouches, reports it if it spotted it, and halts the
    /// squad if it leads it.
    /// </summary>
    public static void Learn(Simulation simulation, Unit unit, string enemyId, Vec2 position, bool spotted)
    {
        var isNew = !unit.KnownEnemies.ContainsKey(enemyId);
        unit.KnownEnemies[enemyId] = position;
        if (!isNew)
        {
            return;
        }

        unit.WatchTarget = position;
        if (simulation.SquadOf(unit.Id) is not { } squad)
        {
            return;
        }

        Alert(unit);
        if (spotted && !unit.Reported.Contains(enemyId))
        {
            var due = simulation.TimeSeconds + simulation.Config.SquadDoctrine.ReactionSeconds(unit.Combatant!.Experience);
            squad.PendingReports.Add(new Squad.SpotReport(unit.Id, enemyId, position, due, IsRelay: false));
        }

        if (squad.LeaderId == unit.Id)
        {
            Halt(simulation, squad, unit, enemyId);
        }
    }

    /// <summary>
    /// A member reported contact in the drill under fire (backlog 0004 S5a2): it knows of its attacker from now on, and the
    /// report is spoken at once as a spot report, with the class of the attacker's weapon, which it heard (when observation
    /// is modelled; with <see cref="ObservationRules.Off"/> nothing is reported).
    /// </summary>
    public static void ContactReported(Simulation simulation, Unit unit, string attackerId, Vec2 position)
    {
        if (simulation.Config.Combat.Observation.IsOff)
        {
            return;
        }

        var attacker = simulation.GetUnit(attackerId);
        var weaponClass = attacker.Combatant?.Weapon.Class;
        if (weaponClass == WeaponClass.HeavyMachineGun)
        {
            unit.KnowsHeavyMachineGun = true;
        }

        Learn(simulation, unit, attackerId, position, spotted: false);
        if (simulation.SquadOf(unit.Id) is { } squad && !unit.Reported.Contains(attackerId))
        {
            squad.PendingReports.Add(new Squad.SpotReport(unit.Id, attackerId, position, simulation.TimeSeconds, IsRelay: false, weaponClass));
        }
    }

    /// <summary>Halts the squad if its leader knows of an enemy the squad has not halted for (a successor who knew of it).</summary>
    public static void HaltForKnownEnemies(Simulation simulation, Squad squad)
    {
        if (squad.LeaderId is not { } leaderId || simulation.GetUnit(leaderId) is not { Health: not Health.OutOfAction } leader)
        {
            return;
        }

        foreach (var enemyId in leader.KnownEnemies.Keys.Where(e => !squad.HaltedFor.Contains(e)).ToList())
        {
            Halt(simulation, squad, leader, enemyId);
        }
    }

    /// <summary>
    /// The leader orders its halted squad on: it takes up its suspended movement order, or <paramref name="newOrder"/>,
    /// which it has already been given, instead; every member learns of the enemies the leader knows, stops watching them
    /// and takes back the posture it moved in before the halt. Those enemies do not halt the squad again.
    /// </summary>
    public static void Resume(Simulation simulation, Squad squad, Unit leader, Order? newOrder = null)
    {
        if (!squad.IsHalted)
        {
            return;
        }

        var now = simulation.TimeSeconds;
        squad.IsHalted = false;
        simulation.Log(new SquadResumed(now, leader.Id, squad.Id));
        if (newOrder is null && squad.SuspendedOrder is { } order)
        {
            CombatQueries.EndOrder(leader);
            leader.CurrentOrder = order;
        }

        squad.SuspendedOrder = null;
        foreach (var enemyId in leader.KnownEnemies.Keys)
        {
            squad.HaltedFor.Add(enemyId);
        }

        foreach (var member in squad.Members)
        {
            var unit = simulation.GetUnit(member.UnitId);
            if (unit.Health == Health.OutOfAction)
            {
                continue;
            }

            if (unit != leader)
            {
                foreach (var (enemyId, position) in leader.KnownEnemies.Where(k => !unit.KnownEnemies.ContainsKey(k.Key)))
                {
                    unit.KnownEnemies[enemyId] = position;
                    simulation.Log(new SpotReceived(now, unit.Id, leader.Id, enemyId, position));
                }
            }

            unit.IsAlerted = false;
            unit.WatchTarget = null;
            if (unit.PostureBeforeAlert is { } posture && unit.Drill is null)
            {
                CombatQueries.WantPosture(unit, posture);
            }

            unit.PostureBeforeAlert = null;
        }
    }

    /// <summary>The member stops where it is and crouches (unless the drill under fire decides its posture).</summary>
    private static void Alert(Unit unit)
    {
        if (unit.IsAlerted)
        {
            return;
        }

        unit.IsAlerted = true;
        // In the drill the drill decides the posture; the one to go back to is the posture from before the drill (S5a2).
        unit.PostureBeforeAlert ??= unit.Drill?.PostureBefore ?? unit.DesiredPosture;
        if (unit.CurrentOrder is null && unit.Drill is null)
        {
            unit.Velocity = Vec3.Zero;
            CombatQueries.SetActivity(unit, UnitActivity.Idle);
        }

        if (unit.Drill is null)
        {
            CombatQueries.WantPosture(unit, Posture.Crouching);
        }
    }

    private static void Halt(Simulation simulation, Squad squad, Unit leader, string enemyId)
    {
        if (!squad.HaltedFor.Add(enemyId) || squad.IsHalted)
        {
            return;
        }

        squad.IsHalted = true;
        simulation.Log(new SquadHalted(simulation.TimeSeconds, leader.Id, squad.Id, enemyId));
        if (leader.CurrentOrder is MoveTo or MoveThroughCorridor)
        {
            squad.SuspendedOrder = leader.CurrentOrder;
            CombatQueries.EndOrder(leader);
        }
        else if (leader.Drill?.SuspendedOrder is MoveTo or MoveThroughCorridor)
        {
            squad.SuspendedOrder = leader.Drill.SuspendedOrder; // the leader's drill dropped it; the halt keeps it (S5a2)
        }

        foreach (var member in squad.Members)
        {
            var unit = simulation.GetUnit(member.UnitId);
            if (unit.Health != Health.OutOfAction)
            {
                Alert(unit);
            }
        }
    }
}
