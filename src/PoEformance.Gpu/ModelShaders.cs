using PoEformance.Game.Files;

namespace PoEformance.Gpu;

/// <summary>
/// The HLSL a model's picture is drawn with - MeshPicture's arithmetic, pixel for pixel where a card can match it.
/// </summary>
/// <remarks>
/// SHADER MODEL 4, so a device the overlay could only get at feature level 10 still draws: nothing
/// here needs more. Compiled at run time like the overlay's own ImGui shaders, through the
/// d3dcompiler_47.dll every Windows since 8 carries.
///
/// THE COLOURS ARE sRGB NUMBERS THROUGHOUT, as MeshPicture keeps them: a texture is read as it is
/// stored (an R8G8B8A8_UNORM view, no conversion) and shaded by multiplying those numbers, so the two
/// pictures agree. The picture's own lamp and ambient are MeshPicture.Lamp and MeshPicture.Ambient.
///
/// TRANSLUCENT SHAPES ARE ADDED IN PREMULTIPLIED TERMS into a float target and turned back into
/// straight colour at the end, the order MeshPicture.Drawing.Over works in: a mixed layer covers its
/// alpha's worth, an added one adds its colour times its alpha and grows the coverage by the brightest
/// channel of what it added.
///
/// THE GAME'S LIGHT IS SceneLight.Shade, line for line (Lighted): the sun through its shadow map, the
/// point lights through the same grid, the flat ambient or the diffuse cube, a glossy material's lobe
/// and environment, the exposure and the colour grade - and the open water's SceneLight.ShadeWater
/// (WaterShaded), the engine's water pass, its cover its light's. Every table the processor reads by - sRGB to
/// light and back, the grade's gamma, the gloss environment - is handed over as it is rather than
/// worked out again, so both pictures read the same entry; and the cube and the grade are read texel
/// by texel as CubeMap.Sample and ColourGrade.Sample read them, not through the card's own filtering,
/// which rounds its weights and crosses a cube's seams.
///
/// A MATERIAL'S PROGRAM IS A SHADER OF ITS OWN (<see cref="Program"/>): <see cref="Common"/>, the
/// program as ShadeHlsl writes it, and <see cref="Programmed"/>'s entry points around it - the solid
/// and the mixed pass, under the lamp or the game's light, as MeshPicture.Drawing runs a program in
/// each.
/// </remarks>
internal static class ModelShaders
{
    /// <summary>The slot the solid depth behind is read from by a mixed program - see ModelTarget.Behind.</summary>
    public const int BehindSlot = 7;

    /// <summary>The slot the specular cube is read from, for the water's reflection - SceneLight.Mirror.</summary>
    public const int MirrorSlot = 8;

    /// <summary>The slot the solid part of the picture is read from, for the water's reflection of it - ScreenReflection.</summary>
    public const int OpaqueSlot = 9;

    /// <summary>
    /// The slot the caustics' picture is read from - ShadeProgram.Caustics, by the water's refraction alone: high, clear of a program's
    /// sheets as the skin's slots are, the next its distortion's.
    /// </summary>
    public const int CausticsSlot = 123;

    /// <summary>The slot the caustics' distortion is read from - ShadeProgram.Distortion; see <see cref="CausticsSlot"/>.</summary>
    public const int DistortionSlot = 124;

    /// <summary>
    /// The slot the area's river flow map is read from, by a river's program (ShadeHlsl.RiverLibrary over WaterLook.Flow) - high, clear of a
    /// program's sheets, below the caustics'.
    /// </summary>
    public const int RiverFlowSlot = 122;

    /// <summary>The slot the ripple texture is read from, by the waves' pixel and vertex shaders - ShadeHlsl.RippleSlot.</summary>
    public const int RippleSlot = ShadeHlsl.RippleSlot;

    /// <summary>The slot the room's shore is read from, by the waves' shaders - ShadeHlsl.ShoreSlot.</summary>
    public const int ShoreSlot = ShadeHlsl.ShoreSlot;

    /// <summary>The slot the swell's cross-section is read from, by the waves' shaders - ShadeHlsl.SwellSlot.</summary>
    public const int SwellSlot = ShadeHlsl.SwellSlot;

    /// <summary>The slot the beach waves' wave shape is read from, by the beach's pixel and vertex shaders - ShadeHlsl.BeachProfileSlot.</summary>
    public const int BeachProfileSlot = ShadeHlsl.BeachProfileSlot;

    /// <summary>The slot the beach waves' noise is read from - ShadeHlsl.BeachNoiseSlot.</summary>
    public const int BeachNoiseSlot = ShadeHlsl.BeachNoiseSlot;

    /// <summary>The slot the field waves' wave shape is read from - ShadeHlsl.FieldProfileSlot, by the pixel and the vertex shaders alike.</summary>
    public const int FieldProfileSlot = ShadeHlsl.FieldProfileSlot;

    /// <summary>The slot the field waves' foam colour is read from - ShadeHlsl.FieldFoamColourSlot.</summary>
    public const int FieldFoamColourSlot = ShadeHlsl.FieldFoamColourSlot;

    /// <summary>The slot the field waves' foam normal is read from - ShadeHlsl.FieldFoamNormalSlot.</summary>
    public const int FieldFoamNormalSlot = ShadeHlsl.FieldFoamNormalSlot;

    /// <summary>The slot a program's first sheet is read from; the rest follow - after the fixed lookups.</summary>
    public const int FirstSheet = ShadeHlsl.FieldFoamNormalSlot + 1;

    /// <summary>
    /// The vertex shader's slot for a skinned mesh's bones, four bytes a vertex - and the next its weights: the last two of the 128 a
    /// shader has, clear of a program's sheets, which run on from <see cref="FirstSheet"/> as many as it reads.
    /// </summary>
    public const int SkinBonesSlot = 126;

    /// <summary>The vertex shader's slot for a skinned mesh's weights - see <see cref="SkinBonesSlot"/>.</summary>
    public const int SkinWeightsSlot = 127;

    /// <summary>The most bones the card skins by: a vertex names its bones in bytes.</summary>
    public const int MostBones = 256;

