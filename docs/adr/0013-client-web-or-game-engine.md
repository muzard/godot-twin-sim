# 0013. Client: a web application, a game engine, or both

- Status: Proposed (draft for the team to discuss; nothing is decided yet)
- Date: 2026-11-19
- Revisits: the client row of [0007](0007-tech-stack.md)

## Context
ADR 0007 picked Godot for the client because of its "strong 2D and UI". ADR 0007 also said: "if a browser
build becomes a requirement, revisit this ADR". A web client with React has since been suggested. This ADR
compares the two and spells out what a game engine gives us that a web page does not, so the team can choose
on purpose.

**What the Godot client does today.** About 900 lines in `src/Twin.Client`:
- `MapView` renders the terrain once into a shaded relief image and shows it as a sprite.
- `ReplayLayer._Draw()` draws over it on every frame, in immediate mode: lines, dashed lines, polylines,
  circles, sector polygons and text for the corridor, cliffs, routes, trails, shots, spotting lines, known
  enemies and agents. That is a few dozen agents and a few hundred draw calls.
- `ReplayHost` adds a camera (zoom and pan), a timeline slider, labels and keyboard playback.
- `SimulationHost` runs a simulation in the same process and turns mouse clicks into orders.

None of this needs a game engine: the HTML canvas has the same primitives (`lineTo`, `arc`, `fill`,
`setLineDash`, `fillText`). What the client uses that is really Godot's own is narrow: one process and one
language with the simulation. The client calls `Twin.Sim` types directly, and the replay rebuilds state with
`EventReplay` from `Twin.Sim`, with no serialisation in between.

**Engine features we may not use.** By design, the engine never decides anything about the world:
- physics, navigation and raycasts stay out of the simulation (ADR 0002, 0005);
- the client "contains no game rules" (ADR 0002, enforced for `Twin.Client` by `tests/Twin.Architecture.Tests`).

So the question is which presentation features we need, not whether the engine can run the simulation.

### Engine features help presentation, not the simulation
Every engine benefit below concerns showing results: instancing, shaders, 3D terrain, effects. None of them makes
the simulation faster or its rules better, in Godot or in a browser.

A viewshed shader, for example, paints what the simulation has already computed (who sees what). The GPU could
compute sight lines itself, and much faster, but its results must never feed back into decisions:
- **Determinism (ADR 0005).** GPU floating-point results can differ between devices and drivers. The same
  scenario could end differently on two machines, and replays and tests would stop holding.
- **Separation (ADR 0002).** Rules computed in the engine would need the engine to run tests, servers and batch
  analysis.

If line of sight or search becomes a bottleneck, the remedy lies in the simulation itself, independent of any
engine:
- better algorithms: precomputed viewsheds, horizon maps, caches, spatial indices;
- faster code in the simulation's language;
- GPU compute through a library outside the engine, if ever, which needs its own decision because of
  determinism.

What an engine does speed up is our *work on* the simulation. When the scored candidates of a search, a viewshed,
or a hundred runs' trails are visible on the map, it is much faster to see why a rule chose what it chose and to
calibrate it than by reading the event log.

### "We would be writing our own engine"
This objection comes up against the web option, and it is fair in part.

**The simulation is not a game engine, in any option.**
- These are rules no engine ships with:
  - corridors and boundaries, forest-edge permits;
  - squad formations and leadership;
  - spotting, fire distribution and suppression;
  - firing positions (0005).
- We write them ourselves wherever they live. Moved into Godot, they would sit in Godot nodes instead of a plain
  library.
- No engine choice removes this work.

**A 2D web client is not an engine either.**
- It uses map and rendering libraries (MapLibre, deck.gl, three.js) the way the Godot client uses Godot.
- The current Godot client, the thing a web client would replace, is about 900 lines.

**Where the objection holds.**
- A 3D terrain view, effects, positional audio, VR or a training client built on the web would mean assembling
  much of what an engine already integrates: cameras, level of detail, instancing, picking, audio, input.
- That is why the draft decision sends exactly those uses to Godot.

### Moving the simulation into the engine
Could we gain by moving the simulation into Godot, as ADR 0002 rejected? What it would replace is small:

