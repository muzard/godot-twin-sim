# Smoke test I: the test suite in plain language

**Status: draft v3 (2026-11-18).** Written from the repository (docs, ADRs, code and the names of the existing tests) and checked
against the Smoke test I brief ("Simulating a single fighter, in order to find out sane ways to build the digital twin").
That brief is short and sets two acceptance criteria, which are in section 10. The detailed v0.1 spec the code refers to
("Taistelusimulaattori: speksi v0.1 ja Smoke test I", with IDs such as W1, M3, C5) is still not in the repository, so
sections 1–9 are not yet checked against it.

**What changed in v3.** Rows and sections changed in v3 carry **[v3]** in their ID cell.

- **Added:** the Stop order (M12); experience (X), fire modes (FM), own-fire safety (LF), suppression as fire pressure
  (SU), cover and firing positions (CP) and the drill under fire (DR), all from backlog 0003; the replay tools and
  viewer (V4–V7); one more log test (D4).
- **Changed:** E5 and E6 (already reworded by the 0003 change; the old tests were removed on purpose, not weakened),
  R9 (the friend must now be outside the 60° sector), V1, V4 and the evidence for Z3, Z4 and Z6.
- **Renamed here only:** the fire-mode tests are called M1–M5 in the code, which clashes with the movement tests M1–M8.
  This document calls them FM1–FM5.
- **Unchanged:** sections 1, 3, 4, 5 and 8, apart from the summary counts.

**How to read this.** Each row is one thing the system must do, in plain words. The last columns say whether an
automated test already checks it and, if so, which one. Rows with a new ID (marked **new**) are gaps found in the gap
analysis: nothing checks them yet. These are the candidates for the next spec phase, where they become code tests.

| Status | Meaning |
|---|---|
| Covered | An automated test checks it. |
| Partial | A test checks part of it; the note says what is missing. |
| Missing | No automated test. Proposed as a new test. |
| Manual | Checked by a person, by design. |

Existing tests are named in `code style`. Spec IDs (W1, M3, C5, A1 …) are kept as they are in the code. The two casualty
tests that the code calls C8 and C9 are called CA1 and CA2 here, because C1–C7 already mean corridors.

## Summary

| Area | Requirements | Covered | Partial | Missing | Manual |
|---|---|---|---|---|---|
| 1. World data | 12 | 9 | 1 | 2 | 0 |
| 2. Movement **[v3]** | 15 | 15 | 0 | 0 | 0 |
| 3. Corridors and permission | 19 | 14 | 0 | 5 | 0 |
| 4. Real map (Smoke test I scenario) | 9 | 3 | 4 | 2 | 0 |
| 5. Speed and platform | 4 | 0 | 2 | 1 | 1 |
| 6. Combat **[v3]** | 86 | 80 | 2 | 4 | 0 |
| 7. Repeatability and event log **[v3]** | 8 | 6 | 0 | 2 | 0 |
| 8. Structure and data rules | 5 | 2 | 1 | 2 | 0 |
| 9. Visual client **[v3]** | 7 | 0 | 0 | 4 | 3 |
| 10. The brief's acceptance criteria | 10 | 0 | 4 | 2 | 4 |

---

## 1. World data

The map the simulation runs on: heights, terrain types, cliffs, and the conversion between map and local coordinates.

