using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Sim.Units;

namespace Twin.Testing;

/// <summary>
/// Vocabulary for squad movement (backlog 0004, S2a): running until a squad has settled, and checking members against
/// their slots. A slot is where the simulation wants a member now (<see cref="Twin.Sim.Simulation.SlotOf"/>).
/// </summary>
public static class SquadMovementAssertions
{
    /// <summary>
    /// True when the squad has settled: its leader has no order and is idle, and every able member that follows the
    /// squad is idle too.
    /// </summary>
    public static bool SquadSettled(this ScenarioRun sim, string squadId)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var squad = sim.Squad(squadId);
        if (squad.LeaderId is not { } leaderId)
        {
            return false;
        }

        var leader = sim.Unit(leaderId);
        return leader.CurrentOrder is null && leader.Activity == UnitActivity.Idle &&
            squad.Members.Where(m => squad.IsFollowing(m.UnitId))
                .Select(m => sim.Unit(m.UnitId))
                .Where(u => u.Health is not (Twin.Sim.Combat.Health.OutOfAction or Twin.Sim.Combat.Health.Pinned))
                .All(u => u.Activity == UnitActivity.Idle);
    }

    /// <summary>Runs until the squad has settled (<see cref="SquadSettled"/>); fails with a diagnostic after <paramref name="timeout"/>.</summary>
    public static ScenarioRun RunUntilSquadSettled(this ScenarioRun sim, string squadId, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return sim.RunUntil(s => s.SquadSettled(squadId), timeout, $"squad {squadId} settled");
    }

    /// <summary><paramref name="unitId"/> has a slot and is within <paramref name="toleranceMeters"/> of it.</summary>
    public static void ShouldBeInSlot(this ScenarioRun sim, string unitId, double toleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var position = sim.Unit(unitId).Position.Xy;
        var slot = sim.Simulation.SlotOf(unitId) ?? throw new ScenarioAssertionException(
            $"Expected '{unitId}' to have a slot (to follow its squad), but it has none.{Environment.NewLine}{sim.Describe()}");
        if (position.DistanceTo(slot) > toleranceMeters)
        {
            throw new ScenarioAssertionException(
                $"Expected '{unitId}' within {toleranceMeters} m of its slot {slot}, but it is at {position}, " +
                $"{position.DistanceTo(slot):0.##} m away, at t={sim.TimeSeconds:0.##} s.{Environment.NewLine}{sim.Describe()}");
        }
    }

    /// <summary>Every member of the squad other than its leader is within <paramref name="toleranceMeters"/> of its slot.</summary>
    public static void ShouldAllBeInSlots(this ScenarioRun sim, string squadId, double toleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var squad = sim.Squad(squadId);
        foreach (var member in squad.Members.Where(m => m.UnitId != squad.LeaderId))
        {
            sim.ShouldBeInSlot(member.UnitId, toleranceMeters);
        }
    }

    /// <summary>
    /// Where a slot <paramref name="slot"/> lies when the leader is at <paramref name="leader"/> on a straight route
    /// heading <paramref name="heading"/> (any length): <c>along</c> metres ahead, <c>across</c> metres to the left.
    /// </summary>
    public static Vec2 OnStraightRoute(Vec2 leader, Vec2 heading, FormationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var forward = heading.Normalized();
        var left = new Vec2(-forward.Y, forward.X);
        return leader + (forward * slot.Along) + (left * slot.Across);
    }
}
