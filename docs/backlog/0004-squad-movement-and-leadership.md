# 0004. Squad: leadership, formations and drills under fire (Smoke test II)

**Status:** design agreed with the product owner (2026-11-17). S1 specified (tests reviewed) and implemented
(2026-11-17); S2a and S2b specified and implemented (2026-11-18); S2c specified and implemented (2026-11-18); S2d specified and implemented (2026-11-19); S5a1
(spotting) specified and implemented (2026-11-18); S5a2 specified and implemented (2026-11-19); S5b specified and implemented (2026-11-19);
CC and S6 have no tests yet. Drafted as 0003 in parallel with
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
| **S2b** | Hard boundaries: the RJ asks, the squad halts in formation, a permit covers the formation; slots across a boundary to the side tuck in (a double file); a member whose way forward crosses one asks the RJ; the RJ briefs its movement order to the squad, and a successor carries it on or asks the platoon leader to repeat it | SM3–SM5, SM7, SU1–SU4 |
| **S2c** | Forest-edge deployment: the squad deploys in an open line 5 m inside the edge before the RJ asks to leave the forest; members' headings and observation sectors (data only) | SM6, observation (decision 20) |
| **S2d** | scenario.json `squad` section (and the enemies an ambush needs); a squad crossing st1, timed; the Godot replay marks the squad and its leader | SM10 |
| **S5a1** | Spotting: spotting time, watch sectors in file and wedge, the spot report (voice range, relay to the RJ), the squad halts and crouches when the RJ knows; free fire only at spotted enemies (decision 21) | SP1–SP12 |
| **S5a** (S5a2) | Contact reaches the RJ up the chain (relay delays, slowed by suppression); the RJ's enemy estimate (distinct enemies, heavy machine gun by the sound of a heard shot); the squad's movement is suspended under contact, not lost; suppression slows commands and takeover; squad commands and 0003's drill no longer conflict (the two S1 limitations) | UF1, UF5–UF7, WS3 |
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
  corridor. (For the forest edge this is replaced by the squad-wide permit, decided 2026-11-19: see below.)
- **A member reaches the boundary first.** A member whose slot lies beyond an unpermitted boundary (the point man of a
  wedge, for example) stops at the boundary and waits for the squad, without crossing.
- **Deploying at the forest edge.** When the RJ must ask for permission to leave the forest, the squad first deploys
  along the edge, then the RJ asks:
  - the squad takes an open line along the edge, centred on the RJ, every member in a forest cell `5 m`
    **[PROPOSAL]** inside the edge, at the open line's interval; members that do not fit the corridor form a second line
    `10 m` deeper (see "S2c spec decisions");
  - members face out of the forest and observe it (headings and observation sectors, data only until the observation
    model arrives);
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
members keep the order of the squad data, team leader first. *(Superseded for the open line and the wedge by decision
20: support team left with its machine gunner next to the RJ, the command team, the point team right; the wedge is
that line folded back at its middle. S3 revisits the other formations.)*

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

