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

    /// <summary>The unit is suppressed: under fire within the suppression time, prone and not moving on.</summary>
    public UnitAssertions BeSuppressed() =>
        Check(_unit.IsSuppressed && _unit.Activity == UnitActivity.Suppressed, "to be suppressed");

    /// <summary>The unit has the given health state.</summary>
    public UnitAssertions HaveHealth(Health expected) =>
        Check(_unit.Health == expected, $"to have health {expected}");

    /// <summary>The unit is in the given posture.</summary>
    public UnitAssertions BeIn(Posture expected) =>
        Check(_unit.Posture == expected, $"to be {expected}");

    internal static string DescribeUnit(Unit u) =>
        $"  unit '{u.Id}': position {u.Position}, velocity {u.Velocity}, activity {u.Activity}, " +
        $"health {u.Health}, posture {u.Posture}, order {u.CurrentOrder?.ToString() ?? "none"}";

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