    /// <summary>
    /// What every shader here shares: the constants, the resources, the vertex shader, and the light - the picture's lamp and the game's.
    /// </summary>
    public static readonly string Common = $$"""
        cbuffer Frame : register(b0)
        {
            row_major float4x4 Clip;
            row_major float4x4 View;
            float4 Lamp;      // xyz: the picture's lamp, in view space
            float4 Ink;
            float4 Shade;     // x: the ambient on the sRGB value; y: the ambient as light; z: the lamp as light; w: the lamp's Fresnel
            float4 Halfway;   // xyz: between the eye and the lamp, in view space
            float4 Clock;     // x: the clock - ShadeProgram.Clock
            float4 Eye;       // the way into the picture in model space and the origin's depth - ShadeProgram.Eye
            float4 Depth;     // x: the reach the depth buffer was laid over, on a parallel view; y: the near plane's depth; z: one in perspective; w: the lift, SceneLight.WaterLift
            float4 Dust;      // xyz: the area's dust colour - ShadeProgram.Dust
            float4 Water;     // xyz: the area's open water, linear; w: its density - ShadeProgram.WaterColour
            float4 Flow;      // x: the area's water flow; y: its reflectiveness; z: its height offset; w: its flow_foam - ShadeProgram.WaterFlow
            float4 Wind;      // the ocean ripples' wind: its way's angle, their height, their speed, their directionness - ShadeProgram.WaterWind
            float4 Shore;     // the room's shore's box: its least x and y, world units a texel, one where there is a shore - ShadeProgram.WaterShore
            float4 Swell;     // the ocean's swell: its height, its period, its intensity - ShadeProgram.WaterSwell
            float4 WaterLit;  // x: the medium's scattering, water_subsurface_scattering - SceneLight.ShadeWater; y: the area's refraction index; z: one where the water refracts - SceneLight.WaterRefraction; w: the caustics' strength, nought for none - SceneLight.WaterCaustics
            float4 Viewer;    // xyz: the eye's place in model space; w: one in perspective - ShadeProgram.Viewer
            float4 RiverBox;  // the river flow map's box in the picture's frame: its least x and y, its size; size nought for no map - WaterLook.FlowBoxRegister
            row_major float4x4 Unclip;  // Clip's inverse: a point on the picture and its depth back to model space - the engine's GetViewPoint
        };

        cbuffer Part : register(b1)
        {
            // x: one where the shape wears a texture; y: the alpha under which a cut-out drops a pixel, or below nought.
            float4 Flags;

            // THE RUN'S SWAY, row vectors in the mesh's frame - RoomSway.At, the identity for a run that does not sway: a ship and
            // its deck turned by the wobble core's bone as the clock runs, the mesh's own vertices left as they are.
            row_major float4x4 Sway;
        };

        // SceneLight's numbers, model space - ModelGpu.SceneConstants, field for field.
        cbuffer Scene : register(b2)
        {
            row_major float4x4 CubeTurn;
            float4 SunColour;   // rgb: the sun's light; w: one where it shines
            float4 SunTravels;  // xyz: the way its light travels; w: one where its shadow map is read
            float4 ToEye;       // xyz: the way to the eye; w: one where the waves reflect the picture itself
            float4 Surround;    // x: the flat ambient; y: cube brightness; z: direct light env ratio; w: gi env occlusion
            float4 Finish;      // x: the exposure; y: one where the waves glint; z: one where they reflect; w: the specular cube's side, nought for none
            float4 CellLeast;   // xyz: the light grid's corner; w: a cell's side
            float4 ShadowU;     // xyz: the shadow map's x axis; w: its corner along it
            float4 ShadowV;     // xyz: its y axis; w: its corner along it
            float4 ShadowW;     // xyz: the way depth grows; w: the depth the map starts at
            float4 ShadowSize;  // x: texels per unit; y: units per texel; z: texels across
            int4 Ambient;       // x: nought none, one flat, two the cube; y: one where graded; z: the cube's side; w: point lights
            int4 Cells;         // xyz: the light grid's cells each way; w: where the cells' entries start in Reach
            int4 Graded;        // xyz: the grade's texels each way; w: the picture's view - SceneLight.View
        };

        // THE BONES THE CARD SKINS BY - ModelGpu.Skinned: x one where it skins; y the first vertex it moves; z the rig's bones, a bone at
        // or past them moving nothing; w how many vertices from y on the weighted mesh has. Then each bone's matrix, row vectors.
        cbuffer Bones : register(b3)
        {
            int4 Skinning;
            row_major float4x4 Bone[{{MostBones}}];
        };

        // THE AIR, written once a picture (ModelGpu.Aired - AirConstants, field for field): the clouds every surface the sun lights reads
        // (Clouded) and the screen-space fog's pass (ModelGpu.Fogged, ModelShaders.Air).
        cbuffer Air : register(b5)
        {
            float4 AirFog;     // the pass's ssf_color - ScreenFog.Colour
            float4 AirShape;   // xy: ssf_disperse_radius and ssf_feathering - ScreenFog.Shape
            float4 AirPlayer;  // xyz: PlayerTrailingGhost in the area's frame - on a stage the camera's focus (CHOSEN)
            float4 AirLight;   // rgb: GetTotalEnvLight - ScreenFog.Environment
            float4 AirOrigin;  // xy: where the picture's frame stands in the area - SceneLight.AreaOrigin
            float4 AirClouds;  // clouds_scale, clouds_intensity, clouds_midpoint, clouds_sharpness - AreaClouds
            float4 AirDrift;   // xy: the noise's x axis, clouds_velocity's way; z: its speed; w: clouds_fade_radius - AreaClouds.Drift
            float4 AirFades;   // x: clouds_pre_fade; y: clouds_post_fade; z: one where the clouds are drawn - SceneLight.Clouds
        };

        // EACH VERTEX'S FOUR BONES AND FOUR WEIGHTS, a byte each - the weighted mesh's own arrays, from Skinning.y on.
        Buffer<uint4> SkinBones : register(t{{SkinBonesSlot}});
        Buffer<uint4> SkinWeights : register(t{{SkinWeightsSlot}});

        // THE WAY TO THE EYE FROM THE PIXEL BEING LIT, model space - MeshPicture.Drawing.EyeWay: the scene's one way on a parallel view, the
        // pixel's own in perspective. Set by every entry point that lights the scene's way, before it lights.
        static float3 EyeWay = float3(0.0, 0.0, 1.0);

        void SeeFrom(float3 place)
        {
            float3 way = Viewer.xyz - place;
            float length = sqrt(dot(way, way));
            EyeWay = Viewer.w > 0.5 && length > 1e-6 ? way / length : ToEye.xyz;
        }

        Texture2D Skin : register(t0);
        Buffer<float> Tabled : register(t1);
        Buffer<float4> Points : register(t2);
        Buffer<uint> Reach : register(t3);
        Buffer<float> Cube : register(t4);
        Buffer<float> Grade : register(t5);
        Texture2D<float> Shadow : register(t6);
        Texture2D<float> Behind : register(t{{BehindSlot}});
        Buffer<float> Mirror : register(t{{MirrorSlot}});
        Texture2D<float4> Opaque : register(t{{OpaqueSlot}});
        Texture2D<float4> CausticsSheet : register(t{{CausticsSlot}});
        Texture2D<float4> DistortionSheet : register(t{{DistortionSlot}});
        Texture2D<float4> RiverFlowSheet : register(t{{RiverFlowSlot}});
        SamplerState Wrap : register(s0);

        // THE TABLES, one after another in Tabled - ModelGpu.Tables lays them out.
        static const int LinearSteps = {{ShadeProgram.TableSteps}};
        static const int GammaSteps = {{ColourGrade.EncodingSteps}};
        static const int GlossSide = {{GlossLight.TableSide}};
        static const int SrgbAt = LinearSteps;
        static const int GammaAt = 2 * LinearSteps;
        static const int BiasAt = GammaAt + GammaSteps + 1;
        static const int ScaleAt = BiasAt + (GlossSide * GlossSide);

        struct Corner
        {
            float3 position : POSITION;
            float3 normal : NORMAL;
            float2 spot : TEXCOORD0;
            float4 tint : COLOR0;
            uint vertex : SV_VertexID;
        };

        // SkeletonPose.Move, vertex for vertex: each weight's bone moving the vertex on its own and the results mixed in the weights'
        // proportion - a weight of nought and a bone past the rig skipped - the normal turned alike and made unit length again, and a
        // vertex nothing weighs left where it is. The vertex's number is the index's own, every draw's base vertex being nought.
        void Skinned(uint vertex, inout float3 position, inout float3 normal)
        {
            uint own = vertex - (uint)Skinning.y;
            if (Skinning.x == 0 || vertex < (uint)Skinning.y || own >= (uint)Skinning.w)
            {
                return;
            }

            uint4 bones = SkinBones.Load(own);
            uint4 weights = SkinWeights.Load(own);
            float3 moved = float3(0.0, 0.0, 0.0);
            float3 turned = float3(0.0, 0.0, 0.0);
            float weight = 0.0;
            [unroll]
            for (int part = 0; part < 4; part++)
            {
                uint bone = bones[part];
                if (weights[part] == 0 || bone >= (uint)Skinning.z)
                {
                    continue;
                }

                float share = weights[part] / 255.0;
                moved += mul(float4(position, 1.0), Bone[bone]).xyz * share;
                turned += mul(float4(normal, 0.0), Bone[bone]).xyz * share;
                weight += share;
            }

            if (weight <= 0.0)
            {
                return;
            }

            position = moved / weight;
            float length = sqrt(dot(turned, turned));
            normal = length > 1e-6 ? turned / length : normal;
        }

        struct Pixel
        {
            float4 position : SV_Position;
            float3 facing : NORMAL;
            float2 spot : TEXCOORD0;
            float3 place : TEXCOORD1;
            float3 turn : TEXCOORD2;
            float4 tint : COLOR0;
        };

        // THE CARD'S INTERPOLATION IS MeshPicture's: affine on a parallel view, where w is one, and perspective-correct in
        // perspective, where MeshPicture.Drawing's Edges hands out the weights of the point each pixel's ray meets.
        Pixel Placed(Corner corner)
        {
            // SKINNED AND SWAYED FIRST, so everything after - the light, the place a program reads - sees the vertex where it stands now.
            Skinned(corner.vertex, corner.position, corner.normal);
            float3 swayedPlace = mul(float4(corner.position, 1.0), Sway).xyz;
            float3 swayedTurn = mul(float4(corner.normal, 0.0), Sway).xyz;
            Pixel pixel;
            pixel.position = mul(float4(swayedPlace, 1.0), Clip);
            pixel.facing = mul(float4(swayedTurn, 0.0), View).xyz;
            pixel.spot = corner.spot;
            pixel.place = swayedPlace;
            pixel.turn = swayedTurn;
            pixel.tint = corner.tint;
            return pixel;
        }

        // ShadeProgram.Linear and Srgb: the table's nearest entry, nought for not a number as saturate gives it.
        float LinearOf(float value)
        {
            return Tabled.Load((int)((saturate(value) * (LinearSteps - 1)) + 0.5));
        }

        float SrgbOf(float value)
        {
            return Tabled.Load(SrgbAt + (int)((saturate(value) * (LinearSteps - 1)) + 0.5));
        }

        float3 Linear(float3 colour)
        {
            return float3(LinearOf(colour.r), LinearOf(colour.g), LinearOf(colour.b));
        }

        float3 Srgb(float3 colour)
        {
            return float3(SrgbOf(colour.r), SrgbOf(colour.g), SrgbOf(colour.b));
        }

        // GlossLight, line for line: the Fresnel, the lobe, and the environment read from its table.
        float GlossFresnel(float vdoth)
        {
            return exp2(((-5.55473 * vdoth) - 6.98316) * vdoth);
        }

        float GlossLobe(float3 normal, float toEye, float3 lamp, float3 halfway, float gloss)
        {
            float lit = dot(normal, lamp);
            if (lit <= 0.0)
            {
                return 0.0;
            }

            lit = min(lit, 1.0);
            float roughness = 1.0 - saturate(gloss);
            float alpha = max(roughness * roughness, 2e-3);
            float alpha2 = alpha * alpha;
            float facing = saturate(dot(normal, halfway));
            float spread = ((alpha2 - 1.0) * facing * facing) + 1.0;
            float k = alpha * 0.5;
            float shadowing = 1.0 / (((toEye * (1.0 - k)) + k) * ((lit * (1.0 - k)) + k));
            return alpha2 / (spread * spread) * shadowing * lit * 0.25;
        }

        float GlossMixed(int from, int x0, int x1, int y0, int y1, float fx, float fy)
        {
            float a = Tabled.Load(from + (y0 * GlossSide) + x0);
            float b = Tabled.Load(from + (y0 * GlossSide) + x1);
            float c = Tabled.Load(from + (y1 * GlossSide) + x0);
            float d = Tabled.Load(from + (y1 * GlossSide) + x1);
            float top = a + ((b - a) * fx);
            float bottom = c + ((d - c) * fx);
            return top + ((bottom - top) * fy);
        }

        void GlossEnvironment(float toEye, float gloss, out float bias, out float scale)
        {
            float x = clamp((saturate(toEye) * GlossSide) - 0.5, 0.0, GlossSide - 1.0);
            float y = clamp((saturate(gloss) * GlossSide) - 0.5, 0.0, GlossSide - 1.0);
            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = min(x0 + 1, GlossSide - 1);
            int y1 = min(y0 + 1, GlossSide - 1);
            float fx = x - x0;
            float fy = y - y0;
            bias = GlossMixed(BiasAt, x0, x1, y0, y1, fx, fy);
            scale = GlossMixed(ScaleAt, x0, x1, y0, y1, fx, fy);
        }

        // MeshPicture.Drawing.Lit's light: the ambient plus the rest times the lamp's cosine, two-sided.
        float LampShade(float3 facing, out float3 normal)
        {
            normal = dot(facing, facing) > 1e-6 ? normalize(facing) : facing;
            float lit = abs(dot(normal, Lamp.xyz));
            return Shade.x + ((1.0 - Shade.x) * lit);
        }

        // MeshPicture.Drawing.Glossed: the colour under the shading as light, the lamp's lobe and the environment added, and back.
        float3 LampGlossed(float3 colour, float shade, float3 normal, float3 specular, float gloss)
        {
            float toEye = -normal.z;
            if (toEye < 0.0)
            {
                normal = -normal;
                toEye = -toEye;
            }

            float lobe = GlossLobe(normal, toEye, Lamp.xyz, Halfway.xyz, gloss) * Shade.z;
            float bias;
            float scale;
            GlossEnvironment(toEye, gloss, bias, scale);
            float unscaled = (lobe * Shade.w) + (Shade.y * bias);
            float scaled = (lobe * (1.0 - Shade.w)) + (Shade.y * scale);
            return float3(
                SrgbOf(LinearOf(colour.x * shade) + unscaled + (scaled * specular.x)),
                SrgbOf(LinearOf(colour.y * shade) + unscaled + (scaled * specular.y)),
                SrgbOf(LinearOf(colour.z * shade) + unscaled + (scaled * specular.z)));
        }

        // MeshPicture.Drawing.Flat: the specular colour past a dielectric's laid onto the albedo.
        float3 Flattened(float3 colour, float3 specular)
        {
            return float3(
                SrgbOf(LinearOf(colour.x) + max(specular.x - 0.04, 0.0)),
                SrgbOf(LinearOf(colour.y) + max(specular.y - 0.04, 0.0)),
                SrgbOf(LinearOf(colour.z) + max(specular.z - 0.04, 0.0)));
        }

        // ShadowMap.Lit: a texel and a half off the surface, two texels of bias, four texels compared and lerped.
        float Open(int x, int y, float depth)
        {
            return depth <= Shadow.Load(int3(x, y, 0)) ? 1.0 : 0.0;
        }

        float SunReaches(float3 place, float3 normal)
        {
            float texel = ShadowSize.y;
            float3 off = place + (normal * (texel * 1.5));
            float x = ((dot(off, ShadowU.xyz) - ShadowU.w) * ShadowSize.x) - 0.5;
            float y = ((dot(off, ShadowV.xyz) - ShadowV.w) * ShadowSize.x) - 0.5;
            float depth = dot(off, ShadowW.xyz) - ShadowW.w - (texel * 2.0);
            float last = ShadowSize.z - 1.0;
            if (x < 0.0 || y < 0.0 || x >= last || y >= last)
            {
                return 1.0;
            }

            int x0 = (int)x;
            int y0 = (int)y;
            float fx = x - x0;
            float fy = y - y0;
            float top = lerp(Open(x0, y0, depth), Open(x0 + 1, y0, depth), fx);
            float bottom = lerp(Open(x0, y0 + 1, depth), Open(x0 + 1, y0 + 1, depth), fx);
            return lerp(top, bottom, fy);
        }

        // SamplerLinearWrap at level nought, as the engine reads its caustics' two textures and the clouds' noise: the four nearest texels'
        // centres mixed by hand, wrapping - ShadeProgram.Ripple's arithmetic and AreaClouds.Sample's, whatever sampler the picture is drawn with.
        float4 WrappedRead(Texture2D<float4> sheet, float2 spot)
        {
            uint width;
            uint height;
            sheet.GetDimensions(width, height);
            float2 at = (spot * float2(width, height)) - 0.5;
            float2 low = floor(at);
            float2 share = at - low;
            int2 size = int2(width, height);
            int2 a = (((int2)low % size) + size) % size;
            int2 b = (a + 1) % size;
            float4 top = lerp(sheet.Load(int3(a.x, a.y, 0)), sheet.Load(int3(b.x, a.y, 0)), share.x);
            float4 bottom = lerp(sheet.Load(int3(a.x, b.y, 0)), sheet.Load(int3(b.x, b.y, 0)), share.x);
            return lerp(top, bottom, share.y);
        }

        // math_utils' SmoothStep - the DECLARATIONS block shadows.ffx includes, saturated outside: AreaClouds.MathSmoothStep.
        float CloudStep(float value, float center, float curve)
        {
            float power = (2.0 / max(1e-3, 1.0 - curve)) - 1.0;
            return saturate(1.0 - center
                + (-pow(saturate(1.0 - max(center, value)), power) * pow(saturate(1.0 - center), -(power - 1.0)))
                + (pow(saturate(min(center, value)), power) * pow(saturate(center), -(power - 1.0))));
        }

        // shadows.ffx's GetCloudsIntensity (AreaClouds.At): the clouds' shadow on the sun's light at a place of the picture - the area's own
        // place read, so the pattern lies where the area lays it, the noise the distortion texture's alpha (AreaClouds.NoisePath, CHOSEN), the
        // player's fade round AirPlayer. One where none is drawn, or where the sun stands at the horizon (AreaClouds.LeastRise, 1e-3).
        float Clouded(float3 place)
        {
            if (!(AirFades.z > 0.5) || abs(SunTravels.z) < 1e-3)
            {
                return 1.0;
            }

            float2 area = place.xy + AirOrigin.xy;
            float fade = smoothstep(0.0, 1.0, saturate(1.0 - (length(AirPlayer.xy - area) / max(1e-4, AirDrift.w))));
            float2 reference = area - (SunTravels.xy / SunTravels.z * 1000.0);
            float2 across = float2(-AirDrift.y, AirDrift.x);
            float2 spot = float2(dot(AirDrift.xy, reference) + (AirDrift.z * Clock.x), dot(across, reference)) * (0.001 * AirClouds.x);
            float faded = saturate(WrappedRead(DistortionSheet, spot).a + (fade * AirFades.x));
            return lerp(1.0, saturate(CloudStep(faded, AirClouds.z, AirClouds.w) + (fade * AirFades.y)), AirClouds.y);
        }

        // THE SUN'S SHARE AT A SURFACE: its shadow map where it is read (SunTravels.w), times the clouds' shadow - the engine's ShadowMap
        // for the directional light, which multiplies the one by the other.
        float SunLit(float3 place, float3 normal)
        {
            return (SunTravels.w > 0.5 ? SunReaches(place, normal) : 1.0) * Clouded(place);
        }

        // CubeMap.Sample: Direct3D's face selection, bilinear within the face, clamped at its edge.
        float3 CubeTexel(int at)
        {
            return float3(Cube.Load(at * 3), Cube.Load((at * 3) + 1), Cube.Load((at * 3) + 2));
        }

        // CubeMap.Sample's face and place: where a direction lands on a cube size texels across, the four texels it lies between.
        bool CubeSpot(float3 direction, int size, out int start, out int x0, out int y0, out int x1, out int y1, out float fx, out float fy)
        {
            float3 size3 = abs(direction);
            int face;
            float sc;
            float tc;
            float ma;
            if (size3.x >= size3.y && size3.x >= size3.z)
            {
                face = direction.x >= 0.0 ? 0 : 1;
                sc = direction.x >= 0.0 ? -direction.z : direction.z;
                tc = -direction.y;
                ma = size3.x;
            }
            else if (size3.y >= size3.z)
            {
                face = direction.y >= 0.0 ? 2 : 3;
                sc = direction.x;
                tc = direction.y >= 0.0 ? direction.z : -direction.z;
                ma = size3.y;
            }
            else
            {
                face = direction.z >= 0.0 ? 4 : 5;
                sc = direction.z >= 0.0 ? direction.x : -direction.x;
                tc = -direction.y;
                ma = size3.z;
            }

            start = 0;
            x0 = 0;
            y0 = 0;
            x1 = 0;
            y1 = 0;
            fx = 0.0;
            fy = 0.0;
            if (!(ma > 0.0))
            {
                return false;
            }

            float u = clamp((((sc / ma) + 1.0) * 0.5 * size) - 0.5, 0.0, size - 1.0);
            float v = clamp((((tc / ma) + 1.0) * 0.5 * size) - 0.5, 0.0, size - 1.0);
            x0 = (int)u;
            y0 = (int)v;
            x1 = min(x0 + 1, size - 1);
            y1 = min(y0 + 1, size - 1);
            fx = u - x0;
            fy = v - y0;
            start = face * size * size;
            return true;
        }

        float3 CubeRead(float3 direction)
        {
            int size = Ambient.z;
            int start;
            int x0;
            int y0;
            int x1;
            int y1;
            float fx;
            float fy;
            if (!CubeSpot(direction, size, start, x0, y0, x1, y1, fx, fy))
            {
                return float3(0.0, 0.0, 0.0);
            }

            float3 top = lerp(CubeTexel(start + (y0 * size) + x0), CubeTexel(start + (y0 * size) + x1), fx);
            float3 bottom = lerp(CubeTexel(start + (y1 * size) + x0), CubeTexel(start + (y1 * size) + x1), fx);
            return lerp(top, bottom, fy);
        }

        float3 MirrorTexel(int at)
        {
            return float3(Mirror.Load(at * 3), Mirror.Load((at * 3) + 1), Mirror.Load((at * 3) + 2));
        }

        // SceneLight.Reflected's cube: the specular cube where one is bound, the diffuse cube where not.
        float3 MirrorRead(float3 direction)
        {
            int size = (int)Finish.w;
            if (size <= 0)
            {
                return CubeRead(direction);
            }

            int start;
            int x0;
            int y0;
            int x1;
            int y1;
            float fx;
            float fy;
            if (!CubeSpot(direction, size, start, x0, y0, x1, y1, fx, fy))
            {
                return float3(0.0, 0.0, 0.0);
            }

            float3 top = lerp(MirrorTexel(start + (y0 * size) + x0), MirrorTexel(start + (y0 * size) + x1), fx);
            float3 bottom = lerp(MirrorTexel(start + (y1 * size) + x0), MirrorTexel(start + (y1 * size) + x1), fx);
            return lerp(top, bottom, fy);
        }

        // ColourGrade.Encoded: pow(x, 1/2.2) read between the gamma table's steps.
        float Encoded(float value)
        {
            if (!(value > 0.0))
            {
                return 0.0;
            }

            if (value >= 1.0)
            {
                return 1.0;
            }

            float at = value * GammaSteps;
            int step = (int)at;
            return lerp(Tabled.Load(GammaAt + step), Tabled.Load(GammaAt + step + 1), at - step);
        }

        // ColourGrade.Sample: trilinear, each step along an axis a texel or none at the far edge.
        float3 GradeTexel(int at)
        {
            return float3(Grade.Load(at * 3), Grade.Load((at * 3) + 1), Grade.Load((at * 3) + 2));
        }

        float3 GradeRead(float u, float v, float w)
        {
            int3 size = Graded.xyz;
            float x = clamp((u * size.x) - 0.5, 0.0, size.x - 1.0);
            float y = clamp((v * size.y) - 0.5, 0.0, size.y - 1.0);
            float z = clamp((w * size.z) - 0.5, 0.0, size.z - 1.0);
            int x0 = (int)x;
            int y0 = (int)y;
            int z0 = (int)z;
            float fx = x - x0;
            float fy = y - y0;
            float fz = z - z0;
            int stepX = x0 < size.x - 1 ? 1 : 0;
            int stepY = y0 < size.y - 1 ? size.x : 0;
            int stepZ = z0 < size.z - 1 ? size.x * size.y : 0;
            int a = (((z0 * size.y) + y0) * size.x) + x0;
            float3 c00 = lerp(GradeTexel(a), GradeTexel(a + stepX), fx);
            float3 c10 = lerp(GradeTexel(a + stepY), GradeTexel(a + stepY + stepX), fx);
            int b = a + stepZ;
            float3 c01 = lerp(GradeTexel(b), GradeTexel(b + stepX), fx);
            float3 c11 = lerp(GradeTexel(b + stepY), GradeTexel(b + stepY + stepX), fx);
            return lerp(lerp(c00, c10, fy), lerp(c01, c11, fy), fz);
        }

        // ColourGrade.Apply: the brightest channel above one taken out, looked up, and put back.
        float3 Graded3(float3 colour)
        {
            float most = max(max(colour.r, colour.g), max(colour.b, 1.0));
            float share = 1.0 / most;
            return GradeRead(Encoded(colour.r * share), Encoded(colour.g * share), Encoded(colour.b * share)) * most;
        }

        // SceneLight.Lit: one light's diffuse share, and its GGX lobe where the material is glossy.
        void Light(float3 light, float3 towards, float3 normal, float facing, bool glossy, float3 specular, float gloss, inout float3 diffuse, inout float3 shine)
        {
            float lit = dot(normal, towards);
            if (lit <= 0.0)
            {
                return;
            }

            diffuse += light * min(lit, 1.0);
            if (glossy)
            {
                float3 halfway = normalize(EyeWay + towards);
                float lobe = GlossLobe(normal, facing, towards, halfway, gloss);
                float fresnel = GlossFresnel(saturate(dot(EyeWay, halfway)));
                shine += light * lobe * (fresnel + (specular * (1.0 - fresnel)));
            }
        }

        // SceneLight.Glinted: one light's GGX lobe on the water's waves.
        void Glinted(float3 light, float3 towards, float3 waves, float facing, float3 specular, float gloss, inout float3 shine)
        {
            if (dot(waves, towards) <= 0.0)
            {
                return;
            }

            float3 halfway = normalize(EyeWay + towards);
            float lobe = GlossLobe(waves, facing, towards, halfway, gloss);
            float fresnel = GlossFresnel(saturate(dot(EyeWay, halfway)));
            shine += light * lobe * (fresnel + (specular * (1.0 - fresnel)));
        }

        // SceneLight.Reflected: the environment's light from one way, as the surround is lit.
        float3 Reflected(float3 direction, float3 total)
        {
            if (Ambient.x == 2)
            {
                return (MirrorRead(mul(float4(direction, 0.0), CubeTurn).xyz) * (Surround.y + (total * Surround.z)) * (1.0 - Surround.w))
                    + (Surround.x * Surround.w);
            }

            return Ambient.x == 1 ? Surround.xxx : float3(0.0, 0.0, 0.0);
        }

        // SceneLight.Channelled: whether the view shows a channel in place of the light.
        bool Channelled()
        {
            return Graded.w > 0 && Graded.w != {{(int)PictureView.LightingOnly}};
        }

        // SceneLight.Channel: a channel as the engine's debug views write it - linear, neither exposed nor graded.
        float3 Channel(float3 albedo, float3 normal, float3 specular, float gloss)
        {
            switch (Graded.w)
            {
                case {{(int)PictureView.Albedo}}: return albedo;
                case {{(int)PictureView.Specular}}: return specular;
                case {{(int)PictureView.Gloss}}: return float3(1.0, 1.0, 1.0) * pow(saturate(gloss), 2.2);
                case {{(int)PictureView.Roughness}}: return float3(1.0, 1.0, 1.0) * pow(1.0 - saturate(gloss), 2.2);
                case {{(int)PictureView.NormalFit}}: return pow(saturate((normal + 1.0) * 0.5), 2.2);
                case {{(int)PictureView.NormalTrue}}: return pow(saturate(normal), 2.2);
                default: return float3(0.0, 0.0, 0.0);
            }
        }

        // SceneLight.Shade: every light, the surround, a glossy material's environment, the waves' own specular, the exposure, the grade.
        float3 Lighted(float3 albedo, float3 place, float3 normal, float sun, bool glossy, float3 specular, float gloss, float3 waves, float environment)
        {
            if (Channelled())
            {
                return Channel(albedo, normal, specular, gloss);
            }

            if (Graded.w == {{(int)PictureView.LightingOnly}})
            {
                albedo = float3(1.0, 1.0, 1.0) * {{SceneLight.LightingGrey.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}};
            }

            float3 diffuse = float3(0.0, 0.0, 0.0);
            float3 shine = float3(0.0, 0.0, 0.0);
            float3 total = float3(0.0, 0.0, 0.0);
            float facing = max(dot(normal, EyeWay), 1e-4);
            bool waved = any(waves != 0.0);
            bool glints = waved && glossy && Finish.y > 0.5;
            float wavesFacing = waved ? max(dot(waves, EyeWay), 1e-4) : facing;
            bool lobes = glossy && !waved;
            if (SunColour.w > 0.5 && sun > 0.0)
            {
                float3 light = SunColour.rgb * sun;
                Light(light, -SunTravels.xyz, normal, facing, lobes, specular, gloss, diffuse, shine);
                if (glints)
                {
                    Glinted(light, -SunTravels.xyz, waves, wavesFacing, specular, gloss, shine);
                }

                total += light;
            }

            if (Ambient.w > 0)
            {
                float3 at = (place - CellLeast.xyz) / CellLeast.w;
                if (at.x >= 0.0 && at.y >= 0.0 && at.z >= 0.0)
                {
                    int3 cell = int3(at);
                    if (cell.x < Cells.x && cell.y < Cells.y && cell.z < Cells.z)
                    {
                        int index = (((cell.z * Cells.y) + cell.y) * Cells.x) + cell.x;
                        int upto = (int)Reach.Load(index + 1);
                        for (int entry = (int)Reach.Load(index); entry < upto; entry++)
                        {
                            int one = (int)Reach.Load(Cells.w + entry) * 3;
                            float4 where = Points.Load(one);
                            float4 tint = Points.Load(one + 1);
                            float zero = Points.Load(one + 2).x;
                            float3 offset = where.xyz - place;
                            float squared = dot(offset, offset);
                            if (squared >= where.w * where.w)
                            {
                                continue;
                            }

                            float distance = sqrt(squared);
                            float ratio = distance / where.w;
                            float fade = max(0.0, 1.0 - (ratio * ratio * ratio * ratio));
                            float divisor = (distance / tint.w) + 1.0;
                            float attenuation = min(10.0, zero * fade * fade / (divisor * divisor));
                            if (!(attenuation > 0.0))
                            {
                                continue;
                            }

                            float3 light = tint.rgb * attenuation;
                            float3 towards = distance > 1e-4 ? offset / distance : normal;
                            Light(light, towards, normal, facing, lobes, specular, gloss, diffuse, shine);
                            if (glints)
                            {
                                Glinted(light, towards, waves, wavesFacing, specular, gloss, shine);
                            }

                            total += light;
                        }
                    }
                }
            }

            float3 colour = diffuse * albedo;
            float3 surround = float3(0.0, 0.0, 0.0);
            if (Ambient.x == 2)
            {
                surround = (CubeRead(mul(float4(normal, 0.0), CubeTurn).xyz) * (Surround.y + (total * Surround.z)) * (1.0 - Surround.w))
                    + (Surround.x * Surround.w);
            }
            else if (Ambient.x == 1)
            {
                surround = Surround.xxx;
            }

            colour += surround * albedo;
            if (lobes)
            {
                float bias;
                float scale;
                GlossEnvironment(facing, gloss, bias, scale);
                float level = (surround.x + surround.y + surround.z) / 3.0;
                shine += (level * bias).xxx + (level * scale * specular);
            }

            if (waved && glossy && Finish.z > 0.5)
            {
                float bias;
                float scale;
                GlossEnvironment(wavesFacing, gloss, bias, scale);
                float3 reflected = (2.0 * dot(waves, EyeWay) * waves) - EyeWay;
                float3 seen = Reflected(reflected, total) * environment;
                shine += (seen * bias) + (seen * scale * specular);
            }

            float3 exposed = (colour + shine) * Finish.x;
            return Ambient.y != 0 ? Graded3(exposed) : exposed;
        }

        // MeshPicture.Drawing.Scened's normal: the model's own, two-sided, turned to the eye.
        float3 Toward(float3 turn)
        {
            float3 normal = dot(turn, turn) > 1e-12 ? normalize(turn) : EyeWay;
            return dot(normal, EyeWay) < 0.0 ? -normal : normal;
        }
        """;

