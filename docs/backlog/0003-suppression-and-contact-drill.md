# 0003. Suppression as fire pressure, experience, and the immediate action drill under fire

**Status:** proposal (2026-11-17), waiting for review before the spec phase. Builds on 0002 (shooting).

## Decisions
Decided with the product owner on 2026-11-17.

**Purpose and scope**
- Returning fire aims to **suppress**, not to cause casualties. It may still hit, rarely; a special-forces soldier at
  close range is expected to land effective hits.
- No fire at a position that is only heard; that needs area fire and is a later ticket (0002's rule stands).
- `HoldFire` is unchanged: no fire without an order, even under fire, and no drill.
- An agent that opens fire on its own under `FreeFire` uses aimed fire.

**Suppression**
- Suppression depends on what passes the target: high-calibre machine-gun fire close overhead suppresses far more
  than a stray round passing far away. The 0002 rule "every shot suppresses for 10 s" is **replaced**.
- Fire effect depends on the shooter's **experience**, **range**, the **quality of observation** and the target's
  **cover**.
- Three experience levels: conscript/reservist, regular (the default) and special forces.
- **Heavy suppression stops all fire.** Its threshold must be high: ordinary rifle fire should rarely reach it.
- Fire also suppresses **fighters next to the target**, usually less. It depends on the situation: when a point patrol
  makes contact, the fighters at the rear of the wedge are not paralysed, at least not completely.
- A round passing close enough puts a fighter **under fire**, and that starts the **whole drill**, not only alertness.

**The immediate action drill**
- The drill: **1) a few snap shots while taking cover, 2) when suppression allows, move to cover and/or return fire
  with aimed shots, 3) wait for the leader's orders** (break contact, hook, …).
- **Cover comes first:** if the agent is in danger of being hit, it first moves to cover, even cover from which it
  cannot fire. From there it moves on to a good firing position.
- **How the agent moves depends on how suppressed it is.** Lightly suppressed, it may still move (crawl) to cover or
  to a firing position; more heavily suppressed, it stays where it is. It may leave cover to reach a firing position.
- **Never fire towards one's own:** a fighter does not fire when an own fighter is within **30° either side** of its
  muzzle direction (a 60° sector), as at the rear of a wedge with team-mates in front. It then has to move to a
  firing position from which it can fire, **forward** by default. The sector reaches out to the weapon's maximum
  range: own fighters beyond it are ignored, those within it always count.
- Lightly suppressed means pressure below `2 τ`; an agent that is not suppressed rushes to cover crouching.
- **Three snap shots** for every experience level, so the drill stays deterministic.
- The agent waits for orders indefinitely by default. The later goal is that a fallen or incapacitated leader is
  replaced along the chain of command (later ticket).
- The drill keeps an ammunition reserve by default.
- The drill's timings, the reserve and the weapon values need calibration. Weapon values are left open for now.

Values marked **[PROPOSAL]** are starting values for review. As in 0002, tests read them from one place in
`tests/Twin.Testing`, so calibration later does not change what a test checks.

## Story
As an exercise designer, I can see a soldier who comes under fire fire a few snap shots as he goes to ground, then,
once the fire lets him, get into cover, work his way to a good firing position and answer with aimed shots, and wait
for orders, as the drill says. A machine gun sweeping close overhead keeps a squad down for long; a few stray rounds
from far away barely do. When the point patrol of a wedge makes contact, the fighters at the rear are not pinned down
by it, and they never fire through their own. Special forces react faster, shoot better and shake off fire sooner than
conscripts.

## Model

### Experience (X)
`Combatant` gets an `Experience`: `Conscript`, `Regular` (default), `SpecialForces`. The default keeps every existing
scenario as it is.

