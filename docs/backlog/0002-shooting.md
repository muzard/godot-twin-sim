# 0002. Shooting: line of sight, hit probability, effects and rules of engagement

**Status:** implemented (2026-11-09). Tests were written in the spec phase and reviewed before implementation; casualty care is only prepared for. Spec v0.1 left enemies, perception, line of sight and fire
out of scope; this ticket brings in the smallest playable version of them. Decided with the product owner
on 2026-11-08:

- hits use a hit probability, drawn from `Simulation.Random`
- forest attenuates line of sight and fire instead of blocking it outright
- a target has health states, and fire also suppresses
- agents shoot on an order or under a rule of engagement
- the first hit pins the target completely for 60 s
- shots make sound and a muzzle flash, depending on the weapon
- postures are in scope
- casualty care is prepared for here and implemented in a later ticket

Values marked **[PROPOSAL]** are starting values for review. As in v0.1, tests read them from one place
(`SpecShooting` in `tests/Twin.Testing`), so calibrating them later does not change what a test checks.

## Story
As an exercise designer, I can place two opposing agents on real terrain and see them engage each other. They shoot
when the rules of engagement allow it and they can see each other. They hit more often at short range, in the open and
against standing targets. They take cover under fire, and they hear and see where shots come from.

## Model

### Sides
Every agent belongs to a side (`Blue`, `Red`). Agents never shoot at their own side.

### Posture (P)
| Posture | Eye height | Aim point (target centre) | Speed factor | Target size factor |
|---|---|---|---|---|
| Standing | 1.6 m | 1.0 m | 1.0 | 1.0 |
| Crouching | 1.1 m | 0.7 m | 0.5 | 0.7 |
| Prone | 0.3 m | 0.2 m | 0.1 (crawling) | 0.4 |

All values in the table are **[PROPOSAL]**. Posture changes with a `SetPosture` order. A suppressed agent goes prone by
itself and returns to its previous posture when the suppression ends **[PROPOSAL]**. Changing posture takes
**1 s [PROPOSAL]**, during which the agent neither moves nor shoots. Eye height in v0.1 (`EyePosition`) becomes the
posture's eye height.

### Weapon
A `WeaponProfile` holds the weapon's values. The v0.1 infantry rifle **[PROPOSAL]**:
- hit curve `P_range(d) = 0.6 · exp(−d / 150 m)`, 0 beyond 600 m
- one aimed shot every 2 s, 150 rounds
- sound range 1500 m, muzzle flash 2 s

A suppressed weapon would have a shorter sound range and no flash. That variant only needs to exist so the tests
show the values come from the weapon.

### Line of sight (L)
The sight line runs from the observer's eye to the target's aim point, both from the posture table. It is sampled every
**0.5 m [PROPOSAL]**.
- **Terrain:** the line is blocked if any sample lies below the ground height `h_maa`.
- **Buildings:** the line is blocked if any sample lies in a building cell. The data has no building heights, so
  buildings block at any height. Later datasets are expected to fix this.
- **Forest:** forest does not block the line; it attenuates it. With `L_f` the length of the line that runs through
  forest cells below the canopy height of **15 m [PROPOSAL]** (until the `h_latvus` layer exists), transmission is
  `T = exp(−L_f / λ)`, with **λ = 25 m [PROPOSAL]**.
- **Seeing:** the target is visible when the line is not blocked and `T ≥ T_min`, with **T_min = 0.05 [PROPOSAL]**,
  about 75 m of forest.
- **Muzzle flash:** for the weapon's flash time after it fires, a shooter is visible down to **T_flash = 0.01
  [PROPOSAL]**, about 115 m of forest.

### Sound (S)
Every shot is heard by every agent within the weapon's sound range, terrain or not. A listener learns the shooter's
position as a contact (`ShotHeard`). v0.1 has no direction error. Hearing alone does not let an agent shoot back:
that still needs line of sight. It does trigger the rules of engagement below.

### Hit probability (F)
`P(hit) = P_range(d) · T · Size(target posture) · S_shooter · W_shooter`
- `T` is the forest transmission.
- `Size` is the target's posture factor.
- `S_shooter` is 0.3 if the shooter is suppressed, otherwise 1.
- `W_shooter` is 0.5 if the shooter is wounded, otherwise 1.

All factors are **[PROPOSAL]**. Each shot draws one number from `Simulation.Random`. Agents draw in id order and shots
in time order, so a run is deterministic.

### Effects (E)
- **Health:** `Unhurt → Wounded → OutOfAction`. A hit takes an unhurt target to `Wounded` and a wounded target to
  `OutOfAction`.