| ID | In plain words | Status | Tests / note |
|---|---|---|---|
| W1 | The first row in a map file is the southernmost row, both when the preprocessing writes it and when the simulation reads it. | Covered | `W1_row_0_of_a_layer_file_is_the_southernmost_row`; Python `test_w1_written_float32_layer_has_the_southernmost_row_first`, `test_w1_written_uint8_layer_is_one_byte_per_cell_south_row_first` |
| W2 | The height at the middle of a cell is that cell's value. Between cell middles the height is blended smoothly. The terrain type at a point is the type of the cell it lies in. | Covered | `W2_height_at_a_cell_center_is_the_cell_value`, `W2_height_between_cell_centers_is_bilinear`, `W2_terrain_class_is_the_class_of_the_containing_cell_with_row_0_in_the_south` |
| W3 | Converting a national-grid (ETRS-TM35FIN) point to local meters and back gives the same point within a millimeter. | Covered | `W3_etrs_to_local_is_the_offset_from_the_south_west_corner`, `W3_etrs_to_local_and_back_returns_the_same_point_within_a_millimeter`, `W3_local_to_etrs_adds_the_origin` |
| W4 | The heights in the prepared map equal what the GIS tool (GDAL) reads from the original elevation file at the same points. | Covered | `W4_preprocessed_heights_match_the_values_GDAL_reads_from_the_original_GeoTIFF` |
| W5 | The terrain types in the prepared map equal the original topographic data at the same points. | Partial (**new**) | W4 checks heights only. Needs sample points with the expected terrain type, read from the source data. |
| W6 | When a map feature overlaps another, the stronger one wins, in this order: building, water, road, bog, field, forest, open. | Missing (**new**) | Rule is documented in `scenarios/README.md`; no Python test. |
| W7 | The terrain type numbers in the file (0–6) mean what the documentation says. | Covered | `Terrain_class_codes_match_the_preprocessing_contract` |
| W8 | A map file that does not match its recorded checksum is refused, with a clear message. | Covered | `Layer_whose_checksum_does_not_match_the_manifest_is_rejected`, `A_cliff_file_whose_checksum_does_not_match_is_rejected` |
| W9 | The map's size, origin and layers come from its description file. A world with no cliff file simply has no cliffs. | Covered | `Loaded_world_has_the_size_and_origin_from_the_manifest`, `A_world_without_a_cliff_file_has_no_cliffs`, `World_bounds_cover_all_cells_from_the_south_west_corner` |
| W10 | Cliff lines are stored in local meters measured from the south-west corner, and are cut off at the edge of the area. | Covered | `Cliff_lines_are_read_in_local_meters`; Python `test_cliff_lines_are_in_local_meters_from_the_south_west_corner`, `test_cliff_lines_are_clipped_to_the_area` |
| W11 | If the map data is missing, or is an un-downloaded Git LFS placeholder, the test fails with a message that says so, rather than with a confusing error. | Covered | `Missing_scenario_file_fails_with_a_clear_message`, `Test_run_finds_the_smoke_test_scenario_directory` (placeholder detection is in `ScenarioOnDisk`; no separate test, so **check**) |
| W12 | Asking for the height or terrain outside the map is an error, not a made-up value. | Missing (**new**) | Only plain terrain (`Height_outside_the_map_is_an_error`) is tested; the raster world is not. |

## 2. Movement

Finding a route and walking it. The agent is infantry on a 2 m grid.

| ID | In plain words | Status | Tests |
|---|---|---|---|
| M1 | On flat open ground the route is a straight line (also diagonal) and takes distance divided by speed. | Covered | `M1_on_flat_open_ground_the_path_is_straight_and_costs_distance_over_speed`, `M1_a_diagonal_path_…` |
| M2 | A wall with a gap: the route goes through the gap. | Covered | `M2_when_a_wall_has_a_gap_the_path_goes_through_the_gap` |
| M3 | The agent cannot cut across the corner of a building. | Covered | `M3_a_diagonal_step_across_a_building_corner_is_not_allowed` |
| M4a | Walking uphill is slower than walking downhill over the same distance. | Covered | `M4_uphill_is_slower_than_downhill_over_the_same_distance` |
| M4b | A slope steeper than the agent's limit cannot be walked, up or down. | Covered | `M4_a_slope_steeper_than_the_limit_is_impassable_both_up_and_down` |
| M5 | A bog is avoided when the way around is quicker, and crossed when the way around is slower. | Covered | `M5_a_bog_is_avoided_…`, `M5_a_bog_is_crossed_…` |
| M6 | In symmetric terrain, where several routes are equally good, the same route is picked every time. | Covered | `M6_in_symmetric_terrain_a_tie_always_resolves_to_the_same_path` |
| M7 | Asking for a route to an unreachable point returns "no route" quickly and does not hang. | Covered | `M7_searching_for_an_unreachable_point_returns_no_path_and_does_not_hang` |
| M8 | Each step the agent advances speed × step time at the local terrain speed; its position stays on the ground and its eyes are at ground + eye height. | Covered | `M8_agent_advances_v_times_dt_per_step_at_the_local_terrain_speed`, `M8_agent_eyes_are_at_ground_height_…` |
| M9 | A plain move order: the unit starts on the next tick, travels at its speed, arrives exactly at the destination and becomes idle; a new order replaces the old; unknown units and destinations outside the map are rejected. | Covered | `MoveToTests` (9 tests, e.g. `Unit_reaches_destination_and_becomes_idle`, `Arrival_time_equals_distance_divided_by_speed`) |
| M10 | A route may not cross a cliff line, in either direction, unless the agent can rappel. A cliff is just impassable: nobody is asked for permission. Where the height model is gentle but a cliff is drawn, the cliff still blocks. | Covered | `CliffTests` (7 tests), `CliffAcceptanceTests`, `The_agent_never_crosses_a_cliff_on_the_real_map` |
| M12a **[v3]** | A moving unit that is ordered to stop is idle with zero speed after the next tick, and it has no current order left. | Covered | `Moving_unit_ordered_to_stop_is_idle_with_zero_velocity_after_the_next_tick`, `Stopped_unit_has_no_current_order` |
| M12b **[v3]** | It stops where it is (on the ground), and a move order given afterwards works normally. | Covered | `Stopped_unit_halts_where_it_was_and_stays_on_the_ground`, `Move_order_after_stop_reaches_its_destination` |
| M12c **[v3]** | Stopping an idle unit does nothing; stopping an unknown unit is rejected. | Covered | `Stopping_an_idle_unit_is_accepted_and_has_no_effect`, `Stop_for_unknown_unit_is_rejected` |
| M11 | A rappelling agent crosses cliffs both ways, even steeper than its normal slope limit, and reaches the bottom. | Covered | `A_unit_that_can_rappel_crosses_a_cliff_…`, `A_rappelling_agent_reaches_the_bottom_of_the_cliff` |

