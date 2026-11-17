# 0003. Suppression as fire pressure, experience, and the immediate action drill under fire

**Status:** proposal (2026-11-17), waiting for review before the spec phase. Builds on 0002 (shooting).

Decided with the product owner on 2026-11-17:

- Returning fire aims to **suppress**, not to cause casualties. It may still hit, rarely; a special-forces soldier at
  close range is expected to land effective hits.
- Suppression depends on what passes the target: high-calibre machine-gun fire close overhead suppresses far more
  than a stray round passing far away. The 0002 rule "every shot suppresses for 10 s" is **replaced**.
- Fire effect depends on the shooter's **experience** (conscript/reservist vs special forces), **range**, the
  **quality of observation** and the target's **cover**.
- Fire also suppresses **fighters next to the target**, usually less. It depends on the situation: when a point patrol
  makes contact, the fighters at the rear of the wedge are not paralysed, at least not completely.
- Under fire an agent runs the immediate action drill: **1) a few snap shots while taking cover, 2) when suppression
  allows, move to a covered position and/or return fire with aimed shots, 3) wait for the leader's orders** (break
  contact, hook, …). The leader's orders themselves are later tickets.
- The agent waits for orders indefinitely by default. The later goal is that a fallen or incapacitated leader is
  replaced along the chain of command (later ticket).
