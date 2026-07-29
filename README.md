# Nut & Bolt Sort (V1)

## Overview

This Unity URP prototype is a portrait, one-finger nut-and-bolt colour sorting game. The player sorts coloured nut rings onto matching bolts. A full single-colour bolt locks in place on the board.

The project currently contains one playable scene:

- `Assets/Scenes/SampleScene.unity`

## Gameplay

1. Tap a bolt to select it. The selected bolt lifts slightly.
2. Tap another bolt to move the top nut group.
3. A move is legal only when the destination has free space and is empty or has the same colour on top.
4. Consecutive nuts of the same top colour move together, up to the destination capacity.
5. A bolt is complete when all four of its nut positions are filled with one colour.
6. A completed bolt remains visible in its current position and locks from further moves.
7. The level ends when every bolt is either empty or complete.

There is no failure state or undo. **Restart** reloads the exact saved procedural puzzle.

## Camera and platform

- Android portrait orientation
- Fixed orthographic 3D camera
- No player camera movement
- Tap/click interaction works through the bolt colliders

## Procedural levels setup

Levels are generated at runtime by `ProceduralLevelGenerator`; authored `LevelDataSO` assets are no longer required for normal play. The generator creates only bottom-to-top logical nut lists and passes them unchanged to `LevelManager`, which continues to instantiate the board and apply `BoltGridLayout`.

1. Open `Assets/Scenes/SampleScene.unity` and select the object that has `GameManager` and `LevelManager` (or add `ProceduralLevelGenerator` to that same object).
2. Assign the generator to **Game Manager → Procedural Level Generator**. If omitted, it is added automatically at runtime.
3. In **Procedural Level Generator**, configure `Supported Colors` with the existing `NutColor` enum entries and create level-range `Difficulty Tiers`. Every tier must use at least one empty bolt and no more colors than the supported list.
4. Keep `BoltView.Capacity` as the single capacity source. The supplied game uses four slots; each selected color is generated exactly four times.
5. Press Play. Restart reads a deep-copied current-level snapshot. Next Level advances the endless index and creates a new seed.
6. For tuning, use the component context-menu commands: Generate New Level, Generate Using Seed, Reload Saved Current Level, Validate Current Puzzle, Replay Guaranteed Solution Logically, and Batch Generate 100 Levels. Enable deterministic test seed to reproduce a result.

Do not add bolt placement code to the generator. Logical bolt ordering may vary, but the existing staggered grid is still the only system assigning bolt positions.

## Scene authoring

The scene is manually authored. Pressing Play does **not** create, delete, reposition, or align game objects. Arrange the board, camera, lights, and art directly in the Unity Hierarchy.

The expected hierarchy under `NUT & BOLT SORT — Editable Prototype` is:

```text
NUT & BOLT SORT — Editable Prototype
├── 00_Environment (replace meshes here)
├── 01_Bolts — Bottom (move bolt roots to arrange)
│   ├── Bolt 1 ...
│   │   ├── Red nut
│   │   ├── Blue nut
│   │   └── ...
│   └── Bolt 2 ...
```

### Required names

The game identifies objects from their hierarchy names. Keep these names when replacing visual meshes:

- Bolt group: `01_Bolts — Bottom (move bolt roots to arrange)`
- Each playable bolt: name begins with `Bolt ` and has a `BoxCollider`.
- Each nut root: `[Colour] nut`, for example `Red nut`, `Blue nut`, `Green nut`, or `Yellow nut`.

You may replace the meshes/materials inside nut roots freely. Do not rename the required roots unless you also update the gameplay script.

## Board layout

The default board follows a mobile sort-puzzle layout:

- Four working bolts in the upper/rear row.
- Two empty manoeuvring bolts in the lower/front row.
- Four colours: red, blue, green, yellow.
- Maximum capacity: four nuts per bolt.

The current level data and gameplay logic are in:

- `Assets/Scripts/NutBoltSortPrototype.cs`

Three hardcoded test layouts are included in that script and can be loaded through the in-game **Level** button. The scene itself remains manually editable and is used as-is when Play starts.

## Boundaries

Included:

- Bolt sorting and grouped moves
- Bolt completion detection
- Completed bolts remain locked in place
- Restart button with exact current-puzzle restore
- Endless deterministic procedural levels with inverse-move replay validation

Not included:

- Undo
- Locked or special nuts
- Menus, progression, ads, currency, audio polish, or monetisation