| | Conscript | Regular | Special forces |
|---|---|---|---|
| Accuracy factor `A` (multiplies P(hit)) | 0.8 | 1.0 | 1.3 |
| Dispersion factor `σ_X` (multiplies miss distance) | 1.3 | 1.0 | 0.6 |
| Reaction time before the first snap shot | 2.0 s | 1.5 s | 0.5 s |
| Suppression threshold `τ` | 0.8 | 1.0 | 1.5 |
| Pressure half-life `H` | 6 s | 4 s | 2.5 s |

All values **[PROPOSAL]**.

### Fire modes (M)
Each shot is either `Aimed` or `Snap`; `ShotFired` records the mode.
- **Aimed:** a `FireAt` order, fire opened on one's own under `FreeFire`, and drill stage 4. As in 0002.
- **Snap** (heittolaukaus): drill stage 1. Quick shots fired while taking cover, to make the enemy duck.

| | Aimed | Snap |
|---|---|---|
| Interval (rifle) | 2 s | 0.7 s |
| Hit factor `K` | 1 | 0.25 |
| Angular dispersion `σ_M` | 3 mil | 6 mil |

All values **[PROPOSAL]**.

`P(hit) = 0002 formula · A · K` per round. Regular aimed fire gives exactly the 0002 value. Example: special forces,
snap shot, 25 m in the open against a standing target: 0.6 · e^(−25/150) · 1.3 · 0.25 ≈ 0.17 per round.

Sustained suppressive fire on order (for example a machine gun keeping a position down) is a later ticket, together
with area fire.

### Fire sector (LF)
A shooter's **fire sector** is the 60° sector centred on its direction to the target: everything within **30°**
(decided, to be calibrated) either side of that direction, in front of the shooter, out to the weapon's maximum range.
It is **clear** when no other agent of the shooter's side is in it. Own agents beyond the weapon's maximum range are
ignored; within it they count, also far beyond the target and in dead ground (decided). Angles are measured on the map (X, Y). **No shot is
fired into a sector that is not clear**, whatever the reason for the shot (order, rule of engagement or drill).

Outside the drill, an agent waiting for a clear sector keeps its target and order and does not move. In the drill it
moves to a firing position with a clear sector (CP).

### Weapons (W)
`WeaponProfile` gets:
- `SuppressionPerRound` `S`: how much a round passing close suppresses (calibre, crack, volume)
- `RoundsPerBurst` `n`: rounds in one shot event (1 for a rifle)
- `SnapSecondsPerShot`: interval of snap shots

Starting values, **open, to be calibrated**:

| | Rifle (0002) | Machine gun 7.62 | Heavy machine gun 12.7 |
|---|---|---|---|
| `S` | 1.2 | 1.5 | 4.0 |
| `n` | 1 | 5 | 5 |
| Aimed interval | 2 s | 2 s | 3 s |
| Range, rounds | 600 m, 150 | 800 m, 500 | 1500 m, 200 |

A burst is one `ShotFired` with `rounds = n` and **one** draw: P(burst hits) = 1 − (1 − p)^n. One draw per shot event
keeps the random stream as in 0002.

### Observation quality (Q)
How well the shooter sees its target scales the miss distance:
- **Seen** (sight line clear, T ≥ T_min): factor 1
- **Muzzle flash only** (visible only through `T_flash`): factor 2; only snap shots, never aimed
- **Heard only:** no fire (unchanged from 0002)

All values **[PROPOSAL]**.

### Suppression as fire pressure (SU)
This replaces 0002's "a shot suppresses for 10 s".

1. **Miss distance at the target:** each round passes the target at the expected distance
   `m = d · σ_M · σ_X · Q`, with d the range and σ_M in radians (1 mil = 0.001 rad). It is an expected value, not a
   draw, so suppression uses no random numbers.
2. **Miss distance for everyone else:** a round flies along the ray from the shooter through the target, out to the
   weapon's maximum range. Any other agent in front of the shooter, on either side, is passed at `m_b = √(m² + b²)`, with `b` its perpendicular distance from that ray. The
   shooter itself is never passed.
