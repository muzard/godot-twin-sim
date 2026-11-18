using Twin.Sim.Combat;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Sim.Tests.Squads;

/// <summary>
/// How a fighter's experience (backlog 0003) reads in the chain of command (backlog 0004, "Experience unified with
/// 0003"): a conscript waits for its team leader's word and reacts in the inexperienced time; a regular soldier and
/// special forces act on the squad leader's command directly, in the experienced time. Provisional mapping.
/// </summary>
public class SquadDoctrineTests
{
    [Theory]
    [InlineData(Experience.Conscript, false, SpecSquad.InexperiencedReactionSeconds)]
    [InlineData(Experience.Regular, true, SpecSquad.ExperiencedReactionSeconds)]
    [InlineData(Experience.SpecialForces, true, SpecSquad.ExperiencedReactionSeconds)]
    public void Each_experience_level_has_its_place_in_the_chain_of_command(
        Experience experience, bool actsDirectly, double reactionSeconds)
    {
        Assert.Equal(actsDirectly, SquadDoctrine.ActsDirectly(experience));
        Assert.Equal(reactionSeconds, SpecSquad.Doctrine.ReactionSeconds(experience));
    }
}
