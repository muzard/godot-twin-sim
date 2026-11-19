# 0014. Simulation in C#; port proven hotspots to Rust

- Status: Accepted
- Date: 2026-11-19
- Confirms the language row of [0007](0007-tech-stack.md) after a review of the stack

## Context
ADR 0007 chose C# for the simulation (`src/Twin.Sim`) and the client. In November 2026 the team reviewed the
choice as if starting from scratch. Python and Rust were the main alternatives: the team knows both, and Python
is what most of the field's tooling and analysis is written in.

The simulation's load is many agents doing geometry every tick:
- line of sight over a height field;
- path search on a grid;
- movement with slope and terrain checks;
- searches over 2 m grids (backlog 0003 and 0005).

The largest case we plan for is about 1,000 agents a side. Every agent scans every enemy within 600 m at each
0.5 s tick.

**Measurements (2026-11-16 and 17).**
- Hardware: an Apple M4 Pro with 14 cores, on the st1 2 km map.
- Line of sight: C# ran the unchanged `LineOfSight.Between` from `Twin.Sim`. Python and Rust ran ports that gave
  identical results.
- The figures are a snapshot: re-measure before relying on them for a new kernel.

| Kernel | Rust | C# | numpy | Plain Python | Python objects |
|---|---|---|---|---|---|
| Line of sight, relative to single-threaded Rust | 1× | 1.9× | 13.5–22× | 111–116× | 265–275× |
| A* over 428,000 cells | 80 ms | 116 ms | — | — | 2.8 s |
| Movement, per agent and tick | 4.0 ns | 6.1 ns | 27 ns | — | 1.1 µs |
| Largest case, line of sight per tick, 1 thread | 199 ms | 381 ms | 3.6 s | — | 52 s |
| Largest case, line of sight per tick, 14 threads | 19.6 ms | 40 ms (`Parallel.For`) | 1.0 s (threads) | 1.9–2.4 s (process pool) | 5.2 s (process pool) |

What the table says:
- At 0.5 s per tick, the largest case runs faster than real time in C# even on one thread, and many times
  faster with a deterministic parallel phase (see Decision).
- Rust is about 1.5–2× faster than C#. That is worth having in a hot loop, but not decisive.
- Python is too slow for the simulation loop. Plain Python and Python objects are two orders of magnitude
  behind. numpy helps only where work can be vectorised, which agent-by-agent decisions mostly cannot. Even
  then it stays an order of magnitude behind.
- Rust called from Python through PyO3 costs little per call. But logic left in Python stays slow: 638 ns per
  agent and tick with the height lookup in Rust, against 6 ns in C#.

**Beyond speed:**
- **Rust's borrow checker** makes a simulation with much shared, mutable, cross-referencing state slower to
  write and change. Units refer to squads, squads to leaders and teams, orders travel through a chain of
  command. Our rules change weekly under TDD, so speed of change matters more than the last 2× of speed.
- **C#'s model is close to Scala's.** It has garbage collection, records, pattern matching, expression-bodied
  code and LINQ over collections. That is a shorter step for people who know Scala or a JVM language than
  Rust's ownership model.
- **The work done.** `Twin.Sim` is about 10,000 lines, specified by about 13,000 lines of tests (577 tests). A
  rewrite would be months spent getting back to where we are.

## Decision
1. **The simulation stays in C#** (.NET, as pinned by ADR 0007). Rules, state, orders, systems and the event log
   are C#.
2. **Speed problems are solved in this order:**
   1. **Measure first.** Profile, and find the kernel that dominates the tick. Nothing is ported on a hunch.
   2. **Better algorithms.** Spatial indices for "enemies within 600 m", caches and precomputed viewsheds,
      cheaper early exits.
   3. **Faster C#.** Arrays of structs, `Span<T>`, no allocation in hot loops, `System.Numerics` SIMD.
   4. **A deterministic parallel phase.** Read-only queries (line of sight, searches) run in parallel. Their
      results are merged in id order before any state changes. ADR 0005 asked for such a deliberate design
      before any parallelism.
   5. **Only then, port the kernel to Rust.**
3. **When a kernel moves to Rust:**
   - **Pure function.** It takes inputs and returns results: no simulation state, no callbacks into C#. The
     simulation keeps owning all state.
   - **Batched calls.** One call per tick or per batch, not one per agent or per pair, so the interop cost
     stays negligible.
   - **C ABI.** Exposed as a C ABI from a `cdylib` and called with P/Invoke from `Twin.Sim`. No engine
     dependency, so ADR 0002 holds.
   - **Matches the C# reference.** The C# kernel stays as the reference. Tests check that the Rust kernel gives
     bit-identical results on recorded inputs, and the determinism tests (ADR 0005) run with it.
     Transcendental functions (`Math.Sin`, `Math.Atan2`) can differ between .NET and Rust's libm, so a kernel
     that needs them uses the same algorithm on both sides. Otherwise its change of results is a deliberate,
     reviewed decision.
   - **Dependency policy.** ADR 0006 extends to Rust: crates are pinned in `Cargo.lock`, a version must have
     been public for 14 days, each upgrade is its own pull request, and warnings are errors.
   - **CI.** CI builds the native library on Linux and Windows (and developers on macOS). `./dev setup`
     installs the Rust toolchain only once the first kernel exists.
4. **Python** is used outside the simulation loop:
   - the offline map pipeline (`prep/`, GDAL);
   - analysis of event logs and batch results;
   - notebooks.

   It reads what the simulation writes, the event log and scenario files. It does not call into the simulation
   per tick.

## Alternatives considered
- **The whole simulation in Rust.**
  - About 1.5–2× faster, and the team knows Rust.
  - Rejected for the cost of change: the borrow checker against cross-referencing mutable state, slower
    iteration on rules that change every week, and a rewrite of 10,000 lines.
  - Porting single kernels keeps most of the speed where it matters.
- **Python, with numpy or Rust kernels through PyO3.**
  - The team knows it, and the analysis tooling is excellent.
  - The decision logic would stay in Python, two orders of magnitude slower than C#.
  - Kernels in Rust do not save the per-agent logic around them (638 ns against 6 ns per agent and tick).
  - Too slow for the largest case, hopelessly so for batch runs of many seeds.
- **Free-threaded Python (3.15t).**
  - Measured: naive code got 2.5–3.5× slower on 14 threads, because of shared module-level constants in the
    hot loop.
  - Hand-tuned code scaled about 7×, which still leaves it far behind single-threaded C#.
- **Scala or another JVM language.**
  - Speed similar to C#, and close to what part of the team knows.
  - We would lose the in-process link to the Godot client (ADR 0007) and the work already done in C#.
- **C++ through GDExtension for hotspots** (ADR 0007's earlier escape hatch). It ties the kernel to the engine
  and to the client. A Rust library called from `Twin.Sim` serves the headless runner, the tests and any client
  alike.

## Consequences
- **Day to day:** the simulation stays one language, with the existing tests, tools and Godot client. Changing
  rules stays cheap.
- **Speed:** there is a defined path to more of it, and Rust enters the code base only for a measured reason,
  one kernel at a time.
- **When the first Rust kernel lands:**
  - The build gains a second toolchain, a native library per platform, and interop tests.
  - The C# reference implementation of each ported kernel must be kept, and kept in step.
- **The deterministic parallel phase** (Decision 2.4) is the next lever and needs its own design. It is not
  allowed by this ADR alone: ADR 0005's rules still apply until that design is accepted.
- **Python stays at the edges.** A Python API to the simulation, for batch control of many seeded runs, would
  be its own decision.
