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
  And the pane draws a material's channels as the engine's own debug views do (v0.1.319, the light panel's
  "shows" row, `PictureView`, names as the game's program lists them): albedo, specular, gloss, roughness, the
  normal two ways - written unlit, unexposed and ungraded, through RGBToSRGB as debug.ffx writes them - and the
  light alone over a quarter grey. Ask it whether a texture or a light is wrong before theorising about either.
  The water's layer is left out of a channel view (CHOSEN: the engine draws water in a pass of its own).
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

The same lines carry a whole TURN, read past for a year: after the angle come four numbers, a unit quaternion x y z w, and the
angle is only its turn about the vertical, `2 atan2(z, w)` - on every flat line of every capture held against it (Port, Augury,
Marrow, Oasis, Steaming Springs, Steppe, Sulphuric, the Grotto, Traitor's Passage), while one line in ten or so also LEANS. A room's
`.arm` from version 18 and a tile's `.tmd` alike. Port's harbour ladder showed it (v0.1.321, said from the live client with the game's
picture beside the Monster Book's: the ladder stood up into the sky, a sack lay level in the air, carts floated): its `.tmd` line leans
54 degrees, and drawn through `Matrix4x4.CreateFromQuaternion` and then the tile's turn, its box lies along the ladder the game drew
against the barrels (`ATILESDoodadLeansByItsQuaternionWhereTheGameDrewIt`); the tilt reversed lies on the floor. `RoomDoodad.Rotation`
holds it and `Turning` draws with it, the angle alone where a line carries no quaternion of unit length.

## The two screen-space systems

The game projects to the screen in **two independent ways**, and mixing them up looks
exactly like a bug in the other one:

1. **The 3D world** — `WorldData.WorldToScreen(position, height)`, driven by the camera
   matrix at `WorldData + 0x1A0`. Clip components are dots with COLUMNS of the flat array.
   Entity positions come from `Render.WorldPosition`; its `Z` is the entity's base LESS
   `ModelBounds.Z` - the middle of a box from the base up (MEASURED v0.1.328 on Malgor, the player,
   checkpoints and ground items: z plus the bound is the terrain under them) - and
   `Z - ModelBounds.Z`, the box's top, is where the game floats the health bar.
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

## The model pane's camera

Every model pane (Monster, Tile, Item and Effect Book) is looked at through one `CameraPose` (v0.1.324, asked for from the live
client: navigating Port's stage was poor and zooming close impossible): a FOCUS it looks at and turns about, a turn and a tilt -
`MeshPicture.Camera`'s own angles, so the parallel pose fitted to the box with no pan IS the old camera, pinned by
`APARALLELPoseFittedToTheBoxIsTheOldCamera` - a distance, and perspective or parallel (the edge button; perspective by default).
MEASURED off the game's camera matrix (Port's capture of v0.1.319): thirty degrees from the picture's top to its bottom
(`CameraPose.FieldOfView`), and the game's view 48.67 degrees down along +x +y with +x -y to the right, 2,165 units off the player
(`GameTurn`, `GameTilt`, `GameDistance`, the "game view" button). CHOSEN and said in the code: the near plane (a hundredth of the
distance, 0.05 to 5), the closest the eye comes (2 units - a barrel fills the picture), the keys' pace (four fifths of the
distance a second, shift four times), rooms, tiles and stages starting at the game's angle and a monster as the last one was. The
gestures: left drag turns about the focus, right drag pans, the wheel stretches the camera about the surface under the pointer
(`MeshPicture.Pick`, a ray through every triangle on every core), a double click looks at the point under it from where the eye
stands (`LookingAt`), W/S A/D Q/E fly eye and focus together while the pointer is over the picture, shift + left drag the sun.
A capture's `model-panes.txt` writes the pose and what it comes to (`CapturePanes.Camera`).
THE GAME VIEW LOOKS AT A STAGED MONSTER AS THE GAME'S CAMERA LOOKS AT THE PLAYER (v0.1.373; said from the live client: "when I click game
view the monster is not at the right coordinates but always a few metres off"). Two causes: the area round you stood the monster at its
block's middle, 75 units from the player in The Red Vale's capture of v0.1.372 - now where the player stood when the block was taken (the
key's `stood=` word, `AreaBlock.Stood`, which the stage's cache leaves out - `AreaBlock.Unstood` - so taking it again inside the block moves
him and lays no tile again); and the button turned about whatever the pane looked at. MEASURED over 20 captures in 5 areas, each from
capture.txt's camera matrix and player line: the game's camera looks at the ground (-37 to -41, -34 to -39) units from the player's feet in
x and y, median (-38.2, -37.7) - about 54 towards the camera - from 2,150 to 2,166 units off it; so the button looks at a staged monster's
feet that much ahead (`CameraPose.GameAhead`, `AsTheGameAt`), the eye within a hundredth of the distance of the game's in every capture held.
FROM v0.1.375 THE WORD CARRIES HIS GROUND'S HEIGHT TOO (`stood=x,y,z`, his Render TerrainHeight - the tile levels' frame the block's picture
keeps, LaidRoomModels' measured relation), and the monster stands on the floor nearest it of the nine asked round the place; a key from before
stands him on the floor at the place itself, the ring's middle only where the place finds none (`StagedModels.Staged`; a review found the
ring's median, a boss place's rule, put him on the river bed beside a footbridge and on the wall tops in a doorway). A roof wider than the
ring still covers all nine.

PERSPECTIVE REACHES EVERY PLACE THE PARALLEL VIEW WAS ASSUMED, and each keeps the parallel arithmetic to the bit:
- The processor (`MeshPicture.Drawing.Edged`) finds each pixel's weights as the point its ray meets - `r · (v1 × v2)` and its
  kin over `r · n`, sums of the column and row like the parallel edge functions - so every coordinate is perspective-correct and
  no triangle is cut: the near plane is a test per pixel, and `Reached` boxes the part past it. A texture's level stays one per
  triangle (the finest through the near plane); the card's per block of pixels is the picture.
- The card's depth is REVERSED (one nearest, `Greater`), so a float keeps its precision from the near plane to the horizon; in
  perspective it is `near / z`, read back by `BehindAt`.
- The game light, the water and the probes take the way from the pixel to the eye (`EyeWay`, the card's `SeeFrom`), and a shade
  program's ViewDir, DepthDistance and MaskedContactFade the point's own ray (`ShadeProgram.Viewer`, a fixed register; the
  engine's own definitions, `length(bottom - camera) - length(world_pos - camera)`, which a parallel view had reduced to depths).
- The water's screen ray is the engine's march in perspective (`ScreenReflection.MarchSeen`: InitScreenRaymarchContext's
  projected start and end, the depths' reciprocals in step, the thickness projected at the start - water.ffx, read in water.txt).
- The floor's lines come from the real eye (`ModelFloor`), and the sun's mark stays in front of it.

## Who skins a monster

THE GRAPHICS CARD SKINS A PLAYING MONSTER ITSELF (v0.1.325, asked for from the live client, which first asked whether the processor
was needed at all and decided to keep it for what checks the card): the pane hands it the bones' matrices once a pose (`CardSkin`,
`ModelScene.Skinned`; on a stage conjugated by his place, `SkeletonPose.SkinAt`) and the vertex shader runs `SkeletonPose.Move`'s
weighted mean (`ModelShaders`' `Skinned`, bones and weights a byte each from the mesh's own arrays at t126/t127, the matrices in
`Bones`, b3). Before, every frame of an animation skinned tens of thousands of vertices on every core and sent them to the card.
THE PROCESSOR KEEPS what reads the posed vertices, and poses them only when one asks (`MonsterPortrait.CpuPosed`): its own picture
(the probe, the grey, a shader still compiling - and every card test's reference), the pick under the pointer, the export. The
lowest point the "planted" notice reads is `SkeletonPose.Lowest`, the z alone to the bit, five times a second while playing (CHOSEN).
THE SUN'S MAP OF A SKINNED POSE is laid over a hull, not the posed vertices nobody on the processor holds (DECIDED from the live
client): each bone's box of the vertices it moves, carried by the bone (`PoseHull`) - looser, never tighter (MEASURED on a real rig:
the map's side 1.24 times the pose's at the median, 1.54 at most; boxes turned to each bone's spread barely better). The processor
lays its map over the same hull (`MeshPicture.Canvas.Hull`), so both still draw one shadow; on a stage the stage's frame holds him
and nothing changes. `CardSkinTests` and `SkinnedGpuTests` hold it all against Move on `blackguard-v6.one.ast`.

## Textures go to the card as the file's blocks

A BLOCK-COMPRESSED TEXTURE WITH EVERY LEVEL IS KEPT AS THE FILE HOLDS IT (v0.1.326, asked for from the live client): `BlockTexture` reads
the DDS's header and where each level lies, `Mipmaps.Kept` keeps it undecoded, and the card is handed those levels as BC1, BC2, BC3, BC4,
BC5 or BC7 where it samples the format (`ModelGpu`'s `Blocked`; a card below feature level 11 reads no BC7 and is handed decoded bytes).
MEASURED in Port's capture of v0.1.319: 800 million texels at the top level, 4.3 GB decoded with levels and 0.8 GB as blocks; decoding
was 25 of the build's thread-seconds and the levels 8 more, now none. THE FILE'S OWN LEVELS, NOT BOX AVERAGES OF THE TOP (DECIDED from
the live client): the card can only be handed what the file carries, and the processor's picture is the card's reference, so both read
them - the top level decodes as before, the smaller ones are the game's own. A cube, an array, a volume or a file short of any level
is decoded at load with levels built as before (CHOSEN: no picture coarser than it was). THE PROCESSOR DECODES BEHIND THE LOAD (DECIDED
from the live client): `MonsterPortrait.UnpackBehind` decodes every texture of the model shown on every core but the frame's once it
landed, stopped when the model goes; a processor picture asked for meanwhile - the probe, the grey, a shader still compiling, no card -
waits with a line saying how far it is, rather than decoding on the frame's thread. A capture's build lines say how many textures were
kept as blocks and how long the unpacking took. BlockArt now reads BC2 to BC5 itself: BC2 and BC4 to Pfim's byte, BC3's alpha and BC5
as the card rounds them (Pfim truncates; a level apart at most). `BlockArtTests`, `BlockTextureTests` and `BlockGpuTests` hold it.

## A staged boss against the game

MALGOR ON PORT'S STAGE (said from the live client with two pictures, v0.1.326): he floats above the deck in every animation and above
his wooden throne in `emerge_idle_01`; the spyglass he holds in his left hand in the game is not drawn at all; and his ship's wheel sits
at the bottom of his body whatever he does. Nothing settled where any of it comes from - the captures held neither his own files nor
his entity - so `monster-files.txt` (`CaptureMonsterFiles`, v0.1.327, the Windows group) writes the Monster Book's monster as the tool
builds and stands him: the place the stage put him (`StagedModels.Staged`, `MonsterModel.Standing`: the rest's lowest point set on the
middle of the floors found at his place and eight points 120 round it), each part with its socket and box (`PartFit`), his lowest point
and root bone in every animation, every `.ao` and object type of him whole (`ModelDump.Of`, `.ot`/`.otc` chains) - and the game's own
entity of him where one is near: its place, the terrain's height under it, its bounds, the animation it plays. No offline reader: the
files it writes are the reading. Read it, from a capture standing where the game shows him with him on the Monster Book's stage,
before the placing rule, a part's socket or a missing part is changed.
WHAT THE GAME SHOWS OF THE TWO PARTS (said from the live client after a Port map, with pictures): the spyglass is in his hand
THROUGHOUT, fired as a cannon in some skills; the wheel hangs ROUND HIS NECK throughout, on his throne before the fight too (first
said "in his second phase", corrected from a picture). From v0.1.328 the part also writes each socketed part's bone in every animation -
its place and least axis scale, a bone scaled to nothing hiding its part - and plays every part with a rig of its own by that rig
(`MonsterModels.Piece`): its mesh's box and size per animation, whether he plays one of that name, and for a `"<root>"` piece his bone
nearest its middle.
ITS FIRST CAPTURE (v0.1.328, Port, the player at the arena's door, Malgor on his throne) SETTLED ALL THREE, and v0.1.329 mends them:
- HE FLOATED BY HIS BIND POSE: the stage set the rest's lowest point on the floor, and his bind pose hangs 209.736 under his origin, so
  in idle_01 his lowest point stood 128 over the deck and in emerge_idle_01 he sat 209.7 over his throne. The game stands his ORIGIN on
  the ground - MEASURED: his entity's Render z -479.419 is the terrain under him, -335.938, less his bounds z 143.481, which is exactly
  half the 286.962 his rest reaches over his origin (schema `CurrentWorldPosition`, `CharacterModelBounds`). So every staged monster's
  origin is set on the floor (`StagedModels.Staged`, DECIDED from the live client for all monsters; a monster whose feet are its origin
  stands as before).
- THE SPYGLASS IS `PirateBossCannon.fmt` at `aux_cannon_jntBnd`, a version 8 fixed mesh the reader refused ("geometry outside a DOLm
  block"). `FixedMesh.Older` reads it as poe_data_tools' v8_section does; the vertex's eight unnamed bytes are taken as the normal where
  their first or second four lie along the file's own triangles, else the normals are the triangles' (`FixedMesh.NormalsFrom`).
- THE WHEEL IS `PirateBossHelm.ao`: 41 animations of its own, 30 of them his names, each putting the wheel's root on his
  `aux_helm_jntBnd`, 1.7 to 5.3 units off. So a `"<root>"` piece whose rig shares only the root and whose animations carry his names
  RIDES his rig (`AnimationSkeleton.Carrying`, `Riders`, `MonsterModels.Riding`, `PartKind.Played`; DECIDED from the live client over
  hanging it rigidly on the bone): its bones after his, its own animation of each name joined in step (`AnimationSkeleton.Played`,
  `AnimationTracks.With`) - by the seconds of each one's rate, CHOSEN (attack_01 is 84 frames for him, 168 for the wheel); where it has
  no animation of the name it stands at its bind. Every loader of a monster's animation reads `Played`.
V0.1.330 DREW ALL THREE AS THE GAME DOES (said from the live client with pictures and two videos): his origin on the deck and on his
throne, the spyglass in his left hand, the wheel round his neck in every animation, `emerge_idle_01` included. In `wheel_spin_01` his
own hand turns the wheel round his neck in step with it - the first evidence from the game that joining a rider's animation by each
one's rate (CHOSEN above) holds.
VOLKHAR ON CRIMSON SHORES' STAGE (fishery_boss, said from the live client with two pictures and a capture, v0.1.343): the arena floor
white with red bands, the rocks pink, dark red domes along its back, the water pale, and a dark rectangle in the sea. Five causes, each
read from the capture, v0.1.344 mends them:
- THE HOLE WAS A PIECE THE SEARCH BROKE (`TerrainTiles.Pieces`): two `deep_fill_01` laid the same way side by side, the room's block
  starting at the second; the flood fill took the first's last column (names 2, 0..2) for the second's, drew it a tile off and left its
  own last column out as not whole. A piece is now the tiles naming one least corner (place less name - MEASURED: v0.1.248's Port, every
  placement of this room). Over eleven captures the old search split pieces in up to 412 of 624 blocks (Titan Grotto; Port 221 of 403),
  the new in none - every laid room whose block starts inside a piece had holes or shifted tiles.
- THE MASTER'S TileMaterialOverrides (`material_overrides.tmo`) reached a lone tile only: a laid or built room now puts them on its tiles
  (`TileModels.Apart`) and on the tiles' own `.tmd` doodads (`MonsterModels.Swapped`) - the room's own lines too from v0.1.373 (see THE
  SCATTER WEARS THE MASTER'S SWAPS below) - the room's own doodads as they were (DECIDED from
  the live client: the file names `CorruptionGradientc.mat`, the tendrils of a tile's `.tmd`). Crimson Shores' swaps its rocky edges and
  stromatolites to the dark Vastiri ones and the tendrils to the mask nothing draws. PORT'S SEA WENT WITH IT (said from the live client
  with a picture, v0.1.348: no water, a pale floor): Port's own list swaps `nvidian_Ocean.mat` for `Water01c_water1.mat` -
  `"shadertype":"water1"`, its one graph `CrashingOceanWaves.fxgraph` - which compiled through its graph drew white on the harbour's
  48 planes. So from v0.1.349 `water1` is the ocean as `Ocean` is (`ShadeProgram.IsOcean`, `CrashingOceanShader`): the material the game
  draws there, drawn as the planes were before the swap reached them, a look held against the game's (v0.1.307).
- THE DOMES ARE THE ARENA'S TEN FORCE WALLS (`BossForceFieldDoorVisuals`, twilight_strand's force_wall, colour 0.155, 0.012, 0.012), whose
  materials end in `Radial_PlayerMask.fxgraph`: shown within 500 units times their scale of the local player. The compile knew neither
  LocalPlayerPosition nor ModelTransformDecompose; now the player stands at `ShadeProgram.NoPlayer` (CHOSEN from the live client: no player
  on a stage) and the transform's scale is one, so the walls show nowhere, as in the game far from the player.
- THE WHITE GROUND: StromatoliteDeep and StromatoliteShallow, the sea floor along the shore, are in no group of the area's `materials.mtd`
  (106 sub-tiles), and the arena's stromatolite tiles carry no ground grid and name no ground at their own corners (18). Both now take
  the group with no name (`GroundPainter.SetOf`): the unlisted grounds CHOSEN until measured, the own corners DECIDED from the live
  client. And two groups offering the same materials at the same weights draw as one - seven of the list's eight are the same three sands.
  `ground-on-screen.txt`'s choices hold a sub-tile of an unlisted ground against EVERY material of the list (`SubTileGrounds.Everything`):
  a capture with the shore's sea floor on the game's picture says what the game draws there.

## Every room at once

THE STAGE SWEEP (v0.1.346, asked for from the live client after Crimson Shores: "not every scene perfect, but the basic building
blocks should not need mending by hand each time"). Each of that stage's five causes was a gap in a building block every scene shares,
found because one scene showed it. `stage-sweep.txt` (`CaptureStageSweep`) builds EVERY ROOM OF EVERY ROOM SET in the install, each
under every master naming its set (a room under another master wears other grounds and swaps), as the Monster Book builds a stage from
its tileset (`BuiltRoomModels.Built`, every doodad laid), and counts what fails over all of them, each cause ranked by the rooms it
reaches - so one mend is found once for every scene. DECIDED from the live client: every room of every set; the building blocks are no
white surfaces, the geometry whole, nothing drawn the game shows only at times, the water there and lit; it may run overnight.
- A CAUSE is text under its group's word - `room`, `white`, `ground`, `geometry`, `visible`, `water`, `partly` (a colour drawn with part
  of its graphs left out), `lost` (what the compile lost besides the colour) - its paths kept to their kind and its counts to a hash
  (`CaptureStageSweep.Normal`), weighed in triangles, sub-tiles or doodad lines; the compile's reasons are ranked again by node alone.
  Every room is a row after `=== every room`, and `stage-sweep-replay.txt` ranks the rows again offline (`CaptureStageSweep.Report`).
- A BUILD'S NUMBERS ARE KEPT AS NUMBERS (`RoomAccount`, `MonsterModel.Account`): pieces laid, not whole, bare, capped and unturned, the
  swaps, the ground painted and left plain by why, the grounds no MaterialsList names, the slots unfilled and unmet - the line under the
  picture says them as sentences, which a sum over thousands of rooms cannot read back. And the compile now says what it lost of a
  material it still drew (`ShadeCompile.Lost`: the colour's alpha, a normal or gloss that did not compile), which nothing said before.
- ONE PAINT CACHE FOR EVERY ROOM, lean (`MonsterModels.Paints.Lean`: a texture only asked whether it reads, drawn as one texel): a
  material compiled once in the whole sweep; the graphs a cache has read kept as they come, not gathered on each ask (`Paints.Shaders`);
  the files read kept to a gigabyte, the oldest dropped first, and a quarter of the cores' rooms built at once - both CHOSEN.
- IT WAS FEARED TO RUN FOR HOURS, so it is the FIRST PART OFF UNTIL TICKED (`CapturePart.OffByDefault`, DECIDED from the live client):
  the settings keep such parts switched ON apart (`captureOn`, `CaptureParts.OffOf`, `KeptOn`), so a later one starts off too and an
  untouched settings file gains no key. Its step in the capture's window says how many rooms are built and which, and its stop keeps
  what it built. MEASURED (the first sweep, v0.1.346, Crimson Shores): 16,787 rooms under 526 masters in 6.9 minutes.
NEXT, DECIDED from the live client: once a sweep is in, its causes are mended from the top - the most rooms first - asking only where
the files do not settle a rule.
THE FIRST SWEEP'S TOP CAUSES (v0.1.346; rooms reached): slots laid with a side unmet 10,483 (the build's own fallback); ground left plain
because the template names no mask that reads 5,329; doodad sets (`_set.ao`) not drawn 4,242, other objects with no mesh 3,104; grounds
no MaterialsList names 2,661 for `BlackInsideWall` alone; `.smd` "no level of detail to read" 1,007; rivers (`FlowingWater`) drawn as an
ordinary material 1,120; "nothing to colour it with" 648; "more than 192 registers in the colour" 555 white and 582 partly. Mended in
v0.1.348:
- THE REGISTERS: the compile handed every value a register of its own for the whole program; now a register nobody reads any more
  is handed on (`ShadeProgram.Finish`'s remarks), so a long graph fits - `ALONGChainHandsItsRegistersOnAndKeepsItsColour`.
- EVERY CAUSE NAMES ITS FILES (asked for from the live client: the mends need the files, and only the capture carries them; the
  patch CDN keeps no old version and the patch server that names the current one does not answer through this environment's proxy).
  Under each cause the ranking writes its subjects - the material, the object and the mesh it failed on, the tile with its template
  and mask, the ground type file, the room - met in the most rooms first (`CaptureStageSweep.Subjects`). The first ten causes of each
  group write their first three subjects' files beside the part (`stage-sweep/` at their install paths, `Samples`) with what those name
  in their own bytes, twice over (`Named`: an object's meshes and what it extends, a material's graphs, a template's mask), to 80 MB,
  textures past a mask's size left out and listed. `stage-sweep-replay.txt` reads those files again with today's readers
  (`CaptureStageSweep.Recheck`: each object built, each material compiled, each tile read), so a mend is held against the very files
  offline before the next sweep.
THE SECOND SWEEP (v0.1.348, Crimson Shores) took the rooms with a white surface from 3,043 to 2,491. Its fourth white cause, 291 rooms,
was Port's own sea: `Water01c_water1.mat`, `"shadertype":"water1"`, drawn as the ocean from v0.1.349 (above). Mended in v0.1.350, each
CHOSEN from the live client:
- A DOODAD SET DRAWS ONE MEMBER A LINE (`RoomModels.Member`): a `_set.ao` is `extends "Metadata/AOSetParent"` and an `AOSet` whose every
  `ao = "<weight> <path>"` is a member (`WallSand_Cc_set.ao`: three walls at 100 each). The game stands one member at each line; which is
  its seed's, in no file, so the tool takes one by its weight through a hash of where the line stands - the same room draws the same
  choice every time; a member that is a set again is chosen from in turn. 145,687 lines in 4,242 rooms drew nothing before.
  `random_rotation`, `random_flip` and `random_scale_min` in the same `AOSet` are not read.
- TWO GROUNDS WHOSE TEMPLATE NAMES NO MASK ARE MIXED BY THEIR SUB-TILE'S CORNERS (`GroundPainter`, `Plan.Maskless`): the stand-in the shapes
  across the mask's atlas take already (v0.1.310), the second ground's share of the four corners, bilinear. The engine mixes them by its
  own mask through per-tile transforms no file holds (GroundGenerateWeights_2a). A tile with no ground grid has no sub-tile corners and
  stays plain, said why. 5,329 rooms left them plain before.
STILL OPEN from that sweep, ranked: rivers and the other water shader types drawn as ordinary materials (`FlowingWater` 1,120 rooms,
`NonsplashyWater` 254, `NonsplashyFlowingWater` 188, `Swamp_Water` 5) - DECIDED from the live client: the engine's river surface first
(GetFlowingWaterPlanarStateWorld, its flow map made at run time); `LowerPrisonWaterc.mat` (318 rooms, "nothing to colour it with": an
empty default graph and no shader type); `BlackInsideWall` named in no MaterialsList (CHOSEN, the group with no name).
THE RIVER AND STANDING WATER ARE THE ENGINE'S WATER (v0.1.351, `ShadeProgram.KindOf`, `WaterKind`, `ShadeProgram.Water(kind)`; DECIDED
from the live client). The pass has two surfaces (internalonly.ffx): the ocean's and WaterComputeFlowingSurface, GetFlowingSurface over
GetFlowingWaterPlanarStateWorld (water.ffx, whole in `water.txt`). `FlowingWater` and `NonsplashyFlowingWater` take the second;
`NonsplashyWater` and `Swamp_Water` the ocean's surface over no shore - the wind's ripples twice over, no swell, beach or field waves (a
reading: the program holds `NonsplashyOcean`, and nothing splashes on a pond). Each is the same medium, lit as the ocean is
(SceneLight.ShadeWater), under a blend word of its own (`MonsterModels.Paints.RiverMode`, `StillMode`) - mixed, but the room's shore, the
lift and the field waves are the ocean's alone (`OceanMode`; `ShadeProgram.Lifts`, the card's `Sea`); every kind casts its wetness.
THE RIVER IS DRAWN AT NO FLOW: GetFlowSample reads `flowmap_tex_sampler` over `river_flowmap_planar_aabb`, made at run time by
`RiverMarkupField(IDevice*, const PointLine*, const MarkupFlowConstraints*, ...)` or its twin over a grid of bools, inputs no file of the
118,851 water-shore.txt searched names - so the sample is nought throughout, DECIDED from the live client. Then, as written: GetFlowSurfaceField's
eight octaves on the ripple texture (`ShadeProgram.River`), each pair fading in and out in place, every 3 s and 1.3 times quicker each pair
(the scroll along the flow nought); its foam GetFlowingFoamColor's one node at 0.2 a second (`RiverFoam`, the strength
`saturate(0.015 * flow_foam + 0.25)` - `water.flow_foam`, default 1 MEASURED, the flow register's w, the card's lift moved to its depth
constant's w), reading the engine's distortion texture (`muddle_double.dds`, the caustics', bound for a river too) four times a layer;
`RiverTests` hold both against water.ffx's statements with HLSL's float2x2s. NOT DRAWN: the flow itself, the bubbles and turbulence it
makes, the refraction's own ground-water ratio. THE FLOW MAP IS SOUGHT: `ocean-markup.txt` now lists every box inside the scene with
whole corners beside the scene-sized ones ("inner", `ShoreFieldHunt.Inner`) and lays grids at the flow resolutions' multiple 7 too -
press it in an area with a river (The Riverbank's `arena_crossing_fastflow`, act 1 - which cannot be revisited, see below) before the flow is given a rule.
THE RED VALE ANSWERED WHERE, NOT WHAT (v0.1.351's capture, the player on `RedvaleRiver` tiles; schema `WaterPassBlock`). Beside the scene's
size the processor keeps a block of the water pass's numbers, 25 copies, the same layout as Crimson Shores' - and four boxes in a row where
common.ffx declares fog_flowmap, guiding_fog, river_flowmap and shoreline_coord_planar_aabb. The Red Vale's river slot holds (-16.30, -16.30)
to (14983.69, 15961.96), 276 x 294 texels; Crimson Shores', which has no river, the empty (0, 0) to (1, 1), its fog slot a box of the whole
scene instead. Every texture there is 54.3478 units a texel, 1250 / 23 - five cells - MEASURED in both; the shoreline box is the scene a quarter
tile wider all round in both; and 0.0004 stands where common.ffx's order puts dist_to_color (a READING, against the tool's CHOSEN 0.0005).
The map's texels are not beside it, and no shader of the install builds a markup field, so the processor likely makes them. SO THE NEXT
PART LOOKS FOR THEM (v0.1.352, asked for from the live client: "keep measuring"): `river-flow.txt` (`RiverFlowHunt`, run at the press, the
Memory group) finds the block again by its shoreline box, makes each of its boxes a grid, and sweeps the whole of memory on every core for
8-byte pairs whose difference is such a grid's length (texels, corners or cells, 1 to 16 bytes a texel - a std::vector's begin and end)
and for the grid's size as two ints with the pointers beside them; every list found is read, set out every way its texels may be read
(bytes, halves, floats, one to four channels: range, noughts, how alike neighbours are), the likeliest drawn as a map of the way it points,
and its bytes written beside the report (`river-flow-N.bin`) - `river-flow-replay.txt` sets them out again offline. Press it in The Red Vale
on the river before the flow is given a rule.
THE FLOW MAP IS FOUND (v0.1.352's capture, The Red Vale, the player on the river; schema `RiverFlowField`, `RiverFlowOwner`). Its list 1 is
flowmap_tex_sampler's contents, every channel as water.ffx reads it: four floats a texel, rows along x from the box's least y; x and y the
flow in world units a second (the engine's times 2 x water_flow_intensity, capped at 100 - median 50, at most 357), z the sources and sinks
(18 texels: the river's head and its end), w the distance to the bank, positive in the water - an exact Euclidean distance over whole texels,
the bank itself minus nought - and the flow exactly where w >= 0. One river, nearly free of divergence, slower by its banks. It hangs in a
holder (a std::vector at +0x00, the grid's size at +0x40) that an object holds beside the grid's size again, and the guiding fog's field -
list 2, laid out the same over 98% of the grid at 3 a second - hangs in a twin whose owner points at the river's. The map is made at run time;
no file read so far names its inputs (the object scripts name only `SetWaterBlocking`). SO IT IS TO BE READ LIVE (DECIDED from the live
client), where the Monster Book's stage is the area's own as the area laid it; every other stage keeps no flow. For that `river-flow.txt`
now names every list laid out as a flow field (`RiverFlowHunt.FlowMap`, its share of the grid telling the river's from the fog's; the replay
too) and seeks each one's WAY HOME (`RiverFlowHunt.Home`, v0.1.353): writable memory searched again level by level for pointers at most
0x800 before the holder, then before those places, up to four levels, ending at a static of the program or the area instance; each place
named by the nearest vtable before it through MSVC's run-time type information (`ClassOf`), and every chain written as the reads that
follow it. CHOSEN: the reach, the levels, 30 s a level, 32 places a level. Both sweeps now leave out memory the game cannot write and the
driver's write-combined and uncached heaps, and say how much. Press it on The Red Vale's river again before the live read is built.
ITS FIRST WAY HOME (v0.1.353's capture, the same game session as v0.1.352's - the same addresses; schema `RiverFlowRoot`, `RiverFlowFields`):
the sweeps took 9 s for 7.9 GB, leaving out 11 GB the game cannot write or the driver's, and four levels of 5 to 7 s found four chains,
all to two statics of the program - `module+0x46FD388 -> +0x0 -> +0x8` = the river's holder, the same through module+0x46FD450, and through
the address sixteen bytes before the object at +0x390 and +0x458 (a std::shared_ptr's pointer and its control block as make_shared lays
them, a reading). The object's first word points at a run whose +0x08 is the river's holder and +0x10 the guiding fog's. No vtable named
a class anywhere on the way. A static's address is one launch's and the run's order one area's, so before the live read is built,
v0.1.354 writes every place a chain reads whole, word by word, and finds the program's code reading each static: every [rip + disp32]
landing on it, the bytes round it and the shortest pattern round it found once in the image - what the schema's statics are found by.
Press it once more AFTER RESTARTING THE GAME, on The Red Vale's river or another's (not The Riverbank, which cannot be revisited): the pattern and whether the
chain holds come from that one capture.
THE RESTART HELD THE LOWER HOPS (v0.1.353 pressed again after a restart, The Red Vale; every address new): the run holding the river's holder
at +0x08 and the guiding fog's at +0x10, and the object pointing at it from its +0x00 - but no way home, because the third level's 32 places
all went to stringstream buffers pointing at one another, as near their targets as the statics and nearer in memory, which ranked the
statics out. From v0.1.355 every place in a static or the area instance is kept whatever the rest, and one target keeps at most eight
(`RiverFlowHunt.MostPerTarget`) - `ASTATICIsKeptHoweverManyNearerPlacesPointAtTheSameHolder`. Press v0.1.355 after a restart.
v0.1.355 FOUND THE WAY AGAIN, IN OTHER SLOTS (a second instance of The Red Vale in the second launch, 262 x 331 texels): `module+0x46FD358
-> +0x0 -> +0x8` and the same through module+0x46FD480 - where the first launch had +0x46FD388 and +0x46FD450. The object is std::make_shared's
(its control block sixteen bytes before it, two owners), its +0x00 a std::vector of three field pointers, the first null; no code reads the
slots themselves, and beside +0x46FD480 stand a begin and an end pointing into the image (module+0x46FD358 to +0x46FD388) after 8, 6 and 0.8 -
a container in the program's own data whose slots move, a reading. The area instance reached the holders too (+0x770 and +0xF8), but
through offsets past the end of the vectors they start from - neighbours in the heap, no way to read by. So v0.1.356 writes the program's
words round the slots as one run (0x200 either side) and every piece of code reading within that run, by the place it reads, with the
shortest pattern round each - the anchor a live read needs. Press it in The Red Vale on the river.
V0.1.356'S TWO CAPTURES NAMED THE SLOTS, AND THE RIVER FLOWS (v0.1.357; one launch, The Red Vale and the same area reset to a new layout;
schema `RiverFlowCache`, `RiverFlowCacheNode`). The program keeps a cache of markup objects in its own data: four nodes of 0x30 - each
the object's std::shared_ptr, the nodes towards the head and the tail, and a key growing with every instance (4, 10, 12, 14, 16) - the most and least
recent nodes' places in the two words before the CURRENT ENTRY, a std::shared_ptr always holding the current instance's object,
module+0x46FD480 in all three instances. A new instance takes the least recent node, so the node holding the object moves and the
current entry does not; no code reads it by its address, and it moved with a build. So it is FOUND BY ITS SHAPE once a launch
(`RiverFlowLive`, DECIDED from the live client): in the program's writable data, make_shared's pair whose two words before it are places
within 0x400 before it, the first holding the same object, the object a vector of two to eight field pointers - its second the river's
map, read whenever the object changes and believed only where it flows within its banks; a word every quarter second (CHOSEN), the search
and the read behind the frame. THE RIVER FLOWS where a Monster Book or Tile Book stage is laid as the area laid it, while the area is the
one it was laid in (`MonsterModel.AreaStamp`; a staged monster keeps his laid room's place, `StagedModels.Staged`): `WaterLook.Flow`, the
map's box three tenths of a texel below the area's origin (MEASURED in four instances), so the map's size is all it needs. On the card and
the processor alike (DECIDED from the live client), GetFlowingWaterPlanarStateWorld as written (`ShadeProgram.RiverFlowAt`, `RiverFoaming`;
the card's `ShadeRiverFlowAt` and `ShadeRiverFoaming` over `RiverFlowSheet`, t122, and the frame's `RiverBox`): the flow twice the area's
flow_intensity, held to 100; GetFlowSurfaceField's octaves scrolled along it; the foam's strength from its curl, its speed, the shallows
and the 25-step march back up the flow; GetFlowingFoamColor's two nodes; the bubbles laid as white over the medium (BuildWaterRefraction).
Where a drawing carries no map everything is as at no flow, to the bit (`RiverTests`). NOT DRAWN: GetFlowingSurface's march of the view onto
the moved surface. `river-flow.txt` opens with the live read's own account (`RiverFlowLive.Report`): what the pictures were drawn with, the
search run again, the cache node by node, the map's box and flow - press it on the river, the stage laid where you stand, if it does not move.
THE RIVERBANK CANNOT BE REVISITED (said from the live client): it is every character's starting area, left for good at the first town.
So the second river, which is to say whether the river's field stands second in every area's object (RiverFlowFields), is measured
elsewhere. Where rivers are, by the second stage sweep (v0.1.348; rooms wearing `FlowingWater` or `NonsplashyFlowingWater`, per master):
Act 1's 1_5 (The Red Vale, 43 - measured), 1_2 (33), 1_14 (33), 1_13 (30), 1_1 (27) and 1_4 (14); Act 3's 3_10 (57), 3_1 (40), 3_8 (33),
3_14 (29) and 3_2_2 (22); the interlude's p1_4 (89); the maps Riverhold (78), Rockpools (48), Rupture (42), Hidden Grotto (42), Bluff (31),
Cenotes (29), Vaal City (26), Deforestation (26), Riverside (24), Woodland (24) and Wetlands (23). A room's material is not a flow map:
the area's own `river-flow.txt` says whether the engine made one and where it stands.
NO STAGE WAS TO BE HAD ON THE RED VALE'S RIVER (said from the live client with three pictures, v0.1.357): its river rooms (`curve5` and
its kin) are one big slot each and name no doodad, so neither the doodads nor the corner search places them - the Tile Book placed 7 of
the area's 187 rooms, none of them the river - and the Monster Book offers laid rooms named for an arena alone. Every tile the area laid
is known with its place and turn, so from v0.1.359 both books offer THE AREA ROUND YOU (`AreaBlock`, `LaidRoomModels.Block`, `AroundYou`;
DECIDED from the live client): the block of tiles round the player's tile laid as a laid room lays its pieces - their ground and their
own `.tmd` doodads, no room's lines, zones or boss places - its side a slider shared by both books, 3 to 25 tiles, 9 the usual (CHOSEN
from the live client, the settings' `aroundSize`), the picture's origin the block's corner in the area, so the river's flow map is drawn
on it. The Tile Book's "around you" button shows it in place of a row until another is chosen; the Monster Book lists it as a stage.
The ground is painted by the master beside the area's rooms that names the most of the block's ground types (`AreaBlock.MastersOf`,
`CaptureGroundScreen.MasterOf`), the line under the picture saying which.
EVERY DOODAD FOUND IS TO BE DRAWN (said from the live client with v0.1.359's capture: "the goal is every book as near the game as it can
be"): the river moved the game's way at about its speed, the water was too red against the game's brown, and the block lacked the forest
floor - ferns, grass, leaves, strewn weapons - laying ONE tile doodad over 25 x 25 tiles. So from v0.1.360 the block also lays the own
doodads of every room the tool placed in the area that reaches it (`AreaBlock.Rooms`, `RoomsIn`, `LaidRoomModels.OthersOf`, as a laid room
lays its own), and the forest floor's files are read: a MaterialsList names after a material its DOODAD LAYERS (`.dlp`, `GroundChoice.Layers`
- The Red Vale's `Grass_redvale.dlp`, `FallenLeafPiles.dlp`, `JungleFoliageA_Redvale.dlp`, `RVL_SwordScatter01.dlp`), and a master its
DOODAD LIST (`.ddt`, the `Doodads` key: rusted weapons over Default, clutter over Stream). The client strews both itself, where no file says,
so the tool will strew them ITSELF too (DECIDED from the live client: a scene built away from its area could read nothing from memory either)
- but what a layer's number after each mesh and its pairs between nought and one mean, and the list's numbers, no reference names
(`DoodadLayer`, `GroundDoodadList`: poe_data_tools' `dlp` and `ddt` grammars). `ground-scatter.txt` (`CaptureGroundScatter`, Area files)
writes every layer and list the area's masters name, read and whole, each layer's meshes with their size, and how much of the area each
ground type covers, the files themselves beside it (`ground-scatter/`); `ground-scatter-replay.txt` reads them again offline. Read it
before the scatter is given a rule. Then the water's colour: the game's river is brown, the tool's red.
THE SCATTER IS STREWN (v0.1.362, `GroundScatter`, every laid, built and block stage, as each piece's ground is painted). Its first capture
(v0.1.360, The Red Vale) MEASURED: a layer's points lie on a grid of thirty-seconds of the unit square, no two of its meshes in one cell - one
pattern dealt out among them (RVL_SwordScatter01 49 of 49, RVL_SwordScatter02 588 of 588; JungleFoliageA_Redvale's off the grid); in
RVL_SwordScatter01 every mesh carries round(4 x its number) points, all nine; the numbers are ninths, RVL_SwordScatter02's capped at 10 for
nine meshes of 39 to 94 points; the ground textures carry no painted weapon where the points lie; and none of it is an entity - the list's
rusted weapons are a handful among 1,243 entities, each a room's own line. DECIDED from the live client (2026-10-30), each said under the
picture: the number is a mesh's count a tile and the points a pattern over two tiles by two (`GroundScatter.Pattern`, 500 units - the ground
colour's repeat too), laid in the area's own coordinates; a mesh without points strewn its number a tile at random; every layer of a group at
its material's share of the group's weights (the painted ground staying its heaviest) and its ground's share of the sub-tile's corners,
bilinear; the doodad list one a tile (CHOSEN, it names no density), from the group named as the ground's group, else "Default"; a layer not
AllowOnBlocking kept off ground nobody walks where the area's walkable grid is known (a built room's is not). CHOSEN: the seed, a turn spread
over MaxRotation (half of it either way), a size between RandomScale's two shrunk towards MinEdgeScale where the ground gives way, upright on
the ground's top. NOT DRAWN: waving, AudioType, a layer's own Seed, a group's extra choices. The strewn lines go to `RoomModels.Lay` last
(`strewn`), so the doodads slider drops them first. v0.1.363 MENDED what an adversarial review of it found and confirmed: a point on the
pattern's edge (a coordinate of one - five in RVL_SwordScatter01) was laid nowhere, now where the next square's nought lies; a sub-tile two
templates lay ground on (Landmark_feature_01's three) was strewn once a template, now once on the top of both; whether the walkable grid is
known was judged from the room's own tiles, so a room over sea or abyss in a read area strewed over it - now from the whole grid
(`TerrainGrid.AnyWalkable`); a list doodad that is a set drew its member by the room frame's rounded place, now by the scatter's own draw
(`GroundScatter.MemberOf`); and the list's first number (1 in The Red Vale, 0.25 in Marrow, "0.2 5" in Port), poe_data_tools' "scale?",
was taken as a size - NOT READ now, the list's doodads at their own size, until its meaning is measured. THE WATER BESIDE IT (v0.1.360's capture, the Monster Book at the game's view, its probe on the river by the
bridge): the medium as the engine's pass makes it - 0.0204, 0.0001, 0.0000 - 66 units of water to the rock behind, a tenth let through; the
game's river there red with its bed showing in the shallows, sRGB (35, 14, 14) at the median with green equal to blue (a neutral share over a
red medium), the pane's (52, 12, 12), the pane's land twice the game's brightness - so the pane's light is not the game's, and the colours
are not yet compared on equal terms. The engine raises open_water_color to 2.2 itself (GetWaterMediumColor), as the tool does; the
river's curvature (its height times 5e-5) barely moves the hue; water_height_offset moves only the beach, crashing and flood waves and the
spray, and river areas without one abound - so neither is the cause.

## A material's vertex stages

THE ZONE TRANSITIONS' LIGHT DREW AS A SOLID WHITE TUBE (said from the live client with a probe, v0.1.363: The Red Vale's exit to the
Grelwood). `generic_cylinder.ao` wears `light_raysc.mat`, whose `AddMaskMuddleTex` reads its mask at `InputVertexUV` taken at VertexInit -
a node the compile does not know - so the whole alpha chain (the mask, Alpha_SmoothStep, SoftAlphaParticle, FresnelFade) was lost and the
tube added at alpha one; its `ScaleMoveUV` scrolls the colour three times across by writing `VertexUV` at WorldTransform, a vertex stage the
compile does not run, so nothing scrolled either. The stage sweep counts the node missing in 1,846 rooms, and writes at the late pixel
stages the compile does not place (FogStage_Calc's IndirectLightMult in 2,876 rooms, Lighting_Calc 238, PostLighting 89 white). Inside
the tube stood a capsule lettered AUDIO, `Metadata/Terrain/Tools/AudioTools/G1_5/OldForestEntrance.ao`: a sound emitter, hidden as a tool
from v0.1.364 (`RoomModels.EditorFolder`, DECIDED from the live client).
WHAT THE ENGINE'S GRAPHS SAY (render-debug's engine graphs, read offline): fixed.fxgraph and the particles' write VertexUV from the mesh's
coordinates (VertexLocalUV) at VertexInit and the pixel side's UV from the vertex side's last (FromVertexUV) at PixelInit; the trails link a
vertex stage's input straight to a pixel stage's writer (InputVertexTrailPhase@VertexInit to TrailPhase@VertexToPixel) - a value crossing
the two shaders is the engine's own means, so AddMaskMuddleTex's mask reads the mesh's coordinates unscrolled. What is NOT known is the
vertex stages' order: the links of the 335 graphs earlier captures hold mostly follow the names and in places run against them (face_camera.fxgraph reads at
WorldTransform and writes at WorldTransform_Init). DECIDED from the live client: measure before any of it is compiled. `shader-stages.txt`
(`ShaderStageHunt`, the Memory group, run at the press) reads the client's stage table - an array of MSVC std::string in its image, of
which only the pixel side's ten entries were ever seen (the schema's 2026-10-09 find) - by reading every eight-byte place of the image as
one and its heap names, and writes the runs of them 0x20 to 0x60 apart holding stage names (up to 16 runs of 512 entries), the best taken
as the table where it holds three - written whole on its own, the order every later section reads - and the compile's order held against it;
then every link of the install's graphs across stages by stage pair (forwards or backwards in the table), those from a vertex stage to a
pixel stage by their ends' types, and every write of the coordinates with what feeds it. `shader-stages-replay.txt` holds the report's pairs
and this build's compile order against the table it took again (v0.1.366, from a review: it read run 1 even where the report took none;
a v0.1.365 report, which wrote no table of its own, by run 1 where its head says it took one).
THE TABLE WAS READ WHOLE (v0.1.365's capture, The Red Vale; the schema, at the stage table's note): module+0x459C760, 81 entries - the vertex
side VertexInit, Animation, LocalTransform, LocalTransform_Calc, LocalTransform_Final, WorldTransform_Init, WorldTransform, WorldTransform_Calc,
WorldTransform_Final, ProjectionTransform_Init to _Final, VertexOutput, _Calc, _Final, _Pass, then VertexToPixel, PixelInit, the pixel side's
ten as the compile had them, AlphaClip, the lighting's, the fog's and PixelOutput's, and the particles', volumetrics' and trails' own. Every
one of the 69 stages the install's 11,972 graphs use is in it; 193 of the 197 stage pairs they link run with it. So FROM v0.1.367 THE
COMPILE RUNS THE VERTEX SIDE FOR THE COORDINATES (`ShadeProgram.Stages`, MEASURED): `VertexUV` is a channel starting as the mesh's own
(the engine's fixed.fxgraph writes it from VertexLocalUV at VertexInit), `InputVertexUV` reads it at its own stage, the pixel's `UV` is
its last at PixelInit (fixed.fxgraph's FromVertexUV), and `FromVertexUV` reads that last - worked out per pixel, the engine's value
wherever the arithmetic is affine in the coordinates (a scroll, a tiling), CHOSEN for the rest. `ConstantVertex`..4 and `FresnelFactor`
(effects.ffx) are compiled beside them. The zone transitions' light now reads its mask, fades by its Fresnel rim and its soft edge and
scrolls three times across (offline, light_raysc.mat of both Red Vale captures); what it still leaves out is IndirectLightMult at
FogStage_Calc - a stage after the lighting, which the compile does not place.

## The sun and the area's colours

THE SUN THREW ITS SHADOWS THE WRONG WAY UNTIL v0.1.368 (said from the live client with three pictures of The Red Vale: shadows up-right
where the game's fall down-left, everything too bright, the grass a juicy green the game's picture of the area has nowhere). `SceneLight.GameSun`
was the shadow box's UP axis, found in Azmerian Ranges as the only one of the eight readings in memory and its sign settled by the ground. Necropolis's
light hunt (cap1) had dumped all three of the box's plane pairs - eight corners, then each axis's plane and its negative, sixteen bytes apart (schema
`SunShadowBox`) - and the light is the third: `-(sin phi cos theta, sin phi sin theta, cos phi)`, phi measured from straight down
(`SunReading.PolarPhiDown`), so the sun stands phi - 90 high (The Red Vale 30, Port 35, Crimson Shores 38, Necropolis 29, Azmerian Ranges 44)
where the up axis had stood it at 180 - phi and thrown the shadows the opposite way. MEASURED: the shadows' bearing in the game's pictures of four
areas through each capture's own camera, Port's skeleton's shadow length, the sun's glints on the water; `THEGAMESSunIsTheShadowBoxsLightAxis` pins
cap1's planes. Port looked right only in its BUILT stage, laid as written while the game's instances had laid the boss room turned 180 - its laid
stage threw the shadows the other way too, unremarked. `LightHunt` looks for both readings now and takes only the light's pair, naming any other as the
box's frame and saying the axes stand in a row (`THESHADOWBoxsTwoAxesAreToldApartAndTheLightIsTaken`).
THE SCATTER WEARS THE MASTER'S SWAPS (v0.1.368, CHOSEN): The Red Vale's TileMaterialOverrides swap the ferns its JungleFoliageA_Redvale layer strews
(JGD_JungleFoliage01c) for the dry RVL_JungleFoliage01c, and its swords' SkeletonSwords01c likewise - 2,470 of the 3,212 lines its v0.1.365 capture
laid round the player wore a swapped material unswapped. The room's own lines stayed as they were until v0.1.373 - The Red Vale's BRDgrass lines
among them, BRD_MarshGrass02c, ten lines; from v0.1.373 THEY AND OTHER ROOMS' OWN LINES IN A BLOCK WEAR THE SWAPS TOO (DECIDED from the live
client, 2026-10-30: "the room's own doodad lines wear the master's material swaps as well").
STILL OPEN, from the same pictures: the shade measured 2.6 times the game's in The Red Vale (more sunlit ground under the old sun accounts for part -
measure again on v0.1.368 - MEASURED on v0.1.372: the shade is the game's now, see THEIR FIRST CAPTURE below); a neutral or blue share the pane lacks in both maps; the ambient's occlusion (indirect_color.a) and the GI, neither drawn;
and the tree crowns' square shadows - a lead, not a cause: the woods' templates carry canopy cards whose materials are shadow-only
(JungleCanopyc and canopytestc on ForceAlphaTestWithShadowOnly.fxgraph, black_shadowonlyc on OpaqueShadowOnly), and a mode naming "shadowonly"
casts whole triangles here, its texture unread. The shadow-casters part below measures it in one press (v0.1.369) - read it before any rule
is changed.

TWO CAPTURE PARTS MEASURE BOTH (v0.1.369, asked for from the live client: "build the capture option now, it comes anyway - then we need not
trade crumbs of information"). Both need a book on a stage laid as this area instance laid it ("the area round you") and the light panel's
light (built as "game light" would build it, ticked or not - SceneLightPanel.Measured); keep "game picture" ticked and stand still.
- `light-against-game.txt` (`CaptureLightCompare`, over `CaptureAtGame`): the stage drawn on the processor THROUGH THE GAME'S OWN CAMERA
  (`GameCamera` takes the matrix at WorldData + 0x1A0 apart - eye, right, down, forward, focal lengths, principal point, near and far, held
  to WorldToScreen by `GameCameraTests`; `MeshPicture.Camera.Through`, `Canvas.Lens`, `Canvas.Rows`) at 1/ceil(longer side / 2048) of the screen,
  the game's picture averaged over each drawn pixel's block in linear light, its interface and everything that moves masked. Every second
  drawn pixel each way carries what it was made of (`PixelGather`, written where the drawing lights it: triangle, place, normal and
  `SceneLight.LightTerms` - the light term by term, the same colour to the bit, `Shaded` over a struct the compiler specialises). Reported:
  how the two pictures' edges line up (no shift is the camera's and the placing's agreement); the game over the pane by the pane's sun and
  shade, by material (with the game's OWN sun and shade, Otsu on its light over the pane's albedo), as game = s pane + n, term by term
  (non-negative least squares of the game taken back through the pane's grade and exposure - CHOSEN: the engine's grade the same table),
  by the distance to the player (the CHOSEN lamp) and by depth. `light-against-game.bin` keeps every compared pixel; the replay works the
  report out again (`CaptureLightCompare.Replay`).
- `shadow-casters.txt` (`CaptureShadowCasters`): the sun's map that keeps its casters (`ShadowOwners`, `ShadowMap.Owned`,
  `MeshPicture.Casters`) as drawn and with shadow-only shapes cut by their texture's alpha (`CastRule.ShadowOnlyCut`), the ground alone
  beside them; every caster of the ground's shadow by blend, the word its graphs spell, material and clear share of its texture; each piece
  of shadow's fill of its tightest box (square-like from 0.8, CHOSEN); the blend graphs (ForceAlphaTestWithShadowOnly and its kin) and
  the casting materials whole; and, through the game's camera, the game's light where a cut would take the pane's shadow away beside its
  light in the open and in a shadow both readings cast - t nought the game casts as the picture does, one it casts nothing there.
  `shadow-casters.bin` keeps the ground's map by kind; the replay counts its pieces again.
- MENDED FROM A REVIEW (v0.1.370): a screen taller than wide drawn at its width's fraction overran the canvas; a stage out of view was
  said to line up (no shift measured, nought taken as agreement); a light with no sun counted every face towards its way as in the sun
  (`LightTerms.Sun` and `SunFacing` are nought now where there is no sun); t counted the lost pixels of groups no brighter lit than
  shaded among the judged, pulling it to nought; the verdict grouped by shape, a laid block's ground being dozens of shapes of one
  material; and a cut card's kept shadow was credited to whatever cast once cut. v0.1.371: THE GAME'S PICTURE AT ITS CEILING is left out of
  both comparisons (`CaptureAtGame.Clipped`, a channel at 254 or more in the block, CHOSEN) - the game's picture clips where its light passes
  one while the pane's is unclamped, and MEASURED on the captures' game pictures Port's sunlit cobblestones reach 255 on 0.7 to 4.3% of the
  picture, Crimson Shores' arena floor on 2.5 to 3.4%: taken as exact they read as a pane too bright and too saturated. The numbers are
  version 2 (a ninth reason left out; version 1 still replays). A drawing's or a part's failure inside a parallel loop (AggregateException)
  no longer ends the whole capture; the light panel's lines take the frame at the press and the lamp's height as set. v0.1.372: the
  capture draws the programs and the water as the measured light says (its spray twins and way in), "game light" ticked or not; and with
  no stage laid in this instance the shadow part takes a stage with ground (`CaptureShadowCasters.Fallback`), not a lone monster. The
  drawing through the game's camera runs as the first waiting part's step, so the capture's window says what it draws, and a drawing that
  fails is filed as failed, not as not written.
THEIR FIRST CAPTURE (v0.1.372, The Red Vale, the Monster Book on the area round you at 25 tiles, the player in a clearing among the trees) -
MEASURED, every number from light-against-game.txt and its .bin worked offline:
- THE SUN IS THE GAME'S NOW. Per material the game's own sun over its own shade (Otsu on its light over the pane's albedo) is the pane's
  within a tenth - the ground 7.8 against 7.3, the jungle foliage 8.0 against 8.6 - and in the pane's shade the game is the pane's, 1.06 at
  the median, where the old sun had measured the shade 2.6 times too bright. The shadows' bands fall the same way in both pictures.
- WHERE THE PANE LIGHTS THE GROUND THE GAME IS DARK: in the pane's sun the game is 0.245 of the pane. Block by block (16 gathered pixels,
  the ground beyond 500 units of the player) the pane's lit blocks stand 13 times its shaded ones, the game's 2.2 times; of the ground the
  pane lights the game shades two thirds. The area's .env sets CLOUDS - clouds_intensity 0.9, midpoint 0.55, sharpness 0.75, scale 0.8,
  speed 30, angle 2.94, the fade radius the engine's 800 (schema `ParsedEnvironment`) - which the tool draws nowhere; the game's picture
  is a bright clearing round the player inside dark ground. Where the engine lays the clouds over the sun is in no captured source:
  common.ffx declares their uniforms, lighting.ffx reads the sun's shadow from a screen-space map a pass makes - `light-extras.txt`
  (v0.1.373) gathers what applies them before they are drawn.
- THE GAME IS GREYER: its chroma 0.39 of the pane's, its shade nearly neutral (0.349 0.314 0.337 against the pane's orange 0.368 0.341
  0.291), and game = s pane + n leaves a grey of a third of the game's mean. A lead: the .env's SCREEN-SPACE FOG (ssf_color 0.139 0.109
  0.119, alpha 0.1, 5 layers, disperse radius 1,300), drawn by graphs reading effects.ffx's SSF fragments - gathered by the same part.
- THE PLAYER'S LAMP is stronger in the game: within 400 units across of the player the game in the pane's shade is 2.4 to 2.9 times the pane.
  From v0.1.375 the part FITS game = a rest + g lamp over the pane's shade (non-negative least squares, `CaptureLightCompare.LampFit`) where
  it took each pixel's ratio, which divides the rest's mismatch by the lamp's share (a review: 1.46 asked of The Red Vale's v0.1.372, negative
  in two bins). MEASURED: v0.1.372's capture g 2.725, a 1.341; v0.1.374's (another spot, the same area) g 0.674, a 1.691 - the two disagree,
  and in both the 300 to 500 unit bins ask the rest three times the pane's where the pane's lamp hardly reaches: a reading, not settled, that
  the game's lamp reaches farther than the pane's 500 units. Hold the lamp against the engine's own (light-extras.txt) before a rule.
- THE GAME HIDES ROOF SECTIONS IN FRONT OF THE PLAYER (said from the live client: the engine keeps big objects from standing in front of the
  camera and blocking the view). A material whose .mat says `"roofsection": true` - the OFO_BlackForestTrees01*_fadec trunks and
  RoofFade_JungleTreeBranches01c, not the plain OFO_BlackForestTrees01Ac or Dc - is a roof section; of the twelve such shapes the pane drew,
  the two the game left out (11 and 2.5 times brighter there: what lies behind) stood nearer the camera than the player and within 85 gathered
  pixels of him on the screen; one as near on the screen but at the player's own depth, and every one farther off, the game drew. A reading,
  one capture: a cylinder from the eye to the player some 180 units round. light-against-game.txt leaves roof sections out and reports them
  apart from v0.1.373.
- THE CANOPY: JungleCanopyc and canopytestc (ShadowOnlyAlphaTest) cast 432,000 of the ground's 949,000 shadowed texels as drawn and keep 98%
  of them cut by their alpha - the cards overlap; black_shadowonlyc (OpaqueShadowOnly) casts 64,000, 15 of its 392 pieces square-like. The
  game's light where a cut takes the shadow away, judged over 1,200 pixels: t 0.33 - too few, and the clouds darken the lit reference too.
- SO v0.1.373 GATHERS AND MEASURES BEFORE ANY OF IT IS BUILT: `light-extras.txt` (`CaptureLightExtras`, Area files) writes every
  environment's clouds, screen-space fog and player-light keys, every shader source and graph naming the clouds, the fog, the screen-space
  shadow, roof sections, the see-through fade or the player light, whole, the program's strings and the textures they name, and the
  roof-section materials the panes wear (`MaterialFile.RoofSection`); `light-against-game.txt` leaves roof sections out and sets them apart
  by the distance to the player, holds the ground block by block (`light-against-game-blocks.png`: the pane's sun share against the game's
  light over the albedo), and works out the gain the game asks of the player's lamp in the pane's shade. Press both in The Red Vale, the
  Monster Book on the area round you, before the clouds, the fog, the roof fade or the lamp is given a rule.

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
  part unasked (a part that runs for hours is the one exception, off until ticked - see "Every room at
  once" below). Nothing goes on a window as a copy button or a new pane; the catalogue is the one
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
  t13 and t14, a material's sheets from t15 - t18 from v0.1.295, after the field waves' t15 to t17): `gerstner_positive_wave_profile.dds`, 256 square, two floats a texel, a fixture; and
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
  ITS FIRST WATCH ANSWERED (v0.1.290). The ship's own components never change; the WobblePoint holds a 4x4 matrix (row vectors,
  +0x68 to +0xA4) that is a PURE TURN ABOUT ONE POINT - (4162, 12473, -328.2), z the core's TerrainHeight, the translation left over
  under half a unit against 81 - tilting up to 0.37 degrees, swings 1.9 to 2.4 s apart whose sizes repeat after about 12.9 s: an authored
  loop. The files say whose: the core's `.ot` writes `WobblePoint { sector = "caravan"; bone = "caravan_jntBnd" }` and an Animated
  object whose `.ao` plays `idle_small_01` on `Boatnaut/BoatWobbleRig/rig.ast` (`randomise_start_position`); Port's boss room writes a
  ZONE `"caravan" 26 -102 261 454` round the ship (`RoomZone`, poe_data_tools' `Zone`, read from v0.1.291; the room's other zones beside
  it); the program names `no_wobble`, which `DoodadNoWobble.ot` sets. In memory the WobblePoint's +0x40 is the Sector component of an
  entity of its own (`Metadata/MiscellaneousObjects/Sector`). SO THE SHIP SWAYS ON THE CARD from v0.1.291 (`WobbleRig`, `RoomSways`,
  `RoomSway`, `MonsterModel.ShapeSways` and `Sways`, `ModelScene.Sways` and `Swayed`): the bone's world matrix
  (the core's frame; its skin matrix until v0.1.293, below) conjugated into the picture's by the core line's own place, every doodad line in a
  zone of the sector's name whose type sets no no_wobble turned by it in the vertex shader (`Part.Sway`, the shadow's casters cast again
  each frame over the kept stage). READ FROM THE FILES, said under the picture: the zone rule (the census in wobble.txt names the
  Sector's own list in the next capture) and that the bone's skin matrix is the WobblePoint's (wobble-files.txt plays the rig
  beside the watch, `wobble-replay.txt` fits both halves' fixed points). CHOSEN: every core starts its loop at nought. NOT DONE:
  the processor's picture stands still; the curve at the WobblePoint's bone block (+0x180) is unread. v0.1.291 SWAYED NOTHING (its Port
  capture: "its keyframes did not unpack"): the rig was unpacked with `Oodle.Decompress`, the shipped managed decoder, which refuses this
  rig's bundle while the install's own (`GameFiles.Unpack`) reads every monster's - so the install's decoder is handed over as it opens
  (`WobbleRig.Unpacker`, v0.1.292), and a rig that still does not unpack says which decoder ran and what it refused. That capture's census
  also showed a "sector" IS a room zone: the WobblePoint's Sector lists further Sector entities, one a zone of the boss room.
  v0.1.292 SWAYED, BUT SANK THE SHIP 232 UNITS (its gangway below the quay, said from the live client with a picture). Its capture held
  both halves at last: the tool's turn is the WobblePoint's to the float, but the SKIN matrix carries the rest's z -232.42 as +232, the
  animation holding the bone at nought. The game's matrix is the bone's WORLD matrix (`SkeletonPose.World`) conjugated by the core's
  place: less a turn about the core's own position (Render x and y, TerrainHeight z) its translation leaves under 0.003 in x and y and
  the bone's heave, -0.864..0.664, in z - the world matrix's own, exactly. `WobbleRig.At` takes it from v0.1.293. The earlier fixed
  point 200 units beside the core was the heave biasing a fit along the turn's axis, the same bias the tool's own matrix shows.
  v0.1.293 put the gangway back on the quay and swayed "almost identically" to the game (said from the live client).
  THE SWELL RUNS UNDER THE SHIP (said from the live client, v0.1.293, with an in-game picture): in the game the waves make their way
  between the ship and the quay, a breaking crest in the channel; the tool's run in under one side of the hull and out of the other,
  because the shore field is made from the ground and the spraying rocks alone and the ship is neither. What the engine makes ITS field
  from is in no file read so far - but the program holds two script commands, `SetWaterBlocking` and `SetWaterFlow` (lambdas over
  ScriptTokens, as an object script's commands are), beside `RiverMarkupField` and `MarkupFlowConstraints`. `water-shore.txt`
  (`CaptureWaterShore`, v0.1.294) searches every `.ot` and `.otc` under Metadata/ for them on every core, lists each line naming them,
  the area's loaded types reaching one through what they extend, the shader sources naming the field and the program's strings naming a
  shore; `water-shore-replay.txt` reads the lines back. Read it before the ship is made shore.
  THE BREAKERS ARE THE ENGINE'S FIELD WAVES (v0.1.295, four in-game videos of Port from the live client). MEASURED off the videos, the
  brightness of every patch of water over time: white-crested fronts 4.9 s apart at the quay's corner, 5.1 s on the open sea, 4.9 s at
  the pier's end - water.ffx's `field_waves` (`field_wave_length` 1,000, `field_wave_speed` 200: 5 s), not the beach waves (2.5 s) nor
  the flood waves (25 s); in the channel by the ship the front spans the channel and runs along it, and at the pier's end the crest
  lines bend round it. The engine sends them (and the swell) down the slope of a second field, `ocean_dist` (ShoreData's blue), kept
  apart from the shore's distance (red) - and the program names what makes it: `OceanMarkupField`, built from a grid of ints or three
  point lines, beside `ShoreDeep`, `ShoreShallow` and `IgnoreDeepWaterMarkup`; Port loads `island_ocean_deepwater.gt`, which none of
  its tile corners holds, among water ground types of other areas. `water-shore.txt` now searches every object script, ground and
  edge type, environment, master's list, tile and room - and the data tables - for those words on the bytes, ASCII and UTF-16, and
  writes every ground type the area loaded whole: read it before the way in is changed. UNTIL THEN THE WAY IN IS CHOSEN
  (`ShoreField.Oceans`): the shore's distance in the open sea (`ShoreField.Deep`, 1,000, or at the field's edge), and inside it the
  open sea's less the shortest way through the water from there (sixteen ways a cell, within two percent of the straight line),
  round whatever crosses the water's surface (its top above and its bottom under: the hull, a post, a rock - not a bridge or a
  gangway); `model-panes.txt` counts the cells in the way and draws `shore-way.png`, a white line every 250 units where the game's
  crests should run. The field waves are DRAWN ON THE CARD ALONE (DECIDED from the live client): CrashingWavesVertex and
  CrashingWavesFragment ported line for line (`ShadeHlsl.FieldLibrary`, `Op.Field`), laid on the room's own water - lifted by the crest
  and the area's height offset (the shape's mean lies under nought; Port's -15 lifts it back), turned by its slope and covered by its
  foam as the swell's foam covers; their wave shape `gerstner_wave_profile.dds` (read red and blue, GetGerstnerProfile - the profile the
  program names that neither the swell nor the beach takes), their noise and foam the beach's (CHOSEN, no graph names theirs); fixed
  slots t15 to t17, a program's sheets from t18; the light panel's water row switches them ("crash", SceneLight.WaterCrash), and with
  them on the water is drawn lifted whatever the lift. The processor draws none, and the line under the picture says so.
  THE WATER IS LIT AS THE ENGINE'S WATER PASS LIGHTS IT (v0.1.296, asked for from the live client: with the field waves on, Port's
  water came nearest the game under the FLAT ambient, while the cube is the engine's). The engine does not light water as an albedo
  (water.ffx, lighting.ffx, whole in `water.txt`): GetWaterMediumColor sets the medium's diffuse colour to nought and hands the area's
  colour over as its subsurface colour, `pow(colour / (0.3, 0.9, 0.5), 1 / (1 + max(-0.9, 250 * height * 5e-5 * water_subsurface_scattering)))
  * (0.3, 0.9, 0.5)` - a crest lifted greener, a trough darkened (GetTotalWaveSurfaceNumerical's `curvature`, the ocean's height); the
  foam is the albedo, the medium less it, translucency 3 and front scattering 1, so every light adds `saturate((N.L + 3) / 4)` of itself on
  the medium (ComputeDiffuse), N the waves' normal for all of it; the cube's diffuse light falls on both less gi_env_occlusion, the GI
  on the albedo alone; and BuildWaterRefraction mixes `specular * (1 - ratio^3) + (behind * T + absorbed * (1 - T)) * lerp(1 - reflectivity,
  1, ratio)`, the foam hiding by `1 - (1 - a)^3`, `reflectivity = lerp(Fresnel(N.V), 1, specular) * (1 - ratio)` - its reflection thinned
  by the shore's ratio alone, where this tool had thinned it with the medium's cover. `SceneLight.ShadeWater` ports it line for line, the
  card's `WaterShaded` the same, handed its parts by the water's glossy twin (`ShadeProgram.WaterOut`: the foam as albedo, what gets
  through, the ratio, the colour's pattern, the waves' height); `water.subsurface_scattering` is WaterLook's, Port's default one. As a layer
  its cover is one less the share of what lies behind, its colour the rest over that cover. The probe's water line writes every part
  (`WaterLight`). NOT DRAWN: the refraction's bend and caustics, the terrain water colour, and the tidal state's own water_to_terrain_ratio
  and ground water ratio - both read off the shore field, whose scale is CHOSEN. Its first capture (v0.1.296, Cube Ambient, "by far
  nearer the original", said from the live client) put the probed open water at about sRGB (42, 62, 58) before the grade, the game's
  open water (50, 72, 70) at the median - and found a rectangle of false shadow beside the ship: the wobble core's plane
  (`transparentobjectsc.mat`, Zero under ForceAlphaTest) was cut away when drawn but cast solid, the shadow map cutting through a
  texture it has not. A cut-out shape whose program's alpha is one number under the cut now casts nothing on either drawing
  (`ShadeProgram.FixedAlpha`, `CutAway`; MeshPicture's `Casts`, ModelGpu's `Casting`), as the engine's shadow pass clips it (v0.1.297).
  With that gone, a straight "obstacle" still stood in the channel along Port's quay, the swell's foam and the breakers stopping at it
  with and without the field waves (said from the live client, v0.1.297). It was the way in's: v0.1.295's rule put a cell in the waves'
  way where ANY blocking shape stood above the water and ANY under it - the least and most z over all of them at the cell's middle - and
  the same wobble plane over the quay's sunken foundation bricks (`MSHD_GraveBrickTile01c`, 240 under the water, the probe's) made a
  bar 125 units wide along the quay, the plane's length, red in `shore-way.png`. From v0.1.298 one triangle has to cross the surface
  itself, and the cells it stands in the way in are those its waterline runs through, walked one step along x or y at a time
  (`ShoreField.Crossing`, `Walked`) - a sheer wall covers no cell's middle and blocks all the same, a hull's waterline closes round it.
  The way-in line in `model-panes.txt` now names the shapes that crossed, the most triangles first (`ShoreField.CrossingShapes`).
  The channel then stood WHITE THROUGHOUT, between crests too (said from the live client, v0.1.298), while the game draws it blue-green
  between crests and the pier's sheltered side dark to its wall. That is the field waves' foam as written: GetFieldFoamProfile adds
  `saturate(exp(-(shore_dist - 150) / 100)) * 0.7` - whole foam within 150 of a shore - to a breaking foam of 0.4 to 0.7 near one, and
  the channel (some 300 to 450 units across, off v0.1.296's `shore.png`) lay wholly near the quay. So A WALL OUT OF DEEP WATER IS NO
  SHORE from v0.1.299, CHOSEN from the live client: land counts towards the shore's distance where ground lies beside it under no more
  than `ShoreField.BeachDeep` (300) of water, or where what stands in the sea stands; land meeting only deeper water is a wall
  (`ShoreField.WallCells`, grey in `shore.png`). Port's ground under its water lies 55 deep at most or past 300 (its beach sheet, 21 to
  176 against a level of 125). The way in still runs to every land, walls included. What the engine counts is its OceanMarkupField's,
  made from an int grid `ClientTileMap::CalculateMapMarkup` fills - a method that takes and returns nothing, so it keeps what it works
  out - and the terrain at AreaInstance + TerrainMetadata is a C++ object of its own, the likeliest ClientTileMap: `ocean-markup.txt`
  (`OceanMarkupHunt`, run at the press) reads it and one hop beyond every pointer in it for lists whose length is a grid of the area's
  shape, each written as a map with its values beside the walkable grid and the corners' ground types; `ocean-markup-replay.txt` reads
  the maps back. Read it before the rule is changed. Still to come, asked for from the live client: the crests' crash against the
  quay, the spray thrown up it and the foam running down its face. The spray is a particle effect water.ffx's CrashingSprayTrigger,
  BreakingSprayTrigger and FloodSprayTrigger fire, named in no file read: from v0.1.300 `water-shore.txt` also searches every effect
  graph, particle, effect package and trail (`CaptureWaterShore.EffectExtensions`) for the spray's words, writes those found whole,
  and searches again for every file naming one of them by name - read it before the spray is drawn.
  v0.1.299 LEFT THE CHANNEL WHITE (said from the live client, crash on and off): its capture (v0.1.300) still put the quay's edge
  at a shore's distance of nought, 495 cells walls elsewhere. The rule had kept what stands in the sea shore whatever the water beside
  it, and Port's quay is faced with spraying stone (`MSHD_GraveBrickTile01c`) standing out of the water, over ground that is the
  quay's top (template z 0 to 62) or the floor 375 under (z 500), nothing between (v0.1.296's ground.txt). From v0.1.301 WHAT STANDS
  IN THE SEA TAKES THE SAME TEST: rocks on a beach stay shore, a faced quay or pier over deep water is a wall
  (`ShoreField.StandingWalls`). With crash off that capture's channel showed foam in rectangles with straight edges; the harbour's
  planes (`WaterPlane3x3`, 69 apart from 34 to 379 in its `.tmd`) reach past its 15 sub-tiles and may lie over the fill tiles' own,
  and water drawn unlifted blends once a shape - so `model-panes.txt` now counts the cells under two water shapes or more
  (`ShoreField.OverlappedCells`, `MostLayers`). The ocean markup hunt runs in the area the player STANDS in: that capture's was the
  Canal Hideout's (no sea, no markup grid), and found two unnamed grids beside the known ones - terrain +0x100 and +0x118, the walkable
  and projectile grids' near twins, and +0x638, eight bytes a tile corner (schema `Grid100`, `Grid118`, `CornerWords638`).
  THE SPRAY'S GRAPHS FOUND (v0.1.300's water-shore.txt): `Metadata/Effects/Graphs/GPUParticles/Update/Water/` holds
  `CrashingWavesTriggerCondition.fxgraph` and its breaking and flood kin, and no particle, effect package or trail names them - a GPU
  particle (.pet, JSON) names an `update_material` and a `render_material`, and the material names its graph; the program names
  `splashywater` (Port's ocean material sets it), `NonsplashyOcean` and the environments' `water_splash_ao`. So from v0.1.302 the part
  follows the chain hop by hop (`CaptureWaterShore.SprayHops`): the trigger graphs whole, the materials naming them, the particles and
  trails naming those, the effect packages, objects and environments naming those - materials and animated objects walked for the
  chain alone (`ChainExtensions`), a file name under six letters not looked for.
  PORT ANSWERED (v0.1.302's capture, the player on the pier's end, crash on): the channel goes blue-green between crests (the
  live client's two videos), 1153 cells walls, 658 of them standing stone; NO CELL UNDER TWO WATER SHAPES - the crash-off
  rectangles are not layers blended twice, their cause open. THE SPRAY'S CHAIN WHOLE: CrashingWavesTriggerCondition.fxgraph,
  its update materials under `Metadata/Effects/Spells/environment_effects/Act4_FOUR/crashing_waves/mat/`, the GPU particle
  `crashing_waves/FX/foamCrash_01.pet` naming them, and the objects carrying it - `crashingwaves_01.ao` to `_03` and doodads
  of other areas' island tiles (`oceancliffwaves_01.ao`, `*_crashingwaves_01.ao` under `Terrain/Islands/Tiles/Island/` and
  the prison intro): the spray is LAID AS A DOODAD where a tile wants it, firing as a crest passes. Port's rooms, tiles and
  loaded files name none of them (a cached file goes unlisted, so it is not settled), and the game's picture at the pier's
  end shows the crests' churn on the water, no spray in the air. The OCEAN MARKUP HUNT found no int grid of the tiles' or
  cells' shapes in Port's terrain or one hop beyond, but a byte grid of one value a three by three cells at terrain +0x6D8
  (1127 x 483; the Canal Hideout's 460 x 460) it could not draw - drawn from v0.1.303 (`OceanMarkupHunt.CellSteps`), schema
  `Grid6D8`; and `CornerWords638`'s nine kinds go with the corners' ground types for the most part. v0.1.305's capture drew it
  NOT: the steps counted the walkable grid's width, its stride twice (3382), where Port's 147 tiles are 3381 cells - from
  v0.1.306 over tiles x 23, the part cell kept as a shape of its own.
  THE WAVES CROSS THE CHANNEL THE OTHER WAY (v0.1.305, a video of the game and the Monster Book side by side at the checkpoint by
  the ship, crash on; the foam's brightness along lines through the channel, frame by frame): in the game the foam front reaches
  the hull's side before the quay's (0.3 to 0.7 s across 220 px) and the gangway's end before the stern's (0.3 to 0.9 s over
  730 px) - two recordings agree; in the Monster Book it runs along the channel from the stern in (-1.3 to -2.0 s), round the hull
  as the way in (`ShoreField.Oceans`) routes it. So the game's waves come from the ship's side, as they would were the ship no
  obstacle to them - which a markup made from the tile map (`ClientTileMap::CalculateMapMarkup`) would not know of. A reading,
  not yet settled: Grid6D8 drawn is to say whether the engine's grid holds the ship.
  IT CANNOT (v0.1.306's Port capture): Grid6D8 drawn is a COARSE WALKABLE GRID - 0 on 99% of the walkable cells, 255 on the sea and
  the town's fill alike, 127 and 180 along the edges, its map the walkable grid's at 1127 x 483 shape for shape (schema `Grid6D8`) -
  and the hunt found no other grid of the area's proportions in the terrain or one hop beyond. So THE WAY IN RUNS BY THE GROUND
  ALONE from v0.1.307, CHOSEN from the live client on the video's evidence: the open sea measured from the ground and nothing but the
  ground in the waves' way (`ShoreField.Way`); what stands in the water stays shore for the shore's distance. The way round it as well
  is kept as the field's twin, the light panel's water row's "way round" (`SceneLight.WaterWayRound`), so the two can be held against
  the game; `model-panes.txt` counts both and draws both, `shore-way.png` and `shore-way-round.png`.
  MEASURED (v0.1.307, a side-by-side video at the checkpoint by the ship, crash on, way round off): the Monster Book's front now
  reaches the channel's gangway end 0.6 s before its stern end (correlation 0.62) and the hull's side before the pier's, as the
  game's does (the game's half there: the quay side 0.67 s after the hull's; the rest of its lines lay under the Monster Book's
  window), both at 5.2 to 5.4 s a wave - "by far the best Port scene built yet, identical to the game's look and waves but for
  the smallest details" (said from the live client). Its capture's way in by the ground: 5,575 cells shorter than their distance to
  the ground, 3 unreached; round what stands: 8,683 and 399.
  THE CRASH-OFF RECTANGLES are asked after again (from the live client, v0.1.308: crash stays on - it is what makes the breakers the
  game's - but the processor draws no field waves, the probe's click among it, and the swell's foam under them is the same).
  Not the shallows: their foam term (`-(shoreDist - 0.18) * 10`) is on along Port's two beaches alone (v0.1.307's shore.png: 3,252
  water cells under 0.18 colour units, none in the channel). So from v0.1.309 the probe writes the swell's foam term by term at a
  water pixel (`ProbeSwell`, `ShadeProgram.SwellProbe`, the drawing's own `FoamTermsAt`): the shore field's cell and what it is, the
  shore's distance, the way in, the tide's phase, the cross-section read there, the depth the view crosses, the trail, the front,
  the shallows and the cover. Probe a rectangle's inside and its edge with crash off, then capture.
  On v0.1.309 the rectangles were no longer to be seen (said from the live client, with a picture) - though that build changed
  nothing they could come from, only the probe. Their cause is NOT FOUND; if they come back, probe one and capture.
  THE CRESTS RISE WHITE BY THE SHIP (asked from the live client with v0.1.321's video: "some waves seem to come up from under the
  ship again"). Nothing in the waves changed after v0.1.307, and v0.1.307's own measuring video shows the same. The direction is the
  game's (the front reaches the hull's side first and the quay about a second later), but the Monster Book's crest is white foam from
  the hull on, where the game's stays a dark ridge with a few streaks over the first two thirds of the channel and whitens only near
  the quay (the brightness along lines across the channel, frame by frame: crests 100 to 150 against 207 to 252 near the quay; the
  Monster Book's 225 to 235 from the hull). The field waves' foam is the break phase's - `1 - shore * 0.0002` ramped about
  `0.5 + 0.3 * noise` (GetBreakPhaseField) - and the shore's distance there is the CHOSEN field's: v0.1.319's `shore.png` puts the
  channel at 1,000 to 1,600 units, measured straight through the pier to the rocks behind it (the quay a wall since v0.1.299/301),
  which ramps to 0.66 to 0.8 - where `PolyStep(break, 0.53, 0.95)` already lets foam form whole (0.999 at 0.66). The engine's own
  field is unread and its noise stood in for. So from v0.1.322 the probe writes the field waves term by term at a water pixel
  (`ProbeField`, `ShadeProgram.FieldProbe` - the card's functions on the processor, which draws none): where the card's lifted water
  meets the view (the nearest crossing, marched), the shore's distance and the way in, the noise and the break's centre, the break
  phase, the wave's coordinate and the crest's lift, the foam's profile term by term (the break's share, spike, tail, constant, the
  near-shore term) and what it covers at the level the card reads it. Probe a crest by the hull and one by the quay, crash on, then
  capture - before the shore's distance or the break is given a rule.
  ITS CAPTURES (v0.1.322, Port's built stage, crash on): the crest by the hull at a shore's distance of 1,372, the noise 0.274 (the
  break's centre 0.582), the break phase 0.649, the break's share 0.999, the near-shore term nought, covering 0.98; the crest by the
  quay at 1,076, 0.213 (0.564), 0.735, 1.000, nought, 0.93 - both breaking whole, the hull's the whiter, the quay no shore under the
  wall rule. PolyStep(break, 0.53, 0.95) runs from 0.13 at 0.50 to 0.79 at 0.56, which at these noises is a shore's distance of
  about 1,900 to 2,000; the game's look - dark by the hull, white by the quay - puts that threshold inside the channel, some 600 to
  700 units farther out than this tool's field has it. So from v0.1.323 THE BREAK MAY TAKE THE GROUND'S SHORE ALONE, CHOSEN from the
  live client as a switch to hold against the game (the light panel's water row, "break by ground", `SceneLight.WaterBreakByGround`,
  off by default): `ShoreField.GroundTexels`, each cell's distance to the shore of the ground alone - what stands in the sea, the
  rocks and the pier's stones, water to it, the walls no shore as before - the card's shore texture's third float
  (`ShoreField.CardTexels`, `ShadeShoreAll`), taken by GetBreakPhaseField where the swell register's w is two; the near-shore term,
  the swell, the beach and the spray keep the shore's distance with the rocks. A reading, not the engine's: its OceanMarkupField is
  made from the tile map (ClientTileMap::CalculateMapMarkup), which knows no doodad. `model-panes.txt` counts the water cells the
  ground's shore puts farther and draws `shore-ground.png`; the probe writes both distances and which the break took.
  WHAT IS LEFT OF PORT'S WATER, DECIDED from the live client (2026-10-28) with the Monster Book otherwise "done" for Port: the water
  running down the quay's face after a wave first, then the refraction and its caustics, then the wet sand, then the rocky shore - each
  on the card alone, as the field waves are, the processor's picture saying under it what it leaves out; the flood waves left out, never
  seen in Port. THE QUAY'S RUNOFF (a video from the live client, v0.1.331): a crest climbs the quay's stone face, and as it falls back thin
  bright streaks run down the dark stone for about a second while the face looks wet - CrashingFoamSurface's own look, its foam
  scrolling down a face at rest and its wetness. The quay's facing stone sprays (v0.1.301), so the node runs there already and something
  in it leaves the face dry: from v0.1.332 the probe writes it term by term on any fragment whose program sprays (`ProbeSpray`,
  `ShadeProgram.ProbeSprays`, the drawing's own run): what the graph handed it, the shore field's cell and water level, the shore's and
  the way in's distances, the noises and the crashing wave's coordinate, the spray's phase and strength, the band's three factors (its
  height absolute z, not over the water's level), the fades, the scroll, the cover and the wetness; a fragment's line says whether its
  program carries the spray at all. Probe the quay's face just after a crest, crash on, then capture - before any rule is changed.
  THE QUAY'S RUNOFF IS THE ENGINE'S WETNESS PASS, NOT ITS SPRAY (v0.1.335's capture, the probe over the quay's face; v0.1.336). The
  quay's stone (`MSHD_GraveBrickTile01c.mat`) hands CrashingFoamSurface its curvature as `lerp(IndirectColor.w, 0.2741, 0.0377)` - its
  ambient occlusion - and `TRO_MossBlend.fxgraph`, earlier in the same material, sets that w to one at Texturing_Calc after reading it for
  its dust: the curvature comes to 0.973, the probe's to the bit, and RampNorm against it leaves the node's foam and wetness at a few
  hundredths even at the water's line. As the files are written the spray cannot wet the quay in the game either. What does is the
  engine's wetness pass (common.ffx's `wetness_tex`, the debug views "Wetness (Caster)" and "Wetness (Receiver)"): a material whose blend
  mode is `Wetness` writes its alpha into the target - the water by WaterComputeWetness, `1 - saturate(1 - gap * 0.03)^3` of how deep it
  covers what lies behind; the beach waves by `BreakingOceanWavesWetness.fxgraph`, BreakingWavesFragment's wetness, the film's subsurface
  past a tenth and at least 0.8 within 1.4 waves of the waterline - and a receiver's `specular_wetness_mult.fxgraph` reads it at its pixel
  (ReadPixelWetness), darkening its albedo to three tenths, glossing it to 0.95 and flattening its normal. 20 of the stage's 35 written
  materials read it - the quay, the rocks, the crates, the ship - and the sand none (PBRGroundBN alone), so the beach's wetness wets what
  stands in its film, not the sand. DRAWN ON THE CARD from v0.1.336 (`ModelGpu.Wet`, `ShadeProgram.ReadsWetness` and `Wetness`, the light
  panel's water row "wetness", on by default): cast after the picture into a pair of sixteen-bit targets (`ModelTarget.Wetted`) and read
  the next frame, so the solid part is walked once; drying over `ModelGpu.WetFade`, a second, CHOSEN from the live client's video of the
  quay (the engine keeping its target from frame to frame or not is in no shader); a moved picture starts dry. The processor reads every
  receiver dry, its picture as before. NOT DRAWN: the receiver's gloss and normal (NormalVarianceToRoughness and
  CombineTbnNormalsFromInput are not compiled, so both stay lost as they were) and its MultiplySpecularLight (CustomLighting, past the
  colour's stages); `specular_wetness_mult_wspace.fxgraph`, wet by height (`0.6 * (z - offset)`), leaves the colour as it is.
  READ IN v0.1.336'S CAPTURE: `water_default_wetness.fxgraph` is WaterComputeWetness's opacity as the alpha (albedo one, lighting off)
  over the mesh's own place (TransformToWorld, no lift) - so the sea casts from its STILL surface and wets nothing above the still
  waterline; it names its blend mode BackgroundGBufferSurfaces where the river's (`water_flowing_wetness.fxgraph`) names Wetness,
  unexplained. CrashingWavesFragment has no wetness output and no graph casts one for it.
  THE QUAY HELD AGAINST THE GAME (v0.1.338; a side-by-side video at the quay, crash and wetness on, and two probes on its face from
  the live client). Every face pixel the water reached, its brightness over the dry face from the moment the water left: in the game
  bright foam stays and fades over about 1.5 s (3.8 times the dry face to 1 in the wide shot, 4.2 to 1 in the close one), the face
  hardly darker after ("in the game nothing really gets darker - the water visibly runs down the wall for a second or so", said from
  the live client); in the Monster Book no foam at all, the face at 0.64 to 0.70 of dry at once and dry again at 0.8 s, snapping back as
  the receiver's `SmoothStep(wetness, 0.068, 0.92)` lets go under 0.07. That dark band was the cast from the lifted crest, which the
  engine's graph does not do: from v0.1.338 THE SEA CASTS FROM ITS STILL SURFACE (ModelGpu's Wet, `_placed`), DECIDED from the live
  client. THE RUNOFF IS CrashingFoamSurface's - its foam scrolls down a face at 100 a second as the spray dies away, a second or two a
  crest - and as compiled it cannot show on the quay: the curvature is `lerp(IndirectColor.w, 0.2741, 0.0377)` (OceanSprayFoam's
  OceanSpray_Intensity), the occlusion, which TRO_MossBlend sets to one at Texturing_Calc after reading it for its dust; the probes 63
  and 69 units above the water put the foam's strength at 0.003 and 0.002, a hundredth at the band's best, three hundredths of cover
  even where the foam texture's alpha is one (the cover passes where `SmoothStep(foam, 1 - Gain(alpha, 0.1), 0.5)` does). On the rocks,
  which carry no moss, the curvature is the stone's own occlusion and the foam gathers in its cracks. So either the reset does not
  reach the spray in the engine, or the runoff is something else - the crashing waves' own surface climbing the wall. The first is
  DRAWN BEHIND A SWITCH, CHOSEN to hold against the game: the light panel's water row "spray from occlusion" (`SceneLight.SprayOccluded`,
  `ShadeProgram.Occluded`: the material compiled again with every reset of the occlusion before the spray's read left out - a write of
  IndirectColor whose w is a `One` and whose rest is handed on as read; OceanSprayFoam's own reset at PreLighting_Calc, after its read,
  makes no twin), both drawings taking the twins (`MonsterPortrait.Occluded`). The probe's spray line now runs the node at its place
  moved up the face, -25 to 300 above the water, a tenth of a second apart over one crashing wave (`SprayRise`, `ShadeProgram.Rising`):
  the most it covers at each height, when, and for how long over a twentieth - a single probe almost never lands on a texel dense
  enough to streak. THE BLACK LINES (said from the live client with a picture, v0.1.336: thin black straight and cornered lines in the
  water, moving with the waves): not yet found. Port's harbour is 48 `WaterPlane3x3` (2,842 triangles each, 750 a side) laid edge to
  edge 750 apart; a corner of one in the middle of the other's edge would crack open onto the black sea floor once lifted, so
  `model-panes.txt` counts the water's seams as drawn (`WaterSeams`: border edges meeting corner to corner, T-junctions, the water's
  own border) - read it before the water's mesh is changed.
  THE RUNOFF HELD AGAINST THE GAME (v0.1.339, a side-by-side video at the quay from the live client, "spray from occlusion" on): the
  Monster Book's face, every pixel the water reached, after the water left - foam at 2.9 times the dry stone fading to 1.07 over about
  2 s, no dark band; the game's 3.8 to 4.2 times fading to 1 over about 1.5 s (v0.1.338's measurement). So "spray from occlusion" IS ON
  BY DEFAULT from v0.1.340, DECIDED from the live client - still a reading of the engine's (whether the moss's reset reaches the spray
  in the game is not read), the switch keeping the files as compiled. And THE BLACK LINES WERE GONE on v0.1.339 (said from the live
  client with a picture) - after crash, wetness and shadows had each been switched off on v0.1.336 without taking them away. Their
  cause is NOT FOUND; if they come back, probe one at once (the probe names every fragment under the pixel) and capture
  (`model-panes.txt`'s water seams).
  THE RUNOFF WENT WITH THE TILESET'S SWAPS (said from the live client, v0.1.374: "spray from occlusion no longer works in Port"; its capture,
  the probe on the quay's face). The quay is TILES (Port_Boss_01.tgt and the OceanEdge templates name MSHD_GraveBrickTile01c.mat), and from
  v0.1.344 a laid or built room puts the master's TileMaterialOverrides on its tiles, as the game does (Crimson Shores matched the game only
  so): Port's material_overrides.tmo swaps MSHD_GraveBrickTile01c.mat for `Art/Models/Terrain/Maps/Port/Textures/Architecture/
  MPRT_GraveBrickTile01c.mat`. So the GAME'S QUAY WEARS MPRT, and that answers v0.1.338's question: its moss is MaterialBlend_GradientUV, whose
  write of the occlusion is `lerp(occlusion, the moss normal texture's z, a moss mask)` - no reset to one - and OceanSprayFoam after it
  (OceanSpray_Height -200, OceanSpray_Intensity -0.325) reads the stone's own, moss-lerped occlusion: the runoff the twin stood in for. The
  compile drops it: the moss mask transforms the normal map's normal through the TBN basis (`Transform(InputTbnNormal, InputTbnBasis)`), which
  ShadeProgram refuses with no mesh tangents; the occlusion's write is lost, OceanSprayFoam's read of it after, and the spray with it - so the
  quay draws no runoff with the switch on or off. The same refusal is among the stage sweep's largest causes (MaterialBlend_GradientTriplanar
  alone 2,804 rooms). Being mended by compiling the transform over the engine's screen-space tangent frame (FixModelTBN's, which the
  parallax already reads). From v0.1.375 `model-panes.txt` writes every master swap a stage wears beside its original, what each can do
  (sprays, the twin, wetness read and cast, water, alpha) and LOST: / GAINED:, both whole where they differ - this swap reads "LOST: sprays,
  spray from occlusion" with no probe click.
  THE CAUSTICS' AND THE ROCKY SHORE'S TEXTURES. water.ffx's GetCausticsBias reads `distortion_sampler` (red and green as a vector,
  `(rg * 2 - 1) * 0.4`, at the place times 0.001 drifting with the clock) and `caustics_tex_sampler` (its green, `pow(g, 1 / 4.2) * 2 - 1`,
  at the place times 0.002), both wrapping, and GetClashingWaterPlanarState and the beach foam's leading edge read the same distortion -
  pass textures the engine binds, which is what stopped all three before. The game's program names
  `Art/particles/environment_effects/waterfalls/twilight_strand/water_caustics_2.dds` (and the source's own comment over the caustics
  says "for water_caustics_2.dds") and `Art/particles/distortion/muddle_double.dds`. Which files they are is to be SETTLED BY THE FILES, as
  the swell's profile was: from v0.1.332 the water textures part measures every candidate (`WaterPassTextures`: those two, the install's
  other caustics, flow and noise textures, the program's names of the same words) as the shader reads it - each channel's spread, the seam
  across its wrap against a step inside, red and green's likeness, the distortion's mean vector, the caustics' bias - and writes each whole
  (`water-pass-N-name.dds`); `water-pass-replay.txt` measures them again offline. The refraction's own march (GetRaytracedLightAdaptiveStepping)
  and its dispersion (ComputeDispersedColor, 0.2) are in water.txt whole and need neither.
  THE REFRACTION IS DRAWN ON THE CARD (v0.1.341, DECIDED from the live client: a switch, on by default; its caustics wait for their
  measurement). `ModelShaders`' Refraction ports GetRefractedOpaqueSurface but its caustics line for line: GetRefractedDir from the air
  by the area's index (`WaterLook.RefractionIndex`, `water.refraction_index`: Port's default 1.33 MEASURED in the parsed environment,
  The Red Vale's file 1.30095) thinned to one by the shore's ratio; GetRaytracedLightAdaptiveStepping's three steps over the solid depth
  (`SceneLight.RefractionSteps`), a point on the picture put back into model space through Clip's inverse (`Unclip`, the engine's
  GetViewPoint); ComputeDispersedColor at 0.2, a shift at most 0.003 of the picture (`SceneLight.WaterDispersion`), over the solid
  part's copy (`ModelTarget.Saw`); and GetRefractedWaterLight's share that gets through over the bent ray's own length, not the straight
  view's depth. The water then covers whole, the bent colour mixed in by the share the layer let through, in the picture's own numbers
  as the blend mixed it - so at an index of one it draws what it drew before (`THEREFRACTIONAtAnIndexOfOneDrawsWhatTheStraightViewDrew`),
  and over a floor of two halves under still water each pixel shows the half the engine's march reaches, held against the march worked
  out over the floor's plane (`THEREFRACTIONBendsTheViewIntoStillWaterAsTheEnginesMarch`). The light panel's water row, "refraction"
  (`SceneLight.WaterRefraction`); the processor draws the water as before, and the line under its picture says so. A translucent shape
  earlier in the mesh's order and in front of the water is now covered by it where the blend let it show partly. NOT DRAWN: the
  caustics - GetCausticsBias times the pass's `water.caustics_mult`, which Port.env leaves unset, so its default is to be read off the
  parsed environment (light-hunt.txt) in an area whose file sets it. 39 environments do (water.txt): hideouts (Yaochi 1.605, Blank Sea 4),
  maps (Cenotes and Crimson Shores 3, Lake and Azmerian Ranges 4, Woodland 1.5, Rustbowl 1.401) and the Act 4 interlude's
  (`gallows/act4_intermission/p3_1.env` and `p3_town.env` 1.5, `p3_2.env` 4). legacy.ffx's water materials declare their own
  `water_uv_caustics_mult "Caustics" "0" "2" "1"` - a lead, as the medium colour's default was, not the pass's value.
  CRIMSON SHORES MEASURED THEM, AND THE OCEAN'S DEFAULTS WITH THEM (v0.1.341's capture there, light-hunt.txt; v0.1.343). Its file sets
  caustics_mult 3 and nearly every ocean key, each found in both copies of its parsed environment with a flag of one, where Port's hold the
  engine's defaults with a nought (schema `ParsedEnvironment`): caustics_mult +0x200, PORT'S 1 - MEASURED; directionness +0x208, 1; the
  wind's way, height and speed +0x250 to +0x260, 0, 1.06 and 1; the swell's angle, height, intensity and period +0x268 to +0x280, 0, 37.5,
  0 and 0.04 (the two noughts at +0x250 and +0x278 told apart by the keys' order, the wind's three and the swell's four each in their
  names' order - either way Port's swell intensity is nought); shoreline_offset +0x288, 0; flow_intensity's default 1 (Crimson Shores
  sets none). So THE OCEAN'S DEFAULTS ARE THE ENGINE'S from v0.1.343, DECIDED from the live client (`WaterLook`): the wind's height 1.06
  (CHOSEN 1.0667 before), the swell's intensity 0 (CHOSEN 1 - the swell takes its shore's shape late, its trail foam within 300 units of
  the shore where it stood 150 to 1,000 out, `THEFOAMRollsInBandsOnTheEnginesProfile`), the flow 1 where a file sets none (still water
  before); the rest were the engine's already. The wind's keys come in two spellings no file mixes - `water_wind_*` in 22 files, the maps
  (Crimson Shores: its slots MEASURED), `wind_*` in 30, the hideouts - and both are read as the ripples' wind (`WaterLook.Of`).
  THE CAUSTICS ARE DRAWN ON THE CARD (v0.1.343, DECIDED from the live client: a switch of their own, "caustics", on by default, with the
  refraction): `ModelShaders`' Caustics ports GetCausticsBias and GetCausticsIntensity line for line over the light where the bent ray
  ended - the distortion read at the end's place times 0.001 drifting with the clock, `(rg * 2 - 1) * 0.4`, moving the caustics' picture
  read at times 0.002, `pow(g, 1 / 4.2) * 2 - 1`, times `saturate(depth * 0.02 - 0.3) * 0.7` and the area's caustics_mult less the shore's
  ratio, the light times one more than that; both read as SamplerLinearWrap reads at level nought (`WrappedRead`, four loads), at fixed
  slots t123 and t124 clear of a program's sheets. THE TEXTURES SETTLED BY THE FILES (`ShadeProgram.Caustics`, the water textures part
  measured again in that capture): `water_caustics_2.dds` under the twilight strand's waterfall particles, which the program names and the
  source's comment calls - one greyscale picture, bright lines over a darker floor, tiling (the install's `General/Water/water_caustics_2_bc1.dds`
  holds the same picture) - and `muddle_double.dds`, which the program names too, the only distortion candidate both centred on nought and
  tiling; read off the drawing thread as the install opens (`ShadeProgram.UseCaustics`), undrawn until both are in. READINGS, said under the
  picture: the ground water ratio nought, as everywhere it is not taken (the deep water's frequency and intensity throughout); the
  multiplication made on the solid part's light after its exposure and grade, through the tables, where the engine's is before; the
  pattern's phase following the picture's frame, a room's or a stage's, not the area's world. `THECAUSTICSPatternTheBentLightAsTheEnginesBias`
  holds a white and a black caustics picture over still water 150 deep, where the intensity comes to its most: the light seen through
  times 1.7 and 0.3.
  THE GAME'S CRESTS RUN ALONG THE PIER (a side-by-side video from the live client, v0.1.332, the game left and the Monster Book right at
  Port's pier, crash on): every pixel's phase at the waves' own period - 5.5 s in the game, 5.3 in the book, both the field waves - put the
  game's crests running along the pier's sea face, some 17 degrees off it and away, and the book's square onto it at about 49 degrees, in
  rings round a point off the quay ("way round" was on; by the ground the lines wrap every face alike, v0.1.329's `shore-way.png`). As
  though the pier and the quay - walls out of deep water - were in no wave's way, as the ship was found to be (v0.1.305/307). So two things
  from v0.1.333, DECIDED from the live client: a third way in, PAST THE WALLS (`ShoreField.Way(round, pastWalls)`, `Steering`, `WallReach`,
  the light panel's "past walls", `SceneLight.WaterPastWalls`; CHOSEN to hold against the game): each land cell belongs to its nearest
  coast, and ground whose nearest coast is a wall within 800 units (400 in v0.1.333) neither stands in the way nor is measured from - the line under the
  picture names the way drawn; and the capture writes every shore field as NUMBERS (`shore-field.bin`, `CapturePanes.FieldBytes`, its
  format in `FieldFormat`; `ShoreField.Kinds`' bits, the shore's distance, all three ways in and the ground's shore, world units;
  `shore-field-replay.txt` reads them back) beside a third picture, `shore-way-past.png`. With the capture's own camera (`capture.txt`) and
  the boss room's placing (`rooms.txt`), a side-by-side video recorded while F9 is pressed, the player not moving, reads the game's crests
  into the room's frame and sets them beside each way in - which one, if any, the game's is, is measured there before a default changes.
  ITS FIRST MEASUREMENT (v0.1.333's capture and a video at its spot, the player not moving; the stage laid as the area laid it, so its
  frame is the world's less the room's corner, tile 127, 19 x 250 - the field's walls drawn through the capture's camera lie on the pier's
  waterline in the game's picture). Every pixel's phase at the waves' period, 5.05 s, read onto the water's plane (the field's level, 125),
  its slope set beside each way in's over 1,831 water cells west of the pier: the game's crests run ALONG the pier towards the land. Off
  the game's way, the median and the share within 60 degrees - by the ground 81 and 20%, round what stands 82 and 19%, past the walls at
  400 75 and 31% (the pier is some 900 across in the field, so its middle kept steering), past the walls at 800 or any reach beyond 27
  and 90%, the straight distance to the nearest beach the same; left over, a turn of about 21 degrees the same way throughout, its cause
  unread, and a wavelength of 814 units against the engine's 1,000. So `WallReach` is 800 from v0.1.334, and "past walls" stays a switch
  (DECIDED from the live client) until the channel between the ship and the pier - where by the ground matched the game (v0.1.307) - is
  measured the same way. The reading is scratch arithmetic over the capture's own files (`capture.txt`'s camera, `shore-field.bin`): no
  part reads a video.
  THE CHANNEL MEASURED (v0.1.335, over v0.1.334's `shore-field.bin` and the game's half of v0.1.307's side-by-side video at the checkpoint
  by the ship - the one recording whose game picture shows the channel; v0.1.334's own video lay under the Monster Book's window there).
  That instance laid the boss room otherwise, so its frame is fitted on the two objects of the room both instances hold,
  `Checkpoint_Endgame_Boss` and `BossForceFieldDoorVisuals`: 725.1 units apart in both, a quarter turn between them, `(x, y)` to `(y, -x)`
  about the first - a mirror would want its axis 58 degrees off the grid - and today's waterlines drawn through v0.1.307's camera lie on
  that picture's quay and hull. Over 272 water cells of the channel's land half the game's crests cross it from the hull to the quay, ten
  degrees towards the land; off the game's way, the median and the share within 30 degrees - by the ground 15 and 76% (98% within 60),
  round what stands 72 and 10%, past the walls at any reach 27 and 54%, its crests turned towards the channel's sea end, the straight way
  to the nearest ground that steers. The pier measured again from v0.1.334's video at v0.1.333's spot: by the ground 82 and 6%, past the
  walls 22 to 26 and 56 to 67% - the first reading holds. SO NO WAY IN READ SO FAR FITS BOTH: "past walls" stays a switch and by the
  ground the default, as DECIDED from the live client. A lead, not a rule: the game's corners name no ground on the pier and on the quay
  at the channel's head (ground-grid-replay.txt's null slot, tile columns 131 to 133 of that instance); leaving every cell that touches
  one out of the land fits the pier at 18 degrees (69% within 30) but the channel at 38 (41%), the quay's ground going with it. The
  engine's OceanMarkupField stays unread.
  ITS NUMBERS ARE SOUGHT IN MEMORY (v0.1.342, asked for from the live client over drawing the engine's ocean markup views from the
  tool's own field). The engine's `debug_ocean_markup_deep` and `_land` (render-debug.txt, v0.1.318) and its water read the field through
  ShorelineFieldData: GetShoreData samples `shoreline_coord_tex_sampler` at `(tilemap_pos - shoreline_coord_planar_aabb.xy) / (aabb.zw -
  aabb.xy)`, steps by `scene_planar_size.x / shoreline_coord_tex_size.x`, and makes world distances `(1 - r) / dist_to_color` (the shore)
  and `(1 - b) / dist_to_color` (the open sea) - pass uniforms the processor sets, while OceanMarkupField makes the texture on the card.
  So `ocean-markup.txt` goes on, after the terrain's grids, to the whole of memory on every core (`ShoreFieldHunt`, ninety seconds at
  most): the scene's planar size - the flood waves read `planar_pos / scene_planar_size.xy`, so it is the area's in world units, 250 a
  tile - both ways round, and the walkable grid's size in cells, and beside every place found the texture sizes, boxes, int pairs and
  small factors standing there, tallied over the windows, each window written whole; `ocean-markup-replay.txt` lists them again from the
  file. The tool's `ShoreField.Reach` of 2,000 a colour unit is a dist_to_color of 0.0005 - read the hunt before it is changed.

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
cannot run as one program, and the line under the picture says why. NOT DRAWN, DECIDED from the live client (drawn behind a
switch from v0.1.311 - see THE ENGINE'S HEIGHT BLENDING below): the engine's
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
THE SHAPES ACROSS THE ATLAS ARE MIXED BY THEIR CORNERS (v0.1.310), CHOSEN from the live client with the old mix kept behind a switch -
built before the comparison settled anything, and said so. A black rectangle in squares lay on the sand by the ship's stern in Port's
built harbour (v0.1.309's probe there: `blackc.mat + G_KGB_DrySand02.mat by newmask.dds's green`, the green 0.004 - the ocean's black -
beside squares at 1.000). It is the checkerboard above: Port_Boss_01 has 18 ground shapes in 17 sub-tiles of two grounds whose block
coordinates reach a cell of the atlas or more across (c4r2, c5r2, c6r2 to c6r6, c1r6 to c5r6, c13r10, c13r11, c14r11, c14r12, c15r12 -
the shore strip, the slopes and the pier's edge). That capture's comparison settles nothing: on the screen the two candidates follow the
picture no better than the same fields moved (IslandOcean + TropicalFill, 312,534 pixels in 17 sub-tiles: brightness mask r -0.002
against -0.047 +/- 0.131 moved, corners +0.237 against +0.198 +/- 0.138), most of those sub-tiles lying under the water. So
`GroundPainter` now mixes exactly those shapes - a shape of a two-ground sub-tile, in a definition with a ground grid, whose block
coordinates span 64 texels or more of the mask along either axis, the capture's own rule (`GroundPainter.AtlasCell`, `Across`) - by the
second ground's share of the sub-tile's four corners, bilinear (`CaptureGroundCompare.Share`), worked out per vertex on copies of the
shape's vertices and carried in the coordinates' first channel (`ShadeProgram.GroundsByCorners`, worn as `GroundBlend.Cornered`); every
other shape stays on the mask. The Monster Book's stage switch "ground by the mask's cells" (`RoomKey.MaskCells`, the key word
`maskcells`, a laid or built room) keeps them on the mask as before, squares and all, for holding the two against the game. The line
under the picture counts both, the probe says which weight a pixel took, and `model-panes.txt` writes the corner mix as such.
Held against the game (said from the live client with two pictures of the stern, switch on and off): the squares are gone. Held
against the game's picture of Port's quay by the crates and sandbags (v0.1.309's capture, the cobblestone and the dry sand, 685,179
pixels in 18 sub-tiles): neither the mask nor the corners' share follows it better than the same fields moved (brightness mask r
-0.139 against -0.090 +/- 0.036, corners -0.131 against -0.111 +/- 0.037) - the game draws cobblestones standing out of the sand,
dense where the corners' share puts sand. So the corner mix stays the default as the better of the two, not as the game's.
THE ENGINE'S HEIGHT BLENDING BEHIND A SWITCH (v0.1.311, asked for from the live client on that picture). GroundInterpolate (ground.ffx,
whole in ground.txt) as written (`ShadeProgram.Grounds`, `Mixing`): where GROUND_HEIGHT_BLENDING is defined - the macro of the fragment
EnableGroundHeightBlending, which PBRGroundBN.fxgraph carries and which is the whole ground shader's, so either ground turns it on
(`ShadeProgram.BlendsGroundHeights`) - each weight is multiplied by `1 / (1 + (max_height - height) / 0.01)`, the weights divided by
their sum, and the two surfaces added up by them. A ground's height is what its graphs write (the Height extension point, a channel
the compile now follows), nought where none writes one (InitSurfaceData; blackc.mat's BasicColour). PBRGroundBN writes it as the
SmoothStep node of its normal map's BLUE (`NormalAOGloss`, read at the graph's own coordinates, half the projected ones) with
`Height_Center` and `Height_Steepness` - ConstantFloats Port's ground materials leave at 0 (ConstantFloat's default) and 1 - which
comes to `1 - (1 - blue)^1999`: one wherever the blue rises past about 0.003, nought only where it is nought. So two of Port's
grounds both near one weigh as before, and the height decides where one of them is nought - in the texture's darkest blue, or
blackc's - the higher taking a hundredfold for each unit between. The height costs the normal map's read, so it is compiled into
a twin of the program (`ShadeProgram.Heighted`, the real PBRGroundBN reading colour and normal where the plain one reads colour
alone) that only a mix weighed by heights takes. The Monster Book's stage switch "ground by the grounds' heights"
(`RoomKey.GroundHeights`, the key word `groundheights`, a laid or built room; off by default, as DECIDED before) turns it on; the
probe writes both heights and the share they came to. THE CAPTURE HOLDS IT AGAINST THE PICTURE TOO (v0.1.312, asked for from the live
client): ground-on-screen's comparison (`CaptureGroundCompare`) weighs the mask's green and the corners' share by the two grounds'
heights as the engine does - two candidates more, "mask by heights" and "corners by heights" - each ground's own program run at every
measured pixel with its reads at the level the picture would take (`ShadeProgram.GroundReader`, the renderer's own rule per triangle:
a coarser level averages the blue's noughts away, so a height read at the top level is one the game never saw), on every core. Beside
each pair it writes the heights - how often each ground's comes under a half, how far the weighing moved each share - and
`ground-on-screen-heights.png` tints the picture by the mask weighed by the heights, the engine's own mix as its shaders write it.
ITS FIRST CAPTURE (v0.1.312, Port's quay by the crates and sandbags) SETTLED IT FOR THE COBBLESTONE AND THE SAND: at the level the
picture reads, both grounds' heights stand over a half on every one of 1,017,832 pixels, and the weighing moved neither share by
anything (0.000) - the height blending changes nothing between them. It moves only against the ocean's black, which writes no height:
IslandOcean + TropicalFill by 0.275 (mask) and 0.454 (corners), the group with no name + IslandOcean by 0.391 (corners) - the black
giving way - and none of the four candidates follows the picture there better than the moved controls (the best, the mask's local
contrast, +0.409 against +0.317 +/- 0.060). So the switch stays off by default, and what lays the cobblestones through the sand is not
the mix: WHICH MATERIAL OF ITS GROUP THE GAME DRAWS is the next question. A group offers several with weights - Port's group with no
name Cobblestone01 to 03 and CobblestoneDirt01 twice (30, 25, 40, 20, 20), TropicalFill DrySand01 to 03 (30, 45, 25) - while the
painter takes the heaviest everywhere (`GroundPainter.Heaviest`). ground-on-screen measures it from v0.1.313 (`CaptureGroundChoices`):
every material each sub-tile's groups offer is run at every pixel at the level the picture reads (`ShadeProgram.GroundReader`), and its
brightness held against the picture's DETAIL - its brightness less its mean over nine by nine pixels, which the light, shadow and
grade leave the pattern in - with the moved controls; a winner is clear three spreads over its moved ones and a twentieth over the
next. Per group it writes how often each material won a sub-tile of that ground alone beside its weight, per piece its sub-tiles'
winners (one winner throughout says the game chooses per tile), every sub-tile's every r, and `ground-on-screen-choices.png` tints each
clear winner. No material lining up anywhere would question the coordinates themselves - the world times 0.004, the scale the
environments write and the offset a pass uniform no file holds.
ITS FIRST CAPTURE (v0.1.313, the same spot on Port's pier) MEASURED NOTHING, AND SAID WHY THE COMPARISONS OVER THE HARBOUR BEFORE IT
MEASURED NOTHING EITHER. Every material came to r 0.00 +/- 0.01, its controls alike. Held offline against the capture's own files (the
game's picture, the camera, the colour textures ground.txt writes at 256 texels): the game's cobblestones line up with the pier's
materials at r 0.08 to 0.29 - z 13 to 39 over the same material shifted where nothing lines up, the water round it 2 to 6 - at the
world times 0.004 halved by PBRGroundBN's own MultiplyConst2 (the colour repeats every 500 units), the tool's axes as they are (no turn,
no mirror), and within 16 units: across the pier the best line-up moved from (2, -12) to (4, -2) units, one shift, (12, 6), on a plane
16 units higher. MEASURED: the ground's texture coordinates are the world's times 0.004, as the environments write - the pass's scale
settled; its offset (ground_scalemove_uv.zw) and the camera's are within 13 units, which one spot cannot tell apart. Four units off the
line-up r is nought, and v0.1.313 read each material at the pixel's own place. Nor were its pixels the pier's: `LaidRoomModels.GroundsOver`
lifted each piece by its FIT to the area's ground - the mean of the area's height less the file's - and Port_Boss_01's sea floor, 500 down
in the file under the harbour's water where the area's grid says nought, drew the fit to -450. The capture drew the pier 450 units above
the game's: its sub-tiles' pixel counts come out as at -450 and no other height, within 2%. EVERY COMPARISON OVER PORT'S HARBOUR THROUGH
ground-on-screen - v0.1.262's tint edges, v0.1.264's and v0.1.310's candidates, v0.1.312's heights - WAS MEASURED ON THE WRONG PIXELS, and
its conclusion for the harbour is void until measured again (The Excavation's, v0.1.256, rests on whether its pieces' fits met their
levels, not checked). From v0.1.314 `GroundsOver` lays each piece at its tiles' own level, as the game lays a tile's doodads (Marrow,
Sulphuric) and Port's pier (this capture), and its line says how far the fit would have put each; and from v0.1.316 a LAID ROOM does too,
DECIDED from the live client on this evidence (`LaidRoomModels.Of`, the built stage at the level already) - the fit kept behind the Tile
Book's "fitted" switch (`RoomKey.Fitted`, the key word `fitted`; an older key's `level` is the usual now and reads as it). And the choices LINE UP (`CaptureGroundChoices`): the detail summed in cells of 2 units, each
material read once a cell and shifted up to 24 units each way for its best line-up, its controls the same material shifted 48 and 72
units; a winner is clear at z 5 and 0.01 over the next material, both CHOSEN, and the shift each winner lined up at is written. Offline,
on this capture's 256-texel textures: PORT'S FIVE COBBLESTONES ARE ONE LAYOUT, fewer stones in each, so a winner says which density the
picture shows - CobblestoneDirt01 round the player (c12r11 0.110 against Cobblestone03's 0.082, c13r11 0.084 against 0.057),
Cobblestone03 at c12r8 (0.194 against CobblestoneDirt01's 0.154), the dense ones within 0.01 of each other at the pier's far end. The game
does not draw the heaviest everywhere; whether it chooses per sub-tile, per tile or by some field is what the next capture's lines say.
ITS CAPTURE (v0.1.319, Port's pier again) LINED EVERY CLEAR WINNER UP AT NO SHIFT, (0, 0): the tool's place of a pixel is the game's to
within a cell of 2 units, so ground_scalemove_uv.zw is nought - MEASURED; the 16 units the offline reading needed were its flat plane's.
Of the 16 sub-tiles of the group with no name alone on the screen, 4 had a clear winner - Cobblestone03 three (c12r14, c12r17, c13r19),
CobblestoneDirt01 one (c12r15, 0.162 against 0.126) - and the rest stood within 0.01 of the next material; the two that win are the
group's two heaviest (40, and 20 + 20), Cobblestone01 and 02 (30, 25) never. Whether the game draws one of them a sub-tile by weight
or mixes them is not yet told.
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
  cell, not a flag. Whether a diagonal step counts as one is not settled. THE ENGINE'S OWN SOURCE AGREES (v0.1.317's
  `render-debug.txt`, the engine's debug views): utilitynodes.ffx's `SceneColliders` reads one texture (`walkability_sampler`)
  holding both - its z the walkability's distance and its w the projectiles', each `(value - 0.5) * 4` bilinear and clamped to
  `STATIC_COLLIDERS_MAX_DIST`, 400 units - and `debug_walkability.fxgraph` paints the first as a hue, distance / 400. How the
  grids' 0 to 5 map onto those units is not read.
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

## When the overlay stays away

AFTER AN ALT+TAB THE OVERLAY AT TIMES STAYED EMPTY FOR GOOD with the game in front (said from the live
client, v0.1.336: borderless fullscreen, a Monster or Tile Book open or not, a browser or another 3D program
in front meanwhile), and only ending the tool brought it back. The cause is NOT KNOWN; four readings fit it
and v0.1.337 guards against each and writes down which one it was:
- A LOST GRAPHICS DEVICE. Present's answer was dropped (upstream's loop), so a removed device drew into
  nothing forever. Now it is read (`Overlay.Presented`), and a removed, reset or hung device is made anew
  with its swap chain, ImGui's device objects and font atlas, ImGui's context kept (`RebuildDevice`); every
  texture holder forgets its handles (`EntityOverlay.DeviceRebuilt`: the panes, the card, the icons, the
  terrain and projectile sheets).
- THE WINDOW PUSHED OUT OF THE FRONT. It is put back on top as the game comes back (`KeepOnTop`).
- THE GAME'S WINDOW MADE ANEW. The handle read at attach was held for life; it is followed now
  (`GameWindowTracker.Follow`, `Game`), and the game's process in front through any of its windows draws.
- A RENDER THREAD THAT NEVER CAME BACK. A watchdog thread writes a frame standing still for 3 s and the stage it stood at.
The restore key (F10 unless rebound, Appearance → Keys) does all of it at once. Everything goes to
`overlay.log` beside the tool - the session before kept as `overlay.previous.log`, since the session that
went wrong is the one that was killed - and a capture writes both (`overlay-log.txt`). Read it before
changing any of this: the line as the game came back says whether the window was shown, on top, and what
Present said.

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
