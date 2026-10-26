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
  before theorising about why a tile or room looks wrong. Each probe click also puts its lines in
  the clipboard (asked for from the live client, v0.1.204) - the one copy the capture rule below
  allows, because it is the probe's own result and not a diagnosis kept out of the catalogue. Only the
  click's drawing is the processor's (v0.1.238): left on the processor while the probe stood on, a room
  with running water drew at five frames a second on every core, and a capture pressed then took ages.
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

**Every offset found goes into the schema in the push that finds it** (asked for from the live
client, 2026-10-11) - one a capture part measured as much as one the tool reads, and one not yet
named as a field with what it holds and why it is unnamed (`ParsedEnvironment.Colour3A0`). A
measurement that lives only in a capture's text or in this file is lost the moment that capture is.

**Before every commit, look at the repository as it stands** (asked for from the live client,
2026-10-11): fetch, then check main, the other branches and the open pull requests - other sessions
work on this repository at the same time.

## Record, then diagnose offline

`--record` captures every read into a small file that replays without the game. Recordings
are the reason offsets can be diagnosed from Linux, and committed ones under `tests/fixtures/`
are regression tests against real memory. A recording can only contain reads the running
build actually performed, so a new diagnostic needs a fresh recording.

**The capture (F9, "Capture for Diagnosis") is the hub for every diagnosis - probes, fixtures,
recordings, dumps, measurements - and it is grown, not worked around.** One key writes one zip
from one spot at one moment: the pictures, the recording, the area's files, every window's
diagnosis that is ticked on the Keys page (`CaptureParts.All`). A zip past 29 MB is written straight into pieces,
`name.zip.001`, `.002` and on, each under 29 MB (`CaptureReport.PieceBytes`, `PieceStream`) - the zip's own bytes in order, joined
again by concatenation (7-Zip opens the `.001`, `copy /b`, `cat`): asked for from the live client (2026-10-13), the place a capture is
sent to taking no file over 30 MB, and chosen there over several whole zips. From v0.1.260 a capture's files sit in a folder for
their heading, in the folder and the zip alike - `pictures`, `entities`, `area`, `rooms` (both room headings), `windows`, `mem` (asked
for from the live client, 2026-10-13); capture.txt stays at the top, and a part's pictures and dumps go where its file goes
(`CaptureParts.Folders`, `FolderOf`, `PathOf`). An offline reader opens a capture's file through `CaptureParts.Find`, which finds it at
the top - every earlier capture and every fixture - or in its folder. That is the rule for new work:

- **Debug lives in the capture, nowhere else** (asked for from the live client, 2026-10-11). A measurement
  the tool can make - a memory search, a dump, an evaluation of what a window shows - is a capture part run
  at the press: not a test written for one problem and kept in the test project, and not a button on a
  window the person must find first. Unit tests guard the code; they are not where a diagnosis is made. The
  light panel's memory hunt is the example: `light-hunt.txt` runs it at the press for the area's own
  environment, the water's evidence with it.

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
- **A part that runs long says so in the capture's ONE window** (`CaptureProgress`, asked for from the live
  client, v0.1.207): from the end of the hiding to the zip it lists every ticked part with where it stands,
  the long ones - the rooms, the memory hunt, the recording - with what they are doing and how far, and a
  summary once written. One window for every part, never one per diagnosis, and never the main window. A
  new long part gives its step a `Live` line and a `Fraction`, and a `Stop` that keeps what it found where
  it can be stopped. Whoever presses the key has chosen to spend the machine on it: a search runs on every
  core (`FloatHunt`), and writes where its time went by kind of memory beside what it found.
- **Every part has an offline reader.** A capture is also a fixture: the replay test
  (`CaptureReplayTests`, `POEF_CAPTURE=<folder>`) runs the tool's own arithmetic over the capture's
  files and writes its answers beside them, so a change to a finder, a rule or a parser is held
  against a real area here, without the game, before a build goes out. A new part that carries data
  worth re-running gets its reading there too, and a capture worth keeping goes under
  `tests/fixtures/`.
- **Print both halves.** A part that measures writes what the tool computed AND what it computed it
  from (the raw identities, the lines, the entities), so a disagreement can be settled from the file.
