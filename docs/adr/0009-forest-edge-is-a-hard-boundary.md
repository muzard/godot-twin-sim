# 0009. Leaving forest always crosses a hard boundary

- Status: Accepted
- Date: 2026-11-08

## Context
Spec v0.1 (Smoke test I) says a corridor has hard boundaries, for example the edge between field and forest, and
that crossing one needs the leader's permission. It left open whether the forest edge is hard only along the
corridor's edge or at every point where a route moves from forest to open ground, including inside the corridor.

## Decision
Every forest edge is a hard boundary, everywhere, when an agent **leaves** the forest: any move from a forest cell
to a passable cell of another class (open, field, road, bog). Entering the forest is free. The boundary is derived from
the terrain-class layer, needs no drawn line, and has the id `forest-edge` (`HardBoundary.ForestEdgeId`). A permit for it
is valid within the permit radius of the requested crossing point, measured to the arc's end point. Drawn boundaries
from the order (for example "do not cross the road") are hard boundaries as well, each with its own id.

Reason: in the drone age (and before), a unit should never be caught without overhead concealment unless a leader
decided to accept that.

## Alternatives considered
- Forest edge hard only at the corridor's edge: lets agents cross clearings inside the corridor without asking,
  which is the exposure this rule exists to prevent.
- Forest edges as preprocessed polylines: same effect, but adds a vectorisation step and can drift from the raster.

## Consequences
- An agent that starts in forest and has a destination outside it always asks for permission at least once.
- Pathfinding treats forest-to-other arcs as forbidden unless a matching permit covers them.
- Tests: `Leaving_forest_crosses_a_hard_boundary_but_entering_forest_does_not` and the forest variant of C4.
- Bog, field and other partial cover are not concealment under this rule; revisit if the movement model gets cover classes.
