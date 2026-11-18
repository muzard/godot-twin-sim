using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Terrain;
using Twin.Sim.Units;

namespace Twin.Testing;

/// <summary>Entry point for unit assertions: <c>sim.Unit("alpha").Should().BeIdle()</c>.</summary>
public static class UnitAssertionExtensions
{
    /// <summary>Starts an assertion chain on a unit.</summary>
    public static UnitAssertions Should(this Unit unit) => new(unit);
}

/// <summary>
/// Readable assertions about a unit. Failure messages always include the unit's full state,
/// which makes failures quick to diagnose for people and coding agents alike.
/// </summary>
public sealed class UnitAssertions
{
    /// <summary>Default position tolerance in meters.</summary>
    public const double DefaultToleranceMeters = 0.01;

    private readonly Unit _unit;

    internal UnitAssertions(Unit unit) => _unit = unit ?? throw new ArgumentNullException(nameof(unit));

    /// <summary>The unit is within <paramref name="toleranceMeters"/> of a map position (horizontal distance).</summary>
    public UnitAssertions BeAt(Vec2 expected, double toleranceMeters = DefaultToleranceMeters)
    {
        var distance = _unit.Position.Xy.DistanceTo(expected);
        return Check(distance <= toleranceMeters,
            $"to be at {expected} (±{toleranceMeters} m), but it is {distance:0.###} m away");
    }

    /// <summary>The unit is within <paramref name="toleranceMeters"/> of a 3D position.</summary>
    public UnitAssertions BeAt(Vec3 expected, double toleranceMeters = DefaultToleranceMeters)
    {
        var distance = _unit.Position.DistanceTo(expected);
        return Check(distance <= toleranceMeters,
            $"to be at {expected} (±{toleranceMeters} m), but it is {distance:0.###} m away");
    }

    /// <summary>The unit is idle.</summary>
    public UnitAssertions BeIdle() =>
        Check(_unit.IsIdle && _unit.Velocity == Vec3.Zero, "to be idle and stationary");

    /// <summary>The unit has no current order ("order none" in diagnostics).</summary>
    public UnitAssertions HaveNoOrder() =>
        Check(_unit.CurrentOrder is null, $"to have no current order, but it has {_unit.CurrentOrder}");

    /// <summary>The unit is moving and its horizontal velocity points at <paramref name="destination"/>.</summary>
    public UnitAssertions BeMovingToward(Vec2 destination)
    {
        var heading = _unit.Velocity.Xy.Normalized();
        var wanted = (destination - _unit.Position.Xy).Normalized();
        const double maxAngleCos = 0.999; // within ~2.5 degrees
        return Check(
            _unit.Activity == UnitActivity.Moving && heading.Dot(wanted) >= maxAngleCos,
            $"to be moving toward {destination}");
    }

    /// <summary>The unit's height equals the ground height under it.</summary>
    public UnitAssertions BeOnGround(ITerrain terrain, double toleranceMeters = DefaultToleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var ground = terrain.HeightAt(_unit.Position.Xy);
        return Check(Math.Abs(_unit.Position.Z - ground) <= toleranceMeters,
            $"to be on the ground (ground height {ground:0.###} m)");
    }

    /// <summary>The unit's horizontal speed is <paramref name="expectedMps"/> within a tolerance.</summary>
    public UnitAssertions HaveGroundSpeed(double expectedMps, double toleranceMps = 0.001)
    {
        var speed = _unit.Velocity.Xy.Length;
        return Check(Math.Abs(speed - expectedMps) <= toleranceMps,
            $"to have ground speed {expectedMps} m/s, but it is {speed:0.###} m/s");
    }

    /// <summary>
    /// The unit is suppressed (backlog 0003): its fire pressure has reached its threshold and not yet fallen below a
    /// quarter of it; it is prone and does not move on with its order.
    /// </summary>
    public UnitAssertions BeSuppressed() =>
        Check(_unit.IsSuppressed && _unit.Activity == UnitActivity.Suppressed, "to be suppressed");

    /// <summary>The unit is not suppressed.</summary>
    public UnitAssertions NotBeSuppressed() =>
        Check(!_unit.IsSuppressed, "not to be suppressed");

    /// <summary>The unit is heavily suppressed (backlog 0003): pressure at five times its threshold; it does not fire at all.</summary>
    public UnitAssertions BeHeavilySuppressed() =>
        Check(_unit.IsSuppressed && _unit.IsHeavilySuppressed, "to be heavily suppressed");

    /// <summary>The unit is not heavily suppressed (it may still be suppressed).</summary>
    public UnitAssertions NotBeHeavilySuppressed() =>
        Check(!_unit.IsHeavilySuppressed, "not to be heavily suppressed");

