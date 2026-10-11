# Project conventions

Path of Exile 2 memory-reading / overlay tool. C# on .NET 10, ImGui for the overlay,
WebView2 for configuration, Native AOT for shipping. A ground-up rewrite of the AutoHotkey
v2 tool, not a port of its structure.

## Never guess — read the reference

**When something about the game's memory or rendering is unclear, OPEN THE REFERENCE
before forming a theory.** This is the single most important rule here, and ignoring it has
already cost real time on this project.

- **`Gordin/GameHelper2`** (branch `main`) is the authority. It is a working tool against
  the same game, so its code answers questions that guessing cannot. Fetch the actual file:
  `https://raw.githubusercontent.com/Gordin/GameHelper2/main/<path>`.
  Especially useful: `GameHelper/RemoteObjects/States/InGameStateObjects/WorldData.cs`
  (the world-to-screen projection), `Plugins/Radar/` (the map projection and its Helper),
  `Plugins/HealthBars/` (how to place something over an entity), and
  `GameOffsets/` (struct layouts).
- **The AHK tool** (`imm0r/PoEformance`, `ahk/`) is battle-tested against PoE2 specifically
  and carries drift history the upstream does not. Its `CLAUDE.md` is a long record of
  problems already solved.
- **This tool's own interface browser** is a reference too, and the easiest one to forget
  because it is not a file: Inspect → UI Browser walks the live UI element tree, prints
  every element's StringId, rectangle, flags and child path, and F8 picks whatever is under
  the cursor. Any question of the form "does the game name that thing, and where is it" is
  one screenshot away — ask it there before concluding that something cannot be measured.
  The model pane has the same thing for its OWN pictures: the bug button down its right edge (the probe), then a
  click, lists every surface under that pixel (material, blend, program or texture, which
  doodad or tile piece, depths), and counts how each translucent layer came out over the
  whole picture. The capture key (F9, "Capture for Diagnosis") writes it to `model-probe.txt`
  in the capture's zip, beside every other diagnosis the tool can give — ask for that capture
  before theorising about why a tile or room looks wrong.
- **`adamthedash/poe_data_tools`** is to the game's FILE formats what GameHelper2 is to its
  memory: working parsers for `.ao`, `.sm`, `.smd`, `.fmt`, `.mat` and `.ast`. Clone it and
  read the parser, not a diagram of it — `crates/poe_data_tools-lib/src/file_parsers/`.
  `.smd` and `.fmt` wrap the SAME DOLm geometry block, and every rule about what sits between
  that block and the shape names is a `cond(...)` in its `dolm` parser. Reading those gates
  as unconditional cost this project a day: four bytes taken that were not there put every
  shape name four bytes out, and the names came back NEARLY right, which reads as a wrongly
  placed piece rather than a wrongly stepped file. It covers BOTH games — its CLI takes
  `--patch 1`, `2` or a specific version — so a parser there is a PoE2 parser unless it
  branches on the patch.
- **Know which game a reference was written against.** The two games share most file formats,
  which is exactly why a PoE1 tool can look like proof: it reads the PoE2 file and nearly
  works. Check its paths before citing it.
  - `annalithic/poeformats` is PoE2 now: `GameDirectory.cs` lists extracts 0.1.0 to 0.5.4f,
    the PoE2 releases, and `Schema` defaults to `poe2 = true`. Its `Ggpk.cs` still points at
    an old 0.8.8 extract, so a legacy corner of it is not.
  - `annalithic/poeterrain` is **Path of Exile 1 only**: every path names 3.17 Siege to 3.21
    Crucible, and its last commit is 2023-08-10. Its `.arm` importer helped (a slot as a fan of
    four corner quarters, slots at their own `(x, y)`), but nothing taken from it is settled for
    PoE2 until PoE2 files or the game agree. Its doodads prove nothing: the line that reads them
    (`Arm.cs`) and the loop that places them (`ArmImportComponent.cs`) are both commented out.
- Do not trust a summarised directory listing over the real thing. A tree summary once
  reported "no Radar plugin" for a repo that plainly has one, and that wrong answer was
  taken at face value.

