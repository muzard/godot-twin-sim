# 0004. Squad: leadership, formations and drills under fire (Smoke test II)

**Status:** design agreed with the product owner (2026-11-17). S1 specified (tests reviewed) and implemented
(2026-11-17); S2a specified and implemented (2026-11-18); S2b–S6 have no tests yet. Drafted as 0003 in parallel with
[0003 (suppression and the contact drill)](0003-suppression-and-contact-drill.md), which merged first; renumbered to 0004
and its experience unified with 0003's (see "Experience unified with 0003" below). S4 and S5 overlap 0003 and must be
redesigned on top of it before their spec phase. Values marked **[PROPOSAL]** are
starting values; as in 0002, tests will read them from one place (`SpecSquad` and `SpecShooting` in
`tests/Twin.Testing`), so calibrating them later does not change what a test checks. The decisions are listed under
"Decisions" at the end.

This is an epic. It was planned as six parts (S1–S6 below); after S1 it is delivered as the parts of the
**ambush slice** below. Each part is its own spec → review → implement cycle and its own pull request, in this order.

## Delivery plan: the ambush slice (2026-11-18)
Goal: a squad moves through a corridor on st1 in file or wedge, runs into an ambush, every member reacts on its own,
and the RJ then directs the squad's fire and manoeuvres it: forward by bounds against a weak enemy, back by bounds
otherwise. Decided with the product owner on 2026-11-18:

- **Immediate action needs no leader.** Under fire each member does 0003's drill on its own (snap shots, cover,
  firing position, aimed fire, contact report). There is **no `TakeCoverAndReturnFire` command**; it is replaced by
  0003's drill. The RJ's part starts after it: **a) directing fire, b) manoeuvring the squad.**
- **Formations in S2:** file (jono) and wedge (kiila). The other five come with S3, after the slice.
- **S2 in full:** hard-boundary permits, cohesion, forest-edge deployment and the `squad` section of scenario.json.
- **S5 and S6 in full,** bounding included, in parts.
- **S4 is mostly done by 0003** (machine gun bursts, suppression per round, light and heavy suppression). What is left
  (the weapon class of a heard shot) moves into S5a.

