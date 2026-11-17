# 0003. Squad: leadership, formations and drills under fire (Smoke test II)

**Status:** design agreed with the product owner (2026-11-17). S1 specified (tests reviewed) and implemented
(2026-11-17); S2–S6 have no tests yet. Values marked **[PROPOSAL]** are
starting values; as in 0002, tests will read them from one place (`SpecSquad` and `SpecShooting` in
`tests/Twin.Testing`), so calibrating them later does not change what a test checks. The decisions are listed under
"Decisions" at the end.

This is an epic. It is delivered in six parts (S1–S6 below). Each part is its own spec → review → implement cycle and
its own pull request, in this order.

## Story
As an exercise designer, I place a squad (ryhmä) on real terrain and give one order to its leader: move through this
corridor in a wedge. The squad moves as a group inside the corridor, keeps its formation where the terrain allows it,
deploys at the forest edge and waits as one while its leader asks to cross. When it comes under fire, the squad leader
judges the enemy and commands a drill, and the members carry it out with delays that depend on their experience, on
the chain of command and on how hard they are suppressed.

## What already exists and is reused
- Single-agent corridor movement, permits and the scripted leader (spec v0.1, `CorridorMovementSystem`).
- Combat per agent: sides, rules of engagement, suppression, pinning, posture, line of sight, `ShotHeard` (0002).
- `Combatant.UnitId`: the fire team or squad an agent belongs to. Unused until now; this ticket uses it.
- `UnitActivity.GivingAid` stays reserved; casualty care is still a later ticket.

## Model

### Squad and roles (S1)
A **squad** (`Squad`) is a named set of agents with one leader. It is data, placed with `Simulation.AddSquad(...)` or
the `squad` section of scenario.json.

| Role | Finnish | What it does in the model |
|---|---|---|
| `SquadLeader` | ryhmänjohtaja (RJ) | Takes the squad's orders, plans the route, asks the scripted leader for permits, judges the enemy, commands formations and drills |
| `SecondInCommand` | varajohtaja (VJ) | Leads the support team; takes command when the RJ is out of action |
| `TeamLeader` | partionjohtaja (PJ) | Relays the RJ's commands to its team; leads its team's bounds in a drill |
| `MachineGunner` | konekivääriampuja | As a rifleman, with the machine gun (S4) |
| `Rifleman` | taistelija | Follows its slot and commands |

- **Fire teams (partio).** A squad has one or more teams, each with a team leader. The composition depends on the unit
  and is data, not code. Reference squad: the FDF's traditional squad, 9 agents in three teams of three:
  - **johtopartio** (command team): the RJ, who is also its team leader, + 2 riflemen
  - **kärkipartio** (point team): PJ + 2 riflemen
  - **tukipartio** (support team): the VJ as its team leader, the machine gunner and 1 rifleman **[PROPOSAL]**
- **Experience** per agent, two levels: `Inexperienced` (kokematon, for example a conscript in training) and
  `Experienced` (kokenut, a reservist or better).
- **Chain of command and the team leader's weight.** A command from the RJ reaches a member after a delay:
  - an `Experienced` member acts on the RJ's command directly: delay `t_react(Experienced)`;
  - an `Inexperienced` member acts only when its team leader relays it: delay `t_react(PJ) + t_relay +
    t_react(Inexperienced)`.

  So the team leader matters a lot for inexperienced members and almost nothing for experienced ones. If an
  inexperienced member's team leader is out of action, the next member of that team (by squad order) takes the team
  after `t_takeover`; until then the team's inexperienced members do not act on new commands.
- **Delays** (accepted starting values): `t_react` = 3 s inexperienced / 1 s experienced, `t_relay` = 2 s,
  `t_takeover` = 5 s.
