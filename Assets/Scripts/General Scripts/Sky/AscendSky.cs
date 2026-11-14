// ═════════════════════════════════════════════════════════════════════════════
//  AscendSky.cs  -  blue sky, light clouds and the Sun as the main light.
//
//  Put this on ONE GameObject in the scene and drag in:
//    - Sun:          your Directional Light (it becomes the scene's sun and main light)
//    - Sky Material: AscendSky.mat (shader "Ascend/Sky")
//  The sun disk in the sky always sits where the Sun light points from, so the
//  shadows and the sky match. Rotate the Sun light to change the time of day.
//
//  During a launch, AscendFlight calls AscendSky.SetAltitude(meters):
//  the clouds fade out as you climb through them and the sky turns black in space.
// ═════════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class AscendSky : MonoBehaviour
{
    public static AscendSky Active { get; private set; }

    [Header("Drag these in")]
    [Tooltip("The Directional Light used as the Sun (the main light of the scene).")]
    public Light sun;
    [Tooltip("Material using the 'Ascend/Sky' shader.")]
    public Material skyMaterial;

    [Header("Sun light")]
    public Color sunColor = new Color(1f, 0.96f, 0.88f);
    [Tooltip("Sun brightness on the ground.")]
    public float sunIntensity = 1.45f;
    [Tooltip("Sunlight in space is a bit stronger (no air in the way).")]
    public float spaceSunBoost = 1.25f;

    [Header("Ambient (light from the sky itself)")]
    public Color ambientSky = new Color(0.62f, 0.72f, 0.86f);
    public Color ambientHorizon = new Color(0.62f, 0.66f, 0.66f);
    public Color ambientGround = new Color(0.4f, 0.4f, 0.34f);
    [Tooltip("Ambient light in space (almost black: only the Sun lights things).")]
    public Color ambientSpace = new Color(0.03f, 0.035f, 0.045f);

    [Header("Clouds")]
    [Range(0f, 1f)] public float cloudCoverage = 0.42f;
    [Tooltip("Height (m) where you start flying into the clouds: they begin to fade from the sky.")]
    public float cloudBase = 1800f;
    [Tooltip("Height (m) above which the clouds are fully below you.")]
    public float cloudTop = 2700f;

    [Header("Haze (distant hills fade into the sky, like real air)")]
    public bool haze = true;
    [Tooltip("How thick the haze is. 0.0006 = hills 1 km away are about 30% hazy.")]
    public float hazeDensity = 0.0006f;
    [Tooltip("The haze thins out between these heights (m) so the ground stays clear from high up.")]
    public float hazeFadeFrom = 400f, hazeFadeTo = 4000f;

    [Header("Space")]
    [Tooltip("How fast the sky turns black with height (m). Half dark at about 0.7 x this.")]
    public float atmosphereScale = 14000f;

    [Header("Preview")]
    [Tooltip("Test the sky at a height (m) in the editor. Ignored while a launch sets it.")]
    public float previewAltitude = 0f;

    float altitude = -1f;            // < 0 = use previewAltitude
    Material runtimeMat;

    public static void SetAltitude(float meters) { if (Active) Active.altitude = Mathf.Max(0f, meters); }
    public static void ResetAltitude() { if (Active) Active.altitude = -1f; }

    void OnEnable()
    {
        Active = this;
        if (!sun) sun = RenderSettings.sun;
        if (!sun)
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) { sun = l; break; }
        Apply();
    }

    void OnDisable()
    {
        if (Active == this) Active = null;
        if (runtimeMat) { if (Application.isPlaying) Destroy(runtimeMat); else DestroyImmediate(runtimeMat); }
    }

    void LateUpdate() => Apply();
    void OnValidate() => Apply();

    Material Mat()
    {
        if (!skyMaterial) return null;
        if (!Application.isPlaying) return skyMaterial;      // edit the asset only in the editor (so the scene saves it)
        if (!runtimeMat) runtimeMat = new Material(skyMaterial) { name = skyMaterial.name + " (runtime)" };
        return runtimeMat;                                    // play mode: a copy, so the asset never changes
    }

    void Apply()
    {
        var m = Mat();
        if (!m) return;
        if (RenderSettings.skybox != m) RenderSettings.skybox = m;
        if (sun && RenderSettings.sun != sun) RenderSettings.sun = sun;

        float alt = altitude >= 0f ? altitude : Mathf.Max(0f, previewAltitude);
        float space = 1f - Mathf.Exp(-alt / Mathf.Max(1f, atmosphereScale));
        float clouds = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cloudBase, cloudTop, alt));

        Vector3 sunDir = sun ? -sun.transform.forward : new Vector3(0.3f, 0.6f, 0.5f).normalized;
        m.SetVector("_SunDir", sunDir);
        m.SetFloat("_Space", space);
        m.SetFloat("_CloudFade", clouds);
        m.SetFloat("_CloudCoverage", cloudCoverage);
        m.SetColor("_SunColor", sunColor);

        // The Sun: main light with shadows, dims when it sets
        float day = Mathf.Clamp01(sunDir.y * 4f + 0.25f);
        if (sun)
        {
            sun.color = sunColor;
            sun.intensity = sunIntensity * Mathf.Lerp(1f, spaceSunBoost, space) * Mathf.Lerp(0.05f, 1f, day);
            if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
        }

        // Haze in the horizon color
        RenderSettings.fog = haze;
        if (haze)
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = hazeDensity * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hazeFadeFrom, hazeFadeTo, alt)));
            Color hz = m.HasProperty("_HorizonColor") ? m.GetColor("_HorizonColor") : new Color(0.64f, 0.8f, 0.96f);
            RenderSettings.fogColor = hz * Mathf.Lerp(0.1f, 1f, day);
        }

        // Sky light: soft blue from above, fades to almost nothing in space
        RenderSettings.ambientMode = AmbientMode.Trilight;
        float amb = Mathf.Lerp(0.15f, 1f, day);
        RenderSettings.ambientSkyColor = Color.Lerp(ambientSky * amb, ambientSpace, space);
        RenderSettings.ambientEquatorColor = Color.Lerp(ambientHorizon * amb, ambientSpace, space);
        RenderSettings.ambientGroundColor = Color.Lerp(ambientGround * amb, ambientSpace * 0.5f, space);
    }
}