## 3. Corridors and permission

An order tells the agent to go through a corridor. Crossing a "hard boundary" (a drawn line, or any edge where it
leaves forest) needs the leader's permission.

| ID | In plain words | Status | Tests |
|---|---|---|---|
| C1 | With no penalty, the route may use the soft side zones for free. | Covered | `C1_with_zero_penalty_…` |
| C2 | With a penalty, the route stays in the inner zone when the detour is short enough. | Covered | `C2_with_a_penalty_…` |
| C3 | The route never goes beyond the corridor's outer limit; if the only way is outside it, there is no route. | Covered | both `C3_…` tests |
| C3a | Without a permit the route never crosses a hard boundary; the unrestricted reference route does cross it. | Covered | `Without_a_permit_the_path_does_not_cross_a_hard_boundary`, `Reference_path_that_ignores_hard_boundaries_crosses_them` |
| C3b | Leaving forest counts as crossing a hard boundary; entering forest does not. | Covered | `Leaving_forest_crosses_a_hard_boundary_but_entering_forest_does_not` |
| C4 | If the detour that avoids the boundary is short (within 1.5 × the direct route), the agent takes the detour and does not ask. This also holds for the forest edge. | Covered | `C4_without_a_permit_…`, `C4_the_forest_edge_is_a_hard_boundary_…` |
| C5 | If the detour is too long, the agent asks for permission, naming the boundary and the crossing point, and waits where it stands. | Covered | `C5_when_the_detour_exceeds_…`, `C5_while_waiting_for_an_answer_…` |
| C6 | With a permit, the agent crosses only near the permitted spot (within the permit radius), and arrives. A permit for one boundary does not open another. | Covered | `C6_…` (4 tests) |
| C7 | If the leader says no, the agent takes the detour if there is one; otherwise it stops and reports it is blocked. | Covered | `C7_after_a_denial_…` (2 tests) |
| C7b | If the leader does not answer, the agent waits the timeout (120 s) and then does the same as for a denial. | Covered | `C7_when_the_leader_does_not_answer_…` (2 tests) |
| C8 | After a permit, the agent may ask again for the next boundary, and arrives once all are granted. | Covered | `After_a_grant_the_agent_asks_again_…` |
| C9 | A leader who would answer after the timeout is not waited for; an answer arriving exactly at the timeout counts. | Covered | `A_leader_slower_than_the_timeout_is_not_waited_for`, `An_answer_arriving_exactly_at_the_timeout_counts` |
| C10 | A boundary lying between the agent and the middle of its cell is not crossed unasked. | Covered | `A_boundary_between_the_agent_and_its_cell_center_…` |
| C11 | Bad corridor orders are refused: fewer than two points, start outside the outer limit, destination in water or outside the map. | Covered | `PermissionEdgeCaseTests` (4 tests) |
| C12 | A new order given while the agent waits for permission cancels the request; a late answer then does nothing. | Missing (**new**) | |
| C13 | An agent that is already at its destination is idle at once and asks nothing. | Missing (**new**) | |
| C14 | A leader who answers with no delay (0 s) works. A permit covers a crossing exactly at the radius and not a bit further. | Missing (**new**) | Two small edge tests. |
| C15 | Two agents in the same corridor each ask and are answered separately. | Missing (**new**) | Scenario kit supports several units; no corridor test uses two. |
| C16 | The scripted leader's answer, delay and permit radius come from the scenario file. | Missing (**new**) | Doctrine reading is tested; leader reading is only used, not checked. |

## 4. The real map (Smoke test I scenario `st1`)

One agent walks from A in forest to B about 0.9 km away, in a 100 m corridor, past buildings, water, a steep slope,
bog and a field. Leaving forest is a hard boundary, so the agent must ask at least once.