    /// <summary>The entry points every picture is drawn with: the plain shapes, the sun's shadow map.</summary>
    public const string Fixed = """
        // MeshPicture.Drawing.Lit without a program: two-sided, the ambient plus the rest times the lamp's cosine.
        float4 Solid(Pixel pixel) : SV_Target
        {
            float3 colour = Ink.rgb;
            if (Flags.x > 0.5)
            {
                float4 texel = Skin.Sample(Wrap, pixel.spot);
                if (texel.a < Flags.y)
                {
                    discard;
                }

                colour = texel.rgb;
            }

            float3 normal;
            float shade = LampShade(pixel.facing, normal);
            return float4(colour * shade, 1.0);
        }

        // Unlit, as MeshPicture lays translucent shapes: an effect's light is its own.
        float4 Plain(Pixel pixel)
        {
            return Flags.x > 0.5 ? Skin.Sample(Wrap, pixel.spot) : float4(Ink.rgb, 0.5);
        }

        float4 Mixed(Pixel pixel) : SV_Target
        {
            float4 colour = Plain(pixel);
            float alpha = saturate(colour.a);
            return float4(colour.rgb * alpha, alpha);
        }

        float4 Added(Pixel pixel) : SV_Target
        {
            float4 colour = Plain(pixel);
            float3 added = colour.rgb * saturate(colour.a);
            return float4(added, max(added.r, max(added.g, added.b)));
        }

        // MeshPicture.Drawing.Scened without a program: two-sided, the normal in model space, the colour as light and back.
        float4 Scened(Pixel pixel) : SV_Target
        {
            SeeFrom(pixel.place);
            float3 colour = Ink.rgb;
            if (Flags.x > 0.5)
            {
                float4 texel = Skin.Sample(Wrap, pixel.spot);
                if (texel.a < Flags.y)
                {
                    discard;
                }

                colour = texel.rgb;
            }

            float3 normal = Toward(pixel.turn);
            float sun = SunLit(pixel.place, normal);
            return float4(Srgb(Lighted(Linear(colour), pixel.place, normal, sun, false, float3(0.0, 0.0, 0.0), 0.0, float3(0.0, 0.0, 0.0), 1.0)), 1.0);
        }

        // THE SUN'S SHADOW MAP - ShadowMap.Build: the mesh drawn along the light onto the map's texels,
        // each keeping its nearest depth by the blend's minimum.
        struct Cast
        {
            float4 position : SV_Position;
            float2 spot : TEXCOORD0;
            float away : TEXCOORD1;
        };

        Cast Casting(Corner corner)
        {
            // THE NORMAL SKINNED FOR NOTHING is left out by the compiler: the map reads the place alone.
            Skinned(corner.vertex, corner.position, corner.normal);
            corner.position = mul(float4(corner.position, 1.0), Sway).xyz;
            Cast cast;
            float x = (dot(corner.position, ShadowU.xyz) - ShadowU.w) * ShadowSize.x;
            float y = (dot(corner.position, ShadowV.xyz) - ShadowV.w) * ShadowSize.x;
            cast.position = float4(((2.0 * x) / ShadowSize.z) - 1.0, 1.0 - ((2.0 * y) / ShadowSize.z), 0.5, 1.0);
            cast.spot = corner.spot;
            cast.away = dot(corner.position, ShadowW.xyz) - ShadowW.w;
            return cast;
        }

        float Away(Cast cast) : SV_Target
        {
            if (Flags.x > 0.5 && Skin.Sample(Wrap, cast.spot).a < Flags.y)
            {
                discard;
            }

            return max(0.0, cast.away);
        }
        """;

