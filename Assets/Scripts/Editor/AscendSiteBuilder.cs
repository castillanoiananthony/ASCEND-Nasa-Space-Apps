// ═════════════════════════════════════════════════════════════════════════════
//  AscendSiteBuilder.cs  -  Menu: ASCEND > Site > Build Launch Area, Roads & People
//
//  1. Dirt patch around the launch site: radius = biggest launch pad (with the
//     biggest rocket it can hold, crawler parked) + 3 m, with an uneven natural edge.
//     The ground there is flattened a little so the pad sits level.
//  2. Ring road around the patch, and a road to the two buildings (it finds a gentle
//     way around the hills), splitting at a junction to each entrance.
//  3. Pavement: a plaza at each entrance, sidewalks along the branch roads and around
//     both buildings.
//  4. Trees along the road and around the buildings (terrain trees = cheap billboards).
//  5. Six people walking loops (AscendWalker).
//  Everything goes under one object "ASCEND Site (generated)". The terrain is saved
//  first: ASCEND > Site > Remove puts the ground back exactly as it was.
// ═════════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AscendSiteBuilder
{
    const string RootName = "ASCEND Site (generated)";
    const string GenDir = "Assets/Generated/Site";
    const string TexDir = "Assets/Imports/Environment/Road Textures/";
    const float RoadWidth = 8f, RingWidth = 8f, SidewalkWidth = 2.6f, CurbHeight = 0.15f;
    static string BackupDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "TerrainBackup");

    static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

    // ───────────────────────────── menu ─────────────────────────────
    [MenuItem("ASCEND/Site/Build Launch Area, Roads & People")]
    public static void Build()
    {
        log.Clear();
        var data = LoadData();
        if (!data) { Debug.LogError("AscendSiteBuilder: AscendCAD Data asset not found."); return; }
        RemoveInternal(false);                 // start clean (restores the ground if built before)
        SaveTerrains();

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build site");
        Directory.CreateDirectory(GenDir);

        Vector3 c = data.launchSitePosition;
        float padR = BiggestPadRadius(data);
        float R = padR + 3f;
        c.y = H(c.x, c.z);
        log.AppendLine($"Launch site {c}, biggest pad radius {padR:0.0} m -> dirt radius {R:0.0} m");

        // 1. dirt patch + level ground
        Flatten(c, R + 8f, R + 30f);
        PaintDirt(c, R);

        // 2. roads
        float ringR = R + 4f + RingWidth * 0.5f;
        var ring = new List<Vector3>();
        for (int i = 0; i < 120; i++) { float a = i / 120f * Mathf.PI * 2f; ring.Add(new Vector3(c.x + Mathf.Cos(a) * ringR, 0f, c.z + Mathf.Sin(a) * ringR)); }
        SetHeights(ring, 1, true);

        var doors = FindDoors();
        if (doors.Count < 2) { Debug.LogWarning("AscendSiteBuilder: couldn't find the two building doors ('Mission Control' and 'Office Door' under Exterior). Roads end at the buildings' center."); }
        Vector3 mid = doors.Count >= 2 ? (doors[0].pos + doors[1].pos) * 0.5f : new Vector3(780f, 0f, 700f);
        Vector3 toSite = c - mid; toSite.y = 0f; toSite.Normalize();
        Vector3 junction = mid + toSite * 70f;
        Vector3 ringExit = c + (junction - c).WithY(0f).normalized * ringR;

        var main = Route(ringExit, junction, c, R + 12f);
        SetHeights(main, 9, false);
        main[0] = main[0].WithY(H(ringExit.x, ringExit.z));

        var roads = new List<(List<Vector3> pts, float w, bool loop, float lift)> { (ring, RingWidth, true, 0.08f), (main, RoadWidth, false, 0.1f) };
        var plazas = new List<(Vector3 center, Vector3 fwd)>();
        foreach (var d in doors)
        {
            Vector3 front = d.pos + d.outward * 9f;
            var branch = Smooth(new List<Vector3> { junction, Vector3.Lerp(junction, front, 0.5f) + Vector3.Cross(Vector3.up, (front - junction).normalized) * 6f, front }, 3);
            branch = Resample(branch, 3f);
            SetHeights(branch, 5, false);
            roads.Add((branch, RoadWidth - 1.5f, false, 0.12f));
            plazas.Add((d.pos + d.outward * 5f, d.outward));
        }

        // carve the ground under every road so grass and bumps never poke through
        foreach (var r in roads) Carve(r.pts, r.w * 0.5f + 1f, r.w * 0.5f + 5f, r.loop);
        // re-read heights after carving
        foreach (var r in roads) SetHeights(r.pts, 1, r.loop, true);

        var asphalt = Mat("Road Asphalt", "asphalt_02", 1f, 0.15f);
        var paving = Mat("Pavement", "rectangular_paving", 1f, 0.1f);
        var line = LineMat();
        var roadRoot = Child(root, "Roads");
        int k = 0;
        foreach (var r in roads)
        {
            var g = Ribbon(roadRoot, "Road " + (++k), r.pts, r.w, r.lift, r.loop, asphalt, r.w, true);
            // center lines stop before the junctions (no lines crossing other roads)
            if (r.loop)
            {
                var open = OpenAround(r.pts, ringExit, RoadWidth * 1.4f);
                if (open.Count > 1) Ribbon(g.transform, "Center Line", open, 0.18f, r.lift + 0.012f, false, line, 6f, false);
            }
            else
            {
                var cl = Trim(r.pts, k == 2 ? RingWidth * 1.2f : RoadWidth * 1.4f, k == 2 ? RoadWidth * 1.4f : 4f);
                if (cl.Count > 1) Ribbon(g.transform, "Center Line", cl, 0.18f, r.lift + 0.012f, false, line, 6f, false);
            }
        }
        // clean, round asphalt patches where roads meet (instead of two strips overlapping)
        Disc(roadRoot, "Junction", junction, RoadWidth * 0.95f, 0.14f, asphalt);
        Disc(roadRoot, "Ring Junction", ringExit, RoadWidth * 0.95f, 0.13f, asphalt);

        // 3. pavement: plazas, sidewalks along branches, around both buildings
        var paveRoot = Child(root, "Pavement");
        for (int i = 0; i < plazas.Count; i++) Slab(paveRoot, "Entrance Plaza " + (i + 1), plazas[i].center, plazas[i].fwd, 16f, 10f, paving);
        for (int i = 2; i < roads.Count; i++)
        {
            float off = roads[i].w * 0.5f + SidewalkWidth * 0.5f + 0.2f;
            // start after the junction so sidewalks never cross the other road
            var trimmed = Trim(roads[i].pts, RoadWidth * 1.8f, 0f);
            if (trimmed.Count < 2) continue;
            Ribbon(paveRoot, "Sidewalk L " + (i - 1), Offset(trimmed, off), SidewalkWidth, CurbHeight, false, paving, 3f, true, true);
            Ribbon(paveRoot, "Sidewalk R " + (i - 1), Offset(trimmed, -off), SidewalkWidth, CurbHeight, false, paving, 3f, true, true);
        }
        var footprints = doors.Select(d => d.footprint).Where(b => b.size.sqrMagnitude > 1f).ToList();
        // A walkway along the front of each building (not all the way round: big models reach into the hills)
        var loops = new List<List<Vector3>>();
        foreach (var d in doors)
        {
            Vector3 along = Vector3.Cross(Vector3.up, d.outward);
            float faceHalf = Mathf.Abs(Vector3.Dot(along, d.footprint.extents.WithY(0f))) + 0f;
            float half = Mathf.Clamp(Mathf.Abs(along.x) > 0.5f ? d.footprint.extents.x : d.footprint.extents.z, 10f, 28f);
            Vector3 c0 = d.pos + d.outward * 3.5f;
            var walk = new List<Vector3> { c0 - along * half, c0 + along * half };
            walk = Resample(walk, 3f); SetHeights(walk, 3, false);
            Ribbon(paveRoot, "Front Walkway " + d.name, walk, 4f, CurbHeight, false, paving, 3f, true, true);
            // people walk a long oval in front of the entrance: along the walkway and back through the plaza
            Vector3 near = d.pos + d.outward * 2.6f, far = d.pos + d.outward * 7.5f;
            float h2 = half - 2f;
            var lp = new List<Vector3> { near - along * h2, near + along * h2, far + along * 6f, far - along * 6f };
            lp = Resample(new List<Vector3>(lp) { lp[0] }, 2f); lp.RemoveAt(lp.Count - 1);
            for (int i = 0; i < lp.Count; i++) lp[i] = lp[i].WithY(H(lp[i].x, lp[i].z) + CurbHeight);
            loops.Add(lp);
        }

        // 4. trees
        var avoid = new List<(List<Vector3> pts, float r)>();
        foreach (var r in roads) avoid.Add((r.pts, r.w * 0.5f + 4f));
        foreach (var lp in loops) avoid.Add((lp, 7f));
        int trees = PlantTrees(main, c, R + 12f, footprints, avoid);
        ClearGrass(roads, plazas, c, R + 2f);

        // 5. people
        var people = Child(root, "People");
        int n = 0;
        var rnd = new System.Random(7);
        foreach (var lp in loops)
        {
            for (int j = 0; j < 2; j++) AddWalker(people, ++n, lp, j == 0 ? 0f : 0.5f, j == 1, rnd);
        }
        if (roads.Count >= 4)   // walk between the two entrances along the sidewalks
        {
            var a = Offset(roads[2].pts, roads[2].w * 0.5f + 1.5f); var b2 = Offset(roads[3].pts, -(roads[3].w * 0.5f + 1.5f));
            var path = new List<Vector3>(); path.AddRange(Enumerable.Reverse(a)); path.AddRange(b2);
            var back = new List<Vector3>(path); back.Reverse();
            path.AddRange(back);
            AddWalker(people, ++n, path, 0f, false, rnd);
            AddWalker(people, ++n, path, 0.5f, false, rnd);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        log.AppendLine($"Roads: {roads.Count}, plazas: {plazas.Count}, building sidewalks: {footprints.Count}, trees: {trees}, people: {n}");
        Debug.Log("AscendSiteBuilder: done.\n" + log);
    }

    [MenuItem("ASCEND/Site/Remove (put the ground back)")]
    public static void Remove() { RemoveInternal(true); }

    static void RemoveInternal(bool logIt)
    {
        var old = GameObject.Find(RootName);
        if (old) Undo.DestroyObjectImmediate(old);
        bool restored = RestoreTerrains();
        if (logIt) Debug.Log("AscendSiteBuilder: removed" + (restored ? " and the ground was put back." : "."));
    }

    // ───────────────────────────── data ─────────────────────────────
    static AscendCADData LoadData()
    {
        foreach (var g in AssetDatabase.FindAssets("t:AscendCADData"))
        {
            var d = AssetDatabase.LoadAssetAtPath<AscendCADData>(AssetDatabase.GUIDToAssetPath(g));
            if (d) return AscendCADData.Resolve(d);
        }
        return null;
    }

    // Build every built-in pad at its biggest allowed rocket (crawler parked) and measure it
    static float BiggestPadRadius(AscendCADData data)
    {
        float best = 15f;
        if (data.launchPads == null) return best;
        foreach (var p in data.launchPads)
        {
            if (p == null || !AscendPadModels.Has(p)) continue;
            GameObject g = null;
            try
            {
                float rr = p.maxWidthM > 0 ? p.maxWidthM * 0.5f : 3f, hh = p.maxHeightM > 0 ? p.maxHeightM : 60f;
                g = AscendPadModels.Build(p, data, rr, hh, false, true, out _);
                g.transform.position = Vector3.zero;
                float r = 0f;
                foreach (var rend in g.GetComponentsInChildren<Renderer>())
                {
                    var b = rend.bounds;
                    foreach (var x in new[] { b.min.x, b.max.x }) foreach (var z in new[] { b.min.z, b.max.z }) r = Mathf.Max(r, new Vector2(x, z).magnitude);
                }
                log.AppendLine($"  pad {p.name}: radius {r:0.0} m");
                best = Mathf.Max(best, r);
            }
            catch (Exception e) { Debug.LogWarning("AscendSiteBuilder: pad " + p.name + " failed: " + e.Message); }
            finally { if (g) UnityEngine.Object.DestroyImmediate(g); }
        }
        return best;
    }

    struct Door { public string name; public Vector3 pos, outward; public Bounds footprint; }

    // The two building entrances (exterior teleport doors) and the footprint of the building behind each
    static List<Door> FindDoors()
    {
        var res = new List<Door>();
        var bigs = new List<(Transform t, Bounds b)>();
        var bRoot = GameObject.Find("Buildings");
        if (bRoot)
            foreach (Transform t in bRoot.transform)
            {
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.x > 8f && b.size.z > 8f && b.size.x < 200f && b.size.z < 200f) bigs.Add((t, b));
            }
        foreach (var d in UnityEngine.Object.FindObjectsByType<DoorInteraction>(FindObjectsInactive.Include))
        {
            if (!d.transform.parent || d.transform.parent.name != "Exterior") continue;
            if (d.name != "Mission Control" && d.name != "Office Door") continue;
            var door = new Door { name = d.name, pos = d.transform.position };
            // the building this door belongs to: the nearest big building
            float bestD = float.MaxValue; Bounds fb = new Bounds(door.pos, Vector3.zero);
            foreach (var (t, b) in bigs) { float dd = b.SqrDistance(door.pos); if (dd < bestD) { bestD = dd; fb = b; } }
            door.footprint = fb;
            // outward = from the building center through the door
            Vector3 o = door.pos - fb.center; o.y = 0f;
            if (o.sqrMagnitude < 0.01f) o = Vector3.forward;
            // snap to the nearest face direction of the footprint
            o = Mathf.Abs(o.x / Mathf.Max(0.1f, fb.extents.x)) > Mathf.Abs(o.z / Mathf.Max(0.1f, fb.extents.z)) ? new Vector3(Mathf.Sign(o.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(o.z));
            door.outward = o;
            door.pos.y = H(door.pos.x, door.pos.z);
            res.Add(door);
            log.AppendLine($"  door {d.name} at {door.pos}, building {fb.center} size {fb.size}, faces {o}");
        }
        return res.OrderBy(x => x.name).ToList();
    }

    // ───────────────────────────── terrain ─────────────────────────────
    static Terrain[] Terrains => UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);

    static Terrain TerrainAt(float x, float z)
    {
        foreach (var t in Terrains)
        {
            Vector3 p = t.transform.position, s = t.terrainData.size;
            if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z) return t;
        }
        return null;
    }

    static float H(float x, float z)
    {
        var t = TerrainAt(x, z);
        return t ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : 0f;
    }

    static float Slope(float x, float z)
    {
        const float e = 4f;
        float dx = (H(x + e, z) - H(x - e, z)) / (2 * e), dz = (H(x, z + e) - H(x, z - e)) / (2 * e);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static void SaveTerrains()
    {
        Directory.CreateDirectory(BackupDir);
        foreach (var t in Terrains)
        {
            var d = t.terrainData;
            Undo.RegisterCompleteObjectUndo(d, "Build site");
            string p = Path.Combine(BackupDir, "site_" + d.name + ".bin");
            if (File.Exists(p)) continue;   // keep the very first (original) state
            using (var w = new BinaryWriter(File.Create(p)))
            {
                int hr = d.heightmapResolution; var h = d.GetHeights(0, 0, hr, hr);
                w.Write(hr); for (int y = 0; y < hr; y++) for (int x = 0; x < hr; x++) w.Write(h[y, x]);
                int ar = d.alphamapResolution, al = d.alphamapLayers; var a = d.GetAlphamaps(0, 0, ar, ar);
                w.Write(ar); w.Write(al); for (int y = 0; y < ar; y++) for (int x = 0; x < ar; x++) for (int l = 0; l < al; l++) w.Write(a[y, x, l]);
                int dr = d.detailResolution, dl = d.detailPrototypes.Length;
                w.Write(dr); w.Write(dl);
                for (int l = 0; l < dl; l++) { var dd = d.GetDetailLayer(0, 0, dr, dr, l); for (int y = 0; y < dr; y++) for (int x = 0; x < dr; x++) w.Write(dd[y, x]); }
                var trees = d.treeInstances; w.Write(trees.Length);
                foreach (var tr in trees) { w.Write(tr.position.x); w.Write(tr.position.y); w.Write(tr.position.z); w.Write(tr.prototypeIndex); w.Write(tr.widthScale); w.Write(tr.heightScale); w.Write(tr.rotation); }
                w.Write(d.treePrototypes.Length);
            }
        }
    }

    static bool RestoreTerrains()
    {
        bool any = false;
        foreach (var t in Terrains)
        {
            var d = t.terrainData;
            string p = Path.Combine(BackupDir, "site_" + d.name + ".bin");
            if (!File.Exists(p)) continue;
            Undo.RegisterCompleteObjectUndo(d, "Remove site");
            using (var r = new BinaryReader(File.OpenRead(p)))
            {
                int hr = r.ReadInt32(); var h = new float[hr, hr];
                for (int y = 0; y < hr; y++) for (int x = 0; x < hr; x++) h[y, x] = r.ReadSingle();
                if (hr == d.heightmapResolution) d.SetHeights(0, 0, h);
                int ar = r.ReadInt32(), al = r.ReadInt32(); var a = new float[ar, ar, al];
                for (int y = 0; y < ar; y++) for (int x = 0; x < ar; x++) for (int l = 0; l < al; l++) a[y, x, l] = r.ReadSingle();
                if (ar == d.alphamapResolution && al == d.alphamapLayers) d.SetAlphamaps(0, 0, a);
                int dr = r.ReadInt32(), dl = r.ReadInt32();
                for (int l = 0; l < dl; l++)
                {
                    var dd = new int[dr, dr]; for (int y = 0; y < dr; y++) for (int x = 0; x < dr; x++) dd[y, x] = r.ReadInt32();
                    if (dr == d.detailResolution && l < d.detailPrototypes.Length) d.SetDetailLayer(0, 0, l, dd);
                }
                int tn = r.ReadInt32(); var trees = new TreeInstance[tn];
                for (int i = 0; i < tn; i++)
                    trees[i] = new TreeInstance { position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), prototypeIndex = r.ReadInt32(), widthScale = r.ReadSingle(), heightScale = r.ReadSingle(), rotation = r.ReadSingle(), color = Color.white, lightmapColor = Color.white };
                int protoCount = r.ReadInt32();
                if (d.treePrototypes.Length > protoCount) d.treePrototypes = d.treePrototypes.Take(protoCount).ToArray();
                d.SetTreeInstances(trees, true);
            }
            EditorUtility.SetDirty(d);
            any = true;
        }
        return any;
    }

    // Level the ground in a circle (smooth blend at the edge)
    static void Flatten(Vector3 c, float inner, float outer)
    {
        foreach (var t in Terrains)
        {
            var d = t.terrainData; int res = d.heightmapResolution; var h = d.GetHeights(0, 0, res, res);
            Vector3 tp = t.transform.position, sz = d.size; bool changed = false;
            float target = (c.y - tp.y) / sz.y;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = tp.x + x / (float)(res - 1) * sz.x, wz = tp.z + y / (float)(res - 1) * sz.z;
                float dist = new Vector2(wx - c.x, wz - c.z).magnitude;
                if (dist > outer) continue;
                float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, dist));
                h[y, x] = Mathf.Lerp(h[y, x], target, k); changed = true;
            }
            if (changed) { d.SetHeights(0, 0, h); EditorUtility.SetDirty(d); }
        }
    }

    // Dirt in a not-quite-perfect circle (painted into the terrain, so it blends naturally)
    static void PaintDirt(Vector3 c, float R)
    {
        float ph1 = 1.3f, ph2 = 4.1f;
        foreach (var t in Terrains)
        {
            var d = t.terrainData;
            int dirt = Array.FindIndex(d.terrainLayers, l => l && (l.name == "Dirt" || l.name.ToLower().Contains("dirt")));
            if (dirt < 0) { Debug.LogWarning("AscendSiteBuilder: no Dirt terrain layer on " + t.name + " (run ASCEND > Terrain > Make Terrains Realistic first)."); continue; }
            int res = d.alphamapResolution, L = d.alphamapLayers; var a = d.GetAlphamaps(0, 0, res, res);
            Vector3 tp = t.transform.position, sz = d.size; bool changed = false;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = tp.x + (x + 0.5f) / res * sz.x, wz = tp.z + (y + 0.5f) / res * sz.z;
                float dx = wx - c.x, dz = wz - c.z, dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > R * 1.3f + 6f) continue;
                float ang = Mathf.Atan2(dz, dx);
                float edge = R * (1f + 0.07f * Mathf.Sin(3f * ang + ph1) + 0.04f * Mathf.Sin(7f * ang + ph2))
                           + (Mathf.PerlinNoise(wx * 0.06f + 17f, wz * 0.06f + 3f) - 0.5f) * 6f;
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 2.5f, edge + 2.5f, dist));
                if (w <= 0f) continue;
                for (int l = 0; l < L; l++) a[y, x, l] = l == dirt ? Mathf.Max(a[y, x, l], w) : a[y, x, l] * (1f - w);
                float sum = 0f; for (int l = 0; l < L; l++) sum += a[y, x, l];
                if (sum > 0f) for (int l = 0; l < L; l++) a[y, x, l] /= sum;
                changed = true;
            }
            if (changed) { d.SetAlphamaps(0, 0, a); EditorUtility.SetDirty(d); }
        }
    }

    // Lower / raise the ground to sit just under a road
    static void Carve(List<Vector3> pts, float flat, float blend, bool loop)
    {
        foreach (var t in Terrains)
        {
            var d = t.terrainData; int res = d.heightmapResolution; var h = d.GetHeights(0, 0, res, res);
            Vector3 tp = t.transform.position, sz = d.size; bool changed = false;
            float cell = sz.x / (res - 1);
            int segs = loop ? pts.Count : pts.Count - 1;
            for (int s = 0; s < segs; s++)
            {
                Vector3 a = pts[s], b = pts[(s + 1) % pts.Count];
                float minX = Mathf.Min(a.x, b.x) - blend, maxX = Mathf.Max(a.x, b.x) + blend, minZ = Mathf.Min(a.z, b.z) - blend, maxZ = Mathf.Max(a.z, b.z) + blend;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - tp.x) / cell)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((maxX - tp.x) / cell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((minZ - tp.z) / cell)), y1 = Mathf.Min(res - 1, Mathf.CeilToInt((maxZ - tp.z) / cell));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(tp.x + x * cell, tp.z + y * cell), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
                    float tt = Mathf.Clamp01(Vector2.Dot(p - A, B - A) / Mathf.Max(1e-4f, (B - A).sqrMagnitude));
                    float dist = Vector2.Distance(p, A + (B - A) * tt);
                    if (dist > blend) continue;
                    float roadY = (Mathf.Lerp(a.y, b.y, tt) - 0.06f - tp.y) / sz.y;
                    float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flat, blend, dist));
                    h[y, x] = Mathf.Lerp(h[y, x], roadY, k); changed = true;
                }
            }
            if (changed) { d.SetHeights(0, 0, h); EditorUtility.SetDirty(d); }
        }
    }

    static void ClearGrass(List<(List<Vector3> pts, float w, bool loop, float lift)> roads, List<(Vector3 center, Vector3 fwd)> plazas, Vector3 c, float R)
    {
        foreach (var t in Terrains)
        {
            var d = t.terrainData; int res = d.detailResolution; if (res <= 0) continue;
            Vector3 tp = t.transform.position, sz = d.size;
            for (int l = 0; l < d.detailPrototypes.Length; l++)
            {
                var g = d.GetDetailLayer(0, 0, res, res, l); bool changed = false;
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    if (g[y, x] == 0) continue;
                    Vector3 p = new Vector3(tp.x + (x + 0.5f) / res * sz.x, 0f, tp.z + (y + 0.5f) / res * sz.z);
                    bool clear = new Vector2(p.x - c.x, p.z - c.z).magnitude < R;
                    if (!clear) foreach (var r in roads) if (DistToLine(r.pts, p, r.loop) < r.w * 0.5f + 1.5f) { clear = true; break; }
                    if (!clear) foreach (var pl in plazas) if (new Vector2(p.x - pl.center.x, p.z - pl.center.z).magnitude < 11f) { clear = true; break; }
                    if (clear) { g[y, x] = 0; changed = true; }
                }
                if (changed) { d.SetDetailLayer(0, 0, l, g); EditorUtility.SetDirty(d); }
            }
        }
    }

    // ───────────────────────────── routing ─────────────────────────────
    // A* on a 12 m grid: short, but avoids steep slopes and the launch area
    static List<Vector3> Route(Vector3 from, Vector3 to, Vector3 site, float keepOut)
    {
        const float cell = 12f;
        float minX = Mathf.Min(from.x, to.x) - 400f, minZ = Mathf.Min(from.z, to.z) - 400f;
        int W = Mathf.CeilToInt((Mathf.Max(from.x, to.x) + 400f - minX) / cell), Hh = Mathf.CeilToInt((Mathf.Max(from.z, to.z) + 400f - minZ) / cell);
        Func<int, int, Vector3> P = (i, j) => new Vector3(minX + i * cell, 0f, minZ + j * cell);
        var cost = new float[W, Hh];
        for (int i = 0; i < W; i++)
        for (int j = 0; j < Hh; j++)
        {
            Vector3 p = P(i, j);
            float s = TerrainAt(p.x, p.z) ? Slope(p.x, p.z) : 1f;
            float inSite = new Vector2(p.x - site.x, p.z - site.z).magnitude < keepOut ? 50f : 0f;
            cost[i, j] = 1f + 60f * s * s + (s > 0.15f ? 40f : 0f) + inSite;
        }
        int si = Mathf.Clamp(Mathf.RoundToInt((from.x - minX) / cell), 0, W - 1), sj = Mathf.Clamp(Mathf.RoundToInt((from.z - minZ) / cell), 0, Hh - 1);
        int ti = Mathf.Clamp(Mathf.RoundToInt((to.x - minX) / cell), 0, W - 1), tj = Mathf.Clamp(Mathf.RoundToInt((to.z - minZ) / cell), 0, Hh - 1);
        cost[si, sj] = 1f;
        var g = new float[W, Hh]; var came = new int[W, Hh]; var closed = new bool[W, Hh];
        for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++) { g[i, j] = float.MaxValue; came[i, j] = -1; }
        var open = new SortedSet<(float f, int i, int j)>();
        g[si, sj] = 0f; open.Add((0f, si, sj));
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur);
            int ci = cur.i, cj = cur.j;
            if (closed[ci, cj]) continue; closed[ci, cj] = true;
            if (ci == ti && cj == tj) break;
            for (int k = 0; k < 8; k++)
            {
                int ni = ci + dx[k], nj = cj + dz[k];
                if (ni < 0 || nj < 0 || ni >= W || nj >= Hh || closed[ni, nj]) continue;
                float step = (k < 4 ? 1f : 1.4142f) * (cost[ci, cj] + cost[ni, nj]) * 0.5f;
                float ng = g[ci, cj] + step;
                if (ng >= g[ni, nj]) continue;
                g[ni, nj] = ng; came[ni, nj] = ci * Hh + cj;
                float hEst = Mathf.Sqrt((ni - ti) * (ni - ti) + (nj - tj) * (nj - tj));
                open.Add((ng + hEst, ni, nj));
            }
        }
        var path = new List<Vector3> { to.WithY(0f) };
        int pi = ti, pj = tj, guard = 0;
        while (came[pi, pj] >= 0 && guard++ < 100000) { int prev = came[pi, pj]; pi = prev / Hh; pj = prev % Hh; path.Add(P(pi, pj)); }
        path[path.Count - 1] = from.WithY(0f);
        path.Reverse();
        log.AppendLine($"  main road: {path.Count} grid points, about {path.Count * cell:0} m");
        return Resample(Smooth(Simplify(path), 4), 4f);
    }

    static List<Vector3> Simplify(List<Vector3> p)
    {
        if (p.Count < 3) return p;
        var r = new List<Vector3> { p[0] };
        for (int i = 1; i < p.Count - 1; i++)
        {
            Vector3 a = (p[i] - r[r.Count - 1]).normalized, b = (p[i + 1] - p[i]).normalized;
            if (Vector3.Dot(a, b) < 0.999f) r.Add(p[i]);
        }
        r.Add(p[p.Count - 1]);
        return r;
    }

    // Chaikin smoothing (keeps the end points)
    static List<Vector3> Smooth(List<Vector3> p, int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            if (p.Count < 3) return p;
            var q = new List<Vector3> { p[0] };
            for (int i = 0; i < p.Count - 1; i++) { q.Add(Vector3.Lerp(p[i], p[i + 1], 0.25f)); q.Add(Vector3.Lerp(p[i], p[i + 1], 0.75f)); }
            q.Add(p[p.Count - 1]);
            p = q;
        }
        return p;
    }

    static List<Vector3> Resample(List<Vector3> p, float step)
    {
        var r = new List<Vector3> { p[0] };
        float carry = 0f;
        for (int i = 0; i < p.Count - 1; i++)
        {
            Vector3 a = p[i], b = p[i + 1]; float len = Vector3.Distance(a, b), d = step - carry;
            while (d <= len) { r.Add(Vector3.Lerp(a, b, d / len)); d += step; }
            carry = len - (d - step);
        }
        if (Vector3.Distance(r[r.Count - 1], p[p.Count - 1]) > step * 0.3f) r.Add(p[p.Count - 1]); else r[r.Count - 1] = p[p.Count - 1];
        return r;
    }

    // Heights from the ground, then smoothed along the road so it has no bumps
    static void SetHeights(List<Vector3> pts, int window, bool loop, bool keepSmooth = false)
    {
        var raw = pts.Select(p => H(p.x, p.z)).ToArray();
        for (int i = 0; i < pts.Count; i++)
        {
            float sum = 0f; int cnt = 0;
            for (int k = -window / 2; k <= window / 2; k++)
            {
                int j = i + k;
                if (loop) j = (j % pts.Count + pts.Count) % pts.Count; else if (j < 0 || j >= pts.Count) continue;
                sum += raw[j]; cnt++;
            }
            pts[i] = pts[i].WithY(sum / cnt);
        }
    }

    // Drop the first / last part of a line (by distance)
    static List<Vector3> Trim(List<Vector3> pts, float fromStart, float fromEnd)
    {
        float total = 0f; for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        var r = new List<Vector3>(); float d = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) d += Vector3.Distance(pts[i - 1], pts[i]);
            if (d >= fromStart && d <= total - fromEnd) r.Add(pts[i]);
        }
        return r;
    }

    // A closed loop opened up around one point (gap of 'gap' meters each side), as an open line
    static List<Vector3> OpenAround(List<Vector3> loop, Vector3 at, float gap)
    {
        int n = loop.Count, start = 0; float best = float.MaxValue;
        for (int i = 0; i < n; i++) { float d = new Vector2(loop[i].x - at.x, loop[i].z - at.z).magnitude; if (d < best) { best = d; start = i; } }
        var r = new List<Vector3>();
        for (int k = 1; k < n; k++)
        {
            var p = loop[(start + k) % n];
            if (new Vector2(p.x - at.x, p.z - at.z).magnitude > gap) r.Add(p);
        }
        return r;
    }

    // Flat round road patch that follows the ground
    static void Disc(GameObject parent, string name, Vector3 c, float radius, float lift, Material mat)
    {
        const int N = 40;
        var v = new List<Vector3> { new Vector3(c.x, H(c.x, c.z) + lift, c.z) }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var tri = new List<int>();
        for (int i = 0; i <= N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f; float x = c.x + Mathf.Cos(a) * radius, z = c.z + Mathf.Sin(a) * radius;
            v.Add(new Vector3(x, H(x, z) + lift, z)); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            if (i > 0) { tri.Add(0); tri.Add(i + 1); tri.Add(i); }
        }
        var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        if (m.normals[0].y < 0f) { tri.Reverse(); m.SetTriangles(tri, 0); m.RecalculateNormals(); }
        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(GenDir + "/" + name + ".asset"));
        var go = new GameObject(name); go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<MeshCollider>().sharedMesh = m; go.isStatic = true;
    }

    static List<Vector3> Offset(List<Vector3> pts, float off)
    {
        var r = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 f = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).WithY(0f).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, f);
            Vector3 p = pts[i] + side * off;
            r.Add(p.WithY(H(p.x, p.z)));
        }
        return r;
    }

    static List<Vector3> RectLoop(Bounds b, float pad)
    {
        float x0 = b.min.x - pad, x1 = b.max.x + pad, z0 = b.min.z - pad, z1 = b.max.z + pad;
        var corners = new[] { new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x0, 0, z1) };
        var r = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], c = corners[(i + 1) % 4];
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, c) / 3f));
            for (int k = 0; k < n; k++) { var p = Vector3.Lerp(a, c, k / (float)n); r.Add(p.WithY(H(p.x, p.z))); }
        }
        return r;
    }

    static float DistToLine(List<Vector3> pts, Vector3 p, bool loop)
    {
        float best = float.MaxValue; Vector2 P = new Vector2(p.x, p.z);
        int segs = loop ? pts.Count : pts.Count - 1;
        for (int s = 0; s < segs; s++)
        {
            Vector2 A = new Vector2(pts[s].x, pts[s].z), B = new Vector2(pts[(s + 1) % pts.Count].x, pts[(s + 1) % pts.Count].z);
            float t = Mathf.Clamp01(Vector2.Dot(P - A, B - A) / Mathf.Max(1e-4f, (B - A).sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(P, A + (B - A) * t));
        }
        return best;
    }

    // ───────────────────────────── meshes ─────────────────────────────
    static GameObject Child(GameObject parent, string n) { var g = new GameObject(n); g.transform.SetParent(parent.transform, false); return g; }
    static GameObject Child(Transform parent, string n) { var g = new GameObject(n); g.transform.SetParent(parent, false); return g; }

    // A flat strip along a line (road, sidewalk, center line). raised = sidewalk with curb sides.
    static GameObject Ribbon(Transform parent, string name, List<Vector3> pts, float width, float lift, bool loop, Material mat, float uvLen, bool collider, bool raised = false)
        => Ribbon(parent.gameObject, name, pts, width, lift, loop, mat, uvLen, collider, raised);

    static GameObject Ribbon(GameObject parent, string name, List<Vector3> pts, float width, float lift, bool loop, Material mat, float uvLen, bool collider, bool raised = false)
    {
        int n = pts.Count; if (n < 2) return new GameObject(name);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        float dist = 0f;
        int count = loop ? n + 1 : n;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = pts[i % n];
            Vector3 prev = pts[loop ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)], next = pts[loop ? (i + 1) % n : Mathf.Min(i + 1, n - 1)];
            Vector3 f = (next - prev).WithY(0f).normalized; Vector3 side = Vector3.Cross(Vector3.up, f) * width * 0.5f;
            if (i > 0) dist += Vector3.Distance(pts[(i - 1) % n], p);
            Vector3 top = p + Vector3.up * lift;
            v.Add(top - side); v.Add(top + side);
            uv.Add(new Vector2(0f, dist / uvLen)); uv.Add(new Vector2(1f, dist / uvLen));
            if (raised) { v.Add(top - side - Vector3.up * (lift + 0.25f)); v.Add(top + side - Vector3.up * (lift + 0.25f)); uv.Add(new Vector2(-0.05f, dist / uvLen)); uv.Add(new Vector2(1.05f, dist / uvLen)); }
        }
        int stride = raised ? 4 : 2;
        for (int i = 0; i < count - 1; i++)
        {
            int a = i * stride, b = (i + 1) * stride;
            tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            if (raised) { tri.AddRange(new[] { a + 2, b + 2, a, a, b + 2, b }); tri.AddRange(new[] { a + 1, b + 1, a + 3, a + 3, b + 1, b + 3 }); }
        }
        var m = new Mesh { name = name, indexFormat = v.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        // make sure the top faces up
        if (m.normals.Length > 0 && m.normals[0].y < 0f) { tri.Reverse(); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateTangents(); }
        string path = AssetDatabase.GenerateUniqueAssetPath(GenDir + "/" + name + ".asset");
        AssetDatabase.CreateAsset(m, path);
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = m;
        go.isStatic = true;
        return go;
    }

    // Raised pavement rectangle (plaza) in front of an entrance
    static void Slab(GameObject parent, string name, Vector3 center, Vector3 fwd, float width, float depth, Material mat)
    {
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var pts = new List<Vector3> { center - fwd * depth * 0.5f, center + fwd * depth * 0.5f };
        SetHeights(pts, 1, false);
        float y = Mathf.Max(pts[0].y, pts[1].y);
        pts = new List<Vector3> { (center - fwd * depth * 0.5f).WithY(y), (center + fwd * depth * 0.5f).WithY(y) };
        Ribbon(parent, name, pts, width, CurbHeight, false, mat, 3f, true, true);
    }

    static Material Mat(string name, string tex, float tiling, float smooth)
    {
        string path = GenDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var d = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + tex + "_diff_1k.jpg");
        var n = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + tex + "_nor_gl_1k.jpg");
        var ni = AssetImporter.GetAtPath(TexDir + tex + "_nor_gl_1k.jpg") as TextureImporter;
        if (ni && ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); n = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + tex + "_nor_gl_1k.jpg"); }
        m.SetTexture("_BaseMap", d); m.SetTextureScale("_BaseMap", new Vector2(tiling, 1f));
        if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
        m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return m;
    }

    // Dashed white center line: a small texture (white dash, clear gap), alpha clipped
    static Material LineMat()
    {
        string tp = GenDir + "/Road Line.png", mp = GenDir + "/Road Line.mat";
        if (!File.Exists(tp))
        {
            var t = new Texture2D(8, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 8; x++) t.SetPixel(x, y, y < 32 ? new Color(0.95f, 0.95f, 0.9f, 1f) : new Color(0, 0, 0, 0));
            File.WriteAllBytes(tp, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(tp);
            var imp = (TextureImporter)AssetImporter.GetAtPath(tp); imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Bilinear; imp.SaveAndReimport();
        }
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, mp); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
        m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Smoothness", 0.3f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ───────────────────────────── trees ─────────────────────────────
    static int PlantTrees(List<Vector3> road, Vector3 site, float siteR, List<Bounds> buildings, List<(List<Vector3> pts, float r)> avoid)
    {
        var protos = new[] {
            "Assets/Imports/Environment/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Fruit_Tree_01_green.prefab" };
        var rnd = new System.Random(42);
        var spots = new List<Vector3>();
        // along the main road, both sides, with gaps
        float run = 0f;
        for (int i = 1; i < road.Count; i++)
        {
            run += Vector3.Distance(road[i - 1], road[i]);
            if (run < 28f + (float)rnd.NextDouble() * 20f) continue;
            run = 0f;
            Vector3 f = (road[i] - road[i - 1]).WithY(0f).normalized, side = Vector3.Cross(Vector3.up, f);
            foreach (int s in new[] { -1, 1 }) if (rnd.NextDouble() > 0.35) spots.Add(road[i] + side * s * (RoadWidth * 0.5f + 6f + (float)rnd.NextDouble() * 6f));
        }
        // around each building
        foreach (var b in buildings)
            for (int i = 0; i < 14; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, dd = Mathf.Max(b.extents.x, b.extents.z) + 10f + (float)rnd.NextDouble() * 22f;
                spots.Add(b.center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dd);
            }
        int planted = 0;
        foreach (var t in Terrains)
        {
            var d = t.terrainData;
            var list = d.treePrototypes.ToList();
            var idx = new List<int>();
            foreach (var path in protos)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!pf) continue;
                int k = list.FindIndex(p => p.prefab == pf);
                if (k < 0) { list.Add(new TreePrototype { prefab = pf }); k = list.Count - 1; }
                idx.Add(k);
            }
            if (idx.Count == 0) continue;
            d.treePrototypes = list.ToArray();
            var inst = d.treeInstances.ToList();
            Vector3 tp = t.transform.position, sz = d.size;
            foreach (var s in spots)
            {
                if (s.x < tp.x || s.x > tp.x + sz.x || s.z < tp.z || s.z > tp.z + sz.z) continue;
                if (new Vector2(s.x - site.x, s.z - site.z).magnitude < siteR) continue;
                if (buildings.Any(b => { var e = b; e.Expand(new Vector3(8f, 1000f, 8f)); return e.Contains(new Vector3(s.x, b.center.y, s.z)); })) continue;
                if (avoid.Any(a => DistToLine(a.pts, s, true) < a.r)) continue;
                if (Slope(s.x, s.z) > 0.35f) continue;
                inst.Add(new TreeInstance
                {
                    position = new Vector3((s.x - tp.x) / sz.x, 0f, (s.z - tp.z) / sz.z),
                    prototypeIndex = idx[rnd.Next(idx.Count)],
                    widthScale = 0.85f + (float)rnd.NextDouble() * 0.4f, heightScale = 0.85f + (float)rnd.NextDouble() * 0.45f,
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2f, color = Color.white, lightmapColor = Color.white
                });
                planted++;
            }
            d.SetTreeInstances(inst.ToArray(), true);
            EditorUtility.SetDirty(d);
        }
        return planted;
    }

    // ───────────────────────────── people ─────────────────────────────
    static readonly string[] People = { "BusinessMan", "BusinessWoman", "Worker", "WomanB", "WomanC", "BusinessWoman" };

    static void AddWalker(GameObject parent, int n, List<Vector3> loop, float start, bool reverse, System.Random rnd)
    {
        var g = new GameObject("Person " + n);
        g.transform.SetParent(parent.transform, false);
        var w = g.AddComponent<AscendWalker>();
        var pts = new List<Vector3>(loop); if (reverse) pts.Reverse();
        w.waypoints = pts;
        w.startAt = start + (float)rnd.NextDouble() * 0.1f;
        w.speed = 1.15f + (float)rnd.NextDouble() * 0.3f;
        w.heightScale = 0.94f + (float)rnd.NextDouble() * 0.1f;
        w.personHeight = People[(n - 1) % People.Length].Contains("Woman") ? 1.64f : 1.75f;
        // real animated character (CC0, Quaternius)
        string path = "Assets/Imports/People/" + People[(n - 1) % People.Length] + ".glb";
        w.model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            var clip = a as AnimationClip; if (!clip) continue;
            if (clip.name.EndsWith("|Walk")) w.walkClip = clip;
            else if (clip.name.EndsWith("|Idle")) w.idleClip = clip;
            else if (clip.name.EndsWith("|Wave")) w.waveClip = clip;
        }
        if (!w.model || !w.walkClip) { w.model = null; w.outfit = (AscendWalker.Outfit)(n % 4); }
        g.transform.position = pts[0];
    }

    static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
}
#endif