| ID | In plain words | Status | Tests / note |
|---|---|---|---|
| A1 | The leader grants: the agent asks, is allowed, reaches B, becomes idle, and never breaks a rule on the way. | Partial | `A1_with_permission_…`. It does not check the numbers in the scenario notes: one request at about 0.5 s, permit after 30 s, arrival at about 1260 s. (**new** A1b: check these, with a tolerance.) |
| A2 | The leader denies, or does not answer: the agent either takes a detour and arrives, or reports it is blocked, depending on whether a detour exists. | Partial | `A2_without_permission_…`. The scenario says no detour exists, so only the "blocked" branch ever runs. The "arrives by detour" branch is never run on real data. (**new** A2b: a real-map variant where a detour exists.) |
| A2c | With no answer, the agent waits the full timeout before reporting blocked. | Missing (**new**) | |
| A3 | Running the same scenario twice gives byte-for-byte identical event logs. | Covered | `A3_two_runs_with_the_same_seed_…` |
| A4 | Rebuilding the state from the written event log gives exactly the live state at every step. | Covered | `A4_state_rebuilt_from_the_event_log_…` |
| A5 | A different seed on the real map changes nothing for a lone walker (no randomness is used), so the log is identical. | Missing (**new**) | Low value; decide whether to keep. |
| A6 | The agent never leaves the corridor's outer limit or crosses a hard boundary without a permit, in the route it planned and in the route it actually walked. | Covered | `AssertNoViolations` inside A1, A2 |
| A7 | The larger 2 × 2 km area also runs as a scenario, not only as a speed test. | Partial (**new**) | st1-2km is only used in P1. |
| A8 | The scenario files themselves are valid: required fields present, points inside the map, area not restricted. | Partial | Loading fails clearly if fields are missing; nothing checks the rest. |

## 5. Speed and platform

| ID | In plain words | Status | Tests / note |
|---|---|---|---|
| P1 | Finding a route on 1 × 1 km and on 2 × 2 km takes an acceptable time. | Partial | `P1_pathfinding_time_is_measured_and_recorded` measures and prints but **has no limit**, so it can never fail. A limit has to be decided with the team. |
| P2 | The whole core test suite runs in Linux (Docker) in under 60 s. | Partial | `./dev docker-test` and the CI job print the time; whether CI fails above 60 s needs checking. |
| P3 | Preparing a map takes seconds (measured about 0.1 s). | Missing (**new**) | Only measured by hand. |
| P4 | The visual client runs the whole area at an acceptable frame rate. | Manual | Not measured yet; viewer not built for this area. |

## 6. Combat (backlogs 0002 and 0003)

Two sides, shooting with line of sight, posture, suppression, hits, and rules for when to fire.

### Posture

| ID | In plain words | Status | Tests |
|---|---|---|---|
| PO1 | Eye height follows posture: standing 1.6 m, crouching 1.1 m, prone 0.3 m above the ground. | Covered | `PO1_…` |
| PO2 | Crouching halves walking speed, crawling cuts it to a tenth. | Covered | `PO2_…`, `A_crawling_agent_moves_slowly_under_a_move_to_order_too` |
| PO3 | Changing posture takes one second, during which the agent neither moves nor shoots. | Covered | `PO3_…` |
| PO4 | A prone target behind a low rise is hidden, but a standing one is seen. | Covered | `PO4_…` |
| PO5 | The smaller the posture, the harder to hit (factors 1, 0.7, 0.4). | Covered | `PO5_a_smaller_posture_is_harder_to_hit` |

### Seeing

| ID | In plain words | Status | Tests |
|---|---|---|---|
| L1 | In the open at 100 m the target is fully visible. | Covered | `L1_…` |
| L2–L3 | A ridge between them blocks sight; a shooter on much higher ground sees over it. | Covered | `L2_…`, `L3_…` |
| L4 | A building between them blocks sight. | Covered | `L4_…` |
| L5–L6 | Forest dims sight by how much forest the line passes through; beyond about 75 m of forest the target cannot be seen. | Covered | `L5_…`, `L6_…` |
| L7 | If A sees B on flat ground with equal postures, B sees A. | Covered | `L7_…` |
| L8 | A muzzle flash shows the shooter through more forest, for two seconds only. | Covered | `L8_…` |
| L9 | Forest above the 15 m tree line does not dim a line of sight that passes over it. | Covered | `Forest_above_the_line_does_not_attenuate_…` |

### Sound

| ID | In plain words | Status | Tests |
|---|---|---|---|
| S1 | A shot is heard within the weapon's sound range (1500 m) and not beyond. | Covered | `S1_…` |
| S2 | A shot is heard even behind a hill. | Covered | `S2_…` |
| S3 | Sound range and flash come from the weapon: a silenced weapon is heard less far and shows no flash. | Covered | `S3_…` |

### Hitting

| ID | In plain words | Status | Tests |
|---|---|---|---|
| F1 | Hit chance falls with distance: 0.6 at point-blank, then ÷e every 150 m. | Covered | `F1_…` |
| F2 | Beyond 600 m the chance is zero and no shot is fired. | Covered | `F2_…` (two tests) |
| F3 | Forest dimming multiplies the hit chance. | Covered | `F3_…` |
| F4 | A suppressed shooter hits at 0.3 ×, a wounded one at 0.5 ×, both together at 0.15 ×. | Covered | `F4_…` |
| F5 | An agent fires one shot every two seconds. | Covered | `F5_…` |
| F6 | An agent with no rounds left does not fire; with 3 rounds it fires exactly 3 shots. | Covered | `F6_…` |
| F7 | With no line of sight no shot is fired, and the order is kept. | Covered | `F7_…` |
| F8 | Over many shots the share of hits matches the stated probability. | Covered | `F8_…` |