    /// <summary>
    /// The water's vertex shader: MeshPicture.Lifted, vertex for vertex - the area's wind in Wind, the lift in Depth.w.
    /// </summary>
    public const string Lifting = """
        // MeshPicture.Lifted: the water's vertices lifted by its waves - ShadeProgram.Ocean at the
        // vertex's x and y in the area's wind, times the lift, taken off z (up is minus z) - and by the
        // field waves' crest at the engine's own height, the area's height offset in Flow.z with it, where they are drawn (Swell.w,
        // the card's alone).
        Pixel PlacedWaves(Corner corner)
        {
            float2 at = corner.position.xy;
            corner.position.z -= ShadeWaves(at, Wind, float2(Clock.x, 1.0e9), Shore, Swell).w * Depth.w;
            corner.position.z += ShadeFieldLift(at, Clock.x, Flow.z, Shore, Swell);
            return Placed(corner);
        }

        // MeshPicture.Beached: the beach's vertices moved by BreakingWavesVertex's own displacement - ShadeProgram.BeachLift at the
        // vertex's x and y, the area's height offset in Flow.z, added to z.
        Pixel PlacedBeach(Corner corner)
        {
            corner.position.z += ShadeBeachLift(corner.position.xy, Clock.x, Flow.z, Shore);
            return Placed(corner);
        }
        """;