- **The first hit pins completely for 60 s.** The new health state `Pinned` lasts **60 s** from the hit. During it,
  the agent:
  - does not move, shoot, change posture or ask for permission
  - goes prone

  A second hit during the 60 s puts it out of action.
- **After the 60 s:** `Wounded`. The agent moves at **0.5 ×** its speed **[PROPOSAL]**, shoots with `W_shooter`, and
  resumes its order.
- **Out of action:** does not move, shoot or ask for permission. It drops its order.
- **Suppression:** a shot at an agent, hit or miss, suppresses it for **10 s [PROPOSAL]**. Each new shot restarts the
  10 s. A suppressed agent:
  - goes prone and stops moving (`Activity = Suppressed`)
  - shoots with `S_shooter`
  - resumes its order and posture when the suppression ends

### Casualty care: foundation only, not implemented in this ticket (C)
A hit can lead to one or more other members of the unit stopping to treat or carry the wounded. That takes fighters
out of the fight, which is a key effect of casualties. This ticket only lays the foundation:
- **Event:** every hit that wounds or puts an agent out of action logs `CasualtyOccurred(agent, position, health)`.
  It is the hook a later care system listens to.
- **Unit membership:** an agent has an optional `UnitId` (fire team or squad). Nothing uses it yet. The care system
  will pick helpers from the same unit.
- **Reserved activity:** `UnitActivity.GivingAid` is reserved and documented, but nothing sets it yet.

Not decided, for the later ticket:
- how many helpers, and who: nearest, by role or by drill
- whether care needs cover first
- how long care takes
- whether the wounded are carried, and how that slows movement
- whether care changes the casualty's state, for example stopping bleeding

### Orders and rules of engagement (R)
- **`FireAt(targetId)` order:** the agent shoots at the target while it is visible and in range, until the target is
  out of action or the order is replaced. The order is rejected if the target is on the agent's own side, does not
  exist, or is the agent itself.
- **Rules of engagement:**
  - `HoldFire`: the agent shoots only on a `FireAt` order, even when it is fired at.
  - `ReturnFire` (the default): fire opened on the agent gives it the permission **and the duty** to fire back. When
    an enemy shoots at it, it shoots back at that enemy as soon as it sees it (muzzle flash included), without waiting
    for an order. This duty outranks moving along a corridor.
  - `FreeFire`: as `ReturnFire`, and it shoots at the nearest visible enemy in range (ties broken by id).
- **Movement and firing:** an agent with a `MoveThroughCorridor` order keeps moving while it fires under its rule of
  engagement (fire on the move). Suppression and the 60 s pin stop it.

### Event log (D)
New events:
- `ShotFired(shooter, target, hit)` and `ShotHeard(listener, shooter, shooterPosition)`
- `HealthChanged(agent, health)`, `CasualtyOccurred(agent, position, health)`
- `SuppressionStarted(agent, until)`, `SuppressionEnded(agent)`
- `PostureChanged(agent, posture)`

`EventReplay` also rebuilds health, suppression and posture, so the state can be rebuilt from the log alone (as in A4).

## Acceptance criteria and planned tests

Test names start with their criterion ID. "Pure" tests check a function directly (`Twin.Sim.Tests`). "Scenario"
tests run the simulation (`Twin.Scenarios.Tests`). Probabilities are checked exactly, against the formula; sampling
is checked statistically with a fixed seed (see "Statistical tests" below).

### P: posture (`tests/Twin.Scenarios.Tests/Combat/PostureTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| PO1 | `PO1_eye_height_follows_posture` | standing, crouching, prone → eye at ground + 1.6, 1.1, 0.3 m |
| PO2 | `PO2_crouching_and_crawling_slow_the_agent` | M8-style step in forest: 0.45, 0.225, 0.045 m |
| PO3 | `PO3_changing_posture_takes_one_second_without_moving_or_shooting` | `SetPosture` during movement → 2 steps without movement |
| PO4 | `PO4_a_prone_target_behind_a_low_rise_is_hidden_but_a_standing_one_is_not` | 0.5 m rise between them → prone not visible, standing visible |
| PO5 | `PO5_a_smaller_posture_is_harder_to_hit` | Size factors 1, 0.7, 0.4 applied exactly |

