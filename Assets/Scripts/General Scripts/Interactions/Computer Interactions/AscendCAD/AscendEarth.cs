// ═════════════════════════════════════════════════════════════════════════════
//  AscendEarth.cs  -  the HD Earth used while the satellite is in orbit.
//  Made at runtime far away from the Universe scene, on its own layer, so only the
//  orbit cameras see it.  1 unit = 1 km.  Materials: Resources/AscendEarth + AscendAtmosphere.
//  Knows latitude / longitude, so photo targets (Sahara, Greenland, ...) are real places.
// ═════════════════════════════════════════════════════════════════════════════
using UnityEngine;

public class AscendEarth : MonoBehaviour
{
    public const int Layer = 30;
    public const float RadiusKm = 6371f;
    public static readonly Vector3 Center = new Vector3(0f, -60000f, 0f);   // far below the Universe scene

    public Material surface, atmosphere;
    float cloudShift;

    public static AscendEarth Create()
    {
        var go = new GameObject("Earth (HD)");
        go.layer = Layer;
        go.transform.position = Center;
        var e = go.AddComponent<AscendEarth>();
        go.AddComponent<MeshFilter>().sharedMesh = SphereMesh(RadiusKm, 360, 180);
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        var m = Resources.Load<Material>("AscendEarth");
        e.surface = m ? new Material(m) : new Material(Shader.Find("Ascend/Earth"));
        mr.sharedMaterial = e.surface;

        var a = new GameObject("Atmosphere");
        a.layer = Layer;
        a.transform.SetParent(go.transform, false);
        a.AddComponent<MeshFilter>().sharedMesh = SphereMesh(RadiusKm * 1.022f, 128, 64);
        var amr = a.AddComponent<MeshRenderer>();
        amr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; amr.receiveShadows = false;
        var am = Resources.Load<Material>("AscendAtmosphere");
        e.atmosphere = am ? new Material(am) : new Material(Shader.Find("Ascend/Atmosphere"));
        amr.sharedMaterial = e.atmosphere;

        // Keep every other camera from seeing it
        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include)) c.cullingMask &= ~(1 << Layer);
        return e;
    }

    public void SetSun(Vector3 sunDir)
    {
        if (surface) surface.SetVector("_SunDir", sunDir);
        if (atmosphere) atmosphere.SetVector("_SunDir", sunDir);
    }

    /// <summary>Spin eastward (degrees). Clouds drift a little on their own.</summary>
    public void Spin(float degrees, float dt)
    {
        transform.Rotate(0f, -degrees, 0f, Space.Self);
        cloudShift += dt * 0.0004f;
        if (surface) surface.SetFloat("_CloudShift", cloudShift);
    }

    // ── latitude / longitude ──
    public static Vector3 LocalDir(float latDeg, float lonDeg)
    {
        float la = latDeg * Mathf.Deg2Rad, lo = lonDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(la) * Mathf.Cos(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Sin(lo));
    }

    public Vector3 WorldDir(float lat, float lon) => transform.TransformDirection(LocalDir(lat, lon));
    public Vector3 WorldPoint(float lat, float lon) => transform.position + WorldDir(lat, lon) * RadiusKm;

    public void LatLonOf(Vector3 worldDir, out float lat, out float lon)
    {
        Vector3 d = transform.InverseTransformDirection(worldDir).normalized;
        lat = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
        lon = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
    }

    // Equirectangular UV: u = 0 at 180°W, 0.5 at Greenwich, east to the right; v = 0 at the South Pole
    static Mesh SphereMesh(float r, int N, int M)
    {
        var v = new Vector3[(N + 1) * (M + 1)]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        for (int j = 0; j <= M; j++)
        for (int i = 0; i <= N; i++)
        {
            float lat = Mathf.Lerp(-90f, 90f, j / (float)M), lon = Mathf.Lerp(-180f, 180f, i / (float)N);
            Vector3 d = LocalDir(lat, lon);
            int k = j * (N + 1) + i;
            v[k] = d * r; n[k] = d; uv[k] = new Vector2(i / (float)N, j / (float)M);
        }
        var tri = new int[N * M * 6]; int t = 0;
        for (int j = 0; j < M; j++)
        for (int i = 0; i < N; i++)
        {
            int p = j * (N + 1) + i, q = p + 1, s = p + N + 1, w = s + 1;
            tri[t++] = p; tri[t++] = s; tri[t++] = q; tri[t++] = q; tri[t++] = s; tri[t++] = w;
        }
        var m = new Mesh { name = "HD Earth", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.vertices = v; m.normals = n; m.uv = uv; m.triangles = tri;
        // make sure the faces point outward
        var tr = m.triangles; int mid = (M / 2) * N * 6;
        Vector3 c = Vector3.Cross(v[tr[mid + 1]] - v[tr[mid]], v[tr[mid + 2]] - v[tr[mid]]);
        if (Vector3.Dot(c, v[tr[mid]]) < 0f) { for (int k = 0; k < tr.Length; k += 3) { int x = tr[k + 1]; tr[k + 1] = tr[k + 2]; tr[k + 2] = x; } m.triangles = tr; }
        m.RecalculateBounds();
        return m;
    }

    // ── photo targets ──
    public struct Target { public string name, blurb; public float lat, lon, radiusKm; public Target(string n, string b, float la, float lo, float r) { name = n; blurb = b; lat = la; lon = lo; radiusKm = r; } }

    public static readonly Target[] Targets =
    {
        new Target("The Philippines", "Our home: 7,641 islands", 12.5f, 122.5f, 650f),
        new Target("Sahara Desert", "The biggest hot desert on Earth", 23f, 12f, 1500f),
        new Target("Greenland", "The biggest island, covered in ice", 72f, -40f, 900f),
        new Target("Amazon Rainforest", "The biggest rainforest on Earth", -4f, -62f, 1200f),
        new Target("Himalayas", "The tallest mountains, home of Mount Everest", 29f, 85f, 700f),
    };
}
