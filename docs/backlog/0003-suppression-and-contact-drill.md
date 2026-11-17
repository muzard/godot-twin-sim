# 0003. Suppression as fire pressure, experience, and the immediate action drill under fire

**Status:** proposal (2026-11-17), waiting for review before the spec phase. Builds on 0002 (shooting).
Decided with the product owner on 2026-11-17:

- returning fire aims to **suppress**, not to cause casualties; it may still hit, rarely, and a special-forces
  soldier at close range is expected to land effective hits
- suppression depends on what passes the target: high-calibre machine-gun fire close overhead suppresses far more
  than a stray round passing far away. The 0002 rule "every shot suppresses for 10 s" is **replaced**
- fire effect depends on the shooter's **experience** (conscript/reservist vs special forces), **range**, the
  **quality of observation** and the target's **cover**
- under fire an agent runs an immediate action drill: **1) return fire, 2) start accurate fire, 3) wait for the
  leader's orders** (break contact, hook, …). The leader's orders themselves are later tickets
- no fire at a position that is only heard; that needs area fire and is a later ticket (0002's rule stands)
- an agent that opens fire on its own under `FreeFire` uses aimed fire

Values marked **[PROPOSAL]** are starting values for review. As in 0002, tests read them from one place in
`tests/Twin.Testing`, so calibration later does not change what a test checks.

## Story
As an exercise designer, I can see a soldier who comes under fire go to ground, answer the fire to keep the enemy's
head down, then switch to accurate fire and wait for orders, as the drill says. A machine gun sweeping close overhead
keeps a squad down for long; a few stray rounds from far away barely do. Special forces react faster, shoot better and
shake off fire sooner than conscripts.

## Model

### Experience (X)
`Combatant` gets an `Experience`: `Conscript`, `Regular` (default), `SpecialForces` **[PROPOSAL: names and count]**.
The default keeps every existing scenario as it is.

| | Conscript | Regular | Special forces |
|---|---|---|---|
| Accuracy factor `A` (multiplies P(hit)) | 0.8 | 1.0 | 1.3 |
| Dispersion factor `σ_X` (multiplies miss distance) | 1.3 | 1.0 | 0.6 |
| Reaction time before returning fire | 2.0 s | 1.5 s | 0.5 s |
| Return-fire stage length (drill stage 1) | 8 s | 5 s | 3 s |
| Suppression threshold `τ` | 0.8 | 1.0 | 1.5 |
| Pressure half-life `H` | 6 s | 4 s | 2.5 s |

All values **[PROPOSAL]**.

### Fire modes (M)
Each shot is either `Aimed` or `Suppressive`; `ShotFired` records the mode.
- **Aimed:** a `FireAt` order, fire opened on one's own under `FreeFire`, and drill stage 2. As in 0002.
- **Suppressive:** drill stage 1. Faster, less accurate, made to keep the enemy down.

| | Aimed | Suppressive |
|---|---|---|
| Interval (rifle) | 2 s | 1 s |
| Hit factor `K` | 1 | 0.25 |
| Angular dispersion `σ_M` | 3 mil | 6 mil |

All values **[PROPOSAL]**.

`P(hit) = 0002 formula · A · K` per round. Regular aimed fire gives exactly the 0002 value. Example: special forces,
suppressive, 25 m in the open against a standing target: 0.6 · e^(−25/150) · 1.3 · 0.25 ≈ 0.17 per round.

### Weapons (W)
`WeaponProfile` gets:
- `SuppressionPerRound` `S`: how much a round passing close suppresses (calibre, crack, volume)
- `RoundsPerBurst` `n`: rounds in one shot event (1 for a rifle)
- `SuppressiveSecondsPerShot`: interval in suppressive mode

| | Rifle (0002) | Machine gun 7.62 | Heavy machine gun 12.7 |
|---|---|---|---|
| `S` | 1.2 | 1.5 | 4.0 |
| `n` | 1 | 5 | 5 |
| Aimed / suppressive interval | 2 s / 1 s | 2 s / 1 s | 3 s / 1.5 s |
| Range, rounds | 600 m, 150 | 800 m, 500 | 1500 m, 200 |

All values **[PROPOSAL]**. A burst is one `ShotFired` with `rounds = n` and **one** draw: P(burst hits) =
1 − (1 − p)^n. One draw per shot event keeps the random stream as in 0002.

### Observation quality (Q)
How well the shooter sees its target scales the miss distance:
- **Seen** (sight line clear, T ≥ T_min): factor 1
- **Muzzle flash only** (visible only through `T_flash`): factor 2; only suppressive fire is possible, never aimed
- **Heard only:** no fire (unchanged from 0002)

All values **[PROPOSAL]**.

### Suppression as fire pressure (SU)
This replaces 0002's "a shot suppresses for 10 s".

1. **Miss distance:** each round passes the target at the expected distance
   `m = d · σ_M · σ_X · Q`, with d the range and σ_M in radians (1 mil = 0.001 rad). It is an expected value, not a
   draw, so suppression uses no random numbers.
2. **Pressure from one round:** `p = S · exp(−m / r) · C`, with **r = 2 m [PROPOSAL]**.
3. **Cover `C`:** `C = T · Cover(target posture)`, with T the forest transmission on the shooter's line, and
   `Cover` 1.0 standing, 0.8 crouching, 0.6 prone **[PROPOSAL]**. The data has no other cover yet.
4. **Accumulation:** every agent has a pressure `L ≥ 0`. A shot adds `n · p`. `L` halves every `H` (target's
   experience).
5. **States:**
   - **Suppressed** when `L ≥ τ`; ends when `L` falls below `0.25 τ` **[PROPOSAL]**. The gap stops the state
     flickering. Effects as in 0002: prone, stops moving, hit factor 0.3.
   - **Heavily suppressed** when `L ≥ 3 τ` **[PROPOSAL]**: as suppressed, and it does not fire at all.

Worked examples, regular target standing in the open:

| Fire | Pressure | Result |
|---|---|---|
| One aimed rifle shot from 100 m (m = 0.3 m) | 1.2 · 0.86 ≈ 1.03 | suppressed, ends after ≈ 8 s |
| One aimed rifle shot from 500 m (m = 1.5 m) | 1.2 · 0.47 ≈ 0.57 | **not** suppressed |
| Aimed rifle fire from 500 m, every 2 s | 0.57, 0.97, 1.25, … levels off at ≈ 1.9 | suppressed from the 3rd shot |
| Suppressive MG bursts from 300 m, every 1 s (m = 1.8 m) | 5 · 1.5 · 0.41 ≈ 3.0 per burst, levels off at ≈ 19 | heavily suppressed; ≈ 25 s to recover after the fire stops |

### The immediate action drill under fire (DR)
An agent under `ReturnFire` or `FreeFire` without a `FireAt` order starts the drill when it is first shot at (it
becomes the target of a `ShotFired`). Its current movement order is suspended. `ContactDrillStageChanged(agent,
stage)` is logged at each stage.

1. **Return fire** (`ReturningFire`): the agent drops prone at once. After its reaction time it fires suppressively
   at the nearest attacker it sees (by sight or muzzle flash). The stage lasts the experience's stage length from the
   first shot at it.
2. **Accurate fire** (`AccurateFire`): aimed fire at an attacker it sees clearly. An attacker seen only by its muzzle
   flash still gets suppressive fire. No fire at an attacker it cannot see.
3. **Awaiting orders** (`AwaitingOrders`): at the start of stage 2 the agent reports contact,
   `ContactReported(agent, attacker, attacker position)`, and from then waits for orders. It keeps firing as in stage 2
   while it waits. It does **not** resume its suspended order by itself. Any new order (`MoveTo`,
   `MoveThroughCorridor`, `Stop`, `FireAt`, `CeaseFire`, later `BreakContact` and `Hook`) ends the drill.

Suppression always wins: while suppressed the agent stays prone and fires with the 0.3 factor; while heavily
suppressed it does not fire. The drill's clock keeps running meanwhile.

Unchanged from 0002: a `HoldFire` agent never fires on its own; an agent with a `FireAt` order follows 0002 and its
doctrine (`ReturnFireOverridesFireAt`).

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
| X3 | `X3_special_forces_return_fire_sooner_than_conscripts` | same contact → first return shot after 0.5 s and 2.0 s (± one tick) |
| X4 | `X4_special_forces_are_harder_to_suppress` | the same fire suppresses a conscript but not a special-forces soldier |
| X5 | `X5_special_forces_recover_from_suppression_sooner` | same pressure → suppression ends after H·log2(L / 0.25τ) for each |

### M: fire modes (`FireModeTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| M1 | `M1_suppressive_fire_hits_with_a_quarter_of_the_aimed_probability` | pure: K = 0.25 exactly |
| M2 | `M2_special_forces_at_close_range_land_hits_with_return_fire` | statistical: SF at 25 m, hit rate ≈ 0.17 within ±4σ |
| M3 | `M3_fire_at_orders_and_free_fire_engagements_are_aimed` | `ShotFired.mode == Aimed` |
| M4 | `M4_suppressive_fire_is_twice_as_fast` | consecutive suppressive rifle shots are 1 s apart |
| M5 | `M5_a_burst_is_one_shot_event_with_one_draw` | MG burst → one `ShotFired` with rounds = 5; P = 1 − (1 − p)^5 |

### SU: suppression (`SuppressionModelTests.cs` pure, `SuppressionTests.cs` scenario)
| ID | Test | Setup → expected |
|---|---|---|
| SU1 | `SU1_miss_distance_grows_with_range_and_shrinks_with_aim_and_skill` | pure: m = d · σ_M · σ_X · Q exactly |
| SU2 | `SU2_a_round_suppresses_less_the_farther_it_passes` | pure: p = S · exp(−m / 2) · C |
| SU3 | `SU3_cover_reduces_pressure` | pure: forest T and prone posture multiply p |
| SU4 | `SU4_a_close_rifle_shot_suppresses` | one miss from 100 m → suppressed, prone, in place |
| SU5 | `SU5_a_single_distant_rifle_shot_does_not_suppress` | one miss from 500 m → not suppressed, keeps walking |
| SU6 | `SU6_suppression_ends_when_pressure_falls_below_a_quarter_of_the_threshold` | end time = last shot + H · log2(L / 0.25τ), ± one tick; resumes order and posture |
| SU7 | `SU7_sustained_fire_keeps_the_target_suppressed` | aimed rifle fire from 500 m every 2 s → suppressed from the 3rd shot until after the fire stops |
| SU8 | `SU8_machine_gun_fire_suppresses_far_more_than_rifle_fire` | MG vs rifle, same range, same time → MG target heavily suppressed and suppressed longer |
| SU9 | `SU9_a_heavily_suppressed_agent_does_not_fire` | under MG fire → no `ShotFired` by it, though it sees the gunner |
| SU10 | `SU10_suppression_draws_no_random_numbers` | a fight with suppression draws exactly one number per `ShotFired`: in a fixed duel, a fresh generator with the same seed and the formula's P(hit) reproduces every hit |

### DR: the drill (`ContactDrillTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| DR1 | `DR1_under_fire_the_agent_drops_prone_and_returns_suppressive_fire_after_its_reaction_time` | regular shot at once, not suppressed → prone, first return shot suppressive after 1.5 s |
| DR2 | `DR2_after_the_return_fire_stage_it_switches_to_aimed_fire` | clearly seen attacker → aimed shots from 5 s on |
| DR3 | `DR3_an_attacker_seen_only_by_its_muzzle_flash_keeps_getting_suppressive_fire` | attacker in 90 m of forest → no aimed shots at it |
| DR4 | `DR4_it_reports_contact_and_waits_instead_of_resuming_its_corridor` | corridor order, fire stops → `ContactReported` once; still in place and `AwaitingOrders` 5 min later |
| DR5 | `DR5_a_new_order_ends_the_wait` | `MoveThroughCorridor` while waiting → moves on, drill over |
| DR6 | `DR6_a_hold_fire_agent_does_not_run_the_drill` | `HoldFire` → no shots, no `ContactReported`, behaves as in 0002 |
| DR7 | `DR7_an_agent_with_a_fire_at_order_keeps_its_ordered_target` | ordered target engageable → no drill shots at the attacker |

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
- `EngagementTests.R3`: should hold as it is (return fire is now delayed and suppressive); check in the spec phase.

## Kit additions (spec phase, `tests/Twin.Testing`)
- `SpecSuppression`: the values above and the formulas (miss distance, pressure, decay, release time).
- `WithFighter(..., experience)`; weapons `SpecSuppression.MachineGun`, `HeavyMachineGun`.
- Assertions: `.BeHeavilySuppressed()`, `.BeInDrillStage(ContactDrillStage.AccurateFire)`.

## Open questions
1. **Experience levels:** are three right, and the names? Is "regular" the right default?
2. **Heavy suppression:** should it stop all fire (proposal), or only lower accuracy further?
3. **End of drill stage 1:** fixed time by experience (proposal), or when the agent first sees its attacker clearly?
4. **Waiting for orders:** indefinitely (proposal, the drill says so), or should doctrine allow a timeout after which
   the agent resumes its order? Without a timeout, a scenario with no leader stops at the first contact.
5. **`HoldFire` under fire:** unchanged (proposal), or drop prone and report contact without firing?
6. **Ammunition:** should the drill keep a reserve, for example stopping suppressive fire at 20 % of rounds?
7. **Team-mates:** does an agent next to one that is shot at start the drill too? Proposal: not in this ticket; it
   belongs with unit-level reactions (fire teams).
8. **Weapon values:** `S`, burst size and intervals for the machine guns.

## Later tickets
- `BreakContact` (irti) and `Hook` (haka) orders, and a scripted leader who gives them.
- Area fire at a heard or last known position.
- Unit-level reactions to contact.