- **Suppression slows command** (S5, once suppression has a strength, S4). A delay is multiplied by the suppression
  factor of the agent whose delay it is, taken when the delay starts:

  | Suppression of that agent | Factor **[PROPOSAL]** |
  |---|---|
  | none | 1 |
  | light | 1.5 |
  | heavy | 3 |

  - a suppressed member acts on a command later (`factor × t_react`);
  - a suppressed team leader relays later (`factor × t_relay`);
  - a suppressed successor takes command later (`factor × t_takeover`, 15 s when heavily suppressed).
- **Succession:** when the RJ is out of action, the VJ becomes the leader after its takeover delay, and the squad's
  order, permits, formation, enemy estimate and drill pass to it. It keeps leading the support team. If both are out,
  the point team's leader.
- These rules are doctrine, so they are data (`SquadDoctrine`, the `squadDoctrine` section of scenario.json), like
  `Doctrine` in 0002.

### Moving as a squad (S2)
- **Only the leader is ordered.** `MoveThroughCorridor` issued to the RJ moves the whole squad. Members get the
  internal state "follow the leader"; the operator does not order them one by one. An order given directly to a member
  takes it out of the squad's movement until it is ordered `Rejoin` **[PROPOSAL]**.
- **The leader plans, members follow.** Only the RJ runs the full A* corridor search (as in spec v0.1). Members do not:
  at 66 ms per search on st1, nine agents replanning often would be too slow. A member moves toward its **slot**
  (below) in a straight line; if that line is blocked (impassable cell, cliff, unpermitted hard boundary) it uses a
  local A* in a small window around itself **[PROPOSAL: 64 × 64 m]**.
- **Slots follow the route, not a compass.** A slot is given as `(along, across)` relative to the leader: `along`
  metres ahead of (or behind) the leader's position *measured along the leader's route*, and `across` metres to the
  side of the route at that point. A file therefore walks the leader's exact path around bends, and the point man of a
  wedge is ahead on the route even before a turn.
- **Cohesion:** the leader moves at `0.8 ×` its terrain speed **[PROPOSAL]**, so members have a reserve to catch up.
  It halts when an able member is more than `max(20 m, 2 × interval)` **[PROPOSAL]** behind its slot, and moves on
  when all are back. Pinned and out-of-action members do not hold the squad (until casualty care exists).
- **Corridor:** members stay inside the corridor's outer limit. If the formation is wider than the corridor's inner
  zone, the formation's lateral intervals are compressed evenly to fit; below a 2 m interval the squad moves in an
  open file instead **[PROPOSAL]**.
- **Hard boundaries:** members never cross a hard boundary without a permit. Only the RJ asks the scripted leader,
  and the whole squad halts while it waits. A permit given to the RJ covers the whole formation: it is valid for every
  member of the squad within `permit radius + the formation's half-width` of the requested point, so the squad crosses
  in formation. The half-width is that of the formation at the time of the request, after any compression to fit the
  corridor.
- **A member reaches the boundary first.** A member whose slot lies beyond an unpermitted boundary (the point man of a
  wedge, for example) stops at the boundary and waits for the squad, without crossing.
- **Deploying at the forest edge.** When the RJ must ask for permission to leave the forest, the squad first deploys
  along the edge, then the RJ asks:
  - the squad takes an open line along the edge, centred on the RJ's requested crossing point, every member in a
    forest cell `5 m` **[PROPOSAL]** inside the edge, at the open line's interval, compressed to fit the corridor;
  - members face out of the forest (observing; there is no observation model yet, so this is position and heading
    only);
  - the RJ asks when all able members are in place (`EdgeDeployed`), or after `60 s` **[PROPOSAL]** at the latest;
  - after a grant the squad crosses in the ordered formation; after a denial it re-forms and takes the respecting
    route, or reports blocked.

  Drawn boundaries from the order (a road, for example) do not trigger the deployment; the squad halts in formation.
- **Arrival:** when the RJ reaches the destination, the members take their slots around it and the squad is idle.

### Formations (S3)
`SetFormation(formation, interval?)` is issued to the RJ. The RJ commands it, and each member changes slot after its
command delay. Formations are data: a table of `(along, across)` slots in units of the interval, per squad size.

