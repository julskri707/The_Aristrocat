#ifndef WALL_CLADDING_GPU_LIBRARY_INCLUDED
#define WALL_CLADDING_GPU_LIBRARY_INCLUDED

// Set every frame from C# (WallCladdingGpuGlobals.PushGeneratorParams + PushOncePerFrame).
// Use in Shader Graph: Custom Function → reference this file → function names below (…_float).

float4 _WallCladdingCameraWorld;
float _WallCladdingTime;

float _WallCladdingLodNear;
float _WallCladdingLodFar;
float _WallCladdingMaxRenderDistance;
float _WallCladdingHorizonStart;
float _WallCladdingHysteresis;

/// Distance from world position to the active camera point pushed by the game.
void WallCladdingDistanceToCamera_float(float3 WorldPosition, out float Distance)
{
    Distance = distance(WorldPosition, _WallCladdingCameraWorld.xyz);
}

/// 0 = “near” band, 1 = “far” band — blend detail maps or normals in Shader Graph.
void WallCladdingLodBlend_float(float3 WorldPosition, out float LodAlpha)
{
    float d = distance(WorldPosition, _WallCladdingCameraWorld.xyz);
    float denom = max(_WallCladdingLodFar - _WallCladdingLodNear, 1e-4);
    LodAlpha = saturate((d - _WallCladdingLodNear) / denom);
}

/// Optional: soft factor beyond max render distance (for alpha / clip in graph).
void WallCladdingHorizonAttenuation_float(float3 WorldPosition, out float Attenuation)
{
    float d = distance(WorldPosition, _WallCladdingCameraWorld.xyz);
    float inner = max(0.0, _WallCladdingMaxRenderDistance - _WallCladdingHysteresis);
    float outer = max(inner + 1e-3, _WallCladdingMaxRenderDistance);
    Attenuation = saturate((outer - d) / max(outer - inner, 1e-4));
}

#endif