What guessing produced, for the record: a matrix invariant that rejected the correct offset
and accepted a decoy; a projection "proven" by a check that a wrong matrix passes trivially;
a 52-pixel offset explained by an invented theory about HUD framing; markers moved onto
`TerrainHeight`, which belongs to a different coordinate system entirely; a hand-written
parser for the game's key bindings that missed both the `Input_flask_4_primary` spelling and
the fact that a numeric value is a decimal VIRTUAL-KEY CODE (`81` is Q, not the 8 and 1
keys); and a whole feature built to have the USER say where the HUD is, on the conclusion
that the game does not name its parts — while the tool's own browser lists the HUD as an
element called `HUD` with `life_orb`, `mana_orb` and `experience_bar` as its children. Each
was a one-minute lookup away.

That last one is the variant to watch for, because it does not feel like guessing: the
reference projects were checked, neither had an answer, and "not measurable" followed. **The
absence of an answer in the reference is not evidence of absence in the game.** GameHelper2's
Radar has the user drag a culling window over their own screen; that is what the reference
does, not what the game permits. Check what the game actually exposes before adopting
somebody else's workaround.

The game's own files are reference material too, not just the two projects above. The flask
keys live in `poe2_production_Config.ini`, so the tool reads them rather than assuming the
default 1-5 layout — the assumption looks correct until someone rebinds, and then the only
symptom is that nothing happens.

A monster's `.ao` is the same kind of file, and six versions went out learning it. Attached
pieces came out in the wrong place, and every fix was a rule invented from bone NAMES and
rest positions — any shared name, then a shared name resting where the parent's rests, then
the socket, then the nearest shared ancestor — while the answer sat in the piece's own `.ao`
the whole time: an `attachment_bones` line and the `bone_group` it ends with, two parallel
lists pairing the piece's bones to the parent's by POSITION in the list. Everything in the
middle agrees, which is why guessing by name got most of a monster right; the first entry
does not, which is why the rest of him hung off the floor. Two of the invented rules each
broke the monster the other had just fixed.

What finally showed the shape of it was printing the PARENT's rest position beside the
piece's in the model dump. The two rigs carry different rest poses — a piece is authored in a
neutral one and the body in a crouch — and four rules in a row had assumed they matched
without ever putting the two numbers side by side. **Print both halves of a comparison before
theorising about either**, and read the attachment's own file before its bone list.

A room's `.arm` is the same again. Each doodad line carries a counted list of floats that
neither reference names (poe_data_tools: `floats`, poeformats: `unk7`). It is the doodad's z
in the area's own frame. Two things in the game settled it: a pot read from memory sits at its
line's −115, and seepage's offices drew right once every doodad was set at its value rather than
on the ground. A doodad whose line carries no value stands on the ground. `RoomDoodad.Height`
holds the value, and the Tile Book's "doodad z" button keeps the two wrong readings for comparison.

## The two screen-space systems

The game projects to the screen in **two independent ways**, and mixing them up looks
exactly like a bug in the other one:

1. **The 3D world** — `WorldData.WorldToScreen(position, height)`, driven by the camera
   matrix at `WorldData + 0x1A0`. Clip components are dots with COLUMNS of the flat array.
   Entity positions come from `Render.WorldPosition`; its `Z` is the entity's BASE, and
   `Z - ModelBounds.Z` is where the game floats the health bar.
2. **The in-game map** — no matrix at all. A fixed 38.7-degree isometric transform whose
   scale comes from the map UI element's own zoom and shift. See the block comment above
   `ImportantUiElements` in `schema/poe2.offsets.json` for the formula.

Markers from (1) will never line up with the markers the game draws in (2), because the map
is zoomable. Comparing them is what makes a correct projection look broken.

**The game's frame is LEFT-HANDED** — world and model files alike, with up as minus z. The map
draws grid x up-right and y up-left (x to y anticlockwise from above, so x × y = −z), and every
rig puts its `L_` bones at +x while facing −y. A renderer built from proper rotations draws that
frame as its mirror image, which looks entirely right until it is held against the game and
cannot be turned to match: a room drawn from the area's own tiles came out exactly that way.
`MeshPicture.Camera` turns x over once for every picture the tool draws.

