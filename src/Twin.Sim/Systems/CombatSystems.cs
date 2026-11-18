using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Combat state at the start of a step (backlogs 0002 and 0003): ends the 60 s pin (the agent is then wounded), lets fire
/// pressure decay (it halves every half-life of the agent's experience) and ends suppression when the pressure falls
/// below a quarter of the threshold, keeps pinned and suppressed agents prone, and carries out posture changes, which
/// take the rules' change time. Runs before movement, so an agent that may move again does so in the same step.
/// </summary>
public sealed class CombatStatusSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var now = simulation.TimeSeconds;
        foreach (var unit in simulation.Units)
        {
            if (unit.CombatRules is not { } rules || unit.Health == Health.OutOfAction)
            {
                continue;
            }

            if (unit.Health == Health.Pinned && now >= unit.PinnedUntil - CombatQueries.TimeEpsilon)
            {
                unit.Health = Health.Wounded;
                simulation.Log(new HealthChanged(now, unit.Id, Health.Wounded));
            }

            Decay(simulation, unit, rules, dtSeconds);

            if (unit.Health == Health.Pinned || unit.IsSuppressed)
            {
                CombatQueries.ForceProne(simulation, unit);
            }
            else if (unit.PostureChangeUntil is { } until)
            {
                if (now >= until - CombatQueries.TimeEpsilon)
                {
                    unit.PostureChangeUntil = null;
                    if (unit.Posture != unit.DesiredPosture)
                    {
                        unit.Posture = unit.DesiredPosture;
                        simulation.Log(new PostureChanged(now, unit.Id, unit.Posture));
                    }
                }
            }
            else if (unit.Posture != unit.DesiredPosture)
            {
                unit.PostureChangeUntil = now + rules.PostureChangeSeconds;
            }

            CombatQueries.UpdateActivity(unit);
        }
    }

    private static void Decay(Simulation simulation, Unit unit, CombatRules rules, double dt)
    {
        if (unit.SuppressionPressure <= 0)
        {
            return;
        }

        var values = rules.Suppression.For(unit.Combatant!.Experience);
        unit.SuppressionPressure *= Math.Pow(2, -dt / values.PressureHalfLifeSeconds);
        var now = simulation.TimeSeconds;
        if (unit.IsSuppressed && unit.SuppressionPressure < rules.Suppression.ReleaseFraction * values.SuppressionThreshold)
        {
            unit.IsSuppressed = false;
            unit.IsHeavilySuppressed = false;
            simulation.Log(new SuppressionEnded(now, unit.Id));
        }
        else if (unit.IsHeavilySuppressed && unit.SuppressionPressure < rules.Suppression.HeavyFactor * values.SuppressionThreshold)
        {
            unit.IsHeavilySuppressed = false;
            simulation.Log(new SuppressionStarted(now, unit.Id, Heavy: false));
        }
    }
}

