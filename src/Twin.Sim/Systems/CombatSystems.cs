using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Combat state at the start of a step (backlog 0002): ends the 60 s pin (the agent is then wounded) and suppression,
/// keeps pinned and suppressed agents prone, and carries out posture changes, which take the rules' change time.
/// Runs before movement, so an agent that may move again does so in the same step.
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

            if (unit.IsSuppressed && now >= unit.SuppressedUntil - CombatQueries.TimeEpsilon)
            {
                unit.IsSuppressed = false;
                simulation.Log(new SuppressionEnded(now, unit.Id));
            }

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
}

/// <summary>
/// Firing (backlog 0002). Each combatant that can shoot picks a target: the target of its <see cref="FireAt"/> order, or
/// under its rules of engagement the nearest visible agent that shot at it, or (free fire) the nearest visible enemy.
/// It fires one aimed shot per weapon interval at a visible target in range. Every shot is heard within the weapon's
/// sound range, shows a muzzle flash, suppresses the target, and may hit it: the first hit pins it, a second puts it out
/// of action. Units act in id order and every shot draws one number, so a run is deterministic.
/// </summary>
public sealed class FireSystem : ISimSystem
{
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
                shooter.PostureChangeUntil is not null || shooter.RoundsLeft <= 0 || now < shooter.NextShotTime - CombatQueries.TimeEpsilon)
            {
                continue;
            }

            if (ChooseTarget(simulation, shooter, combatant) is { } target)
            {
                Fire(simulation, shooter, combatant.Weapon, target, now);
            }
        }
    }

    private static Unit? ChooseTarget(Simulation simulation, Unit shooter, Combatant combatant)
    {
        if (shooter.CurrentOrder is FireAt order)
        {
            var ordered = simulation.GetUnit(order.TargetId);
            if (Engageable(simulation, shooter, ordered))
            {
                return ordered;
            }

            // Doctrine: the duty to return fire may beat an order whose target cannot be engaged now.
            return combatant.Rules != RulesOfEngagement.HoldFire && simulation.Config.Combat.Doctrine.ReturnFireOverridesFireAt
                ? Nearest(simulation, shooter, simulation.Units.Where(u => shooter.Attackers.Contains(u.Id)))
                : null;
        }

        if (combatant.Rules == RulesOfEngagement.HoldFire)
        {
            return null;
        }

        var attacker = Nearest(simulation, shooter, simulation.Units.Where(u => shooter.Attackers.Contains(u.Id)));
        if (attacker is not null || combatant.Rules != RulesOfEngagement.FreeFire)
        {
            return attacker;
        }

        return Nearest(simulation, shooter, simulation.Units);
    }

    private static Unit? Nearest(Simulation simulation, Unit shooter, IEnumerable<Unit> candidates) =>
        candidates
            .Where(u => Engageable(simulation, shooter, u))
            .OrderBy(u => shooter.EyePosition.DistanceTo(u.AimPoint))
            .ThenBy(u => u.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool Engageable(Simulation simulation, Unit shooter, Unit target) =>
        target != shooter && target.Combatant is { } other && other.Side != shooter.Combatant!.Side &&
        target.Health != Health.OutOfAction &&
        shooter.EyePosition.DistanceTo(target.AimPoint) <= shooter.Combatant.Weapon.MaxRangeMeters &&
        CombatQueries.Sees(simulation, shooter, target);

    private static void Fire(Simulation simulation, Unit shooter, WeaponProfile weapon, Unit target, double now)
    {
        var rules = simulation.Config.Combat;
        shooter.RoundsLeft--;
        shooter.NextShotTime = now + weapon.SecondsPerShot;
        shooter.FlashUntil = now + weapon.FlashSeconds;

        var sight = CombatQueries.Sight(simulation, shooter, target);
        var probability = HitModel.Probability(weapon, rules, shooter.EyePosition.DistanceTo(target.AimPoint),
            sight.Transmission, target.Posture, shooter.IsSuppressed, shooter.Health == Health.Wounded);
        var hit = HitModel.Roll(simulation.Random, probability);
        simulation.Log(new ShotFired(now, shooter.Id, target.Id, hit));

        foreach (var listener in simulation.Units)
        {
            if (listener != shooter && listener.Combatant is not null && listener.Health != Health.OutOfAction &&
                listener.Position.Xy.DistanceTo(shooter.Position.Xy) <= weapon.SoundRangeMeters)
            {
                simulation.Log(new ShotHeard(now, listener.Id, shooter.Id, shooter.Position.Xy));
            }
        }

        if (!target.Attackers.Contains(shooter.Id))
        {
            target.Attackers.Add(shooter.Id);
        }

        if (hit)
        {
            Hit(simulation, target, rules, now);
        }

        if (target.Health != Health.OutOfAction)
        {
            target.SuppressedUntil = now + rules.SuppressionSeconds;
            target.IsSuppressed = true;
            simulation.Log(new SuppressionStarted(now, target.Id, target.SuppressedUntil));
            CombatQueries.ForceProne(simulation, target);
        }

        CombatQueries.UpdateActivity(target);
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
            if (target.IsSuppressed)
            {
                target.IsSuppressed = false;
                simulation.Log(new SuppressionEnded(now, target.Id));
            }
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

    /// <summary>True if the observer sees the target, counting the target's muzzle flash.</summary>
    public static bool Sees(Simulation simulation, Unit observer, Unit target) =>
        Sight(simulation, observer, target).IsVisible(
            simulation.Config.Combat, muzzleFlash: simulation.TimeSeconds < target.FlashUntil - TimeEpsilon);

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
