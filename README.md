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

There is no failure state or undo in V1. Use **Restart** to reload the current scene.

## Camera and platform

- Android portrait orientation
- Fixed orthographic 3D camera
- No player camera movement
- Tap/click interaction works through the bolt colliders

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

## V1 boundaries

Included:

- Bolt sorting and grouped moves
- Bolt completion detection
- Completed bolts remain locked in place
- Restart button
- Three test layouts

Not included:

- Procedural level generation
- Undo
- Locked or special nuts
- Menus, progression, ads, currency, audio polish, or monetisation
