# uex — UE pak export & exploration tool

Standalone .NET 10 console tool on CUE4Parse. Replaces manual FModel exports for the
arkive pipeline (E:\arkive-games\arkive) and gives agents pak exploration via CLI,
`serve` (JSON-lines), and `mcp` (stdio MCP server).

## Commands
- Build: `dotnet build` — Test: `dotnet test` (49 tests, no paks needed) — Run: `dotnet run --project src/Uex -- <cmd>`
- Publish: `dotnet publish src/Uex -c Release -o publish`
- Real-pak health check: `dotnet run --project src/Uex -- doctor --profile aion2` (AION2 is installed locally; mounts ~1 min; Palworld is NOT on this machine)

## Architecture
- `Config/ProfilesConfig` — named per-game profiles (profiles.json, gitignored: AES keys).
  Resolution: --config > UEX_PROFILES env > ./profiles.json > exe dir.
- `Core/ProviderManager` — lazily mounts one CUE4Parse provider per profile, cached,
  failed mounts evicted (retryable); every operation takes a `profile` parameter →
  one process serves many games. Oodle/zlib DLLs auto-download to `.uex-cache/`.
- `Core/OutputPaths`, `Core/VfsQuery` — pure, unit-tested (no paks needed).
- `Core/ExportRunner` — batch export, FModel-compatible tree: packages → `.json`
  (serialized exports array), textures → `.png`, AION2 `.dat` → decoded `.json`,
  other files raw-copied.
- `Core/Aion2Dat.cs` — ALL AION2-specific .dat handling isolated here. The decryption
  itself is CUE4Parse's own code (`CUE4Parse.GameTypes.Aion2.*` readers); this file is
  only the dispatch (which directory → which reader), which every consumer must supply —
  CUE4Parse core never auto-invokes GameTypes readers, and mainline FModel raw-saves
  .dat. MapEvent = decrypted JSON text; non-MapData files under Data/Map get the empty
  default — parsing them crashes with StackOverflow.
- `Serve/` — JSON-lines stdin/stdout server. `Mcp/` — MCP stdio server, same ops.

## Conventions
- Unit tests must not require game paks; real-pak verification is `doctor`.
- Output layout compatibility with FModel is a hard contract — the arkive `tools/`
  pipeline consumes it (`PALWORLD_RAW` etc.). Semantic JSON equality is the bar;
  for AION2 .dat decoding, byte-identity with the FModel reference was verified.
- CUE4Parse pins: net10.0-only package. The NuGet packages ARE the GitHub releases
  (identical .nupkg assets, e.g. 1.2.2.202607) — no separate "GitHub build" exists;
  bump by taking the newest release tag. When bumping, re-run doctor + an export diff.
- Test files need explicit `using Xunit;` (ImplicitUsings doesn't cover it).

## Plan / history
Original implementation plan: docs/superpowers/plans/2026-07-19-uex-exporter.md
(Task 6b — AION2 .dat decode — was added mid-execution when raw copies turned out
to be obfuscated; see the Aion2Dat commit messages.)