### Effects of being shot

| ID | In plain words | Status | Tests |
|---|---|---|---|
| E1 | The first hit pins the agent completely for 60 s: it is prone and does not move, shoot, change posture or ask for permission. | Covered | `E1_…` |
| E2 | After 60 s it is wounded, resumes its order and moves at half speed. | Covered | `E2_…` |
| E3 | A second hit, while pinned or while wounded, puts the agent out of action. | Covered | both `E3_…` |
| E4 | An agent out of action does not move, shoot or ask, and takes no orders. | Covered | `E4_…`, `An_agent_out_of_action_takes_no_orders` |
| E5 | Fire suppresses: the agent goes prone and stops. An agent that holds fire afterwards resumes its order and posture; one on return or free fire runs the drill under fire instead (backlog 0003). Since 0003 suppression depends on how close, how heavy and how much fire passes the agent (fire pressure), not a fixed 10 s. | Covered | `SuppressionTests` SU4–SU7, `ContactDrillTests` (backlog 0003) |
| E6 | Sustained fire keeps the agent suppressed until after it stops. | Covered | `SU7_…` (backlog 0003) |
| CA1 | A wounding hit is logged as a casualty at the agent's position. | Covered | `C8_a_wounding_hit_logs_a_casualty_with_its_position` |
| CA2 | Nobody gives first aid yet (to be changed when care is built). | Covered | `C9_no_one_gives_aid_yet` |

### Orders and rules of engagement

| ID | In plain words | Status | Tests |
|---|---|---|---|
| R1 | A fire order at an own-side agent, an unknown agent, or oneself is rejected. | Covered | `R1_…` |
| R2 | A hold-fire agent does not shoot even when fired at. | Covered | `R2_…` |
| R3 | A return-fire agent must shoot back at whoever shot at it. | Covered | `R3_…` |
| R4 | A return-fire agent never opens fire first. | Covered | `R4_…` |
| R5 | A free-fire agent engages a shooter it sees only by its muzzle flash. | Covered | `R5_…` |
| R6 | A free-fire agent engages the nearest visible enemy; equal distance, the first by name. | Covered | both `R6_…` |
| R7 | A fire order ends when the target is out of action. | Covered | `R7_…` |
| R8 | An agent keeps walking its corridor while firing, unless suppressed or pinned. | Covered | `R8_…` |
| R9 **[v3]** | Agents never shoot their own side. Since 0003 a friend close to the line of fire also stops the shot (see LF1–LF4). | Covered | `R9_…`, re-specified in 0003: the friend now stands outside the 60° sector |
| R10 | Fire and posture orders need a fighter; a fire order replacing a move stops the agent. | Covered | `Combat_orders_need_fighters_on_both_ends`, `A_fire_at_order_that_replaces_a_move_stops_the_agent` |
| R11 | Two agreed doctrine choices (return fire beats a fire order; permission answers while suppressed) work in both settings and can be read from the scenario file. | Covered | `DoctrineTests` (4), `ScenarioDoctrineTests` (3) |
| R12 | Several attackers: a return-fire agent answers the nearest visible one. | Missing (**new**) | |
| R13 | A ceasefire order ends only a fire order, not a move. | Missing (**new**) | `CeaseFire` is used in tests but its own rule has no test. |
| R14 | A posture order keeps the current order; a forced drop to prone is instant. | Missing (**new**) | Stated in the ticket; check whether `PO3` covers it. |
| R15 | A free-fire agent that runs out of rounds stops firing and does not shoot an own-side agent by mistake. | Missing (**new**) | |

### Experience, fire modes and own-fire safety (backlog 0003) **[v3]**

| ID | In plain words | Status | Tests |
|---|---|---|---|
| X1 **[v3]** | A fighter is "regular" by default and hits as often as in 0002. | Covered | `X1_…` |
| X2 **[v3]** | Experience (conscript, regular, special forces) scales the hit chance by 0.8, 1.0 and 1.3. | Covered | `X2_…` |
| X3 **[v3]** | Special forces fire their first snap shot sooner than conscripts (0.5 s against 2.0 s). | Covered | `X3_…` |
| X4–X5 **[v3]** | Special forces are harder to suppress, and how long suppression lasts depends on experience. | Covered | `X4_…`, `X5_…` |
| FM1 **[v3]** | A snap shot (quick, unaimed) hits with a quarter of the aimed chance; special forces at close range still land hits. | Covered | `M1_…`, `M2_…` |
| FM2 **[v3]** | Orders and free-fire engagements use aimed fire; snap shots come every 0.7 s. | Covered | `M3_…`, `M4_…` |
| FM3 **[v3]** | A machine-gun burst is one shot event of five rounds, and hits if any round would. | Covered | both `M5_…` tests |
| LF1 **[v3]** | No one fires when a friend is within 30° either side of the muzzle direction, whatever the rule or order. | Covered | `LF1_…`, `LF2_…` (a friend behind the target also blocks) |
| LF3–LF4 **[v3]** | A friend outside that sector, behind the shooter or beyond weapon range does not block. The shooter fires once the sector clears. | Covered | `LF3_…` (2 tests), `LF4_…` |
| LF5 **[v3]** | A fallen team-mate still blocks the sector. | Covered | `A_fallen_team_mate_still_blocks_the_fire_sector` |

