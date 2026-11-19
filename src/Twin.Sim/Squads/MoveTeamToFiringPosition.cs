using Twin.Sim.Geometry;
using Twin.Sim.Orders;

namespace Twin.Sim.Squads;

/// <summary>
/// A team leader's (or the squad leader's) command moving a team to a firing position (backlog 0005): each member of
/// <paramref name="Team"/> goes to its spot in <paramref name="Spots"/>, a line across the direction to the enemy. A squad
/// command, delivered with the squad's delays; logged as <see cref="Events.CommandIssued"/>.
/// </summary>
internal sealed record MoveTeamToFiringPosition(string Team, IReadOnlyDictionary<string, Vec2> Spots) : Order
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(MoveTeamToFiringPosition)} {Team}: " + string.Join(", ", Spots.OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => FormattableString.Invariant($"{s.Key} ({s.Value.X:0.#}, {s.Value.Y:0.#})")));
}
