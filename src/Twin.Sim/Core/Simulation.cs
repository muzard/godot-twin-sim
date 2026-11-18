using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Systems;
using Twin.Sim.Terrain;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim;

/// <summary>
/// The digital twin's simulation: world state plus the fixed-order systems that advance it.
/// Time only moves when <see cref="Step"/> is called, so callers (tests, the Godot client,
/// the headless runner) control time explicitly.
/// </summary>
public sealed class Simulation
{
    private readonly SortedDictionary<string, Unit> _units = new(StringComparer.Ordinal);
    private readonly List<(string UnitId, Order Order)> _pendingOrders = [];
    private readonly IReadOnlyList<ISimSystem> _systems;
    private readonly List<SimEvent> _events = [];
    private readonly SortedDictionary<string, AgentSnapshot> _logged = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Squad> _squads = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Squad> _squadOfUnit = new(StringComparer.Ordinal);
    private int _lastCommandId;

    /// <summary>Creates a simulation over the given terrain.</summary>
    public Simulation(ITerrain terrain, SimulationConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        Terrain = terrain;
        Config = config ?? new SimulationConfig();
        Random = new DeterministicRandom(Config.Seed);

        // System order is part of the simulation's behaviour. Change it deliberately.
        _systems =
        [
            new CombatStatusSystem(), new SquadFollowSystem(), new SquadCohesionSystem(), new MovementSystem(),
            new CorridorMovementSystem(), new ContactDrillSystem(), new FireSystem(), new SquadSystem(),
        ];
    }

    /// <summary>The ground surface.</summary>
    public ITerrain Terrain { get; }

    /// <summary>Run parameters.</summary>
    public SimulationConfig Config { get; }

    /// <summary>The only source of randomness systems may use.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Number of ticks completed.</summary>
    public long Tick { get; private set; }

    /// <summary>Simulated time in seconds.</summary>
    public double TimeSeconds => Tick * Config.TickSeconds;

    /// <summary>All units, ordered by id (stable iteration order is required for determinism).</summary>
    public IReadOnlyCollection<Unit> Units => _units.Values;

    /// <summary>Everything that has happened so far, in order (spec v0.1 event log; see <see cref="EventLog"/>).</summary>
    public IReadOnlyList<SimEvent> EventLog => _events;

    /// <summary>All squads, ordered by id (backlog 0004).</summary>
    public IReadOnlyCollection<Squad> Squads => _squads.Values;

    /// <summary>Places a ground unit on the map. Its height is taken from the terrain.</summary>
    public Unit AddUnit(string id, Vec2 position, double maxSpeedMps)
    {
        CheckPlacement(id, position);
        return Place(new Unit(id, position.WithZ(Terrain.HeightAt(position)), maxSpeedMps));
    }

    /// <summary>
    /// Places an agent that moves by a movement profile: terrain-dependent speeds, slope limit and eye height.
    /// Only such agents can take <see cref="MoveThroughCorridor"/> orders.
    /// </summary>
    public Unit AddUnit(string id, Vec2 position, MovementProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        CheckPlacement(id, position);
        return Place(new Unit(id, position.WithZ(Terrain.HeightAt(position)), profile));
    }

    /// <summary>
    /// Places a fighter (backlog 0002): an agent with a movement profile, a side, rules of engagement and a weapon,
    /// standing and unhurt. Line of sight and hits follow <see cref="SimulationConfig.Combat"/>.
    /// </summary>
    public Unit AddUnit(string id, Vec2 position, MovementProfile profile, Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(combatant);
        CheckPlacement(id, position);
        return Place(new Unit(id, position.WithZ(Terrain.HeightAt(position)), profile, combatant, Config.Combat));
    }

    /// <summary>
    /// Forms a squad from fighters already on the map (backlog 0004). Moves no one. Logs <see cref="SquadFormed"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The squad is invalid (see <see cref="SquadDefinition"/>); nothing is changed.</exception>
    public Squad AddSquad(SquadDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (leaderId, teamLeaderIds) = ValidateSquad(definition);
        var squad = new Squad(definition, leaderId, teamLeaderIds);
        _squads.Add(squad.Id, squad);
        foreach (var member in squad.Members)
        {
            _squadOfUnit.Add(member.UnitId, squad);
        }

        Log(new SquadFormed(TimeSeconds, leaderId, squad.Id, squad.Teams, teamLeaderIds, squad.Formation));
        return squad;
    }