### Suppression as fire pressure **[v3]**

Suppression no longer lasts a fixed 10 s per shot. Rounds that pass close put "pressure" on a fighter. Pressure fades over
time, and heavy pressure stops all fire.

| ID | In plain words | Status | Tests |
|---|---|---|---|
| SU1 **[v3]** | How far a round misses by grows with range and shrinks with aim and skill. | Covered | `SU1_…` (2 tests, pure) |
| SU2–SU3 **[v3]** | A round suppresses less the farther it passes, and more if the weapon is heavier. Cover and a prone posture reduce it. A blocked line gives none. | Covered | `SU2_…`, `SU3_…` (pure) |
| SU4–SU5 **[v3]** | A close rifle shot suppresses; a single distant one does not. | Covered | `SU4_…`, `SU5_…` |
| SU6–SU7 **[v3]** | Suppression ends when pressure falls low enough, and sustained fire keeps the target suppressed. | Covered | `SU6_…`, `SU7_…` |
| SU8–SU10 **[v3]** | Machine guns suppress far more than rifles. A heavily suppressed agent does not fire. Rifle fire alone rarely suppresses heavily. | Covered | `SU8_…`, `SU9_…`, `SU10_…` |
| SU11 **[v3]** | Suppression uses no random numbers, so it cannot change the hit rolls. | Covered | `SU11_…` |
| SU12–SU13 **[v3]** | Fire also suppresses fighters beside the target, usually less. | Covered | `SU12_…`, `SU13_…` (2 tests) |
| SU14 **[v3]** | A round passing within 10 m starts the whole drill, not just alertness. | Covered | `SU14_…` |
| SU15 **[v3]** | When a point patrol makes contact, the rear of the wedge is not pinned down. | Covered | `SU15_…` |
| SU16 **[v3]** | Own rounds never suppress or start the drill. | Covered | `SU16_…` |

### Cover and firing positions **[v3]**

| ID | In plain words | Status | Tests |
|---|---|---|---|
| CP1–CP3 **[v3]** | A building is cover but not a firing position. A low rise is a firing position. Open ground is neither. | Covered | `CP1_…`, `CP2_…`, `CP3_…` |
| CP4 **[v3]** | The nearest cover within 20 m is chosen; farther cover is ignored. | Covered | both `CP4_…` |
| CP5 **[v3]** | A position behind a hard boundary, or outside the forest when the agent is inside it, is not chosen (ADR 0009). | Covered | both `CP5_…` |
| CP6–CP8 **[v3]** | A firing position needs a clear fire sector and must hide the agent from every known attacker. Forward positions are preferred over nearer ones behind. | Covered | `CP6_…`, `CP7_…`, `CP8_…` |

### The drill under fire **[v3]**

When a fighter is fired on, it follows a fixed drill: a few snap shots while taking cover, then cover or a firing
position, then wait for the leader's orders.

| ID | In plain words | Status | Tests |
|---|---|---|---|
| DR1 **[v3]** | Under fire the agent drops prone and fires three snap shots after its reaction time. | Covered | `DR1_…` |
| DR2–DR4 **[v3]** | In danger it first moves into cover, then on to a firing position. With cover but no firing position it holds fire. | Covered | `DR2_…`, `DR3_…`, `DR4_…` |
| DR5 **[v3]** | With no cover nearby it stays and fires aimed shots. | Covered | `DR5_…` |
| DR6–DR7 **[v3]** | Lightly suppressed, it crawls to cover. More heavily suppressed, it stays put. | Covered | `DR6_…`, `DR7_…` |
| DR8–DR9 **[v3]** | At the firing position it fires aimed shots crouching. An attacker seen only by muzzle flash gets snap shots, not aimed ones. | Covered | `DR8_…`, `DR9_…` |
| DR10 **[v3]** | At the rear of a wedge it moves forward to fire past its team-mates. | Covered | `DR10_…` |
| DR11–DR12 **[v3]** | It reports contact and waits for orders instead of resuming its corridor. A new order ends the wait. | Covered | `DR11_…`, `DR12_…` |
| DR13–DR14 **[v3]** | A hold-fire agent does not run the drill. An agent with a fire-at order keeps its ordered target. | Covered | `DR13_…`, `DR14_…` |
| DR15 **[v3]** | Fire without an order keeps a reserve of rounds. | Covered | `DR15_…` |
| DR16–DR17 **[v3]** | A leader's new order stands under continuing fire. Fire from a new attacker restarts the drill. | Covered | `DR16_…`, `DR17_…` |
| DR18 **[v3]** | When the drill ends, the agent takes back the posture it wanted. | Covered | `DR18_…` |
| DR19 **[v3]** | After a fire-at order ends, the attacker's next round starts the drill. | Covered | `An_attacker_whose_fire_met_only_a_fire_at_order_…` |
| DR20 **[v3]** | The drill never leaves forest for cover (no permit, ADR 0009). | Partial | `CP5_…` checks the cover choice; no test runs the whole drill in a forest. **Check.** |

