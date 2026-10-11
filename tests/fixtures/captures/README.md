# Committed captures

Captures (F9, "Capture for Diagnosis") kept as fixtures, so the room rules are held against real
areas on every build (`CaptureFixtureTests`) and every replay of `CaptureReplayTests` can be run
again without the game. All were taken on 2026-10-11; the pictures were left out (about 20 MB a
capture) - nothing the replays read is in them.

| Folder | Area | Why it is kept |
|---|---|---|
| `oasis` | Oasis (MapOasis), `0x07D66EE7` | where the orientation and origin rules were first read; 14 rooms placed by their doodads |
| `steppe` | Steppe (MapSteppe), `0xE9FD7267` | the open `forced_blank` question; its list weights tiles 1, 20 and 300 |
| `sulphuric-caverns` | Sulphuric Caverns (MapSulphuricCaverns), `0x03EB5F7D` | the boss room lays tile files no list names (`Thickwall/Fill_01` for the list's `Murky/Thickwall/Fill_01`, and an `Abyss/Lava/Fill_02`) - 17 of its 39 settled slots |
| `steaming-springs` | Steaming Springs (MapSteamingSprings), `0x6FC87FF1` | the most weight samples (57) and 15 placed rooms; every settled slot comes out the game's file |
| `marrow` | Marrow (MapMarrow), `0x193FC4A6` | v0.1.183, the first with `tile-files.txt`: `.tmd` read as a tile's doodad list, no tile inheriting, a laid plain tile sharing its template with the listed `_feature` one, four `mine_abyss_fill` tiles no tile list names, and the first tile-list words (`I`, `R90`, `R180`, `R270`) |

The first four were taken with v0.1.179 and carry neither `tile-files.txt` nor `area-masters.txt`
(v0.1.184); a capture from that build on is what says whether a master's fill list is where the
unlisted tiles come from.

Run every replay over one of them:

```
POEF_CAPTURE="tests/fixtures/captures/oasis" dotnet test tests/PoEformance.Core.Tests --filter "FullyQualifiedName~CaptureReplay"
```

and the weight estimate over several at once (the platform's path separator between):

```
POEF_CAPTURES="tests/fixtures/captures/oasis:tests/fixtures/captures/steaming-springs" dotnet test tests/PoEformance.Core.Tests --filter "FullyQualifiedName~THEWEIGHT"
```

The replays write their `*-replay.txt` beside the capture; `.gitignore` keeps them out of the repository.
