# Describe Database Enhancement Design

## Overview

Enhance `describe_database` in `Tools/PermitQLTools.cs` from a markdown-oriented schema summary into a JSON-only metadata contract for agent consumption.

The goal is to improve agent performance for:

- mutation planning
- relationship impact analysis
- query planning
- explicit capability discovery

This design intentionally excludes inferred business semantics. V1 should expose only metadata the system can derive or declare reliably.

## Goals

- Return a stable JSON contract instead of markdown
- Preserve strict governance boundaries from the active ruleset
- Surface high-value structural metadata beyond columns and outbound foreign keys
- Expose validator and provider capabilities explicitly instead of making agents learn by failure
- Allow lightweight probing for inexpensive statistics without turning `describe_database` into a diagnostic workload

## Non-Goals

- Human-oriented markdown output
- Inferred business descriptions for tables or columns
- Recommended search fields or business identifiers
- Expensive data profiling or full table scans
- Partial rewriting of constraints or indexes that would distort their meaning

## Current Problem

`describe_database` currently emits a markdown report with:

- SQL dialect
- global limits
- schemas and tables
- columns with type, nullability, and primary-key marker
- row filters
- outbound foreign-key relationships

That is sufficient for basic schema orientation, but it under-specifies:

- default values and generated/identity behavior for write planning
- unique and check constraints
- inbound foreign keys and referential actions
- index metadata
- lightweight row-count hints
- query and validator capability support

## Architectural Direction

The architecture should favor clean boundaries over a tool-specific snapshot object.

`IDataAccessor` becomes the authoritative introspection boundary with several focused methods rather than one coarse-grained describe-specific method. `describe_database` should assemble filtered JSON from these focused metadata sources instead of embedding dialect-specific introspection logic in `PermitQLTools`.

Responsibilities:

- `IDataAccessor` and provider-specific collaborators fetch metadata
- `PermitQLTools.DescribeDatabase` applies ruleset filtering and shapes the output JSON
- provider/dialect logic lives below the accessor boundary
- validator capability disclosure remains owned by the server/query layer, then merged into the tool response

## JSON Contract

`describe_database` should return a single JSON object with stable top-level sections:

- `database`
- `limits`
- `capabilities`
- `schemas`

### `database`

- `ruleSetKey`
- `dialect`

### `limits`

- `maxRowsReturned`
- `timeoutMs`
- optional global default allowed operations if needed for interpretation

### `capabilities`

This section exposes agent-relevant query and validation behavior.

Recommended fields:

- `ctes`
- `subqueries`
- `derivedTables`
- `windowFunctions`
- `mutations`
- `notes`

Capability values should distinguish support states clearly, for example:

- `supported`
- `unsupported`
- `unknown`

### `schemas`

`schemas` is an array of schema objects. Each schema contains:

- `name`
- `tables`

Each table contains:

- `name`
- `allowedOperations`
- `rowFilter`
- `columns`
- `constraints`
- `relationships`
- `indexes`
- `statistics`
- optional `omissions`

### `columns`

Each column object contains:

- `name`
- `type`
- `nullable`
- `primaryKey`
- `defaultValue`
- `isGenerated`
- `generationKind`

`generationKind` should be a stable enum-like string such as:

- `none`
- `identity`
- `auto_increment`
- `computed`
- `unknown`

### `constraints`

Split constraint types rather than flattening them:

- `unique`
- `check`

Unique constraint object:

- `name`
- `columns`

Check constraint object:

- `name`
- `expression`

### `relationships`

Split relationships into:

- `outbound`
- `inbound`

Each relationship object contains:

- `constraintName`
- `fromSchema`
- `fromTable`
- `fromColumn`
- `toSchema`
- `toTable`
- `toColumn`
- `onDelete`
- `onUpdate`

### `indexes`

Each index object contains:

- `name`
- `columns`
- `unique`

### `statistics`

V1 statistics should remain intentionally small:

- `approximateRowCount`
- optional `lastAnalyzed` only when trivially available and reliable

### Contract Rules

- Keep arrays present even when empty
- Use `null` or explicit support-status values when absence would be ambiguous
- Omit fields only when they are truly not applicable
- Never invent semantic meaning or guessed metadata

## Rule Filtering And Disclosure

The tool must preserve the same governance boundary as query execution. Metadata may only be disclosed for schemas, tables, and columns already visible through the active ruleset.

Filtering rules:

- only exposed schemas appear
- only exposed tables appear
- only allowed columns appear
- metadata objects referencing hidden columns or hidden tables are filtered out unless they remain fully truthful after filtering

