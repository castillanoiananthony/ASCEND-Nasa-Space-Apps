using UnityEngine;

public class marsBehaviour : MonoBehaviour
{
    [Header("Simulation Scale & Speed")]
    [Tooltip("1 Unity Unit = 10,000 km")]
    public float scaleFactor = 10000f;

    [Tooltip("How many Earth Days pass per real-world second in Unity")]
    public float timeScaleInDaysPerSecond = 1.0f;

    [Header("Mars Real Data")]
    public float realDiameterKm = 6779f;
    public float realOrbitDistanceKm = 227900000f;
    public float orbitalInclinationDegrees = 1.85f;
    public float axialTiltDegrees = 25.19f;
    public float orbitalPeriodDays = 686.98f;
    public float rotationPeriodDays = 1.03f;

    private Transform orbitPivot;
    private Transform tiltHolder;

    void Start()
    {
        InitializeMarsHierarchy();
    }

    void Update()
    {
        AnimateOrbitAndRotation();
    }

    void InitializeMarsHierarchy()
    {
        float scaledDiameter = realDiameterKm / scaleFactor;       // 0.6779 units
        float scaledDistance = realOrbitDistanceKm / scaleFactor;   // 22790 units

        // Orbit Pivot at world origin (0,0,0) tilted by orbital inclination
        GameObject pivotGO = new GameObject("Mars_OrbitPivot");
        pivotGO.transform.position = Vector3.zero;
        pivotGO.transform.rotation = Quaternion.Euler(0f, 0f, orbitalInclinationDegrees);
        orbitPivot = pivotGO.transform;

        // Tilt Holder child positioned at orbital distance with 25.19° axial tilt
        GameObject tiltGO = new GameObject("Mars_TiltHolder");
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
        float daysPassed = Time.deltaTime * timeScaleInDaysPerSecond;

        if (orbitPivot != null)
        {
            float orbitDegrees = (360f / orbitalPeriodDays) * daysPassed;
            orbitPivot.Rotate(Vector3.up, orbitDegrees, Space.Self);
        }

        float rotationDegrees = (360f / rotationPeriodDays) * daysPassed;
        transform.Rotate(Vector3.up, rotationDegrees, Space.Self);
    }
}
