using Twin.Testing;

namespace Twin.Sim.Tests.Scenarios;

/// <summary>
/// Scenario files that cannot be run fail when they are loaded, with an <see cref="InvalidDataException"/> that names the
/// problem, not later when the simulation is created (backlog 0004 S2d, found in review): a squad start or an enemy off
/// the map, a member's starting slot off the map, an id used twice across the squad and the enemies, and a corridor whose
/// first segment gives no direction to lay the squad out along.
/// </summary>
public class ScenarioSquadValidationTests
{
    private static string Scenario(string start = "[385080.0, 6672101.0]", string centerline = "[[385080.0, 6672101.0], [385420.0, 6672101.0]]", string enemies = "[]") => $$"""
        {
          "schemaVersion": 1,
          "corridor": { "centerline": {{centerline}}, "width": 80, "softMargin": 25, "softPenalty": 1 },
          "leader": { "answer": "grant", "delaySeconds": 20, "permitRadius": 10 },
          "squad": {
            "id": "r1", "start": {{start}}, "formation": "wedge", "teams": ["a"],
            "members": [
              { "id": "pj", "role": "rifleman", "team": "a" },
              { "id": "rj", "role": "squadLeader", "team": "a" },
              { "id": "k1", "role": "rifleman", "team": "a" }
            ]
          },
          "enemies": {{enemies}}
        }
        """;

    private static InvalidDataException Fails(string json) =>
        Assert.Throws<InvalidDataException>(() => TestScenarioFolder.Load(json));

    [Fact]
    public void A_squad_start_off_the_map_fails_to_load()
    {
        Assert.Contains("map", Fails(Scenario(start: "[386000.0, 6672101.0]")).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_member_whose_starting_slot_is_off_the_map_fails_to_load()
    {
        // The wedge's apex (pj, the middle of the line rj, pj, k1) stands one step (7.1 m) ahead of the leader: off the
        // east edge (x = 500) when the leader is at x = 498.
        var error = Fails(Scenario(start: "[385498.0, 6672101.0]", centerline: "[[385498.0, 6672101.0], [385499.0, 6672101.0]]"));

        Assert.Contains("pj", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enemy_off_the_map_fails_to_load()
    {
        var error = Fails(Scenario(enemies: """[ { "id": "red-1", "position": [385300.0, 6673000.0] } ]"""));

        Assert.Contains("red-1", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[ { "id": "rj", "position": [385300.0, 6672150.0] } ]""", "rj")]
    [InlineData("""[ { "id": "red-1", "position": [385300.0, 6672150.0] }, { "id": "red-1", "position": [385310.0, 6672150.0] } ]""", "red-1")]
    public void An_id_used_twice_across_the_squad_and_the_enemies_fails_to_load(string enemies, string id)
    {
        Assert.Contains($"'{id}'", Fails(Scenario(enemies: enemies)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[[385080.0, 6672101.0], [385080.0, 6672101.0], [385420.0, 6672101.0]]")]
    [InlineData("[[385080.0, 6672101.0]]")]
    public void A_corridor_whose_first_segment_gives_no_direction_fails_to_load_a_squad(string centerline)
    {
        Assert.Contains("corridor", Fails(Scenario(centerline: centerline)).Message, StringComparison.OrdinalIgnoreCase);
    }
}