| Formation | Finnish | Shape | Default interval **[PROPOSAL]** |
|---|---|---|---|
| `File` | jono | one behind the other on the leader's path | 2 m |
| `OpenFile` | avojono | as above, open | 8 m |
| `DoubleFile` | parijono | two files, one each side of the path | 8 m along, 6 m across |
| `Line` | rivi | side by side across the path | 2 m |
| `OpenLine` | avorivi | as above, open | 8 m |
| `Wedge` | kiila | point man ahead, the rest in two arms back and out | 8 m |
| `EchelonLeft` / `EchelonRight` | porrastus vasemmalle / oikealle | diagonal line back to one side | 8 m |

Team order in every formation: the point team in front, the command team with the RJ in the middle, the support team
at the rear. In a file they follow in that order; in a line the point team is on one flank and the support team on the
other; in a wedge the point team is at the tip and the command and support teams are in the arms. Inside a team the
members keep the order of the squad data, team leader first.

### Combat model additions: machine gun and suppression strength (S4)
These change the 0002 combat model and are a part of their own, with no squad in the tests. Every 0002 test must
still pass unchanged.

- **Weapon class.** `WeaponProfile` gets a `Class`: `Rifle`, `MachineGun`, `HeavyMachineGun`. The class can be
  recognised by its sound, so a listener knows what fired (used by the enemy estimate in S5).
- **Bursts.** `WeaponProfile` gets `BurstRounds` (1 for a rifle). A burst is `BurstRounds` rounds at the same moment;
  each round has its own hit roll from `Simulation.Random` and logs its own `ShotFired`. Sound and flash are per burst.
- **Machine gun** (kevyt konekivääri) **[PROPOSAL]**: burst of 5 rounds every 3 s, per-round hit curve
  `0.3 · exp(−d / 250 m)`, 0 beyond 800 m, 500 rounds, sound range 2000 m, muzzle flash 2 s.
- **Heavy machine gun** (raskas konekivääri), for the enemy **[PROPOSAL]**: burst of 10 every 3 s,
  `0.35 · exp(−d / 400 m)`, 0 beyond 1500 m, 1000 rounds, sound range 3000 m, flash 2 s.
- **Suppression strength** comes from the rate of fire at the agent. Count the rounds fired at it, hits and misses,
  in the last `10 s` **[PROPOSAL]**:

  | Rounds in the last 10 s | `SuppressionLevel` |
  |---|---|
  | 0 | `None` |
  | 1–4 | `Light` |
  | 5 or more | `Heavy` |

  `IsSuppressed` stays true for `Light` and `Heavy`, and every 0002 effect of suppression is unchanged (prone, no
  movement, the 0.3 hit factor, 10 s from the last shot). The level adds the command delay factor above. One rifle
  firing every 2 s makes its target heavily suppressed after about 8 s; one machine-gun burst does it at once.
- New event `SuppressionLevelChanged(agent, level)`; `EventReplay` rebuilds the level.

### Contact, the enemy estimate and the leader's decisions (S5)
- **Contact:** a member has contact when it is shot at, is suppressed or hit, sees an enemy, or hears a shot within
  `300 m` **[PROPOSAL]**. It reports to the RJ after its reaction delay (`ContactReported`: the enemy's last known
  position and, if it fired, its weapon class). The RJ can also have contact itself.
- **The enemy estimate** (`EnemyEstimate`): the RJ's picture of the enemy, built only from contact reports:
  - the number of fighters: distinct enemies reported seen or heard firing. Hearing has no direction error in 0002, so
    two shooters are told apart; that is optimistic and is noted for later;
  - **reinforcing factors:** a heavy machine gun (recognised by its sound) and a strong fortification. There is no
    fortification model yet, so that factor is never present until a later ticket adds it;
  - it is logged as `EnemyEstimateChanged(squad, fighters, factors)`.
