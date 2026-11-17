using Twin.Sim.Combat;
using Twin.Sim.Squads;

namespace Twin.Testing;

/// <summary>
/// A squad member as a test places it (backlog 0004): its place in the squad plus the experience the fighter is placed
/// with. In the simulation, experience belongs to the fighter (<see cref="Combatant.Experience"/>), not to the squad.
/// </summary>
/// <param name="UnitId">The agent.</param>
/// <param name="Role">Its role.</param>
/// <param name="Team">Its team.</param>
/// <param name="Experience">The fighter's experience; use <see cref="SpecSquad.Experienced"/> or
/// <see cref="SpecSquad.Inexperienced"/>.</param>
public sealed record SquadMemberSpec(string UnitId, SquadRole Role, string Team, Experience Experience)
{
    /// <summary>The member as the simulation's squad knows it.</summary>
    public SquadMember ToMember() => new(UnitId, Role, Team);
}

/// <summary>
/// A squad as a test places it with <see cref="Scenario.WithSquad"/>: the <see cref="SquadDefinition"/> plus each
/// member's experience.
/// </summary>
/// <param name="Id">The squad's id.</param>
/// <param name="Teams">The team names, in formation order.</param>
/// <param name="Members">The members, in squad order.</param>
public sealed record SquadSpec(string Id, IReadOnlyList<string> Teams, IReadOnlyList<SquadMemberSpec> Members)
{
    /// <summary>The definition the simulation forms the squad from.</summary>
    public SquadDefinition Definition => new(Id, Teams, Members.Select(m => m.ToMember()).ToList());
}
