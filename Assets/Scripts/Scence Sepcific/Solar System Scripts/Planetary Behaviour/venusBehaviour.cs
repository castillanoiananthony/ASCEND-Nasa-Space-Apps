using UnityEngine;

public class venusBehaviour : MonoBehaviour
{
    [Header("Simulation Scale & Speed")]
    [Tooltip("1 Unity Unit = 10,000 km")]
    public float scaleFactor = 10000f;

    [Tooltip("How many Earth Days pass per real-world second in Unity")]
    public float timeScaleInDaysPerSecond = 1.0f;

    [Header("Venus Real Data")]
    public float realDiameterKm = 12104f;
    public float realOrbitDistanceKm = 108200000f;
    public float orbitalInclinationDegrees = 3.39f;
    public float axialTiltDegrees = 177.36f; // Retrograde tilt
    public float orbitalPeriodDays = 224.7f;
    public float rotationPeriodDays = 243.0f;

    private Transform orbitPivot;
    private Transform tiltHolder;

    void Start()
    {
        InitializeVenusHierarchy();
    }

    void Update()
    {
        AnimateOrbitAndRotation();
    }

    void InitializeVenusHierarchy()
    {
        float scaledDiameter = realDiameterKm / scaleFactor;       // 1.2104 units
        float scaledDistance = realOrbitDistanceKm / scaleFactor;   // 10820 units

        // Orbit Pivot at world origin (0,0,0) tilted by orbital inclination
        GameObject pivotGO = new GameObject("Venus_OrbitPivot");
        pivotGO.transform.position = Vector3.zero;
        pivotGO.transform.rotation = Quaternion.Euler(0f, 0f, orbitalInclinationDegrees);
        orbitPivot = pivotGO.transform;

        // Tilt Holder child positioned at orbital distance with axial tilt
        GameObject tiltGO = new GameObject("Venus_TiltHolder");
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