- **The RJ decides.** On the first contact report the RJ commands a drill (`CommandIssued`); members carry it out after
  their command delays (`CommandReceived`). The rules are `SquadDoctrine`, all values **[PROPOSAL]** unless stated:
  - on the first contact report: `TakeCoverAndReturnFire`;
  - **advance** (the RJ may do this on its own): from `TakeCoverAndReturnFire` to `BoundForward` when the enemy is
    **weak**: the estimate is at most `3` fighters and has no reinforcing factor (decided);
  - **break contact:** `BreakContact` after `2` casualties, or after `120 s` of fire without the enemy going out of
    action. Break contact wins over advance when both hold;
  - an order to the RJ (`Drill(...)`, `Advance`) overrides its own decision.
- **Drills.** This epic has three; more come in later tickets (counter-ambush, for example). A new drill is a new class
  with its own tests, without changes to the others.

| Drill | Finnish | What the members do |
|---|---|---|
| `TakeCoverAndReturnFire` | suojaan ja tulitaisteluun | halt, go prone, fire at the reported enemy when visible (rules of engagement raised to `FreeFire` toward it); the squad's movement order is kept and resumes on the RJ's `Advance` |
| `BoundForward` | tuli ja liike eteenpäin | one team moves a bound (`20 m` **[PROPOSAL]**) toward the enemy while the other two fire prone; then it goes prone and fires, and the next team moves |
| `BreakContact` | irtautuminen | as `BoundForward`, but away from the enemy, back along the route, until no member has seen or been shot at by the enemy for `60 s` **[PROPOSAL]**; then the squad re-forms |

### Bounding (S6)
- **Turns:** all three teams take turns; one team moves while the other two fire. The order is point, command, support
  **[PROPOSAL]**.
- **The team leader ends its bound:** an inexperienced team waits for its PJ's word; an experienced team moves on its
  own. This is where the team leader's role shows in a drill.
- **A partly immobile team:** the members who can move bound; a suppressed or pinned member stays and follows when it
  is free.
- **A team that cannot bound:** when no member of the team whose turn it is can move, its leader reports to the RJ that
  the bound is not possible (`BoundImpossible`), after its own reaction delay. The RJ gives the bound to the next team.
  The stuck team keeps firing and takes its turns again when it is free.

### Event log
New events: `SquadFormed(squad, leader, members)`, `LeaderChanged(squad, leader)`, `FormationChanged(squad,
formation, interval)`, `CommandIssued(leader, command)`, `CommandReceived(agent, command)`, `ContactReported(agent,
enemyPosition, weaponClass)`, `EnemyEstimateChanged(squad, fighters, factors)`, `DrillStarted(squad, drill)`,
`DrillEnded(squad, drill)`, `EdgeDeployed(squad)`, `BoundImpossible(team, leader)`, `SuppressionLevelChanged(agent,
level)`. `EventReplay` rebuilds squad, leader, formation, drill and suppression level, so the Godot replay can show
them.

### Scenario file
An optional `squad` section next to the existing single `agent` (schema stays 1):

```json
"squad": {
  "id": "r1",
  "start": [385120.0, 6672110.0],
  "formation": "wedge",
  "teams": ["command", "point", "support"],
  "members": [
    { "id": "rj",  "role": "squadLeader",     "team": "command", "experience": "experienced" },
    { "id": "pj1", "role": "teamLeader",      "team": "point",   "experience": "inexperienced" },
    { "id": "vj",  "role": "secondInCommand", "team": "support", "experience": "experienced" },
    { "id": "kk",  "role": "machineGunner",   "team": "support", "experience": "inexperienced", "weapon": "machineGun" }
  ]
},
"squadDoctrine": {
  "onContact": "takeCoverAndReturnFire",
  "advanceWhenEnemyAtMost": 3,
  "breakContactAfterCasualties": 2,
  "breakContactAfterSeconds": 120,
  "suppressionDelayFactors": { "light": 1.5, "heavy": 3 },
  "deployAtForestEdge": true
}
```

Every key of `squadDoctrine` is optional and keeps its default, as with `doctrine` in 0002. scenarios/README.md
documents them when the part that reads them lands. Members start in their slots around `start`; the order and the
corridor are the same as for one agent.

