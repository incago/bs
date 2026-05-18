# BetterScriptable Architecture Notes

## Goal

BetterScriptable should make ScriptableObject data feel closer to spreadsheet or database-table editing without abandoning Unity's native asset workflow.

## Boundaries

- Runtime code should stay small and dependency-light.
- Editor code owns discovery, table rendering, editing, validation, and save flows.
- External data sources should be adapters, not the core storage model.

## Suggested Modules

- Asset discovery: find ScriptableObject assets by type and path.
- Schema reflection: convert serialized fields and properties into editable table columns.
- Table UI: render rows, columns, sorting, filtering, selection, and editing.
- Persistence: track dirty assets, undo operations, and explicit save behavior.
- Validation: surface errors and warnings before bulk saves.
- Import/export adapters: optional bridges for CSV, JSON, Excel, Google Sheets, or SQLite.

## Document Pair Model

BetterScriptable uses a paired file model:

- `.betterscriptable`: source document metadata, schema information, serialized source data, sheet state, and future cell formula/note data.
- `.asset`: the exported Unity ScriptableObject asset used by runtime game systems.

The editor window opens the `.betterscriptable` document, resolves the linked `.asset`, and creates a temporary working copy from the serialized data stored inside the document. Saving writes the source data back to `.betterscriptable`; Save & Export then applies only that serialized data portion to the linked `.asset`.

In this model, the `.betterscriptable` file is the authoring source of truth. The linked `.asset` is intentionally treated like an exported runtime artifact.

## Formula Model

Each sheet can store multiple formulas in the `.betterscriptable` document. A formula can target a whole column or one specific cell:

```text
C = A + B
C1 = A1 + B1
Total = Price * Count
D = C + '_key'
```

Columns start at `A` and continue through `Z`, `AA`, `AB`, and so on. Rows start at `1`.

Column formulas are evaluated once per row, so `C = A + B` computes each row's `C` value from that same row's `A` and `B` values. A cell formula has higher priority than a column formula for the same cell, so `C1 = A1 + B1 + 300` overrides the row 1 result from `C = A + B`. Formula expressions support numeric constants, single-quoted string literals, cell references, column references, serialized field names, `+`, `-`, `*`, `/`, parentheses, and unary `+`/`-`. The `+` operator adds numbers or concatenates text when either operand is a string. Formula target cells are calculated from the document source data and exported into the linked `.asset` with the rest of the serialized data.

## Generation Workflow

The `BetterScriptableGenerator` window creates two scripts:

- A runtime ScriptableObject data class that inherits `BetterScriptableAsset`.
- An Editor-only factory menu item under `Assets/Create/...`.

The generated factory menu creates the `.betterscriptable` and `.asset` files together so users do not need to manually create or wire the pair.

The generated factory script also stores the schema used for creation. When the Generator window is open, selecting the generated runtime script or factory script reloads that schema back into the window for revision.

`CreateAssetMenu` alone is not used for generated assets because it can only create the `.asset` file. BetterScriptable needs an Editor factory menu so one menu action can create both files.