- **A picture's own lines are in it too.** `model-panes.txt` writes every model pane's lines under its
  picture - a room stage's own among them (`MonsterModel.StageMove`; before v0.1.202 a staged monster
  dropped them) - and every doodad file of the room shown with what became of its lines (laid, not drawn,
  capped, unseen), its box, where its first line puts it and its materials; a file that did not draw is
  written whole with what it extends. Built when Port's harbour stood without its water - 31
  `WaterPlane3x3.ao` lines in its `.tmd`, the probe over the water finding only the ocean's floor - and the
  stage said nothing of why. Its first capture answered at once: the planes loaded fine and all 48 were
  left out by the cap - the room comes to 2.25 million triangles, the ship alone 355,726 of the usual
  500,000 - so a Monster Book stage now lays every doodad and tile piece (`RoomModels.EveryDoodad`).
  Laid, the planes came out white, so the part writes the materials under the last probe click and those
  drawn plain whole too, with what each compiles to, what drawing it left out and its graphs
  (`CaptureMaterials`, shared with the ground's part). That capture (v0.1.204) said why: the planes stand where
  they should (the probe put them over the harbour's sunken bricks and the ocean's floor, under nothing), but
  `nvidian_Ocean.mat` is no ordinary material - `"shadertype":"Ocean"`, `"splashywater":true` - and its graph
  writes as albedo only a constant, (0.53, 0, 0, 0.608) off the minimap, a mark in the frame's buffers. Water is
  the engine's own pass (`common.ffx`: `PASS_TYPE_WATER`, `water_color_open`, `water_color_terrain`,
  `water_clarity` and more as pass uniforms; `WaterComputeSurface`, `WaterComputeRefraction`), while Port.env's
  water section names only its flow and a height offset. `water.txt` (`CaptureWater`) writes every
  environment's water keys and the water pass's sources whole - read it before the water is given a colour.
  Its first capture (v0.1.205): 313 of 861 environments write a water section, 28 keys, the colours among
  them as `open_water_color` and `terrain_water_color` with `open_water_murkiness` and `terrain_water_murkiness`
  beside them - and water.ffx takes `water_color_open.a` as the density, `exp(-distance * density)`. Port.env
  sets none, so Port's water is the engine's default, written in no file: `light-hunt.txt` (`LightHunt`, run at
  the press) looks for it in memory two ways - the water pass's uniform block as common.ffx declares it, the
  file's values in their slots and the rest read out by name; and the file's values in the window round the
  parsed environment's phi, printed whole. Its first full run (v0.1.208, 16 GB in 57 s - most of the threads' time
  on 4.4 GB of write-combined memory, the driver's upload heaps, read at 0.01 GB/s) found no uniform block as declared,
  but four copies of Port's parsed environment, every field a value and a flag - one where the file sets it, nought
  where the engine's default stands: flow_intensity at phi+0x218 and height_offset at +0x290 among the water's floats
  (defaults between them 0.01, 0.03, 1.33, 1, 0.13, 0, 1.06, 1, 0, 37.5, 0, 0.04), and between dust_color (+0x360) and
  ssf_color (+0x3B0) two colours Port's file does not set, (0.1, 0.19, 0.22) at +0x390 and (0.1015, 0.09106, 0.05037)
  at +0x3A0. Which is the open water's is NOT read from their order: `light-hunt.txt` maps every value the area's file
  sets to its slot (`LightHunt.Layout`), and `water.txt` lists every environment's water section file by file, so one
  capture in an area whose file sets the water's colours names the slots. Clearfell Encampment named it (v0.1.211):
  its file sets open_water_color, found at +0x390 with a one - so PORT'S OPEN WATER IS (0.1, 0.19, 0.22), the engine's
  default (`ParsedEnvironment.WaterOpenColor`). The Red Vale named its density (v0.1.212): open_water_murkiness at +0x228,
  Port's default 0.01, with reflectiveness 0.03 (+0x230), refraction_index 1.33 (+0x238) and subsurface_scattering 1
  (+0x240) beside it. That water_color_open.a IS the murkiness the shaders say themselves (v0.1.217's water.txt):
  legacy.ffx's water materials declare their medium colour `"R G B Density"` defaulting to `0.1 0.19 0.22 0.01` - the
  colour and murkiness measured - and hand it to water_color_open; a material's uniform, not the pass's, read.
  SO AN OCEAN MATERIAL IS NOW DRAWN AS THE AREA'S WATER (`ShadeProgram.Water`, `WaterLook`): a medium over the solid
  surface behind, covering by `1 - exp(-depth * murkiness)` along the view, its colour `pow(open_water_color, 2.2)` lit
  as any colour is - the area's file's values where it sets them, the measured defaults where it does not, handed in
  per drawing like the dust (two fixed registers, `WaterColour` and `WaterFlow`). MEASURED: the colour and the density.
  CHOSEN, and said under the picture: the moving pattern (value noise, a cell every 120 units, 0.85 to 1.15 of the
  colour, drifting at the flow) standing for the engine's waves; still water where a file sets no flow, the engine's
  flow default never having stood in a capture. The first capture of it (v0.1.217, Port's harbour staged) put the probe's
  water 583 units along the view over the ocean's black floor, and 94% of the water's pixels over 100: deep water at 0.01
  covers whole, so the harbour comes out one dark teal - as the formula says, the engine's too. TAKEN FROM THE PASS'S
  SOURCE (v0.1.219): the shore. WaterComputeRefraction thins the density by `saturate(gap * 0.03)` (`ShadeProgram.ShoreFade`)
  and discards the water at the floor; the thirtyfold its formula keeps for terrain water is nought on this path
  (`GetFlowingWaterPlanarStateWorld`). THE WAVES (v0.1.221, asked for from the live client): the water's glossy twin
  (`ShadeProgram.HasWaves`) hands the game light a normal of its own - GetFlowSurfaceField's eight octaves as written
  (`ShadeProgram.Ripples`: each turned half a radian, every pair twice as fine, fading over three seconds and sliding along
  the drift as it fades), its slope worked out exactly - and the light glints it (every light's GGX lobe, gloss 0.99, specular
  the area's `water.reflectiveness`, 0.03 where unset) and reflects the environment along it (the env's `specular_cube`,
  never loaded before, its level of 128 - `CubeMap.Read`'s `largest`), each by a switch in the light panel's "water" row;
  the colour and the diffuse light keep the mesh's normal, and the lamp draws the water as before. TAKEN FROM THE SOURCE:
  the octaves, the gloss, the specular. CHOSEN, and said under the picture: the ripple texture the octaves read
  (`surface_tex_sampler`, a pass texture the engine binds, named nowhere) stands as a texture of gradient noise made once,
  128 texels a side as the engine's, a cell every 16 texels (`ShadeProgram.RippleSheet`, `RippleCell`), and READ AS THE
  ENGINE READS ITS OWN from v0.1.225 - linear and wrapping, four texels a unit of the octave, the height `(r - 0.4) * 3` and
  the texels' slope mixed, not the mixed height's; the card loads it at t10, four loads an octave, where it hashed noise
  per pixel and vertex before, and the processor's `Ripples` went from 630 to 331 ns - its texels alone change when the
  engine's is found; the
  drift (the engine's flow map is made at run time) is the colour pattern's, (18, 12) a unit of flow. At Port's flow they
  lean 2.7 degrees at the median, 8.7 at most, so the sun glints only where its mirror image falls near - real glitter.
  Seen from the side the sea stood flat (asked for from the live client), and so does the engine's: its ocean planes are
  flat meshes, GetFlowingSurface lifts the surface per pixel only, and only the beach, crashing and flood waves move their
  vertices (`BreakingWavesVertex` and its kin) - the game's camera, always above, never shows it. So THE LIFT IS CHOSEN
  (v0.1.223, `SceneLight.WaterLift`, the light panel's "lift", one the engine's height, up to five): a room's water shapes
  are split to edges of 48 (`SkinnedMesh.Refined`, `RoomModels.Watered`, by the blend word "water") and their vertices
  lifted by `Ripples` times the lift - the card's `PlacedWaves`, the processor's `MeshPicture.Lifted`, which also keeps a
  lifted drawing from `Again`. THE SCENE IN THE WATER (v0.1.224, the water row's "scene"): the engine's own screen-space
  reflection, `CastScreenRay` as written (`ScreenReflection`) - the view turned about the waves' normal and kept above the
  water's own surface, marched in twenty steps of a fiftieth of the picture over the SOLID part's depth, a hit where a
  surface lies in front of the ray by under fifty units, its colour the solid part's (the engine's opaque framebuffer:
  the card copies the solid pass, `ModelTarget.Saw`; the processor draws every band's solid part before any band's water,
  `Drawing.Reflects`), the cube's share `pow((step + 0.5) / 20, 5)`. READINGS, said under the picture: the dither
  (`GetBayerNoise16` named, not read - a 4 by 4 Bayer) and the reflected colour's alpha as the cube's share
  (`ComputeEnvironmentLighting` unread); `water.txt` asks for both sources from v0.1.224. The reflected light is added
  after the exposure and grade, having been drawn with them. NOT ON THIS PATH, read from water.ffx (v0.1.228): the
  height offset - `water_height_offset` is added only by the beach, crashing and flood waves (`BreakingWavesVertex`,
  `CrashingWavesVertex`, `FloodWaveVertex`, their water depth and the beach foam's height fade), never on an ocean plane's,
  so Port's -15 moves nothing the harbour draws. NOT DONE: the terrain water colour, the refraction's bend, foam - and the waves' own look: the ripple
  texture's texels and the flow map that drive them are not read, so they look and move otherwise than the game's (said from the
  live client, v0.1.223). `water-textures.txt` (`CaptureWaterTextures`, v0.1.227) looks for the texture: every install
  texture whose path names the water (the startup walk keeps only those, `WalkPlace.Words`) with its size and format, those
  128 square marked, the likeliest decoded channel by channel beside it, and the game program's own strings naming one -
  read it before the stand-in's texels are replaced.
  THE OCEAN'S OWN PATH (v0.1.229). The octaves above are the RIVER'S: the engine draws water through two surfaces
  (internalonly.ffx), WaterComputeFlowingSurface (GetFlowingSurface, GetFlowSurfaceField - slide along the flow, fade
  back to the start every 1.4 to 3 s) and WaterComputeSurface (GetTotalWaveSurfaceNumerical over
  `GetOceanWaterPlanarState`), and at Port's flow the first read as "a two-second film looped" (said from the live
  client, v0.1.228) while the game draws clear crests rolling in to the harbour and spray along the quay. Which surface
  an "Ocean" material takes is in no file read - the names and the game's picture say the second. Its parts: a SWELL
  rolling towards the shore (`GetTidalWaterPlanarState`: a cross-section texture `wave_data_sampler` and its slope
  `wave_gradient_sampler` read at the tide's phase and the shore's distance, over a shoreline field
  `shoreline_coord_tex_sampler` the engine makes at run time - r one less the shore's distance, g the length along the
  shore, b the distance out to sea, a the strength, positive a beach and negative a rocky shore that takes
  `GetClashingWaterPlanarState` instead), the WIND'S RIPPLES on top (`GetWindRippleSurfaceField3`: three octaves on the
  ripple texture SCROLLING STEADILY - nothing fades or starts over - in `water_wind_direction`, `_intensity`, `_speed`
  and `water_directionness`), times `shallowRippleMult * foamRippleMult * 2`; the flow is read on the river's path alone.
  The spray is a particle effect outside the surface (`CrashingSprayTrigger` and its kin). Port's file sets none of the
  ocean's keys, so every one is the engine's default: CHOSEN until measured, the old full-screen tracer's constants the
  uniforms replaced (screenspacetracer.hlsl: wind intensity 0.1 x 4 x 4 / 1.5, speed 1, directionness 1, no wind matrix;
  swell height 37.5, period 0.04) - the parsed environment's unnamed water floats hold 1.06, 1, 37.5 and 0.04 among them
  (light-hunt.txt, v0.1.208), and a capture in an area whose file sets the keys (the Blank Sea and Yaochi hideouts set
  all of them) names their slots. DRAWN from v0.1.229: the ripples (`ShadeProgram.Ocean`, `WindRipples`, the
  `WaterWind` register from `WaterLook.WindRegister`), a wind way turned by the light panel's "wind"; at the former
  constants they stand -4.8 to 9.4 and lean 1.8 degrees at the median, 8.4 at most, at 118 ns a read. THE SWELL (v0.1.230,
  asked for from the live client: "approximate it, and measure once Port is captured"): `GetTidalWaterPlanarState` as
  written (`ShadeProgram.Ocean`) - the tide's phase from the distance out to sea, rolling in at 1.1 a second, the clock's
  smooth noise (the engine's Hash and GetSmoothNoise), slowed and hurried near the shore, the cross-section read at the phase
  and the shore's distance times `water_swell_height`, its slope the cross-section's along the phase's (the engine leaves
  the shore's own slope out - 22% of the differences on a bay), less over ground under water; the ripples thinned to
  nothing at the shoreline. CHOSEN, and said under the picture and in `model-panes.txt`: the SHORELINE FIELD (`ShoreField`,
  `MonsterModel.Shore`, made in `RoomModels.Piled` from the room's water shapes and its "ground of" shapes: a cell is land
  where the ground rises above the water over it; the shore's distance the grid's exact Euclidean distance, 2,000 units a
  colour unit so the 25 periods put crests 500 apart, the beach waves' own length; the distance out to sea one less; the
  shore's length nought and its strength one - all beach, no clashing state), and the CROSS-SECTION (`Swell`,
  `SwellSheet`, standing for `wave_data_sampler` and `wave_gradient_sampler`: a trochoid steepening from 0.2 to 0.8 and
  growing from half to whole height towards the shore). The card reads the field at t11 and the cross-section at t12 (program
  sheets from t13); `WaterShore` and `WaterSwell` are fixed registers. `model-panes.txt` writes each room's field - what it
  was made from and what came of it - and `shore.png` beside it; `water-textures.txt` also looks for shore, coast, swell,
  tide and distortion textures. NOT DRAWN: the swell's foam, its orbit carrying the ripples to and fro (the cross-section's
  blue, the distortion texture), the rocky shore's clashing state, the spray.
  THE ENGINE'S LOOKUPS (v0.1.231). Port's captures of v0.1.228 and v0.1.230 found them: the game's program names three -
  `Art/2DArt/Lookup/surface_ripple.dds` (128 square, one level, its header's code the NUMBER 116, D3DFMT_A32B32G32R32F,
  which the bytes' decoder could not read), `gerstner_wave_profile.dds` and `wave_profile_gradient.dds` - and the lookup
  folder holds `wave_profile.dds` and `gerstner_positive_wave_profile.dds` beside them (and `foam_shore_colour_dxt1.dds`
  elsewhere). The sources bind by sampler name alone, so which file is which is SETTLED BY THE FILES (`WaterLookups`): the
  ripple texture is taken where its green and blue go with its red's own differences a texel (correlation 0.8 either way
  round), and the swell's profile is whichever of the three whose red's differences along the phase go with the gradient's
  red - the others being the beach's. `FloatArt` reads the float formats (the old numbers 111 to 116, DXGI's float ones) as
  a card does; `ShadeProgram.Use` hands the taken ones over as the install opens, the card making its textures again on a
  change, the swell read at its own size (`GetDimensions`); where one is not taken the stand-in stays and the line under the
  picture says why. `water-textures.txt` reads every lookup the walk or the program names as floats, writes each whole
  beside it (`water-lookup-name.dds`) and what the water takes and why; `water-lookups-replay.txt` reads them again
  offline. Port's first `shore.png` (v0.1.230) drew the pier, the quay and the bay where the game lays them.
  MEASURED (v0.1.231, Port): `surface_ripple.dds`'s green and blue are its red's differences a texel, correlation 1 and
  scale 1 - TAKEN; `wave_profile.dds` goes with `wave_profile_gradient.dds` at correlation 1 and scale 1 along the phase
  (0.997 along the shore's distance) - TAKEN - while `gerstner_wave_profile.dds`, the one the program names, does not
  (0.03): the beach's. All four are fixtures (`tests/fixtures/water-lookups`, `WaterLookupsTests`). The profile is no
  trochoid: flat far out (its first rows), the swell standing whole about a quarter of a colour unit in, then breaking -
  flat again by the shore with its green, the trail foam, rising along the phase to one; its blue the orbit about 0.5, its
  alpha the front's phase from -1 to 2. THE FOAM (v0.1.233, asked for from the live client): `ShadeProgram.Foam`,
  GetTidalWaterPlanarState's foam and GetFoamColor3's trail as written - the cross-section's green times
  `saturate(gBufferDelta / 25)` (the depth the view crosses, the water program's own), three octaves of the engine's
  Voronoi noise (GetVoronoiNoise, Hash2), the shallows' `-(shoreDist - 0.18) * 10` - laid as InitWaterSurfaceData lays it
  (colour lerped to the foam's, what lies behind hidden by `1 - (1 - a)^3`, specular to 0.04, gloss to nought), and the
  ripples flattened under it (`foamRippleDampening`, `pow(trail, 3)`). Over a bay at the shore field's 2,000 units a colour
  unit the foam rolls in bands from 1,000 units out to 200, a fifth of the water covered (`THEFOAMRollsInBandsOnTheEnginesProfile`);
  where that falls in Port depends on the field's scale, which is CHOSEN (the engine's `dist_to_color` and the env's
  `shoreline_offset` and `shoreline_resolution` are unread). NOT DRAWN: the leading edge's textured layers
  (`water_tex_sampler` "for foam leading edge" and `distortion_sampler`, bound by the engine and named nowhere), the orbit's
  and the distortion's offsets, the rocky shore's clashing state and its GetFoamColor2, the spray (particles). A code block
  offered from elsewhere (2026-10-12) drew foam from a height threshold, `noise_worley_low.dds`, the walkable grid and a
  pulsing sine - none of it in the engine's source, which is in `water.txt` whole: read it there.
  THE ROCKS' SPRAY (v0.1.234, asked for from the live client: "the water and foam running down the rocks"). It is not the
  water pass's: Port's shore rocks (`ISL_Rock*.mat`) and the pier's bricks are instances of `OceanSprayFoam.fxgraph` beside their
  stone, whose node CrashingFoamSurface (effects.ffx) the compile left out - so the model panes' capture part writes every material
  naming a shore's node (`CapturePanes.ShoreNodes`) and each program texture's header (`CaptureMaterials`), and
  `materials-replay.txt` (`CaptureReplayTests.Materials`) compiles a capture's materials again from its own files. Now compiled
  (`ShadeProgram.Spraying`, `Op.Spray`, `ShadeHlsl.SprayLibrary`) as written, line for line against the source: a 1,000-unit wave
  rolling in at 200 a second off the distance out to sea (GetFieldWaveGeom's `wave_coord`, its advected and stationary noise the
  noise texture's green), the spray's phase and strength, a band of height (`saturate(0.8 + z / spray_height) * saturate((150 - z)
  / 100)`, the place lifted 75 by the graph and the band 350 high, within 200 of the shore), and the foam read three ways
  (GetTriplanarScrollingFoam) scrolling down the face and towards the shore as the spray peaks; its wetness darkens the stone and
  wets its gloss by the graph's own nodes. The node's three outputs are one sum (`SprayKey` on the processor, one variable on the
  card). hash33Stable is PCG's rounds over the float's bits, each a fraction by HashUToF (its top 23 bits) - utilitynodes.ffx's
  `DECLARATIONS hashes`, which the fragments include; util.hlsl's `float3(h) / ~0u` is the engine's .hlsl files' own (v0.1.234 to
  v0.1.241 divided, a difference under 2^-23; corrected v0.1.242, `BeachTests` holds three values worked out apart), and a review
  from elsewhere (2026-10-12) that called the rotation, frac, the sign bit and RgToGradYFix wrong was answered from the source:
  `mul(v, float2x2(c, s, -s, c))`, `x - floor(x)`, `asuint`, `abs(normal.z) > 1e-7`, all as written. The noise is
  `flow_muddle_blur_01_R16G16.dds`, two 16-bit channels, which `GameArt` now reads in every header spelling them (a texture that
  does not read leaves the whole material undrawn). CHOSEN: the shore field it reads (2,000 units a colour unit, as the swell's);
  the normal at length one. On the processor the foam's textures are read at their top level. NOT DRAWN: the foam's own normal
  (CombineWorldNormals is not compiled) and the screen's wetness (ReadPixelWetness, a pass of the engine's). MEASURED (v0.1.234's
  capture): the noise's header is DXGI 35, R16G16_UNORM, and all three read; the rocks' foam "looks fantastic" (said from the live
  client). WHAT STANDS IN THE SEA IS SHORE (v0.1.236, asked for from the live client: in the game the waves run on over the rocks,
  here through them): `ShoreField.Of` takes a third kind of land, CHOSEN as the shapes whose own program sprays (`ShadeProgram.Sprays`
  - the rocks and the pier's stones, not the ship, round whose hull the game breaks nothing), counted apart in `model-panes.txt`; the
  field is made again once a room's materials are compiled (`RoomModels.ShoreOf`, `MonsterModels.Shaded`), and a stage takes its
  tile's. The engine's own rocky shore - the field's negative strength, which takes GetClashingWaterPlanarState instead of the
  swell - reads `distortion_sampler` throughout (its offsets and GetFoamColor2's), a pass texture named nowhere, so it is NOT DRAWN
  and every shore stays a beach. THE PATCHES BETWEEN THE CRESTS (said from the live client, v0.1.234: "a water texture sticking out
  of the wave", sand-coloured shapes that move with the swell) are either the sea floor where a lifted trough drops below it (the
  water then dropped, the probe's fragment "dropped by its program") or the shore reflected off a wave's face by the screen ray -
  so the probe now writes, for every water pixel lit the game's way, where it stood, how far its waves lean and what its
  reflection met (`ProbeWater`, `PictureProbe.Watered`). Pause the pane on a patch, probe it, then capture.
  THE LIFTED SEA IS ONE SURFACE (v0.1.237). Port's capture of v0.1.236 (lift 5x, the probe on the water) crossed ONE water shape
  three times at one pixel - 550, 667 and 893 along the view, a crest standing in front of the trough behind it - and drew 667:
  a translucent shape blends once a pixel, the FIRST in the mesh, so wherever a plane's triangles ran from far to near the far
  crossing showed through the wave's face, near the shore the shallow trough's floor ("a water texture sticking out of the wave",
  sand-coloured patches with straight sides; said from the live client). The engine never meets it, lifting per pixel only, so it
  is DECIDED with the lift: the lifted water's triangles are binned apart (`MeshPicture.Binned`, the fourth kind) and drawn where
  the first of them comes among the translucent ones, as one surface across shapes - a walk for the nearest crossing in front of
  the solid and the triangle that made it (`Drawing.Nearest`, a tie to the first in the mesh), then each triangle drawn only where
  it won (`Drawing.Surface`; the probe says `Folded` for the rest). On the card (`ModelGpu.Surfaced`): a depth walk with no colour,
  the runs drawn at LessEqual once a pixel by the stencil, then the solid depth copied back (`ModelTarget.Back`) for the
  translucent runs after. Unlifted water blends as before. The probe's water line now says whether the sun reaches the pixel
  (`ProbeWater.Sun`): a straight edge where the waves seemed to stop beside the ship was either a seam where the drawing order
  flipped between two planes or the ship's shadow taking the glints - probe it on the new build to tell.
  THE ROCKS FLOODED (said from the live client, v0.1.238, with three in-game shots): by Port's pier a thick sheet of white foam
  runs in over the rocks in surges and floods them up to the sand, while the Monster Book's water ends where its planes meet the
  beach. That is not the planes' pass: water.ffx has three waves that move their own vertices over the shore - BreakingWavesVertex
  and FloodWaveVertex (both `engineonly`) and CrashingWavesVertex - over meshes the engine makes and textures it binds itself
  (`wave_shape_precomp_tex`, `wave_coord_noise_tex`, `foam_color_tex`, `foam_normal_tex`, `ripple_normal_tex`), the beach waves'
  constants in `beach_waves` (500 units a wave, 200 a second, the shoreline half a wave in) and their place from the shore field's
  world distances (`GetShoreFieldData`, divided by the pass's `dist_to_color`). Port's ocean material says only `"splashywater":true`;
  the program names the graphs, `BreakingOceanWaves.fxgraph` and `BreakingOceanWavesWetness.fxgraph`, and a debug graph for the
  flood waves' markup. So the water textures part writes them whole (v0.1.241, `CaptureWaves`): every graph the program names for
  them and every graph they name, the textures they name with their headers; `waves-replay.txt` parses each again offline, its
  nodes and which the compile knows. Read it before the beach waves are built: their mesh and `dist_to_color` stay unread.
  THE BEACH WAVES ARE DRAWN (v0.1.242, asked for from the live client: "the rocks are flooded in the game"). `ShadeProgram.Beaching`
  and `BeachLift` port beach_waves, BreakingWavesVertex and BreakingWavesFragment line for line (GetBeachWaveFront, BuildWavePhase,
  the foam's scrolling layers, profile, dissolve and overlay, the crest off the wave shape, the ripples), and `ShadeProgram.BeachOf`
  builds the program from the engine's graph itself (`BreakingOceanWaves.fxgraph`, a fixture under `tests/fixtures/beach-waves`): the
  three textures it binds to the fragment, the graph's mix (the foam covering `0.7 * a`, the film under it by its thickness, specular
  0.05, gloss `1 - roughness`). The wave shape and noise are fixed lookups (`WaterLookups.BeachProfilePath`, `BeachNoisePath`, slots
  t13 and t14, a material's sheets from t15): `gerstner_positive_wave_profile.dds`, 256 square, two floats a texel, a fixture; and
  `flow_muddle_blur_01_R16G16.dds`, 512 square R16G16_UNORM, which `FloatArt` now reads as sixteen-bit fractions and the water
  textures part writes whole. THE SHEET IS CHOSEN (said from the live client, 2026-10-12, between laying it flat at the water and on
  the ground - "auf den Boden legen"): `ShoreField.Beach`, a vertex at every shore cell's middle whose ground lies from 300 under the
  water to 100 above it, 4 over that ground, no square rising past 50; rocks and doodads are not ground and stand out of it; joined
  in `RoomModels.Piled` once (`BeachSource`), wearing the graph's path (`BeachMode`, mixed). It moves its vertices at the engine's
  own height whatever the light (`MeshPicture.Beached`, the card's `PlacedBeach`), the area's height offset added - Port's -15 moves
  it at last. IT IS DRAWN AS THE ENGINE'S BACKGROUND SURFACES ARE (`BackgroundGBufferSurfaces`, `CustomDepthTest`): after the solid
  part and before anything translucent, its nearest crossing alone, writing its depth (`Drawing.Laid`, `ModelGpu.Laid`) - so the sea
  measures its depth to it, a crest standing out of the water hides the sea behind it, and the screen ray reflects it; its normal is
  its surface's for all its light (`Beaches`, `ShadeBeach`), not the water's waves. CHOSEN beside the sheet: the shore's two
  distances one (`ocean_dist` and `shore_travel_dist` both the field's), and the travel distance half a wave less
  (`BeachTravelShift`) - MEASURED on a beach rising a tenth a unit, with the waterline as nought the crest stood tallest 240 units up
  the dry sand and the foam ran 300 to 600 in, against the game's foam line on the shore; shifted, the crest builds from 300 out to
  44 tall at the waterline, rolling in at about 170 a second, and the foam runs 100 to 500 up the sand and stops by 600
  (`BeachTests`). The film's colour is the area's water and its absorption one alpha, the graph's three channels' mean. NOT DRAWN:
  the refraction and caustics, the wet sand (`BreakingOceanWavesWetness.fxgraph`), the spray particles, the flood and crashing
  waves. On the processor a beach pixel costs about 5 microseconds and a vertex 1; the card is the way it is meant to be drawn.
  `model-panes.txt` writes each room's sheet - its triangles, box, band and lookups. THE PROBE ON THE BEACH (v0.1.243): v0.1.242's
  capture drew the waves, but its tally left the beach out - a picture that reflects itself draws the beach with its solid part, a
  phase nobody counted; now it is. Dark patches with straight, toothed edges lay on the sand behind the foam, which a picture cannot
  tell from the film over a deep gap, the lifted sea or a shadow: the probe now writes the beach's own line (`ProbeBeach`: its lift,
  the shore's distance, the foam, the film and its path) and, for every fragment the game light shaded, how much sun reached it and
  what cast the shadow (`ProbeSun`, one ray towards the sun on a click). Probe a patch, then capture.
  THE SHIP SWAYS, AND NOT BY ITS MODEL (v0.1.290, asked for from the live client). A real-time video measured Port's gangway 4 px up
  and down every 2.05 s (the hull 0.62 of that, in step, so a tilt with a heave; the amplitude comes and goes), while v0.1.289's capture
  wrote `boss_boat.ao` whole: a FixedMesh, no rig, no animation. Beside it the room lays `BoatnautWobbleCore_small.ao`, a two-triangle
  plane of the entity type `.../4_6/Objects/BoatWobbleCore`, whose entity carries a component no reference names, `WobblePoint` - and
  the room's doodads are of the types `Doodad` and `DoodadNoWobble`. Its one read held a turn of 0.0777 degrees about a line in the
  ground's plane (schema `WobblePoint`). `wobble.txt` (`WobbleProbe`, run at the press for 20 s) watches every WobblePoint and every
  component of the doodads by it, one hop beyond their pointers, with a DoodadNoWobble as the control; `wobble-files.txt`
  (`CaptureWobble`) writes the object types whole and the paths, sources and program strings naming a wobble; `wobble-replay.txt`
  (`WobbleSeries`) reads the watch back. Read them before the ship is moved: what sways it, by how much, about which point.

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
not hung on the tile's mesh, whose sub-tile rows run down -y while the cells run down +y; mirrored with
a mirrored piece (from v0.1.266 - turned alone before, on the reading that an entity carries an angle only,
which Port's mirrored boss room refuted: see THE MONSTER BOOK'S ARENA below); the height above the piece's own lift (the level, as
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
what `ground.txt` measures, each template's green read at its ground's own vertices and set against their height
and shape. It follows height (v0.1.200): the harbour r 0.78, its sea floor all 1 and its pier mostly under a half;
the seven ocean-edge walls r 1.00; the twelve coasts too. So two grounds are split by the template mask's green at the
block's own coordinates (`GroundGenerateWeights_1a1b`), and in all three of Port's families the ground at green nought
is the one the MaterialsList lists FIRST (Town fill, then Ocean, then Tropical - neither the names' order nor the
terrain's list fits) - one area's evidence, which another area's capture is to confirm. A laid or built room now
paints its ground so (`GroundPainter`): one ground in its group's heaviest choice, two mixed per pixel by the green,
three or four as the two most of their corners name, ties to the list's first, mixed as two (CHOSEN from the live client,
v0.1.267 - a stand-in: the engine mixes them with its own mask's red too, GroundGenerateWeights_2a1b, 3a1b and 2a2b, read
through per-tile transforms no file holds; left plain they drew as white squares along Port's quay, c14r12 under the
tarpaulin crate among them, where the game draws the quay's top as its neighbours'; the capture's ground-on-screen still
marks them as the engine's own and leaves them out of its comparison); the materials' coordinates the placed position times 0.004; the master given, or
`master.tsi` beside the room's `Rooms/`. The line under the picture says each of those decisions. TWO GROUNDS ARE MIXED
PER PIXEL AS THE ENGINE MIXES THEM (v0.1.244, `ShadeProgram.Grounds`, `MonsterModels.Paints.Grounds`, worn under a name of
their own, `GroundBlend`): the mesh keeps the block's own coordinates for the mask, the program reads its green there, works
the materials' coordinates out of the place (CreateProjectedCoords) and runs both grounds, mixing them by
`lerp(first, second, green)` (GroundGenerateWeights_1a1b). Until then a triangle took the second ground where its vertices'
green averaged a half, and Port's beach showed the ocean's black (`blackc.mat`) in patches with straight, toothed edges under
the thin sea and beach film (v0.1.242's probe on one, said from the live client). The split by triangle stays only where the two
cannot run as one program, and the line under the picture says why. NOT DRAWN, DECIDED from the live client: the engine's
height blending (GROUND_HEIGHT_BLENDING, which Port's PBRGroundBN.fxgraph turns on with EnableGroundHeightBlending: each weight
times `1 / (1 + (max_height - height) / 0.01)`, a material's height its texture's, blackc.mat's nought) - with it the
cobblestone would stand over the black almost everywhere but where the mask is wholly green. KEPT FOR WHEN IT IS NEEDED (asked
for from the live client, 2026-10-12): two grounds are to be mixed the way the engine mixes them, not split by triangle - so a
tile that still falls back to the split is a gap to close, and the height blending is the rest of the engine's mix, built into
the mixing step of `ShadeProgram.Grounds` (each ground's height beside its colour, as its graphs hand it - InputHeight) once a
picture asks for it. THE CHECKERBOARD (v0.1.246's ground-mix probe line, three captures of Port's harbour): mixed per pixel, the
boss's harbour tile came out in squares of black and cobblestone with straight, stepped sides - on the shallow floor and on a path
under the bridge alike - the dark square at green 1.000 and the light one beside it 0.004, in neighbouring sub-tiles of the one
tile. Height blending would not mend it (a weight of nought stays nought). ground.ffx's `first_ground` and `second_ground` are only
the material SLOTS 0 and 1 (`float4(1,0,0,0)`, `float4(0,1,0,0)`) and `invert_g_mask` is an object uniform, so which two materials
the engine binds to a piece of ground, and whether it inverts the green there, is in no shader - and three things in the files are
read by nothing: each ground shape's u16 before its box (poe_data_tools' ordinal; a sub-tile's ground is one to five shapes), the
tile mesh's unnamed header word and byte and its tail entries, and the tile definition past its corners (zao's side offsets,
sub-tile blocks and ground grid). `ground.txt` writes all three from v0.1.247 (`CaptureGround.Shapes`), each shape with the green
at its vertices and the mask's 64-texel cells it falls in, and draws the biggest masked templates from above,
`ground-shapes-N-name.png`: the shapes by ordinal, the green as the mix reads it, the height. Read it before the mix is given a
rule for which ground a piece of a tile gets. ITS FIRST CAPTURE ANSWERED (v0.1.247): the ordinal is only the shape's own index,
and the header's word the ground's shape count; but the tile definition carries, just before its `.tmd` reference, a GROUND
GRID - (Width + 1) x (Height + 1) words, each a `.gt` reference or 0xFFFFFFFF, a ground type at every SUB-TILE corner, row nought
at the template's least y (`TileDefinition.GroundGrid`, `CornersOf`; one such run and only one in all 69 laid 3x3 definitions,
the coast's ocean row by its deep sub-tiles). Port's harbour names two grounds at its corners and four in its strings (the ocean,
the town fill, the tropical fill, PortTownTrim), and its picture showed the checkerboard exactly where its land's ground shapes
carry coordinates laid across the mask a third a sub-tile - a plain projection, read through newmask.dds's atlas of transitions -
while its two-ground shapes point into one uniform cell each (green 0.02 or 1.00). So THE GROUND IS PAINTED SUB-TILE BY SUB-TILE
from v0.1.248 (`GroundPainter`, `GroundPiece`): each sub-tile's grounds from its own four corners in the grid, one ground drawn
plain with the world's coordinates (the engine's GroundGenerateWeights_1a reads no mask), two mixed as before; the tile's four
corners stand for every sub-tile only where a definition carries no grid. The harbour's own grid lay past that capture's 32 KB
cut of the bytes (41,526 past its corners); the cut is 64 KB from v0.1.248, and `ground.txt` writes every definition's grid by
name and each sub-tile's corners, the pictures a fourth panel of each sub-tile's grounds. Its capture (v0.1.248) drew the land
clean - c2r4 and c3r4, the checkerboard's sub-tiles, are tropical_fill at every corner - but left the PIER white: its sub-tiles'
corners are 0xFFFFFFFF in the grid, 24 sub-tiles naming no ground. `ground-grid-replay.txt` (`CaptureReplayTests.GroundGridReplay`,
from the recording's TileCornerData) answered what that is: the game holds the null slot of its ground-type list at exactly those
corners - the two maps alike, mirrored by the piece's laying. So FROM v0.1.250 A CORNER THE GRID LEAVES UNNAMED TAKES THE
MATERIALSLIST'S GROUP WITH NO NAME (Port's: Cobblestone01 to 03 and CobblestoneDirt01) - MEASURED that the game holds null there and
draws cobblestone; DECIDED that it draws that group; a tile's own four corners, standing in where there is no grid, still name nothing
when unnamed. "The game draws wood there" was written here once from
a picture not looked at closely: the gangway and the ship are wood, the pier is not - look before writing it down.
THE SLOPES' CHECKERBOARD (said from the live client with v0.1.250: "it comes from the ground textures"). In a sub-tile of two grounds
most of Port's shapes point into one cell of newmask.dds (green 0.02 or 1.00, one ground or the other), but its slopes carry
coordinates laid across the mask a third a sub-tile, and the mix reads the atlas's 64-texel cells there as squares - v0.1.248's
ground.txt: the shore strip c1r6 to c6r6, c4r2, c5r2, c6r2 to c6r5, c14r12 and c15r12, and from v0.1.250 the pier's edge c13r10,
c13r11 and c14r11. The engine reads the green at `uv2` alike (GetUserMask), and which coordinate fills `uv2` is wired in its own
code (OutputUserBlendUvs takes an `in float2 uv` the shader text does not name), so what the game draws there is to be read off its
picture: `ground-on-screen.png` (`CaptureGroundScreen`, v0.1.251) lays every sub-tile of two grounds on the screen over the game's
picture where the Monster Book has it (`LaidRoomModels.GroundsOver`, the room's own placing), named as ground.txt names it, its
shapes across the atlas drawn magenta, and `ground-on-screen.txt` gives each sub-tile's grid corners and grounds beside the game's
own corner types there, and each shape's height, green and reach across the mask. It has no offline reader: the meshes are the
install's, which a capture does not carry. v0.1.251 counted nought in five areas: it painted from the first master of the install's
whole index (`CaptureKey`'s masters are the index, not the area's), a list of another tileset that named none of the laid grounds.
From v0.1.252 it takes, of the area's masters (`AreaRoomSet.Masters`), the one whose MaterialsList names the most of the ground types
at the laid pieces' corners (`CaptureGroundScreen.MasterOf`) - not the first even of those: Act 4's areas list
`worldmaps/act4/master.tsi` first, whose list holds the unnamed group alone - and says every master's count and, per piece, how many
sub-tiles came to one ground, several, or none and why. Those captures' ground.txt (Whakapanu Island, The Excavation, Plunder's
Point) name where to measure: The Excavation's `Digsite_Entrance_01` has 18 sub-tiles of two grounds with shapes across the atlas and
its ledges 4 each, Whakapanu's `TropicalCliff_Cv_Cove_01` 22; Plunder's Point's shores none.
THE EXCAVATION MEASURED (v0.1.255, the player on `Digsite_Entrance_01`'s piece 7, 17): 45 sub-tiles of two grounds on the screen, 17 with
shapes across the atlas - every one of them the entrance's flat floor at the template's height nought, none a slope (the cliffs' shapes span
21 x 21 texels, inside one cell). Their coordinates are the mask laid FLAT over the tile: u up 0.2 and v up 1/7 a sub-tile exactly (c8r1
u 0..0.2, c9r1 0.2..0.4, c10r1 0.4..0.6), the whole of `mask_ground.dds` over five by seven sub-tiles, while its green is an atlas of four by
eight 64-texel cells with hard edges between them - so the mix reads squares about 312 by 219 units there. The game draws even dirt under
all 17 (`G_PCE_DirtExcavated` and `G_PCE_DirtExcavatedEdge01`, the unnamed group and IslandCliffInnerTrim, or IslandInnerCliff's gritty dirt),
no squares; the dirt grows grittier towards the earth bank in an uneven band. Two dirts need not show their squares, so it is not yet settled:
from v0.1.256 the part TINTS each wide shape by the green the mix reads at every pixel (blue the first ground, red the second, half seen
through, the coordinates interpolated over the clip w as the card's are) and says how many of its pixels are the second ground's - whether
the game's ground changes along the tint's edges is the measurement. That capture had the game's picture switched off; the player had not
moved since the capture before it, so its picture was laid under by hand. Keep `game-picture` ticked for this part.
ITS FIRST TINT (v0.1.256, 18 wide shapes, 905,001 pixels tinted) laid the mix's squares plainly over the floor - "our checkerboard" (said
from the live client) - and the game's picture changes nowhere along them: across the 11,896 pixels of the tint's red and blue edges the
game's picture is 2.4 of 255 lighter on the red side, against 0.6 +/- 0.4 with the same edges moved elsewhere, and no warmer at all. So the
game draws no squares there; whether that says it does not read the mask there waits on the two dirts looking different, and the Tile Book
cannot show it - it draws a lone tile's ground plain (its "drawn as" says so), only a laid room in the Monster Book paints it. From
v0.1.257 the ground part writes each ground material's colour texture beside ground.txt (`ground-material-N-name.png`, the sRGB textures
its program reads, at most 256 a side, the mean colour and alpha said) - any capture in the area carries them.
THEY LOOK ALIKE, SO THE EXCAVATION CANNOT SETTLE IT (v0.1.257's ground.txt): DirtExcavated01 to 03 average (107..110, 94..97,
79..82), DirtExcavatedEdge01 (105, 92, 78), DirtExcavatedGritty01 to 03 (108..112, 94..98, 80..83) - within five of 255 of each other,
apart only in their fine pattern (scratches, light blotches, grit) - and the edge test above weighed brightness and warmth, the very
things they share. Squares of these would hardly show whatever the engine reads there. DECIDED from the live client (2026-10-13): the
measurement waits for Port, whose two grounds (the ocean's black and the town fill's cobblestone) cannot be mistaken - a capture at
the harbour with `game-picture`, `ground-screen` and `ground` ticked. Whakapanu's cove (`TropicalCliff_Cv_Cove_01`, wet sand against
the rocky sea bed) differs too, but lies mostly under water and reads green near one on most of its wide shapes.
PORT'S FIRST TINT (v0.1.257) MEASURED NOTHING: the harbour was drawn across its own footprint. The part's block
(`LaidRoomModels.GroundsOver`) chose how the files lie by the heights of its sloped pieces (the coasts), and a way turning the
15 by 21 file across its footprint drops a third of the points the misses are SUMMED over, so `(y, x)` won - every harbour
sub-tile on the screen stood where its neighbour across the diagonal belongs and read that tile's corners, and the tint lay
over the pier's cobblestone. Laid as the game's turn decodes it - as filed, then placement 1's mirror in x, the Monster Book's
own room (29 of 29 pieces where the index has them) - all ten of its sub-tiles on the screen meet the game's corner types
exactly, one of them ocean on one side and nothing on the other. v0.1.261 then let the area's index choose the way, taking a tile's
index for the file's column and row - but EVERY TILE OF A PIECE NAMES ITS PLACE IN THE LAID FOOTPRINT, counted from its least corner,
whichever way the piece was laid (v0.1.248's ground-grid replay: every turned piece of Port, the harbour among them), so the index
says nothing of how a file turns; it chose the way undoing the commonest placement, and the next harbour capture (v0.1.261) drew
the harbour wrong again, 131 of its 315 sub-tiles on the game's corner types. FROM v0.1.262 THE GAME'S OWN CORNER TYPES CHOOSE THE
WAY BEFORE THE HEIGHTS (`LaidRoomModels.Weighed`, `Way.Met`, `CornersMeet`: the ways that lay the most sub-tiles' four grid corners
on TileCornerData's types at the area's corners they reach; the heights only among those), in a laid room and the block alike, and
the line under the picture says what the corners left and the next way's count. The ground-on-screen part writes how the files lie
and counts, piece by piece, the sub-tiles whose corners meet the game's (the same `CornersMeet`) - read that count before the tint:
short of all, the placing is wrong.
THE HARBOUR MEASURED (v0.1.262, the player on the quay at Port_Boss_01's piece 1, 8): 504 of 504 sub-tiles on the game's corner
types, the files as filed (next mirrored, 280). The quay's land side (c13r9 to c14r11, the unnamed group's cobblestone and
TropicalFill's dry sand, wide shapes at z 24 to 176) is where the two grounds cannot be mistaken: the sand's colour textures average
106 to 109 in brightness, the cobblestones' 85 to 98, so squares of the mix would put the blue side of every tint edge some ten levels
darker in the game's picture (lit at about 0.6). Across 10,953 pairs six pixels apart over the tint's edges the game's picture is
+1.4 brighter on the blue side, against -0.2 +/- 1.3 with the same edges moved, and no more textured (+0.17 against -0.04 +/- 0.47);
the green's correlation with the picture's local contrast is -0.14 against -0.12 +/- 0.05 moved. THE GAME DRAWS NO SQUARES WHERE
THE MIX READS THE ATLAS'S CELLS - in two areas now - but cobblestones scattered through sand, thinning and thickening smoothly
(height blending's look, over a weight that is not the green at the block's coordinates). What the engine reads there is not
measured; the coast's and the wall's tints (under the surf; a wall's face drawn over flat ground) settle nothing.
THE TWO CANDIDATES HELD AGAINST THE PICTURE (v0.1.264). Nothing in a vertex weighs the mix: format 0x238's 24 bytes are all read
(position, normal and tangent, the mask's coordinates), and all 66 of Port's templates say `MaterialSlots 0`. But the engine has a
second way to mix two grounds, GroundGenerateWeights_2a (ground.ffx, internalonly.ffx, whole in ground.txt): the ENGINE mask's red,
read from the world position through two per-tile transforms no file holds into an atlas of transitions - and the game's own corner
types laid on v0.1.262's picture put the cobblestone at the cobblestone corners and the sand at the sand corners, thinning between.
So ground-on-screen now holds two fields against the picture (`CaptureGroundCompare`): every sub-tile of two grounds rasterized over
the block's whole ground (the nearest ground a pixel, a depth buffer), the mix's green at the block's coordinates and the second
ground's share bilinear from the sub-tile's four corners (`SubTileGrounds.Seconds`), each correlated with the picture's brightness
and local contrast - per pair of grounds, across and within sub-tiles, against the same fields moved 85 to 120 pixels - and
`ground-on-screen-corners.png` tinted by the corners' share. Neither field is the engine's transition; which one the picture
follows is what to read before the mix is changed.
THE MONSTER BOOK'S ARENA IS BUILT, AND LAID AS WRITTEN (`BuiltRoomModels`, said from the live client: "our harbour diorama is never
the game's, always mirrored somehow"). The game lays the boss room each instance its own way - Port's build replays: turned 180 (the
harbour at placement 0), turned 90 and mirrored (placement 4); v0.1.262's instance laid the harbour at placement 1, mirrored - while
the built stage lays it as the `.arm` writes it (placement 3). A mirror is not turned away by any camera. So the Monster Book has
a switch for a built stage, DECIDED from the live client over laying it as the area's own (2026-10-13): "mirrored" (`RoomKey.Mirrored`,
`BuiltRoomModels.MirroredTurn`) turns the built grid over in x - each piece where its mirror image lies, laid the way a mirror sends it
(`TileOrientation.MirroredInX`: as filed comes to placement 1, as the game put the harbour), its tiles counting their places from the
piece's least corner again (`TerrainTiles.MirroredInX`; the sizes from the pool AND the fill list, `BuiltRoomModels.SizesOf` - v0.1.265
took the pool's alone, so Port's 3x3 `Building_Fill_01` to `03` read as one tile each and 56 pieces of the town round the harbour were
left out as not whole) - and lays the room as written, mirrored. A MIRRORED ROOM'S DOODADS ARE MIRRORED WITH IT, MEASURED (v0.1.265's
capture, Port's boss room laid as written, mirrored): the game holds every doodad entity of it at MINUS its line's angle
(Render.RotationCurrent, 187 of 187 matched to their lines) and carries no mirror beside the angle in Render (+0x1C4 is -1 on every
entity) - yet the ship, `boss_boat.ao` at angle nought, lays its gangway on the quay the mirror moved to its other side, and the gangway
is the ship's own mesh (neither the room's lines nor the harbour tile's `.tmd` name one). So the angle is only the turning half; the game
draws the mirror image (`LaidRoomModels.Through`, the laying whole). v0.1.265 had turned them instead, fearing a mirrored mesh would
draw inside out - neither drawing culls by winding, and mirrored tile pieces always drew whole - and the ship put its gangway into the
water ("most passengers won't fancy that", said from the live client). A TILE'S DOODADS IN A MIRRORED PIECE ARE MIRRORED TOO, DECIDED
from the live client (2026-10-13) on that evidence: the same kinds of entity, held at the same laid facing (`TileDoodadList.Shown`); not
measured on a tile's doodad, and the line under the picture says so. The boss stays turned - the server's monster, not a piece of the room.
The room's mirror ALSO put Port's rock formation where the game has it ("for the first time 1:1 with the game", said from the live client).
`ground-replay.txt` counts the cells: a tile of one ground all round covers 39% (Steaming Springs) to
77% (Port) of an area, but Port's harbour, the boss's floor, is one 315-cell tile of two grounds.
Eight captures are committed under
`tests/fixtures/captures/` (its README says why each), and `CaptureFixtureTests` holds the orientation
rule, the origin rule, the settled slots, the fill rule, the tile doodads' frame and height, the one
laid word, the room lattice, the joins' edge combinations, the overlays' suppressed props and the projectiles'
grid against them on every build. Two captures of one area forty
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
lacks it. And a fourth: a master's `Overlays` list (`.toy`, `OverlayList`) names rooms no set does -
the overlays, laid in a pass of their own - so it is read too, and a loaded `.toy` likewise.

**The generator's own rules are in the master, and a talk said where to look.** GGG's procedural-generation
talk (Rhys Abraham, ExileCon, known here through a third party's notes - a lead, not a source) has rooms laid
on a grid overlapping by one tile, their keys merged there by rules, and overlays laid over the made layout by
matching shapes. Every master names the numbers and files for that - `RoomUnitSize`, `RoomOverlap`,
`OuterBufferSize`, `EdgeCombination` (`.ecf`), `Overlays` (`.toy`), `MergeInsideWalls` - and nothing read them.
Measured on the committed captures (`lattice-replay.txt`, `joins-replay.txt`, `overlays-replay.txt`, pinned in
`CaptureFixtureTests`):
- **THE LATTICE** (`RoomLattice`). A room's dimension line is its master's module - Augury "5 1", poe_data_tools'
  unnamed `side_length` and `uint1` (`RoomLayout.ModuleSide`) - and all 548 rooms of the six captures are the
  overlap plus whole steps of (side - overlap) each way. A room of the master's module stands at
  `OuterBufferSize + k x (side - overlap)` both ways: Augury 29 of 29 places with their lines within ten units,
  Marrow's and Sulphuric's bosses; the grid comes out whole (Augury 53 = 4 + 11 x 4 + 1 + 4). The places off it
  were the weaker twin of a place on it, 25 to 152 units from their lines. So the doodads, the tiles and the
  search place such a room on its lattice alone - a step squared fewer candidates for the search. A fill or league
  room ("3 0", "1 0", "3 1") lies anywhere and is not held. Oasis, Steppe and Steaming Springs predate
  `area-masters.txt`: their module rooms sit on one residue, 1, of their own step, the buffer unread.
- **THE JOINS.** Where two of Augury's placed rooms lay their rims on one row and both slots name
  `dungeon_thick_wall` on a side, the game laid what `combination.ecf` makes of the two - nothing (an opening) or
  `dungeon_stairs` - on 168 of 184 sides, 84 each, mostly one or the other a whole join; the boss room's joins
  with the lever rooms keep the wall (12), one join lays a ledge (4). What picks the opening or the stairs is NOT
  in either slot's edge pair (all 1/3) nor the corner heights (all nought); the `.ecf`'s own number (none for the
  opening, 1 for the stairs) is unread. This is what `RoomMisses`' openings and caps were found from a screenshot.
  Nothing builds with it yet - `RoomBuilder` builds a room alone, and a join is two rooms'.
- **THE OVERLAYS.** Augury's two both say `+SuppressUnderlyingDoodads`, and every one of the 32 prop lines of a
  placed room with no entity stands where an overlay's every slot fits the laid tiles - 879 present and none
  missing anywhere else; 24 of the 32 on a tile that is not what the room's own slot asks. Excusing them would
  place none of Augury's thirteen unplaced rooms (their props are missing everywhere - not laid), so the placing
  is unchanged; it explains the rings on placed rooms.
- **THE SECOND GRID** (`GridLandscapeData`) IS THE PROJECTILES' GRID - measured, not taken from ExileCore's PoE1
  naming (LayerMelee, LayerRanged) or the talk's "tall geometry". The capture part `landscape.txt`
  (`CaptureParts.Landscape`, `TerrainLayers`, `CaptureLandscape`; `LandscapeReplay` reads it back) was asked for in two
  Act 2 areas reached by a waypoint, Traitor's Passage (walls and chasms) and the Titan Grotto (walkways over an abyss),
  both now fixtures (`THESECONDGridIsWhereProjectilesStop`). Along the edge of where anybody can stand a wall is nought
  in it on every cell (2875 of 2875) and a chasm five (395 of 395; 3413 of 3413 over the Grotto's abyss); no walkable
  cell is ever nought. And both grids are the same kind of value: nought where they stop a body or a projectile, then
  counting up from the noughts to a cap of five - the walkable grid's 1 to 5 are a distance from the nearest unwalkable
  cell, not a flag. Whether a diagonal step counts as one is not settled.
  `TerrainReader` reads it beside the walkable grid (`TerrainGrid.CanShootOver`, `IsClearShot`, `IsShotOnly`; the walkable
  grid's length or nothing, `ShotNote` says why), and two things read it (v0.1.218): the rule fact `MonsterCountInShot`,
  `MonsterCountInSight`'s line over the projectiles' grid - beside it, not instead of it, so every rule written keeps its
  meaning; the aim and `IsTargetable` take each line fact's own grid - and the map sheet `ShotLayer`, the difference
  between the two grids (`TerrainOutline.Shots`). The sheet is DRAWN AT THE HEIGHT OF THE NEAREST WALKABLE GROUND: a
  chasm's own height is its walkable neighbour's at the edge and falls away inside - the Grotto's abyss up to 85 cells of
  displacement below the paths - so at its own height, or flat, the sheet sat tens of cells off the outline; and it is
  GATHERED per pixel rather than scattered per cell, since a cell pushed by a lift twenty cells from its neighbour's
  tore black wedges into the picture. `THEPROJECTILESheetMeetsTheFloorItBorders` pins it against both captures. Over an
  abyss nearly the whole map's rectangle is such ground (the Grotto: 7.6 million cells to 336 thousand walkable), so the
  sheet takes a REACH (v0.1.226, the page's slider, `shotsReach`, world units, nought no limit and the default): only what
  lies within it of the floor as drawn - a chamfer distance over the lift's blocks, three a block across and four corner to
  corner. At 1000 the Grotto's sheet is a band round its paths, 286 thousand pixels of 1.9 million.

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