## Verify against the game, not against yourself

A check that a wrong value passes is worse than no check. Prefer tests the game itself can
settle:

- Project an entity's health-bar height and see whether it lands on the bar the game drew.
  That is a pixel-accurate reference, supplied free, on every monster on screen.
- For the camera matrix, require the player to be centred **and** the rest of the scene to
  spread out proportionally (`MatrixHunt`). Centring alone is satisfied by any matrix that
  inflates `w`, which collapses the whole scene onto one point.
- Structural fingerprints (a unit-length row, a plausible pointer) are weak. Frustum planes,
  basis blocks and inverse transforms all look like matrices.

## Offsets are data

`schema/poe2.offsets.json` is the reverse-engineering knowledge of this project. Edit it,
hot-reload with `--watch`, no rebuild. Every field may declare an invariant; the drift report
runs them at attach time. Record WHY an offset is what it is, and its drift history — that is
the part that is expensive to rediscover.

## Record, then diagnose offline

`--record` captures every read into a small file that replays without the game. Recordings
are the reason offsets can be diagnosed from Linux, and committed ones under `tests/fixtures/`
are regression tests against real memory. A recording can only contain reads the running
build actually performed, so a new diagnostic needs a fresh recording.

**The capture (F9, "Capture for Diagnosis") is the hub for every diagnosis - probes, fixtures,
recordings, dumps, measurements - and it is grown, not worked around.** One key writes one zip
from one spot at one moment: the pictures, the recording, the area's files, every window's
diagnosis that is ticked on the Keys page (`CaptureParts.All`). That is the rule for new work:

- **Before asking for anything from the game, ask the capture.** A screenshot, a copied line, a
  dump pasted into chat is the slow road and the one that loses context. Ask for a capture from
  the right spot; if the question is one of "what does the game hold / draw / name here", a
  capture answers it or shows which part is missing.
- **A question the capture cannot answer is a new part, built BEFORE the question is asked.**
  Add the part to `CaptureParts.All` (key, group, label, file, hint - and what it must read of
  memory, `CaptureReads`), its writer in `CaptureReport`, its hook in `CaptureKey.Write`, on by
  default - the settings keep only the keys that are off, so an older settings file gains the new
  part unasked. Nothing goes on a window as a copy button or a new pane; the catalogue is the one
  place. Do this on your own initiative whenever a diagnosis needs data: the person at the game
  then presses one key, and the measurement is in the zip.
- **Every part has an offline reader.** A capture is also a fixture: the replay test
  (`CaptureReplayTests`, `POEF_CAPTURE=<folder>`) runs the tool's own arithmetic over the capture's
  files and writes its answers beside them, so a change to a finder, a rule or a parser is held
  against a real area here, without the game, before a build goes out. A new part that carries data
  worth re-running gets its reading there too, and a capture worth keeping goes under
  `tests/fixtures/`.
- **Print both halves.** A part that measures writes what the tool computed AND what it computed it
  from (the raw identities, the lines, the entities), so a disagreement can be settled from the file.