3. **Pressure from one round:** `p = S · exp(−m / r) · C` (or `m_b` for others), with **r = 2 m [PROPOSAL]**.
4. **Cover `C`:** `C = T · Cover(posture)`, with T the forest transmission on the line from the shooter to that agent,
   and `Cover` 1.0 standing, 0.8 crouching, 0.6 prone **[PROPOSAL]**. A blocked line (ridge, building) gives C = 0.
5. **Accumulation:** every agent has a pressure `L ≥ 0`. A shot adds `n · p`. `L` halves every `H` (that agent's
   experience).
6. **States:**
   - **Suppressed** when `L ≥ τ`; ends when `L` falls below `0.25 τ` **[PROPOSAL]**. The gap stops the state
     flickering. Effects as in 0002: prone, stops its order's movement, hit factor 0.3.
   - **Lightly suppressed** is the lower part of it, `L < 2 τ` (decided, to be calibrated): in the drill, the agent may still
     crawl to cover or to a firing position. From `2 τ` up it stays where it is.
   - **Heavily suppressed** when `L ≥ 5 τ` **[PROPOSAL, kept high on purpose]**: as suppressed, and it does not fire
     at all. Against a regular or special-forces agent, sustained aimed rifle fire from one shooter levels off below it
     at any range (≈ 3.5 τ at 100 m for a regular). A conscript at close range can reach it.