    /// <summary>The fixed shaders' source: <see cref="Common"/>, the waves' and the beach's helpers (ShadeHlsl's) and <see cref="Fixed"/> with <see cref="Lifting"/>.</summary>
    public static readonly string Model = Common + ShadeHlsl.Library + ShadeHlsl.WavesLibrary + ShadeHlsl.FoamLibrary + ShadeHlsl.BeachLibrary + ShadeHlsl.FieldLibrary + Fixed + Lifting;

    /// <summary>
    /// A program's entry points: MeshPicture.Drawing's solid and mixed passes running it, under the picture's lamp or the game's light.
    /// </summary>
    /// <remarks>
    /// THE SOLID PASS cuts a cut-out shape on the alpha its graphs leave where they set one and on its
    /// texture's where they do not, and drops what a graph discarded; THE MIXED PASS covers by the
    /// program's alpha where its graphs set one and by the texture's or a half where they do not, and
    /// measures a ground layer against the solid depth behind it - ModelTarget's copy of the depth the
    /// solid pass left, since a translucent shape writes none.
    /// </remarks>
    public static readonly string Programmed = $$"""
        // THE DEPTH IS REVERSED (ModelGpu's _writes): nought where nothing is, the near plane's over the view's z in perspective, a
        // half less the view's z over twice the reach on a parallel view.
        float BehindAt(float4 position)
        {
            float held = Behind.Load(int3(position.xy, 0));
            return held <= 0.0 ? 3.40282347e38 : Depth.z > 0.5 ? Depth.y / held : (0.5 - held) * 2.0 * Depth.x;
        }

        ShadeOut Ran(Pixel pixel, bool translucent)
        {
            float behind = translucent && ShadeUsesDepth ? BehindAt(pixel.position) : 3.40282347e38;
            ShadePixel = pixel.position.xy;
            return ShadeRun(Wrap, pixel.spot, pixel.place, pixel.turn, pixel.tint, behind, Clock.x, Eye, Dust.xyz, Water, Flow, Wind, Shore, Swell, Viewer);
        }

        // MeshPicture.Drawing.Lit with a program.
        float3 LampLit(float3 colour, float3 specular, float gloss, float3 facing)
        {
            float3 normal;
            float shade = LampShade(facing, normal);
            if (ShadeHasSpecular && !ShadeHasWaves)
            {
                if (ShadeHasGloss)
                {
                    return LampGlossed(colour, shade, normal, specular, gloss);
                }

                colour = Flattened(colour, specular);
            }

            return colour * shade;
        }

        static const int ShadeBayer[16] = { {{string.Join(", ", ScreenReflection.Bayer)}} };

        // ScreenReflection.March, and MarchSeen in perspective: the ray over the solid depth - the step it hit at, or below nought. In
        // perspective the depths compared are minus one over the view's z, the engine's nonlinear depth's order and spacing.
        int Marched(int x, int y, float away, float2 across, float deeper, int size, float thickness, bool perspective, out int2 hit)
        {
            hit = int2(-1, -1);
            float span = sqrt(dot(across, across));
            if (!(span > 1e-6))
            {
                return -1;
            }

            float unit = {{ShadeHlsl.Number(ScreenReflection.StepShare)}} * size / span;
            float jitter = ShadeBayer[((y & 3) * 4) + (x & 3)] / 16.0;
            [loop] for (int step = 0; step < {{ScreenReflection.Steps}}; step++)
            {
                float along = (step + jitter) * unit;
                float px = x + 0.5 + (across.x * along);
                float py = y + 0.5 + (across.y * along);
                if (!(px >= 0.0 && py >= 0.0 && px < size && py < size))
                {
                    break;
                }

                int cx = (int)px;
                int cy = (int)py;
                float ray = away + (deeper * along);
                float solid = BehindAt(float4(cx, cy, 0.0, 1.0));
                if (perspective)
                {
                    solid = -1.0 / solid;
                }

                if (solid < ray && solid > ray - thickness)
                {
                    hit = int2(cx, cy);
                    return step;
                }
            }

            return -1;
        }

        // MeshPicture.Drawing.Mirrored: the picture's solid part as the waves reflect it, and the environment's share of the rest.
        float3 Mirrored(float3 waves, float3 normal, float4 position, float3 place, float3 specular, float gloss, out float environment)
        {
            environment = 1.0;
            float3 reflected = (2.0 * dot(waves, EyeWay) * waves) - EyeWay;
            reflected += normal * max(0.0, -dot(reflected, normal));
            uint width;
            uint height;
            Behind.GetDimensions(width, height);
            int size = (int)width;
            int2 hit;
            int step;
            float start = ShadeDeep(Eye, float4(place, 0.0));
            if (Depth.z > 0.5)
            {
                // IN PERSPECTIVE THE ENGINE'S OWN MARCH (ScreenReflection.MarchSeen): the start and a point along the ray projected, a unit
                // along or nearer where the ray runs at the eye, the depths' reciprocals in step between them.
                float towards = dot(reflected, Eye.xyz);
                float along = towards < 0.0 ? min(1.0, start * 0.5 / -towards) : 1.0;
                float4 from = mul(float4(place, 1.0), Clip);
                float4 to = mul(float4(place + (reflected * along), 1.0), Clip);
                float2 across = ((to.xy / to.w) - (from.xy / from.w)) * float2(0.5, -0.5) * size;
                float first = -1.0 / from.w;
                step = Marched(
                    (int)position.x, (int)position.y, first, across, (-1.0 / to.w) - first, size,
                    (1.0 / from.w) - (1.0 / (from.w + {{ShadeHlsl.Number(ScreenReflection.Thickness)}})), true, hit);
            }
            else
            {
                float4 onScreen = mul(float4(reflected, 0.0), Clip);
                step = Marched(
                    (int)position.x, (int)position.y, start,
                    float2(onScreen.x, -onScreen.y) * (size * 0.5), dot(reflected, Eye.xyz), size, {{ShadeHlsl.Number(ScreenReflection.Thickness)}}, false, hit);
            }
            if (step < 0)
            {
                return float3(0.0, 0.0, 0.0);
            }

            float far = (step + 0.5) / {{ScreenReflection.Steps}}.0;
            environment = far * far * far * far * far;
            float3 seen = Linear(Opaque.Load(int3(hit, 0)).rgb);
            float bias;
            float scale;
            GlossEnvironment(max(dot(waves, EyeWay), 1e-4), gloss, bias, scale);
            return ((seen * bias) + (seen * scale * specular)) * (1.0 - environment);
        }

        // water.ffx's GetScreenPointFromView: where a point lands on the picture, nought to one across and down.
        float2 ScreenOf(float3 place)
        {
            float4 clip = mul(float4(place, 1.0), Clip);
            float2 seen = clip.xy / clip.w;
            return float2((seen.x * 0.5) + 0.5, 0.5 - (seen.y * 0.5));
        }

        // The texel a point on the picture falls in, held at the picture's edges - the engine's point-clamped reads.
        int3 TexelOf(float2 screen, int size)
        {
            return int3(clamp((int2)floor(screen * size), 0, size - 1), 0);
        }

        // water.ffx's GetViewPoint over the solid depth (ReadNearestNonlinearDepth): the solid surface at a point on the picture, model
        // space - Clip undone at the point and the depth held under it. False where nothing solid lies there.
        bool SolidAt(float2 screen, int size, out float3 solid)
        {
            float held = Behind.Load(TexelOf(screen, size));
            float4 back = mul(float4((screen.x * 2.0) - 1.0, 1.0 - (screen.y * 2.0), held, 1.0), Unclip);
            solid = back.w != 0.0 ? back.xyz / back.w : float3(0.0, 0.0, 0.0);
            return held > 0.0;
        }

        // water.ffx's GetRefractedDir from the air (n1 one) into the water: the ray bent at the surface, or kept where it would reflect whole.
        float3 RefractedWay(float3 ray, float3 normal, float index)
        {
            float r = 1.0 / max(1.0, index);
            float c = -dot(ray, normal);
            float under = 1.0 - (r * r * (1.0 - (c * c)));
            return under > 0.0 ? (r * ray) + (((r * c) - sqrt(under)) * normal) : ray;
        }

        // water.ffx's GetRaytracedLightAdaptiveStepping: a point along the bent ray put on the picture and the solid surface there taken where it
        // lies ahead along the ray, the next point as far along the ray as that surface; where it lies behind, the step halved back. The solid
        // point last taken - the start where none was - or false where a step met nothing solid, which no light comes back from.
        bool RefractedHit(float3 origin, float3 ray, int size, out float3 hit)
        {
            float3 current = origin;
            float3 next = origin;
            hit = origin;
            [unroll] for (int step = 0; step < {{SceneLight.RefractionSteps}}; step++)
            {
                float3 solid;
                if (!SolidAt(ScreenOf(next), size, solid))
                {
                    return false;
                }

                if (dot(solid - origin, ray) < 0.0)
                {
                    next = (current + next) * 0.5;
                }
                else
                {
                    hit = solid;
                    current = next;
                    float3 gone = solid - origin;
                    next = origin + (sqrt(dot(gone, gone)) * ray);
                }
            }

            return true;
        }

        // One of ComputeDispersedColor's shifted reads: the picture's solid part there, or the unshifted colour where the surface there lies
        // behind where the ray started - nothing solid lying ahead, as the engine's far depth does.
        float3 DispersedRead(float2 screen, float3 start, float3 ray, int size, float3 unshifted)
        {
            float3 solid;
            bool found = SolidAt(screen, size, solid);
            return !found || dot(solid - start, ray) > 0.0 ? Opaque.Load(TexelOf(screen, size)).rgb : unshifted;
        }

        // water.ffx's ComputeDispersedColor at GetRefractedOpaqueSurface's dispersion: blue read where the bent ray ended, green one shift and
        // red two towards where it started, a shift that share of the way and no more than a set share of the picture.
        float3 Dispersed(float3 start, float3 end, float3 ray, int size)
        {
            float2 ends = ScreenOf(end);
            float2 shift = (ScreenOf(start) - ends) * {{ShadeHlsl.Number(SceneLight.WaterDispersion)}};
            float span = sqrt(dot(shift, shift));
            shift *= min(span, {{ShadeHlsl.Number(SceneLight.WaterDispersionMost)}}) / (span + 1e-7);
            float3 unshifted = Opaque.Load(TexelOf(ends, size)).rgb;
            float red = DispersedRead(ends + (shift * 2.0), start, ray, size, unshifted).r;
            float green = DispersedRead(ends + shift, start, ray, size, unshifted).g;
            return float3(red, green, unshifted.b);
        }

        // SceneLight.WaterCaustics: water.ffx's GetCausticsBias and GetCausticsIntensity over the light where the bent ray ended, the ground
        // water ratio nought as it is everywhere here - the deep water's frequency and intensity - and the strength WaterLit.w, the area's
        // caustics_mult less the shore's ratio. The light is the picture's sRGB, so it is multiplied as light and put back.
        float3 Caustics(float3 seen, float3 origin, float3 end, float terrain)
        {
            float strength = WaterLit.w * (1.0 - terrain);
            if (!(strength != 0.0))
            {
                return seen;
            }

            float2 drifting = (end.xy * 1e-3) + (float2(1.0, 0.3) * Clock.x * 0.05);
            float2 distortion = ((WrappedRead(DistortionSheet, drifting).rg * 2.0) - 1.0) * 0.4;
            float pattern = pow(saturate(WrappedRead(CausticsSheet, (end.xy * 2e-3) + distortion).g), 1.0 / 4.2);
            float intensity = saturate((abs(end.z - origin.z) * 0.02) - 0.3) * 0.7;
            float bias = ((pattern * 2.0) - 1.0) * intensity;
            return Srgb(Linear(seen) * (1.0 + (bias * strength)));
        }

        // SceneLight.WaterRefraction, the card's alone: GetRefractedOpaqueSurface - the view bent into the water by the area's index thinned
        // to one by the shore's ratio, marched over the solid depth, the solid part read where it ended with the colours split and patterned
        // by the caustics - and GetRefractedWaterLight's share of it that gets through, over the bent ray's own length.
        float Refraction(float3 place, float3 normal, float terrain, out float3 seen)
        {
            uint width;
            uint height;
            Behind.GetDimensions(width, height);
            int size = (int)width;
            float3 bent = RefractedWay(-EyeWay, normal, lerp(WaterLit.y, 1.0, terrain));
            float3 hit;
            if (!RefractedHit(place, bent, size, hit))
            {
                seen = float3(0.0, 0.0, 0.0);
                return 0.0;
            }

            seen = Caustics(Dispersed(place, hit, bent, size), place, hit, terrain);
            float3 travel = hit - place;
            return exp(-sqrt(dot(travel, travel)) * Water.w * (1.0 - terrain));
        }

        // SceneLight.ShadeWater's normal: the waves', turned to face the eye.
        float3 WaterNormal(ShadeOut said)
        {
            float3 normal = dot(said.waves, said.waves) > 1e-12 ? normalize(said.waves) : EyeWay;
            return dot(normal, EyeWay) < 0.0 ? -normal : normal;
        }

        // SceneLight.WaterLit: one light on the water - its share on the foam and on the medium, ComputeDiffuse's wrap.
        void WaterLight(float3 light, float3 towards, float3 normal, inout float3 diffuse, inout float3 through)
        {
            float lit = dot(normal, towards);
            diffuse += light * saturate(lit);
            through += light * saturate((lit + {{ShadeHlsl.Number(SceneLight.WaterTranslucency)}}) / (1.0 + {{ShadeHlsl.Number(SceneLight.WaterTranslucency)}}));
        }

        // SceneLight.WaterSky: the specular cube along a way times the cube's brightness, none of it the GI's.
        float3 WaterSky(float3 direction, float3 total)
        {
            if (Ambient.x == 2)
            {
                return MirrorRead(mul(float4(direction, 0.0), CubeTurn).xyz) * (Surround.y + (total * Surround.z));
            }

            return Ambient.x == 1 ? Surround.xxx : float3(0.0, 0.0, 0.0);
        }

        // SceneLight.ShadeWater: one pixel of open water lit as the engine's water pass lights it - the layer's colour, linear,
        // exposed and graded, and its cover in w; a cover of nought drops the pixel. What gets through the medium is handed in: the
        // program's, over the straight view, or the refraction's, over the bent ray.
        float4 WaterShaded(ShadeOut said, float3 place, float sun, float environment, float3 mirrored, float passing)
        {
            float terrain = saturate(said.waterState.y);
            if (terrain > 0.999999)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float3 normal = WaterNormal(said);
            float share = saturate(said.waterFoam.w);
            float exponent = 1.0 / (1.0 + max(-0.9, 250.0 * said.waterState.w * 5e-5 * WaterLit.x));
            float3 scatter = float3({{ShadeHlsl.Number(SceneLight.WaterScatterColour.X)}}, {{ShadeHlsl.Number(SceneLight.WaterScatterColour.Y)}}, {{ShadeHlsl.Number(SceneLight.WaterScatterColour.Z)}});
            float3 subsurface = pow(max(Water.xyz * said.waterState.z, 0.0) / scatter, exponent) * scatter * (1.0 - share);
            float3 albedo = max(said.waterFoam.xyz, 0.0);
            float3 specular = said.specular;
            float gloss = said.gloss;
            float facing = max(dot(normal, EyeWay), 1e-4);
            bool glints = Finish.y > 0.5;
            float3 diffuse = float3(0.0, 0.0, 0.0);
            float3 through = float3(0.0, 0.0, 0.0);
            float3 shine = float3(0.0, 0.0, 0.0);
            float3 total = float3(0.0, 0.0, 0.0);
            if (SunColour.w > 0.5 && sun > 0.0)
            {
                float3 light = SunColour.rgb * sun;
                WaterLight(light, -SunTravels.xyz, normal, diffuse, through);
                if (glints)
                {
                    Glinted(light, -SunTravels.xyz, normal, facing, specular, gloss, shine);
                }

                total += light;
            }

            if (Ambient.w > 0)
            {
                float3 at = (place - CellLeast.xyz) / CellLeast.w;
                if (at.x >= 0.0 && at.y >= 0.0 && at.z >= 0.0)
                {
                    int3 cell = int3(at);
                    if (cell.x < Cells.x && cell.y < Cells.y && cell.z < Cells.z)
                    {
                        int index = (((cell.z * Cells.y) + cell.y) * Cells.x) + cell.x;
                        int upto = (int)Reach.Load(index + 1);
                        for (int entry = (int)Reach.Load(index); entry < upto; entry++)
                        {
                            int one = (int)Reach.Load(Cells.w + entry) * 3;
                            float4 where = Points.Load(one);
                            float4 tint = Points.Load(one + 1);
                            float zero = Points.Load(one + 2).x;
                            float3 offset = where.xyz - place;
                            float squared = dot(offset, offset);
                            if (squared >= where.w * where.w)
                            {
                                continue;
                            }

                            float distance = sqrt(squared);
                            float ratio = distance / where.w;
                            float fade = max(0.0, 1.0 - (ratio * ratio * ratio * ratio));
                            float divisor = (distance / tint.w) + 1.0;
                            float attenuation = min(10.0, zero * fade * fade / (divisor * divisor));
                            if (!(attenuation > 0.0))
                            {
                                continue;
                            }

                            float3 light = tint.rgb * attenuation;
                            float3 towards = distance > 1e-4 ? offset / distance : normal;
                            WaterLight(light, towards, normal, diffuse, through);
                            if (glints)
                            {
                                Glinted(light, towards, normal, facing, specular, gloss, shine);
                            }

                            total += light;
                        }
                    }
                }
            }

            // THE AMBIENT on the foam and the medium alike, the GI's stand-in on the foam alone.
            float3 ambient = float3(0.0, 0.0, 0.0);
            if (Ambient.x == 2)
            {
                ambient = (CubeRead(mul(float4(normal, 0.0), CubeTurn).xyz) * (Surround.y + (total * Surround.z)) * (1.0 - Surround.w) * (albedo + subsurface))
                    + (albedo * (Surround.x * Surround.w));
            }
            else if (Ambient.x == 1)
            {
                ambient = (albedo + subsurface) * Surround.x;
            }

            if (Finish.z > 0.5)
            {
                float bias;
                float scale;
                GlossEnvironment(facing, gloss, bias, scale);
                float3 reflected = (2.0 * dot(normal, EyeWay) * normal) - EyeWay;
                float3 seen = WaterSky(reflected, total) * environment;
                shine += (seen * bias) + (seen * scale * specular);
            }

            float brightest = max(shine.x, max(shine.y, shine.z));
            if (brightest > {{ShadeHlsl.Number(SceneLight.WaterSpecularCap)}})
            {
                shine *= {{ShadeHlsl.Number(SceneLight.WaterSpecularCap)}} / brightest;
            }

            float fresnel = GlossFresnel(saturate(dot(normal, EyeWay)));
            float reflectivity = (fresnel + (specular.x * (1.0 - fresnel))) * (1.0 - terrain);
            float refracted = (1.0 - reflectivity) + (reflectivity * terrain);
            float kept = 1.0 - (terrain * terrain * terrain);
            float3 absorbed = (diffuse * albedo) + ambient + (through * subsurface);
            float clear = 1.0 - share;
            float hidden = 1.0 - (clear * clear * clear);
            float mixed = ((1.0 - hidden) * refracted * (1.0 - passing)) + hidden;
            float cover = 1.0 - ((1.0 - hidden) * refracted * passing);
            if (!(cover > 1e-6))
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float3 layer = ((shine * kept) + (absorbed * mixed)) * (Finish.x / cover);
            float3 graded = Ambient.y != 0 ? Graded3(layer) : layer;
            return float4(graded + (mirrored * (kept / cover)), cover);
        }

        // MeshPicture.Drawing.WaterScened: the water's layer under the game's light, sRGB, its cover in w. A mixed layer that refracts
        // (WaterLit.z, the solid part copied for it) covers whole, what lies behind bent into its colour - mixed in by the share the layer
        // would have let through, in the picture's own numbers, as the blend mixes it.
        float4 WaterSceneLit(ShadeOut said, float3 place, float3 turn, float4 position, bool mixed)
        {
            float3 normal = Toward(turn);
            float sun = SunLit(place, normal);
            float environment = 1.0;
            float3 mirrored = float3(0.0, 0.0, 0.0);
            if (ShadeHasSpecular && ShadeHasGloss && Finish.z > 0.5 && ToEye.w > 0.5)
            {
                mirrored = Mirrored(said.waves, normal, position, place, said.specular, said.gloss, environment);
            }

            bool refracts = mixed && WaterLit.z > 0.5;
            float passing = saturate(said.waterState.x);
            float3 seen = float3(0.0, 0.0, 0.0);
            if (refracts)
            {
                passing = Refraction(place, WaterNormal(said), saturate(said.waterState.y), seen);
            }

            float4 lit = WaterShaded(said, place, sun, environment, mirrored, passing);
            float3 shown = Srgb(lit.rgb);
            return refracts && lit.w > 0.0 ? float4((shown * lit.w) + (seen * (1.0 - lit.w)), 1.0) : float4(shown, lit.w);
        }

        // MeshPicture.Drawing.WaterScened in a channel view: the water's layer left out.
        float4 WaterSceneLitOrNot(ShadeOut said, float3 place, float3 turn, float4 position)
        {
            return Channelled() ? float4(0.0, 0.0, 0.0, 0.0) : WaterSceneLit(said, place, turn, position, true);
        }

        // MeshPicture.Drawing.Scened with a program.
        float3 SceneLit(float3 colour, float3 specular, float gloss, float3 place, float3 turn, float3 waves, float4 position)
        {
            // THE BEACH'S OWN SURFACE FOR ALL ITS LIGHT - MeshPicture.Drawing.Scened's.
            float3 normal = Toward(ShadeBeach ? waves : turn);
            float3 albedo = Linear(colour);
            if (ShadeHasSpecular && !ShadeHasGloss)
            {
                albedo += max(specular - 0.04, 0.0);
            }

            float sun = SunLit(place, normal);
            bool glossy = ShadeHasSpecular && ShadeHasGloss;
            float3 wavy = ShadeHasWaves ? waves : float3(0.0, 0.0, 0.0);
            float environment = 1.0;
            float3 mirrored = float3(0.0, 0.0, 0.0);
            if (ShadeHasWaves && glossy && Finish.z > 0.5 && ToEye.w > 0.5 && !Channelled())
            {
                mirrored = Mirrored(wavy, normal, position, place, specular, gloss, environment);
            }

            // THE PICTURE'S OWN LIGHT IS ADDED AS IT WAS DRAWN, exposed and graded already.
            return Srgb(Lighted(albedo, place, normal, sun, glossy, specular, gloss, wavy, environment) + mirrored);
        }

        float4 Solidly(Pixel pixel, bool scened)
        {
            SeeFrom(pixel.place);
            bool cut = Flags.y >= 0.0;
            float edge = Flags.x > 0.5 ? Skin.Sample(Wrap, pixel.spot).a : 1.0;
            if (cut && !ShadeHasAlpha && edge < Flags.y)
            {
                discard;
            }

            ShadeOut said = Ran(pixel, false);
            if ((cut && ShadeHasAlpha && said.alpha < Flags.y) || said.dropped)
            {
                discard;
            }

            // THE WATER LIT AS THE ENGINE'S WATER PASS LIGHTS IT, whole where it is drawn solid; in a channel view its program's channels.
            if (ShadeWater && scened && !Channelled())
            {
                return float4(WaterSceneLit(said, pixel.place, pixel.turn, pixel.position, false).rgb, 1.0);
            }

            float3 shown = scened
                ? SceneLit(said.colour, said.specular, said.gloss, pixel.place, pixel.turn, said.waves, pixel.position)
                : LampLit(said.colour, said.specular, said.gloss, pixel.facing);
            return float4(shown, 1.0);
        }

        float4 Mixing(Pixel pixel, bool scened)
        {
            SeeFrom(pixel.place);
            float skinned = Flags.x > 0.5 ? Skin.Sample(Wrap, pixel.spot).a : 0.5;
            ShadeOut said = Ran(pixel, true);

            // THE WATER'S COVER IS ITS LIGHT'S (SceneLight.ShadeWater): what lies behind seen through the medium and the surface.
            if (ShadeWater && scened)
            {
                float4 water = said.dropped ? float4(0.0, 0.0, 0.0, 0.0) : WaterSceneLitOrNot(said, pixel.place, pixel.turn, pixel.position);
                if (!(water.w > 0.0))
                {
                    discard;
                }

                float covered = saturate(water.w);
                return float4(water.rgb * covered, covered);
            }

            float cover = ShadeHasAlpha ? said.alpha : skinned;
            if (said.dropped || !(cover > 0.0))
            {
                discard;
            }

            float3 shown = scened
                ? SceneLit(said.colour, said.specular, said.gloss, pixel.place, pixel.turn, said.waves, pixel.position)
                : LampLit(said.colour, said.specular, said.gloss, pixel.facing);
            float alpha = saturate(cover);
            return float4(shown * alpha, alpha);
        }

        float4 ProgramSolid(Pixel pixel) : SV_Target
        {
            return Solidly(pixel, false);
        }

        float4 ProgramSolidScened(Pixel pixel) : SV_Target
        {
            return Solidly(pixel, true);
        }

        float4 ProgramMixed(Pixel pixel) : SV_Target
        {
            return Mixing(pixel, false);
        }

        float4 ProgramMixedScened(Pixel pixel) : SV_Target
        {
            return Mixing(pixel, true);
        }

        // A CASTER'S WETNESS - ShadeProgram.Wetness, the colour's alpha its wetness - into the wetness pass's target (ModelGpu's Wet).
        float4 ProgramWet(Pixel pixel) : SV_Target
        {
            ShadeOut said = Ran(pixel, true);
            if (said.dropped)
            {
                discard;
            }

            return float4(saturate(said.alpha), 0.0, 0.0, 0.0);
        }
        """;