/// <summary>
/// Firing (backlogs 0002 and 0003). Each combatant that can shoot picks a target: the target of its <see cref="FireAt"/>
/// order; in the drill under fire, the nearest visible attacker (snap shots first, then aimed); otherwise, under its
/// rules of engagement, the nearest visible agent that shot at it, or (free fire) the nearest visible enemy. Only an enemy
/// the shooter has spotted itself is a target (backlog 0004 S5a1; while spotting is on, an ordered target too). It never
/// fires with an own fighter in its fire sector, nor while heavily suppressed, and fire without an order keeps a reserve
/// of rounds. Every shot is heard within the weapon's sound range, shows a muzzle flash, and adds fire pressure to its
/// target and to anyone its rounds pass close by; a round passing within the under-fire distance starts the drill of an
/// agent that may return fire. A shot may hit: the first hit pins the target, a second puts it out of action. Units act
/// in id order and every shot draws one number, so a run is deterministic.
/// </summary>
public sealed class FireSystem : ISimSystem
{
    /// <summary>Rounds passing farther than this many crack radii add less than 1e-4 of their pressure, so they are ignored.</summary>
    private const double PressureCutoffRadii = 10;

    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var now = simulation.TimeSeconds;
        foreach (var shooter in simulation.Units)
        {
            if (shooter.CurrentOrder is FireAt done && simulation.GetUnit(done.TargetId).Health == Health.OutOfAction)
            {
                CombatQueries.EndOrder(shooter); // the target is out of action: the order is done
            }

            if (shooter.Combatant is not { } combatant || shooter.Health is Health.OutOfAction or Health.Pinned ||
                shooter.IsHeavilySuppressed || shooter.PostureChangeUntil is not null || shooter.RoundsLeft <= 0 ||
                now < shooter.NextShotTime - CombatQueries.TimeEpsilon)
            {
                continue;
            }

            if (ChooseTarget(simulation, shooter, combatant) is { } choice)
            {
                Fire(simulation, shooter, combatant, choice.Target, choice.Mode, now);
            }
        }
    }

    private static (Unit Target, FireMode Mode)? ChooseTarget(Simulation simulation, Unit shooter, Combatant combatant)
    {
        if (shooter.CurrentOrder is FireAt order)
        {
            var ordered = simulation.GetUnit(order.TargetId);
            if (Engageable(simulation, shooter, ordered))
            {
                return (ordered, ModeFor(simulation, shooter, ordered, FireMode.Aimed));
            }

            // Doctrine: the duty to return fire may beat an order whose target cannot be engaged now.
            return combatant.Rules != RulesOfEngagement.HoldFire && simulation.Config.Combat.Doctrine.ReturnFireOverridesFireAt
                ? WithMode(simulation, shooter, Nearest(simulation, shooter, CombatQueries.Attackers(simulation, shooter)), FireMode.Aimed)
                : null;
        }

        if (combatant.Rules == RulesOfEngagement.HoldFire || InReserve(simulation, shooter, combatant))
        {
            return null;
        }

        if (shooter.Drill is { } drill)
        {
            return ChooseInDrill(simulation, shooter, combatant, drill);
        }

        var attacker = Nearest(simulation, shooter, CombatQueries.Attackers(simulation, shooter));
        if (attacker is not null || combatant.Rules != RulesOfEngagement.FreeFire)
        {
            return WithMode(simulation, shooter, attacker, FireMode.Aimed);
        }

        return WithMode(simulation, shooter, Nearest(simulation, shooter, simulation.Units), FireMode.Aimed);
    }

    private static (Unit Target, FireMode Mode)? ChooseInDrill(Simulation simulation, Unit shooter, Combatant combatant, ContactDrill drill)
    {
        var attackers = CombatQueries.Attackers(simulation, shooter);
        switch (shooter.DrillStage)
        {
            case ContactDrillStage.SnapShots:
                var reaction = simulation.Config.Combat.Suppression.For(combatant.Experience).ReactionSeconds;
                if (drill.SnapShotsFired >= simulation.Config.Combat.Doctrine.Drill.SnapShots ||
                    simulation.TimeSeconds < drill.StartedAt + reaction - CombatQueries.TimeEpsilon)
                {
                    return null;
                }

                return WithMode(simulation, shooter, Nearest(simulation, shooter, attackers), FireMode.Snap);
            case ContactDrillStage.ReturningFire:
                var target = Nearest(simulation, shooter, attackers) ??
                    (combatant.Rules == RulesOfEngagement.FreeFire ? Nearest(simulation, shooter, simulation.Units) : null);
                return WithMode(simulation, shooter, target, FireMode.Aimed);
            default:
                return null; // moving to cover or to a firing position: no fire on the way
        }
    }

    private static (Unit Target, FireMode Mode)? WithMode(Simulation simulation, Unit shooter, Unit? target, FireMode mode) =>
        target is null ? null : (target, ModeFor(simulation, shooter, target, mode));

    /// <summary>A target seen only by its muzzle flash gets snap shots, never aimed ones.</summary>
    private static FireMode ModeFor(Simulation simulation, Unit shooter, Unit target, FireMode wanted) =>
        CombatQueries.Observation(simulation, shooter, target) == ObservationQuality.MuzzleFlash ? FireMode.Snap : wanted;

    /// <summary>Fire without an order stops when the reserve is all that is left.</summary>
    private static bool InReserve(Simulation simulation, Unit shooter, Combatant combatant) =>
        shooter.RoundsLeft <= Reserve(simulation, combatant);

    /// <summary>Rounds kept in reserve: the doctrine's share of the rounds carried, rounded up.</summary>
    private static int Reserve(Simulation simulation, Combatant combatant) =>
        (int)Math.Ceiling((simulation.Config.Combat.Doctrine.Drill.ReserveFraction * combatant.Weapon.Rounds) - 1e-9);

    private static Unit? Nearest(Simulation simulation, Unit shooter, IEnumerable<Unit> candidates) =>
        candidates
            .Where(u => Engageable(simulation, shooter, u))
            .OrderBy(u => shooter.EyePosition.DistanceTo(u.AimPoint))
            .ThenBy(u => u.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool Engageable(Simulation simulation, Unit shooter, Unit target) =>
        target != shooter && target.Combatant is { } other && other.Side != shooter.Combatant!.Side &&
        target.Health != Health.OutOfAction &&
        CombatQueries.HasSpotted(simulation, shooter, target) &&
        shooter.EyePosition.DistanceTo(target.AimPoint) <= shooter.Combatant.Weapon.MaxRangeMeters &&
        CombatQueries.SectorClear(simulation, shooter, shooter.Position.Xy, target.Position.Xy) &&
        CombatQueries.Sees(simulation, shooter, target); // the sector first: it is far cheaper than a sight line

    private static void Fire(Simulation simulation, Unit shooter, Combatant combatant, Unit target, FireMode mode, double now)
    {
        var rules = simulation.Config.Combat;
        var weapon = combatant.Weapon;
        // Fire without an order never dips into the reserve, not even with the end of a burst.
        var available = shooter.CurrentOrder is FireAt ? shooter.RoundsLeft : shooter.RoundsLeft - Reserve(simulation, combatant);
        var rounds = Math.Min(weapon.RoundsPerBurst, available);
        shooter.RoundsLeft -= rounds;
        shooter.NextShotTime = now + (mode == FireMode.Snap ? weapon.SnapSecondsPerShot : weapon.SecondsPerShot);
        shooter.FlashUntil = now + weapon.FlashSeconds;
        if (shooter.Drill is { } drill && shooter.DrillStage == ContactDrillStage.SnapShots)
        {
            drill.SnapShotsFired++;
        }

        var sight = CombatQueries.Sight(simulation, shooter, target);
        var observation = sight.IsVisible(rules) ? ObservationQuality.Seen : ObservationQuality.MuzzleFlash;
        var distance = shooter.EyePosition.DistanceTo(target.AimPoint);
        var probability = HitModel.Probability(weapon, rules, distance, sight.Transmission, target.Posture,
            shooter.IsSuppressed, shooter.Health == Health.Wounded, combatant.Experience, mode);
        var hit = HitModel.Roll(simulation.Random, HitModel.BurstProbability(probability, rounds));
        simulation.Log(new ShotFired(now, shooter.Id, target.Id, hit, mode, rounds));

        foreach (var listener in simulation.Units)
        {
            if (listener != shooter && listener.Combatant is not null && listener.Health != Health.OutOfAction &&
                listener.Position.Xy.DistanceTo(shooter.Position.Xy) <= weapon.SoundRangeMeters)
            {
                simulation.Log(new ShotHeard(now, listener.Id, shooter.Id, shooter.Position.Xy));
            }
        }

        // Fire pressure: on the target at the round's miss distance, on everyone else at their distance from its path.
        var miss = SuppressionModel.MissDistance(rules, distance, mode, combatant.Experience, observation);
        var targetPressure = rounds * SuppressionModel.RoundPressure(weapon, rules, miss, sight.Transmission, target.Posture);
        PassBystanders(simulation, shooter, target, weapon, rounds, miss, now);

        if (hit)
        {
            Hit(simulation, target, rules, now);
        }

        if (target.Health != Health.OutOfAction)
        {
            CombatQueries.AddPressure(simulation, target, targetPressure);
            UnderFire(simulation, target, shooter, now);
        }

        CombatQueries.UpdateActivity(target);
    }

    private static void PassBystanders(Simulation simulation, Unit shooter, Unit target, WeaponProfile weapon, int rounds, double miss, double now)
    {
        var rules = simulation.Config.Combat;
        var cutoff = Math.Max(rules.Suppression.UnderFireMeters, PressureCutoffRadii * rules.Suppression.CrackRadiusMeters);
        foreach (var other in simulation.Units)
        {
            // Own rounds have no effect (decision of 2026-11-17).
            if (other == shooter || other == target || other.Combatant is not { } combatant ||
                combatant.Side == shooter.Combatant!.Side || other.Health == Health.OutOfAction ||
                SuppressionModel.MissDistanceBeside(shooter.Position.Xy, target.Position.Xy, other.Position.Xy, miss,
                    weapon.MaxRangeMeters) is not { } passing || passing > cutoff)
            {
                continue;
            }

            var line = CombatQueries.Sight(simulation, shooter, other);
            if (!line.Clear)
            {
                continue; // ground or a building between: the rounds do not reach it
            }

            CombatQueries.AddPressure(simulation, other,
                rounds * SuppressionModel.RoundPressure(weapon, rules, passing, line.Transmission, other.Posture));
            if (passing <= rules.Suppression.UnderFireMeters)
            {
                UnderFire(simulation, other, shooter, now);
            }

            CombatQueries.UpdateActivity(other);
        }
    }

    /// <summary>
    /// The agent is under fire: the shooter becomes an attacker (the duty to return fire). Fire from an attacker not yet
    /// answered by a drill or an order may start the drill; so a leader's order given under fire stands.
    /// </summary>
    private static void UnderFire(Simulation simulation, Unit agent, Unit shooter, double now)
    {
        if (!agent.Attackers.Contains(shooter.Id))
        {
            agent.Attackers.Add(shooter.Id);
        }

        if (!agent.KnownAttackers.Contains(shooter.Id))
        {
            ContactDrillSystem.Start(simulation, agent, now);
        }
    }

    private static void Hit(Simulation simulation, Unit target, CombatRules rules, double now)
    {
        if (target.Health == Health.Unhurt)
        {
            target.Health = Health.Pinned;
            target.PinnedUntil = now + rules.PinnedSeconds;
        }
        else
        {
            target.Health = Health.OutOfAction;
            target.CurrentOrder = null;
            target.CorridorProgress = null;
            target.Velocity = Vec3.Zero;
            target.SuppressionPressure = 0;
            target.IsHeavilySuppressed = false;
            if (target.IsSuppressed)
            {
                target.IsSuppressed = false;
                simulation.Log(new SuppressionEnded(now, target.Id));
            }

            ContactDrillSystem.End(simulation, target);
        }

        simulation.Log(new HealthChanged(now, target.Id, target.Health));
        simulation.Log(new CasualtyOccurred(now, target.Id, target.Position.Xy, target.Health));
        CombatQueries.ForceProne(simulation, target);
    }
}