- The drill keeps an ammunition reserve by default.
- `HoldFire` is unchanged: no fire without an order, even under fire.
- No fire at a position that is only heard; that needs area fire and is a later ticket (0002's rule stands).
- An agent that opens fire on its own under `FreeFire` uses aimed fire.
- The drill's timings, the reserve and the weapon values need calibration. Weapon values are left open for now.

Values marked **[PROPOSAL]** are starting values for review. As in 0002, tests read them from one place in
`tests/Twin.Testing`, so calibration later does not change what a test checks.

## Story
As an exercise designer, I can see a soldier who comes under fire fire a few snap shots as he goes to ground, then,
once the fire lets him, rush to a covered firing position and answer with aimed shots, and wait for orders, as the
drill says. A machine gun sweeping close overhead keeps a squad down for long; a few stray rounds from far away barely
do. When the point patrol of a wedge makes contact, the fighters at the rear are not pinned down by it. Special forces
react faster, shoot better and shake off fire sooner than conscripts.

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
- **Aimed:** a `FireAt` order, fire opened on one's own under `FreeFire`, and drill stage 2. As in 0002.
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
   weapon's maximum range. Any other agent in front of the shooter, on either side, is passed at
   `m_b = √(m² + b²)`, with `b` its perpendicular distance from that ray. The shooter itself is never passed.
3. **Pressure from one round:** `p = S · exp(−m / r) · C` (or `m_b` for others), with **r = 2 m [PROPOSAL]**.
4. **Cover `C`:** `C = T · Cover(posture)`, with T the forest transmission on the line from the shooter to that agent,
   and `Cover` 1.0 standing, 0.8 crouching, 0.6 prone **[PROPOSAL]**. A blocked line (ridge, building) gives C = 0.
5. **Accumulation:** every agent has a pressure `L ≥ 0`. A shot adds `n · p`. `L` halves every `H` (that agent's
   experience).
6. **States:**
   - **Suppressed** when `L ≥ τ`; ends when `L` falls below `0.25 τ` **[PROPOSAL]**. The gap stops the state
     flickering. Effects as in 0002: prone, stops moving, hit factor 0.3.
   - **Heavily suppressed** when `L ≥ 3 τ` **[PROPOSAL]**: as suppressed, and it does not fire at all.
7. **Under fire:** an agent is under fire when it is the target of a shot, or a round passes it within
   **10 m [PROPOSAL]** (`m_b ≤ 10 m`) with a clear line from the shooter. Being under fire adds the shooter to its
   attackers (0002's duty to return fire) and starts the drill. Only hearing a shot does neither.

Worked examples, regular agent standing in the open:

| Fire | Pressure | Result |
|---|---|---|
| One aimed rifle shot from 100 m (m = 0.3 m) | 1.2 · 0.86 ≈ 1.03 | suppressed, ends after ≈ 8 s |
| One aimed rifle shot from 500 m (m = 1.5 m) | 1.2 · 0.47 ≈ 0.57 | **not** suppressed |
| Aimed rifle fire from 500 m, every 2 s | 0.57, 0.97, 1.25, … levels off at ≈ 1.9 | suppressed from the 3rd shot |
| Aimed MG bursts from 300 m, every 2 s (m = 0.9 m) | 5 · 1.5 · 0.64 ≈ 4.8 per burst, levels off at ≈ 16 | heavily suppressed at once; ≈ 24 s to recover after the fire stops |
| The same rifle shot from 100 m, at a fighter 3 m beside the target (b = 3 m) | 1.2 · e^(−1.52) ≈ 0.26 | not suppressed by one shot; under fire, starts the drill |
| Point-patrol contact at 200 m; fighter at the rear of the wedge, 15 m from the ray | m_b ≈ 15 m | no pressure to speak of, not under fire: not pinned down |

### Cover positions (CP)
A **covered firing position** is a point where the attacker cannot see the agent lying prone (0002 line of sight),
but the agent sees the attacker when crouching: behind a low rise or at the edge of a forest. The agent looks for one
within **20 m [PROPOSAL]** of where it is, on a **2 m grid [PROPOSAL]**, nearest first, ties by grid order, with a
route its movement profile can take (no hard boundary crossed, the 0002/v0.1 rules). The search is deterministic.

### The immediate action drill under fire (DR)
An agent under `ReturnFire` or `FreeFire` without a `FireAt` order starts the drill when it comes under fire (SU7).
Its current movement order is suspended. `ContactDrillStageChanged(agent, stage)` is logged at each stage.

1. **Snap shots while taking cover** (`TakingCover`): the agent drops prone at once. After its reaction time it fires
   **3 [PROPOSAL]** snap shots at the nearest attacker it sees (by sight or muzzle flash), one per snap interval. An
   attacker it cannot see gets none. The stage ends after the last snap shot, or after reaction time + 3 snap
   intervals if it could not fire.
2. **Cover and aimed fire** (`ReturningFire`), as soon as the agent is not suppressed:
   - If a covered firing position (CP) exists, it rushes there crouching, then fires aimed shots from it, crouching.
   - Otherwise it stays where it is and fires aimed shots, prone.
   - At an attacker seen only by its muzzle flash, it fires snap shots instead. At one it cannot see, it holds fire.
   - Whenever it is suppressed again, it goes prone and stops (0002); it carries on when the suppression ends.
3. **Awaiting orders** (`AwaitingOrders`): at the start of stage 2 the agent reports contact,
   `ContactReported(agent, attacker, attacker position)`, and from then waits for orders while it carries on with
   stage 2. It does **not** resume its suspended order by itself, however long it waits. Any new order (`MoveTo`,
   `MoveThroughCorridor`, `Stop`, `FireAt`, `CeaseFire`, later `BreakContact` and `Hook`) ends the drill.

**Ammunition reserve:** fire without an order stops when **20 % [PROPOSAL]** of the rounds are left. A `FireAt` order
may use the reserve.

Unchanged from 0002: a `HoldFire` agent never fires on its own and does not run the drill; an agent with a `FireAt`
order follows 0002 and its doctrine (`ReturnFireOverridesFireAt`).

The drill's values (reaction times, snap shots, search radius, reserve) are kept as data in `Doctrine`, so a scenario
can calibrate them without code changes.

### Event log (D)
- `ShotFired(shooter, target, hit)` gains `mode` and `rounds`.
- `SuppressionStarted(agent, until)` loses `until`, which is no longer known in advance; it gains `heavy`.
  `SuppressionEnded(agent)` stays. A change from suppressed to heavily suppressed logs a new `SuppressionStarted`.
- New: `ContactDrillStageChanged(agent, stage)`, `ContactReported(agent, attacker, attackerPosition)`.
- `EventReplay` rebuilds suppression, heavy suppression and the drill stage from the log. The pressure level itself is
  not logged.
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
| SU10 | `SU10_suppression_draws_no_random_numbers` | in a fixed duel, a fresh generator with the same seed and the formula's P(hit) reproduces every hit |
| SU11 | `SU11_a_round_passes_others_at_their_distance_from_its_path` | pure: m_b = √(m² + b²); nobody behind the shooter; a hidden bystander gets 0 |
| SU12 | `SU12_a_fighter_beside_the_target_is_suppressed_less_than_the_target` | aimed MG bursts at A from 100 m, B 3 m beside → B suppressed too, not heavily, and its suppression ends sooner than A's; aimed rifle fire at A does not suppress B |
| SU13 | `SU13_a_fighter_beside_the_target_comes_under_fire_and_starts_the_drill` | B 3 m beside A → B's first snap shot targets the shooter |
| SU14 | `SU14_point_patrol_contact_does_not_pin_down_the_rear_of_the_wedge` | wedge, point patrol fired on from 200 m → rear fighters never suppressed and no drill; they hear the shots |

### CP: cover positions (`CoverPositionTests.cs`, pure)
| ID | Test | Setup → expected |
|---|---|---|
| CP1 | `CP1_behind_a_low_rise_is_a_covered_firing_position` | rise 0.5 m high between agent and attacker → hidden prone, sees crouching |
| CP2 | `CP2_on_open_ground_there_is_no_covered_firing_position` | flat open map → none |
| CP3 | `CP3_the_nearest_covered_firing_position_within_twenty_meters_is_chosen` | two rises at 8 m and 15 m → the 8 m one; one at 25 m → none |
| CP4 | `CP4_a_position_behind_a_hard_boundary_is_not_chosen` | covered spot across a no-crossing road → not chosen |

### DR: the drill (`ContactDrillTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| DR1 | `DR1_under_fire_the_agent_drops_prone_and_fires_three_snap_shots_after_its_reaction_time` | regular, not suppressed → prone at once; 3 snap shots from 1.5 s, 0.7 s apart |
| DR2 | `DR2_when_not_suppressed_it_rushes_to_a_covered_firing_position_and_fires_aimed_shots` | rise 10 m away → moves there crouching, then aimed shots |
| DR3 | `DR3_without_cover_nearby_it_stays_and_fires_aimed_shots` | open map → stays, aimed shots prone |
| DR4 | `DR4_while_suppressed_it_neither_moves_to_cover_nor_fires_aimed_shots` | sustained fire → stays prone in place until the suppression ends, then moves |
| DR5 | `DR5_an_attacker_seen_only_by_its_muzzle_flash_gets_snap_shots_not_aimed_ones` | attacker in 90 m of forest → no aimed shots at it |
| DR6 | `DR6_it_reports_contact_and_waits_instead_of_resuming_its_corridor` | corridor order, fire stops → `ContactReported` once; still not on its corridor and `AwaitingOrders` 10 min later |
| DR7 | `DR7_a_new_order_ends_the_wait` | `MoveThroughCorridor` while waiting → moves on, drill over |
| DR8 | `DR8_a_hold_fire_agent_does_not_run_the_drill` | `HoldFire` → no shots, no `ContactReported`, behaves as in 0002 |
| DR9 | `DR9_an_agent_with_a_fire_at_order_keeps_its_ordered_target` | ordered target engageable → no drill shots at the attacker |
| DR10 | `DR10_fire_without_an_order_keeps_a_reserve_of_rounds` | 10 rounds → 8 fired in the drill, then none; a `FireAt` order fires the last 2 |

### D: determinism and log (`CombatDeterminismTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| D4 | `D4_suppression_and_drill_stage_rebuilt_from_the_log_match_the_live_state` | D3 with the new events |

### Existing tests the new rules re-specify
The spec phase changes these. They describe the 0002 rule this ticket replaces; this is a decided change of
behaviour, not a weakened test.
- `EffectsTests.E5_a_shot_at_an_agent_suppresses_it_for_ten_seconds`: a 500 m miss no longer suppresses. Replaced by
  SU4–SU6.
- `EffectsTests.E6_each_new_shot_extends_suppression`: the 10 s rule. Replaced by SU6–SU7.
- `DoctrineTests` permission answers while suppressed: they use `SuppressionSeconds`, which goes away. Re-expressed
  with "after `SuppressionEnded`".
- `CombatDeterminismTests.D3`: the event fields change (becomes D4).
- `SpecShooting`: `SuppressionSeconds` removed; the values above added.
- To check in the spec phase, as the drill now changes what a fired-on `ReturnFire`/`FreeFire` agent does:
  `EngagementTests.R3`, `R5`, `R8`, `CasualtyTests`, the effect tests where the target runs the drill, and the st1
  acceptance tests `K2`.

## Kit additions (spec phase, `tests/Twin.Testing`)
- `SpecSuppression`: the values above and the formulas (miss distance, pressure, decay, release time).
- `WithFighter(..., experience)`; weapons `SpecSuppression.MachineGun`, `HeavyMachineGun`.
- A `Scenario.WithWedge(...)` that places a fire team in a wedge.
- Assertions: `.BeHeavilySuppressed()`, `.BeInDrillStage(ContactDrillStage.ReturningFire)`.

## Open questions
1. **Experience levels:** are three right, and the names? Is "regular" the right default?
2. **Heavy suppression:** should it stop all fire (proposal), or only lower accuracy further?
3. **Cover without a field of fire:** if there is only cover where the agent cannot see the attacker, should it move
   there and hold fire, or stay and fire (proposal)? The decision said "move to cover **and/or** return fire".
4. **Rushing to cover:** crouching at half speed (proposal), standing at full speed, or crawling?
5. **Under-fire radius (SU7):** is 10 m from the round's path right, and should a near miss on a team-mate start the
   drill at all, or only make the agent alert?
6. **Snap shots:** is 3 right, and should the number depend on experience?

## Later tickets
- `BreakContact` (irti) and `Hook` (haka) orders, and a scripted leader who gives them.
- Chain of command: a fallen or incapacitated leader is replaced in order of succession.
- Ordered suppressive fire and area fire at a heard or last known position.
- Unit-level reactions to contact beyond each agent's own drill.
- Calibration of the drill's timings, the reserve and the weapon values.