### Godot replay viewer
Lines from each member to its leader, the leader marked, the formation's name, suppression level, and commands,
contact reports and the enemy estimate in the event list. Part of S2 (movement) and S5 (commands).

## Parts and acceptance criteria (draft)

IDs: `SQ` squad and command, `SM` squad movement, `FO` formations, `WS` weapons and suppression, `UF` under fire,
`BO` bounding. Squad tests go in `tests/Twin.Scenarios.Tests/Squad/`, S4 tests next to the 0002 tests in
`tests/Twin.Scenarios.Tests/Combat/` and `tests/Twin.Sim.Tests/Combat/`, acceptance tests on st1 in
`tests/Twin.Acceptance.Tests/Squad/`.

### S1: squad, roles and chain of command
- SQ1 A squad is formed from agents already on the map; each member knows its squad, role, team and experience. Forming
  it moves no one.
- SQ2 Invalid squads are rejected: no leader, two leaders, a member in two squads, a team without a leader, unknown ids.
  Squad orders go only to the squad's leader.
- SQ3 A command from the RJ reaches an experienced member after `t_react(Experienced)` and an inexperienced member
  after the relay delay; the RJ's own team needs no relay.
- SQ4 A team whose leader is out of action is taken over by its next member after `t_takeover`. A command given while
  the team has no leader lapses for its inexperienced members; an experienced member acts on it.
- SQ5 When the RJ is out of action, the VJ leads after `t_takeover`. (Keeping the squad's order and permits is tested
  in S2.)
- SQ6 A squad's runs with the same seed give byte-identical logs; state rebuilt from the log matches the live state.

#### S1 spec decisions (2026-11-17)
- **Forming:** `AddSquad` groups agents already placed; it moves no one. Placing members in formation slots and the
  `squad` section of scenario.json come with S2.
- **First squad command:** `SetSquadPosture(posture)` ("Maahan!"), an order to the squad leader. The leader takes the
  posture at once; every member takes it when it acts on the command.
- **The leader's own team needs no relay:** when the commanding leader is also the member's team leader (the RJ and
  the command team, or the VJ and the support team after it takes command), an inexperienced member acts after its own
  `t_react`.
- **A command given while a team has no leader lapses** for that team's inexperienced members, including the member
  who then takes the team over. The leader must give it again after the takeover.
- **Until the takeover the squad has no leader** and takes no squad orders; the old leader takes none either (it is
  out of action).
- **Timing tolerance:** a member acts no earlier than its delay and at most one tick after it (dt = 0.5 s in tests,
  so every delay is a whole number of ticks), as in 0002 E2.

#### API the S1 tests need from `Twin.Sim`
- namespace `Twin.Sim.Squads`: enums `SquadRole` (`SquadLeader`, `SecondInCommand`, `TeamLeader`, `MachineGunner`,
  `Rifleman`) and `Experience` (`Inexperienced`, `Experienced`); records `SquadMember(UnitId, Role, Team, Experience)`,
  `SquadDefinition(Id, Teams, Members)` and `SquadDoctrine(ExperiencedReactionSeconds, InexperiencedReactionSeconds,
  RelaySeconds, TakeoverSeconds)`; class `Squad` with `Id`, `LeaderId` (null while there is none), `Teams`, `Members`,
  `Member(unitId)` and `TeamLeaderId(team)` (null while there is none).
- `Simulation`: `AddSquad(SquadDefinition)` (throws `ArgumentException` for an invalid squad and leaves no trace),
  `GetSquad(id)`, `SquadOf(unitId)` (null outside a squad).
