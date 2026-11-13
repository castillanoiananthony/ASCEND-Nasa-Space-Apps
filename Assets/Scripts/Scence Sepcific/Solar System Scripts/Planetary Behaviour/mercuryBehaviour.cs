using UnityEngine;

public class mercuryBehaviour : MonoBehaviour
{
    [Header("Simulation Scale & Speed")]
    [Tooltip("1 Unity Unit = 10,000 km")]
    public float scaleFactor = 10000f;

    [Tooltip("How many Earth Days pass per real-world second")]
    public float timeScaleInDaysPerSecond = 1.0f;

    [Header("Mercury Real Data")]
    public float realDiameterKm = 4879f;
    public float realOrbitDistanceKm = 57900000f;
    public float orbitalInclinationDegrees = 7.00f;
    public float axialTiltDegrees = 0.034f;
    public float orbitalPeriodDays = 87.97f;
    public float rotationPeriodDays = 58.65f;

    private Transform orbitPivot;
    private Transform tiltHolder;

    void Start()
    {
        InitializeMercuryHierarchy();
    }

    void Update()
    {
        AnimateOrbitAndRotation();
    }

    void InitializeMercuryHierarchy()
    {
        // 1. Calculate scaled dimensions
        float scaledDiameter = realDiameterKm / scaleFactor;       // 0.4879 units
        float scaledDistance = realOrbitDistanceKm / scaleFactor;   // 5790 units

        // 2. Create the Orbital Pivot at World Center (0,0,0) with 7.0° inclination
        GameObject pivotGO = new GameObject("Mercury_OrbitPivot");
        pivotGO.transform.position = Vector3.zero;
        pivotGO.transform.rotation = Quaternion.Euler(0f, 0f, orbitalInclinationDegrees);
        orbitPivot = pivotGO.transform;

        // 3. Create the Tilt Holder as a child of Pivot at scaled distance (5790 units)
        GameObject tiltGO = new GameObject("Mercury_TiltHolder");
        tiltGO.transform.SetParent(orbitPivot, false);
        tiltGO.transform.localPosition = new Vector3(0f, 0f, scaledDistance);
        tiltGO.transform.localRotation = Quaternion.Euler(0f, 0f, axialTiltDegrees);
        tiltHolder = tiltGO.transform;

        // 4. Attach this Mercury sphere to the Tilt Holder and apply physical scale
        transform.SetParent(tiltHolder, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * scaledDiameter;
    }

    void AnimateOrbitAndRotation()
    {
        float daysPassed = Time.deltaTime * timeScaleInDaysPerSecond;

        // Revolve around the Sun along inclination plane
        if (orbitPivot != null)
        {
            float orbitDegrees = (360f / orbitalPeriodDays) * daysPassed;
            orbitPivot.Rotate(Vector3.up, orbitDegrees, Space.Self);
        }

        // Self-rotation on tilted axis
        float rotationDegrees = (360f / rotationPeriodDays) * daysPassed;
        transform.Rotate(Vector3.up, rotationDegrees, Space.Self);
    }
}
