# The engine's water lookups

Five of the files Port's captures (v0.1.231 and v0.1.241, 2026-10-12) wrote whole beside `water-textures.txt`,
as the capture names them (`water-lookup-` and the file's own name), so `WaterLookups` is held against the
engine's own files on every build (`WaterLookupsTests`, `BeachTests`):

| File | What it is |
|---|---|
| `surface_ripple.dds` | `surface_tex_sampler`, 128 square, Direct3D format 116 (four floats a texel, 262,272 bytes: the header and 128 x 128 x 16): its green and blue are its red's own differences a texel along x and y, correlation 1, scale 1 |
| `wave_profile.dds` | `wave_data_sampler`, the swell's cross-section, 128 square, format 116: its red's differences along the phase go with the gradient's red at correlation 1, scale 1, and along the shore's distance with the gradient's green at 0.997. Its green is the trail foam's strength, its blue the orbit about 0.5, its alpha the front's phase (-1 to 2) |
| `wave_profile_gradient.dds` | `wave_gradient_sampler`, the profile's slope: red along the phase, green along the shore's distance |
| `gerstner_wave_profile.dds` | 256 square, format 116 - the program names it, but it does not go with the gradient (correlation 0.03): the beach's own shape, kept as the decoy the check must turn down |
| `gerstner_positive_wave_profile.dds` | the beach waves' `wave_shape_precomp_tex` (BreakingOceanWaves.fxgraph binds it by path), 256 square, Direct3D format 115 (two floats a texel): red the crest's sideways offset (-0.159 to 0.159), green its height (0 to 0.304) by the wave's coordinate across and its break phase down - `BeachTests` holds the crest it makes against a beach |
