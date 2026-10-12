# The engine's beach waves' graph

`BreakingOceanWaves.fxgraph`, as Port's v0.1.241 capture (2026-10-12) wrote it whole in `water-textures.txt` (the
capture's beach waves section, `CaptureWaves`): the graph the game's program names for the beach waves, its
BreakingWavesVertex and BreakingWavesFragment nodes and the textures it binds to them. `ShadeProgram.BeachOf` builds
the beach program from it, and `BeachTests` holds that against the file on every build: the foam's colour
(`SeaFoam_colour_Uncompressed.dds`, sRGB), its normal (`SeaFoam_normal_Uncompressed.dds`) and the ripples'
(`backwatertile_normal_DXT1.dds`). Its wave shape (`gerstner_positive_wave_profile.dds`) is under
`tests/fixtures/water-lookups`.