7. **Under fire:** an agent is under fire when it is the target of a shot, or a round passes it within
   **10 m [PROPOSAL]** (`m_b ≤ 10 m`) with a clear sight line from the shooter. Being under fire adds the shooter to
   its attackers (0002's duty to return fire) and starts the whole drill. Only hearing a shot does neither.

Worked examples, regular agent standing in the open:

| Fire | Pressure | Result |
|---|---|---|
| One aimed rifle shot from 100 m (m = 0.3 m) | 1.2 · 0.86 ≈ 1.03 | suppressed, ends after ≈ 8 s |
| One aimed rifle shot from 500 m (m = 1.5 m) | 1.2 · 0.47 ≈ 0.57 | **not** suppressed |
| Aimed rifle fire from 500 m, every 2 s | 0.57, 0.97, 1.25, … levels off at ≈ 1.9 | suppressed from the 3rd shot, never heavily |
| Aimed MG bursts from 300 m, every 2 s (m = 0.9 m) | 5 · 1.5 · 0.64 ≈ 4.8 per burst: 4.8, 8.2, …, levels off at ≈ 16 | suppressed at once, heavily from the 2nd burst; ≈ 24 s to recover after the fire stops |
| One rifle shot from 100 m at a fighter 3 m beside the target (b = 3 m) | 1.2 · e^(−1.51) ≈ 0.27 | not suppressed by one shot; under fire, starts the drill |
| Point-patrol contact at 200 m; fighter at the rear of the wedge, 15 m from the ray | m_b ≈ 15 m | no pressure to speak of, not under fire: not pinned down |

### Positions (CP)
Two kinds of position, judged against every attacker the agent knows of (its attackers that are not out of action):
- **Cover:** no attacker can see the agent there lying prone (0002 line of sight). The agent may not see them either.
- **Firing position:** cover, and from there the agent sees an attacker when crouching, with a clear fire sector (LF)
  towards it: behind a low rise or at the edge of a forest.

The agent is **in danger of being hit** when an attacker can see it where it is, in its posture.

A search looks within **20 m [PROPOSAL]** of where the agent is, on a **2 m grid [PROPOSAL]**, for points it can reach
without crossing a hard boundary (v0.1 rules). The search is deterministic:
- **Cover:** nearest first, ties by grid order.
- **Firing position, forward by default:** first the points no farther from the attacker than the agent is now,
  nearest first; only if there are none, the points behind. Ties by grid order.

### Moving in the drill
| Agent's state | Moves to cover or a firing position |
|---|---|
| Not suppressed | rushing, crouching (decided) |
| Lightly suppressed (`τ ≤ L < 2 τ`) | crawling, prone |
| Suppressed from `2 τ`, or heavily | does not move |

The agent re-checks after every tick, so it speeds up, slows down or stops as its pressure changes.

### The immediate action drill under fire (DR)
An agent under `ReturnFire` or `FreeFire` without a `FireAt` order starts the drill when it comes under fire (SU7).
Its current movement order is suspended. `ContactDrillStageChanged(agent, stage)` is logged at each stage. Every
shot in the drill needs a clear fire sector (LF).

1. **Snap shots** (`SnapShots`): the agent drops prone at once. After its reaction time it fires **3** snap shots
   (decided) at the nearest attacker it sees (by sight or muzzle flash), one per snap interval. An attacker it cannot
   see, or cannot fire at, gets none. The stage ends after the third snap interval.
2. **Into cover** (`MovingToCover`): if it is in danger of being hit and there is cover within reach, it moves there
   as "Moving in the drill" allows, without firing. Otherwise it skips this stage.
3. **To a firing position** (`MovingToFiringPosition`): if it is not at a firing position with a clear sector and
   there is one within reach, it moves there, forward by default, as "Moving in the drill" allows, even out of cover.
   Otherwise it skips this stage.
4. **Return fire** (`ReturningFire`): aimed shots at the nearest attacker it sees clearly, crouching at a firing
   position or prone elsewhere; snap shots at an attacker seen only by its muzzle flash; no fire when it sees none or
   its sector is not clear. An agent in cover without a firing position stays there and holds fire.

Whenever the agent is suppressed it goes prone (0002). Heavily suppressed, it does not fire.

**Waiting for orders:** at the end of stage 1 the agent reports contact, `ContactReported(agent, attacker, attacker
position)`, and from then on waits for orders (`Unit.IsAwaitingOrders`) while it carries on with the drill. It does
**not** resume its suspended order by itself, however long it waits. Any new order (`MoveTo`, `MoveThroughCorridor`,
`Stop`, `FireAt`, `CeaseFire`, later `BreakContact` and `Hook`) ends the drill.

**Ammunition reserve:** fire without an order stops when **20 % [PROPOSAL]** of the rounds are left. A `FireAt` order
may use the reserve.

Unchanged from 0002: a `HoldFire` agent never fires on its own and does not run the drill; an agent with a `FireAt`
order follows 0002 and its doctrine (`ReturnFireOverridesFireAt`).

The drill's values (reaction times, snap shots, light-suppression limit, search radius, fire-sector angle, reserve) are kept as data in
`Doctrine`, so a scenario can calibrate them without code changes.

### Event log (D)
- `ShotFired(shooter, target, hit)` gains `mode` and `rounds`.
- `SuppressionStarted(agent, until)` loses `until`, which is no longer known in advance; it gains `heavy`.
  `SuppressionEnded(agent)` stays. A change from suppressed to heavily suppressed logs a new `SuppressionStarted`.
- New: `ContactDrillStageChanged(agent, stage)`, `ContactReported(agent, attacker, attackerPosition)`.
- `EventReplay` rebuilds suppression, heavy suppression, the drill stage and waiting for orders from the log. The
  pressure level itself is not logged.
- The client and `./dev replay` read `SuppressionStarted`; they drop `until` too.

## Acceptance criteria and planned tests
Pure tests in `tests/Twin.Sim.Tests/Combat/`, scenario tests in `tests/Twin.Scenarios.Tests/Combat/`.

### X: experience (`ExperienceTests.cs`, pure and scenario)
| ID | Test | Setup → expected |
|---|---|---|
| X1 | `X1_the_default_experience_is_regular_and_keeps_the_0002_hit_probability` | default combatant → P(hit) as in 0002 |
| X2 | `X2_experience_scales_the_hit_probability` | factors 0.8, 1.0, 1.3 applied exactly |
| X3 | `X3_special_forces_fire_their_first_snap_shot_sooner_than_conscripts` | same contact → first snap shot after 0.5 s and 2.0 s (± one tick) |
| X4 | `X4_special_forces_are_harder_to_suppress` | the same fire suppresses a conscript but not a special-forces soldier |
| X5 | `X5_special_forces_recover_from_suppression_sooner` | same pressure → suppression ends after H·log2(L / 0.25τ) for each |

### M: fire modes (`FireModeTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| M1 | `M1_a_snap_shot_hits_with_a_quarter_of_the_aimed_probability` | pure: K = 0.25 exactly |
| M2 | `M2_special_forces_at_close_range_land_hits_with_snap_shots` | statistical: SF at 25 m, hit rate ≈ 0.17 within ±4σ |
| M3 | `M3_fire_at_orders_and_free_fire_engagements_are_aimed` | `ShotFired.mode == Aimed` |
| M4 | `M4_snap_shots_come_at_the_snap_interval` | consecutive snap shots are 0.7 s apart (± one tick) |
| M5 | `M5_a_burst_is_one_shot_event_with_one_draw` | MG burst → one `ShotFired` with rounds = 5; P = 1 − (1 − p)^5 |

### LF: fire sector (`FireSectorTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| LF1 | `LF1_no_shot_is_fired_with_an_own_fighter_within_thirty_degrees_of_the_muzzle` | friend 25° off the direction to the enemy, nearer than it → no shot, under any rule or order |
| LF2 | `LF2_a_friend_beyond_the_target_inside_the_sector_also_blocks_fire` | friend behind the enemy, 10° off → no shot |
| LF3 | `LF3_a_friend_outside_the_sector_or_behind_the_shooter_does_not` | friends 35° off and 10 m behind → shots |
| LF4 | `LF4_the_shooter_fires_once_the_sector_clears` | friend walks out of the sector → first shot once it is past 30° |

### SU: suppression (`SuppressionModelTests.cs` pure, `SuppressionTests.cs` scenario)
| ID | Test | Setup → expected |
|---|---|---|
| SU1 | `SU1_miss_distance_grows_with_range_and_shrinks_with_aim_and_skill` | pure: m = d · σ_M · σ_X · Q exactly |
| SU2 | `SU2_a_round_suppresses_less_the_farther_it_passes` | pure: p = S · exp(−m / 2) · C |
| SU3 | `SU3_cover_reduces_pressure` | pure: forest T and prone posture multiply p; a blocked line gives 0 |
| SU4 | `SU4_a_close_rifle_shot_suppresses` | one miss from 100 m → suppressed, prone, in place |
| SU5 | `SU5_a_single_distant_rifle_shot_does_not_suppress` | one miss from 500 m → not suppressed |
| SU6 | `SU6_suppression_ends_when_pressure_falls_below_a_quarter_of_the_threshold` | end time = last shot + H · log2(L / 0.25τ), ± one tick |
| SU7 | `SU7_sustained_fire_keeps_the_target_suppressed` | aimed rifle fire from 500 m every 2 s → suppressed from the 3rd shot until after the fire stops |
| SU8 | `SU8_machine_gun_fire_suppresses_far_more_than_rifle_fire` | MG vs rifle, same range, same time → MG target heavily suppressed and suppressed longer |
| SU9 | `SU9_a_heavily_suppressed_agent_does_not_fire` | under MG fire → no `ShotFired` by it, though it sees the gunner |
| SU10 | `SU10_sustained_rifle_fire_alone_does_not_suppress_heavily` | aimed rifle fire from 100 m for 60 s → suppressed, never heavily |
| SU11 | `SU11_suppression_draws_no_random_numbers` | in a fixed duel, a fresh generator with the same seed and the formula's P(hit) reproduces every hit |
| SU12 | `SU12_a_round_passes_others_at_their_distance_from_its_path` | pure: m_b = √(m² + b²); nobody behind the shooter; a hidden bystander gets 0 |
| SU13 | `SU13_a_fighter_beside_the_target_is_suppressed_less_than_the_target` | 3 aimed MG bursts at A from 100 m, B 3 m beside → A heavily suppressed, B only suppressed, and B's suppression ends sooner |
| SU14 | `SU14_a_round_passing_within_ten_meters_starts_the_whole_drill` | B 3 m beside A → B logs `ContactDrillStageChanged` and its first snap shot targets the shooter |
| SU15 | `SU15_point_patrol_contact_does_not_pin_down_the_rear_of_the_wedge` | wedge, point patrol fired on from 200 m → rear fighters never suppressed and no drill; they hear the shots |

### CP: positions (`CoverPositionTests.cs`, pure)
| ID | Test | Setup → expected |
|---|---|---|
| CP1 | `CP1_behind_a_high_ridge_is_cover_but_not_a_firing_position` | 3 m ridge → hidden prone, cannot see crouching |
| CP2 | `CP2_behind_a_low_rise_is_a_firing_position` | 0.5 m rise → hidden prone, sees crouching |
| CP3 | `CP3_on_open_ground_there_is_neither` | flat open map → none |
| CP4 | `CP4_the_nearest_position_within_twenty_meters_is_chosen` | rises at 8 m and 15 m → the 8 m one; one at 25 m → none |
| CP5 | `CP5_a_position_behind_a_hard_boundary_is_not_chosen` | covered spot across a no-crossing road → not chosen |
| CP6 | `CP6_a_position_without_a_clear_fire_sector_is_not_a_firing_position` | team-mate within 30° of the direction from it to the attacker → not a firing position |
| CP7 | `CP7_a_position_must_hide_the_agent_from_every_known_attacker` | two attackers, the rise covers from one only → not cover |
| CP8 | `CP8_a_firing_position_forward_is_preferred_to_a_nearer_one_behind` | firing positions 12 m ahead and 6 m behind → the one ahead; only the one behind → it |

### DR: the drill (`ContactDrillTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| DR1 | `DR1_under_fire_the_agent_drops_prone_and_fires_three_snap_shots_after_its_reaction_time` | regular, not suppressed → prone at once; 3 snap shots from 1.5 s, 0.7 s apart |
| DR2 | `DR2_in_danger_of_being_hit_it_first_moves_into_cover` | attacker sees it; ridge 8 m away → moves behind it crouching, no shots on the way |
| DR3 | `DR3_from_cover_it_moves_on_to_a_firing_position_and_fires_aimed_shots` | ridge at 8 m, low rise at 14 m → cover, then firing position, then aimed shots crouching |
| DR4 | `DR4_in_cover_without_a_firing_position_it_holds_fire` | only the ridge → stays behind it, no aimed shots |
| DR5 | `DR5_without_cover_nearby_it_stays_and_fires_aimed_shots` | open map → stays, aimed shots prone |
| DR6 | `DR6_lightly_suppressed_it_crawls_to_cover` | pressure between τ and 2τ → moves prone at crawling speed |
| DR7 | `DR7_more_heavily_suppressed_it_stays_where_it_is` | pressure above 2τ → no movement until it falls below 2τ, then crawls on |
| DR8 | `DR8_it_leaves_cover_for_a_firing_position_when_suppression_allows` | ridge at 8 m, low rise 6 m farther forward → leaves the ridge for the rise |
| DR9 | `DR9_an_attacker_seen_only_by_its_muzzle_flash_gets_snap_shots_not_aimed_ones` | attacker in 90 m of forest → no aimed shots at it |
| DR10 | `DR10_at_the_rear_of_a_wedge_it_moves_forward_to_fire_past_its_team_mates` | rear fighter under fire, team-mates in its sector → no shots where it is; moves forward to a firing position with a clear sector, then fires |
| DR11 | `DR11_it_reports_contact_and_waits_instead_of_resuming_its_corridor` | corridor order, fire stops → `ContactReported` once; off its corridor and waiting for orders 10 min later |
| DR12 | `DR12_a_new_order_ends_the_wait` | `MoveThroughCorridor` while waiting → moves on, drill over |
| DR13 | `DR13_a_hold_fire_agent_does_not_run_the_drill` | `HoldFire` → no shots, no `ContactReported`, behaves as in 0002 |
| DR14 | `DR14_an_agent_with_a_fire_at_order_keeps_its_ordered_target` | ordered target engageable → no drill shots at the attacker |
| DR15 | `DR15_fire_without_an_order_keeps_a_reserve_of_rounds` | 10 rounds → 8 fired in the drill, then none; a `FireAt` order fires the last 2 |

### D: determinism and log (`CombatDeterminismTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| D4 | `D4_suppression_drill_stage_and_waiting_rebuilt_from_the_log_match_the_live_state` | D3 with the new events |

### Existing tests the new rules re-specify
The spec phase changes these. They describe 0002 rules this ticket replaces; this is a decided change of behaviour,
not a weakened test.
- `EffectsTests.E5_a_shot_at_an_agent_suppresses_it_for_ten_seconds`: a 500 m miss no longer suppresses. Replaced by
  SU4–SU6.
- `EffectsTests.E6_each_new_shot_extends_suppression`: the 10 s rule. Replaced by SU6–SU7.
- `EngagementTests.R9_agents_never_shoot_their_own_side`: its friend stands exactly on the line between shooter and
  enemy and the test expects shots. Under LF there are none. Move the friend out of the 60° sector (LF covers the
  case inside it).
- `DoctrineTests` permission answers while suppressed: they use `SuppressionSeconds`, which goes away. Re-expressed
  with "after `SuppressionEnded`".
- `CombatDeterminismTests.D3`: the event fields change (becomes D4).
- `SpecShooting`: `SuppressionSeconds` removed; the values above added.
- To check in the spec phase, as the drill now changes what a fired-on `ReturnFire`/`FreeFire` agent does:
  `EngagementTests.R3`, `R5`, `R8`, `CasualtyTests`, the effect tests where the target runs the drill, and the st1
  acceptance test `K2`.

## Kit additions (spec phase, `tests/Twin.Testing`)
- `SpecSuppression`: the values above and the formulas (miss distance, pressure, decay, release time).
- `WithFighter(..., experience)`; weapons `SpecSuppression.MachineGun`, `HeavyMachineGun`.
- A `Scenario.WithWedge(...)` that places a fire team in a wedge.
- Assertions: `.BeHeavilySuppressed()`, `.BeInDrillStage(ContactDrillStage.ReturningFire)`, `.BeAwaitingOrders()`.

## Open questions
None as of 2026-11-17. The [PROPOSAL] values stand until calibration.

## TODO
- **Scanning range:** no agent scans beyond **2 km** (a magic number, to be calibrated). This covers the per-tick
  searches over other agents: target choice and seeing (`Sees`), the fire-sector check (LF), bystander pressure (SU2)
  and the positions' attacker checks (CP). Kept as one value in `CombatRules`, so it is tuned in one place. Weapon
  ranges stay as they are; the limit only cuts work that cannot matter. The spec phase decides whether this ticket
  includes it or it becomes its own small ticket.

## Later tickets
- `BreakContact` (irti) and `Hook` (haka) orders, and a scripted leader who gives them.
- Chain of command: a fallen or incapacitated leader is replaced in order of succession.
- Ordered suppressive fire and area fire at a heard or last known position.
- Unit-level reactions to contact beyond each agent's own drill.
- Calibration of the drill's timings, the reserve and the weapon values.