    /// <summary>
    /// The water's wetness caster - <see cref="Common"/>, ShadeHlsl's library and <c>WaterWet</c> - drawn over the water's own vertex shaders.
    /// </summary>
    /// <remarks>
    /// WaterComputeWetness AS WRITTEN (water.ffx's BuildWaterWetness, the caster's alpha under the blend mode Wetness): one less the
    /// cube of <c>saturate(1 - gap * 0.03)</c>, the gap the view crosses from the water to what lies behind it - ShadeProgram's
    /// DepthDistance, along the pixel's own ray in perspective - so a surface under the water by a thirtieth is a little wet and one
    /// 33 under it wet through, and one standing out of the water none. The tidal state's own ratio to the terrain, which the
    /// engine takes the larger of, is not drawn here as the water's light does not draw it (SceneLight.ShadeWater). The engine's caster
    /// (water_default_wetness.fxgraph, read in v0.1.336's capture) is this fragment's opacity as the alpha over the mesh's own place,
    /// so it is drawn over the still water's vertex shader alone, never the lifted one (ModelGpu's Wet). That graph names its blend
    /// mode BackgroundGBufferSurfaces where the river's (water_flowing_wetness.fxgraph) names Wetness - what the engine makes of that
    /// is in no file read.
    /// </remarks>
    public static readonly string Wetting = Common + ShadeHlsl.Library + """
        float4 WaterWet(Pixel pixel) : SV_Target
        {
            float held = Behind.Load(int3(pixel.position.xy, 0));
            if (!(held > 0.0))
            {
                discard;
            }

            float behind = Depth.z > 0.5 ? Depth.y / held : (0.5 - held) * 2.0 * Depth.x;
            float4 at = float4(pixel.place, 1.0);
            float gap = behind - ShadeDeep(Eye, at);
            if (Viewer.w > 0.5)
            {
                gap *= length(ShadeRaying(Eye, Viewer, behind, at));
            }

            float ratio = saturate(1.0 - (max(gap, 0.0) * 0.03));
            return float4(1.0 - (ratio * ratio * ratio), 0.0, 0.0, 0.0);
        }
        """;

