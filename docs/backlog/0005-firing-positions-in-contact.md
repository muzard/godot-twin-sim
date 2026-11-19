# 0005. Firing positions in contact: the fighter, the team leader and the squad leader

**Status:** decisions agreed with the product owner (2026-11-19); tests reviewed and implemented (2026-11-19).
Builds on [0003 (the contact drill)](0003-suppression-and-contact-drill.md) and
[0004 (the squad, S5a2 contact and S5b fire control)](0004-squad-movement-and-leadership.md). Values marked
**[PROPOSAL]** are starting values; tests read them from one place in `tests/Twin.Testing`.

## Why
Seen in the st1 demos: members in contact stand still when a squad mate is in their fire sector, they have no sight
line, or the enemy is out of range. 0003's drill already takes a member that is **fired at** to a firing position
toward its attackers; nothing moves a member that is in contact but not fired at. The search for a position is a
fighter's own behaviour, but deciding where a team goes runs through the chain of command, so this is its own ticket
rather than another part of 0004. 0003's drill keeps its own search and is not changed.

## Decisions (product owner, 2026-11-19)
1. **Who.** A member in contact (as in 0004 S5b: it knows of an enemy, spotted or reported, or its squad is halted on
   contact) that knows of an enemy it should fire at
   (before the fire distribution any enemy it knows of; after it, one in its fire area, 0004 S5b) but cannot engage any
   of them where it stands, and is not in the contact drill and not under hold fire.
2. **First on its own.** It looks for a firing position near by and moves there.
3. **The search: 0003's, improved.** A wooded rise is better than an open field, so positions are scored for cover,
   concealment and height, not only distance.
