// ═════════════════════════════════════════════════════════════════════════════
//  AscendTerrainTool.cs  -  Menu: ASCEND > Terrain > Make Terrains Realistic
//
//  For every Terrain in the open scene:
//    1. Shape: adds natural ridges and gullies to the hills (flat ground where the
//       buildings stand is NOT touched). Always starts from the original heights
//       (saved once in <project>/TerrainBackup), so running it again doesn't stack.
//    2. Paint: 4 photo texture layers (grass, dry grass, rock, dirt) placed by
//       slope, height and large-scale noise - one shader pass, cheap on the GPU.
//    3. Trees are moved onto the new ground height.
//    4. Performance settings for integrated graphics (instancing, shorter grass distance).
//
//  Menu: ASCEND > Terrain > Restore Original Heights   puts the old hill shapes back.
// ═════════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class AscendTerrainTool
{
    const string TexDir = "Assets/Imports/Environment/Terrain Textures/";
    const string LayerDir = "Assets/Imports/Environment/Terrain Textures/Layers/";
    static string BackupDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "TerrainBackup");

    // ── Tunables ──
    const float HillStart = 3f, HillFull = 30f;       // m: below HillStart the ground is left exactly as it is
    const float RidgeScale = 160f;                    // m: size of the ridges
    const float RidgeBase = 4f, RidgePerMeter = 0.12f; // ridge height = 4 m + 12% of the hill height
    const float RockFrom = 46f, RockTo = 58f;         // slope (degrees) where grass turns into rock
    const float DirtFrom = 38f, DirtTo = 48f;

    [MenuItem("ASCEND/Terrain/Make Terrains Realistic")]
    public static void MakeRealistic()
    {
        var layers = MakeLayers();
        if (layers == null) return;
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var t in terrains)
        {
            Undo.RegisterCompleteObjectUndo(t.terrainData, "Realistic terrain");
            Shape(t);
            Paint(t, layers);
            FixTrees(t);
            Tune(t);
            EditorUtility.SetDirty(t.terrainData);
            EditorUtility.SetDirty(t);
        }
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"AscendTerrainTool: updated {terrains.Length} terrain(s).");
    }

    [MenuItem("ASCEND/Terrain/Restore Original Heights")]
    public static void Restore()
    {
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var h = LoadBase(t.terrainData);
            if (h == null) continue;
            Undo.RegisterCompleteObjectUndo(t.terrainData, "Restore terrain");
            t.terrainData.SetHeights(0, 0, h);
            FixTrees(t);
            EditorUtility.SetDirty(t.terrainData);
        }
        AssetDatabase.SaveAssets();
    }

    // ───────────── layers ─────────────
    static TerrainLayer[] MakeLayers()
    {
        string[] names = { "rocky_terrain_02", "sparse_grass", "rock_face", "dirt" };
        string[] layerNames = { "Grass", "Dry Grass", "Rock", "Dirt" };
        float[] tiles = { 15f, 12f, 18f, 6f };
        // color multipliers: greener, slightly lighter grass; dry grass and rock softened
        Vector4[] tint = { new Vector4(0.92f, 1.05f, 0.82f, 1f), new Vector4(0.95f, 1f, 0.85f, 1f), new Vector4(0.9f, 0.88f, 0.85f, 1f), Vector4.one };
        if (!AssetDatabase.IsValidFolder(LayerDir.TrimEnd('/'))) AssetDatabase.CreateFolder(TexDir.TrimEnd('/'), "Layers");
        var result = new TerrainLayer[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string dPath = TexDir + names[i] + "_diff_1k.jpg", nPath = TexDir + names[i] + "_nor_gl_1k.jpg";
            SetImport(dPath, false); SetImport(nPath, true);
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(dPath);
            var nor = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
            if (!diff) { Debug.LogError("AscendTerrainTool: missing " + dPath); return null; }
            string lp = LayerDir + layerNames[i] + ".terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp);
            if (!l) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, lp); }
            l.diffuseTexture = diff; l.normalMapTexture = nor; l.normalScale = 1f;
            l.tileSize = new Vector2(tiles[i], tiles[i]); l.tileOffset = Vector2.zero;
            l.metallic = 0f; l.smoothness = 0f;
            l.diffuseRemapMin = Vector4.zero; l.diffuseRemapMax = tint[i];
            EditorUtility.SetDirty(l);
            result[i] = l;
        }
        return result;
    }

    static void SetImport(string path, bool normal)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!imp) return;
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (imp.textureType == type && imp.maxTextureSize == 1024 && imp.mipmapEnabled) return;
        imp.textureType = type; imp.maxTextureSize = 1024; imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 4; imp.sRGBTexture = !normal;
        imp.textureCompression = TextureImporterCompression.Compressed;
        imp.SaveAndReimport();
    }

    // ───────────── shape ─────────────
    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    static float Ridged(float x, float z)
    {
        float sum = 0f, amp = 1f, norm = 0f, f = 1f / RidgeScale;
        for (int o = 0; o < 4; o++)
        {
            float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * f + 31.7f + o * 7.3f, z * f + 12.9f - o * 3.1f) * 2f - 1f);
            sum += n * n * amp; norm += amp; amp *= 0.5f; f *= 2.1f;
        }
        return sum / norm;
    }

    static float Fbm(float x, float z, float scale, int oct)
    {
        float sum = 0f, amp = 1f, norm = 0f, f = 1f / scale;
        for (int o = 0; o < oct; o++) { sum += Mathf.PerlinNoise(x * f + 101.3f + o * 5.1f, z * f + 57.7f + o * 9.4f) * amp; norm += amp; amp *= 0.5f; f *= 2f; }
        return sum / norm;
    }

    static void Shape(Terrain t)
    {
        var d = t.terrainData;
        var base_ = LoadBase(d) ?? SaveBase(d);
        int res = d.heightmapResolution;
        Vector3 size = d.size, pos = t.transform.position;
        var delta = new float[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float hb = base_[y, x] * size.y;
            float mask = Smooth(HillStart, HillFull, hb);
            if (mask <= 0f) continue;
            float wx = pos.x + x / (float)(res - 1) * size.x, wz = pos.z + y / (float)(res - 1) * size.z;
            float ridge = Ridged(wx, wz) - 0.45f;
            float fine = Fbm(wx, wz, 40f, 3) - 0.5f;
            delta[y, x] = mask * (ridge * (RidgeBase + hb * RidgePerMeter) + fine * 3f);
        }
        // soften (not on the outer edges, so neighbouring terrains still line up)
        var h = new float[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float dv = delta[y, x];
            if (x > 0 && y > 0 && x < res - 1 && y < res - 1)
                dv = (delta[y, x] * 4f + delta[y - 1, x] + delta[y + 1, x] + delta[y, x - 1] + delta[y, x + 1]) / 8f;
            h[y, x] = Mathf.Clamp01(base_[y, x] + dv / size.y);
            if (base_[y, x] * size.y < HillStart) h[y, x] = base_[y, x];   // flat ground stays exactly the same
        }
        d.SetHeights(0, 0, h);
    }

    static string BasePath(TerrainData d) => Path.Combine(BackupDir, d.name + "_" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(d)) + ".heights");

    static float[,] SaveBase(TerrainData d)
    {
        Directory.CreateDirectory(BackupDir);
        int res = d.heightmapResolution;
        var h = d.GetHeights(0, 0, res, res);
        using (var w = new BinaryWriter(File.Create(BasePath(d))))
        {
            w.Write(res);
            for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) w.Write(h[y, x]);
        }
        return h;
    }

    static float[,] LoadBase(TerrainData d)
    {
        string p = BasePath(d);
        if (!File.Exists(p)) return null;
        using (var r = new BinaryReader(File.OpenRead(p)))
        {
            int res = r.ReadInt32();
            if (res != d.heightmapResolution) return null;
            var h = new float[res, res];
            for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) h[y, x] = r.ReadSingle();
            return h;
        }
    }

    // ───────────── paint ─────────────
    static void Paint(Terrain t, TerrainLayer[] layers)
    {
        var d = t.terrainData;
        d.terrainLayers = layers;
        int res = d.alphamapResolution;
        Vector3 size = d.size, pos = t.transform.position;
        var a = new float[res, res, layers.Length];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
            float steep = d.GetSteepness(u, v);
            float hgt = d.GetInterpolatedHeight(u, v);
            float wx = pos.x + u * size.x, wz = pos.z + v * size.z;
            float hill = Smooth(2f, 15f, hgt);
            float breakup = Fbm(wx, wz, 18f, 2) * 6f - 3f;                       // ragged rock / dirt edges
            float rock = Smooth(RockFrom, RockTo, steep + breakup) * hill;
            float dirt = Smooth(DirtFrom, DirtTo, steep + breakup) * (1f - rock) * 0.5f * hill;
            float macro = Fbm(wx, wz, 220f, 2);
            float dry = Smooth(0.55f, 0.72f, macro) * 0.5f + hill * Smooth(80f, 180f, hgt) * 0.25f;
            float rem = Mathf.Max(0f, 1f - rock - dirt);
            float wDry = rem * Mathf.Clamp01(dry);
            a[y, x, 0] = rem - wDry;
            a[y, x, 1] = wDry;
            a[y, x, 2] = rock;
            a[y, x, 3] = dirt;
        }
        d.SetAlphamaps(0, 0, a);
    }

    static void FixTrees(Terrain t)
    {
        var d = t.terrainData;
        var trees = d.treeInstances;
        for (int i = 0; i < trees.Length; i++)
        {
            var p = trees[i].position;
            p.y = d.GetInterpolatedHeight(p.x, p.z) / d.size.y;
            trees[i].position = p;
        }
        d.treeInstances = trees;
    }

    // ───────────── performance (integrated graphics friendly) ─────────────
    static void Tune(Terrain t)
    {
        t.drawInstanced = true;
        t.heightmapPixelError = 6f;
        t.basemapDistance = 400f;
        t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, 120f);
        t.treeDistance = Mathf.Min(t.treeDistance, 2000f);
        t.treeBillboardDistance = Mathf.Min(t.treeBillboardDistance, 60f);
        t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }
}
#endif