    /// <summary>
    /// The wetness pass's drying: last frame's wetness, less what dries in the time since, into this frame's target - or nought where
    /// the picture starts again (ModelGpu's Wet).
    /// </summary>
    public const string Drying = """
        cbuffer Dry : register(b4)
        {
            float4 Dry;  // x: how much the wetness falls this frame; y: one where it starts again from nought
        };

        Texture2D<float> Held : register(t0);

        float4 Whole(uint corner : SV_VertexID) : SV_Position
        {
            float2 at = float2((corner << 1) & 2, corner & 2);
            return float4((at * float2(2.0, -2.0)) + float2(-1.0, 1.0), 0.0, 1.0);
        }

        float4 Dried(float4 position : SV_Position) : SV_Target
        {
            float left = Dry.y > 0.5 ? 0.0 : max(Held.Load(int3(position.xy, 0)) - Dry.x, 0.0);
            return float4(left, 0.0, 0.0, 0.0);
        }
        """;

    /// <summary>
    /// A program's shader source: <see cref="Common"/>, the program, and <see cref="Programmed"/> - a screen program's with <see cref="Air"/> before
    /// it, whose light it calls, and <see cref="Screened"/> after.
    /// </summary>
    public static string Program(ShadeProgram program)
        => program.Screen
            ? Common + Air + ShadeHlsl.Of(program, FirstSheet) + Programmed + Screened
            : Common + ShadeHlsl.Of(program, FirstSheet) + Programmed;