#### Known limitations of S1 with 0003's drill (inputs to the S5 redesign) — *resolved in S5a2: the drill wins*
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
- **Wedge (kiila):** *(redefined in S2c, decision 20; the original S2a wedge had pj at the apex and the others
  alternating left and right in formation order.)* One squad wedge: the open line (see "S2c spec decisions") folded
  back at its middle member, 10 m apart along each arm at 45° (7.07 m back and 7.07 m out per step). The RJ walks the
  route itself, on the left arm one step behind and left of the apex, so the wedge reaches 21.2 m to its left and
  35.4 m to its right:

  ```
            j1
         rj    j2
      kk          pj
   vj                k1
  t1                    k2
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
- ~~**A new leader does not take over the movement order.**~~ *Resolved in S2b:* a successor that heard the RJ's brief
  carries the order on (the members re-form round it); one that did not asks the platoon leader to repeat it. The S2a
  edge test was updated accordingly.
- **The leader in 0003's drill:** while the leader is in the drill the members do not follow (they hold, or do their
  own drill if they are under fire); its rush to cover is not recorded as the squad's path. A following member's own
  drill searches cover without a corridor, since it has no order of its own. S5a.
- ~~**A leader waiting for permission**~~ *Resolved in S2b:* the formation keeps facing the way it came.
- ~~**Straight moves ignore hard boundaries and the forest edge.**~~ *Resolved in S2b.*
- **Performance on st1** is not measured yet (SM10, S2d). Slots are computed a few times per step; the trail keeps a
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

#### S2b spec decisions (2026-11-18)
Decided with the product owner; S2b was split from the forest-edge deployment (now S2c) to keep the part small.
- **The RJ asks as in v0.1** when its plan needs a permit (a drawn line or the forest edge); the **squad halts in
  formation** around it, facing the direction it last walked (the route ahead is ignored while the RJ waits, so the
  formation does not swing toward the destination). A drawn line never triggers a deployment (SM7).
- **A permit covers the formation:** it is the squad's, valid for every member within `permit radius + the formation's
  half-width` of the requested point (the half-width after any compression, when the request is made).
- **No member crosses an unpermitted hard boundary,** drawn or the forest edge, on a straight move or a path search.
- **Tucking in (SM5, sideways):** a slot that lies across an unpermitted boundary from its point on the RJ's path moves
  toward that point to the nearest point on the path's side. Several such members end up side by side along the
  boundary, so the formation becomes a double file (parijono) there.
- **Asking the RJ (SM5, forward):** a member whose way forward to its slot crosses an unpermitted boundary stops just
  before it (`WaitingForPermission`) and asks the RJ (`PermissionRequested` by the member). After the relay time (2 s)
  the RJ asks the platoon leader (the scripted leader; `PermissionRequested` by the RJ, same boundary and point) unless a
  squad permit already covers it. The squad halts while it waits. A grant is a squad permit as above; after a denial or a
  timeout no one in the squad asks to cross that boundary again under this order: a member goes round it if its path
  search finds a way, and otherwise stays on its side while the squad waits for it (nobody is left behind). *(Changed
  in review, 2026-11-18: the earlier text said its slot tucks in, which left it stuck for good when the boundary lies
  ahead of it rather than to the side.)*
- **A permit lasts one order:** the squad's permits, refusals and any pending request lapse when its leader gets a new
  order from the platoon leader; a successor carrying the old order on keeps them.
- **The order brief (SU1):** when the RJ gets a movement order it tells it to the squad at once, through the chain of
  command (`CommandIssued` with the order; each member knows it when it acts on it, S1 delays). The members follow the
  RJ's movement meanwhile whether they have heard it or not.
- **Succession (SU2–SU4):** a successor that has heard the brief carries on with the squad's movement order and its
  permits. One that has not asks the platoon leader (`OrderRequested`); the scripted leader repeats it after its delay
  (`OrderRepeated`, any answer but `NoAnswer`), and the successor then carries on and briefs the squad. Until then the
  squad holds its ground. (With S2a's provisional rule an out-of-action member still halts the squad, so in S2b "carries
  on" is seen as the successor holding the order and waiting for the squad.)

#### API the S2b tests need from `Twin.Sim`
- `Squad.Permits`: the squad's permits, each with the radius widened by the formation's half-width (a forest-edge
  permit's radius is infinite since the squad-wide permit, 2026-11-19).
- `Squad.KnowsOrder(unitId)`: true once the member has acted on the RJ's brief of the current movement order (the RJ
  knows its own order).
- events `OrderRequested(Time, AgentId = successor, SquadId)` and `OrderRepeated(Time, AgentId = successor, SquadId)`;
  `PermissionRequested` by a member for its request to the RJ.

#### S2b tests
| ID | Test | Checks |
|---|---|---|
| SM3 | the squad halts in formation while the RJ waits for permission | RJ `WaitingForPermission`; every member idle in its slot, laid out along the walked path |
| SM3 | no member crosses an unpermitted boundary | `RouteAudit` on every member's trail against the squad's permits |
| SM4 | after a grant the squad crosses in formation and arrives | every crossing within `radius + half-width` of the requested point |
| SM4 | after a denial the squad takes the respecting route, or halts as one when there is none | no crossing; RJ `Blocked`, members idle |
| SM5 | slots across a boundary to the side tuck in to a double file | tucked slots on the path's side, within 1 m of the boundary |
| SM5 | a member whose way forward crosses a boundary asks the RJ, which asks the platoon leader after the relay time | event order and times; the squad halts; after the grant it crosses |
| SM7 | a drawn boundary halts the squad where the RJ asks; it does not move up to the line first | members and RJ still where they were |
| SU1 | the RJ briefs its movement order through the chain of command | `CommandIssued` with the order; `KnowsOrder` after S1 delays |
| SU2 | a successor that heard the brief carries on with the order and the permits | new leader holds the corridor order |
| SU3 | a successor that did not hear it asks the platoon leader, who repeats it after the leader's delay | `OrderRequested`, `OrderRepeated`; then the order |
| SU4 | a platoon leader that never answers leaves the squad holding its ground | no order, members still |
| — | edge cases found in review (`SquadPermitEdgeCaseTests`) | a permit does not carry over to the next order; no member leaves the forest without a permit; after a denial a member goes round and the squad arrives; a successor carries the squad's permits |

Also changed with approval: S2a's edge test now checks that the members re-form round a successor that carries the
order on, and SU2 waits for vj's suppression from the nearby fire to end before it checks that vj waits for the squad.
Known limitations of S2b: a member's request is not withdrawn if its slot moves and it no longer needs to cross (the
squad waits for the answer); the Godot replay matches permission answers to requests by agent, so a member's request
stays marked unanswered there (S2d).

#### S2c spec decisions (2026-11-18)
Decided with the product owner.
- **Only a squad deploys.** A lone agent asks to leave the forest as in v0.1, where it stands when it plans.
- **When:** the RJ's plan needs a permit and the first unpermitted crossing is the forest edge (a drawn line first is
  S2b: the squad halts in formation, SM7). The RJ does not ask where it stands: it walks on along its reference route,
  with the squad in formation behind it, to the last point before the crossing that lies `5 m` **[PROPOSAL]** inside
  the edge (inside = distance to the nearest cell outside the forest that counts as leaving it, ADR 0009). If it is
  already closer to the edge, it deploys where it is.
- **The deployment is a squad command:** there the RJ commands it (`CommandIssued` with `DeployAtForestEdge`); each
  member moves to its spot in the line when the command reaches it (S1 delays).
- **The open line (avorivi), teams side by side:** left to right facing out of the forest, the second-in-command's team
  (its riflemen outermost, then the second-in-command, then the machine gunner next to the RJ), the RJ and its team in
  squad order, then the other teams in team order, each in squad order. One interval (10 m) apart, the RJ at its point.
  Reference squad: t1, vj, kk, **rj**, j1, j2, pj, k1, k2 (three to the RJ's left, five to its right). *The general rule
  (machine gunner next to the RJ, riflemen outermost) is our reading of the product owner's order for the reference
  squad.* The wedge is this line folded back at its middle member (decision 20), so the line is the wedge with its
  tails brought level. The line runs along the edge (perpendicular to the direction from the RJ to the nearest point
  of the edge), and each spot is moved along that direction to the forest cell `5 m` inside the edge. The order is the
  same whatever formation the squad arrives in.
- **A second line (provisional):** a spot outside the corridor's outer limit, outside the forest, or impassable is not
  used. The members left over form a second line `10 m` **[PROPOSAL]** deeper (15 m inside the edge), in the same
  order, one interval apart, centred on the RJ's axis (k1 and k2 in the 14 m test corridor); a third line if needed,
  and so on. *Compressing the line is the
  alternative and may replace this.*
- **Asking:** the RJ asks (`PermissionRequested` for the forest edge, as v0.1) in the same step as the squad has
  deployed: every able (not out of action) member that follows the squad stands in its spot (`EdgeDeployed`, by the RJ).
  At the latest it asks `60 s` **[PROPOSAL]** after its command, without `EdgeDeployed`.
- **The permit covers the line:** radius plus the line's half-width (the farthest first-line spot from the RJ), as S2b's
  half-width at the time of the request. (Replaced 2026-11-19 by the squad-wide forest-edge permit: see below.)
- **After a grant** the squad crosses and re-forms in its ordered formation in the open; **after a denial** it takes the
  respecting route or reports blocked as in S2b, without leaving the forest.

**Headings and observation (S2c, data only).** The observation model is in development, so S2c gives every member a
heading and observation sectors, and seeing does not depend on them yet. Values are **[PROPOSAL]**:
- every sector is `90°` wide (±45° about its centre); a member faces the centre of its first sector;
- on the move (directions relative to the RJ's path at the member's slot): the point observes ahead and the others
  their own side. In a file the point is the first, and the others alternate by their place in the file, odd places
  left and even places right; in a wedge the point is the apex and each arm observes its own side. The two at the back
  (the last two of a file, the ends of a wedge's arms) also observe the rear and the air;
- deployed at the forest edge: everyone observes out of the forest; the two ends of the first line also observe their
  flank along the edge, the rear and the air;
- together the squad observes all round (360°) on the move and at the edge;
- an agent outside a squad faces the way it moves and keeps its heading when it stops; it has no sectors.

#### API the S2c tests need from `Twin.Sim`
- `SquadDoctrine`: `EdgeInsetMeters` (5), `EdgeDeploymentSeconds` (60), `SecondLineDepthMeters` (10),
  `SectorWidthDegrees` (90).
- event `EdgeDeployed(Time, AgentId = RJ, SquadId)`; the deployment command logged as `CommandIssued` whose `Command`
  starts with `DeployAtForestEdge`.
- `FormationSlots.LineOrder(SquadDefinition)`: the open line's members, left to right.
- `Unit.Heading` (a unit vector in map coordinates), `Unit.ObservationSectors` (`ObservationSector(Centre, WidthDegrees)`,
  centre a unit vector), `Unit.WatchesAir`, and `Unit.Observes(direction)`: true when a sector covers the direction,
  edges included.

#### S2c tests
| ID | Test | Checks |
|---|---|---|
| SM6 | the squad deploys in an open line 5 m inside the forest edge | every member 5 ± 1 m inside, in line order 10 m apart, in the forest, in its slot |
| SM6 | the RJ asks to leave the forest only once the squad has deployed | `EdgeDeployed`, then `PermissionRequested` for the forest edge in the same step; the command came first; RJ 5 m inside |
| SM6 | members take their line spots when the deployment command reaches them | S1 delays for k2 (1 s) and k1 (8 s) |
| SM6 | the RJ asks at the deployment time limit when the line has not formed | a 3 s limit: request 3 s after the command, no `EdgeDeployed` |
| SM6 | after a grant the squad leaves the forest within the permit and arrives in formation | ~~permit radius 10 + 50~~ the permit covers the whole edge (2026-11-19); `RouteAudit` on every trail; slots at B |
| SM6 | after a denial with no way round the squad stays in the forest and halts as one | RJ `Blocked`; nobody past the edge; all still |
| SM6 | members that do not fit the corridor form a second line 10 m deeper | 14 m corridor: k1 and k2 15 m inside, ±5 m about the RJ's axis |
| SM6 | the line follows a slanted forest edge | 30° edge: every member 5 ± 1.5 m inside, 10 m apart along the edge |
| SM6 | a deployment survives a round trip through the log and repeats byte for byte | `EdgeDeployed` in the log |
| — | observation (`SquadObservationTests`) | file: pj ahead, odd places left, even right, kk and t1 rear and air; wedge: j1 ahead, each arm its side, t1 and k2 rear and air; all round; facing the first sector; at the edge all face out, the ends (t1, k2) watch flanks, rear and air, all round; a lone agent faces the way it moves |
| FO1 | the open line and the wedge redefined (`FormationSlotsTests`, pure) | `FormationSlots.LineOrder`; the wedge is the line folded at j1, neighbours 10 m apart |

Changed in the S2c spec with the redefined wedge: S2a's wedge tests (FO1 layout, FO4 scale now from k2's 5 steps,
FO5 with k2's slot in a building, the cliff and bog comments), S2b's (SM4 permit radius `10 + 5 steps` and k2's
crossing, SM5 tucks j2, pj, k1 and k2, and j1 the apex asks first), and the default corridor of the S2a movement and
formation tests widened from 60 to 80 m (S2b's wedge tests too) so the wider wedge fits without compression.

Known limitations of S2c: headings and sectors are not in the event log or the replay yet (S2d; *since the st1 ambush
demo the watch direction, the sectors and the air watch are logged as `WatchChanged` and replayed, and `AgentPlaced`
carries the side; `Unit.Heading` is still not logged*); sectors do not affect
seeing until the observation model; the line's second-line rule is provisional (compression may replace it).
Choices made while implementing: while the RJ walks up to the edge and while the squad is deployed, a member that reaches
the forest edge waits there without asking (the RJ asks for the squad); the edge's direction is fitted through the edge
cells within 15 m of the crossing; a member stands in its spot when within 0.5 m of it; after a denial with no way round
the squad stays in its line; a member waiting at the edge for the RJ does not hold the squad back; a member that rejoined
after the line was laid out has no spot and is not waited for. Edge cases found in review are tested in
`SquadEdgeDeploymentEdgeCaseTests` (a squad in file, a squad that starts in the open, a clearing on the way and a second
edge, a leader already closer than 5 m) and `HeadingEdgeCaseTests` (a lone agent faces any way it moves, keeps its
heading when it stops).

#### S2d spec decisions (2026-11-18; tests reviewed, implemented 2026-11-19)
Decided with the product owner: the `squad` section as drafted above (members listed one by one); enemies as a list of
fighters; the st1 ambush demo becomes a scenario file; the replay marks the squad leader and shows the squad's id and
formation. The details below were accepted with the tests.
- **`squad` or `agent`:** a scenario has exactly one of them. A squad needs `id`, `start` (ETRS), `teams` and `members`
  (each `id`, `role`, `team`; optional `experience`, default `regular`; optional `weapon`, default `rifle`; optional
  `rules`, default the squad's). The squad's optional `formation` (default `file`) and `rules` (`freeFire`,
  `returnFire` or `holdFire`; default `returnFire`) apply to every member. The definition is validated as in
  `Simulation.AddSquad` (one squad leader, a leader in every team and so on); an invalid squad fails the load with a
  message naming the problem.
- **Where members start:** the squad leader at `start`, everyone else in its slot of the starting formation laid out
  along the corridor's first segment (facing the way the corridor runs), so the squad moves off already formed.
- **Weapons by name:** `rifle` (`WeaponProfile.Rifle`) and `machineGun`, a new `WeaponProfile.MachineGun` in
  `Twin.Sim` with backlog 0003's open values (bursts of 5, 800 m, 500 rounds; to be calibrated).
- **`enemies`:** an optional list of fighters, each `id`, `position` (ETRS); optional `side` (default `red`),
  `posture` (default `standing`), `rules` (default `freeFire`), `weapon` (default `rifle`), `experience` (default
  `regular`) and `watchToward` (ETRS; the fighter watches that point, as a `Watch` order). Enemies get no movement
  order (later parts).
- **`world`:** optional path of the world directory, relative to the scenario directory (default `world`), so
  `scenarios/st1-ambush` uses `../st1/world` instead of a copy of the map.
- **The run:** `CreateSimulation` places the squad (or the agent) and the enemies and issues the corridor order to the
  squad leader. `./dev replay` ends a squad run when the leader has arrived or is blocked, when one side has nobody in
  action, or when shots have been fired and nobody has fired for 120 s (as the ambush demo did).
- **`scenarios/st1-ambush`:** the ambush demo as a file (same squad, red, corridor and leader); the code demo
  (`AmbushDemo`) is removed and `./dev replay st1-ambush` runs the file.
- **SM10:** an acceptance test runs the reference squad through the st1 corridor from A for 30 min of simulated time,
  measures the wall-clock time with `Stopwatch` (as P1) and prints it; it fails if the squad has not got past the forest
  road at x = 330 (a check that the run is meaningful), not on the time.
- **The replay (Godot):** the squad leader's dot gets a second ring, and the squad's id and formation are written next
  to it (`blue: wedge`; `halted` while the squad is halted). Presentation only, no tests.

#### API the S2d tests need from `Twin.Sim`
- `ScenarioDefinition`: `Squad` (a `SquadDefinition`, null for an agent scenario), `SquadStart`, `SquadCombatants`
  (each member's `Combatant`, by id), `Enemies` (`ScenarioEnemy`: `Id`, local `Position`, `Combatant`, `Posture`,
  `WatchToward`, null if none). *(Changed with approval in the implement phase:)* `AgentId` is the agent the corridor
  order goes to, so in a squad scenario the squad leader; a null id would have broken the st1 tests' non-null use.
- `WeaponProfile.MachineGun`.

#### S2d tests
| Test | Checks |
|---|---|
| `ScenarioSquadTests` (Twin.Sim.Tests) | the squad section and each member's fighter; defaults (regular, rifle, the squad's rules, file); members start in their slots along the corridor; the leader gets the corridor order; the enemies section and its defaults; enemies placed, prone, watching their point, not moved; a shared world; exactly one of agent and squad; an invalid squad and an unknown weapon fail with a message; `WeaponProfile.MachineGun` = 0003's values |
| `St1AmbushTests` (Twin.Acceptance.Tests) | `scenarios/st1-ambush` places 9 + 3 on the st1 map; the squad halts on red within 5 min; SM10: 30 min of a squad on st1, wall-clock time printed (`./dev smoke`), the squad gets past x = 330 |

Changed with approval in the implement phase: the enemy watch test runs 5 s, not 20 s (by 20 s red-mg has spotted the
squad at the edge of its view and watches it, as S5a1 says), and the squad test expects `AgentId` = `rj`.
Found in review and tested in `ScenarioSquadValidationTests`: a scenario that cannot be placed fails when it is loaded
(a squad start, a member's starting slot or an enemy off the map, an id used twice across the squad and the enemies, a
corridor whose first segment has no length). Starting in impassable terrain is not checked (as for one agent).
Kit: `TestScenarioFolder` writes and loads a scenario directory with a flat open world; `ScenarioOnDisk.Load` follows
the scenario's `world` key. SM10 passes before the implementation (it measures what S2a–S2c already do).

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

#### S5a split (product owner, 2026-11-18)
S5a is delivered in two parts. **S5a1: spotting** (observation, watch sectors, the spot report, the squad halts),
below. **S5a2:** the rest of S5a (UF1, UF5–UF7, WS3: contact by fire up the chain, the enemy estimate, suppression
slows command, the drill conflicts), built on S5a1's reports.

#### Squad-wide forest-edge permit (decided 2026-11-19; specified and implemented)
Decided with the product owner after the st1 patrol demo, where the wedge's arms leaving the tree clumps along the road
asked for permission again and again: **once the squad has a permit to leave the forest, it covers every member's every
forest-edge crossing for the rest of that order, anywhere in the corridor.** Drawn boundaries (a road, for example) keep
S2b's rule (radius plus half-width around the requested point). A permit lasts one order, as before.

Tests (`SquadForestPermitTests`, spec 2026-11-19): in file and in wedge, a route that leaves the forest and then a strip
of trees 120 m on asks once, and the squad's forest-edge permit covers the second edge (fails before the change: the
leader asks again at the strip); a road permit still covers only radius plus half-width; the forest-edge permit lapses
with a new order (the last two pass already and guard against widening the change).

Two S2c tests encoded the old rule and were changed with the product owner's approval (2026-11-19): SM6
(`SquadEdgeDeploymentTests`) now expects the forest-edge permit to cover the whole edge (infinite radius) instead of
radius plus the line's half-width. `RouteAudit` still checks the members' routes against impassable cells and the
corridor limits, but given the squad's own infinite permit its forest-edge check can no longer fail. The clearing edge case
(`SquadEdgeDeploymentEdgeCaseTests`, now `After_a_clearing_on_the_way_the_first_permit_covers_the_far_edge`) expects
one deployment and one request, at the first edge: the far edge after the clearing is crossed on the same permit.

#### Forward and lateral forest-edge crossings (decided 2026-11-19; specified and implemented)
Refines the squad-wide permit above, decided with the product owner after review found that a permit a member obtained
did not reach the leader's own plan:
- **The squad's permit is its leader's.** A forest-edge permit the squad leader (or a successor that has taken command)
  holds, whether it asked itself or relayed a member's request, covers every member. Team leaders obtain no permits
  of their own. A leader that already plans to deploy at an edge takes a covering squad permit into account at once.
  Rationale: when the leader moves the squad over a field edge, the squad can take that crossing as authorized.
- ~~**Forward crossings only.** The permit covers crossings in the squad's direction of movement~~ (superseded in
  implementation, see below; the permit covers every direction, and the angle decides only where slots go): a move out
  of the forest is sideways when its direction is more than `45°` **[PROPOSAL]** from the corridor centreline's
  direction at that point.
- **Lateral crossings are not made.** A member that would leave the forest sideways because the formation is wider than
  the forest (moving north, crossing the west edge) does not cross and does not ask: it keeps inside the edge and goes
  round on the forest side, so the formation narrows there.

Tests (`SquadForestPermitEdgeCaseTests`, spec 2026-11-19), on bands of forest 40 m wide with bog (slower than forest)
on both sides, so the leader's own route keeps to the forest: without a permit, a wedge wider than the band neither asks
nor leaves it, and comes along to B (passes already: a member keeps inside an edge the squad has no permit for, and
guards that); with the squad's forest-edge permit from leaving an earlier forest forward, a wedge commanded inside the
band still does not leave it sideways (fails before the change: k1 walks out into the bog on the permit). Forward
crossings on the permit stay covered by `SquadForestPermitTests`.

The gap review found (a permit relayed for a member not reaching the leader's plan) cannot arise with the current
flows, so it has no test: members start to follow only once the leader moves; the leader asks for its drawn boundaries
from where it stands before it moves; a leader whose route leaves the forest heads for that edge from the start, and no
member asks to leave the forest meanwhile (S2c); and a new order or a new leader starts from the squad's permits. A
member's forest-edge permit is therefore only ever asked for while the leader's route has no forest edge of its own.

**How it is implemented (2026-11-19).** The rule acts on the formation, not on the permit: a member's slot that lies
outside the forest, sideways from its point on the leader's path (the move there leaves the forest more than
`SquadDoctrine.ForwardCrossingDegrees` = 45° from the corridor centreline's direction), is tucked in toward the path as
for an unpermitted boundary (S2b), with or without a permit, so the formation narrows to the forest. The permit itself
keeps covering every forest-edge crossing in any direction. A first version that made the permit forward-only
deadlocked the st1 acceptance runs: where a forest road runs along the route, members had to step across the road strip
sideways to reach slots inside the forest on its far side; the permit no longer covered that, there was no way round,
and the leader waited for them for good. Crossing to a slot inside the forest is not the "formation wider than the
forest" case, so it stays covered. ⚠ Moving the rule from the permit to the slots was the implementer's call, to be
confirmed by the product owner. Open, found in review: (a) a slot's offset is always square to the leader's path, so
while the leader keeps near the centreline the 45° value hardly matters (a slot just outside a slanted forest edge is
tucked in too, until its path point is out); (b) only slots are kept in the forest, not routes: on the permit a
member's or the leader's route may still cut out of the forest sideways and back where the land beside it is faster
(the tests put bog beside the bands); (c) a slot in water or a building is moved inward to the first standable point
by the older S2b code, which may lie outside the forest.

#### S5b spec decisions (2026-11-19; tests reviewed and implemented)
Decided with the product owner: before the squad leader distributes fire, members fire at the first enemy they see;
after it, into the fire area (tulialue) the leader commands. A fire area is a direction sector from each fighter, and
sectors can and should overlap. The leader decides on its own and commands through the chain of command; a member
watches its area and fires once it spots an enemy there; fire ends when the target is out of action or on a command.
⚠ **The sector split below is a first version and must be reworked properly later** (product owner, 2026-11-19).
- **Before the fire command (in contact).** A member that knows of an enemy (spotted or reported), or whose squad is
  halted on contact, fires at the first enemy it spots, as under free fire, unless it holds fire. (Return fire is raised
  to free fire in contact; a member under hold fire still holds fire.)
- **The leader distributes fire on its own.** When the leader learns of an enemy it did not know of, it commands the
  fire distribution after its reaction time (`DirectSquadFire`, a squad command: `CommandIssued`, delivered with S1's
  delays and S5a2's suppression factors). A later new enemy brings a new command. The same order can be given to the
  leader from outside; `SquadCeaseFire` to the leader ends the distribution.
- **The front.** The squad's fire area as the leader sees it: the bearings from the leader to the enemies it knows of,
  widened by `10°` **[PROPOSAL]** on each side (at least `20°` wide).
- **Sectors (first version).** The front is split into equal shares, one per team, left to right in the open line's
  team order (second-in-command's team, leader's team, other teams). Each share is widened by half its width (a quarter
  on each side), so neighbouring sectors overlap. A fighter's sector is its team's share as a direction sector from the
  fighter's own position. The machine gunner's sector is the whole front.
- **In its sector** a member watches the sector's centre and fires at the enemies it has spotted there, nearest first; it
  does not fire outside it, except back at its own attacker (0003's duty to return fire). An enemy known but not yet
  spotted is not fired at (direction fire is a later ticket) — **except by the machine gun:** it fires suppressive bursts
  at the known positions of enemies in its sector even if it has not spotted them, as snap fire (0003's snap hit model);
  its misses suppress as any rounds do.
- **Fire ends** at a target out of action (the member moves on to the next enemy in its sector, or stops and keeps
  watching), or on `SquadCeaseFire`: then members fire only back at their own attackers until a new distribution.
  0003's reserve applies to all fire without a `FireAt` order.

#### API the S5b tests need from `Twin.Sim`
- Orders `DirectSquadFire` and `SquadCeaseFire` (squad commands to the leader; logged as `CommandIssued` whose text
  starts with the order's name).
- `FireArea(Centre, WidthDegrees)` (`Twin.Sim.Squads`; a direction sector, `Covers(direction)`); `Squad.Front` (null
  before any distribution); `Unit.FireArea` (null when the member has none).
- `SquadDoctrine.FrontMarginDegrees` (10) and `SquadDoctrine.TeamShareWidening` (0.5).

#### S5b tests (`FireControlTests`)
| Test | Checks |
|---|---|
| before the command a member in contact fires at the first enemy it spots | a member fires at a red it spotted, before the fire command reaches it, though nobody fired at it |
| a squad holding fire does not fire in contact | no shots |
| the leader distributes fire its reaction time after it learns of an enemy | `DirectSquadFire` 1 s after rj learns of red |
| a new enemy brings a new fire command | a second red appears: a second command 1 s after rj learns of it |
| the front spans the known enemies widened by the margin | centred between the two reds, 2 × 21.8° + 2 × 10° wide |
| each team gets an overlapping share left to right | support left, command middle, point right; each a third of the front widened by half |
| the machine gunner gets the whole front | kk's fire area = the front |
| a member watches the centre of its fire area | watch direction = fire area centre |
| after the distribution a member fires only at enemies in its sector | point team only at red-right, support riflemen only at red-left, command team not at all |
| the machine gun fires suppressive bursts at a known enemy it has not spotted | kk (blind) fires snap bursts at red-left; t1 (blind) does not fire |
| a member whose sector holds no enemy in action stops firing | after red-right is out of action the point team fires no more |
| a squad cease fire ends the distribution and the fire | no more shots; no fire area |
| a fire distribution can be ordered from outside | `DirectSquadFire` to rj issues a new command |

Choices made while implementing: a leader that holds fire does not distribute fire on its own (its squad may not fire,
and fire areas would turn its members' watch; S5a1's spotting tests caught it); a member with a fire area watches it
before a just-learned enemy's position; a member's own attacker comes first, in or out of its fire area; the front's
centre is the mean of the bearings, so it never wraps round. Known limitations: fire areas are fixed when the command
reaches a member and do not follow it when the squad moves; the sector split is a first version (see above). Evidence
for the rework (st1 ambush, review 2026-11-19): with three red fighters within about 3° of each other as rj sees them,
the front is about 23° wide and each widened share about 12°; seen from the support team's own positions its share
misses the enemy (t1 spotted all three within 245 m but never fired), so only the middle team engages; blue lost
3 of 9 and red none, where before S5b it was 2 and 2. The machine gun fires only at known enemies it can see (not yet
spotted); suppressing one out of sight is direction fire, a later ticket. Known enemies out of action still widen the
front. A leader holding fire still distributes when ordered from outside; `DirectSquadFire` with no known enemy does
nothing.

Decided with the product owner in review (2026-11-19) and tested in `FireDistributionLifetimeTests`: a successor that
knows of enemies distributes fire its reaction time after it takes command (a distribution due when the leader falls
waits for it), and the squad moving on (`ResumeMovement`, or a new movement order to the leader) drops the fire areas at
once, without a cease fire, so members watch their observation sectors again. Also found in review and tested in
`FireControlEdgeCaseTests`: a member fires back at its own attacker outside its fire area and after a cease fire, and a
cease fire calls off a distribution already due. The machine gun picks known enemies by their known position.

The squad stands in a row facing north with weapons that never hit (except where a test says otherwise); two red
fighters hold fire 100 m north, one in the support team's share only and one in the point team's. Kit:
`Scenario.WithSquad(..., weapons)`, `SpecSquad.FrontMarginDegrees`, `TeamShareWidening`, `DirectSquadFireCommand`.

#### S5a2 spec decisions (2026-11-19; tests reviewed and implemented)
Decided with the product owner: contact by fire travels like a spot report; suppression slows commands 1.5 × / 3 ×; the
enemy estimate is built from what the RJ knows; the drill wins over a squad posture command.
- **Contact by fire reaches the RJ (UF1).** When a member reports contact in 0003's drill (`ContactReported`, after its
  reaction and snap shots), the report is spoken as in S5a1: squad mates within voice range (30 m) learn of the attacker
  and its position (`SpotReceived`), and if the RJ did not hear it, the member nearest the RJ among those who know
  repeats it after `t_relay`, until the RJ knows. When the RJ knows, the squad halts (S5a1).
- **Weapon class (WS3).** Every weapon has a class (`WeaponClass`: `Rifle`, `MachineGun`, `HeavyMachineGun`;
  `WeaponProfile.Class`). Whoever hears a shot learns the shooter's weapon class (`ShotHeard.WeaponClass`). A contact
  report about an attacker carries the class the reporter heard, and so does every relay of it. `Twin.Sim` gets
  `WeaponProfile.HeavyMachineGun` with backlog 0003's values.
- **The enemy estimate (UF7).** The RJ's picture of the enemy (`Squad.EnemyEstimate`): `Fighters`, the number of
  distinct enemies the RJ knows of (spotted itself, or from spot or contact reports), and `HeavyMachineGun`, true once
  the RJ has heard a heavy machine gun itself or received a report naming one. Logged as
  `EnemyEstimateChanged(squad, fighters, heavyMachineGun)` when it changes. A new RJ's estimate is what it knows. Enemies
  out of action are not taken off yet; a strong fortification, the second reinforcing factor, has no model yet.
- **Suppression slows command (UF5, UF6).** A member's reaction to a command is `1.5 ×` **[PROPOSAL, accepted]** its
  normal reaction time while it is suppressed and `3 ×` while it is heavily suppressed (0003's two levels), judged when
  the command reaches it (when it is given, or when its team leader passes it on). A team leader's relay time is
  multiplied the same way, by its own state when it acts. A successor takes command (or a team) after `1.5 ×` or `3 ×`
  `t_takeover`, by its state when the leader's place empties (or when it becomes the successor).
- **The drill wins over a squad posture command (the two S1 limitations).** A member in 0003's drill keeps the drill's
  posture; a squad posture command that reaches it meanwhile is remembered and taken when the drill ends (the drill
  does not restore its old posture over it). `SetSquadPosture` to the RJ, like `SetPosture`, does not end the RJ's own
  drill and keeps its current order.
- **The squad's movement is suspended under contact, not lost.** The RJ's corridor order survives its own drill: after
  the drill the squad is halted (S5a1) and `ResumeMovement` resumes the order in formation.

#### API the S5a2 tests need from `Twin.Sim`
- `WeaponClass` (`Rifle`, `MachineGun`, `HeavyMachineGun`), `WeaponProfile.Class` (default `Rifle`),
  `WeaponProfile.HeavyMachineGun`; `ShotHeard.WeaponClass`.
- `Squad.EnemyEstimate` (`Fighters`, `HeavyMachineGun`); event `EnemyEstimateChanged(Time, AgentId = RJ, SquadId,
  Fighters, HeavyMachineGun)`.
- A member reporting contact knows of its attacker from then on (`Simulation.KnowsOf`).

#### S5a2 tests
| Test | Checks |
|---|---|
| `WeaponClassTests` (pure) | the classes of rifle, machine gun and heavy machine gun; `WeaponProfile.HeavyMachineGun` = 0003's values |
| `ShotHearingTests` (WS3) | a listener's `ShotHeard` carries the weapon class (rifle, machine gun, heavy machine gun) |
| `ContactReportTests` (UF1) | mates within voice range of the contact reporter learn of the attacker at the report, those beyond do not; the report is passed on to a leader out of range and the squad halts; a leader shot at knows its attacker from its own report; the leader's movement order survives its drill and resumes after `ResumeMovement` |
| `EnemyEstimateTests` (UF7) | the estimate counts distinct known enemies (1, then 2); a heavy machine gun heard marks it; rifle fire does not; the event survives the log |
| `SuppressedCommandTests` (UF5, UF6) | heavily suppressed: acts after 3 × its reaction; suppressed: 1.5 ×; an unsuppressed neighbour: normal; a heavily suppressed team leader relays 3 × later; a heavily suppressed second-in-command takes command after 3 × `t_takeover` |
| `DrillAndSquadPostureTests` | a member in the drill keeps the drill's posture when a squad posture command reaches it; when the drill ends it takes the commanded posture; `SetSquadPosture` does not end the leader's own drill |

Changed with the product owner's approval in the implement phase (2026-11-19): three S1 tests (`ChainOfCommandTests`
SQ4 takeover and relay, `SuccessionTests` SQ5 command team) now expect the takeover or reaction time times the
member's suppression factor, because the fire at its leader 3 m away suppresses it (S5a2); and five S5a2 tests were
fixed: a friend stood in the shooter's fire sector in `ShotHearingTests` and UF6 (so nobody fired), the estimate may
grow 0 → 2 in one step, UF1 leaves out members that already knew of red from their own contact report, and the
movement test lets the squad form up before red fires. Choice made while implementing: a member alerted while in the
drill goes back, on `ResumeMovement`, to the posture it had before the drill (it was staying prone and crawling).

Found in review and fixed: a heavy machine gun on the squad's own side no longer marks its estimate, and the estimate
is empty while the leader's place is empty (`EnemyEstimateEdgeCaseTests` also covers a heavy machine gun named only in a
contact report). Scenario files accept `heavyMachineGun` as a weapon.

#### Known limitations of S5a2
- The estimate can read 0 fighters with a heavy machine gun: hearing it marks the gun but does not tell where it is.
- A relay that is dropped because the leader already knows of the enemy can drop the heavy machine gun it names.
- Enemies out of action are not taken off the estimate.
- `ResumeMovement` restores the posture from before the halt, overriding a squad posture command given during the halt
  (or during the drill).
- `SetSquadFormation` still ends the leader's own drill (only `SetSquadPosture` was decided).

In the contact and estimate tests blue cannot spot anyone (its spotting takes 1000 s) and red's weapons have no muzzle
flash, so contact reports and the sound of red's fire are the only way the squad learns of red. Kit: the machine guns of
`SpecSuppression` get their classes; `UnitAssertions.NotBeIn`.

#### S5a1 spec decisions (draft, 2026-11-18; values marked [PROPOSAL] are for review)
**Observation (spotting).** Seeing is no longer instant. An observer spots an enemy after watching it continuously
for the spotting time

`t_spot = t_base(experience) · (1 + d / d_ref) · A(angle) · (1 / SizeFactor(target posture)) / T`

- `t_base` (product owner, 2026-11-18): `Conscript` 1.5 s, `Regular` 1.25 s, `SpecialForces` 1 s; `d_ref` = 100 m
  **[PROPOSAL]**. A regular soldier spots a standing enemy in the open straight ahead at 100 m in 2.5 s, at 300 m in
  5 s.
- `A(angle)`, the angle between the observer's watch direction and the enemy (product owner): `1` within the focus
  cone (straight ahead), `3` elsewhere in the field of view, never outside it. Cone and field of view are data
  (`ObservationRules`) so night-vision devices and the like can change them later. Focus half-angle 15°, field of view
  half-angle 90° **[PROPOSAL]**.
- `SizeFactor` is the 0002 posture size (1 / 0.7 / 0.4): a crouching enemy takes 1.43 ×, a prone one 2.5 × as long.
- `T` is the 0002 forest transmission; below `VisibleTransmission` there is nothing to see (as now).
- Progress builds up each step as `dt / t_spot` under the current conditions and the enemy is spotted at 1. It
  restarts from zero when the enemy leaves the line of sight or the field of view.
- **A muzzle flash in the field of view** spots its shooter at once (0003: a target seen only by its flash gets snap
  shots). **Sounds are heard all round** (`ShotHeard`, unchanged); hearing alone spots nobody.
- Later tickets (product owner): *movement reveals* (a moving enemy is spotted sooner), darkness, night vision.

**Watch direction.** Every combatant has one. *Merged with S2c (product owner, 2026-11-18; replaces the sector table
first drafted here):* a squad member following its squad watches its S2c observation sectors (decision 20): the centre
of each sector is a watch direction, the first one its `WatchDirection`, and the best angle factor of them counts. So
the wedge's apex watches ahead, each arm its own side, and the ends of the arms also the rear; in the file the first
watches ahead, the others alternately left and right, and the last two also the rear. An agent outside a squad watches its direction of movement (the
last one when it stops; set in the scenario before it moves). An agent in 0003's drill watches its nearest attacker. An
agent that learns of an enemy (by spotting it or from a report) watches its last known position.

**Spot report.** A member that spots an enemy reports it after its command reaction delay (`t_react`: 1 s experienced,
3 s inexperienced, S1): `SpotReported(spotter, enemy, position)`. For now the report is spoken: every squad mate within
voice range (30 m **[PROPOSAL]**) learns of the enemy and its position at once (`SpotReceived`). Hand signals (silent,
need a line of sight) come in a later ticket (product owner); the report's method is a field so it can be added.
If the RJ did not hear it, the report is passed toward the RJ: of the members who know of the enemy, the one nearest
the RJ repeats it after `t_relay` (2 s), again and again until the RJ knows, so a broken chain is tried again (review
decision, 2026-11-18). A member reports an enemy it spotted once, and never one it already knew of; relays repeat it.

⚠ **Voice range is a placeholder** (product owner, 2026-11-18): one fixed range is not enough and must be modelled
better in a later ticket. How far a shout carries depends at least on the terrain and vegetation between (forest,
ground in the way), background noise (weapon fire nearby drowns it), wind, and how loud the speaker chooses to be
(a low voice to stay hidden versus a shout under fire). Keep the range behind one query so that model can replace it.

**The squad halts.** A member stops and crouches as soon as it knows of an enemy (it spotted it or heard of it). When
the RJ knows of one, the whole squad halts at once, without a command delay (product owner), and every member
crouches (product owner). The squad's movement order is suspended, not lost: `ResumeMovement` **[PROPOSAL: name]** to
the RJ resumes it in formation, and members take back their posture from before the halt.

**Fire.** A fighter fires aimed shots only at an enemy it has spotted itself; at one it has not spotted it can only
fire toward the direction (product owner). Direction fire (area fire at a heard or reported position) is a later
ticket, as in 0003, so in S5a1: free fire engages only spotted enemies, and an attacker seen only by its muzzle flash
gets snap shots as in 0003.

**0002 and 0003 tests.** They test firing, not spotting, so they keep instant all-round sight: `SpecShooting.Rules`
pins `ObservationRules.Off` (no spotting time, no field of view). New spotting tests use `SpecSpotting`. (Agreed by
the product owner with the S5a1 tests, 2026-11-18.)

#### S5a1 acceptance criteria (draft)
- SP1 A regular fighter spots a standing enemy in the open straight ahead at 100 m after `t_spot` (2.5 s), not before
  and at most one tick after; `EnemySpotted(observer, enemy, position)` is logged.
- SP2 The same enemy elsewhere in the field of view is spotted after 3 × as long; behind the observer it is never
  spotted, however long it stays.
- SP3 A crouching or prone enemy, more distance or forest between take as much longer as the formula says; a broken
  line of sight starts the time again.
- SP4 A conscript takes longer than a regular soldier, special forces less.
- SP5 Changed observation rules (a wider focus cone, as with night vision) change the spotting time.
- SP6 A muzzle flash in the field of view spots its shooter at once; a shot from behind is heard but spots no one.
- SP7 In a wedge an enemy on the right flank is spotted by a member of the right arm and not by the left arm; in a
  file, by the members watching right.
- SP8 The spotter stops and crouches at once and reports after its reaction delay; squad mates within 30 m learn of
  the enemy then, those farther away do not.
- SP9 An RJ out of the spotter's voice range learns of the enemy through the nearest hearer, `t_relay` later.
- SP10 When the RJ knows of an enemy the whole squad halts at once and every member crouches; `ResumeMovement`
  resumes the squad's movement order in formation.
- SP11 A free-fire fighter does not fire at an enemy in sight that it has not spotted, and fires once it has.
- SP12 Runs with the same seed give byte-identical logs; known enemies and the halt are rebuilt from the log.

#### S5a1 details settled while writing the tests (2026-11-18, for review)
- **Distance** in `t_spot` is from the observer's eye to the enemy's aim point, as in the hit model. Exactly on the
  field of view's edge counts as outside.
- **Turning an observer:** a new order `Watch(direction)` ("tähystä suuntaan"): the agent stops, as with `Stop`, and
  watches that direction. Tests need it to aim an observer; it is also an operator order. A zero direction is
  rejected. **[PROPOSAL]**
- **A settled squad** keeps watching its S2c sectors (relative to its last direction of travel).
- **The spotter stops and crouches when it spots**, before the RJ knows; so does any member when it hears a report.
- **The squad halts when the RJ learns of an enemy it did not know of,** so a known enemy does not halt it again.
  `ResumeMovement` to the RJ (rejected for anyone else) ends the halt, logs `SquadResumed`, and resumes the
  suspended movement order in formation; members take back the posture they had before the halt and learn of the
  enemies the RJ knows (so seeing them again halts no one). **[PROPOSAL]**
- **A known enemy's position** is the position spotted or reported; S5a1 does not track it after that.
- **0002 and 0003 tests** keep `ObservationRules.Off` through `SpecShooting.Rules`: a fighter knows every enemy it can
  see, all round, and nothing is spotted, reported or halted. Checked: with the new API stubbed, all 366 existing tests
  still pass.
- **Layouts:** the squad spotting tests use a 12 m interval so no squad mate stands exactly 30 m from another (with
  10 m the wedge's apex is exactly 30 m from its third-rank corners). The hidden enemy lies prone behind a 0.6 m
  mound and stands up when the test wants it seen.
- **A `FireAt` order's target must be spotted first** (implemented so, 2026-11-18): the rule "aimed fire only at an
  enemy it has spotted itself" covers ordered fire too. The tests do not depend on it (the ordered shooter is turned to
  its target first); revisit in S5b (fire control) if ordered fire should work differently.
- **Watching a known enemy ends when the agent moves on** (implemented, 2026-11-18): an agent that learns of an enemy
  watches its last known position while it stands still; moving again turns it back to its direction of movement or
  its sector.
- **Limitation:** a halted corridor order is planned again on `ResumeMovement`, so permits already granted are lost.

#### Merge with S2b and S2c (product owner, 2026-11-18)
- Spotting uses the S2c observation sectors (decision 20) as the squad members' watch directions; the sector table
  first drafted for S5a1 (`WatchSectors`) is gone. Each sector's centre is a watch direction and the best angle factor
  of them counts, so the members at the back also spot an enemy behind the squad.
- The squad spotting tests were laid out again for S2c's wedge (apex j1; left arm t1, vj, kk, rj; right arm j2, pj, k1,
  k2) and its file sectors, and checked by the product owner in a spec phase.
- A new movement order to the RJ ends a halt (S5a1) and is then briefed to the squad (S2b).

#### S5a1 review decisions (product owner, 2026-11-18)
- **A report that does not reach the RJ is repeated** every `t_relay` by the member nearest the RJ of those who know of
  the enemy, until the RJ knows (a relay out of action, nobody nearer the RJ, or the RJ's place empty during a
  takeover).
- **The squad halts whenever its leader knows of an enemy it has not halted for**, also when a successor who already
  knew of it takes command.
- **A new movement order to the RJ during a halt ends the halt** like `ResumeMovement`, with the new order instead of
  the suspended one; the suspended order is dropped.
- **Moving on keeps the movement posture:** the squad moves on in the posture it moved in before the halt (standing, or
  crawling if it was ordered prone on the move), whatever posture was commanded during the halt.
- **A squad leader's `Watch`** holds while it has no movement order, instead of turning back to its sector next step.
- **The combat demo** turns each side toward the other with `Watch`.

#### Known limitations of S5a1 (inputs to the next parts)
- ~~A squad leader in 0003's drill keeps its movement order in the drill, so a halt suspends nothing and
  `ResumeMovement` (which ends the drill) loses it.~~ *Resolved in S5a2: the halt keeps the order the drill dropped.*
- A detached member (one with its own order) that learns of an enemy crouches but carries on with its order.
- `Spotted` is permanent: an enemy that hides and reappears is fired at again at once. Losing track of an enemy, and
  movement revealing it, are later tickets.
- A spotter that nobody hears repeats its report every 2 s for as long as the RJ does not know.
- Spotting checks every fighter against every enemy each step (a sight line each while in the field of view); not
  measured on st1 with many agents.

#### API the S5a1 tests need from `Twin.Sim`
- `Twin.Sim.Combat`: record `ObservationRules(ConscriptSeconds, RegularSeconds, SpecialForcesSeconds,
  ReferenceDistanceMeters, FocusHalfAngleDegrees, FieldOfViewHalfAngleDegrees, FieldOfViewFactor)` with
  `static ObservationRules Off`; `CombatRules.Observation { get; init; }`.
- `Twin.Sim.Squads`: record `WatchSectors(IReadOnlyList<double> FileDegrees, IReadOnlyList<double> WedgeDegrees)`
  (degrees to the right of the direction of travel, in formation order); `SquadDoctrine.WatchSectors` and
  `SquadDoctrine.VoiceRangeMeters` (`init`); enum `ReportMethod { Voice }`; `Squad.IsHalted`.
- `Unit.WatchDirection` (`Vec2`); `Simulation.KnowsOf(agentId, enemyId)`.
- orders `Watch(Vec2 Direction)` and `ResumeMovement`.
- events `EnemySpotted(Time, AgentId, EnemyId, EnemyPosition)`, `SpotReported(Time, AgentId, EnemyId, EnemyPosition,
  Method)` (the spotter's report and every relay), `SpotReceived(Time, AgentId, ReporterId, EnemyId, EnemyPosition)`,
  `SquadHalted(Time, AgentId = leader, SquadId, EnemyId)`, `SquadResumed(Time, AgentId = leader, SquadId)`; all in
  `EventLog`'s JSON lines.
- snapshots: `SquadSnapshot.Halted`; `WorldSnapshot.KnownEnemies`, a list of `KnownEnemySnapshot(AgentId, EnemyId,
  Position)` ordered by agent and enemy, rebuilt by `EventReplay`.

Kit additions: `SpecSpotting` (the values, the formula `SpotSeconds`, `Turned`), `ObservationRules.Off`
in `SpecShooting.Rules`, `Scenario.ForSpotting` / `ForSquadSpotting`, `SpottingAssertions` (`RunUntilSpotted`,
`ShouldHaveSpottedAfter`, `ReportsOf`, `ReceivedFrom`, …) and `UnitAssertions.BeWatching`.

#### S5a1 tests
| Criterion | Tests |
|---|---|
| SP1–SP6 | `Combat/SpottingTests.cs` (also `Watch` and the watch direction of an agent outside a squad) |
| SP7 | `Squad/WatchSectorTests.cs` |
| SP8, SP9 | `Squad/SpotReportTests.cs`; SP8's moving spotter in `Squad/SquadHaltTests.cs` |
| SP10 | `Squad/SquadHaltTests.cs` |
| SP11 | `Combat/SpottedEnemyFireTests.cs` |
| SP12 | `Squad/SpottingDeterminismTests.cs` |

Shared layouts: `Squad/SpottingLayouts.cs`.

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
16. **Delivery:** the ambush slice (S2a–S2d, S5a–S5b, casualty care (CC, added by decision 18), S6a–S6b, then the
    ambush acceptance test); file and wedge in S2, the other formations in S3 after the slice; S2, S5 and S6 in full.
17. **S2a:** 10 m interval in file and wedge, provisional (spacing must become terrain-dependent); compression
    allowed with lateral gaps of at least 1 m for now; one squad wedge with the point team leader at the apex (changed by
    decision 20); a member
    ordered directly (by any order) is detached until `Rejoin`; compression and unusable slots (FO4, FO5) in S2a.
18. **Nobody is left behind:** a squad does not move on normally under fire; a pinned member halts the squad, at least
    its team stays with it. Breaking contact is an option, and the squad's return fire should then free the pinned
    fighter to move (S6b). The dead are left; the wounded are carried or helped, and casualties are reported to the
    platoon leader. Casualty care must be implemented (part CC).
19. **S2b (2026-11-18):** a slot across a boundary to the side tucks in (a double file if several do); a member whose way
    forward crosses a boundary asks the RJ, which asks the platoon leader; the RJ briefs its movement order to the squad,
    and a successor that did not hear it asks the platoon leader to repeat it. Forest-edge deployment moved to S2c.
20. **S2c (2026-11-18):** the open line keeps the teams side by side, left to right t1, vj, kk, rj, j1, j2, pj, k1, k2,
    with the RJ at its point; the wedge is the same order folded back at the middle (replacing S2a's wedge); members that do not fit form
    a second line (provisional: compression is also possible and may replace it); only a squad deploys, a lone agent
    asks as in v0.1. Members get a heading and 360° observation: the point observes ahead, the sides their own side, the
    last ones also the rear and the air; sectors are data until the observation model (in development) arrives.
21. **Spotting (S5a1, delivered before the rest of S5a):** spotting takes time (distance, forest, target posture,
    experience); a watch direction with a focus cone (×1), the rest of the field of view (×3) and nothing behind,
    adjustable for night vision; movement reveals and darkness later. A spotter reports to the RJ and to squad mates
    near it; spoken for now, hand signals later. When the RJ knows of an enemy the whole squad halts at once and
    crouches. Without spotting an enemy a fighter can only fire toward its direction (direction fire is a later
    ticket). Watch sectors follow the member's place in the formation.

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
