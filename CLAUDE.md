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
- CUE4Parse is a **pinned submodule** (`external/CUE4Parse`, ProjectReference), not the
  NuGet package. Clone with `--recurse-submodules`. The releases lag source by weeks and
  new games land in source first: Lord of Mysteries' container support was absent from
  1.2.2.202608 and its absence is *silent* — the generic provider just doesn't mount the
  containers it can't describe, so the profile looks fine and comes up 3/4 short. Bump by
  moving the submodule and re-running doctor + an export diff; go back to PackageReference
  only if a release ever carries everything in use.
- Source moves under you: `Aion2DatFileAes` became `Aion2DatFileEncryption`, and
  `ETextureFormat` moved to `CUE4Parse_Conversion.Options`. A ProjectReference also does
  not propagate the package's global usings, so namespaces must be spelled out.
- Games with their own container layout need their own provider — see
  `ProviderManager.CreateProvider`. Add the mapping there, never at the call site.
- Test files need explicit `using Xunit;` (ImplicitUsings doesn't cover it).

## Plan / history
Original implementation plan: docs/superpowers/plans/2026-07-19-uex-exporter.md
(Task 6b — AION2 .dat decode — was added mid-execution when raw copies turned out
to be obfuscated; see the Aion2Dat commit messages.)