    /// <summary>
    /// A screen program's light (ShadeProgram.CompileScreen, the screen-space fog): the omni light its layers take - <c>ShadeOmni</c>, which the
    /// program's steps call - over the pass's values in Common's cbuffer Air.
    /// </summary>
    /// <remarks>
    /// GetIntegralOmniLight AS lighting.ffx WRITES IT, over this tool's own lights: the sun through IntegrateShadowMap's seven samples along the
    /// layer and the clouds' shadow at its first point (Clouded), every point light at the layer's first point as Lighted finds them in the
    /// light grid (ComputePointLightParams, unshadowed: the engine's shadowed dynamic lights have no map here), the environment's light times
    /// the ambient's alpha, and the ambient's colour. The layer's points are the area's (the fog's pattern is laid in the world, as the game
    /// lays it); the sun's map and the lights are in the picture's own frame, so the frame's place in the area is taken off before they are read.
    /// </remarks>
    public static readonly string Air = """
        // IntegrateShadowMap's one sample of the sun's map at a point of the air: ShadowMap.Lit's four texels compared and lerped, as SunReaches
        // reads them for a surface, with nothing to lift it off and the map's own bias kept - the point moved across the map by an offset.
        float SunAt(float3 place, float2 offset)
        {
            float texel = ShadowSize.y;
            float x = ((dot(place, ShadowU.xyz) - ShadowU.w + offset.x) * ShadowSize.x) - 0.5;
            float y = ((dot(place, ShadowV.xyz) - ShadowV.w + offset.y) * ShadowSize.x) - 0.5;
            float depth = dot(place, ShadowW.xyz) - ShadowW.w - (texel * 2.0);
            float last = ShadowSize.z - 1.0;
            if (x < 0.0 || y < 0.0 || x >= last || y >= last)
            {
                return 1.0;
            }

            int x0 = (int)x;
            int y0 = (int)y;
            float fx = x - x0;
            float fy = y - y0;
            float top = lerp(Open(x0, y0, depth), Open(x0 + 1, y0, depth), fx);
            float bottom = lerp(Open(x0, y0 + 1, depth), Open(x0 + 1, y0 + 1, depth), fx);
            return lerp(top, bottom, fy);
        }

        // IntegrateShadowMap for the directional light (shadows.ffx): seven samples from the layer's first point towards its last, the i-th at
        // (i + hash) / 7 of the way, each moved by its point of the hexagon times shadow_scale.xy - CHOSEN a texel of the sun's map, the engine's
        // scale being read nowhere - and averaged; ShadeOmni lays the clouds' factor over it at the first point, as IntegrateShadowMap does.
        float SunAlong(float3 near, float3 far, float hash)
        {
            static const float2 offsets[7] =
            {
                float2(0.000, 0.000),
                float2(1.000, 0.000),
                float2(0.500, 0.866),
                float2(-0.500, 0.866),
                float2(-1.000, 0.000),
                float2(-0.500, -0.866),
                float2(0.500, -0.866)
            };

            float light = 0.0;
            [unroll] for (int i = 0; i < 7; i++)
            {
                light += SunAt(lerp(near, far, (float(i) + hash) / 7.0), offsets[i] * ShadowSize.y);
            }

            return light / 7.0;
        }

        // GetIntegralOmniLight - see ModelShaders.Air. The points come in the area's frame.
        float3 ShadeOmni(float3 nearAt, float3 farAt, float hash, float4 ambient)
        {
            float3 shift = float3(AirOrigin.xy, 0.0);
            float3 near = nearAt - shift;
            float3 far = farAt - shift;
            float3 total = float3(0.0, 0.0, 0.0);
            if (SunColour.w > 0.5)
            {
                total += SunColour.rgb * (SunTravels.w > 0.5 ? SunAlong(near, far, hash) : 1.0) * Clouded(near);
            }

            if (Ambient.w > 0)
            {
                float3 at = (near - CellLeast.xyz) / CellLeast.w;
                if (at.x >= 0.0 && at.y >= 0.0 && at.z >= 0.0)
                {
                    int3 cell = int3(at);
                    if (cell.x < Cells.x && cell.y < Cells.y && cell.z < Cells.z)
                    {
                        int index = (((cell.z * Cells.y) + cell.y) * Cells.x) + cell.x;
                        int upto = (int)Reach.Load(index + 1);
                        for (int entry = (int)Reach.Load(index); entry < upto; entry++)
                        {
                            int one = (int)Reach.Load(Cells.w + entry) * 3;
                            float4 where = Points.Load(one);
                            float4 tint = Points.Load(one + 1);
                            float zero = Points.Load(one + 2).x;
                            float3 offset = where.xyz - near;
                            float squared = dot(offset, offset);
                            if (squared >= where.w * where.w)
                            {
                                continue;
                            }

                            float distance = sqrt(squared);
                            float ratio = distance / where.w;
                            float fade = max(0.0, 1.0 - (ratio * ratio * ratio * ratio));
                            float divisor = (distance / tint.w) + 1.0;
                            float attenuation = min(10.0, zero * fade * fade / (divisor * divisor));
                            if (attenuation > 0.0)
                            {
                                total += tint.rgb * attenuation;
                            }
                        }
                    }
                }
            }

            return total + (AirLight.rgb * ambient.a) + ambient.rgb;
        }
        """;

    /// <summary>
    /// A screen program drawn over the whole picture (ModelGpu's fog pass): the pass's values set for <c>ShadeRun</c>, its light laid over the
    /// picture by its cover.
    /// </summary>
    /// <remarks>
    /// THE SOLID DEPTH BEHIND THE PIXEL put back into the picture's frame through Clip's inverse - the engine's UnprojectPixelDepth, as the
    /// water's GetViewPoint is (SolidAt) - and nothing drawn where nothing solid lies behind: CHOSEN, the fog lying over the scene and not the
    /// empty ground round a stage, which the game never shows. CameraPos is the eye in perspective; on a parallel view, which the engine never
    /// draws, a point far back along the view from each pixel's surface (CHOSEN), so the layers' rays run as the view does. Its light exposed
    /// and graded as a mixed layer's is and laid over the picture as one is (CHOSEN: the engine's lies under the grade, in its own smaller
    /// target - DownScaledAlphaBlend).
    /// </remarks>
    public const string Screened = """
        float4 ProgramScreen(float4 position : SV_Position) : SV_Target
        {
            float held = Behind.Load(int3(position.xy, 0));
            if (!(held > 0.0))
            {
                discard;
            }

            uint width;
            uint height;
            Behind.GetDimensions(width, height);
            float4 back = mul(float4(((position.x / width) * 2.0) - 1.0, 1.0 - ((position.y / height) * 2.0), held, 1.0), Unclip);
            float3 surface = back.w != 0.0 ? back.xyz / back.w : float3(0.0, 0.0, 0.0);
            float3 camera = Viewer.w > 0.5 ? Viewer.xyz : surface - (Eye.xyz * 100000.0);
            float3 shift = float3(AirOrigin.xy, 0.0);
            ShadeScreenCamera = camera + shift;
            ShadeScreenSurface = surface + shift;
            ShadeScreenPixel = position.xy;
            ShadeScreenFog = AirFog;
            ShadeScreenShape = AirShape.xy;
            ShadeScreenPlayer = AirPlayer.xyz;
            ShadeOut said = ShadeRun(Wrap, float2(0.0, 0.0), surface, float3(0.0, 0.0, -1.0), float4(1.0, 1.0, 1.0, 1.0), held, Clock.x, Eye, Dust.xyz, Water, Flow, Wind, Shore, Swell, Viewer);
            float cover = saturate(said.alpha);
            if (!(cover > 0.0))
            {
                discard;
            }

            float3 exposed = said.colour * Finish.x;
            float3 shown = Srgb(Ambient.y != 0 ? Graded3(exposed) : exposed);
            return float4(shown * cover, cover);
        }
        """;

    /// <summary>The pass that turns the premultiplied float target into the straight bytes ImGui shows.</summary>
    /// <remarks>
    /// TRUNCATED TO A BYTE, NOT ROUNDED, because MeshPicture's Byte truncates - and a value k/255 is
    /// written back as exactly k by the target's conversion, so the two pictures meet on the byte.
    /// </remarks>
    public const string Resolve = """
        Texture2D Accumulated : register(t0);

        float4 Whole(uint corner : SV_VertexID) : SV_Position
        {
            float2 at = float2((corner << 1) & 2, corner & 2);
            return float4((at * float2(2.0, -2.0)) + float2(-1.0, 1.0), 0.0, 1.0);
        }

        float4 Straight(float4 position : SV_Position) : SV_Target
        {
            float4 held = Accumulated.Load(int3(position.xy, 0));
            float alpha = min(held.a, 1.0);
            if (!(alpha > 0.0))
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float4 straight = float4(saturate(held.rgb / alpha), alpha);
            return floor(straight * 255.0) / 255.0;
        }
        """;
}
