# BetterScriptable

BetterScriptable is a Unity utility asset for making ScriptableObject-based game data easier to inspect, compare, and edit.

ScriptableObjects are native to Unity and friendly for small data sets, but the default Inspector makes bulk work painful. This project aims to keep the strengths of ScriptableObjects while adding table-like editor workflows similar to spreadsheets or database tools.

BetterScriptable uses `.betterscriptable` files as the authoring source. The linked `.asset` file is the exported runtime data that game systems read.

## Screenshots

![BetterScriptable editor screenshot](screenshot01.png)

![BetterScriptable spreadsheet screenshot](screenshot02.png)

## Repository Layout

```text
BetterScriptable Unity project
├── Assets/
│   └── Scenes/
├── Packages/
│   └── com.rewuio.betterscriptable/
│       ├── Runtime/
│       ├── Editor/
│       ├── Tests/
│       ├── Documentation~/
│       └── Samples~/
├── ProjectSettings/
└── AGENTS.md
```

## Development Notes

- Unity version: Unity 6.4 (`6000.4.5f1`).
- Main package: `Packages/com.rewuio.betterscriptable`.
- Runtime namespace: `BetterScriptable`.
- Editor namespace: `BetterScriptable.Editor`.
- New reusable source should live in the package, not directly under `Assets/`.

## Initial Milestone

Build a ScriptableObject table editor that can discover assets by type, show each asset as a row, expose serialized fields as columns, and safely save edited assets.

## Current Workflow

1. Open `Tools > BetterScriptable > Generator`.
2. Enter an asset class name, create menu path, asset fields, and array data schemas.
3. Generate the runtime data script and Editor-only creation menu.
4. Use the generated `Assets/Create/...` menu to create a paired `.betterscriptable` document and `.asset`.
5. Select the `.betterscriptable` file and edit its source data through `Tools > BetterScriptable > Open`.
6. Use `Save & Export` to save the document and update the linked runtime `.asset`.

When revising a generated class, keep the Generator window open and select the generated asset script or its factory script. The original schema settings are loaded back into the window for editing.

Array tables support basic spreadsheet-style formulas. Use `C = A + B` to apply a formula to every row in a column, `C1 = A1 + B1` for a single cell, or `D = C + '_key'` for string concatenation. Formula target cells are stored in the `.betterscriptable` document and exported into the linked `.asset`.
