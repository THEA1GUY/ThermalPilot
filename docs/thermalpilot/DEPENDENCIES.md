# TP-00 dependency and provenance inventory

Date: 30 October 2026. Inventory, not a claim of distribution clearance.

## Preserved upstream

Baseline: seerge/g-helper at 0a1538ac4fb0337f6cf3d64a907acde10c91a3d9. Keep LICENSE and upstream documentation/notices. The repository LICENSE is GNU GPL version 3 text.

No original hardware-control source or embedded driver-module bytes were modified in TP-00. app/GHelper.csproj changes the build/startup identity and package-lock behaviour; app/BaselineEntryPoint.cs is a new inert entry point.

| Unchanged artefact | SHA-256 |
|---|---|
| LICENSE | 230184F60BAE2FEAF244F10A8BAC053C8FF33A183BCC365B4D8B876D2B7F4809 |
| app/Pawn/RyzenSMU.bin | B84ECA7F32C63B3D8C14B2C6D45482706DF8683AA6F43EB8BEAD9DC62181D38F |
| app/Pawn/IntelMSR.bin | 5BFDA87500160076158BEFC77300F81A388D72B11DF2C794A3939EBE66777098 |

The embedded modules are inherited from upstream. Their inclusion is not a claim that we installed/loaded PawnIO or created those modules. Trace their distribution notices/provenance before a public binary release.

## Resolved NuGet graph

The lockfile records version/content hashes for seven direct packages and one transitive package. Entries below reflect the restored nuspec metadata, not an inferred licence.

| Package | Version | Kind | Licence metadata |
|---|---|---|---|
| FftSharp | 2.2.0 | Direct | MIT expression |
| HidSharpCore | 1.3.0 | Direct | No licence expression/file/URL found in inspected nuspec; unresolved for release review |
| NAudio.Wasapi | 2.4.0 | Direct | MIT expression |
| NvAPIWrapper.Net | 0.8.1.101 | Direct | Legacy licence URL to upstream LICENSE; review the actual terms before distribution |
| System.Management | 10.0.12 | Direct | MIT expression |
| TaskScheduler | 2.12.2 | Direct | MIT expression |
| WinForms.DataVisualization | 1.10.2 | Direct | MIT expression |
| NAudio.Core | 2.4.0 | Transitive | MIT expression |

Microsoft SDK/runtime/reference packs are supplied by the pinned SDK/tooling and are separate from that application PackageReference count. A later frontend/host/MCP package adds a new inventory entry and lockfile review.

## Open release tasks

- Resolve HidSharpCore notices from the precise package/source version.
- Read the NvAPIWrapper licence linked by that package.
- Preserve inherited contributor/library notices and document binary-module provenance.
- Inventory new UI/MCP/host dependencies when selected.
- Recheck dependency/security advisories and redistribution notices for the actual release build.
- Configure fork-owned signing/release/update identity. Do not reuse claims or credentials tied to upstream.

These tasks do not block a local compile-only checkpoint. They remain explicit TP-12 release gates.
