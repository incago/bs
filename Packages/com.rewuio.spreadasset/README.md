# SpreadAsset Package

This package contains the reusable SpreadAsset runtime and editor tooling.

Minimum Unity version: Unity 6.4 (`6000.4`).

## Folders

- `Runtime`: APIs safe to use in player builds.
- `Editor`: Unity Editor-only tools and UI.
- `Tests`: package tests.
- `Documentation~`: design and usage notes.
- `Samples~`: examples that can be imported through Unity Package Manager.

## Editor Tools

- `Tools > SpreadAsset > Generator`: creates ScriptableObject data classes and their paired creation menu.
- `Tools > SpreadAsset > Open`: opens the spreadsheet-style editor for selected `.spreadasset` documents.

With the Generator window open, selecting a generated asset class script or its generated factory script reloads the schema settings into the window. Use this to revise generated fields or array table definitions without re-entering the original schema from scratch.

Array data fields can be marked as `Key` in the Generator. Key fields are exported as normal runtime fields and generate lookup helpers on the linked `.asset`, such as `TryGetItemDataById(id, out itemData)` and `GetItemDataById(id)`. Key fields cannot also be design-only fields.

Generated creation menus live under `Assets/Create/...` and create both files in one action:

- `*.spreadasset`: SpreadAsset authoring source document, including serialized data and sheet metadata.
- `*.asset`: linked ScriptableObject data asset exported for runtime use.

Use `Save & Export` in the SpreadAsset window to save source edits into the `.spreadasset` document and update the linked `.asset`.

Use `Export CSV` and `Import CSV` in the SpreadAsset window to round-trip the currently selected array sheet through a spreadsheet-friendly CSV file. CSV import updates the selected sheet in the editor and marks the document dirty; use `Save & Export` afterward to persist the `.spreadasset` source and linked `.asset`. Primitive serialized fields, enums, strings, object asset paths, and common Unity value types such as vectors, rects, bounds, quaternions, and colors are supported. More complex nested serialized objects or arrays are skipped by CSV import/export.

Formula rows can be added above each array table. Formulas can target a whole column, such as `C = A + B`, a single cell, such as `C1 = A1 + B1`, or concatenate strings, such as `D = C + '_key'`. Columns are labeled `A`, `B`, `C` and rows start at `1`.

To make a user-defined enum available as a SpreadAsset Generator data field type, mark it with `[SpreadAssetEnum]`. Only annotated enums are added to the dropdown.
