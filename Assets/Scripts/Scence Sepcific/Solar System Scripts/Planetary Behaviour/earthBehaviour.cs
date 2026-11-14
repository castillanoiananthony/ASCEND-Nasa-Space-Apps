using UnityEngine;

public class earthBehaviour : MonoBehaviour
{
    [Header("Simulation Scale & Speed")]
    [Tooltip("1 Unity Unit = 10,000 km")]
    public float scaleFactor = 10000f;

    [Tooltip("How much faster than real life time runs. 1 = real time (one spin takes 24 hours). " +
             "60 = one spin every 24 minutes. 600 = one spin every 2.4 minutes.")]
    [Range(1f, 3600f)]
    public float simulationSpeed = 60f;

    [Header("Earth Real Data")]
    public float realDiameterKm = 12742f;
    public float realOrbitDistanceKm = 149600000f;
    public float orbitalInclinationDegrees = 0.00f;
    public float axialTiltDegrees = 23.44f;
    public float orbitalPeriodDays = 365.25f;
    [Tooltip("One full spin in Earth days (a sidereal day is 0.9973 days)")]
    public float rotationPeriodDays = 0.99727f;

    const float SecondsPerDay = 86400f;

    private Transform orbitPivot;
    private Transform tiltHolder;

    void Start()
    {
        InitializeEarthHierarchy();
    }

    void Update()
    {
        AnimateOrbitAndRotation();
    }

    void InitializeEarthHierarchy()
    {
        float scaledDiameter = realDiameterKm / scaleFactor;       // 1.2742 units
        float scaledDistance = realOrbitDistanceKm / scaleFactor;   // 14960 units

        // Orbit Pivot at world origin (0,0,0)
        GameObject pivotGO = new GameObject("Earth_OrbitPivot");
        pivotGO.transform.position = Vector3.zero;
        pivotGO.transform.rotation = Quaternion.Euler(0f, 0f, orbitalInclinationDegrees);
        orbitPivot = pivotGO.transform;

        // Tilt Holder child positioned at orbital distance with 23.44° axial tilt
        GameObject tiltGO = new GameObject("Earth_TiltHolder");
        tiltGO.transform.SetParent(orbitPivot, false);
        tiltGO.transform.localPosition = new Vector3(0f, 0f, scaledDistance);
        tiltGO.transform.localRotation = Quaternion.Euler(0f, 0f, axialTiltDegrees);
        tiltHolder = tiltGO.transform;

        // Attach mesh to Tilt Holder and scale
        transform.SetParent(tiltHolder, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * scaledDiameter;
    }

    void AnimateOrbitAndRotation()
    {
        // Real time, sped up by simulationSpeed (the old version ran a whole day every second)
        float daysPassed = Time.deltaTime * simulationSpeed / SecondsPerDay;

        if (orbitPivot != null)
        {
            float orbitDegrees = (360f / orbitalPeriodDays) * daysPassed;
            orbitPivot.Rotate(Vector3.up, orbitDegrees, Space.Self);
        }

        float rotationDegrees = (360f / rotationPeriodDays) * daysPassed;
        transform.Rotate(Vector3.up, rotationDegrees, Space.Self);
    }
}
