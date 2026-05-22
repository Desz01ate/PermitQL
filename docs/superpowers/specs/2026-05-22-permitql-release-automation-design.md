# PermitQL Release Automation Design

## Summary

Add GitHub Actions release automation that triggers only on semantic version tags formatted as `vX.Y.Z`. The release pipeline will validate the solution, publish the `PermitQL.Server` application as a .NET global tool package named `permitql` to `nuget.org`, and attach standalone server binaries for `linux-x64`, `win-x64`, and `osx-arm64` to the matching GitHub release.

## Goals

- Publish a .NET CLI tool installable as `permitql`.
- Gate publishing on version tags only.
- Use the Git tag as the single authoritative release version.
- Attach downloadable server binaries for the agreed runtime identifiers.
- Keep the implementation narrow and aligned with the existing solution layout.

## Non-Goals

- Adding push/PR CI beyond what is needed for tagged releases.
- Supporting prerelease tag patterns outside `vX.Y.Z` unless added later.
- Introducing new packaging dependencies or external release tooling.
- Changing the server’s runtime behavior beyond what is necessary to package it as a tool and standalone binaries.

## Current State

[`PermitQL.Server/PermitQL.Server.csproj`](/home/deszolate/Documents/Private/PermitQL/PermitQL.Server/PermitQL.Server.csproj) is a web SDK project already configured for self-contained single-file publish output. The repository does not currently contain a `.github/workflows` directory or an automated release pipeline. The solution targets `.NET 10.0`, uses central package management, and already has a single server entrypoint suitable for both CLI execution and binary publishing.

## Proposed Approach

### Packaging Model

Keep the server project as the single source for both tool packaging and standalone binaries.

Add NuGet tool packaging metadata to [`PermitQL.Server/PermitQL.Server.csproj`](/home/deszolate/Documents/Private/PermitQL/PermitQL.Server/PermitQL.Server.csproj):

- `PackAsTool` enabled.
- `ToolCommandName` set to `permitql`.
- `PackageId` set to `permitql`.
- Public package metadata added where required for `nuget.org`, such as description and repository URL.

Do not hardcode the package version in the project file. The release workflow will inject `PackageVersion` from the Git tag so versioning remains single-source and operationally obvious.

Preserve the existing publish configuration for standalone binaries. If any current publish properties interfere with tool packing, narrow them so pack and publish can coexist without branching into separate projects.

### Workflow Model

Add one workflow file at `.github/workflows/release.yml`.

Trigger:

- `push` on tags matching `v*.*.*`.

Execution order:

1. Checkout the repository.
2. Install the required .NET SDK.
3. Extract `X.Y.Z` from the `vX.Y.Z` tag and expose it as the release version.
4. Restore dependencies.
5. Build the solution in `Release`.
6. Run the full test suite.
7. Pack the .NET global tool package for `permitql` using the tag-derived version.
8. Push the generated package to `nuget.org`.
9. Publish standalone binaries for `linux-x64`, `win-x64`, and `osx-arm64`.
10. Create or update the GitHub release for the tag.
11. Upload zipped publish outputs as release assets.

### Versioning Rules

The Git tag is the only source of truth for the release version.

- Tag format: `vX.Y.Z`
- Package version: `X.Y.Z`
- GitHub release version/title: derived from the same tag
- Asset filenames: include both runtime identifier and tag-derived version

If the pushed ref does not conform to the expected tag format, the workflow should fail early rather than infer or mutate the version.

### Binary Asset Shape

Produce one release archive per runtime identifier:

- `permitql-linux-x64-vX.Y.Z.zip`
- `permitql-win-x64-vX.Y.Z.zip`
- `permitql-osx-arm64-vX.Y.Z.zip`

Each archive will contain the output of `dotnet publish` for that RID. This keeps binary distribution separate from the NuGet tool while reusing the same entrypoint project and release version.

### Secrets and Permissions

Required repository secret:

- `NUGET_API_KEY` for publishing to `nuget.org`

Workflow permissions:

- `contents: write` to create or update GitHub releases and upload assets

No broader token scope should be granted unless later workflow steps require it.

## Files Likely To Change

- [`PermitQL.Server/PermitQL.Server.csproj`](/home/deszolate/Documents/Private/PermitQL/PermitQL.Server/PermitQL.Server.csproj)
  Adds tool packaging and package metadata.
- `.github/workflows/release.yml`
  Implements tag-driven validation, packing, NuGet publish, and release asset upload.
- `README.md`
  Update installation and release usage instructions if the repository root README already documents local installation or release distribution.

## Testing Strategy

Before publishing, the workflow must run:

- `dotnet restore PermitQL.sln`
- `dotnet build PermitQL.sln -c Release`
- `dotnet test PermitQL.sln -c Release`

Local verification for implementation should also include:

- `dotnet pack PermitQL.Server/PermitQL.Server.csproj -c Release -p:PackageVersion=<test-version>`
- `dotnet publish PermitQL.Server/PermitQL.Server.csproj -c Release -r linux-x64`
- `dotnet publish PermitQL.Server/PermitQL.Server.csproj -c Release -r win-x64`
- `dotnet publish PermitQL.Server/PermitQL.Server.csproj -c Release -r osx-arm64`

The implementation should verify that:

- the `.nupkg` is emitted with package ID `permitql`
- the tool command is `permitql`
- publish outputs exist for all three runtime identifiers
- asset naming follows the tag-derived version convention

## Risks and Mitigations

### Tool pack vs publish configuration conflicts

The current project is optimized for self-contained single-file publish. .NET global tools typically expect framework-dependent packaging behavior. The implementation needs to verify that the project can support both workflows cleanly. If not, the fix should stay as small as possible, ideally by scoping publish-only properties rather than splitting into multiple projects.

### .NET 10 availability on GitHub-hosted runners

The workflow depends on the appropriate SDK being installable in GitHub Actions. The implementation should pin the SDK explicitly and use the standard setup action rather than relying on runner defaults.

### Release asset portability

RID-specific publish output can differ in structure. The workflow should archive the published directory contents rather than hand-picking files, which reduces naming and omission mistakes across platforms.

## Acceptance Criteria

- Creating and pushing a tag `v1.2.3` runs the release workflow.
- The workflow builds and tests the solution before any publish step.
- The workflow packs and publishes a NuGet global tool package named `permitql` with version `1.2.3`.
- The workflow publishes to `nuget.org` using the configured secret.
- The workflow creates or updates the GitHub release for `v1.2.3`.
- The workflow uploads zipped standalone binaries for `linux-x64`, `win-x64`, and `osx-arm64`.
- No release or package publish occurs on ordinary branch pushes.