/// <summary>Combat helpers shared by the combat systems and <see cref="Simulation.CanSee"/>.</summary>
internal static class CombatQueries
{
    /// <summary>Tolerance when comparing simulated times computed in different ways.</summary>
    public const double TimeEpsilon = 1e-9;

    private static readonly UnitActivity[] CombatActivities =
        [UnitActivity.Suppressed, UnitActivity.Pinned, UnitActivity.OutOfAction, UnitActivity.ChangingPosture];

    /// <summary>The sight line from the observer's eye to the target's aim point.</summary>
    public static SightLine Sight(Simulation simulation, Unit observer, Unit target) =>
        simulation.Terrain is IWorldQuery world
            ? LineOfSight.Between(world, observer.EyePosition, target.AimPoint, simulation.Config.Combat)
            : new SightLine(false, 0);

    /// <summary>
    /// True if the shooter has spotted the target itself (backlog 0004 S5a1), the only enemies it fires at; always true
    /// while spotting is off (<see cref="ObservationRules.Off"/>).
    /// </summary>
    public static bool HasSpotted(Simulation simulation, Unit shooter, Unit target) =>
        simulation.Config.Combat.Observation.IsOff || shooter.Spotted.Contains(target.Id);

    /// <summary>
    /// True if the agent knows of the enemy (backlog 0004 S5a1): spotted or reported; while spotting is off, if it sees it.
    /// </summary>
    public static bool Knows(Simulation simulation, Unit agent, Unit enemy) =>
        simulation.Config.Combat.Observation.IsOff ? Sees(simulation, agent, enemy) : agent.KnownEnemies.ContainsKey(enemy.Id);