### L: line of sight (`tests/Twin.Sim.Tests/Combat/LineOfSightTests.cs`, pure)
| ID | Test | Setup → expected |
|---|---|---|
| L1 | `L1_on_flat_open_ground_the_target_is_visible_with_full_transmission` | open map, 100 m → visible, T = 1 |
| L2 | `L2_a_ridge_between_shooter_and_target_blocks_line_of_sight` | ridge rising 3 m above both eye lines → not visible |
| L3 | `L3_a_shooter_on_high_ground_sees_over_a_low_ridge` | same ridge, shooter 10 m higher → visible |
| L4 | `L4_a_building_between_them_blocks_line_of_sight` | one building cell on the line → not visible |
| L5 | `L5_forest_attenuates_by_the_length_of_the_line_inside_it` | 20 m of forest on the line → T = exp(−20/25) within one sample step |
| L6 | `L6_beyond_about_75_m_of_forest_the_target_is_not_visible` | 80 m of forest → not visible; 70 m → visible |
| L7 | `L7_line_of_sight_is_symmetric_for_equal_postures_on_flat_ground` | A sees B ⇔ B sees A |
| L8 | `L8_a_muzzle_flash_shows_the_shooter_through_more_forest_for_two_seconds` | 90 m of forest: visible right after a shot, not 3 s later |

### S: sound (`tests/Twin.Scenarios.Tests/Combat/SoundTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| S1 | `S1_a_shot_is_heard_within_the_weapons_sound_range` | listeners at 1400 m and 1600 m → only the first gets `ShotHeard` |
| S2 | `S2_a_shot_is_heard_behind_a_hill` | listener out of sight → still `ShotHeard` |
| S3 | `S3_sound_and_flash_come_from_the_weapon_profile` | suppressed weapon → shorter range, no flash |

### F: hit probability and shots (pure: `HitModelTests.cs`; scenario: `tests/Twin.Scenarios.Tests/Combat/ShootingTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| F1 | `F1_hit_probability_falls_with_distance_as_specified` | d = 0, 150, 300 m → 0.6, 0.6/e, 0.6/e² |
| F2 | `F2_beyond_maximum_range_the_hit_probability_is_zero` | d = 601 m → 0, and no shot is fired |
| F3 | `F3_forest_transmission_multiplies_the_hit_probability` | T = 0.5 → half of the open-ground value |
| F4 | `F4_a_suppressed_or_wounded_shooter_hits_less_often` | factors 0.3 and 0.5, and both together 0.15 |
| F5 | `F5_an_agent_fires_one_shot_every_two_seconds` | `FireAt` at long range → consecutive `ShotFired` events are 2 s apart |
| F6 | `F6_an_agent_without_ammunition_does_not_fire` | 3 rounds → exactly 3 shots |
| F7 | `F7_no_shot_is_fired_without_line_of_sight` | target behind a ridge → no `ShotFired`, order kept |
| F8 | `F8_sampled_hit_rate_matches_the_hit_probability` | statistical, 10 000 draws at P = 0.3 → hit rate within ±4σ |

### E: effects (`tests/Twin.Scenarios.Tests/Combat/EffectsTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| E1 | `E1_the_first_hit_pins_the_agent_completely_for_sixty_seconds` | hit → `Pinned`, prone: no movement, shots, posture changes or permission requests for 60 s |
| E2 | `E2_after_sixty_seconds_a_pinned_agent_is_wounded_and_resumes_its_order` | at 60 s → `Wounded`, moves again at half speed |
| E3 | `E3_a_second_hit_puts_the_agent_out_of_action` | second hit while pinned and while wounded → `OutOfAction` |
| E4 | `E4_an_agent_out_of_action_neither_moves_shoots_nor_asks` | out of action mid-corridor → position fixed, no more events of its own |
| E5 | `E5_a_shot_at_an_agent_suppresses_it_for_ten_seconds` | a miss → `Suppressed`, prone, in place; resumes order and posture after 10 s |
| E6 | `E6_each_new_shot_extends_suppression` | shots every 2 s → still suppressed 10 s after the last shot |

### C: casualty care foundation (`tests/Twin.Scenarios.Tests/Combat/CasualtyTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| C8 | `C8_a_wounding_hit_logs_a_casualty_with_its_position` | `CasualtyOccurred` at the hit agent's position, health `Pinned` |
| C9 | `C9_no_one_gives_aid_yet` | team-mate next to the casualty → its activity and order unchanged *(changes when care is implemented)* |

(C1–C7 are taken by the corridor tests, so these continue from C8.)