4. **If it finds none, it tells its team leader** (the point team's PJ, the support team's VJ, the command team's RJ).
5. **The team leader moves the whole team** to a position from which as many of the team as possible can fire.
6. **Unless the team is still firing.** If at least two of the team can fire where they are, or the team's machine
   gun can, the team leader must ask the squad leader's permission first. The squad leader decides from the situation:
   whether another team is moving, and whether the squad has fire superiority.
7. **If the team leader finds no position, it tells the squad leader,** who searches a wider area and commands the team
   there (0004 S5c draft, decided earlier the same day).

## Acceptance criteria (draft for review)
Proposed values are gathered under "Values" below.

**The fighter**
- **FP1** A member in contact that cannot engage an enemy it knows of (a squad mate in its sector, no sight line, or out
  of range) moves, after its reaction time, to a firing position within the search radius, and fires from there.
- **FP2** It does not move while it can engage an enemy it should fire at; nor under hold fire; nor in the drill (0003
  handles that); nor before its squad is in contact.
- **FP3** Of two positions, it takes the higher score: a wooded rise 8 m above the enemy 25 m off beats an open field 10
  m off. With cover equal and no height, the nearer one wins.
- **FP4** A candidate must be reachable without an unpermitted crossing (ADR 0009) and inside the corridor, see the
  enemy's known position, have its sector toward it clear of squad mates (0003: 30°) and the enemy within range.
- **FP5** It moves as in 0003's drill (`MovingToFiringPosition`): not suppressed, rushing crouched; lightly suppressed,
  crawling prone; heavily suppressed, it does not move and tells its team leader instead (FP6).

**The team leader**
- **FP6** A member that finds no position tells its team leader (`CannotFireReported`, by the member, delivered with
  0004 S1's relay delay).
- **FP7** If fewer than two of the team can fire and the team's machine gun cannot either, the team leader, after its
  reaction time, picks the team position from which the most team members can fire (ties by the fighter's score
  summed, then grid order) and commands the team there; each member goes to its spot in a line across the direction to
  the enemy, at the squad interval.
- **FP8** If two or more of the team can fire, or its machine gun can, the team leader asks the squad leader
  (`TeamMoveRequested`) and moves only on the squad leader's grant.
- **FP9** The squad leader grants when no other team of the squad is moving and the squad has fire superiority;
  otherwise it denies, and the team stays. A team leader denied asks again no sooner than the retry time.
- **FP10** A team leader that finds no team position tells the squad leader, who searches the wider radius around the
  team and commands the team there; if it finds none either, the team stays and nobody reports again until the enemy
  picture or the fire distribution changes.
- **FP11** The command team's leader is the squad leader itself: for its own team it decides at once, without asking.

## Values **[PROPOSAL]**
| Value | Proposal |
|---|---|
| Fighter's search radius | 30 m |
| Team leader's search radius (around the team leader) | 60 m |
| Squad leader's wider search radius (around the team) | 100 m |
| Score | `20 · cover + 10 · concealment + 4 · height above the enemy (m, ≥ 0) − 1 · path length (m) − 1 · path length over open ground (m)` (height weight raised from 2 to 4 while writing FP3: with 2, the rise 25 m off over open ground loses to the field 10 m off) |
| Cover | 0003's: the enemy cannot see the point lying prone (1 or 0) |
| Concealment | forest at the point (1 or 0) |
| "Can fire" for the team count | engageable where it stands now (sight, clear sector, range), as in `FireSystem` |
| Fire superiority | the squad's rounds fired in the last 10 s at least twice the enemy rounds heard in the same 10 s |
| Retry after a denial | 30 s |
| Reaction | 0004's reaction times (experienced 1 s, inexperienced 3 s) |

## API the tests need from `Twin.Sim`
- `Simulation.FindBestFiringPosition(agentId, enemyId, radiusMeters)`: the best point by the score, or null (FP3, FP4).
- `UnitActivity.MovingToFiringPosition`: the activity of a member on its way (FP5).
- Events `CannotFireReported(Time, AgentId, LeaderId)`, `TeamMoveRequested(Time, AgentId, Team)` and
  `TeamMoveAnswered(Time, AgentId, Team, Granted)`.
- The team move is a squad command whose `CommandIssued.Command` starts with `MoveTeamToFiringPosition`, by the team
  leader (or by the squad leader for FP10 and its own team).
- Kit: `SpecFiringPositions` in `tests/Twin.Testing` holds the values; `ShortRange(weapon, metres)` keeps a fighter from
  engaging, the tests' way of making a member unable to fire.

## Tests (spec, 2026-11-19)
`FiringPositionTests` (the S5b row and red pair; t1's rifle reaches 100 m, red-left is 118.7 m off):
| Criterion | Test | Before implementation |
|---|---|---|
| FP1 | `FP1_a_member_that_cannot_engage_moves_to_a_firing_position_within_its_search_radius_and_fires_from_there` | fails: t1 never moves |
| FP2 | `FP2_a_member_that_can_engage_where_it_stands_does_not_move` | passes (guard) |
| FP2 | `FP2_a_member_under_hold_fire_does_not_move_to_a_firing_position` | passes (guard) |
| FP2 | `FP2_a_member_that_knows_of_no_enemy_does_not_move` | passes (guard) |
| FP5 | `FP5_a_member_not_suppressed_rushes_to_its_firing_position_crouching` | fails: t1 never moves; needs the new activity |

`FiringPositionSearchTests` (the query; blue behind a 3 m wall, red 80 m north):
| Criterion | Test |
|---|---|
| FP3 | `FP3_a_wooded_rise_8_m_above_the_enemy_25_m_off_beats_an_open_field_10_m_off` |
| FP3 | `FP3_with_no_cover_concealment_or_height_the_nearer_position_wins` |
| FP4 | `FP4_a_position_outside_the_forest_is_not_taken_without_a_permit` |
| FP4 | `FP4_a_position_with_its_own_side_in_the_fire_sector_is_not_taken` |
| FP4 | `FP4_no_position_brings_an_enemy_beyond_range_and_radius_into_range` |

`FiringPositionTeamTests` (the S5b row and red pair, shortened weapons):
| Criterion | Test |
|---|---|
| FP6 | `FP6_a_member_that_finds_no_firing_position_tells_its_team_leader` |
| FP7 | `FP7_a_team_leader_whose_team_cannot_fire_moves_the_team_without_asking_and_the_team_fires` |
| FP8 | `FP8_a_team_leader_with_two_of_its_team_still_firing_asks_the_squad_leader_and_moves_on_the_grant` |
| FP9 | `FP9_the_squad_leader_denies_a_team_move_while_another_team_is_moving_and_the_team_leader_asks_again_after_the_retry_time` |
| FP10 | `FP10_a_team_leader_that_finds_no_team_position_tells_the_squad_leader_who_moves_the_team_from_its_wider_search` |
| FP10 | `FP10_when_the_squad_leader_finds_no_position_either_the_team_stays_and_nobody_reports_again` |
| FP11 | `FP11_the_squad_leader_moves_its_own_command_team_without_asking` |

The search and team tests do not compile before implementation (the new query and events are missing).

**Not covered by a test (for review):** the reaction-time delays (FP1, FP7); crawling when lightly suppressed and
staying when heavily suppressed (FP5; suppression without being fired at is hard to set up apart from 0003's drill);
the corridor condition (FP4); a denial for lack of fire superiority (FP9: only the "another team moving" denial is
tested); the line shape of the team position (FP7 checks only that every member fires from within range).

## Implementation notes (2026-11-19)
- `FiringPositionSystem` (after `SquadSystem`) and `FiringPositionSearch`; values in `SquadDoctrine.FiringPositions`
  (`FiringPositionRules`). The fighter's search walks 0003's grid (`CoverSearch.Grid`, now taking the squad's permits),
  so it never leaves forest without the squad's forest-edge permit. The team search tries each grid point within the
  radius as the middle of the team's line and checks a straight, passable way to each spot.
- **"Can fire" means could fire where it stands:** in range, in sight, sector clear, as `FireSystem` but without having
  spotted the enemy yet (a reported enemy in sight is spotted soon; the member should not leave for that). The team
  leader counts who can fire at **any** enemy they know of, not only in their fire area: a fire area a step behind the
  enemy picture (the first distribution, made while the leader knew one enemy) otherwise made a firing team look idle.
- A team is "moving" from its move command until every member has acted on it and reached its spot.
- A member holding a firing position is not pulled back to its formation slot; the squad moving on (the end of the fire
  distribution) releases it.
- **Test changes approved by the product owner (2026-11-19):** FP3's wall is a row of building cells instead of a 3 m
  mound (the 2 m grid's interpolated heights made the mound a walkable ramp blue could see from), and its wall ends at
  x = 118; FP3's rise climbs evenly to 8 m (slope 0.67) instead of being a sheer 8 m step no one could climb. FP10's
  "no position anywhere" case uses a 1 m weapon reach instead of 10 m (the squad leader's 100 m search reaches about 4 m
  from the enemy).
- **Only while the squad is fighting:** halted on contact, or its fire distributed. Members never forget an enemy they
  have known of, so "knows of an enemy" alone kept them seeking positions after the squad moved on, and the squad
  waited for them for good (found in review; `FiringPositionEdgeCaseTests`). The squad moving on releases every hold.
- Review fixes: a team granted and about to move counts as moving; a request with no squad leader to answer it is
  retried, and a member with no team leader (one taking over) tries again; a team leader that found nothing does not
  look again until a new report; the team search checks slopes and cliffs and moves the team only to a line from which
  more can fire than now; the fighter's search stays inside the corridor; an order of a member's own ends its move;
  fire superiority counts rounds (`ShotFired.Rounds`), with no fire on either side counting as superiority.
- Not done, for the human: `SpecFiringPositions`' values are not fed into the simulation (as `SpecSquad.Doctrine` is),
  so the tests rely on `FiringPositionRules`' defaults; wiring them means changing `SpecSquad` in the locked kit. The
  team move command reaches every member of the squad (other teams log `CommandReceived` and ignore it).
- In the st1 demos members now leave their spots for firing positions; with a move the point team can end up in the
  support team's sectors, which then asks to move in turn (seen in a probe, not tested).

## Open questions
- Does the drill (0003) take the improved score too? Proposed: not now; 0003's tests stay as they are.
- Leaving the forest to reach a better position needs a permit (ADR 0009). Proposed: candidates outside the forest are
  allowed only on the squad's forest-edge permit (0004), never asked for here.