### Columns

- include only allowed columns

### Unique And Check Constraints

- drop any unique constraint that references a hidden column
- drop any check constraint that cannot be represented truthfully without disclosing hidden-column details

### Relationships

- outbound relationships are shown only when the source column is allowed and the referenced table is exposed
- inbound relationships are shown only when the referencing table is exposed and the referencing column is allowed

### Indexes

- emit only indexes whose full column set is visible
- omit partial indexes rather than emitting misleading truncated definitions

### Statistics

- table-level statistics are safe for exposed tables
- no column-level statistics in v1

### Capabilities

- capabilities are global/tool-level and safe to disclose because they describe execution behavior rather than protected schema content

### Omissions

Include an optional table-level `omissions` array with machine-readable values such as:

- `hidden_constraints_omitted`
- `partial_indexes_omitted`
- `unavailable_statistics`

This helps agents distinguish between "does not exist" and "not disclosed."

## Introspection Interfaces

The design should extend `IDataAccessor` with focused metadata methods.

Recommended surface:

- `GetTableColumnsAsync(schema, table, ct)`
- `GetTableConstraintsAsync(schema, table, ct)`
- `GetOutboundForeignKeysAsync(schema, table, ct)`
- `GetInboundForeignKeysAsync(schema, table, ct)`
- `GetTableIndexesAsync(schema, table, ct)`
- `GetTableStatisticsAsync(schema, table, ct)`
- `GetQueryCapabilitiesAsync(ct)` or an equivalent provider-backed capability source where appropriate

Notes:

- `GetTableColumnsAsync` should expose richer schema-column metadata than the current `ColumnDefinition`
- query/validator capabilities should be merged into one JSON section for the caller, but should come from distinct internal sources

## Internal Structure

`AdoNetDataAccessor` should remain an orchestration layer, not a single class containing all introspection SQL.

Recommended internal organization:

- accessor-level orchestration in `AdoNetDataAccessor`
- focused resolver interfaces per concern family
- dialect-specific implementations for PostgreSQL and SQLite
- null/fallback resolvers for unsupported or unavailable metadata

Concern families:

- column metadata resolver
- constraint resolver
- relationship resolver
- index resolver
- statistics resolver
- provider capability resolver

This follows the same direction already established by the foreign-key resolver work.

## Probing Strategy

V1 may execute lightweight extra queries, but only when the cost is predictable and low.

Guidelines:

- prefer approximate row counts over expensive exact counts
- avoid table scans when a provider cannot supply a cheap estimate
- if cheap statistics are unavailable, return `null`, `unknown`, or an omission reason instead of forcing expensive work

`describe_database` must remain suitable for routine agent use and should not behave like a profiling tool.

## Error Handling

The tool should be tolerant of partial metadata failures.

Top-level failures:

- invalid ruleset
- inaccessible database
- unrecoverable tool initialization problems

These should continue to use the existing top-level error response style.

Per-table or per-section failures should not invalidate the whole response.

Recommended behavior:

- keep the table in the output when core visibility is known
- omit failed metadata sections or mark them unavailable
- emit machine-readable omission/warning indicators where useful
- distinguish `unsupported` from `unknown`

## Testing Strategy

Testing should be split across three layers.

### Unit Tests

Cover filtering and JSON shaping:

- hidden-column constraints are omitted
- partial indexes are omitted
- inbound and outbound relationships are filtered correctly
- arrays, nulls, and omission markers use consistent semantics

### Provider And Resolver Tests

Cover metadata extraction per provider:

- defaults
- generated/identity columns
- unique and check constraints
- referential actions
- indexes
- lightweight statistics behavior

### Tool-Level Integration Tests

Cover end-to-end governed output:

- `describe_database` returns the expected JSON contract
- non-exposed schemas, tables, columns, and metadata are not leaked
- capability flags reflect actual validator/provider behavior

### Absence Semantics

Tests should explicitly enforce the meaning of:

- empty array = known none
- `null` = applicable but unavailable or unknown
- omitted field = not applicable, only where intentionally defined that way

## Open Design Decisions Resolved

Resolved for this design:

- output format is JSON-only
- v1 excludes semantic/business annotations
- lightweight probing is allowed
- architecture favors focused introspection methods over a monolithic metadata snapshot

## Expected Outcome

After implementation, `describe_database` will provide a stronger machine-readable contract for autonomous agents while keeping governance boundaries intact and avoiding speculative semantics.

The result should reduce trial-and-error for:

- inserts and updates
- join and dependency reasoning
- query-shape selection
- validator feature discovery