| Godot feature | Would replace | But |
|---|---|---|
| A* (`AStarGrid2D`) | Part of our path search | Corridors, permits, cliffs and slope limits stay our own logic |
| Navigation meshes and avoidance | Little | Determinism not guaranteed; avoidance runs in parallel |
| Physics raycasts | The basic sight-line test | Hit or miss only: vegetation, eye heights per pose and partial cover stay ours |
| Rigid-body physics | Nothing we need | Infantry needs no rigid bodies, and results are not guaranteed identical across platforms |
| Game loop, timing, scene tree | A few lines of `Simulation` | — |

What it would cost is what makes the simulation valuable:
- **Fast tests.** The tests now run in seconds with no Godot install. Inside the engine they would need the Godot
  runtime.
- **Determinism (ADR 0005).** Replays and the final state hash depend on it.
- **Cheap batch runs.** Many seeds would run through headless Godot processes instead of a plain library.
- **Freedom to choose the simulation's language.**

**The one case where an engine-style library would help the simulation.**
- If the world model ever goes beyond 2.5D (ADR 0004), sight and movement against real 3D geometry become hard:
  building interiors, storeys, bridges.
- Ready-made collision and ray queries would then be worth having.
- Even then the right move is a standalone library used from the simulation (for example Jolt directly), not the
  engine, so determinism and testability stay.
- That would be its own ADR, superseding parts of ADR 0004.

### What a game engine gives (Godot here)
Benefits that matter for a digital twin of an operating environment, roughly by how likely we are to need them:

1. **3D terrain from the 2.5D world.**
   - The world model (ADR 0004) is already a height field: a mesh with level of detail, lighting and a free
     camera is built in.
   - Users would see rises, dead ground, cliffs and forest edges as a fighter does. This makes "why did the
     squad go there?" much easier to judge than contour lines.
   - The web can do it with three.js, but terrain level of detail, picking and cameras are ours to build.
2. **The GPU through shaders.**
   - Whole-map overlays that the simulation computes or samples can be drawn per pixel in real time: viewsheds
     (what an enemy at a point sees), dead ground, fire sectors, cover from a direction, heat maps of
     exposure, and fog of war per side.
   - The simulation stays the source of truth; the shader only paints it.
   - The web has WebGL and WebGPU, but the tooling (shader editor, material system, hot reload) is weaker.
3. **Many objects at a steady frame rate (GPU instancing, `MultiMesh`).** See "Instancing in our use case"
   below for when this matters to us and when it does not.
4. **Smooth motion between simulation ticks.**
   - Interpolation, tweens, `AnimationPlayer`, and per-unit poses (standing, crouching, prone, crawling) are
     everyday engine work.
   - That matters once the view should feel continuous rather than show 20 Hz snapshots.
5. **Effects and sound for training use.**
   - Particles for muzzle flash, impacts, smoke and suppression.
   - Positional audio: hearing where fire comes from is part of what the simulation models (`ShotHeard`).
   - This matters if the twin becomes an exercise or training tool, not only an analysis tool.
6. **Native, offline applications.**
   - Godot exports to Windows, Linux, macOS and Android as one self-contained program.
   - That suits air-gapped networks and field laptops with no server or browser policy to negotiate.
7. **Input beyond mouse and keyboard.** Gamepads, touch, pen, and VR/AR through OpenXR: a commander's view in a
   headset, or a fighter's view at eye height.
8. **The editor as a tool platform.**
   - Editor plugins and `[Tool]` scripts can become a scenario editor: place units, draw corridors and
     boundaries, set rules of engagement, preview the terrain.
   - The output is still the scenario files the simulation reads.
9. **One language and shared types, in process.**
   - The client links `Twin.Sim`, calls queries such as `FindBestFiringPosition` and reuses `EventReplay`.
   - There is no API to design or keep in sync, and live mode needs no server.
   - This is the benefit we use today. It disappears if the simulation moves to another language.
10. **Networked multi-user sessions.** Godot's high-level networking can connect several trainees' clients to
    one authoritative simulation. A web client would do the same over WebSockets.

### Instancing in our use case (`MultiMesh`)
**What it is.**
- A `MultiMesh` (`MultiMeshInstance2D` or `3D`) draws one mesh many times in a single GPU draw call. Each copy
  has its own transform, colour and a few floats of custom data.
- Drawing 10,000 tree symbols costs about as much CPU as drawing one. The CPU only fills one buffer, and only
  for the instances that changed.
- The alternative is drawing shape by shape, as `_Draw()` and Canvas 2D do. There the CPU issues commands for
  every shape on every frame, and that is what limits frame rate as counts grow.

