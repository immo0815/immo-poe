# UI-Labels: Bestandsaufnahme und Umbenennungsvorschläge (Spieler-Seiten)

Scope: alle Seiten außer **Inspect**. Quelle: `src/PoEformance.Overlay/` (Pfade unten relativ dazu),
Style-Zeilen aus `src/PoEformance.Features/StyleCatalogue.cs` (`F:StyleCatalogue.cs`), Capture-Teile aus
`src/PoEformance.Features/CaptureParts.cs` (`F:CaptureParts.cs`).

## Regeln (wie entschieden)

- Label ist ein ganzer Satz (endet auf `.` `!` `?`) → Satzanfang groß. Sonst **alles klein**.
- Akronyme bleiben groß: `UI`, `HUD`, `NPC`, `DPS`, `ES`, `API`, `F9`, `MB`. Sonst auch Spielnamen klein
  (`expedition`, `ritual`, `atlas`, `runecraft`, `poe.ninja`, `poe2db`).
- Ziel ≤ ~4 Wörter; Erklärung wandert in den Hint.
- Jedes Label steht für sich (keine „and their paths“-Fortsetzungen) → abhängige Optionen als eingerückte Kinder.
- Einheiten ins Format, einheitlich: `px`, `units`, `ex`, `×`, `%`, `s`, `ms`.
- Spalte `neu`: `=` steht nur, wenn `alt` schon klein und in Ordnung ist. Ändert sich nur die Groß-/Kleinschreibung,
  steht die kleingeschriebene Fassung in `neu`.
- `kind`: page, tool, group, subsection, master, toggle, checkbox, slider, drag, number, input, search, combo,
  button, tabs (Name eines inneren Tabs), column, radio, figure, header (CollapsingHeader/TreeNode), menu, row (Style-/Capture-Zeile), text (fester Zeilenname, kein Control).
- Versteckte `##`-Labels: Bedeutung steht im Format-String, der in `alt` mit angegeben ist (`fmt:`).

---

## Seiten- und Tool-Namen (Registrierung, `EntityOverlay.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 1 | EntityOverlay.cs:2141 | page | Status | status | |
| 2 | EntityOverlay.cs:2300 | tool | Live readout | live readout | |
| 3 | EntityOverlay.cs:3135 | tool | Update / „Update available“ (live) | update / update available | live-Label `UpdateWindow.cs:85` mitziehen |
| 4 | EntityOverlay.cs:4675 | tool | Read cost | read cost | |
| 5 | EntityOverlay.cs:2006 | page | Area | area | |
| 6 | EntityOverlay.cs:1999 | tool | Loaded Files | loaded files | |
| 7 | EntityOverlay.cs:2019 | tool | Watch List | watch list | |
| 8 | EntityOverlay.cs:3337 | tool | Quests | quests | |
| 9 | EntityOverlay.cs:3343 | tool | Map Pins | map pins | |
| 10 | EntityOverlay.cs:4729 | page | Combat | combat | |
| 11 | EntityOverlay.cs:4729 | tool | Damage | damage | |
| 12 | EntityOverlay.cs:2150 | tool | Tracker | tracker | |
| 13 | EntityOverlay.cs:4688 | tool | Projectiles | projectiles | |
| 14 | EntityOverlay.cs:4693 | tool | How projectiles look | projectile style | siehe S2 |
| 15 | EntityOverlay.cs:4703 | tool | Belt | belt | |
| 16 | EntityOverlay.cs:4708 | tool | Skill bar | skill bar | |
| 17 | EntityOverlay.cs:3079 | page | Atlas | atlas | |
| 18 | EntityOverlay.cs:3080 | tool | Routing | routing | |
| 19 | EntityOverlay.cs:3083 | tool | Display Filters | filters | „display“ redundant |
| 20 | EntityOverlay.cs:3090 | tool | Visual Style | style | siehe S2 |
| 21 | EntityOverlay.cs:3100 | tool | Debug Log / „Debug Log (n)“ | debug log / debug log (n) | gehört zu „check the read“, siehe S6 |
| 22 | EntityOverlay.cs:3370 | tool | Ritual Line | ritual line | |
| 23 | EntityOverlay.cs:3192 | page+tool | Stash | stash | |
| 24 | EntityOverlay.cs:3255 | page+tool | Wealth | wealth | |
| 25 | EntityOverlay.cs:3282 | page+tool | Rates | rates | |
| 26 | EntityOverlay.cs:3398 | page+tool | Runecraft | runecraft | |
| 27 | EntityOverlay.cs:3413 | page+tool | Expedition | expedition | |
| 28 | EntityOverlay.cs:2148 | page | Markers | markers | |
| 29 | EntityOverlay.cs:2138 | tool | Entities & Drops | entities & drops | |
| 30 | EntityOverlay.cs:2153 | tool | Map & Places | map & places | |
| 31 | EntityOverlay.cs:2165 | tool | Health Bars | health bars | |
| 32 | EntityOverlay.cs:2166 | tool | Measuring Aids | measuring aids | nur mit --debug sinnvoll, siehe S7 |
| 33 | EntityOverlay.cs:3847 | page | Routes | routes | |
| 34 | EntityOverlay.cs:3847 / PoiLayer.cs:449 | tool | Active Routes / „Active Routes (n)“ | active routes / active routes (n) | live-Label in `PoiLayer.ActiveRoutesLabel` |
| 35 | EntityOverlay.cs:3851 | tool | Settings | route settings | „settings“ allein ist auf der Tab-Leiste nicht eindeutig |
| 36 | EntityOverlay.cs:2168 | page+tool | Appearance | appearance | |

---

## Status

### Status → Live readout (`DrawStatusTab`, EntityOverlay.cs:4950)