    /// <summary>Gets a squad by id.</summary>
    public Squad GetSquad(string id) =>
        _squads.TryGetValue(id, out var squad) ? squad : throw new KeyNotFoundException($"No squad with id '{id}'.");

    /// <summary>The squad <paramref name="unitId"/> belongs to, or null if it is in none.</summary>
    public Squad? SquadOf(string unitId) => _squadOfUnit.GetValueOrDefault(unitId);

    /// <summary>
    /// Where <paramref name="unitId"/>'s slot in its squad's formation is now (backlog 0004, S2a): for a squad leader its
    /// own position. Null for an agent outside a squad, a member detached by a direct order, and every member before its
    /// leader's first movement order.
    /// </summary>
    public Vec2? SlotOf(string unitId) =>
        SquadOf(unitId) is { } squad && SquadSlots.Compute(this, squad) is { } slots && slots.TryGetValue(unitId, out var slot)
            ? slot
            : null;

    /// <summary>
    /// True if <paramref name="observerId"/> sees <paramref name="targetId"/> now: the line from its eyes to the target's
    /// aim point is clear and the forest transmission is high enough, lower while the target's muzzle flash lasts.
    /// </summary>
    public bool CanSee(string observerId, string targetId) => CombatQueries.Sees(this, GetUnit(observerId), GetUnit(targetId));

    /// <summary>
    /// The nearest cover for an agent against the given attackers (backlog 0003): a point within the drill's search
    /// distance, reachable without crossing a hard boundary of its corridor order, where no attacker can see it lying
    /// prone. Null if there is none.
    /// </summary>
    public Vec2? FindCover(string agentId, params string[] attackerIds) =>
        Destination(CoverSearch.Cover(this, GetUnit(agentId), Fighters(attackerIds)));

    /// <summary>
    /// The nearest firing position for an agent against the given attackers (backlog 0003), forward first: cover from
    /// which it sees an attacker when crouching, in range, with a clear fire sector. Null if there is none.
    /// </summary>
    public Vec2? FindFiringPosition(string agentId, params string[] attackerIds) =>
        Destination(CoverSearch.FiringPosition(this, GetUnit(agentId), Fighters(attackerIds)));

    /// <summary>
    /// Every agent's position, activity, health, posture, suppression and drill stage now, ordered by id: what a live
    /// viewer shows.
    /// </summary>
    public WorldSnapshot Snapshot() =>
        new(TimeSeconds, _units.Values
            .Select(u => new AgentSnapshot(u.Id, u.Position, u.Activity, u.Health, u.Posture, u.IsSuppressed,
                u.IsHeavilySuppressed, u.DrillStage, u.IsAwaitingOrders))
            .ToList())
        {
            Squads = _squads.Values.Select(s => new SquadSnapshot(s.Id, s.LeaderId, [.. s.TeamLeaderIds], s.Formation)).ToList(),
        };

    /// <summary>Gets a unit by id.</summary>
    public Unit GetUnit(string id) =>
        _units.TryGetValue(id, out var unit) ? unit : throw new KeyNotFoundException($"No unit with id '{id}'.");

    /// <summary>
    /// Issues an order. It is validated immediately and takes effect at the start of the next tick.
    /// A new order replaces the unit's current order, except <see cref="SetPosture"/>, <see cref="SetSquadPosture"/> and
    /// <see cref="CeaseFire"/>, which keep it (<see cref="CeaseFire"/> ends only a <see cref="FireAt"/>). <see cref="Stop"/>
    /// ends the current order and leaves the unit idle where it is, with no current order. Squad orders go only to a
    /// squad's leader. Units out of action take no orders.
    /// </summary>
    /// <exception cref="OrderRejectedException">The unit does not exist or the order is invalid.</exception>
    public void Issue(string unitId, Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!_units.ContainsKey(unitId))
        {
            throw new OrderRejectedException($"No unit with id '{unitId}'.");
        }

        if (_units[unitId].Health == Health.OutOfAction)
        {
            throw new OrderRejectedException($"Unit '{unitId}' is out of action.");
        }

        if (order is FireAt fireAt)
        {
            ValidateFireAt(_units[unitId], fireAt);
        }

        if (order is SetPosture or CeaseFire && _units[unitId].Combatant is null)
        {
            throw new OrderRejectedException($"Unit '{unitId}' is not a fighter.");
        }