### R: orders and rules of engagement (`tests/Twin.Scenarios.Tests/Combat/EngagementTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| R1 | `R1_fire_at_an_own_side_agent_is_rejected` | `OrderRejectedException`; also for an unknown target and for itself |
| R2 | `R2_hold_fire_agent_does_not_shoot_even_when_fired_at` | enemy shoots it from the open at 100 m → no shots back |
| R3 | `R3_return_fire_agent_must_shoot_back_at_whoever_shot_at_it` | red shoots blue → blue's next possible shot targets red |
| R4 | `R4_return_fire_agent_does_not_open_fire_first` | both ReturnFire, visible → no shots |
| R5 | `R5_return_fire_agent_shoots_back_at_a_shooter_seen_only_by_its_muzzle_flash` | shooter in 90 m of forest → blue shoots within the flash time |
| R6 | `R6_free_fire_agent_engages_the_nearest_visible_enemy` | two enemies at 80 m and 120 m → first target is the 80 m one; ties by id |
| R7 | `R7_fire_at_stops_when_the_target_is_out_of_action` | after `OutOfAction` → no more shots at it |
| R8 | `R8_an_agent_keeps_moving_along_its_corridor_while_firing` | corridor order + FreeFire, not suppressed → moves and fires |
| R9 | `R9_agents_never_shoot_their_own_side` | FreeFire with a friend nearer than the enemy → shoots the enemy |

### D: determinism and log (`tests/Twin.Scenarios.Tests/Combat/CombatDeterminismTests.cs`)
| ID | Test | Setup → expected |
|---|---|---|
| D1 | `D1_an_engagement_with_the_same_seed_gives_byte_identical_logs` | two FreeFire agents, 5 min, twice → identical JSON Lines |
| D2 | `D2_a_different_seed_gives_a_different_outcome` | seeds 1 and 2 → logs differ (guards against unused randomness) |
| D3 | `D3_health_suppression_and_posture_rebuilt_from_the_log_match_the_live_state` | A4 with combat events |

### Acceptance on real terrain (`tests/Twin.Acceptance.Tests/Combat/EngagementOnSt1Tests.cs`)
| ID | Test | Expected |
|---|---|---|
| K1 | `K1_agents_on_opposite_sides_of_a_hill_on_st1_cannot_see_each_other` | chosen points on st1 → not visible, but shots are heard |
| K2 | `K2_across_a_field_on_st1_they_see_and_engage_each_other` | FreeFire both, run to the end → shots, at least one health change, no shots without line of sight |
| K3 | `K3_line_of_sight_query_time_is_measured` | like P1: time 10 000 line-of-sight queries on st1, recorded, no limit |

## Statistical tests
Randomness comes from `Simulation.Random` with a fixed seed, so a statistical test gives the same result every run.
Its tolerance is ±4σ, so it is not tuned to one seed.

Tests about what a hit does (E1–E3, C8, R7) need a hit to happen at a known time. They place the target in the open
at point-blank range and run until the first `ShotFired` with `hit = true`. A hit is then certain within the timeout,
and the test checks what follows it. No test-only hit model is needed.

## Kit additions (spec phase, `tests/Twin.Testing`)
- `SpecShooting`: the [PROPOSAL] values above and the formulas, the counterpart of `SpecV01`.
- `Scenario.WithUnit(..., side, rulesOfEngagement, posture, weapon, unitId)`.
- `TestMap`: a ridge helper, for example `.WithRidge(fromX, toX, height)`, on top of `WithHeights`.
- `ScenarioRun.RunUntilHit(target)`.
- Assertions:
  - `sim.Unit("x").Should().BeSuppressed()`
  - `.HaveHealth(Health.Pinned)`
  - `.BeIn(Posture.Prone)`

## Answers and open questions
1. **A test hit model (answered):** not used. Tests run until a real hit instead.
2. **Signature of firing (answered):** a shot makes a sound and, depending on the weapon, a muzzle flash. See "Sound"
   and "Muzzle flash".
3. **Rules of engagement (answered):** they depend on the rules. By default, fire opened on the agent gives it the
   permission and the duty to fire back (`ReturnFire`).
   - **Still open:** does the corridor permission timeout keep running while the agent is pinned or suppressed?
     Proposal: yes, because the leader's answer does not depend on the shooting.
4. **Posture (answered):** in scope. See "Posture".
5. **Buildings (answered):** they block at any height for now. Later datasets with building heights should fix this.
6. **Open: casualty care.** The points under "Casualty care" are to be decided in the later ticket.

## Changes made while writing the tests (2026-11-08)

- **Files:**
  - `tests/Twin.Sim.Tests/Combat/`: `LineOfSightTests`, `HitModelTests`
  - `tests/Twin.Scenarios.Tests/Combat/`: `PostureTests`, `SoundAndFlashTests`, `ShootingTests`, `EffectsTests`,
    `CasualtyTests`, `EngagementTests`, `CombatDeterminismTests`
  - `tests/Twin.Acceptance.Tests/Combat/EngagementOnSt1Tests`