- `SimulationConfig.SquadDoctrine`.
- order `SetSquadPosture(Posture)`, rejected with `OrderRejectedException` unless it goes to its squad's leader.
- events `SquadFormed(Time, AgentId = leader, SquadId, Teams, TeamLeaderIds)` (the teams and their first leaders let the
  replay rebuild the squad), `CommandIssued(Time, AgentId = leader, SquadId, CommandId, Command)`,
  `CommandReceived(Time, AgentId, CommandId)` (the member acts), `LeaderChanged(Time, AgentId = new leader, SquadId)`,
  `TeamLeaderChanged(Time, AgentId = new team leader, SquadId, Team)`. A leader's place is empty from its
  `HealthChanged(OutOfAction)` until the successor's change event; the replay reads it from that.
- `WorldSnapshot.Squads`: `SquadSnapshot(Id, LeaderId, TeamLeaderIds)` in team order, rebuilt by `EventReplay`.

Kit additions: `SpecSquad` (delays, team names, reference squad), `Scenario.ForSquad`, `WithSquad`,
`WithSquadDoctrine`, `ScenarioRun.Squad`, `Command`, `RunUntilOutOfAction`, `ReceivedBy`, and
`CommandAssertions.ShouldHaveActedOn` / `ShouldNotHaveActedOn`.

### S2: the squad moves through a corridor behind its leader
- SM1 Ordering only the RJ moves the whole squad to the destination, and every member is idle in its slot at the end.
- SM2 No member leaves the corridor's outer limit or crosses a cliff.
- SM3 The squad halts while the RJ waits for permission, and no member crosses an unpermitted hard boundary
  (including the forest edge, ADR 0009).
- SM4 After a grant the whole squad crosses in formation, every member within `permit radius + half-width` of the
  requested point; after a denial it takes the respecting route or reports blocked as one.
- SM5 A member whose slot lies beyond an unpermitted boundary waits at the boundary.
- SM6 Before leaving the forest, the squad deploys in an open line inside the edge, and the RJ asks only when it has
  deployed, or after the deployment time limit.
- SM7 A drawn boundary does not trigger the deployment; the squad halts in formation.
- SM8 The leader waits for a member that falls behind (for example one slowed by a bog) and moves on when it is back.
- SM9 A pinned or out-of-action member does not hold the squad.
- SM10 On st1, a squad crosses the scenario corridor; the time for a 30 min run is measured and recorded (like P1).

### S3: formations
- FO1 On flat open ground each formation's members are within `1 m` **[PROPOSAL]** of their slots after settling.
- FO2 In a file, members walk the leader's path: each member's track stays within `1 m` of the leader's track.
- FO3 `SetFormation` changes the members' slots after their command delays, not at once.
- FO4 A formation wider than the corridor is compressed to fit; a corridor too narrow for a line gives an open file.
- FO5 A slot in impassable terrain (a building, water) is replaced by the nearest passable point toward the route.

### S4: machine gun and suppression strength
- WS1 A machine gun fires bursts of `BurstRounds` rounds at its interval; each round is a `ShotFired` with its own
  hit roll.
- WS2 The per-round hit probability follows the weapon's curve and is 0 beyond its range (pure).
- WS3 The listener of a shot learns the weapon's class.
- WS4 An agent's suppression level is `Light` after 1–4 rounds in the last 10 s and `Heavy` after 5 or more, and
  falls back as the rounds age out.
- WS5 One machine-gun burst makes its target heavily suppressed at once; one rifle needs five shots.
- WS6 Every 0002 effect of suppression is the same at both levels (no 0002 test changes).
- WS7 The suppression level is rebuilt from the log.

### S5: contact, the enemy estimate and the leader's decisions
- UF1 A member that is shot at reports contact to the RJ after its reaction delay.
- UF2 On the first report the RJ commands `TakeCoverAndReturnFire`; every member halts, goes prone and fires at the
  enemy when it sees it.
- UF3 After the RJ's `Advance`, the squad resumes its corridor order in its formation.
- UF4 Inexperienced members carry out the command later than experienced ones, by the relay delay.
- UF5 A lightly or heavily suppressed member acts on a command after `1.5 ×` or `3 ×` its normal delay; a suppressed
  team leader relays that much later.
