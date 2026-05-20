# SpreadAsset Agent Guide

## Project Intent

SpreadAsset is a Unity editor utility asset for teams that want to keep using ScriptableObject-based data while editing and comparing many assets in a table-like workflow.

Think of the core problem as: ScriptableObjects are approachable and native to Unity, but Unity's default Inspector is poor for bulk data review, comparison, and row-style editing. SpreadAsset documents are the authoring source; linked ScriptableObject assets are exported runtime data.

## Current Structure

- Unity host project: repository root.
- Embedded UPM package: `Packages/com.rewuio.spreadasset`.
- Runtime API: `Packages/com.rewuio.spreadasset/Runtime`.
- Editor tooling: `Packages/com.rewuio.spreadasset/Editor`.
- Editor tests: `Packages/com.rewuio.spreadasset/Tests/Editor`.
- Design notes: `Packages/com.rewuio.spreadasset/Documentation~`.
- Importable examples: `Packages/com.rewuio.spreadasset/Samples~`.

## Engineering Rules

- Prefer Unity 6000.4-compatible APIs unless the package manifest is intentionally updated.
- Keep runtime code free of `UnityEditor` references.
- Put all editor-only code in the `SpreadAsset.Editor` assembly.
- Treat ScriptableObject assets as Unity runtime data; integrations such as CSV, JSON, Excel, Google Sheets, or SQLite should be optional adapters.
- Treat `.spreadasset` documents as the editor source of truth. Runtime `.asset` files are export targets.
- Prefer UI Toolkit for new editor windows and inspectors.
- Keep serialized Unity assets text-based and commit `.meta` files.
- Do not place reusable package source under `Assets/`; use the embedded package.

## Product Direction

The first useful milestone should be a table-style editor that can:

- Generate ScriptableObject data classes from a simple schema.
- Create paired `.spreadasset` authoring documents and runtime `.asset` files.
- Open a selected `.spreadasset` document and resolve its linked `.asset`.
- Display normal serialized fields with Inspector-like editing.
- Display serialized array fields as spreadsheet-like rows and columns.
- Support multiple array fields through sheet tabs.
- Keep runtime systems reading the linked `.asset`, not the editor document.
- Save source edits into `.spreadasset` before exporting the serialized data portion to `.asset`.

Later milestones can add schemas, validation rules, import/export adapters, diff tools, and safer bulk edit flows.