| Part | Content | Criteria (redesigned in its spec phase) |
|---|---|---|
| **S1** | Squad, roles, chain of command, succession | SQ1–SQ6, done (PR #11) |
| **S2a** | Squad movement: only the RJ is ordered; file and wedge slots along the RJ's route; members move straight or by local A*; cohesion (leader at 0.8 ×, waits for stragglers); arrival in slots; `SetSquadFormation` through the chain of command; a member ordered directly leaves the squad's movement until `Rejoin` | SM1, SM2, SM8, SM9, FO1–FO3 for file and wedge |
| **S2b** | Hard boundaries: the RJ asks, the squad halts, a permit covers the formation, a member at the boundary waits; forest-edge deployment in an open line | SM3–SM7 |
| **S2c** | scenario.json `squad` section (and the enemies an ambush needs); a squad crossing st1, timed; the Godot replay marks the squad and its leader | SM10 |
| **S5a** | Contact reaches the RJ up the chain (relay delays, slowed by suppression); the RJ's enemy estimate (distinct enemies, heavy machine gun by the sound of a heard shot); the squad's movement is suspended under contact, not lost; suppression slows commands and takeover; squad commands and 0003's drill no longer conflict (the two S1 limitations) | UF1, UF5–UF7, WS3 |
| **S5b** | The RJ directs fire: commands members or teams to fire at reported enemies (fire control) | new criteria |
| **CC** | Casualty care (0002's foundation): tell the dead from the badly wounded; the dead are left, the wounded are carried or helped by squad mates (`GivingAid`), which slows the squad and takes fighters out of the fight; the RJ reports casualties to the platoon leader | new criteria; replaces S2a's provisional "out of action halts the squad" |
| **S6a** | Bounding forward: one team moves a bound while two fire; the RJ advances on its own against a weak enemy (≤ 3 fighters, no reinforcing factor); a team that cannot move reports `BoundImpossible` and the next one bounds; an order to the RJ overrides its decision; afterwards the squad re-forms and resumes the corridor | UF3, UF8, UF10, BO1–BO5 |
| **S6b** | Breaking contact by bounds back along the route after 2 casualties or 120 s of fire, until out of contact for 60 s; then the squad re-forms. Wounded are taken along (CC), not left; the squad's return fire frees a pinned member to move | UF9, BO for withdrawal |
| **Ambush** | The st1 ambush end to end as an acceptance test and a replay to watch in Godot | new |

**Open questions, each decided in its part's spec phase** (not yet decided):
- S2a (decided 2026-11-18): **every** order given straight to a member (`MoveTo`, `FireAt`, `SetPosture`, …; anything
  but `Rejoin`) detaches it until `Rejoin`.
- S5a: does a member that is not under fire itself (a squad mate is hit, or fire passes 10 m away) start the drill,
  or only the members 0003's "under fire" rule catches? With a leader that halts under fire, the squad halts either way.
- S5a: what the movement of members not in the drill does while the RJ is in it (halt in place, or go prone).
- S5b: how the RJ distributes fire: every member at the nearest reported enemy, or one enemy per team, or sectors.
- S6a: which team bounds first, the bound length (20 m [PROPOSAL]), where the RJ is, and when the advance ends
  (the enemy out of action, or a distance) and what follows (re-form and resume the corridor).
- S6b: how far back the squad withdraws and how it takes up its corridor again.

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
  `Experienced` (kokenut, a reservist or better). *Implemented through 0003's three-level `Combatant.Experience`;
  see "Experience unified with 0003".*
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
  when all are back. ~~Pinned and out-of-action members do not hold the squad~~ *(superseded by decision 18: nobody
  is left behind; see "S2a spec decisions")*.
- **Corridor:** members stay inside the corridor's outer limit. If the formation is wider than the corridor's inner
  zone, the formation's lateral intervals are compressed evenly to fit; ~~below a 2 m interval the squad moves in an
  open file instead~~ *(superseded by decision 17: the lateral gap stays at least 1 m, below that the squad moves in
  file)*.
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
- *Superseded on 2026-11-18 (see "Delivery plan: the ambush slice"): there is no `TakeCoverAndReturnFire` command;
  each member does 0003's drill on its own, and the RJ then directs fire (S5b) and manoeuvres (S6). The text below is
  the original design and is redesigned in S5's spec phase.*
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
    { "id": "rj",  "role": "squadLeader",     "team": "command", "experience": "regular" },
    { "id": "pj1", "role": "teamLeader",      "team": "point",   "experience": "conscript" },
    { "id": "vj",  "role": "secondInCommand", "team": "support", "experience": "regular" },
    { "id": "kk",  "role": "machineGunner",   "team": "support", "experience": "conscript", "weapon": "machineGun" }
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
corridor are the same as for one agent. A member's `experience` is its `Combatant.Experience` (`conscript`, `regular`,
`specialForces`; see "Experience unified with 0003").

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

#### Experience unified with 0003 (2026-11-17)
0003 (suppression and the contact drill) merged to `main` while S1 was in review. It gave every fighter
`Combatant.Experience` with three levels: `Conscript` ("conscript or reservist"), `Regular` (the default) and
`SpecialForces`. Two experience types for one soldier would disagree, so the squad has none of its own: a member's
experience is its `Combatant.Experience`, and the chain of command reads it as follows.

| `Combatant.Experience` | In the chain of command | Reaction to a command |
|---|---|---|
| `Conscript` | inexperienced: waits for its team leader's word | `InexperiencedReactionSeconds` (3 s) |
| `Regular` | experienced: acts on the squad leader's command directly | `ExperiencedReactionSeconds` (1 s) |
| `SpecialForces` | experienced | `ExperiencedReactionSeconds` (1 s) |

The mapping lives in one place in each: `SquadDoctrine.ActsDirectly` and `ReactionSeconds` in the simulation, and
`SpecSquad.Experienced` / `SpecSquad.Inexperienced` in the tests.

**This is provisional and expected to change:**
- The squad design (decision 4) counts **reservists as experienced**, but 0003's `Conscript` level puts conscripts and
  reservists together. Mapped this way, a reservist squad behaves like conscripts. A fourth level (`Reservist`), or
  moving reservists to `Regular`, is a decision for the product owner.
- 0003 already has a reaction time per experience (`ExperienceValues.ReactionSeconds`: 2.0 / 1.5 / 0.5 s, from coming
  under fire to the first snap shot). The squad's command reaction times (`SquadDoctrine`: 3 s / 1 s) are a different
  thing (reacting to a command), kept separate for now; whether they should derive from one table is open.
- `SpecialForces` is treated as `Regular` in the chain of command; it may deserve shorter delays.
- S4 (suppression strength by rate of fire) and S5 (contact, drills) were designed before 0003, which already models
  suppression as fire pressure with light and heavy levels, a contact report and an immediate action drill. Both parts
  must be redesigned on 0003's model before their spec phase.
- A side effect: an inexperienced squad member is a `Conscript` in combat too (lower accuracy, lower suppression
  threshold, slower snap shot).

#### Known limitations of S1 with 0003's drill (inputs to the S5 redesign)
No S1 scenario reaches these (squads hold fire by default, and the drill does not start under `HoldFire`), and none is
tested. They are recorded so S5 decides them on purpose:
- **A squad posture command and the drill both set a member's posture, and the last one wins.** A "Maahan!" that
  reaches a member rushing to cover leaves it prone in that drill stage (the drill moves only a crouching agent); a
  standing command makes a member in the drill stand under fire; and when the drill ends it restores the posture it
  started from, undoing a squad posture command received during it.
- **`SetSquadPosture` ends the leader's own drill** like any new order (only `SetPosture` is exempt), marks its
  attackers as answered, and does not restore the order the drill suspended, so the leader is left idle. Its doc
  comment says it keeps the current order, as `SetPosture` does.

#### API the S1 tests need from `Twin.Sim`
*(As implemented after the merge with 0003: `SquadMember` has no experience; the old `Squads.Experience` enum is gone.)*
- namespace `Twin.Sim.Squads`: enum `SquadRole` (`SquadLeader`, `SecondInCommand`, `TeamLeader`, `MachineGunner`,
  `Rifleman`); records `SquadMember(UnitId, Role, Team)` (experience is the fighter's `Combatant.Experience`),
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

Kit additions: `SpecSquad` (delays, team names, reference squad, `Experienced` = `Regular` and `Inexperienced` =
`Conscript`), `SquadSpec` / `SquadMemberSpec` (a squad definition plus each member's experience, so `WithSquad` can
place each fighter with it), `Scenario.ForSquad`, `WithSquad`,
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
- SM9 ~~A pinned or out-of-action member does not hold the squad.~~ *Superseded by decision 18:* nobody is left behind;
  a pinned member halts the squad, a wounded one is waited for (see "S2a spec decisions").
- SM10 On st1, a squad crosses the scenario corridor; the time for a 30 min run is measured and recorded (like P1).

#### S2a spec decisions (2026-11-18)
- **Interval: 10 m between neighbours in both file and wedge** (`SquadDoctrine.IntervalMeters`). ⚠ **Provisional:** in
  reality the spacing depends on the terrain (closer in forest and at night, wider in the open). It must be made
  terrain-dependent later; one value is a starting point only.
- **Compression:** a formation wider than the corridor's inner zone has its lateral offsets scaled down evenly to fit
  `w/2`, but the lateral gap between neighbouring slots stays at least **1 m** (`MinimumGapMeters`, to be raised later);
  if it cannot, the squad moves in file.
- **Formation order, front to back:** the teams with neither the squad leader nor the second-in-command, in team order
  (the point team), then the squad leader's team, then the second-in-command's team (decision 7). Within a team,
  squad order. Reference squad: pj, k1, k2, rj, j1, j2, vj, kk, t1.
- **File (jono):** everyone on the RJ's path, 10 m apart, in formation order; the point team ahead of the RJ on its
  route, the rest behind it on the path it walked.
- **Wedge (kiila):** one squad wedge. The first member (pj) at the apex; the others alternate left and right arm in
  formation order, 10 m apart along each arm at 45° (7.07 m back and 7.07 m out per step). The RJ walks the route
  itself, on the wedge's axis at the depth of its place, so its own arm slot stays empty:

  ```
          pj
        k1  k2
      ·  rj  j1
    j2          vj
  kk              t1
  ```
- **Slots follow the route.** A slot is `(along, across)` relative to the RJ, measured along the RJ's path (its planned
  route ahead, the path it walked behind) and across it at that point. Beyond the ends the path is extended straight.
  The members follow whatever moves the RJ: a corridor order or a `MoveTo` (a straight route). Before the RJ's first
  movement order nobody moves (forming a squad moves no one, SQ1).
- **A slot that is not usable** (impassable terrain, or outside the corridor's outer limit) is moved to the nearest
  usable point on the line from the slot toward its point on the RJ's path (FO5). If that point on the path is not
  usable either (a file's rear slots behind the start of a narrow corridor, for example), the point moves along the path
  toward the RJ until it is. A route through cell centers can jog back a little at its ends (to the start cell's center,
  past the last cell center to the exact destination); such jogs of up to 3 m are left out of the path so they do not
  turn the formation round.
- **Members** move toward their slot at their own terrain speed, in a straight line, or by A* in a `64 × 64 m` window
  around them when the straight line is blocked; they never leave the corridor's outer limit or cross a cliff. Hard
  boundaries and the forest edge are S2b: S2a tests have none on the way.
- **Cohesion:** the RJ moves at `0.8 ×` its speed. It halts (`UnitActivity.WaitingForSquad`) while an able member is
  more than `20 m` from its slot, and moves on when every able member is within `5 m` of it. So a squad first forms up,
  then moves off. Members detached by a direct order do not count.
- **Nobody is left behind** (product owner, 2026-11-18). A squad does not move on normally under fire: **while any member
  is pinned, the whole squad halts** (`WaitingForSquad`). A wounded member (slower) is waited for like any straggler.
  The dead are left and the badly wounded are carried or helped, but the model cannot yet tell them apart
  (`OutOfAction` covers both); until casualty care exists, **an out-of-action member halts the squad too**
  (provisional). Breaking contact, where the squad's return fire frees a pinned member to move, belongs to S6b.
- **Arrival:** when the RJ arrives, the members take their slots around it (the path extended beyond the destination)
  and become idle there.
- **Direct orders:** every order given straight to a member (not the RJ), not only a movement order, detaches it from
  the squad's movement until it is ordered `Rejoin` (product owner, 2026-11-18); the squad does not wait for it meanwhile. `Rejoin` to the leader or to an agent outside a squad is
  rejected; to a member already following it changes nothing.
- **Formation changes:** `SetSquadFormation(formation)` to the RJ, through the chain of command as in S1: each member
  takes its new slot when it acts on the command. The squad's formation at forming is `SquadDefinition.Formation`
  (default file).

#### Known limitations of S2a (inputs to the next parts)
None of these is reached by an S2a scenario; each is recorded so its part decides it on purpose.
- **A new leader does not take over the movement order.** When the RJ falls mid-move, the VJ takes command (S1) with
  no movement order of its own; the members hold their ground until it gets one (tested). The ticket's design passes the
  squad's order and permits to the successor: S2b, with permits.
- **The leader in 0003's drill:** while the leader is in the drill the members do not follow (they hold, or do their
  own drill if they are under fire); its rush to cover is not recorded as the squad's path. A following member's own
  drill searches cover without a corridor, since it has no order of its own. S5a.
- **A leader waiting for permission** (S2b): the route ahead is then a straight line to the destination, so the
  formation turns toward it while the squad halts.
- **Straight moves ignore hard boundaries and the forest edge;** a member's local path search respects them. S2b makes
  this consistent.
- **Performance on st1** is not measured yet (SM10, S2c). Slots are computed a few times per step; the trail keeps a
  point every 0.5 m and only as far back as the deepest slot needs; the route ahead is cut where no slot reaches; a
  blocked member searches its 64 m window again every step.

#### API the S2a tests need from `Twin.Sim`
- `Twin.Sim.Squads`: enum `SquadFormation` (`File`, `Wedge`); `SquadDefinition(..., Formation = File)`;
  `SquadDoctrine(..., IntervalMeters = 10, MinimumGapMeters = 1, LeaderSpeedFactor = 0.8, StragglerMeters = 20,
  RegroupMeters = 5, LocalSearchMeters = 64)`; `Squad.Formation` (commanded), `Squad.FormationOf(unitId)` (the one the
  member acts on), `Squad.IsFollowing(unitId)` (true unless detached by a direct order); record
  `FormationSlot(UnitId, Along, Across)` and static `FormationSlots.For(formation, SquadDefinition, interval)`: each
  member's slot relative to the RJ before compression, in formation order (across positive to the left).
- `Simulation.SlotOf(unitId)`: the member's current slot on the map, or null when it is not following.
- orders `SetSquadFormation(SquadFormation)` (leader only) and `Rejoin` (members only).
- `UnitActivity.WaitingForSquad`.
- events `FormationChanged(Time, AgentId = leader, SquadId, Formation)`; `SquadFormed` gains `Formation`;
  `SquadSnapshot.Formation`, rebuilt by `EventReplay`.

#### S2a tests
| ID | Test | Checks |
|---|---|---|
| FO1 | file and wedge slots (pure) | the layouts above, reference squad |
| FO1 | members are in their slots once formed up (file, wedge, flat open ground) | within 1 m, while the squad moves straight |
| FO2 | in file each member walks the leader's path | track within 1 m of the RJ's track after forming up |
| FO3 | `SetSquadFormation` changes each member's slot when it acts on the command | S1 delays; slots before and after |
| FO4 | a wedge is compressed to fit a corridor; too narrow gives a file | all slots within `w/2`, gaps ≥ 1 m |
| FO5 | a slot inside a building is moved toward the route | slot passable, on the line to the route |
| SM1 | ordering only the RJ moves the squad; all idle in their slots at the end | file and wedge, `MoveThroughCorridor` and `MoveTo` |
| SM1 | the squad forms up before it moves off | RJ waits until all are within 5 m |
| SM2 | no member leaves the outer limit or crosses a cliff | trails, L-shaped corridor, a cliff beside the route |
| SM8 | the leader waits for a member slowed by a bog and moves on when it is back | `WaitingForSquad`, then moving |
| SM9 | nobody is left behind: a pinned member halts the whole squad; a wounded one is waited for; an out-of-action one halts the squad until casualty care exists | RJ waits at the start; squad arrives with the wounded member in its slot |
| — | a directly ordered member is detached; `Rejoin` brings it back | squad does not wait, then waits for it |
| — | invalid orders | `SetSquadFormation` to a non-leader, `Rejoin` to the leader or outside a squad |
| SQ6 | determinism and replay with squad movement | same log, formation rebuilt from the log |
| — | any direct order detaches (`DirectOrderTests`) | `FireAt`, `SetPosture`, `CeaseFire`, `Stop`; the squad does not wait for a member ordered to fire |
| — | edge cases found in review (`SquadMovementEdgeCaseTests`) | a member finds its way round a building to a slot beyond its search window; members hold their ground when the leader falls mid-move |

Files: `tests/Twin.Sim.Tests/Squads/FormationSlotsTests.cs` (pure FO1) and, in `tests/Twin.Scenarios.Tests/Squad/`,
`SquadMovementTests.cs`, `FormationTests.cs` and `SquadMovementDeterminismTests.cs`. Kit additions: `SpecSquad`
movement values (`IntervalMeters`, `MinimumGapMeters`, `LeaderSpeedFactor`, `StragglerMeters`, `RegroupMeters`,
`LocalSearchMeters`, `WedgeStepMeters`) in `SpecSquad.Doctrine`, `SquadSpec.Formation`, `UnitAssertions.HaveActivity`,
and `SquadMovementAssertions` (`SquadSettled`, `RunUntilSquadSettled`, `ShouldBeInSlot`, `ShouldAllBeInSlots`,
`OnStraightRoute`).

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

## Decisions (product owner, 2026-11-18)
15. **Immediate action needs no leader:** under fire every member does 0003's drill on its own; the RJ then directs
    fire and manoeuvres the squad. No `TakeCoverAndReturnFire` command.
16. **Delivery:** the ambush slice (S2a–S2c, S5a–S5b, casualty care (CC, added by decision 18), S6a–S6b, then the
    ambush acceptance test); file and wedge in S2, the other formations in S3 after the slice; S2, S5 and S6 in full.
17. **S2a:** 10 m interval in file and wedge, provisional (spacing must become terrain-dependent); compression
    allowed with lateral gaps of at least 1 m for now; one squad wedge with the point team leader at the apex; a member
    ordered directly (by any order) is detached until `Rejoin`; compression and unusable slots (FO4, FO5) in S2a.
18. **Nobody is left behind:** a squad does not move on normally under fire; a pinned member halts the squad, at least
    its team stays with it. Breaking contact is an option, and the squad's return fire should then free the pinned
    fighter to move (S6b). The dead are left; the wounded are carried or helped, and casualties are reported to the
    platoon leader. Casualty care must be implemented (part CC).

## Decisions (product owner, 2026-11-17)
1. **Hard boundary:** the RJ asks; its permit covers the whole formation's width and the squad crosses in formation.
2. **Forest edge:** the squad deploys along the edge before the RJ asks (S2).
3. **Drills in this epic:** take cover and return fire, break contact, bound forward. More drills later.
4. **Experience:** two levels, inexperienced and experienced. Experienced members act directly on the RJ's command.
   (Implemented through 0003's `Combatant.Experience`, provisionally: see "Experience unified with 0003".)
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