    /// <summary>The unit is in the given stage of the immediate action drill under fire (backlog 0003).</summary>
    public UnitAssertions BeInDrillStage(ContactDrillStage expected) =>
        Check(_unit.DrillStage == expected, $"to be in drill stage {expected}");

    /// <summary>The unit has reported contact and waits for the leader's orders (backlog 0003).</summary>
    public UnitAssertions BeAwaitingOrders() =>
        Check(_unit.IsAwaitingOrders, "to be waiting for orders");

    /// <summary>The unit has the given health state.</summary>
    public UnitAssertions HaveHealth(Health expected) =>
        Check(_unit.Health == expected, $"to have health {expected}");

    /// <summary>The unit is in the given posture.</summary>
    public UnitAssertions BeIn(Posture expected) =>
        Check(_unit.Posture == expected, $"to be {expected}");

    /// <summary>The unit is not in the given posture.</summary>
    public UnitAssertions NotBeIn(Posture unexpected) =>
        Check(_unit.Posture != unexpected, $"not to be {unexpected}");

    /// <summary>The unit's activity is <paramref name="expected"/> (for example waiting for its squad, backlog 0004).</summary>
    public UnitAssertions HaveActivity(UnitActivity expected) =>
        Check(_unit.Activity == expected, $"to have activity {expected}");

    /// <summary>
    /// The unit watches toward <paramref name="direction"/> (any length), within <paramref name="toleranceDegrees"/>
    /// (backlog 0004 S5a1).
    /// </summary>
    public UnitAssertions BeWatching(Vec2 direction, double toleranceDegrees = 1)
    {
        var watch = _unit.WatchDirection.Normalized();
        var cos = Math.Clamp(watch.Dot(direction.Normalized()), -1, 1);
        var off = Math.Acos(cos) * 180 / Math.PI;
        return Check(off <= toleranceDegrees,
            $"to watch toward {direction} (±{toleranceDegrees}°), but it watches toward {_unit.WatchDirection}, {off:0.#}° off");
    }

    /// <summary>
    /// One of the unit's observation sectors (backlog 0004, S2c) covers the map direction <paramref name="direction"/>
    /// (edges included).
    /// </summary>
    public UnitAssertions Observe(Vec2 direction) =>
        Check(_unit.Observes(direction), $"to observe direction {direction}, but its sectors are {Sectors(_unit)}");

    /// <summary>None of the unit's observation sectors covers the map direction <paramref name="direction"/>.</summary>
    public UnitAssertions NotObserve(Vec2 direction) =>
        Check(!_unit.Observes(direction), $"not to observe direction {direction}, but its sectors are {Sectors(_unit)}");

    /// <summary>The unit also watches the air (backlog 0004, S2c).</summary>
    public UnitAssertions WatchTheAir() => Check(_unit.WatchesAir, "to watch the air");

    /// <summary>The unit does not watch the air.</summary>
    public UnitAssertions NotWatchTheAir() => Check(!_unit.WatchesAir, "not to watch the air");

    /// <summary>The unit faces <paramref name="direction"/> (its <see cref="Unit.Heading"/>) within <paramref name="toleranceDegrees"/>.</summary>
    public UnitAssertions Face(Vec2 direction, double toleranceDegrees = 1)
    {
        var cos = Math.Clamp(_unit.Heading.Normalized().Dot(direction.Normalized()), -1, 1);
        var angle = Math.Acos(cos) * 180 / Math.PI;
        return Check(angle <= toleranceDegrees,
            $"to face {direction} (±{toleranceDegrees}°), but it faces {_unit.Heading}, {angle:0.#}° off");
    }

    private static string Sectors(Unit u) =>
        u.ObservationSectors.Count == 0
            ? "none"
            : string.Join(", ", u.ObservationSectors.Select(s => $"{s.Centre} ±{s.WidthDegrees / 2:0.#}°"));

    internal static string DescribeUnit(Unit u) =>
        $"  unit '{u.Id}': position {u.Position}, velocity {u.Velocity}, activity {u.Activity}, " +
        $"health {u.Health}, posture {u.Posture}, pressure {u.SuppressionPressure:0.###}, drill {u.DrillStage}, " +
        $"awaiting orders {u.IsAwaitingOrders}, rounds {u.RoundsLeft}, order {u.CurrentOrder?.ToString() ?? "none"}";

    private UnitAssertions Check(bool condition, string expectation)
    {
        if (!condition)
        {
            throw new ScenarioAssertionException(
                $"Expected unit '{_unit.Id}' {expectation}.{Environment.NewLine}{DescribeUnit(_unit)}");
        }

        return this;
    }
}
