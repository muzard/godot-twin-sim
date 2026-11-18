using Twin.Sim.Geometry;
using Twin.Sim.Orders;

namespace Twin.Sim.Squads;

/// <summary>
/// The RJ's squad command to deploy at the forest edge (backlog 0004, S2c). Given only by the simulation, never issued
/// from outside; it is logged as <see cref="Events.CommandIssued"/> with the text <c>DeployAtForestEdge</c>. It has no
/// fields, so every instance is equal as a record: code matches a command to its line by reference
/// (<see cref="EdgeLine.Command"/>), never by equality.
/// </summary>
internal sealed record DeployAtForestEdge : Order
{
    /// <inheritdoc />
    public override string ToString() => nameof(DeployAtForestEdge);
}

/// <summary>
/// A squad's open line at the forest edge (backlog 0004, S2c): each member's spot, which way is out of the forest, and
/// which members have acted on the deployment command and so stand to their spot.
/// </summary>
internal sealed class EdgeLine(
    DeployAtForestEdge command, Vec2 leaderAt, Vec2 outward, SortedDictionary<string, Vec2> spots, double halfWidth,
    string? leftEnd, string? rightEnd)
{
    /// <summary>The command that deploys the squad to this line.</summary>
    public DeployAtForestEdge Command { get; } = command;

    /// <summary>The leader's spot: its point on its route, 5 m inside the edge.</summary>
    public Vec2 LeaderAt { get; } = leaderAt;

    /// <summary>Out of the forest: perpendicular to the edge, a unit vector.</summary>
    public Vec2 Outward { get; } = outward;

    /// <summary>Along the edge to the left when facing out, a unit vector.</summary>
    public Vec2 Left => new(-Outward.Y, Outward.X);

    /// <summary>Every member's spot, the leader's included.</summary>
    public SortedDictionary<string, Vec2> Spots { get; } = spots;

    /// <summary>The farthest first-line spot from the leader: the permit to leave the forest covers this much more.</summary>
    public double HalfWidth { get; } = halfWidth;

    /// <summary>The member at the left end of the first line.</summary>
    public string? LeftEnd { get; } = leftEnd;

    /// <summary>The member at the right end of the first line.</summary>
    public string? RightEnd { get; } = rightEnd;

    /// <summary>The members that have acted on the command (the leader at once).</summary>
    public SortedSet<string> Acted { get; } = new(StringComparer.Ordinal);
}
