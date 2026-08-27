# uex

A headless, FModel-compatible Unreal Engine pak exporter and explorer built on
[CUE4Parse](https://github.com/FabianFG/CUE4Parse). One binary serves multiple
games through named profiles. It replaces manual FModel exports for the arkive
game-wiki pipeline and gives tools (and agents) programmatic pak access via a
CLI, a JSON-lines `serve` mode, and an `mcp` stdio server.

## Requirements

- .NET 10 SDK.
- **`git clone --recurse-submodules`.** CUE4Parse is built from a pinned submodule
  under `external/`, not from NuGet — games reach CUE4Parse source well before a
  release, and a missing game-specific provider fails *silently* (its containers
  simply don't mount). Move a reference back to `PackageReference` once a release
  carries what you need.
- The game's pak files (a local install, or a copy of the `Paks` directory).
- A per-game `.usmap` mappings file and, for encrypted games, the AES key.

The usmap/AES/paths values come straight from a working FModel setup —
`%APPDATA%\FModel\AppSettings.json`:
- `GameDirectory` → `paksDir`
- per-directory `UeVersion` → `game` (a CUE4Parse `EGame` enum name)
- `AesKeys.mainKey` → `aesKey`
- `Endpoints` mappings `FilePath` → `usmap`

## Setup

```bash
cp profiles.example.json profiles.json
# edit profiles.json — fill in paksDir, usmap, aesKey per game
```

`profiles.json` is **gitignored** because it holds AES keys and machine-specific
paths. `profiles.example.json` is the committed template.

Config resolution order (first match wins):
1. `--config <path>` global option
2. `UEX_PROFILES` environment variable
3. `./profiles.json` (current directory)
4. `profiles.json` next to the executable

### Profile fields

| field         | meaning                                                        |
|---------------|----------------------------------------------------------------|
| `game`        | CUE4Parse `EGame` enum name, e.g. `GAME_Aion2`, `GAME_Palworld`|
| `paksDir`     | directory containing the game's `.pak`/`.utoc` files           |
| `usmap`       | path to the `.usmap` mappings file (optional for some games)   |
| `aesKey`      | AES key as `0x…` hex, or `null` for unencrypted games          |
| `outputDir`   | root the `export` command writes the FModel-style tree into    |
| `exportRoots` | list of virtual-path roots to walk for `export`/`doctor`       |

## Commands

Every command except `serve`/`mcp` takes `--profile <name>`.

```bash
# Mount + sanity-check a profile (probes 3 packages, exits non-zero on failure)
uex doctor --profile aion2

# Batch export exportRoots to outputDir (FModel-compatible tree)
uex export --profile aion2
uex export --profile aion2 --only AION2/Content/UI          # subset by vpath

# Browse the virtual filesystem
uex list --profile aion2 AION2/Content/UI
uex search --profile aion2 "*.uasset" --regex --limit 200

# Dump one asset as FModel-style JSON to stdout (handles UE packages and .dat)
uex preview --profile aion2 AION2/Content/UI/SomeTexture --max-bytes 200000

# Export a single texture to PNG
uex preview-texture --profile aion2 AION2/Content/UI/SomeTexture --out out.png
```

**Export output layout** (matches FModel — the arkive `tools/` pipeline depends
on it): UE packages (`.uasset`/`.umap`) → `.json` (the serialized exports array);
`UTexture` exports → `.png`; AION2 obfuscated `.dat` data files → decoded `.json`;
everything else → raw copy. The run prints:

```
exported: N packages, N textures, N raw files, N decoded data files -> <outputDir>
```

### serve — JSON-lines over stdin/stdout

Serves **all** profiles from one process; each profile is mounted lazily on first
use. One request/response per line:

```
> {"id":1,"cmd":"list","profile":"aion2","args":{"path":"AION2/Content/UI"}}
< {"id":1,"ok":true,"result":[ ... ]}
```

Commands: `profiles`, `list`, `search`, `preview`, `preview-texture`, `export`,
`shutdown`. Errors come back as `{"id":..,"ok":false,"error":"..."}`.

### mcp — MCP stdio server

Official C# MCP SDK. Tools: `profiles`, `list_dir`, `search_paths`,
`preview_asset`, `preview_texture`, `export_assets`. Every tool except `profiles`
takes a `profile` param, so one server covers every game. Register it with:

```bash
dotnet publish src/Uex -c Release -o publish
claude mcp add uex --scope user \
  -e UEX_PROFILES=E:/arkive-games/uex/profiles.json \
  -- E:/arkive-games/uex/publish/uex.exe mcp
```

## Multiple games

Profiles *are* games. Because a `profile` argument threads through every
operation and mounts are cached per profile, a single `serve` or `mcp` process
transparently serves all configured games with lazy mounting and a per-profile
`outputDir`. Add a new game by adding a profile entry — no code changes.

## Game-specific notes

- **AION2** stores most game data in obfuscated loose `.dat` files under
  `AION2/Content/Data/...`. uex decodes these to JSON with FModel parity
  (byte-identical verified): Table files (key-manifest AES + LZ4), Map/WorldMap
  (keystream), MapEvent (decrypted JSON text), and the MapDataHierarchy reader.
  All of this is isolated in `Core/Aion2Dat.cs`.
- **Lord of Mysteries** (诡秘之主; launcher `GMZZLauncher`, UE project `C7`) needs
  `"game": "GAME_LordOfMysteries"` — the game-specific `0x05070004` (`84344836`),
  **not** plain `GAME_UE5_7` (`0x05070000`). FModel writes that same number as the
  profile's `UeVersion`, so copy it rather than reading "5.7" off the usmap name.
  Its containers use a `.upak` extension for ordinary `.pak` files.
  **Its `paksDir` must be the game root** (`.../Game/C7`), not `Content/Paks`:
  only two of its `.utoc` are real files, and the rest of the container set is
  described by `Content/package.manifest` with chunk names supplied by
  `Content/Manifest_UFSFiles_Win64.txt`. `LoMDefaultFileProvider` needs to see
  both, and uex selects it automatically for this game. Point it at
  `Content/Paks` and you get 148k files and almost no cooked art instead of
  591k — and nothing reports an error, which is the trap.
- **First mount downloads native decompression DLLs** (Oodle, zlib) into a
  `.uex-cache/` directory next to the executable — this needs network access
  once. Subsequent runs are offline.

## Development

```bash
dotnet build
dotnet test                                         # 49 tests, no game paks needed
dotnet run --project src/Uex -- doctor --profile aion2   # real-pak smoke test
dotnet publish src/Uex -c Release -o publish
```

Unit tests cover the pure logic only (`Core/OutputPaths`, `Core/VfsQuery`, config
loading) and require no paks. Real-pak verification is `doctor` against an
installed game.

## License

Copyright (c) 2026 Yihao Liu (tc-imba)

Licensed under the [Apache License 2.0](LICENSE) — the same license as
[CUE4Parse](https://github.com/FabianFG/CUE4Parse), which this tool is built on.