Feste Zeilennamen der Readout-Tabelle (`EntityOverlay.cs:5147-5300`) sind schon klein:

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 37 | EntityOverlay.cs:5147ff | text | idle, area, entities, walked, read, vitals, damage, belt, skills, skillrow, flask, evade, rules, aim, loaded | = | „skillrow“ → `skill row` |
| 38 | EntityOverlay.cs:5385 | header | rule history (n) | = | |
| 39 | EntityOverlay.cs:5694 | group | What Is Drawn | drawn | Inhalt sind Karten-/Marker-Optionen, siehe S3 |
| 40 | EntityOverlay.cs:5702 | toggle | Room Names on the Map | room names | Hint: „…on the large map“ bleibt im Hint |
| 41 | EntityOverlay.cs:5718 | slider | Named up to (fmt `%d placements`) | max repeats (fmt `%d×`) | Kind von #40; Hint ergänzen „rooms placed more often than this are not named“ |
| 42 | EntityOverlay.cs:5729 | slider | Smallest Room Named (fmt `%d tiles`) | min size (fmt `%d tiles`) | Kind von #40 |
| 43 | EntityOverlay.cs:5743 | search | `##rooms`, Platzhalter „only rooms named...“ | = (Platzhalter „filter rooms…“) | Kind von #40; Enter übernimmt |
| 44 | EntityOverlay.cs:5758 | button | Forget the n pinned here | forget pins (n) | Kind von #40; Hint: „pins of this area; rooms of an old instance never match again“ |
| 45 | EntityOverlay.cs:5767 | toggle | Ground Types on the Map | ground names | |
| 46 | EntityOverlay.cs:5785 | slider | Named up to (fmt `%d patches`) | max patches (fmt `%d`) | Kind von #45 |
| 47 | EntityOverlay.cs:5796 | slider | Smallest Patch Named (fmt `%d tiles`) | min size (fmt `%d tiles`) | Kind von #45 |
| 48 | EntityOverlay.cs:5837 | toggle | Hide Noise | hide noise | Hint bleibt (effects, pets, daemons) |
| 49 | EntityOverlay.cs:5849 | toggle | Keep What Is Out of Range | keep out-of-range | |
| 50 | EntityOverlay.cs:5859 | toggle | Name Labels Beside the Dots | dot labels | Hint fehlt (der Hint darunter gehört zu #51) → neuen Hint: „entity name beside each marker dot“ |
| 51 | EntityOverlay.cs:5866 | toggle | Health Bars Only Once Hurt | bars only when hurt | gehört inhaltlich zu Markers → Health Bars, siehe S3 |
| 52 | EntityOverlay.cs:5874 | group | Getting Out of the Way | hiding | |
| 53 | EntityOverlay.cs:5877 | toggle | Hide Markers Behind Big Panels | hide under panels | Hint bleibt (stash, skill tree, atlas, world map) |
| 54 | EntityOverlay.cs:5886 | toggle | Hide These Windows over a Panel | hide windows on panels | Hint bleibt |
| 55 | MapKeepOutEditor.cs:94 | checkbox | Keep the map off the game's interface | keep map off HUD | als Toggle (Stil wie die anderen); Hint bleibt |
| 56 | MapKeepOutEditor.cs:118 | header | Interface parts (n measured) | HUD parts (n) | Kind von #55 |
| 57 | MapKeepOutEditor.cs:151 | checkbox | Measure it | measure HUD | Kind von #56; Note „Off - …“ bleibt |
| 58 | MapKeepOutEditor.cs:172 | checkbox | {part.Label} (dynamisch) | = | Teilnamen des Spiels, unverändert |
| 59 | MapKeepOutEditor.cs:124 | header | Extra boxes (n) | extra zones (n) | Kind von #55; „box“/„zone“ vereinheitlichen |
| 60 | MapKeepOutEditor.cs:127 | checkbox | Show them, to drag them | edit zones | Kind von #59; Hint: „show the zones on screen so they can be dragged“ |
| 61 | MapKeepOutEditor.cs:209 | button | Add a Zone | add zone | |
| 62 | MapKeepOutEditor.cs:218 | button | Remove All | remove all | |
| 63 | MapKeepOutEditor.cs:239 | checkbox | {zone.Name} (dynamisch) | = | |
| 64 | MapKeepOutEditor.cs:251 | button | Remove | remove | |
| 65 | MapKeepOutEditor.cs:369 | button | Done | done | Bearbeiten-Overlay |
| 66 | EntityOverlay.cs:5908 | checkbox | „{Kind} {count}“ (debug) | = (kleiner Kind-Name) | nur --debug |
| 67 | EntityOverlay.cs:5922 | toggle | Calibration Aids | calibration aids | nur --debug; siehe S7 |
| 68 | EntityOverlay.cs:5938 | drag | Probe Height (fmt `%.0f world units`) | probe height (fmt `%.0f units`) | Kind von #67 |
| 69 | EntityOverlay.cs:5943 | button | Reset the probe | reset probe | Kind von #67 |

### Status → Update (`UpdateWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 70 | UpdateWindow.cs:136 | button | Check Now | check now | |
| 71 | UpdateWindow.cs:242 | button | Install and Restart | install & restart | |
| 72 | UpdateWindow.cs:250 | button | Download (n MB) | download (n MB) | |
| 73 | UpdateWindow.cs:256 | button | Not This One | skip version | |
| 74 | UpdateWindow.cs:43 / GitHubTokenField.cs:57 | input | `##update-token`, Platzhalter „GitHub token (Contents: read)“ | = („github token…“ klein, „Contents: read“ ist GitHub-Begriff) | Hint (TokenSaid) bereits klein |
| 75 | GitHubTokenField.cs:29 | button | save token | = | |
| 76 | GitHubTokenField.cs:30 | button | forget token | = | |

### Status → Read cost (`CostWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 77 | CostWindow.cs:58 | checkbox | this map only | = | als Toggle wie Damage „reset per map“ vereinheitlichen |
| 78 | CostWindow.cs:60 | button | Start Over | start over | |
| 79 | CostWindow.cs:66 | slider | Plot Height (fmt `%.0f px`) | plot height (fmt `%.0f px`) | gleicher Name wie Damage #93 |
| 80 | CostWindow.cs:27-32 | text | total, entities, player, terrain, maps, other | = | Plot-Legende |

---

## Area

### Area → Loaded Files (`PreloadWindow.cs` + Style-Zeilen)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 81 | PreloadWindow.cs:82 | button | Look Again | rescan | Hint fehlt → „read the area's file list again“ |
| 82 | PreloadWindow.cs:117 | button | Find the Count Field | find count field | Diagnose-Knopf (nur wenn nichts gefunden); siehe S8 |
| 83 | PreloadWindow.cs:130 | button | Look Inside the Rooms | inspect rooms | Diagnose; siehe S8 |
| 84 | PreloadWindow.cs:143 | button | Write the Rooms Out | export rooms | Diagnose; siehe S8 |
| 85 | PreloadWindow.cs:161 | button | Hunt the Placements | hunt placements | Diagnose; siehe S8 |
| 86 | PreloadWindow.cs:172 | button | Save the List | save list | |
| 87 | PreloadWindow.cs:195 | group | What That Means | matches | zeigt Watch-List-Treffer dieses Gebiets |
| 88 | PreloadWindow.cs:216 | group | Every File It Loaded | all loaded files | |
| 89 | PreloadWindow.cs:299 | search | `###preload-search`, „filter the files loaded here...“ | = („filter files…“) | |
| 90 | PreloadWindow.cs:309 | button | Watch for This | watch this | Hint fehlt → „add the typed fragment to the watch list“ |
| 91 | PreloadWindow.cs:242-243 | column | `##watch`, path | = | |
| 92 | PreloadWindow.cs:262 | button | + Watch | + watch | Hint „Click to copy this path.“ gehört zur Zeile (#Selectable), nicht zum Knopf → prüfen |
| 93a | F:StyleCatalogue.cs:202 | row | Entry line backing | entry backing | Gruppe „What the area loaded“ → `loaded files` |
| 93b | F:StyleCatalogue.cs:203 | row | List backing | list backing | |
| 93c | F:StyleCatalogue.cs:210 | row | Card Scale | card | „Scale“ ist die Spalte, nicht der Name |
| 93d | F:StyleCatalogue.cs:202 | group | What the area loaded | loaded files | Style-Gruppenzeile |

(Nummern 93a-d zählen als 93-96; ab hier fortlaufend mit 97.)

### Area → Watch List (`PreloadAlertWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 97 | PreloadAlertWindow.cs:68 | group | Watch Rules | rules | |
| 98 | PreloadAlertWindow.cs:78 | group | Alert Settings | alerts | |
| 99 | PreloadAlertWindow.cs:83 | toggle | Overlay Window | corner window | Hint bleibt |
| 100 | PreloadAlertWindow.cs:92 | toggle | Announce on Enter | announce on entry | |
| 101 | PreloadAlertWindow.cs:100 | toggle | Show Load Timer | load timer | Kind von #99 (läuft im Eckfenster) |
| 102 | PreloadAlertWindow.cs:109 | toggle | Auto-Hide if Empty | hide when empty | Kind von #99; Hint fehlt → „close the corner window when nothing matched“ |
| 103 | PreloadAlertWindow.cs:115 | toggle | Hide in Safe Zones | hide in town | Kind von #99; Hint bleibt (town + hideouts) |
| 104 | PreloadAlertWindow.cs:134 | button | Say It Again Now | announce again | Kind von #100 |
| 105 | PreloadAlertWindow.cs:162 | search | `##preload-filter`, „filter...“ | = | |
| 106 | PreloadAlertWindow.cs:183-189 | column | on, order, log, colour, name, path, `##delete` | = | |
| 107 | PreloadAlertWindow.cs:269 | checkbox | `##on` | = | |
| 108 | PreloadAlertWindow.cs:282/291 | button | ^ / v | = | Hint fehlt → „move up / down“ |
| 109 | PreloadAlertWindow.cs:309 | checkbox | `##log` | = | Hint bleibt |
| 110 | PreloadAlertWindow.cs:368 | button | Delete | delete | |
| 111 | PreloadAlertWindow.cs:393 | button | Import Defaults | import defaults | |
| 112 | PreloadAlertWindow.cs:427 | input | `##add-name`, „name“ | = | |
| 113 | PreloadAlertWindow.cs:429 | search | `##add-path`, „Metadata/...“ | = | Spielpfad, bleibt |
| 114 | PreloadAlertWindow.cs:434 | button | Add | add | |
| 115 | PreloadPanel.cs:137 | button | Close for This Map | close for this map | im Eckfenster, nicht auf der Seite |

### Area → Quests (`QuestWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 116 | QuestWindow.cs:87 | checkbox | finished too | show finished | |
| 117 | QuestWindow.cs:89 | checkbox | show the flags | show flags | Hint fehlt → „the game's own quest conditions under each quest“ |
| 118 | QuestWindow.cs:91 | search | `##quest-search`, „filter by name“ | = | |
| 119 | QuestWindow.cs:132 | subsection | Act n / No Act | act n / no act | |
| 120 | QuestWindow.cs:208 | button | hide the steps / all the steps | hide steps / show steps | |

### Area → Map Pins (`MapPinWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 121 | MapPinWindow.cs:75 | checkbox | only the locked ones | locked only | |
| 122 | MapPinWindow.cs:77 | checkbox | show the columns | show columns | |
| 123 | MapPinWindow.cs:79 | search | `##pin-search`, „filter by name“ | = | |
| 124 | MapPinWindow.cs:115 | group | Act n / No act | act n / no act | |

---

## Combat

### Combat → Damage (`DamageWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 125 | DamageWindow.cs:155 | figure | Watched | watched | |
| 126 | DamageWindow.cs:161 | figure | Credited | credited | |
| 127 | DamageWindow.cs:167 | figure | Untouched | untouched | |
| 128 | DamageWindow.cs:176 | figure | Out of range | out of range | |
| 129 | DamageWindow.cs:201 | toggle | Reset per Map | reset per map | |
| 130 | DamageWindow.cs:205 | button | Reset DPS | reset DPS | |
| 131 | DamageWindow.cs:211 | slider | `##dmg-height` + Text „Graph height“ (fmt `%.0f px`) | graph height (fmt `%.0f px`) | Label in den Slider statt TextColored daneben |
| 132 | DamageWindow.cs:217 | slider | `##dmg-smoothing` + Text „Smooth window“ (fmt `%.2f s`) | smoothing (fmt `%.2f s`) | dito; Hint bleibt |
| 133 | DamageWindow.cs:241 | subsection | How the Figure Is Measured | measurement | |
| 134 | DamageWindow.cs:247 | toggle | Track Off-Screen / track off-screen | count off-screen kills | |
| 135 | DamageWindow.cs:271 | slider | Max Range (fmt `%.0f grid` / „any distance“) | max range (fmt `%.0f cells`, 0 = „any“) | Kind von #134; Einheit „grid“ → `cells` |
| 136 | DamageWindow.cs:301 | toggle | Damage Heatmap Overlay | heat map | Hint bleibt |
| 137 | DamageWindow.cs:351-353 | radio | damage done / damage taken / time spent | = | Kinder von #136 |
| 138 | DamageWindow.cs:370 | subsection | Rares and Uniques (n) | rares & uniques (n) | |
| 139 | DamageWindow.cs:407-410 | column | monster, took, damage, dps | = (`DPS`) | |
| 140 | DamageWindow.cs:998-1000 | column | target, left, dps | = (`DPS`) | |

### Combat → Tracker (`TrackerWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 141 | TrackerWindow.cs:98 | tabs | Monsters & facing | monsters & facing | |
| 142 | TrackerWindow.cs:104 | tabs | Hazards & ground | hazards | |
| 143 | TrackerWindow.cs:105 | tabs | Status effects | status effects | |
| 144 | TrackerWindow.cs:106 | tabs | Live inspector | live buffs | zeigt aktuelle Buffs, Klick = Regel |
| 145 | TrackerWindow.cs:315 | group | Lines to Monsters | monster lines | |
| 146 | TrackerWindow.cs:318-320 | checkbox+text | Unique / Rare / Magic | unique / rare / magic | Hint bleibt |
| 147 | TrackerWindow.cs:121 | group | Facing Vectors | facing | |
| 148 | TrackerWindow.cs:126 | toggle | Draw Facing Ray | facing rays | Hint bleibt |
| 149 | TrackerWindow.cs:147 | toggle | Active Only | active only | Kind von #148; Hint fehlt → „only monsters that are acting“ |
| 150 | TrackerWindow.cs:155 | toggle | Show Turn | show turn | Kind von #148 |
| 151 | TrackerWindow.cs:163 | toggle | Show Action | show action | Kind von #148 |
| 152 | TrackerWindow.cs:171 | toggle | Include Self | include self | Kind von #148 |
| 153 | TrackerWindow.cs:177 | slider | `##length` (fmt `%.0f world units`) | length (fmt `%.0f units`) | sichtbares Label fehlt |
| 154 | TrackerWindow.cs:184 | slider | `##thickness` (fmt `%.1f wide`) | width (fmt `%.1f px`) | |
| 155 | TrackerWindow.cs:191/201 | colour | `##aimcolour`, `##turncolour` + Text „facing / turning“ | = | |
| 156 | TrackerWindow.cs:396 | toggle | Ring Marked Hazards | hazard rings | |
| 157 | TrackerWindow.cs:421 | toggle | Use Component Radius | game radius | Kind von #156; Hint bleibt |
| 158 | TrackerWindow.cs:442 | slider | `##groundradius` (fmt `%.0f world units`) | radius (fmt `%.0f units`) | Kind von #157 (wenn aus) |
| 159 | TrackerWindow.cs:454 | colour | `##groundcolour` | = | |
| 160 | TrackerWindow.cs:464 | toggle | Seconds Left | countdown | Kind von #156 |
| 161 | TrackerWindow.cs:476 | toggle | Name Beside Ring | hazard name | Kind von #156 |
| 162 | TrackerWindow.cs:488 | toggle | Show Metadata Path | show metadata | Kind von #156 |
| 163 | TrackerWindow.cs:500 | toggle | Draw Boss Beams | boss cast directions | |
| 164 | TrackerWindow.cs:511 | slider | `##beamthickness` (fmt `%.0f px wide`) | width (fmt `%.0f px`) | Kind von #163 |
| 165 | TrackerWindow.cs:522 | colour | `##beamcolour` | = | |
| 166 | TrackerWindow.cs:543 | group | Custom Ground Filter Rules | ground rules | |
| 167 | TrackerWindow.cs:546 | toggle | Ring What the Rules Below Match | use ground rules | Hint bleibt |
| 168 | TrackerWindow.cs:581-587 | column | on, colour, filled, radius, thickness, delete, path | on, colour, filled, radius, width, delete, path | „thickness“ → „width“ wie überall |
| 169 | TrackerWindow.cs:618 | checkbox | filled | = | |
| 170 | TrackerWindow.cs:626 | slider | `##radius` (fmt `%d px`) | = | |
| 171 | TrackerWindow.cs:638 | slider | `##weight` (fmt `%d wide`) | fmt `%d px` | |
| 172 | TrackerWindow.cs:646 | button | x | = | Hint fehlt → „delete rule“ |
| 173 | TrackerWindow.cs:681 | button | Add a Ground Rule | add rule | |
| 174 | TrackerWindow.cs:722 | toggle | Over the Player | on player | |
| 175 | TrackerWindow.cs:729 | toggle | Rares & Uniques Only | on rares & uniques | Hint bleibt |
| 176 | TrackerWindow.cs:744 | group | The Icon Sheet | icon sheet | |
| 177 | TrackerWindow.cs:747 | group | Where the Rows Sit | row position | |
| 178 | TrackerWindow.cs:789 | combo | `##profile` | profile | |
| 179 | TrackerWindow.cs:792 | button | Use This Profile | use profile | |
| 180 | TrackerWindow.cs:804 | button | Detect from This Screen | detect from screen | |
| 181 | TrackerWindow.cs:820ff | text | player row / monster row | = | |
| 182 | TrackerWindow.cs:835/842/849 | slider | `##x` „across %d“, `##y` „down %d“, `##gap` „gap %d“ | fmt `x %d px`, `y %d px`, `gap %d px` | |
| 183 | TrackerWindow.cs:868 | slider | `##shadow` „shadow %.2f“ | fmt `shadow %.0f%%` (×100) | |
| 184 | TrackerWindow.cs:875 | slider | `##shadowsize` „spread %d“ | fmt `spread %d px` | |
| 185 | TrackerWindow.cs:882 | colour | `##barback` + Text „timer bar background“ | timer bar back | |
| 186 | TrackerWindow.cs:899-919 | slider | „stacks x %d“, „stacks y %d“, „timer x %d“, „timer y %d“ | fmt `… %d px` | |
| 187 | TrackerWindow.cs:751 | group | On the Player | on player | |
| 188 | TrackerWindow.cs:755 | group | On Monsters | on monsters | |
| 189 | TrackerWindow.cs:943-951 | column | on, bar, text, tile, pick, scale, delete, label, name | = | |
| 190 | TrackerWindow.cs:996 | button | pick / picking | = | |
| 191 | TrackerWindow.cs:1003 | slider | `##scale` „x%.2f“ | fmt `%.2f×` | |
| 192 | TrackerWindow.cs:1010 | button | x | = | Hint fehlt → „delete rule“ |
| 193 | TrackerWindow.cs:1018 | input | `##label` | = | |
| 194 | TrackerWindow.cs:1027 | input | `##name` | = | |
| 195 | TrackerWindow.cs:1070 | button | Add a Rule | add rule | |
| 196 | TrackerWindow.cs:1171 | group | On you | on you | |
| 197 | TrackerWindow.cs:1178 | group | On monsters | on monsters | |
| 198 | TrackerWindow.cs:261-264 | column | how many, id, name, counts as | count, id, name, counts as | |

### Combat → Projectiles (`ProjectileWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 199 | ProjectileWindow.cs:56 | toggle | Draw Them over the Game | draw projectiles | Warning darunter bleibt |
| 200 | ProjectileWindow.cs:64 | toggle | With the Line They Came Along | trails | Kind von #199 (eingerückt statt Cell(1)); Hint fehlt → „the line each projectile came along“ |
| 201 | ProjectileWindow.cs:72 | toggle | And Their Paths | show path | Kind von #199; Hint fehlt → „metadata path beside each mark“ |
| 202 | ProjectileWindow.cs:91 | toggle | Only Mine | player only | Kind von #199; Hint bleibt |
| 203 | ProjectileWindow.cs:162-165 | column | how many, player, trail, path | count, mine, trail, path | |

### Combat → How projectiles look (Style-Zeilen, Gruppe „Projectiles“)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 204 | F:StyleCatalogue.cs:161 | row | Yours | player | |
| 205 | F:StyleCatalogue.cs:162 | row | Everything else's | others | |
| 206 | F:StyleCatalogue.cs:163 | row | Where it came from | trail | |
| 207 | F:StyleCatalogue.cs:170 | row | Fire trail | fire | Kind von #206 |
| 208 | F:StyleCatalogue.cs:171 | row | Cold trail | cold | Kind von #206 |
| 209 | F:StyleCatalogue.cs:172 | row | Lightning trail | lightning | Kind von #206 |
| 210 | F:StyleCatalogue.cs:173 | row | Chaos trail | chaos | Kind von #206 |
| 211 | F:StyleCatalogue.cs:174 | row | Physical trail | physical | Kind von #206 |
| 212 | F:StyleCatalogue.cs:161 | group | Projectiles | projectiles | Style-Gruppenzeile |

### Combat → Belt / Skill bar (`FigureTab.cs` + Style-Zeilen)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 213 | EntityOverlay.cs:4701 → FigureTab.cs:95 | master | Uses Left on the Flasks | flask uses | Erklärung (BeltExplanation) bleibt |
| 214 | F:StyleCatalogue.cs:181 | row | Uses left on a flask | uses left | |
| 215 | F:StyleCatalogue.cs:186 | row | Backing under the figure | backing | |
| 216 | F:StyleCatalogue.cs:181 | group | Flasks | flasks | |
| 217 | EntityOverlay.cs:4706 → FigureTab.cs:95 | master | DPS on the Skill Bar | skill DPS | |
| 218 | F:StyleCatalogue.cs:191 | row | DPS on a skill | skill DPS — [Farbeinstellung] — Reset | „enable“ ist durch den Master-Schalter schon gesagt. Änderung bezieht sich auf die komplette Zeile |
| 219 | F:StyleCatalogue.cs:192 | row | Backing under the DPS | ☑ DPS background — [Farbeinstellung] — Reset | Änderung bezieht sich auf die komplette Zeile |
| 220 | F:StyleCatalogue.cs:191 | group | Skills | skills | |

---

## Atlas

### Atlas → Lead-Streifen (`AtlasWindow.DrawLead`, über allen Atlas-Tabs)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 221 | AtlasWindow.cs:68 | master | Enable Atlas Overlay | atlas overlay | „enable“ ist durch den Master-Schalter schon gesagt |
| 222 | AtlasWindow.cs:376 | button | Check the Read | debug output | Hint bleibt; Ergebnis im Debug Log (#21) |

### Atlas → Routing (`AtlasWindow.DrawRouting`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 223 | AtlasWindow.cs:286 | search | `##atlas-search`, „only maps called...“ | = („filter maps…“) | |
| 224 | AtlasWindow.cs:296 | group | Off the Screen | off-screen | |
| 225 | AtlasWindow.cs:299 | toggle | Point at Targets off the Screen | edge pointers | Hint bleibt |
| 226 | AtlasWindow.cs:313 | slider | Reaching (fmt `%.1f screens` / „no limit“) | reach (fmt `%.1f screens`) | Kind von #225 |
| 227 | AtlasWindow.cs:476-478 | column | `##colour`, `##name`, `##hops` | = | |
| 228 | AtlasWindow.cs:501 | colour | `##colour` | = | |
| 229 | AtlasWindow.cs:510 | checkbox | {group.Name} (aus Datei) | = | |
| 230 | AtlasWindow.cs:526 | number | maps away | max hops | |

### Atlas → Display Filters (`AtlasWindow.Draw`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 231 | AtlasWindow.cs:102 | group | Drawn | drawn | |
| 232 | AtlasWindow.cs:109 | toggle | Map Names | map names | |
| 233 | AtlasWindow.cs:122 | toggle | What Is in Them | map mechanic | |
| 234 | AtlasWindow.cs:179 | toggle | Node mechanic: icon / Node mechanic: text | mechanic as icons | Kind von #233; ein festes Label statt wechselndem Text (Toggle zeigt den Zustand) |
| 235 | AtlasWindow.cs:134 | toggle | Every Connection | all connections | |
| 236 | AtlasWindow.cs:145 | toggle | Map Ratings | map ratings | |
| 237 | AtlasWindow.cs:159 | toggle | Biome Borders | biome borders | Hint sagt „ring“, ist aber ein Rahmen um den Mapnamen → neuer Hint: „frame around the map name in the biome's colour“ |
| 238 | AtlasWindow.cs:215 | slider | Size of the Writing (fmt `%.2fx`) | text size (fmt `%.2f×`) | |
| 239 | AtlasWindow.cs:222 | group | Hidden | hide | |
| 240 | AtlasWindow.cs:225 | toggle | Hide Finished Maps | hide finished | |
| 241 | AtlasWindow.cs:233 | toggle | Hide Maps with No Way There | hide unreachable | Note „routing targets stay visible“ → in den Hint |
| 242 | AtlasWindow.cs:245 | toggle | Hide Everything over an Unmeasurable Hover Panel | hide on hover | Hint bleibt (Fallback, wenn Panel nicht messbar) |

### Atlas → Visual Style (Style-Zeilen, Gruppe „The atlas“)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 243 | F:StyleCatalogue.cs:218 | group | The atlas | atlas | |
| 244 | F:StyleCatalogue.cs:218 | row | Map name | map name | |
| 245 | F:StyleCatalogue.cs:219 | row | Label backing | name background | |
| 246 | F:StyleCatalogue.cs:220 | row | What is in a map | map mechanic | wie #233 |
| 247 | F:StyleCatalogue.cs:221 | row | Connections | connections | |
| 248 | F:StyleCatalogue.cs:222 | row | Route | route | |
| 249 | F:StyleCatalogue.cs:223 | row | Where a route starts | route start | |

### Atlas → Debug Log (`AtlasLogWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 250 | AtlasLogWindow.cs:150 | combo | `###atlas-log-section` | section | sichtbares Label fehlt |
| 251 | AtlasLogWindow.cs:165 | search | `###atlas-log-find`, „find in the text...“ | = | Hint bleibt |
| 252 | AtlasLogWindow.cs:194-196 | column | #, section, line | = | |

### Atlas → Ritual Line (`RitualWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 253 | RitualWindow.cs:101 | search | `##filter`, „only routes paying...“ | = („filter rewards…“) | |
| 254 | RitualWindow.cs:105 | button | Clear Picks | clear picks | |
| 255 | RitualWindow.cs:114 | checkbox | check the predictions against what the game rolls | verify predictions | Hint fehlt → Text als Hint; Ergebnis-Note bleibt |
| 256 | RitualWindow.cs:195 | checkbox | `##pick` | = | Hint fehlt → „draw this route“ |
| 257 | RitualWindow.cs:274 | number | `##weight` | = | Überschrift „what a reward is worth to you…“ (RitualWindow.cs:253) → `reward weights` als Group |

---

## Stash (`StashWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 258 | StashWindow.cs:154 | tabs | Stash Inspector | inspector | |
| 259 | StashWindow.cs:154 | tabs | Price Sources | price sources | siehe S5 |
| 260 | StashWindow.cs:203 | search | `##search`, „name, mod or stat - the stats are searched too...“ | Platzhalter „name, mod or stat…“ | Rest in Hint |
| 261 | StashWindow.cs:208 | button | Scan Stash | scan stash | |
| 262 | StashWindow.cs:216 | slider | cell (fmt `%.0f px`) | cell size (fmt `%.0f px`) | |
| 263 | StashWindow.cs:252 | checkbox | Hide Empty Tabs | hide empty tabs | |
| 264 | StashWindow.cs:782 | selectable | Everything (n) worth | everything (n) | |
| 265 | StashWindow.cs:304 | group | Where the Prices Come From | prices | |
| 266 | StashWindow.cs:332 | checkbox | Fetch poe.ninja | query poe.ninja | als Toggle |
| 267 | StashWindow.cs:438 | checkbox | Fetch Currency Exchange | query currency exchange | |
| 268 | StashWindow.cs:570 | button | Ask the Currency Exchange Once | test currency exchange | Kind von #267; Hint bleibt (Diagnose) |
| 269 | StashWindow.cs:512 | checkbox | Fetch Trade API (Uniques) | trade API | Hint: „(uniques)“ dort |
| 270 | StashWindow.cs:533 | button | Open Trade Window | open trade login | Kind von #269 |
| 271 | StashWindow.cs:310 | group | Item Pictures | item pictures | |
| 272 | StashWindow.cs:389 | checkbox | Fetch poe2db | query poe2db | |

## Wealth (`WealthWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 273 | WealthWindow.cs:199 | checkbox | Enable Wealth Tracker | wealth tracker | als Master |
| 274 | WealthWindow.cs:208 | checkbox | Show Overlay | overlay | Kind von #273; Hint bleibt |
| 275 | WealthWindow.cs:192 | tabs | Breakdown & Inventory | breakdown | |
| 276 | WealthWindow.cs:193 | tabs | History & Graph | history | |
| 277 | WealthWindow.cs:270 | figure | Holding | total | |
| 278 | WealthWindow.cs:281 | figure | 1 div | = | |
| 279 | WealthWindow.cs:311/322 | figure | hour/day/week/everything / „all of it“ | = | |
| 280 | WealthWindow.cs:516 | group | Currency Breakdown | currency | |
| 281 | WealthWindow.cs:542-550 | column | currency, stacks, each, worth | = | |
| 282 | WealthWindow.cs:734 | group | Stash / Inventory Breakdown | by tab | |
| 283 | WealthWindow.cs:764-772 | column | (leer), tab, stacks, unpriced, worth | = | |
| 284 | WealthWindow.cs:797 | checkbox | `##tab{id}` | = | Hint bleibt |
| 285 | WealthWindow.cs:33-36 | radio | hour, day, week, everything | = | |
| 286 | WealthWindow.cs:1097 | button | Reset Tracking History | reset history | |
| 287 | WealthWindow.cs:1107 | button | Yes, Throw It Away | Yes, delete it. | Satz → Satzschreibung; alternativ `delete` |
| 288 | WealthWindow.cs:1115 | button | Keep It | cancel | |

## Rates (`RatesWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 289 | RatesWindow.cs:89 | search | `##rates-search`, „name...“ | = | |
| 290 | RatesWindow.cs:90 | toggle | Only Where a Loop Pays | paying loops only | |
| 291 | RatesWindow.cs:123 | checkbox | read live rates | live rates | als Toggle; Hint fehlt → „asks the game's exchange; same switch as stash“ (prüfen) |
| 292 | RatesWindow.cs:239-251 | column | currency, in exalted, 7 days, book, via, loop | currency, ex, 7 days, book, via, loop | |

## Runecraft (`RunecraftWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 293 | RunecraftWindow.cs:77 | master | Runecraft prices | runecraft prices | |
| 294 | RunecraftWindow.cs:109 | group | Where the Prices Come From | prices | wie Stash #265 |
| 295 | RunecraftWindow.cs:112 | checkbox | Fetch poe.ninja | query poe.ninja | gleicher Schalter wie #266 – siehe S5 |
| 296 | RunecraftWindow.cs:157 | group | How the Price Is Drawn | price display | |
| 297 | RunecraftWindow.cs:160 | combo | Colour | colouring | Optionen ColourModes (relative/absolute) bleiben |
| 298 | RunecraftWindow.cs:173 | drag | Green from (ex) (fmt `%.1f`) | green from (fmt `%.1f ex`) | Kind von #297 (nur absolut); Einheit ins Format |
| 299 | RunecraftWindow.cs:180 | drag | Red below (ex) (fmt `%.2f`) | red below (fmt `%.2f ex`) | dito |
| 300 | RunecraftWindow.cs:188 | toggle | Frame the most valuable row | highlight most valuable | |
| 301 | RunecraftWindow.cs:195 | toggle | Mark rows nothing could price | mark unpriced | |
| 302 | RunecraftWindow.cs:202 | slider | Sideways offset (fmt `%.0f px`) | x offset (fmt `%.0f px`) | |
| 303 | RunecraftWindow.cs:214 | slider | Writing size (fmt `%.2f x`) | text size (fmt `%.2f×`) | wie Atlas #238 |
| 304 | RunecraftWindow.cs:222 | group | On the Map | map overlay | |
| 305 | RunecraftWindow.cs:225 | toggle | Price each monolith on the map | monolith prices | |
| 306 | RunecraftWindow.cs:237 | toggle | Hole count before the price | hole count | Kind von #305 |
| 307 | RunecraftWindow.cs:284 | toggle | Name the chosen rune on the map | chosen rune | Kind von #305 (steht heute unter Rune Chain) |
| 308 | RunecraftWindow.cs:295 | toggle | Scout propagatable runes on the map | rune scouting | Kind von #305 |
| 309 | RunecraftWindow.cs:251 | group | The Rune Chain | rune chain | |
| 310 | RunecraftWindow.cs:254 | toggle | Value the rune a recipe propagates | value rune chain | |
| 311 | RunecraftWindow.cs:272 | drag | Loot per pack (ex) (fmt `%.1f`) | loot per pack (fmt `%.1f ex`) | Kind von #310 |
| 312 | RunecraftWindow.cs:324-329 | column | rune, loot x, ex/wave, avoid, effect, `##rm` | rune, loot ×, ex/wave, avoid, effect | |
| 313 | RunecraftWindow.cs:347 | drag | `##mult` (fmt `%.2f`) | fmt `%.2f×` | |
| 314 | RunecraftWindow.cs:358 | drag | `##ex` (fmt `%.1f`) | fmt `%.1f ex` | |
| 315 | RunecraftWindow.cs:366 | checkbox | `##avoid` | = | |
| 316 | RunecraftWindow.cs:377 | button | x | = | Hint bleibt |
| 317 | RunecraftWindow.cs:429 | combo | Add a rune („choose...“) | add rune | |
| 318 | RunecraftWindow.cs:456 | button | Reset to the tier-list defaults | reset to defaults | |
| 319 | RunecraftWindow.cs:482 | group | Monoliths in This Area | area monoliths | |
| 320 | RunecraftWindow.cs:498 | drag | Hide offers under (ex) (fmt `%.1f`) | hide under (fmt `%.1f ex`) | |
| 321 | RunecraftWindow.cs:507 | header | {mark}{Headline} (dynamisch) | = | |
| 322 | RunecraftWindow.cs:562-570 | column | reward, x, unit, total, rune, chain, all told | reward, ×, unit, total, rune, chain, sum | |
| 323 | RunecraftWindow.cs:673 | group | What the Panel Read | panel read | Diagnose; siehe S8 |
| 324 | RunecraftWindow.cs:717-720 | column | row, priced by, via, ex | = | |

## Expedition (`ExpeditionWindow.cs`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 325 | ExpeditionWindow.cs:83 | master | Expedition planner | expedition planner | |
| 326 | ExpeditionWindow.cs:105 | tabs | Plan | plan | |
| 327 | ExpeditionWindow.cs:105 | tabs | Weights | weights | |
| 328 | ExpeditionWindow.cs:105 | tabs | Relics | relics | |
| 329 | ExpeditionWindow.cs:105 | tabs | Props | props | |
| 330 | ExpeditionWindow.cs:105 | tabs | Map & Run | map & run | |
| 331 | ExpeditionWindow.cs:121 | group | This Expedition | this expedition | |
| 332 | ExpeditionWindow.cs:145 | number | Total charges (manual) | charges | Hint: „(manual)“ steht schon dort |
| 333 | ExpeditionWindow.cs:165 | button | cooking... | planning… | |
| 334 | ExpeditionWindow.cs:177 | button | Run / Run* | run / run* | |
| 335 | ExpeditionWindow.cs:217 | group | The Plan (n charges) | plan (n charges) | |
| 336 | ExpeditionWindow.cs:244-246 | column | #, at, why | = | |
| 337 | ExpeditionWindow.cs:270 | subsection | Targets (n) | targets (n) | |
| 338 | ExpeditionWindow.cs:292-295 | column | kind, at, worth, what | = | |
| 339 | ExpeditionWindow.cs:345 | subsection | Why (n lines) | trace (n) | |
| 340 | ExpeditionWindow.cs:375 | group | What Is Worth the Walk | anchors | Hint sagt „the anchors“ |
| 341 | ExpeditionWindow.cs:384 | drag | Monolith worth a detour (ex) (fmt `%.0f`) | monolith from (fmt `%.0f ex`) | |
| 342 | ExpeditionWindow.cs:397 | number | ...or holes at least | or holes from | Fortsetzungs-Label → eigenständig; 0 = aus |
| 343 | ExpeditionWindow.cs:413 | group | Reward Flags by Height (normal) | flags (normal) | |
| 344 | ExpeditionWindow.cs:420 | slider | White flag (fmt `%d ex`) | white | |
| 345 | ExpeditionWindow.cs:429 | slider | Magic flag (fmt `%d ex`) | magic | |
| 346 | ExpeditionWindow.cs:438 | slider | Gold flag (fmt `%d ex`) | gold | |
| 347 | ExpeditionWindow.cs:447 | slider | Logbook flag (fmt `%d ex`) | logbook | |
| 348 | ExpeditionWindow.cs:458 | group | Reward Types (Grand) | reward types (grand) | |
| 349 | ExpeditionWindow.cs:465 | slider | Flags per spare charge (fmt `%d`) | flags per spare charge | 4 Wörter, ok |
| 350 | ExpeditionWindow.cs:500-502 | column | reward, weight, `##rm` | = | |
| 351 | ExpeditionWindow.cs:525 | drag | `##w` (fmt `%.0f`) | fmt `%.0f ex` | Hint sagt „in Exalted“ |
| 352 | ExpeditionWindow.cs:534 | button | x | = | |
| 353 | ExpeditionWindow.cs:580 | combo | Add a reward type („choose...“) | add reward | |
| 354 | ExpeditionWindow.cs:621 | button | Reset the reward weights | reset weights | |
| 355 | ExpeditionWindow.cs:660 | group | Relics: seek (+) and shun (-) | relics | (+)/(-) stehen in den Spalten |
| 356 | ExpeditionWindow.cs:693 | column | seek (+) · weight in ex | seek (+) | Einheit ins Drag-Format |
| 357 | ExpeditionWindow.cs:694 | column | shun (-) · avoid, or weight in ex | shun (−) | |
| 358 | ExpeditionWindow.cs:723 | checkbox | `##avoid` | = | |
| 359 | ExpeditionWindow.cs:749 | drag | `##w` (fmt `%.0f`) | fmt `%.0f ex` | |
| 360 | ExpeditionWindow.cs:826 | button | Clear the weights | clear weights | |
| 361 | ExpeditionWindow.cs:826 | button | Clear the avoid marks | clear avoids | |
| 362 | ExpeditionWindow.cs:842 | group | Exploding Props | exploding props | |
| 363 | ExpeditionWindow.cs:863 | checkbox | `##on` | = | |
| 364 | ExpeditionWindow.cs:874 | input | `##path` | = | |
| 365 | ExpeditionWindow.cs:885 | drag | `##r` (fmt `%.0f cells`) | = | |
| 366 | ExpeditionWindow.cs:894 | button | x | = | |
| 367 | ExpeditionWindow.cs:933 | input | New rule's path fragment | new path | |
| 368 | ExpeditionWindow.cs:935 | button | Add rule | add rule | |
| 369 | ExpeditionWindow.cs:942 | group | Objects Matching No Rule | unmatched objects | |
| 370 | ExpeditionWindow.cs:972 | group | On the Map | on the map | |
| 371 | ExpeditionWindow.cs:978 | toggle | Blast ring on each planned charge | blast rings | |
| 372 | ExpeditionWindow.cs:987 | toggle | The line the route walks | route line | |
| 373 | ExpeditionWindow.cs:996 | toggle | Exploding props and their radius | prop radius | Fortsetzungs-Label → eigenständig |
| 374 | ExpeditionWindow.cs:1005 | toggle | Path-blockers and the holes they punch | blockers | dito |
| 375 | ExpeditionWindow.cs:1013 | group | Run | run | |
| 376 | ExpeditionWindow.cs:1015 | combo | Run hotkey | run key | |
| 377 | ExpeditionWindow.cs:1026 | toggle | Write the decision trace to config/expedition-plan.txt | write trace file | Pfad in den Hint |

---

## Markers | Diesen Tab bitte in "Overlay" umbennen

### Markers → Entities & Drops (Style-Zeilen + Reset-Zeile)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 378 | StyleRows.cs:268 | button | Reset All | reset all | Text daneben „everything as it comes - nothing changed yet“ bleibt |
| 379 | F:StyleCatalogue.cs:38 | group | Monsters | monsters | |
| 380 | F:StyleCatalogue.cs:38 | row | Ordinary monster | normal | „normal“ = Spielbegriff |
| 381 | F:StyleCatalogue.cs:39 | row | Magic monster | magic | |
| 382 | F:StyleCatalogue.cs:40 | row | Rare monster | rare | |
| 383 | F:StyleCatalogue.cs:41 | row | Unique monster | unique | |
| 384 | F:StyleCatalogue.cs:43 | group | Drops | drops | |
| 385 | F:StyleCatalogue.cs:43 | row | Ordinary drop | normal | |
| 386 | F:StyleCatalogue.cs:44 | row | Magic drop | magic | |
| 387 | F:StyleCatalogue.cs:45 | row | Rare drop | rare | |
| 388 | F:StyleCatalogue.cs:46 | row | Unique drop | unique | |
| 389 | F:StyleCatalogue.cs:47 | row | Currency | currency | |
| 390 | F:StyleCatalogue.cs:49 | group | Other entities | other entities | |
| 391 | F:StyleCatalogue.cs:49 | row | Chest | chest | |
| 392 | F:StyleCatalogue.cs:50 | row | NPC | NPC | |
| 393 | F:StyleCatalogue.cs:51 | row | Player | player | |
| 394 | F:StyleCatalogue.cs:56 | row | Effect | effect | |
| 395 | F:StyleCatalogue.cs:57 | row | Terrain piece | terrain | |
| 396 | F:StyleCatalogue.cs:58 | row | Anything else | the rest | |
| 397 | F:StyleCatalogue.cs:60 | row | Dot outline | dot outline | |
| 398 | F:StyleCatalogue.cs:61 | row | Dot label | dot label | |

### Markers → Map & Places

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 399 | EntityOverlay.cs:2224 | checkbox | boss arenas wear the game's own picture of their boss | boss icons | Satzteil in den Hint; Kind/ Nachbar von Zeile „boss arena“ (#417) |
| 400 | EntityOverlay.cs:2248 | header | boss pictures still to make | missing boss icons | Kind von #399 |
| 401 | BossIconRows.cs:74 | search | `##boss-plan-find`, „find by id or name...“ | = | |
| 402 | BossIconRows.cs:76 | checkbox | done too | show done | |
| 403 | BossIconRows.cs:78 | checkbox | hideouts and towers | hideouts & towers | |
| 404 | BossIconRows.cs:201-204 | column | map, id, boss, no boss | = | |
| 405 | BossIconRows.cs:262 | checkbox | `##boss-plan-skip-{id}` | = | Hint klein → Satzanfang groß („Tick a map…“) |
| 406 | F:StyleCatalogue.cs:64 | group | Places | environments | |
| 407 | F:StyleCatalogue.cs:64 | row | Unrecognised marker | unknown | |
| 408 | F:StyleCatalogue.cs:65 | row | Area transition | area transition | |
| 409 | F:StyleCatalogue.cs:66 | row | Waypoint | waypoint | |
| 410 | F:StyleCatalogue.cs:67 | row | Checkpoint | checkpoint | |
| 411 | F:StyleCatalogue.cs:68 | row | Chest | chest | |
| 412 | F:StyleCatalogue.cs:69 | row | Strongbox | strongbox | |
| 413 | F:StyleCatalogue.cs:70 | row | Shrine | shrine | |
| 414 | F:StyleCatalogue.cs:71-75 | row | Breach, Ritual, Expedition, Essence, Delirium | breach, ritual, expedition, essence, delirium | |
| 415 | F:StyleCatalogue.cs:76 | row | NPC | NPC | |
| 416 | F:StyleCatalogue.cs:77 | row | Quest objective | quest objective | |
| 417 | F:StyleCatalogue.cs:78 | row | Boss arena | boss arena | |
| 418 | F:StyleCatalogue.cs:79 | row | Place name | labels | |
| 419 | F:StyleCatalogue.cs:90 | group | Map | map | |
| 420 | F:StyleCatalogue.cs:90 | row | Area layout | area layout | |
| 421 | F:StyleCatalogue.cs:94 | row | Where only projectiles pass | projectiles layout | |
| 422 | F:StyleCatalogue.cs:95 | row | Still to walk | undiscovered | |
| 423 | F:StyleCatalogue.cs:101 | row | Room name | room name | Style zu #40 |
| 424 | F:StyleCatalogue.cs:106 | row | Ground type | tile name | Style zu #45 |
| 425 | F:StyleCatalogue.cs:111 | row | Heat map | damage heatmap | Style zu Damage #136 |
| 426 | F:StyleCatalogue.cs:122 | row | Edge indicators (minimap) | edge indicators (minimap) | |
| 427 | F:StyleCatalogue.cs:123 | row | Edge indicators (large map) | edge indicators (large map) | |
| 428 | F:StyleCatalogue.cs:140 | group | Room outlines | room outlines | Tile-Book-Funktion, siehe S7 |
| 429 | F:StyleCatalogue.cs:140 | row | Outline | outline | |
| 430 | F:StyleCatalogue.cs:141 | row | Name | name | |
| 431 | F:StyleCatalogue.cs:142 | row | Backing under the name | name background | |
| 432 | F:StyleCatalogue.cs:143 | row | Corner that disagrees | bad corner | |
| 433 | F:StyleCatalogue.cs:144 | row | Tile that disagrees | bad tile | |
| 434 | F:StyleCatalogue.cs:145 | row | Explained by a join | proper connector | |
| 435 | F:StyleCatalogue.cs:82 | group | Routes | routes | siehe S4 |
| 436 | F:StyleCatalogue.cs:82-86 | row | First route … Fifth route | route 1 … route 5 | |
| 437 | F:StyleCatalogue.cs:87 | row | Direction arrows | arrows | Style zu #470 |

### Markers → Health Bars

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 438 | F:StyleCatalogue.cs:151 | group | Health bars | health bars | |
| 439 | F:StyleCatalogue.cs:151 | row | Monster health bar | monsters | |
| 440 | F:StyleCatalogue.cs:152 | row | Empty part | empty part | |
| 441 | F:StyleCatalogue.cs:153 | row | Energy shield | energy shield | |

### Markers → Measuring Aids

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 442 | F:StyleCatalogue.cs:231 | group | Measuring aids | measuring aids | |
| 443 | F:StyleCatalogue.cs:231 | row | Screen centre | screen centre | |
| 444 | F:StyleCatalogue.cs:232 | row | Ground marker | ground mark | |
| 445 | F:StyleCatalogue.cs:233 | row | Health-bar marker | bar mark | |
| 446 | F:StyleCatalogue.cs:234 | row | Line between the two | link line | |
| 447 | F:StyleCatalogue.cs:235 | row | Selected interface element | selected UI element | Inspect-Funktion, siehe S7 |
| 448 | F:StyleCatalogue.cs:236 | row | Interface element under the cursor | hovered UI element | dito |

### Gemeinsam: Style-Zeilen-Steuerung (`StyleRows.cs`, auf jeder Style-Seite)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 449 | StyleRows.cs:216/220 | number | Text „Set all sizes“ + `###setall` | set all sizes (fmt `%d %`) | Label in das Control; Hint bleibt |
| 450 | StyleRows.cs:225 | button | Apply | apply | |
| 451 | StyleRows.cs:136-141 | column | `##on` `##colour` `##name` `##icon` `##size` `##reset` | = | |
| 452 | StyleRows.cs:332 | checkbox | `###show` | = | Hint fehlt → „draw this“ |
| 453 | StyleRows.cs:341 | colour | `###colour` | = | |
| 454 | StyleRows.cs:376 | button | Reset | reset | |
| 455 | StyleRows.cs:463/477 | button | - / + (Text „%3.0f%“) | = | Hint bleibt |
| 456 | StyleRows.cs:508 | slider | `###width` (fmt `%.1f px` / „as it comes“) | = (0 = „auto“) | „default“ → `auto` |
| 457 | StyleRows.cs:548 | button | Icon | icon | |
| 458 | IconPicker.cs:81/88 | button | None | none | |
| 459 | IconPicker.cs:95 | search | `###find`, „find by name...“ | = | |
| 460 | StyleRows.cs:605 | button | Plate / Plate * | plate / plate * | |
| 461 | StyleRows.cs:626 | input | `###platepath`, „a .png next to the tool, or a full path...“ | Platzhalter „.png path…“ | Note darunter wiederholt den Text → streichen |
| 462 | StyleRows.cs:631 | button | Use / None | use / none | |

---

## Routes

### Routes → Active Routes (`PoiLayer.DrawActiveRoutes`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 463 | PoiLayer.cs:458 | text | you stand at | = | |
| 464 | PoiLayer.cs:497 | button | Clear All | clear all | |
| 465 | PoiLayer.cs:510-516 | column | `##colour`, leads to, x, y, walk, stops, `##drop` | = | |
| 466 | PoiLayer.cs:553 | button | remove | = | |
| 467 | PoiLayer.cs:561 | button | remove stops | = | |

### Routes → Settings (`PoiLayer.DrawRouteSettings`)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 468 | PoiLayer.cs:415 | group | On the Map | map overlay | |
| 469 | PoiLayer.cs:418 | toggle | Draw the Routes | draw routes | |
| 470 | PoiLayer.cs:427 | toggle | Arrows Along Them | draw arrows | Kind von #469 (Fortsetzungs-Label) |
| 471 | PoiLayer.cs:436 | toggle | Points of Interest Window | — | toggle kann entfernt werden |
| 472 | PoiLayer.cs:444 | group | The Mouse on the Large Map | — | Gruppe entfällt; Inhalt ist nur eine Note → Note ans Ende von „Active Routes“ verschieben |
| 473 | PoiLayer.cs:1160 | button | Clear All | clear all | im POI-Fenster, nicht auf der Seite |

---

## Appearance (`StyleWindow.cs` und was es zeichnet)

| # | file:line | kind | alt | neu | anmerkung |
|---|---|---|---|---|---|
| 474 | StyleWindow.cs:155 | subsection | Text and Panels | text & panels | |
| 475 | StyleWindow.cs:168 | slider | Text Size (fmt `%d px`) | text size (fmt `%d px`) | |
| 476 | StyleWindow.cs:202 | slider | Tool Panels (fmt `%d%% solid`) | panel opacity (fmt `%d %`) | Hint bleibt |
| 477 | StyleWindow.cs:211 | slider | Readout (fmt `%d%% solid`) | readout opacity (fmt `%d %`) | Hint bleibt |
| 478 | StyleWindow.cs:224 | button | Reset text and panels | reset | |
| 479 | StyleWindow.cs:127 | subsection | Windows | windows | |
| 480 | WindowChrome.cs:823-825 | column | window, locked, click-through | = | |
| 481 | WindowChrome.cs:838/845 | checkbox | `##locked-…`, `##through-…` | = | |
| 482 | WindowChrome.cs:771 | menu | Locked  (cannot be dragged) | locked | Klammer → Tooltip |
| 483 | WindowChrome.cs:777 | menu | Click-through  (the game gets the click) | click-through | dito |
| 484 | StyleWindow.cs:132 | subsection | Tabs | tabs | |
| 485 | ToolTabs.cs:799 | checkbox | {page.Label} | = (folgt den Seitennamen) | |
| 486 | StyleWindow.cs:137 | subsection | Keys | keys | |
| 487 | EntityOverlay.cs:2737 | group | Screenshot | screenshot | |
| 488 | EntityOverlay.cs:2741 | button | Press a key...  (Esc cancels) | press a key… (Esc cancels) | |
| 489 | EntityOverlay.cs:2753 | button | {Taste} | = | Hint bleibt |
| 490 | CaptureKey.cs:1124 | group | Capture for Diagnosis | diagnosis capture | |
| 491 | CaptureKey.cs:1127 | button | Press a key... (Esc cancels) | press a key… (Esc cancels) | |
| 492 | CaptureKey.cs:1139 | button | {Taste} | = | Hint bleibt |
| 493 | CaptureKey.cs:1167 | text (Gruppen) | Pictures, Entities, Area files, Rooms by ground and tiles, Rooms by doodads, Windows, Memory | pictures, entities, area, rooms by tiles, rooms by doodads, windows, memory | Ordnernamen bleiben |
| 494 | F:CaptureParts.cs:265 | row | the game alone | game only | |
| 495 | F:CaptureParts.cs:267 | row | the game with the overlay | game + overlay | |
| 496 | F:CaptureParts.cs:270 | row | every entity | entities | |
| 497 | F:CaptureParts.cs:275 | row | every monster's mods and buffs | monster mods & buffs | |
| 498 | F:CaptureParts.cs:282 | row | the area's loaded files | area loaded files | |
| 499 | F:CaptureParts.cs:286 | row | the environments | environments | |
| 500 | F:CaptureParts.cs:288 | row | the room files | room files | |
| 501 | F:CaptureParts.cs:292 | row | the rooms around you | nearby rooms | |
| 502 | F:CaptureParts.cs:295 | row | every room arranged | arranged rooms | |
| 503 | F:CaptureParts.cs:298 | row | the Tile Book's picked row | tile book pick | |
| 504 | F:CaptureParts.cs:301 | row | the laid tiles' definitions | tile definitions | |
| 505 | F:CaptureParts.cs:304 | row | the terrain's second grid | projectile grid | |
| 506 | F:CaptureParts.cs:310 | row | the rooms' slots against the tilesets | slot candidates | |
| 507 | F:CaptureParts.cs:314 | row | the tile lists | tile lists | |
| 508 | F:CaptureParts.cs:317 | row | the tile files' chains | tile file chains | |
| 509 | F:CaptureParts.cs:321 | row | the area's masters | master file | |
| 510 | F:CaptureParts.cs:325 | row | the ground's makings | ground | |
| 511 | F:CaptureParts.cs:333 | row | the ground on the screen | ground on screen | |
| 512 | F:CaptureParts.cs:341 | row | the water's makings | water | |
| 513 | F:CaptureParts.cs:347 | row | the water's textures | water textures | |
| 514 | F:CaptureParts.cs:357 | row | the doodads found | doodads | |
| 515 | F:CaptureParts.cs:362 | row | every room placed | placed rooms | |
| 516 | F:CaptureParts.cs:366 | row | the active routes | active routes | |
| 517 | F:CaptureParts.cs:368 | row | the light hunt's report | light hunt | |
| 518 | F:CaptureParts.cs:371 | row | the model probe | model probe | |
| 519 | F:CaptureParts.cs:374 | row | the model panes' lines | model panes | |
| 520 | F:CaptureParts.cs:380 | row | the dissector | dissector | |
| 521 | F:CaptureParts.cs:383 | row | the atlas check | atlas | |
| 522 | F:CaptureParts.cs:386 | row | the light and water hunt | light & water hunt | |
| 523 | F:CaptureParts.cs:394 | row | a memory recording | memory recording | |
| 524 | F:CaptureParts.cs:397 | row | the raw bytes | in raw bytes | |

Gesamt: **524 Zeilen** (93a-d = 93-96).

---

## Strukturvorschläge (Seiten/Tools verschieben)

**S1 – Status → Live readout entschlacken.** alt: Live readout = Readout-Tabelle + rule history + „What Is Drawn“
(room/ground names, hide noise, keep out-of-range, dot labels, bars only when hurt) + „Getting Out of the Way“
(hide under panels, keep map off HUD …) + debug-Kalibrierung. neu: Live readout zeigt nur Readout + rule history.
Die Schalter wandern dorthin, wo ihr Ergebnis gezeichnet und gestylt wird (S3). Grund: die Seite ist laut
Kommentar die „im Eck während des Kampfs“-Seite; Einstellungen dort sind schwer zu finden und widersprechen dem
Grundsatz „Style wo das Feature lebt“.

**S2 – Darstellung von Features bleibt beim Feature, aber einheitlich benannt.** alt: „How projectiles look“
(Combat), „Visual Style“ (Atlas), Style-Zeilen unten in „Loaded Files“, Belt/Skill bar mit eigenen Style-Zeilen,
Appearance = nur das Tool selbst. neu: Jedes Feature-Style-Tool heißt `style` (Combat → `projectile style`, Atlas →
`style`), Loaded-Files-Style als eigenes Tool `loaded files style` oder in Watch List (es stylt Eckfenster und
Ankündigungskarte, die zu den Watch-List-Schaltern #99/#100 gehören). Appearance bleibt für das Tool selbst
(Text, Fenster, Tabs, Tasten). Alternative (nicht empfohlen): alles nach Appearance – widerspricht dem Hint
StyleWindow.cs:163 („styled where each feature lives“).

**S3 – Markers wird die Seite für alles auf Karte und Welt.** alt: Room/Ground names, Hide noise, Keep out-of-range,
Dot labels auf Status; „Health Bars Only Once Hurt“ auf Status, Style auf Markers → Health Bars; Keep map off HUD auf
Status. neu: Markers → Entities & Drops bekommt `hide noise`, `keep out-of-range`, `dot labels`; Markers → Map &
Places bekommt `room names` (+Kinder), `ground names` (+Kinder), `keep map off HUD`; Markers → Health Bars bekommt
`bars only when hurt`; „hide under panels / hide windows on panels“ → Appearance → Windows (betrifft Fenster) bzw.
Markers als eigene Group `hiding`. Grund: Schalter neben den Style-Zeilen desselben Dings.

**S4 – Routen an einem Ort.** alt: Route-Farben (Style-Gruppe „Routes“, #435-437) auf Markers → Map & Places,
Routen-Schalter auf Routes → Settings, Map Pins auf Area. neu: Style-Gruppe „Routes“ nach Routes → Settings
(StyleCatalogue.Homes.MarkerPlaces ohne „Routes“, eigenes Home für Routes); „arrows“ (#470) und dessen Style #437
liegen dann zusammen. Map Pins bleibt auf Area (Spiel-Kartenpins/Quests, keine Routen).

**S5 – Preisquellen einmal.** alt: „Fetch poe.ninja“ doppelt (Stash → Price Sources #266 und Runecraft #295, laut
Hint derselbe Schalter), Rates „read live rates“ (#291) separat. neu: eine Seite/ein Tool `prices` (z. B. Stash →
price sources bleibt Heimat), Runecraft und Rates zeigen nur eine Statuszeile mit Link „set in stash → price
sources“. Optional Wealth + Rates + Stash zu einer Seite `economy` mit Tools stash / wealth / rates / sources
zusammenfassen (drei Seiten weniger auf der Leiste).

**S6 – Atlas „Debug Log“ umbenennen und an „check the read“ koppeln.** alt: Tool „Debug Log“, Knopf „Check the
Read“ im Lead. neu: Tool `check log`; der Knopf bleibt im Lead. Grund: „debug“ suggeriert Entwickler-Inhalt, ist
aber der Bericht des Spielerknopfs.

**S7 – Entwickler-Inhalte aus Spielerseiten nach Inspect.** Measuring Aids (Markers, nur --debug sinnvoll; Zeilen
„selected/hovered UI element“ gehören zum UI Browser), Calibration Aids / Probe height / Kind-Filter (#66-69).
neu: Tool `measuring aids` nach Inspect.

**S8 – Diagnoseknöpfe aus Spieler-Tools in die Capture.** Loaded Files: find count field, inspect rooms, export
rooms, hunt placements (#82-85); Runecraft „What the Panel Read“ (#323); Stash „test exchange“ (#268). Laut
CLAUDE.md gehört Diagnose in die Capture („nothing goes on a window as a copy button“). neu: entfernen bzw. hinter
--debug, als Capture-Teile anbieten.

**S9 – Status-Seite: Update und Read cost.** alt: Status = Live readout, Update, Read cost. neu: Update nach
Appearance (oder eigener Seite `about`/`tool`), Read cost bleibt bei Status (Messwert), aber nur mit --debug. Grund:
Status ist die „im Eck“-Ansicht; der Update-Tab zieht sie sonst bei verfügbarem Update nach vorne.

**S10 – Combat: Belt und Skill bar zusammen.** alt: zwei Tools mit je einem Master + 2 Style-Zeilen. neu: ein Tool
`HUD figures` mit zwei Groups (`flask uses`, `skill DPS`). Grund: zwei fast leere Tabs.

**S11 – Tracker aufteilen?** alt: Combat → Tracker mit vier inneren Tabs (monsters & facing, hazards, status
effects, live buffs). neu: belassen, aber „live buffs“ unter „status effects“ als Subsection (es ist das Werkzeug
zum Anlegen von Status-Regeln, Klick = Regel).

**S12 – Wealth-Tabs.** alt: „Breakdown & Inventory“ / „History & Graph“. neu: `breakdown` / `history`; „Enable
Wealth Tracker“ als echter Master (wie Expedition/Runecraft), „Show Overlay“ als Kind `corner panel`.

---

## Bericht / Unsicherheiten

- Datei: `/home/user/immo-poe/docs/ui-labels-proposal.md`, **524 nummerierte Zeilen**, 12 Strukturvorschläge.
- Spaltenköpfe derselben Tabelle sind je in einer Zeile zusammengefasst (sonst ~100 Zeilen mehr); bei Bedarf einzeln ausrollen.
- Nicht aufgenommen: reine Statustexte/Notes (TextColored, Wrapped), dynamische Datenlabels (Kartennamen, Buff-Namen,
  Stash-Tab-Namen, Selectables mit Werten), das POI-Fenster selbst (PoiLayer.cs:1219, kein Seiten-Tool – nur „Clear All“ #473),
  Atlas-Gruppennamen aus Datendatei (#229), Rarity/Kind-Optionen der Combos `ColourModes` / `KeyNames`.
- Unsicher: `read live rates` (#291) – ob es derselbe Schalter wie poe.ninja ist, habe ich nicht verfolgt; Hint-Vorschlag prüfen.
  „+ Watch“ (#92): der gefundene Hint „Click to copy this path.“ gehört vermutlich zum Selectable daneben, nicht zum Knopf.
  „shadow“ (#183) als % ist ein Vorschlag (Wert 0..1). Zeilennummern der Readout-Zeilen (#37) sind ein Bereich.
  Ob „ES“ (#441) statt „energy shield“ gewollt ist, ist Geschmack. Die Kleinschreibung von „poe.ninja“/„poe2db“
  entspricht der Eigenschreibweise.
