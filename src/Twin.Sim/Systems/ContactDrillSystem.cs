using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// The immediate action drill under fire (backlog 0003). An agent on return fire or free fire without a
/// <see cref="FireAt"/> order that comes under fire drops its order, goes prone and:
/// <list type="number">
/// <item>fires its snap shots after its reaction time (the firing is in <see cref="FireSystem"/>);</item>
/// <item>reports contact and, if an attacker can see it, moves to the nearest cover, even cover it cannot fire from;</item>
/// <item>moves on to a firing position, forward by default; if its fire sector is blocked by its own and there is no
/// covered firing position, to the nearest point with a clear sector;</item>
/// <item>returns fire: crouching at a covered firing position, prone elsewhere.</item>
/// </list>
/// It moves rushing (crouching) when not suppressed, crawling when lightly suppressed, and not at all when more heavily
/// suppressed or pinned. From its contact report it waits for orders; any new order ends the drill (a squad posture
/// command does not). In a squad the contact report is spoken to its squad mates like a spot report (backlog 0004 S5a2).
/// Runs after movement and before <see cref="FireSystem"/>.
/// </summary>
public sealed class ContactDrillSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var unit in simulation.Units)
        {
            if (unit.Drill is not { } drill || unit.Health is Health.OutOfAction or Health.Pinned)
            {
                continue;
            }

            switch (unit.DrillStage)
            {
                case ContactDrillStage.SnapShots when SnapShotsOver(simulation, unit, drill):
                    TakeCover(simulation, unit, drill);
                    var contact = ReportContact(simulation, unit);
                    simulation.Log(contact);
                    SquadAlerts.ContactReported(simulation, unit, contact.AttackerId, contact.AttackerPosition);
                    break;
                case ContactDrillStage.MovingToCover when Move(simulation, unit, drill, dtSeconds):
                    TakeFiringPosition(simulation, unit, drill);
                    break;
                case ContactDrillStage.MovingToFiringPosition when Move(simulation, unit, drill, dtSeconds):
                    ReturnFire(simulation, unit, drill);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Starts the drill for an agent that just came under fire, if it may return fire on its own and is not already in
    /// the drill: it drops its order and goes prone at once.
    /// </summary>
    internal static void Start(Simulation simulation, Unit agent, double now)
    {
        if (agent.Drill is not null || agent.Combatant is not { Rules: not RulesOfEngagement.HoldFire } ||
            agent.Health == Health.OutOfAction || agent.CurrentOrder is FireAt)
        {
            return;
        }

        agent.Drill = new ContactDrill(now, agent.CurrentOrder, agent.DesiredPosture);
        Answer(agent);
        agent.CurrentOrder = null;
        agent.CorridorProgress = null;
        agent.Velocity = Vec3.Zero;
        CombatQueries.SetActivity(agent, UnitActivity.Idle);
        agent.DesiredPosture = Posture.Prone;
        CombatQueries.ForceProne(simulation, agent);
        Enter(simulation, agent, ContactDrillStage.SnapShots);
    }

    /// <summary>Marks every current attacker as answered: its fire no longer starts the drill.</summary>
    internal static void Answer(Unit agent)
    {
        foreach (var attacker in agent.Attackers.Where(a => !agent.KnownAttackers.Contains(a)))
        {
            agent.KnownAttackers.Add(attacker);
        }
    }

    /// <summary>Ends the drill (a new order, or the agent is out of action): it no longer waits for orders.</summary>
    internal static void End(Simulation simulation, Unit agent)
    {
        if (agent.Drill is not { } drill)
        {
            return;
        }

        agent.Drill = null;
        agent.IsAwaitingOrders = false;
        agent.DesiredPosture = agent.PostureAfterDrill ?? drill.PostureBefore; // a squad posture command given during it wins
        agent.PostureAfterDrill = null;
        agent.Velocity = Vec3.Zero;
        CombatQueries.SetActivity(agent, UnitActivity.Idle);
        Enter(simulation, agent, ContactDrillStage.None);
    }

    private static bool SnapShotsOver(Simulation simulation, Unit unit, ContactDrill drill)
    {
        var rules = simulation.Config.Combat;
        var reaction = rules.Suppression.For(unit.Combatant!.Experience).ReactionSeconds;
        var snaps = rules.Doctrine.Drill.SnapShots * unit.Combatant.Weapon.SnapSecondsPerShot;
        return simulation.TimeSeconds >= drill.StartedAt + reaction + snaps - CombatQueries.TimeEpsilon;
    }

    private static ContactReported ReportContact(Simulation simulation, Unit unit)
    {
        unit.IsAwaitingOrders = true;
        var attacker = CombatQueries.Attackers(simulation, unit)
            .OrderBy(a => a.Position.Xy.DistanceTo(unit.Position.Xy)).ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault() ?? simulation.GetUnit(unit.Attackers[0]);
        return new ContactReported(simulation.TimeSeconds, unit.Id, attacker.Id, attacker.Position.Xy);
    }

    /// <summary>Stage 2: into cover if an attacker can see the agent, else on to stage 3.</summary>
    private static void TakeCover(Simulation simulation, Unit unit, ContactDrill drill)
    {
        var attackers = CombatQueries.Attackers(simulation, unit);
        if (attackers.Any(a => CombatQueries.Sees(simulation, a, unit)) &&
            CoverSearch.Cover(simulation, unit, attackers) is { } route && Leads(route))
        {
            Head(simulation, unit, drill, route, ContactDrillStage.MovingToCover, toFiringPosition: false);
            return;
        }

        TakeFiringPosition(simulation, unit, drill);
    }

    /// <summary>Stage 3: to a covered firing position, or if the agent's own block its sector, to a clear one; else stage 4.</summary>
    private static void TakeFiringPosition(Simulation simulation, Unit unit, ContactDrill drill)
    {
        var attackers = CombatQueries.Attackers(simulation, unit);
        if (CoverSearch.FiringPosition(simulation, unit, attackers) is { } covered)
        {
            if (Leads(covered))
            {
                Head(simulation, unit, drill, covered, ContactDrillStage.MovingToFiringPosition, toFiringPosition: true);
                return;
            }

            drill.AtFiringPosition = true; // already there
        }
        else if (!attackers.Any(a => CombatQueries.SectorClear(simulation, unit, unit.Position.Xy, a.Position.Xy)) &&
            CoverSearch.ClearSectorPosition(simulation, unit, attackers) is { } clear && Leads(clear))
        {
            Head(simulation, unit, drill, clear, ContactDrillStage.MovingToFiringPosition, toFiringPosition: false);
            return;
        }

        ReturnFire(simulation, unit, drill);
    }

    /// <summary>Stage 4: return fire, crouching at a covered firing position, prone elsewhere.</summary>
    private static void ReturnFire(Simulation simulation, Unit unit, ContactDrill drill)
    {
        unit.DesiredPosture = drill.AtFiringPosition ? Posture.Crouching : Posture.Prone;
        Enter(simulation, unit, ContactDrillStage.ReturningFire);
    }

    private static bool Leads(List<Vec2> route) => route.Count > 1;

    private static void Head(
        Simulation simulation, Unit unit, ContactDrill drill, List<Vec2> route, ContactDrillStage stage, bool toFiringPosition)
    {
        drill.Route = route;
        drill.Next = 1;
        drill.AtFiringPosition = false;
        drill.HeadingForFiringPosition = toFiringPosition;
        unit.DesiredPosture = Posture.Crouching;
        Enter(simulation, unit, stage);
    }

    /// <summary>
    /// Moves along the drill's route as the agent's suppression allows: crouching when free, crawling when lightly
    /// suppressed, not at all otherwise. True when it has arrived.
    /// </summary>
    private static bool Move(Simulation simulation, Unit unit, ContactDrill drill, double dt)
    {
        var mayMove = unit.IsSuppressed
            ? CombatQueries.IsLightlySuppressed(simulation, unit)
            : unit.Posture == Posture.Crouching && unit.PostureChangeUntil is null;
        if (!mayMove || unit.Profile is not { } profile || simulation.Terrain is not IWorldQuery world)
        {
            unit.Velocity = Vec3.Zero;
            return false;
        }

        var previous = unit.Position;
        var position = previous.Xy;
        var remaining = dt;
        while (remaining > 0 && drill.Next < drill.Route.Count)
        {
            var target = drill.Route[drill.Next];
            var speed = Speed(world, profile, position, target) * unit.SpeedFactor;
            var needed = position.DistanceTo(target) / speed;
            if (needed <= remaining)
            {
                position = target;
                remaining -= needed;
                drill.Next++;
            }
            else
            {
                position += (target - position).Normalized() * (speed * remaining);
                remaining = 0;
            }
        }

        unit.Position = position.WithZ(world.GroundHeight(position.X, position.Y));
        if (drill.Next >= drill.Route.Count)
        {
            unit.Velocity = Vec3.Zero;
            CombatQueries.SetActivity(unit, UnitActivity.Idle);
            drill.AtFiringPosition = drill.HeadingForFiringPosition;
            return true;
        }

        unit.Velocity = (unit.Position - previous) * (1 / dt);
        CombatQueries.SetActivity(unit, UnitActivity.Moving);
        return false;
    }

    /// <summary>Walking speed between two points: the mean base speed of their terrain times the slope factor.</summary>
    private static double Speed(IWorldQuery world, Movement.MovementProfile profile, Vec2 a, Vec2 b)
    {
        var sa = profile.BaseSpeed(world.ClassAt(a.X, a.Y));
        var sb = profile.BaseSpeed(world.ClassAt(b.X, b.Y));
        var baseSpeed = sa > 0 && sb > 0 ? (sa + sb) / 2 : Math.Max(sa, sb);
        var length = a.DistanceTo(b);
        var slope = length > 0 ? (world.GroundHeight(b.X, b.Y) - world.GroundHeight(a.X, a.Y)) / length : 0;
        return Math.Max(baseSpeed * profile.SlopeSpeedFactor(slope), 0.01);
    }

    private static void Enter(Simulation simulation, Unit unit, ContactDrillStage stage)
    {
        unit.DrillStage = stage;
        simulation.Log(new ContactDrillStageChanged(simulation.TimeSeconds, unit.Id, stage));
    }
}