### Combat on the real map

| ID | In plain words | Status | Tests |
|---|---|---|---|
| K1 | On st1, agents on opposite sides of a hill cannot see each other but hear the shots. | Covered | `K1_…` |
| K2 | Across a field on st1, they see and engage each other; no shot is fired without line of sight. | Covered | `K2_…` |
| K3 | 10 000 line-of-sight checks on st1 are timed and recorded (no limit). | Partial | `K3_…` measures only; same as P1. |

## 7. Repeatability and event log

| ID | In plain words | Status | Tests |
|---|---|---|---|
| D1 | Same seed, same fight: identical logs. | Covered | `D1_…` |
| D2 | A different seed gives a different fight. | Covered | `D2_…` |
| D3 | Health, suppression and posture rebuilt from the log match the live state. | Covered | `D3_…` |
| D3b **[v3]** | The drill stage and waiting state rebuilt from the log match the live state. | Covered | `D4_suppression_drill_stage_and_waiting_rebuilt_from_the_log_match_the_live_state` |
| D4 | The whole simulation state is identical at every tick for the same scenario; the fingerprint changes when the world changes. | Covered | `Same_scenario_and_orders_produce_identical_state_every_tick`, `State_hash_changes_when_the_world_changes` |
| D5 | The random generator repeats for the same seed, differs for others, gives a known first value, and stays between 0 and 1. | Covered | `DeterministicRandomTests` (4) |
| D6 | Every event type survives writing to the log and reading back. | Missing (**new**) | Only a bad line (`A_malformed_log_line_is_reported_as_invalid_data`) and the replay time (`Replay_at_a_literal_time_…`) are tested directly. |
| D7 | A combat fight on the real map gives identical logs twice and replays correctly. | Missing (**new**) | D1 and D3 use small test maps. |

## 8. Structure and data rules

| ID | In plain words | Status | Tests / note |
|---|---|---|---|
| X1 | The simulation uses nothing but the base .NET library: no Godot. | Covered | `Simulation_references_only_the_dotnet_base_library` |
| X2 | The simulation uses no clock, system random, GUIDs or threads. | Covered | `Simulation_source_uses_no_nondeterministic_apis` (check whether it also catches looping over unordered dictionaries/sets, as the rules require) |
| X3 | No game rules live in the Godot client. | Partial (**new**) | A rule in `AGENTS.md`, not enforced by a test. |
| X4 | Only open data: no classified or personal data, no areas restricted under the Finnish territorial surveillance act. | Missing (**new**) | A documented rule; a data checklist or review step rather than a code test. |
| X5 | Package versions are declared only in one file. | Missing (**new**) | Rule in `AGENTS.md`; easy to check automatically. |

## 9. Visual client (Godot)

| ID | In plain words | Status | Tests / note |
|---|---|---|---|
| V1 **[v3]** | The interactive demo shows a two-unit test hill, where you select a unit and order it to move. It shows nothing else (no real map, corridor or combat). | Manual | Open `src/Twin.Client/project.godot` in Godot 4.7.2 .NET. |
| V2 | The conversion between simulation and screen coordinates is right (X east, Y north, up is up). | Missing (**new**) | `CoordinateMapper` has no test. |
| V3 | The national map survey's required attribution (CC BY 4.0) is shown in the client. | Missing (**new**) | Required by `scenarios/st1/README.md`. |
| V4 **[v3]** | The headless runner runs a demo for a given time and prints a result without graphics. | Missing (**new**) | `./dev run -- --seconds 60`; no test. |
| V5 **[v3]** | `./dev replay st1` runs the real-map scenario until the agent is idle or blocked, writes its event log, reads it back and prints each agent's state every 60 s. It exits with code 2 if the agent is still busy at the time limit, and rejects unknown options. | Missing (**new**) | `ScenarioReplay.cs` has no test. The state is rebuilt with `EventReplay`, which A4 and D3 do test. |
| V6 **[v3]** | The replay viewer (`./dev replay st1 --godot`) plays a recorded log on the real map: terrain, corridor and boundaries, cliffs, route and trail, permission requests, shots, casualties, health, posture and suppression. It runs no simulation. | Manual | Keys: space, arrows, home/end, wheel, drag, slider. |
| V7 **[v3]** | A built-in fire-fight demo (`./dev replay st1-combat --godot`) shows shooting in the replay viewer. | Manual | Not tested. |