- **L8 moved:** it is now a scenario test in `SoundAndFlashTests`. A muzzle flash depends on time, so the test needs a
  running simulation. Line of sight between two agents is `Simulation.CanSee(observer, target)`, which uses their
  postures and any muzzle flash.
- **New canopy test** (`Forest_above_the_line_does_not_attenuate_a_line_over_the_canopy`): the 15 m canopy rule needed
  a test of its own.
- **R5 changed:** a third agent (`FreeFire`) engages a shooter it sees only by its muzzle flash. A duelling pair always
  sees each other through the same forest, so the flash can only matter to a third agent.
- **E3 split in two:** a second hit while pinned, and a hit on a wounded agent.
- **New `CeaseFire` order:** it ends a `FireAt` order. The effect tests use it to stop the shooter after the first hit.
- **`SetPosture` keeps the agent's current order:** it is not replaced. A forced drop to prone (when pinned or
  suppressed) is immediate; any other posture change takes 1 s.
- **F2:** checks both the pure function and that no shot is fired at 602 m.
- **K points on st1** (chosen from the elevation model):
  - K1: (404125, 6687445) and (404349, 6687501), with a hill about 20 m above the sight line
  - K2: (403871, 6687191) and (404009, 6687083), a clear line over a field
- **Kit additions:**
  - `SpecShooting`
  - `Scenario.ForCombat`, `WithFighter`, `WithUnit(..., Combatant)`, `WithCombatRules`
  - `TestMap.WithRidge`
  - `ScenarioRun.RunUntilHit`
  - the assertions `BeSuppressed`, `HaveHealth` and `BeIn`
- **API the tests need from `Twin.Sim`:**
  - namespace `Twin.Sim.Combat`:
    - enums `Side`, `Posture`, `Health`, `RulesOfEngagement`
    - records `PostureValues`, `CombatRules`, `WeaponProfile`, `Combatant`, `SightLine`
    - static classes `LineOfSight.Between` and `HitModel.Probability/Roll`
  - orders: `FireAt`, `CeaseFire`, `SetPosture`
  - events: `ShotFired`, `ShotHeard`, `HealthChanged`, `CasualtyOccurred`, `SuppressionStarted`, `SuppressionEnded`,
    `PostureChanged`
  - `Unit`: `Health`, `Posture`, `IsSuppressed`, `RoundsLeft`, `UnitId`
  - `UnitActivity`: `Suppressed`, `Pinned`, `OutOfAction`, `ChangingPosture`, `GivingAid`
  - `AgentSnapshot`: `Health`, `Posture`, `Suppressed`
  - `Simulation`: `AddUnit(..., Combatant)`, `CanSee`
  - `SimulationConfig.Combat`

## Implementation (2026-11-09)

- **Code:**
  - `src/Twin.Sim/Combat/`: types, `LineOfSight`, `HitModel`
  - `src/Twin.Sim/Systems/CombatSystems.cs`:
    - `CombatStatusSystem`: ends the pin and suppression, carries out posture changes
    - `FireSystem`: picks targets, fires, applies sound, flash, suppression and hits
- **Step order:** combat status, then movement (`MoveTo` and corridor), then firing. An agent freed this step moves in
  the same step. Hits apply at the end of the step.
- **When pinned, suppressed or changing posture:** the agent does not move, plan or ask for permission. The corridor
  permission timer keeps running, so an answer or timeout that came meanwhile is handled when the agent is free.
- **`FireAt`:** it overrides the rules of engagement. While the ordered target is not visible or out of range, the
  agent waits and does not engage others.
- **Measurement:** 10 000 line-of-sight queries on st1 take about 0.4 s (K3).

## Doctrine decisions (2026-11-09)

The product owner asked that these choices be data rather than code: `Doctrine`, set through `CombatRules.Doctrine`
and the `doctrine` section of scenario.json (scenarios/README.md).

- **`ReturnFireOverridesFireAt = true`.** The duty to return fire wins. An agent under `ReturnFire` or `FreeFire`
  whose `FireAt` target cannot be engaged shoots back at a visible attacker, and keeps its order.
- **`PermissionAnswers = WhenFree`.** A permission answer or timeout that arrives while the agent is pinned or
  suppressed is handled when it is free again. This answers open question 3. `Immediately` handles it on time, but the
  agent still does not move until it is free.

Tests: `tests/Twin.Scenarios.Tests/Combat/DoctrineTests.cs` runs both settings of each rule.
`tests/Twin.Sim.Tests/Combat/ScenarioDoctrineTests.cs` checks reading the doctrine from scenario.json.