**When it gives us nothing.** For units at the scales we model, the agent count alone does not justify it:

| Scale | Agents (order of magnitude) |
|---|---|
| Squad (today's scenarios) | 10 |
| Platoon | 40 |
| Company | 150 |
| Battalion | 1,000 |

Even a battalion is within what immediate-mode drawing handles, in Godot or on a web canvas. Instancing is not a
reason to choose an engine for drawing units.

**Where it does pay off: many discrete objects that are not agents.**
1. **Trees and vegetation in a 3D view.**
   - The forest edge is a hard boundary (ADR 0009), and concealment in forest scores firing positions (0005).
   - Showing the forest as trees rather than a green fill makes both visible.
   - One hectare holds hundreds of trees and a map holds square kilometres of forest: hundreds of thousands of
     instances. This is the main case, and it only arises with 3D.
2. **Inspecting the search.**
   - The cover search (0003) and the firing-position search (0005) score thousands of 2 m grid points per
     decision.
   - We can draw every candidate as a marker coloured by score, with the chosen one highlighted, and step
     through decisions in the replay.
   - That is a debugging and review tool for the rules, and calibrating the 0005 weights needs it.
3. **Many runs at once.**
   - Batch analysis replays one scenario with many seeds.
   - Overlaying 100 runs × 10 agents × hundreds of trail points gives hundreds of thousands of segments, and
     shows where squads actually tend to go and get hit.
4. **Tracers, impacts and suppression effects** in a fire fight, which become numerous and short-lived at
   platoon scale and above.

**Not for continuous fields.** A value per map cell (viewshed, dead ground, cover from a direction) does not
belong in instances. At 2 m cells that is 250,000 values per km². It is drawn as a texture with a shader
(benefit 2), which is cheaper still.

**It is not unique to engines.** The web has GPU instancing too: three.js `InstancedMesh`, deck.gl layers,
PixiJS particle containers. deck.gl is built for exactly the "many runs, many points" case. The difference is
how much we build:
- In Godot, instancing, cameras, picking and level of detail work together out of the box.
- On the web, it means leaving Canvas 2D for a WebGL library, a second rendering stack beside the React UI.

The spike in the decision should measure where a canvas map stops being smooth with our data (trails of many
runs, search candidates), rather than rely on general figures.

### What a web client (React) gives
1. **UI-heavy screens are its home ground.**
   - Tables, forms, filters, event lists, timelines, charts, reports, and side-by-side comparisons of runs.
   - Most of an analysis or command-and-control tool is this kind of screen, and in Godot every one of them is
     hand-built from `Control` nodes.
2. **Zero install and a shareable link.** Anyone with a browser can open a replay or a scenario. Reviews,
   demos and teaching get easier.
3. **Map libraries.**
   - MapLibre, OpenLayers and deck.gl bring projections, tiled raster and vector maps, scale bars and
     measuring tools.
   - The GDAL toolbox (`./dev prep`) can produce the tiles.
   - In Godot these are ours to write.
4. **Testing and tooling.** Playwright for end-to-end tests, component tests, hot reload, and a large hiring
   and skills pool.
5. **Independent of the simulation's language.** A web client talks to the simulation through data. It keeps
   working whatever the simulation is written in.

The map itself is no obstacle. React should own the UI, while the map is drawn imperatively into one
`<canvas>` per frame from the replay data, as `_Draw()` does now. Putting hundreds of SVG elements in React
state is the usual way such a page becomes slow.

### What each costs
| | Godot | Web (React) |
|---|---|---|
| UI-heavy screens | Slow to build, weak widgets | Strong |
| 3D terrain, shaders, effects | Built in | Possible (three.js, WebGL); more of our own code |
| Very many objects (instancing) | `MultiMesh`, built in | WebGL library (deck.gl, three.js, PixiJS) beside the canvas |
| Browser delivery | C# web export is limited (ADR 0007); check Godot 4.7 before relying on it | Native |
| Offline / air-gapped | Native executable | Needs a local server, or a packaged app (Electron/Tauri) |
| Link to the simulation | In-process C# today | Needs a data boundary (below) |
| Automated UI tests | Hard | Mature |
| Rule enforcement | Architecture tests exist | A new TypeScript code base, not covered by them |

**The data boundary a web client needs.** A web client cannot call `Twin.Sim` or reuse `EventReplay`. It
needs:
- **The event log.** Already JSON lines (`EventLog`), so replay data is ready.
- **Snapshots of state at a time** (agents, poses, known enemies, squad state). Either `EventReplay` runs on a
  .NET server and sends them, or `Twin.Sim` is compiled to WebAssembly and runs in the browser. The third way,
  rewriting the reconstruction in TypeScript, duplicates logic and drifts.
- **Terrain and scenario data:** heights, terrain classes, corridors, boundaries, as image tiles or arrays.
- **For live mode:** orders in and state out over WebSocket/SignalR from a .NET host.

The boundary is worth having even if we keep Godot. It makes the simulation usable by any client and by
batch analysis, and it keeps the client choice reversible.

## Decision
Proposed, for the team to confirm or change:

1. **Define a client-neutral data contract.** It covers the event log (exists), state snapshots, terrain and
   scenario export, and later live orders and state. Any client reads only this contract, plus the
   `Twin.Sim` library for the in-process Godot client. Neither kind of client re-implements a rule.
2. **Choose the main client by its main use, with these questions:**
   - Who are the main users in the next 6–12 months: analysts and planners reviewing runs, or trainees in an
     exercise?
   - Must it open in a browser, or must it run offline on closed networks?
   - Do we need a 3D terrain view, viewsheds or effects within that time?
   - What can the team build and maintain fastest?
3. **Draft recommendation, if the answers are "analysts, browser, 2D, team knows web":**
   - Make a **web client** (React for UI, canvas or WebGL for the map) the main client for replay and
     analysis.
   - Keep the Godot client working, but do not extend it.
   - Turn to **Godot** (or another engine) for the uses an engine is for: a 3D terrain view, a training or
     exercise client, VR, an offline field build.
   - Start that only when a ticket needs it.
4. **Before deciding, run a time-boxed spike (about a week):**
   - Build the st1 replay viewer in React and canvas: map, overlays, timeline and event list.
   - Feed it from the existing JSON-lines log and a snapshot endpoint.
   - Compare it with the Godot viewer on effort, smoothness, and how the UI screens felt to build.
   - Load test it with our own heavy data: trails of 100 seeded runs, and the scored candidates of the
     firing-position search. Note where Canvas 2D stops being smooth and a WebGL layer is needed.

## Alternatives considered
- **Godot only (status quo).**
  - Keeps one language and in-process access.
  - Pays for every UI screen by hand, and gives no browser access.
  - The right choice if the twin is mainly a training or 3D tool.
- **Web only, drop Godot.**
  - Simplest stack for a 2D analysis tool.
  - Pushes 3D, effects and VR onto three.js and our own code if they become needed.
  - Nothing in the simulation would block adding an engine client later, so this is less final than it looks.
- **Godot web export.** One client everywhere. ADR 0007 already notes that C# web export is limited; it needs
  checking against the Godot version in use before it can be an option.
- **Godot client with an embedded web view** for the UI screens. Mixes two UI stacks in one program; brittle
  and rarely worth it.
- **Another engine** (Unity, Unreal, Bevy). ADR 0007's reasons still hold: licence, weight, editor maturity.
- **Move the simulation into the engine.**
  - Rejected in ADR 0002 and still not worth it.
  - The engine would replace little of our code: basic A*, a sight-line raycast, the game loop.
  - We would lose fast engine-free tests, determinism, cheap batch runs and the choice of the simulation's language.
  - See "Moving the simulation into the engine" above.
- **Write our own engine.**
  - Nobody proposes it.
  - The simulation is domain rules, not an engine.
  - A 2D web client is built on libraries.
  - Only a 3D, effects or training client on the web would drift towards one, and the draft sends those uses to
    Godot.

## Consequences
- **If a web client is added:**
  - A .NET host (or a WebAssembly build of `Twin.Sim`) and a written snapshot contract become part of the
    product.
  - `AGENTS.md`, `./dev` and CI need a JavaScript toolchain.
  - The rule "no game rules in the client" must be enforced for the new code base by review and by keeping it
    on the data contract, because `tests/Twin.Architecture.Tests` only sees .NET assemblies.
- **If Godot stays the main client:**
  - Browser access and fast UI work are what we give up.
  - ADR 0007's web-export caveat stays open.
- **Either way:**
  - A data contract between the simulation and its clients decouples them, and it is also the seam a change
    of simulation language would need.
  - ADR 0007's client row is updated once this ADR is accepted.