## 10. The brief's acceptance criteria

The brief asks whether simulating single fighters is a sane idea and how to build it: fast, scalable, simple and
dataset-independent enough. Its two acceptance criteria are (1) test-driven development, so coding agents cannot quietly
drop features already built, and (2) a concept simple enough to be developed, understood by the team, and extended as far
as possible. After that a second fireteam reviews the design, the design is adjusted, and concurrent development is
planned. Most of this is about how we work, so much of it is checked by people, not by code tests.

| ID | In plain words | Status | Evidence / note |
|---|---|---|---|
| Z1 | A single fighter behaves sanely in a limited scenario: it moves through terrain, asks before crossing boundaries, can be shot, and fights back. | Partial | Sections 2–4 and 6. The gaps in section 4 (A1, A2) matter most here. |
| Z2 | Every behaviour change starts with a human-reviewed failing test, and the agent cannot change that test. | Partial | Enforced by the phase setting and hooks (`.claude/hooks/guard.cs`, `verify.cs`) and by the written workflow. The hooks themselves have no automated test. |
| Z3 **[v3]** | A feature already built cannot silently disappear: all existing tests run on every change and cannot be edited in the implement phase. | Partial | CI runs everything (`./dev check`); existing test files are locked in the implement phase. Ticket 0003 changed existing tests in the spec phase (E5 and E6 removed, R9, doctrine and D3 re-specified) and lists each one with its reason. That is allowed, but a person must review such changes. Nothing checks that CI blocks a merge when a test is deleted. |
| Z4 **[v3]** | The concept is simple enough to be developed: a new feature can be added by following the workflow. | Manual | Shooting (0002), the Stop order (0001) and suppression with the drill (0003) were built this way. The Stop order trial found four problems (`docs/onboarding-notes.md`); three are fixed, and the fourth, an out-of-date skill, is open. |
| Z5 | The concept is simple enough for the team to understand: a newcomer can read `AGENTS.md`, the workflow and the testing guide and add a small feature. | Manual | Needs a person who did not build it to try. The stop order is the natural exercise. |
| Z6 **[v3]** | The concept can be extended far: a new behaviour is one new system or order, without editing existing systems. | Manual | Design rule (`ISimSystem`, immutable order records). Ticket 0003 added `ContactDrillSystem` and `CoverSearch` as new code, but it also changed `CombatSystems.cs`, `Simulation.cs` and re-specified five existing tests. Judge this in design review. |
| Z7 | Datasets are interchangeable: a different map area runs with the same code and only new data files. | Partial | `st1` and `st1-2km` and hand-built test maps all load through the same code. `st1-2km` is only used for the speed test (see A7). |
| Z8 | The approach scales: the simulation stays fast as the number of agents grows. | Missing (**new**) | Every test uses one to a few agents. A test with, say, 100 and 1 000 agents, recording the time per step, would show the trend. A limit needs a team decision. |
| Z9 | A second fireteam reviews the design and the design is changed accordingly. | Manual | There is an AI `reviewer` agent, but the brief asks for another team. Record the review, what was changed and who agreed. |
| Z10 | A plan exists for developing the simulator concurrently with the full team. | Missing (**new**) | No such plan in the repository. |

---

## Open points

0. **Python or C#.** The team's shared notes (`matrix-docs`, decision of 2026-11-05) say the simulation starts in Python,
   and this repository is C# with Godot (ADR 0007). Section 10 assumes this repository's stack. Settle it before
   the two-fireteam design review.

1. **The detailed spec.** The brief is now covered in section 10, but the v0.1 spec with the W/M/C/A IDs is still not in
   the repository. Sections 1–9 follow the repository; compare them with that spec when it is available.
2. **Numbers and thresholds that need a decision, not just a test:** the pathfinding time limit (P1/K3), whether the
   60 s Linux limit makes CI fail, and whether A5 is worth having.
3. **Status "check" notes.** A few statuses (W11, X2, R14) are based on test names, not on reading the tests. Confirm
   them when writing the code tests.
4. **Not in this suite on purpose:** the stop order (backlog 0001) and casualty care beyond its foundation; neither is
   built.
5. **Existing tests have uneven IDs.** Ticket 0003 also reuses IDs (SU1, SU2, SU3 and SU13 appear twice, as do CP4, CP5 and LF3). C3 is used twice, C4–C7 are shared by several tests, and many tests (cliffs,
   move-to, doctrine) have no ID. When the new tests are written, the existing ones could be given the IDs above so each
   requirement is easy to find in the code.
