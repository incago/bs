# Changelog

## 0.1.3

- Added SpreadAsset Generator custom data field type entry for Unity-serializable types such as `AnimationCurve`, `Gradient`, `List<T>`, object references, enums, and serializable project classes.
- Added `[SpreadAssetClass]` for surfacing annotated project classes and structs in the Generator field type dropdown.
- Kept the Generator type dropdown focused on recommended types while preserving custom direct type entry.
- Updated sample stage data to exercise enum, prefab reference, double, and animation curve fields.
- Documented custom field type entry and `[SpreadAssetClass]` usage.

## 0.1.2

- Added SpreadAsset Generator support for data-class-only array table definitions that generate nested data classes without creating asset-level array fields.
- Added generator field type options for primitive arrays and data classes declared in the same generated asset schema while preventing direct self-references.
- Added key validation in SpreadAsset Editor, including duplicate/blank key warnings, invalid key cell highlighting, and export blocking when key values are not safe.
- Improved expanded array and nested object rendering in SpreadAsset Editor with dynamic row heights and narrower responsive cell layouts.
- Updated sample stage data to use key-based room references and generated row lookup helpers.

## 0.1.0

- Added initial package structure.
- Added Runtime, Editor, Editor Tests, Documentation, and Samples folders.
- Set the minimum Unity version to Unity 6.4.
- Added SpreadAsset Generator for schema-based ScriptableObject class generation.
- Added paired `.spreadasset` document and `.asset` creation flow.
- Added SpreadAsset Window support for editing linked ScriptableObject fields and array tables.
- Changed editing flow so `.spreadasset` stores the source serialized data and exports it to the linked `.asset`.
- Added a custom Project window icon for `.spreadasset` documents.
- Added double-click and context-menu opening for `.spreadasset` documents.
- Added sheet formulas with spreadsheet-style cell references and basic arithmetic.
- Added generator reload support from selected generated asset or factory scripts.
- Added type labels and wider horizontally scrollable columns to SpreadAsset Editor tables.
- Added column formulas such as `C = A + B` for applying one formula across every row.
- Changed formula precedence so specific cell formulas override column formulas for the same cell.
- Fixed formula row focus handling after adding or removing formulas.
- Changed formula validation to run after the formula input loses focus instead of on every keystroke.
- Changed formula rows to show all formulas without an internal scrollbar and made the list collapsible.
- Added single-quoted string literals and string concatenation to formulas.
- Changed table row scrolling to appear only when more than 10 data rows exist.
- Added a prominent top Save & Export button to SpreadAsset Editor.
- Added keyboard navigation for table cells with Tab and Enter.
- Changed table scrolling to use an internal scroll area only for horizontal column overflow.
- Fixed the table's horizontal overflow area so it does not show an internal vertical scrollbar.
- Fixed clipped bottom rows in horizontally scrollable SpreadAsset Editor tables.
- Changed Asset Fields to stay above the table scroll area and made the field list collapsible.
- Added design-only array data fields that are stored in `.spreadasset` documents and omitted from generated runtime assets.
- Added schema refresh for existing `.spreadasset` documents when generated classes gain new fields.
- Added `TEXT`/`FORMAT` formula functions for numeric string formatting such as `TEXT(A, '0000')`.
- Changed SpreadAsset Generator array data field types from free text input to a dropdown.
- Added a Project window menu to recreate a `.spreadasset` document from an existing `.asset`.
- Added migration support that infers a SpreadAsset schema from an existing ScriptableObject asset and generates the matching editor factory script.
- Added selected-sheet CSV export and import from the SpreadAsset Editor window.
- Added Generator key fields for array data tables and generated runtime row lookup helpers.