Its zip holds the pictures, every
room file of the area's room set and every doodad entity with its model and position, so the
room placing runs again here without the game -
`POEF_CAPTURE=<unpacked folder> dotnet test tests/PoEformance.Core.Tests --filter CaptureReplay`
writes `placing-replay.txt` beside the capture's files, and `search-replay.txt`: the
ground-and-tile search over the recording's terrain, each laid tile read from the capture's
`tile-identities.txt`, with every place the doodads found scored by the same yardstick and
ranked against the search's list - the measure of what the tiles alone can place. `slot-candidates.txt`
answers the other question, which tiles the area's tilesets OFFER for each kind of slot in every room
of the set: a room whose every kind one tile answers could be built without standing in its area,
and a kind several answer is where the generator chose. `orientation-replay.txt` is how it was
settled which side of a turned tile meets which side of a turned slot - 180 slots of Oasis's
placed rooms, all 64 readings tried, one of them missing nothing that is an orientation (see
`SlotWants`). `origin-replay.txt` settled what a big slot's 24th number is: which corner of its
footprint the record sits at (`RoomSlot.Origin`) - Oasis's boss arena is recorded at its LAST column
and line, and a rim tile's footprint reaches one tile past the room's grid. Every reader that took
the record for the footprint's first cell was wrong for three corners of four; the tile placing,
the build and the replays now go through `RoomSlot.Corner`. `build-replay.txt` builds each placed
room from the tileset alone (`RoomBuilder`, the Monster Book's "built from the tileset") and holds
it slot by slot against the tiles the game laid: a settled slot must come out the game's file, a
slot with the game's file its way or another way the slot's sides allow. `tileset-lists.txt`
carries the tile lists whole, weights and the words after each tile included. A list's weight is
optional and what the generator takes for a missing one is in its code, not a file;
`weight-replay.txt` estimates it from the game's own choices wherever written and unwritten weights
answer one slot, over any number of captures at once (`POEF_CAPTURES=<folder>:<folder>`, the
platform's path separator between). Whether a tagged tile such as `forced_blank` is drawn for a slot
not asking its tag is open - Stronghold lays it in most empty one-by-one slots, Steppe also in big
slots no draw by size could reach - so the replay prints both readings. Over eleven captures the
estimate for an unwritten weight is about 1 (best 0.7 to 1.4 by reading, 95% within 0.47 to 2.5;
10 and 100 far outside), while Steppe's samples, all of them `forced_blank` against `Blank` (300),
want about 75 - so Steppe measures the tag question, not the default. The build still takes 0 until
that is decided. Every area also LAYS TILE FILES NO LIST NAMES - one to twenty a capture: Sulphuric
Caverns' boss room lays `Thickwall/Fill_01` where its list names `Murky/Thickwall/Fill_01`, and an
`Abyss/Lava/Fill_02` beside the list's `Fill_01`, so 15 to 17 of its 39 settled slots do not come out the
game's file. A `.tmd` is NOT a list of variants, as was first thought: Marrow's are text, `version 8`
then a line a doodad (position, rotation, an `.ao`, an entity type such as `TileUpperDoodad`) - the
doodads a TILE brings, beside the room's own - and one sits beside the `.tdt` or beside its template
(`.tgt`). Marrow inherits nothing either. A master is a key-value file of any keys, and besides `TileSet`
and `RoomSet` it may name `FillTiles`, a `.gft` fill list (`FillList`, poe_data_tools' `gft`) - and
Marrow's five laid tiles that no tile list names are all in its fill list (v0.1.186): the plain
Cavepassage tile and four `mine_abyss_fill` tiles from its `Rooms/Fills` folder. Sulphuric's too (v0.1.188):
its fill list is sectioned by GROUND TYPE - `default` Blank, `BlackInsideWall` `Thickwall/Fill_01`,
`CoralAbyss` `Abyss/Lava/Fill_01` (50) and `Fill_02` (25, with the words `I FR270`) - and every one of the
boss room's fifteen slots asking for BlackInsideWall all round got the fill list's tile, while the tile
list's `Murky/Thickwall/Fill_01` is laid nowhere in the area. Augury (v0.1.188) lays its fill list's
`black.tdt` in 571 one-by-one slots asking BlackInsideWall all round that no tile of its list answers at all,
and Port its fill tile in all fifteen such slots. So THE BUILD FILLS A SLOT ASKING ONE GROUND AT EVERY CORNER
AND NO EDGE FROM THAT GROUND'S SECTION (`RoomBuilder.OneGround`), joined by the `.gt`'s name - the key a
section is written under, while a slot names the file; a master's part writes the area's ground types whole
from v0.1.190, before which only the OuterGroundType is joined offline. The build replay: Sulphuric's boss
room 39 of 39 settled slots the game's file (24 before), Augury's single-placed rooms 327 of 351 fill slots
(the rest on a rim a neighbouring room's walls cover), Port 15 of 15. A slot naming NOTHING is not filled so:
Augury lays fill-list tiles in most of those too (`dungeon_blank_fill`), but which section a bare slot draws
from is its own question, not yet measured. `tile-files.txt` writes for every listed, filled and laid file its chain, its doodad lists and its
flags, `area-masters.txt` every master and every file it names whole, and `tile-files-replay.txt`
counts what explains each laid file no tile list names (inherited, in a fill list, a shared template,
nothing) - read it before giving the build a rule for any of them. A tile list's words appeared for the
first time in Marrow (`I`, `R90`, `R180`, `R270`, on its cave entrance and its boss arena); poe_data_tools
reads them as strings and says nothing of what they mean, so what they do to a tile's placement is to
be measured on a laid one before the build obeys them. The first laid one, Augury's arena (`I`), lay as
authored (placement 3) in a room laid as written - one sample, which `I` as "identity" fits but does not
settle; a fill list carries words too (Sulphuric's `Fill_02`: `I FR270`). A tile's doodads ARE entities: Marrow's recording
holds 238 `TileUpperDoodad` and 6321 `DoodadNoBlocking` (the plain `Doodad` lines mostly not), and
`tile-doodads-replay.txt` puts every laid tile's doodads (`TileDoodadList`, its own grammar - the height
before the angle, one bool, four strings) into the area under all sixteen readings of how a tile's frame
reaches the world, against the unfiltered entities. The wall tops told them apart (v0.1.186), each in
the middle of one PIECE of a big tile: a tile's frame reaches the world as authored, then the game's
placement (262 tops found, the next reading 224). A top's list carries a line for every way it may
face, one place and two to six angles, and the game puts down only those facing the camera's side once
laid - read as the entities' own angles are (nought along -y, so the facing is (sin a, -cos a)), 180 to
270 degrees, no part along +x or +y: 514 of 514 lines, lone and grouped, mirrored tiles and turned
(`TileDoodadList.Shown`). Read as (cos a, sin a) the lines split too, but mirrored and turned tiles
want different facings, which is how the convention was told. A line's z is the entity's Render
`TerrainHeight` less its tile's level, exactly - not its z, which is the ground less some of the model, a
different amount per model at one place. Marrow's tiles were all at level nought; Sulphuric's (v0.1.188)
put 41 found doodads on tiles at -203, every one at its line's z plus the level (364 of 364, and 645 of
645 in Marrow). A tile may carry TWO lists - beside the `.tdt` and beside the template,
of overlapping doodads - and whether the game lays both
or one instead of the other waits for a laid tile carrying both; the replay counts the two apart. Augury
laid two such (its doors' `_open` and `_boss` variants): the doodads both lists share stand, and the lines
only the template's list carries are a `Door` and a `BreakableArchways` - server objects, whose absence from
the client proves nothing, so it is still open. A line
naming a set (`_set.ao`) stands in the game as one member of the set, so matching by model misses it:
7221 such lines found nothing, the seven plain ones all. A laid room's picture - an area's, a built one's
in the Monster Book - draws each piece's doodads so (`LaidRoomModels.TileDoodadsOf`): placed as measured,
not hung on the tile's mesh, whose sub-tile rows run down -y while the cells run down +y; turned, not
mirrored, since an entity carries an angle only; the height above the piece's own lift (the level, as
measured, or the fit to the ground where the piece is drawn fitted); and the `.tdt`'s list taken where
there are two - DECIDED, not measured, and the line under the picture says so. A ROOM NAMES ITS BOSS'S
PLACE: its `.arm` carries point-of-interest groups (poe_data_tools' `poi`: `x y turn "tag"`, read as
`RoomLayout.Points`), and the game stands a map's boss on the one tagged `mapboss`, `force:mapboss` or
`mapbossN`, at its cell's MIDDLE - `boss-points-replay.txt` found Marrow's, Steaming Springs' and Sulphuric's
second boss 0 units from it (8 at the cell's corner). So a stage sets the monster there, turned the
point's way, on the floor round it rather than at it, a throne's seat being at it (`LaidRoomModels.BossSpotOf`,
`StagedModels`). Which of a room's several boss places belongs to which boss is not read; the first is taken.
THE GROUND IS DRAWN WHITE, and what stands before a painted one is in `ground.txt` (`CaptureGround`): every
laid template's ground blocks by vertex format, the fields the reader steps over as bytes AND as halves; each
corner's ground by name with the master's MaterialsList group for it, the ground materials and their graphs;
and the install's shader sources naming the ground's coordinates and its blending, whole; and every laid
template whole, MaterialSlots and the triples' first numbers included, which `TileTemplate` reads past. Port
(v0.1.195) settled three things. Its ground blocks DO carry coordinates - 891 of 893 at format 0x238, small
(0.18..0.21 in one sub-tile), more like a place in the template's ground mask than a material's tiling; only
the blank tiles' (0x30) carry none. The materials' coordinates are the engine's, from the world position
(`internalonly.ffx`, `CreateProjectedCoords`): `uv = (world.xy + ground_scalemove_uv.zw) * ground_scalemove_uv.xy`,
a pass uniform no file holds - every area's environment carries `"ground_scale": 0.004` (one per tile) in the
block whose `dust_color` is that uniform's neighbour, which makes it the likely value and not yet a measured one.
And the white pier under Malgor IS the harbour tile's ground block (the probe, "ground of Port_Boss_01.tdt"),
whose corners name IslandOcean and IslandTownUnwalkableFill - black and cobblestone. The game draws the pier as
cobblestone with dirt between the stones (the in-game picture), which is IslandTownUnwalkableFill's one material,
`G_IHLT_CobblestoneDirt01`; the water round it is the tile's own doodads (31 `WaterPlane3x3` in its `.tmd`) over the
ocean's black. So the corners' MaterialsList does name what the pier is drawn with; what is open is how a tile of
two grounds is split between them - the engine's and the template's masks, up to four materials (`internalonly.ffx`,
`GroundGenerateWeights_*`) - and that is the next capture's. The blend's texture (`ground.ffx`, `user_blend`) is read
red for the engine's mask, through an atlas of eight placed by two per-tile transforms (`GenerateBlendUVs`, 512 the
atlas's size, x doubled), and green for the user's, through the block's own coordinates; and Port's three template
masks (`newmask.dds`, `mask_3x3.dds`, `mask_ground.dds`) are all 256x512 R8G8_UNORM - two channels, red and green,
which Pfim does not read and `GameArt` now does itself. Decoded (v0.1.199), the three reds are one picture - the
engine's atlas, four by eight cells of 64 pixels: curved edges, corners, two straight ramps, cloudy blends - while the
greens differ (`newmask` and `mask_ground` near the atlas again, `mask_3x3` blobs of its own). Which atlas cell a tile
gets is the engine's (its transforms are in no file); whether a tile of two grounds is split by red or by green is
what `ground.txt` now measures, each template's green read at its ground's own vertices and set against their height
and shape - green that follows height on Port's harbour (pier above, sea floor below) would mean it is split by its
own mask and can be painted from the files. "The game draws wood there" was written here once from
a picture not looked at closely: the gangway and the ship are wood, the pier is not - look before writing it down.
`ground-replay.txt` counts the cells: a tile of one ground all round covers 39% (Steaming Springs) to
77% (Port) of an area, but Port's harbour, the boss's floor, is one 315-cell tile of two grounds.
Six captures are committed under
`tests/fixtures/captures/` (its README says why each), and `CaptureFixtureTests` holds the orientation
rule, the origin rule, the settled slots, the fill rule, the tile doodads' frame and height and the one
laid word against them on every build. Two captures of one area forty
minutes apart are how it was learned that the power-line pieces and checkpoints come and go
with play while the plain props never move; hold a finder change against a capture before
asking for a screenshot. The reason they come and go is the network bubble: a scripted
object is the server's entity and the client holds it only while it is near, while the
props the client builds itself from the room files. A third capture had one checkpoint
entity in the client while the game's map drew four, and "there is one checkpoint in this
area" was nearly concluded from it. An entity's absence from the client says nothing. Its
presence does, so `DoodadMemory` keeps every scripted object the frame's read lists, by id,
for the rest of the instance, and the rooms are placed again when one arrives: a checkpoint
room is named once the player has been near its checkpoint, and stays named after.

**A room the doodads cannot place is placed by its tiles** (`RoomTileFinder`): Sinter Rift's boss,
its eight checkpoints and a titan overlay are rooms of big slots alone - no one-by-one slot, so no
corner stamp for the search, and one scripted object or none for the doodads - while a big slot's
tile is the room's fingerprint, `boss_01.tdt` laid once in the area. Two things were measured at the
boss's doodad place before anything was built on them: every big slot's cell carries the FIRST PIECE
of a tile alike to it (the slot's corner is the tile's corner), and 15 of its 17 big slots were alike
by the rule that asked the type sets to be equal, 17 of 17 with an unnamed type as any - so an
unnamed type in a slot is any, as nought is in the stamp (`SlotWants`). The first piece is the
lowest corner of the tile's LAID footprint whichever way it went - measured over three captures on
every laid file at every placement, oblong ones included - so a turned big slot's corner is the
lowest corner of its rectangle turned (`RoomFinder.CornerOf`), not its origin cell turned: that put
the StoneCircle two tiles off and a second checkpoint room inside the first, each "confirmed" by a
line that landed near enough. Rooms or ways round standing on one tile are drawn once under the
shared name ("entrance" for `entrance_01` to `entrance_04`): as the whole footprint they agree on,
or as the tile where they do not. A checkpoint entity that places of several footprints claim
confirms the nearest line's alone - the entrance's own checkpoint had confirmed a checkpoint room
inside the entrance. A fingerprint is a TAGGED tile laid once - a slot asking for a feature, answered by
one tile - because a ledge checkpoint's one untagged 3x3 ledge tile is laid ten times (116
candidates) and another ledge piece laid once stood beside the entrance; a place on anything else
counts only where a doodad of the room stands in it, the checkpoint itself once the player has
been near.

**The loaded-file list is not the area.** It names what was loaded since the area change, and
a file still cached from an earlier instance of the same map is not loaded again and not
listed: The Assembly's list carried ten of its fifty-three rooms, neither the boss room nor
the entrance among them, and for a while "the boss room is not placed" was read as a placing
fault. The area's room set (`generate.rs`) names every room the generator may lay, and
`AreaRoomSet` places all of them. A fresh instance's list carries the set; a re-entered
instance is not generated again and its list carried the master (`master.tsi`) instead,
whose `RoomSet` line names the set; The Stone Citadel's list carried neither, both cached
from an earlier visit. So three roads are read: the listed sets, the listed masters, and
the masters the install's index lists in the folder above the rooms' `Rooms/` - where 456
of RePoE's 458 map graphs keep theirs. A list that lacks a thing is not evidence the area
lacks it.

## Raise the version on every push

`ToolVersion.Number` goes up by one patch with **every** push — one edit, one file, no
exceptions. It is drawn on the overlay's own title bar beside the lock, so "which build is
that" is a question a screenshot answers.

This exists because it cost an hour. Three changes went out in a row, two of them missed a
squash merge by under a minute, and a boss that looked unchanged could not be told apart from
a boss whose build did not contain the fix — the only way to check was comparing a screenshot
against a merge timestamp. A number that moves with every push makes that a glance.

The build stamp (`BuildStamp`, `version.json` beside the exe) still says which COMMIT is
running and is written by the publish workflow; the version says what was meant to be in it.
Both are shown, because they fail differently and a local build has only the version.

## Style

- Layering is compiler-enforced: Core → Game → Features → Overlay/Config → App, with Gpu
  (Game → Gpu → Overlay) beside Features. Nothing reaches backwards.
- Comments explain WHY, especially where a subtlety cost time. Do not narrate what the code
  already says.
- Analyzers are on and warnings are errors in spirit: keep the build at zero warnings.
- Chat may be German; everything committed is English.
