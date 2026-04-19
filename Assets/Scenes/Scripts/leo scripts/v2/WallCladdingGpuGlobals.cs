using UnityEngine;

/// <summary>
/// Globals for HDRP Shader Graph (Custom Function nodes) + any hand-written shaders.
/// All math using these runs on the GPU once the shader compiles — C# only assigns scalars/vectors per frame.
/// </summary>
public static class WallCladdingGpuGlobals
{
    public static readonly int CameraWorld = Shader.PropertyToID("_WallCladdingCameraWorld");
    public static readonly int TimeSeconds = Shader.PropertyToID("_WallCladdingTime");

    public static readonly int LodNear = Shader.PropertyToID("_WallCladdingLodNear");
    public static readonly int LodFar = Shader.PropertyToID("_WallCladdingLodFar");
    public static readonly int MaxRenderDistance = Shader.PropertyToID("_WallCladdingMaxRenderDistance");
    public static readonly int HorizonStart = Shader.PropertyToID("_WallCladdingHorizonStart");
    public static readonly int Hysteresis = Shader.PropertyToID("_WallCladdingHysteresis");

    static int s_LastFrame = -1;
    static Camera s_Cam;

    /// <summary>Camera position + time — once per frame.</summary>
    public static void PushOncePerFrame()
    {
        int f = Time.frameCount;
        if (f == s_LastFrame)
            return;
        s_LastFrame = f;

        if (s_Cam == null || !s_Cam.isActiveAndEnabled)
            s_Cam = Camera.main;
        if (s_Cam == null)
            return;

        Vector3 p = s_Cam.transform.position;
        Shader.SetGlobalVector(CameraWorld, new Vector4(p.x, p.y, p.z, 1f));
        Shader.SetGlobalFloat(TimeSeconds, Time.time);
    }

    /// <summary>
    /// Distance bands for Shader Graph LOD blending / fades. Safe to call every LateUpdate;
    /// if several <see cref="WallCladdingGenerator"/> exist, the last one updated each frame wins.
    /// </summary>
    public static void PushGeneratorParams(
        float lodNear,
        float lodFar,
        float maxRenderDistance,
        float horizonStart,
        float hysteresis)
    {
        Shader.SetGlobalFloat(LodNear, lodNear);
        Shader.SetGlobalFloat(LodFar, lodFar);
        Shader.SetGlobalFloat(MaxRenderDistance, maxRenderDistance);
        Shader.SetGlobalFloat(HorizonStart, horizonStart);
        Shader.SetGlobalFloat(Hysteresis, hysteresis);
    }
}