- UF6 A heavily suppressed VJ takes command after `3 × t_takeover`.
- UF7 The enemy estimate counts distinct enemies reported, and marks a heavy machine gun heard.
- UF8 Against at most 3 enemies with no heavy machine gun, the RJ changes to `BoundForward` on its own; against 4, or
  against one with a heavy machine gun, it does not.
- UF9 The RJ changes to `BreakContact` after 2 casualties or 120 s of fire, also when the enemy is weak.
- UF10 An order to the RJ overrides its own drill decision.

### S6: bounding drills
- BO1 In `BoundForward` exactly one team moves at a time while the other two fire, in turn point, command, support,
  and each bound is about `20 m`.
- BO2 In `BreakContact` the squad bounds away from the enemy along its route until it has had no contact for `60 s`,
  then re-forms.
- BO3 An inexperienced team's bound ends when its PJ says so; an experienced team's ends without waiting.
- BO4 A suppressed member stays while the rest of its team bounds, and follows when it is free.
- BO5 When no member of the team whose turn it is can move, its leader reports `BoundImpossible` and the RJ gives the
  bound to the next team.

## Kit additions (spec phase, `tests/Twin.Testing`)
- `SpecSquad`: the [PROPOSAL] values, slot tables and the reference squad; `SpecShooting` gets the machine gun, the
  heavy machine gun and the suppression thresholds.
- `Scenario.WithSquad("r1", at, SpecSquad.Reference, Formation.Wedge)`.
- `ScenarioRun.RunUntilSquadIdle("r1", timeout)`.
- Assertions: `sim.Squad("r1").Should().BeInFormation(Formation.Wedge, toleranceMeters)`, `.StayInside(corridor)`,
  `.HaveCommanded(Drill.TakeCoverAndReturnFire)`, `sim.Unit("x").Should().BeInSlot()`,
  `.HaveSuppressionLevel(SuppressionLevel.Heavy)`.
- `RouteAudit` over every member's track (corridor limit, cliffs, unpermitted boundaries).

## Out of scope
- Agent-to-agent collision and spacing inside cells (members may share a cell).
- Command range and radios: commands reach every member, only delayed.
- Casualty care (still reserved), ammunition sharing, platoon level.
- Cover-seeking ("move to the nearest cover"): there is no cover model yet; forest attenuation is the only cover.
- A fortification model (the estimate has a place for it) and direction error in hearing.
- Anti-tank and other weapon roles.

## Decisions (product owner, 2026-11-17)
1. **Hard boundary:** the RJ asks; its permit covers the whole formation's width and the squad crosses in formation.
2. **Forest edge:** the squad deploys along the edge before the RJ asks (S2).
3. **Drills in this epic:** take cover and return fire, break contact, bound forward. More drills later.
4. **Experience:** two levels, inexperienced and experienced. Experienced members act directly on the RJ's command.
5. **Composition:** depends on the unit (data). Reference: three teams of three (command, point, support).
6. **The VJ** leads the support team.
7. **Slots:** point team in front, command team with the RJ in the middle, support team at the rear.
8. **Delays:** `t_react` 3 s / 1 s, `t_relay` 2 s, `t_takeover` 5 s as starting values.
9. **Suppression and command:** a heavily suppressed VJ takes command three times slower, and suppressed members take
   commands more slowly. All of it is documented here and kept as data.
10. **Suppression strength** comes from the rate of fire at the agent (S4).
11. **Machine gun** in the support team in this epic (S4).
12. **Bounding:** one team moves while the other two fire, in turn.
13. **A partly immobile team:** those who can move bound. When none can, the team leader reports up, and the RJ gives
    the bound to the next team.
14. **The RJ advances on its own** when the enemy is weak: an estimated 3 fighters or fewer and no reinforcing
    factor (a strong fortification, a heavy machine gun).

No design questions are open. Each part's spec phase reviews its [PROPOSAL] values, most of all: the machine-gun and
heavy-machine-gun values, the suppression thresholds (1–4 / 5+ rounds in 10 s) and factors (1.5 / 3), the bound
length, and the machine gunner's place in the support team.