        if (order is SetSquadPosture or SetSquadFormation && SquadOf(unitId)?.LeaderId != unitId)
        {
            throw new OrderRejectedException($"Unit '{unitId}' does not lead a squad.");
        }

        if (order is SetSquadFormation { Formation: var formation } && !Enum.IsDefined(formation))
        {
            throw new OrderRejectedException($"Unknown formation {formation}.");
        }

        if (order is Rejoin && (SquadOf(unitId) is not { } own || own.LeaderId == unitId))
        {
            throw new OrderRejectedException($"Unit '{unitId}' is not a squad member who could rejoin its squad.");
        }

        if (order is MoveTo move && !Terrain.Contains(move.Destination))
        {
            throw new OrderRejectedException($"Destination {move.Destination} is outside the map.");
        }

        if (order is MoveThroughCorridor corridorOrder)
        {
            ValidateCorridorOrder(_units[unitId], corridorOrder);
        }

        _pendingOrders.Add((unitId, order));
    }

    /// <summary>Advances the simulation by exactly one tick.</summary>
    public void Step()
    {
        Tick++;
        foreach (var (unitId, order) in _pendingOrders)
        {
            var unit = _units[unitId];
            if (order is not SetPosture && !(order is Rejoin && SquadOf(unitId)?.IsFollowing(unitId) == true))
            {
                ContactDrillSystem.End(this, unit);    // any new order ends the drill under fire
                ContactDrillSystem.Answer(unit);       // and the leader's order stands against the fire it answers
            }

            if (order is not Rejoin && SquadOf(unitId) is { } detachedFrom && detachedFrom.LeaderId != unitId)
            {
                detachedFrom.Detach(unitId); // any order straight to a member takes it out of the squad's movement
            }

            switch (order)
            {
                case SetPosture posture:
                    CombatQueries.WantPosture(unit, posture.Posture); // keeps the current order
                    break;
                case CeaseFire:
                    if (unit.CurrentOrder is FireAt)
                    {
                        CombatQueries.EndOrder(unit);
                    }

                    break;
                case Stop:
                    CombatQueries.EndOrder(unit); // zero velocity, idle, no current order
                    break;
                case SetSquadPosture or SetSquadFormation when SquadOf(unitId) is { } squad && squad.LeaderId == unitId:
                    SquadSystem.Command(this, squad, unit, order); // keeps the current order
                    break;
                case SetSquadPosture or SetSquadFormation:
                    break; // the unit lost command of its squad after the order was issued
                case Rejoin when SquadOf(unitId) is { } squad && !squad.IsFollowing(unitId):
                    CombatQueries.EndOrder(unit);
                    squad.Rejoin(unitId);
                    break;
                case Rejoin:
                    break; // already following
                default:
                    CombatQueries.EndOrder(unit);
                    unit.CurrentOrder = order;
                    break;
            }
        }

        _pendingOrders.Clear();
        foreach (var system in _systems)
        {
            system.Update(this, Config.TickSeconds);
        }

        LogStateChanges();
    }

    /// <summary>Adds an event to the log. Systems call this; the time is the current simulated time.</summary>
    internal void Log(SimEvent e) => _events.Add(e);

    /// <summary>A new squad command id, unique in the run.</summary>
    internal int NextCommandId() => ++_lastCommandId;

    private (string LeaderId, List<string> TeamLeaderIds) ValidateSquad(SquadDefinition definition)
    {
        static ArgumentException Invalid(SquadDefinition d, string why) => new($"Squad '{d.Id}': {why}", nameof(definition));

        if (string.IsNullOrEmpty(definition.Id))
        {
            throw new ArgumentException("A squad needs an id.", nameof(definition));
        }

        if (_squads.ContainsKey(definition.Id))
        {
            throw Invalid(definition, "a squad with this id already exists.");
        }

        if (definition.Teams is not { Count: > 0 } || definition.Members is not { Count: > 0 })
        {
            throw Invalid(definition, "a squad needs teams and members.");
        }

        if (definition.Teams.Distinct(StringComparer.Ordinal).Count() != definition.Teams.Count)
        {
            throw Invalid(definition, "team names must be unique.");
        }

        var seen = new SortedSet<string>(StringComparer.Ordinal);
        Side? side = null;
        foreach (var member in definition.Members)
        {
            if (member is null)
            {
                throw Invalid(definition, "a member is missing (null).");
            }

            if (!seen.Add(member.UnitId))
            {
                throw Invalid(definition, $"'{member.UnitId}' is listed twice.");
            }

            if (!_units.TryGetValue(member.UnitId, out var unit))
            {
                throw Invalid(definition, $"no unit with id '{member.UnitId}'.");
            }

            if (unit.Combatant is not { } combatant)
            {
                throw Invalid(definition, $"'{member.UnitId}' is not a fighter.");
            }

            if (unit.Health == Health.OutOfAction)
            {
                throw Invalid(definition, $"'{member.UnitId}' is out of action.");
            }

            side ??= combatant.Side;
            if (combatant.Side != side)
            {
                throw Invalid(definition, "all members must be on one side.");
            }

            if (_squadOfUnit.TryGetValue(member.UnitId, out var other))
            {
                throw Invalid(definition, $"'{member.UnitId}' is already in squad '{other.Id}'.");
            }

            if (!definition.Teams.Contains(member.Team, StringComparer.Ordinal))
            {
                throw Invalid(definition, $"'{member.UnitId}' is in team '{member.Team}', which the squad does not list.");
            }
        }

        var leaders = definition.Members.Where(m => m.Role == SquadRole.SquadLeader).ToList();
        if (leaders.Count != 1)
        {
            throw Invalid(definition, $"a squad needs exactly one squad leader, not {leaders.Count}.");
        }

        if (definition.Members.Count(m => m.Role == SquadRole.SecondInCommand) > 1)
        {
            throw Invalid(definition, "a squad has at most one second-in-command.");
        }

        var teamLeaders = new List<string>();
        foreach (var team in definition.Teams)
        {
            var members = definition.Members.Where(m => m.Team == team).ToList();
            var teamLeader = members
                .Where(m => m.Role is SquadRole.SquadLeader or SquadRole.SecondInCommand or SquadRole.TeamLeader)
                .ToList();
            if (members.Count == 0 || teamLeader.Count != 1)
            {
                throw Invalid(definition, $"team '{team}' needs members and exactly one leader.");
            }

            teamLeaders.Add(teamLeader[0].UnitId);
        }

        return (leaders[0].UnitId, teamLeaders);
    }

    private static Vec2? Destination(List<Vec2>? route) => route is null ? null : route[^1];

    private List<Unit> Fighters(string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Select(GetUnit).Where(u => u.Health != Health.OutOfAction).ToList();
    }

    private void CheckPlacement(string id, Vec2 position)
    {
        if (_units.ContainsKey(id))
        {
            throw new ArgumentException($"A unit with id '{id}' already exists.", nameof(id));
        }

        if (!Terrain.Contains(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Unit must be placed on the map.");
        }
    }

    private Unit Place(Unit unit)
    {
        _units.Add(unit.Id, unit);
        _logged[unit.Id] = new AgentSnapshot(unit.Id, unit.Position, unit.Activity);
        Log(new AgentPlaced(TimeSeconds, unit.Id, unit.Position));
        return unit;
    }

    /// <summary>Logs every agent whose position or activity changed this step, so the log alone can rebuild the state.</summary>
    private void LogStateChanges()
    {
        foreach (var unit in _units.Values)
        {
            var last = _logged[unit.Id];
            if (unit.Position != last.Position)
            {
                Log(new AgentMoved(TimeSeconds, unit.Id, unit.Position));
            }

            if (unit.Activity != last.Activity)
            {
                Log(new ActivityChanged(TimeSeconds, unit.Id, unit.Activity));
            }

            _logged[unit.Id] = new AgentSnapshot(unit.Id, unit.Position, unit.Activity);
        }
    }

    private void ValidateFireAt(Unit unit, FireAt order)
    {
        if (unit.Combatant is not { } shooter)
        {
            throw new OrderRejectedException($"Unit '{unit.Id}' is not a fighter.");
        }

        if (!_units.TryGetValue(order.TargetId, out var target) || target.Combatant is not { } other)
        {
            throw new OrderRejectedException($"No fighter with id '{order.TargetId}'.");
        }

        if (target == unit || other.Side == shooter.Side)
        {
            throw new OrderRejectedException($"'{order.TargetId}' is on {unit.Id}'s own side.");
        }
    }

    private void ValidateCorridorOrder(Unit unit, MoveThroughCorridor order)
    {
        var corridor = order.Corridor ?? throw new OrderRejectedException("The order has no corridor.");
        if (Terrain is not IWorldQuery)
        {
            throw new OrderRejectedException("Corridor orders need a raster world with terrain classes.");
        }

        if (unit.Profile is null)
        {
            throw new OrderRejectedException($"Unit '{unit.Id}' has no movement profile.");
        }

        if (corridor.Centerline is not { Count: >= 2 })
        {
            throw new OrderRejectedException("A corridor's centerline needs at least two points.");
        }

        if (!Terrain.Contains(order.Destination))
        {
            throw new OrderRejectedException($"The corridor ends at {order.Destination}, outside the map.");
        }

        if (corridor.Width <= 0 || corridor.SoftMargin < 0 || corridor.SoftPenalty < 0)
        {
            throw new OrderRejectedException("A corridor needs a positive width and a non-negative soft margin and penalty.");
        }

        var world = (IWorldQuery)Terrain;
        foreach (var (name, point) in new[] { ("starts", unit.Position.Xy), ("would end", order.Destination) })
        {
            if (Segments.DistanceToPolyline(point, corridor.Centerline) > corridor.OuterLimit)
            {
                throw new OrderRejectedException($"Unit '{unit.Id}' {name} at {point}, outside the corridor's outer limit.");
            }

            var terrain = world.ClassAt(point.X, point.Y);
            if (unit.Profile.BaseSpeed(terrain) <= 0)
            {
                throw new OrderRejectedException($"Unit '{unit.Id}' {name} at {point}, in impassable terrain ({terrain}).");
            }
        }
    }

    /// <summary>
    /// A fingerprint of the complete simulation state. Two runs with the same inputs must
    /// produce the same hash at every tick (checked by the determinism tests).
    /// </summary>
    public ulong ComputeStateHash()
    {
        var hash = new StateHasher();
        hash.Add(Tick);
        foreach (var unit in _units.Values)
        {
            hash.Add(unit.Id);
            hash.Add(unit.Position);
            hash.Add(unit.Velocity);
            hash.Add((long)unit.Activity);
            hash.Add((long)unit.Health);
            hash.Add((long)unit.Posture);
            hash.Add(unit.IsSuppressed ? 1 : 0);
            hash.Add(unit.IsHeavilySuppressed ? 1 : 0);
            hash.Add(unit.SuppressionPressure);
            hash.Add((long)unit.DrillStage);
            hash.Add(unit.IsAwaitingOrders ? 1 : 0);
            hash.Add(unit.RoundsLeft);
            hash.Add(unit.CurrentOrder?.ToString() ?? "none");
        }

        foreach (var squad in _squads.Values)
        {
            hash.Add(squad.Id);
            hash.Add(squad.LeaderId ?? "none");
            foreach (var teamLeader in squad.TeamLeaderIds)
            {
                hash.Add(teamLeader ?? "none");
            }

            hash.Add((long)squad.Formation);
            hash.Add(squad.LeaderHeld ? 1 : 0);
            hash.Add(squad.MovementStarted ? 1 : 0);
            hash.Add(squad.LeaderTrail.Count);
            foreach (var point in squad.LeaderTrail)
            {
                hash.Add(point.X);
                hash.Add(point.Y);
            }

            hash.Add(squad.Corridor is null ? 0 : 1);
            foreach (var member in squad.Members)
            {
                hash.Add((long)squad.FormationOf(member.UnitId));
                hash.Add(squad.IsFollowing(member.UnitId) ? 1 : 0);
            }

            foreach (var delivery in squad.Deliveries)
            {
                hash.Add(delivery.UnitId);
                hash.Add(delivery.CommandId);
                hash.Add(delivery.Due);
                hash.Add(delivery.RelayTo.Count);
            }

            foreach (var takeover in squad.TeamTakeovers.Append(squad.LeaderTakeover))
            {
                hash.Add(takeover?.UnitId ?? "none");
                hash.Add(takeover?.Due ?? -1);
            }
        }

        return hash.Value;
    }

    /// <summary>FNV-1a over the exact bit patterns of the state.</summary>
    private sealed class StateHasher
    {
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(long value)
        {
            for (var i = 0; i < 8; i++)
            {
                AddByte((byte)(value >> (i * 8)));
            }
        }

        public void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value));

        public void Add(Vec3 v)
        {
            Add(v.X);
            Add(v.Y);
            Add(v.Z);
        }

        public void Add(string value)
        {
            foreach (var c in value)
            {
                Add(c);
            }

            AddByte(0);
        }

        private void AddByte(byte b)
        {
            unchecked
            {
                Value = (Value ^ b) * 1099511628211UL;
            }
        }
    }
}