    /// <summary>True if the observer sees the target, counting the target's muzzle flash.</summary>
    public static bool Sees(Simulation simulation, Unit observer, Unit target) =>
        Sight(simulation, observer, target).IsVisible(
            simulation.Config.Combat, muzzleFlash: simulation.TimeSeconds < target.FlashUntil - TimeEpsilon);

    /// <summary>How well the observer sees the target: <see cref="ObservationQuality.MuzzleFlash"/> if only by its flash.</summary>
    public static ObservationQuality Observation(Simulation simulation, Unit observer, Unit target) =>
        Sight(simulation, observer, target).IsVisible(simulation.Config.Combat) ? ObservationQuality.Seen : ObservationQuality.MuzzleFlash;

    /// <summary>The agents that have shot at the unit and are not out of action, in the order they first did.</summary>
    public static List<Unit> Attackers(Simulation simulation, Unit unit) =>
        unit.Attackers.Select(simulation.GetUnit).Where(u => u.Health != Health.OutOfAction).ToList();

    /// <summary>
    /// True if no other agent of the shooter's side is in its fire sector from <paramref name="from"/> towards
    /// <paramref name="target"/>: within the doctrine's half angle of that direction, in front of it, within the weapon's
    /// range (backlog 0003). Exactly at the half angle counts as outside.
    /// </summary>
    public static bool SectorClear(Simulation simulation, Unit shooter, Vec2 from, Vec2 target)
    {
        var aim = target - from;
        if (aim.Length == 0 || shooter.Combatant is not { } combatant)
        {
            return true;
        }

        var direction = aim.Normalized();
        var limit = Math.Cos(simulation.Config.Combat.Doctrine.Drill.FireSectorHalfAngleDegrees * Math.PI / 180);
        foreach (var other in simulation.Units)
        {
            if (other == shooter || other.Combatant?.Side != combatant.Side)
            {
                continue;
            }

            var toOther = other.Position.Xy - from;
            var length = toOther.Length;
            if (length > 0 && length <= combatant.Weapon.MaxRangeMeters && direction.Dot(toOther * (1 / length)) > limit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds fire pressure to the agent. It is suppressed (and goes prone) when the pressure reaches its threshold τ, and
    /// heavily suppressed from the heavy factor times τ; each change is logged as <see cref="SuppressionStarted"/>.
    /// </summary>
    public static void AddPressure(Simulation simulation, Unit agent, double pressure)
    {
        if (pressure <= 0 || agent.CombatRules is not { } rules || agent.Combatant is not { } combatant)
        {
            return;
        }

        agent.SuppressionPressure += pressure;
        var threshold = rules.Suppression.For(combatant.Experience).SuppressionThreshold;
        var heavy = agent.SuppressionPressure >= rules.Suppression.HeavyFactor * threshold;
        if (!agent.IsSuppressed && agent.SuppressionPressure >= threshold)
        {
            agent.IsSuppressed = true;
            agent.IsHeavilySuppressed = heavy;
            simulation.Log(new SuppressionStarted(simulation.TimeSeconds, agent.Id, heavy));
            ForceProne(simulation, agent);
        }
        else if (agent.IsSuppressed && heavy && !agent.IsHeavilySuppressed)
        {
            agent.IsHeavilySuppressed = true;
            simulation.Log(new SuppressionStarted(simulation.TimeSeconds, agent.Id, Heavy: true));
        }
    }

    /// <summary>True while the agent is suppressed below the light factor times its threshold: in the drill it may crawl.</summary>
    public static bool IsLightlySuppressed(Simulation simulation, Unit agent) =>
        agent.IsSuppressed && !agent.IsHeavilySuppressed && agent.Combatant is { } combatant &&
        agent.SuppressionPressure < simulation.Config.Combat.Suppression.LightFactor *
            simulation.Config.Combat.Suppression.For(combatant.Experience).SuppressionThreshold;

    /// <summary>
    /// Sets what the unit is doing for its order. While a combat state shows instead, the activity is remembered and
    /// shown when the combat state ends.
    /// </summary>
    public static void SetActivity(Unit unit, UnitActivity activity)
    {
        if (CombatActivities.Contains(unit.Activity))
        {
            unit.ActivityBeforeCombat = activity;
        }
        else
        {
            unit.Activity = activity;
        }
    }

    /// <summary>
    /// Ends the unit's current order: it stops (zero velocity) and is idle, or returns to idle once a combat state
    /// (suppressed, pinned, changing posture) ends.
    /// </summary>
    public static void EndOrder(Unit unit)
    {
        unit.CurrentOrder = null;
        unit.Velocity = Vec3.Zero;
        if (CombatActivities.Contains(unit.Activity))
        {
            unit.ActivityBeforeCombat = UnitActivity.Idle;
        }
        else
        {
            unit.Activity = UnitActivity.Idle;
        }
    }

    /// <summary>
    /// Makes <paramref name="posture"/> the unit's goal; <see cref="CombatStatusSystem"/> carries out the change. A change
    /// in progress restarts toward the new posture.
    /// </summary>
    public static void WantPosture(Unit unit, Posture posture)
    {
        if (unit.DesiredPosture != posture)
        {
            unit.DesiredPosture = posture;
            unit.PostureChangeUntil = null;
        }
    }

    /// <summary>Drops the unit prone at once, cancelling any posture change.</summary>
    public static void ForceProne(Simulation simulation, Unit unit)
    {
        unit.PostureChangeUntil = null;
        if (unit.Posture != Posture.Prone)
        {
            unit.Posture = Posture.Prone;
            simulation.Log(new PostureChanged(simulation.TimeSeconds, unit.Id, Posture.Prone));
        }
    }

    /// <summary>Shows the strongest combat state as the activity, and restores the earlier activity when none is left.</summary>
    public static void UpdateActivity(Unit unit)
    {
        UnitActivity? combat = unit.Health switch
        {
            Health.OutOfAction => UnitActivity.OutOfAction,
            Health.Pinned => UnitActivity.Pinned,
            _ when unit.IsSuppressed => UnitActivity.Suppressed,
            _ when unit.PostureChangeUntil is not null => UnitActivity.ChangingPosture,
            _ => null,
        };

        var inCombatActivity = CombatActivities.Contains(unit.Activity);
        if (combat is { } activity)
        {
            if (!inCombatActivity)
            {
                unit.ActivityBeforeCombat = unit.Activity;
            }

            unit.Activity = activity;
            unit.Velocity = Vec3.Zero;
        }
        else if (inCombatActivity)
        {
            unit.Activity = unit.ActivityBeforeCombat;
        }
    }
}
