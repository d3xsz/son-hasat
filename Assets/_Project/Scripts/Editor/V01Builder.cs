using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// V-01 görsel paket üreticisi (yalnızca editör). Ch1_Island açıkken "Tools/Son Hasat/V-01 ..." menülerinden
/// yeniden çalıştırılabilir; her çalıştırmada kendi ürettiklerini silip baştan kurar (elle yapılan değişiklikler gider).
/// Ürettikleri: Art/V01 (doku, materyal, mesh), sahnede TotemShrines, EnvDecor/Mushrooms|Ferns|Fireflies, CaveEntrance/CaveFace.
/// </summary>
public static class V01Builder
{
    private const string Dir = "Assets/_Project/Art/V01";
    private const string MeshPath = Dir + "/V01Meshes.asset";
    private static System.Random rng;

    // ------------------------------------------------------------------ menü

    [MenuItem("Tools/Son Hasat/V-01 Hepsini kur")]
    public static void BuildAll()
    {
        SetupPlayerBeam();
        BuildShrines();
        BuildForestLife();
        BuildCaveFace();
    }

    // ------------------------------------------------------------------ yardımcılar

    private static float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
    private static int RI(int a, int b) { return rng.Next(a, b); }

    private static float N3(Vector3 p)
    {
        return (Mathf.PerlinNoise(p.x + 13.1f, p.y + 7.7f) + Mathf.PerlinNoise(p.y + 31.4f, p.z + 2.3f) + Mathf.PerlinNoise(p.z + 5.9f, p.x + 17.3f)) / 3f;
    }

    private static void EnsureDir()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Project/Art", "V01");
    }

    /// <summary>Mesh deposu: tek .asset içinde alt mesh'ler. Aynı adla yeniden istenirse eskisi yerinde güncellenir (referanslar kopmaz).</summary>
    private static Mesh StoreMesh(Mesh built)
    {
        EnsureDir();
        Mesh main = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (main == null)
        {
            main = new Mesh { name = "V01Meshes" };
            AssetDatabase.CreateAsset(main, MeshPath);
        }
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath))
        {
            Mesh m = o as Mesh;
            if (m == null || m == main || m.name != built.name) continue;
            m.Clear();
            m.indexFormat = built.indexFormat;
            m.vertices = built.vertices; m.normals = built.normals; m.uv = built.uv; m.triangles = built.triangles;
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
            Object.DestroyImmediate(built);
            return m;
        }
        AssetDatabase.AddObjectToAsset(built, MeshPath);
        EditorUtility.SetDirty(main);
        return built;
    }

    private static Material Mat(string name, Shader shader)
    {
        EnsureDir();
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material FindMat(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:Material", new[] { "Assets/_Project" }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null && m.name == name) return m;
        }
        return null;
    }

    private static Texture2D FindTex(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { "Assets/_Project" }))
        {
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
            if (t != null && t.name == name) return t;
        }
        return null;
    }

    private static Texture2D SaveTex(Texture2D tex, string name, bool clamp, bool alphaTransparency)
    {
        EnsureDir();
        string path = Dir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.alphaIsTransparency = alphaTransparency;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static GameObject Child(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void DestroyChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    private static MeshRenderer AddMesh(GameObject go, Mesh mesh, Material mat, bool shadows)
    {
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return r;
    }

    private static void MarkDirty()
    {
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    /// <summary>Düz gölgelemeli (low-poly) mesh kurucu: her üçgen kendi köşelerini taşır.</summary>
    private class Flat
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<int> t = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Quad(Vector3 p00, Vector3 p10, Vector3 p01, Vector3 p11, Vector3 outward)
        {
            Tri(p00, p01, p11, outward);
            Tri(p00, p11, p10, outward);
        }

        public Mesh Build(string name)
        {
            Mesh m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.uv = new Vector2[v.Count];
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    // ------------------------------------------------------------------ 1) fener huzmesi

    [MenuItem("Tools/Son Hasat/V-01 Fener huzmesi (Player prefab)")]
    public static void SetupPlayerBeam()
    {
        Material beam = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/VFX/Mat_LightBeam.mat");
        const string path = "Assets/_Project/Prefabs/Player.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Light flash = null;
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) if (l.name == "Flashlight") flash = l;
            if (flash == null) { Debug.LogError("V01: Player.prefab içinde Flashlight ışığı yok"); return; }
            FlashlightBeam fb = flash.GetComponent<FlashlightBeam>();
            if (fb == null) fb = flash.gameObject.AddComponent<FlashlightBeam>();
            SerializedObject so = new SerializedObject(fb);
            so.FindProperty("beamMaterial").objectReferenceValue = beam;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ------------------------------------------------------------------ 2) totem tapınakları

    private static readonly float[][][] Glyphs =
    {
        new[] { new[] { 0.2f, 0.5f, 0.5f, 0.76f, 0.8f, 0.5f, 0.5f, 0.24f, 0.2f, 0.5f }, new[] { 0.5f, 0.41f, 0.5f, 0.59f } },                                  // göz
        new[] { new[] { 0.5f, 0.15f, 0.16f, 0.58f }, new[] { 0.5f, 0.15f, 0.31f, 0.8f }, new[] { 0.5f, 0.15f, 0.5f, 0.88f }, new[] { 0.5f, 0.15f, 0.69f, 0.8f }, new[] { 0.5f, 0.15f, 0.84f, 0.58f } }, // beş
        new[] { new[] { 0.5f, 0.12f, 0.5f, 0.88f }, new[] { 0.26f, 0.62f, 0.5f, 0.88f, 0.74f, 0.62f }, new[] { 0.3f, 0.3f, 0.7f, 0.3f } },                     // totem
        new[] { new[] { 0.15f, 0.7f, 0.33f, 0.52f, 0.5f, 0.7f, 0.67f, 0.52f, 0.85f, 0.7f }, new[] { 0.15f, 0.4f, 0.33f, 0.22f, 0.5f, 0.4f, 0.67f, 0.22f, 0.85f, 0.4f } }, // su
        new[] { new[] { 0.5f, 0.1f, 0.5f, 0.9f }, new[] { 0.5f, 0.45f, 0.25f, 0.7f }, new[] { 0.5f, 0.45f, 0.75f, 0.7f }, new[] { 0.5f, 0.25f, 0.3f, 0.42f }, new[] { 0.5f, 0.25f, 0.7f, 0.42f } },     // ağaç
        new[] { new[] { 0.2f, 0.14f, 0.2f, 0.6f, 0.5f, 0.88f, 0.8f, 0.6f, 0.8f, 0.14f }, new[] { 0.42f, 0.36f, 0.5f, 0.46f, 0.58f, 0.36f, 0.5f, 0.26f, 0.42f, 0.36f } },                             // mağara
        new[] { new[] { 0.2f, 0.2f, 0.8f, 0.2f, 0.8f, 0.8f, 0.2f, 0.8f, 0.2f, 0.4f, 0.6f, 0.4f, 0.6f, 0.6f, 0.4f, 0.6f } },                                     // sarmal
        new[] { new[] { 0.22f, 0.22f, 0.78f, 0.78f }, new[] { 0.22f, 0.78f, 0.78f, 0.22f }, new[] { 0.12f, 0.5f, 0.3f, 0.5f }, new[] { 0.7f, 0.5f, 0.88f, 0.5f } }, // mühür
    };

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (p - (a + ab * t)).magnitude;
    }

    private static Texture2D BuildRuneAtlas()
    {
        const int cell = 128;
        Texture2D tex = new Texture2D(cell * 4, cell * 2, TextureFormat.RGBA32, true);
        Color[] px = new Color[tex.width * tex.height];
        for (int g = 0; g < 8; g++)
        {
            int cx = (g % 4) * cell, cy = (g / 4) * cell;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / cell, (y + 0.5f) / cell);
                    float d = 10f;
                    foreach (float[] line in Glyphs[g])
                        for (int i = 0; i + 3 < line.Length; i += 2)
                            d = Mathf.Min(d, SegDist(p, new Vector2(line[i], line[i + 1]), new Vector2(line[i + 2], line[i + 3])));
                    float core = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.026f, 0.05f, d));
                    float glow = 0.32f * Mathf.Exp(-d * d / 0.009f);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(Mathf.Min(p.x, 1f - p.x), Mathf.Min(p.y, 1f - p.y)) / 0.09f);
                    px[(cy + y) * tex.width + cx + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core + glow) * edge);
                }
        }
        tex.SetPixels(px);
        tex.Apply();
        return SaveTex(tex, "T_ShrineRunes", true, true);
    }

    private static Texture2D BuildSoftBand()
    {
        Texture2D tex = new Texture2D(8, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++)
        {
            float v = (y + 0.5f) / 64f * 2f - 1f;
            float a = Mathf.Clamp01(Mathf.Exp(-v * v * 9f) * 0.55f + Mathf.SmoothStep(1f, 0f, Mathf.Abs(v) / 0.22f) * 0.6f);
            a *= Mathf.SmoothStep(0f, 1f, (1f - Mathf.Abs(v)) / 0.2f);
            for (int x = 0; x < 8; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return SaveTex(tex, "T_SoftBand", true, true);
    }

    private struct StoneShape { public float w, h, d, taper, lean, slant, amp, freq; public Vector3 seed; }

    private static Vector3 StonePoint(Vector3 u, StoneShape s)
    {
        float ty = u.y + 0.5f;
        Vector3 p = new Vector3(u.x * s.w * Mathf.Lerp(1f, s.taper, ty), ty * s.h, u.z * s.d * Mathf.Lerp(1f, s.taper * 0.8f + 0.2f, ty));
        p.x += s.lean * ty * ty;
        p.y += s.slant * u.x * 2f * ty * ty * ty;
        float amp = s.amp * (u.z > 0.49f ? 0.3f : 1f);              // ön yüz (rünler) daha düz
        // köşeler yontulmuş: kenarlara yaklaştıkça içe çek
        float cornerX = Mathf.SmoothStep(0f, 1f, (Mathf.Abs(u.x) - 0.3f) / 0.2f), cornerZ = Mathf.SmoothStep(0f, 1f, (Mathf.Abs(u.z) - 0.3f) / 0.2f);
        float chip = cornerX * cornerZ * (0.05f + 0.07f * N3(p * 1.3f + s.seed * 0.5f));
        p.x -= Mathf.Sign(u.x) * chip; p.z -= Mathf.Sign(u.z) * chip * 0.6f;
        float n = N3(p * s.freq + s.seed) - 0.5f;
        float n2 = N3(p * s.freq * 3.3f + s.seed * 1.7f) - 0.5f;
        Vector3 dir = new Vector3(u.x, ty > 0.95f ? 0.5f : 0f, u.z);
        if (dir.sqrMagnitude > 1e-6f) p += dir.normalized * (n * 2.4f * amp + n2 * 0.9f * amp);
        if (ty < 0.01f) p.y = -0.2f;                                 // taban toprağa gömülü
        return p;
    }

    private static Mesh BuildStone(string name, StoneShape s, int nx, int ny, int nz)
    {
        Flat f = new Flat();
        // 4 yan yüz + üst + alt (alt yüz yoksa aşağıdan bakınca taşın içi boş görünür)
        for (int face = 0; face < 6; face++)
        {
            int na = face < 2 ? nx : (face < 4 ? nz : nx);
            int nb = face < 4 ? ny : nz;
            for (int a = 0; a < na; a++)
                for (int b = 0; b < nb; b++)
                {
                    Vector3[] c = new Vector3[4];
                    for (int k = 0; k < 4; k++)
                    {
                        float fa = (a + (k & 1)) / (float)na - 0.5f;
                        float fb = (b + (k >> 1)) / (float)nb - 0.5f;
                        Vector3 u;
                        switch (face)
                        {
                            case 0: u = new Vector3(fa, fb, 0.5f); break;
                            case 1: u = new Vector3(fa, fb, -0.5f); break;
                            case 2: u = new Vector3(0.5f, fb, fa); break;
                            case 3: u = new Vector3(-0.5f, fb, fa); break;
                            case 4: u = new Vector3(fa, 0.5f, fb); break;
                            default: u = new Vector3(fa, -0.5f, fb); break;
                        }
                        c[k] = StonePoint(u, s);
                    }
                    Vector3 outward = face == 0 ? Vector3.forward : face == 1 ? Vector3.back : face == 2 ? Vector3.right : face == 3 ? Vector3.left : face == 4 ? Vector3.up : Vector3.down;
                    f.Quad(c[0], c[1], c[2], c[3], outward);
                }
        }
        return StoreMesh(f.Build(name));
    }

    private static Mesh BuildPaving(string name, int seed)
    {
        System.Random old = rng; rng = new System.Random(seed);
        Flat f = new Flat();
        // totemin dibindeki taş bilezik
        const int ring = 14;
        for (int i = 0; i < ring; i++)
        {
            float a0 = i * Mathf.PI * 2f / ring, a1 = (i + 1) * Mathf.PI * 2f / ring;
            float h0 = 0.07f + 0.03f * Mathf.Sin(i * 2.1f), h1 = 0.07f + 0.03f * Mathf.Sin((i + 1) * 2.1f);
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 i0 = d0 * 0.6f + Vector3.up * h0, i1 = d1 * 0.6f + Vector3.up * h1;
            Vector3 o0 = d0 * 1.12f + Vector3.up * (h0 - 0.015f), o1 = d1 * 1.12f + Vector3.up * (h1 - 0.015f);
            f.Quad(i0, i1, o0, o1, Vector3.up);
            f.Quad(o0, o1, d0 * 1.22f - Vector3.up * 0.02f, d1 * 1.22f - Vector3.up * 0.02f, (d0 + d1));
            f.Quad(i0, i1, d0 * 0.56f - Vector3.up * 0.02f, d1 * 0.56f - Vector3.up * 0.02f, -(d0 + d1));
        }
        // dağınık döşeme taşları
        List<Vector3> placed = new List<Vector3>();
        for (int tries = 0; tries < 400 && placed.Count < 46; tries++)
        {
            float ang = R(0f, Mathf.PI * 2f), rad = Mathf.Sqrt(R(1.45f * 1.45f, 2.55f * 2.55f));
            float size = R(0.2f, 0.42f);
            Vector3 c = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            bool ok = true;
            foreach (Vector3 q in placed) if (Vector2.Distance(new Vector2(q.x, q.z), new Vector2(c.x, c.z)) < q.y + size + 0.05f) { ok = false; break; }
            if (!ok) continue;
            placed.Add(new Vector3(c.x, size, c.z));
            int k = RI(5, 8);
            float h = R(0.035f, 0.075f), rot = R(0f, 6.28f);
            Vector3 tilt = new Vector3(R(-0.05f, 0.05f), 0f, R(-0.05f, 0.05f));
            Vector3[] top = new Vector3[k], bot = new Vector3[k];
            for (int i = 0; i < k; i++)
            {
                float a = rot + i * Mathf.PI * 2f / k + R(-0.2f, 0.2f);
                float r = size * R(0.78f, 1.08f);
                Vector3 o = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                top[i] = c + o * 0.9f + Vector3.up * (h + Vector3.Dot(o, tilt));
                bot[i] = c + o - Vector3.up * 0.02f;
            }
            Vector3 mid = c + Vector3.up * (h + 0.006f);
            for (int i = 0; i < k; i++)
            {
                int n = (i + 1) % k;
                f.Tri(mid, top[i], top[n], Vector3.up);
                f.Quad(top[i], top[n], bot[i], bot[n], (top[i] + top[n]) * 0.5f - c);
            }
        }
        rng = old;
        return StoreMesh(f.Build(name));
    }

    private static Mesh BuildRuneQuad(int g)
    {
        float u0 = (g % 4) / 4f, v0 = (g / 4) / 2f;
        Mesh m = new Mesh { name = "Rune_" + g };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + 0.25f, v0), new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.25f, v0 + 0.5f) };
        m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        m.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    /// <summary>Mühür halkası: 5 yay + her taşa uzanan 5 kısa çizgi (XZ düzlemi; taşlar 72° aralıkla, ilki +Z'de).</summary>
    private static Mesh BuildSeal()
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        const float r = 2.78f, w = 0.2f;
        for (int arc = 0; arc < 5; arc++)
        {
            float a0 = (arc * 72f + 7f) * Mathf.Deg2Rad, a1 = (arc * 72f + 65f) * Mathf.Deg2Rad;
            const int seg = 14;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg);
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(d * (r - w)); uv.Add(new Vector2(i / (float)seg, 0f));
                v.Add(d * (r + w)); uv.Add(new Vector2(i / (float)seg, 1f));
                if (i == seg) continue;
                int b = v.Count - 2;
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3);
            }
            // taşa uzanan çizgi
            float am = arc * 72f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(am), 0f, Mathf.Cos(am)), side = new Vector3(dir.z, 0f, -dir.x) * w;
            int s = v.Count;
            v.Add(dir * 2.3f - side); uv.Add(new Vector2(0f, 0f));
            v.Add(dir * 2.3f + side); uv.Add(new Vector2(0f, 1f));
            v.Add(dir * 3.05f - side); uv.Add(new Vector2(1f, 0f));
            v.Add(dir * 3.05f + side); uv.Add(new Vector2(1f, 1f));
            t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s + 2); t.Add(s + 1); t.Add(s + 3);
        }
        Mesh m = new Mesh { name = "ShrineSeal" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return StoreMesh(m);
    }

    private static Material AdditiveParticle(string name, Color color)
    {
        Material src = FindMat("Mat_DustMote");
        Material m = Mat(name, src != null ? src.shader : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        if (src != null) m.CopyPropertiesFromMaterial(src);
        Texture2D soft = FindTex("T_SoftCircle");
        if (soft != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", soft);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3000;
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Tapınak / oyma taşı: uçurum kayasının koyu, yosunlu, sık dokulu kopyası.</summary>
    private static Material ShrineStoneMat()
    {
        Material cliff = FindMat("Mat_Map_Cliff");
        Material m = Mat("Mat_ShrineStone", cliff.shader);
        m.CopyPropertiesFromMaterial(cliff);
        m.SetColor("_BaseColor", new Color(0.46f, 0.5f, 0.47f, 1f));
        m.SetFloat("_Tiling", 2.4f);
        m.SetFloat("_BumpScale", 1.7f);
        m.SetFloat("_MacroScale", 0.6f);
        m.SetFloat("_MacroStrength", 0.55f);
        m.SetFloat("_Smoothness", 0.16f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static bool Blocked(Vector3 center, Vector3 half, Quaternion rot)
    {
        foreach (Collider c in Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.name.StartsWith("Ground") || c.name == "Sea") continue;
            if (c.GetComponentInParent<Totem>() != null) continue;
            if (c.transform.root.name == "TotemShrines") continue;
            return true;
        }
        return false;
    }

    [MenuItem("Tools/Son Hasat/V-01 Totem tapınakları")]
    public static void BuildShrines()
    {
        rng = new System.Random(5105);
        Texture2D runes = BuildRuneAtlas();
        Texture2D band = BuildSoftBand();
        Shader glowShader = Shader.Find("Goblin/FogResistUnlit");
        Material runeMat = Mat("Mat_ShrineRune", glowShader);
        runeMat.SetTexture("_BaseMap", runes);
        runeMat.SetColor("_BaseColor", new Color(1f, 0.6f, 0.2f, 1f));
        runeMat.SetColor("_EmissionColor", new Color(2.6f, 1.25f, 0.28f));
        runeMat.SetFloat("_FogStrength", 1f);   // KULLANICI: totem yerleri uzaktan belli olmasın → tam sis
        Material sealMat = Mat("Mat_ShrineSeal", glowShader);
        sealMat.SetTexture("_BaseMap", band);
        sealMat.SetColor("_BaseColor", new Color(1f, 0.6f, 0.2f, 1f));
        sealMat.SetColor("_EmissionColor", new Color(2.6f, 1.25f, 0.28f));
        sealMat.SetFloat("_FogStrength", 1f);
        Material emberMat = AdditiveParticle("Mat_Ember", new Color(3.2f, 1.5f, 0.35f, 1f));
        Material stoneMat = ShrineStoneMat();

        Mesh[] stones = new Mesh[4];
        StoneShape[] shapes = new StoneShape[4];
        for (int i = 0; i < 4; i++)
        {
            shapes[i] = new StoneShape
            {
                w = R(0.78f, 1.0f), h = R(1.9f, 2.7f), d = R(0.42f, 0.56f), taper = R(0.55f, 0.78f), lean = R(-0.12f, 0.12f),
                slant = R(-0.3f, 0.3f), amp = 0.085f, freq = 2.1f, seed = new Vector3(R(0f, 50f), R(0f, 50f), R(0f, 50f))
            };
            stones[i] = BuildStone("ShrineStone_" + i, shapes[i], 5, 11, 3);
        }
        Mesh[] runeQuads = new Mesh[8];
        for (int i = 0; i < 8; i++) runeQuads[i] = BuildRuneQuad(i);
        Mesh seal = BuildSeal();

        GameObject old = GameObject.Find("TotemShrines");
        if (old != null) Object.DestroyImmediate(old);
        GameObject rootGo = new GameObject("TotemShrines");
        Physics.SyncTransforms();

        Totem[] totems = Object.FindObjectsByType<Totem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(totems, (a, b) => string.CompareOrdinal(a.name, b.name));
        int index = 0;
        foreach (Totem totem in totems)
        {
            index++;
            Vector3 center = totem.transform.position;
            center.y = 0f;
            Mesh paving = BuildPaving("ShrinePaving_" + index, 900 + index);

            // halka açısı: en az engele denk gelen döndürme
            float bestYaw = 0f; int bestHits = 99;
            for (int step = 0; step < 12; step++)
            {
                float yaw = step * 6f + index * 17f;
                int hits = 0;
                for (int k = 0; k < 5; k++)
                {
                    Quaternion q = Quaternion.Euler(0f, yaw + k * 72f, 0f);
                    if (Blocked(center + q * Vector3.forward * 3.35f + Vector3.up * 1.2f, new Vector3(0.75f, 1f, 0.6f), q)) hits++;
                }
                if (hits < bestHits) { bestHits = hits; bestYaw = yaw; }
                if (hits == 0) break;
            }

            GameObject shrine = Child(rootGo.transform, "Shrine_" + index);
            shrine.transform.position = center;
            shrine.transform.rotation = Quaternion.Euler(0f, bestYaw, 0f);
            List<Renderer> glow = new List<Renderer>();

            GameObject pav = Child(shrine.transform, "Paving");
            pav.transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            AddMesh(pav, paving, stoneMat, false);

            GameObject sealGo = Child(shrine.transform, "Seal");
            sealGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            glow.Add(AddMesh(sealGo, seal, sealMat, false));

            for (int k = 0; k < 5; k++)
            {
                Quaternion q = Quaternion.Euler(0f, k * 72f, 0f);
                Vector3 local = q * Vector3.forward * R(3.25f, 3.45f);
                Quaternion worldRot = shrine.transform.rotation * q;
                if (Blocked(shrine.transform.TransformPoint(local) + Vector3.up * 1.2f, new Vector3(0.6f, 1f, 0.45f), worldRot)) continue;
                int variant = (k + index) % 4;
                StoneShape s = shapes[variant];
                float scale = R(0.9f, 1.12f);
                GameObject stone = Child(shrine.transform, "Stone_" + (k + 1));
                stone.transform.localPosition = local;
                // taşın ön yüzü (+Z) toteme bakar
                stone.transform.localRotation = q * Quaternion.Euler(R(-4f, 4f), 180f + R(-9f, 9f), R(-3f, 3f));
                stone.transform.localScale = Vector3.one * scale;
                AddMesh(stone, stones[variant], stoneMat, true);
                BoxCollider col = stone.AddComponent<BoxCollider>();
                col.center = new Vector3(s.lean * 0.4f, s.h * 0.5f, 0f);
                col.size = new Vector3(s.w * 0.86f, s.h, s.d * 0.86f);

                int count = s.h > 2.3f ? 3 : 2;
                for (int j = 0; j < count; j++)
                {
                    float ty = 0.34f + j * (0.42f / Mathf.Max(1, count - 1)) + (count == 2 ? 0.06f : 0f);
                    float size = Mathf.Min(0.6f, s.w * Mathf.Lerp(1f, s.taper, ty) * 0.8f);
                    GameObject rune = Child(stone.transform, "Rune_" + (j + 1));
                    float zFace = s.d * 0.5f * Mathf.Lerp(1f, s.taper * 0.8f + 0.2f, ty) + 0.07f;
                    rune.transform.localPosition = new Vector3(s.lean * ty * ty, ty * s.h, zFace);
                    float lean = Mathf.Atan2(s.d * 0.5f * (1f - (s.taper * 0.8f + 0.2f)), s.h) * Mathf.Rad2Deg;
                    rune.transform.localRotation = Quaternion.Euler(-lean, 0f, R(-5f, 5f));
                    rune.transform.localScale = new Vector3(size, size, 1f);
                    glow.Add(AddMesh(rune, runeQuads[RI(0, 8)], runeMat, false));
                }
            }

            GameObject emberGo = Child(shrine.transform, "Embers");
            emberGo.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            ParticleSystem ps = emberGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.playOnAwake = true; main.maxParticles = 70;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 5.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.15f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.015f;
            ParticleSystem.EmissionModule em = ps.emission; em.rateOverTime = 8f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box; sh.rotation = new Vector3(-90f, 0f, 0f); sh.scale = new Vector3(5.2f, 5.2f, 0.1f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true; noise.strength = 0.28f; noise.frequency = 0.55f; noise.scrollSpeed = 0.3f;
            ParticleSystem.ColorOverLifetimeModule col2 = ps.colorOverLifetime;
            col2.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.4f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col2.color = new ParticleSystem.MinMaxGradient(g);
            ParticleSystemRenderer pr = emberGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = emberMat; pr.shadowCastingMode = ShadowCastingMode.Off;

            TotemShrine ts = shrine.AddComponent<TotemShrine>();
            SerializedObject so = new SerializedObject(ts);
            so.FindProperty("totem").objectReferenceValue = totem;
            so.FindProperty("embers").objectReferenceValue = ps;
            SerializedProperty arr = so.FindProperty("runeRenderers");
            arr.arraySize = glow.Count;
            for (int i = 0; i < glow.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = glow[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        MarkDirty();
        Debug.Log("V01: " + index + " totem tapınağı kuruldu");
    }

    // ------------------------------------------------------------------ 3) orman: mantar, eğrelti, ateş böceği

    private class Smooth
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public int Add(Vector3 p, float vCoord) { v.Add(p); uv.Add(new Vector2(0.5f, vCoord)); return v.Count - 1; }
        public void Tri(int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0f) { int tmp = b; b = c; c = tmp; }
            t.Add(a); t.Add(b); t.Add(c);
        }
    }

    private static void AddMushroom(Smooth s, Vector3 basePos, float height, float capR, float capH, float bend)
    {
        const int m = 7;
        Vector3 bendDir = new Vector3(Mathf.Cos(bend), 0f, Mathf.Sin(bend));
        float stemR = Mathf.Max(0.012f, capR * 0.2f);
        int[][] rings = new int[3][];
        Vector3[] centers = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            float ty = k / 2f;
            centers[k] = basePos + Vector3.up * (height * ty - (k == 0 ? 0.02f : 0f)) + bendDir * (height * 0.18f * ty * ty);
            rings[k] = new int[m];
            float r = stemR * Mathf.Lerp(1.25f, 0.8f, ty);
            for (int i = 0; i < m; i++)
            {
                float a = i * Mathf.PI * 2f / m;
                rings[k][i] = s.Add(centers[k] + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), Mathf.Lerp(0.04f, 0.36f, ty));
            }
        }
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < m; i++)
            {
                int n = (i + 1) % m;
                Vector3 outw = s.v[rings[k][i]] - centers[k];
                s.Tri(rings[k][i], rings[k + 1][i], rings[k + 1][n], outw);
                s.Tri(rings[k][i], rings[k + 1][n], rings[k][n], outw);
            }
        // şapka: alt yüz (ışıyan), kubbe
        Vector3 top = centers[2];
        const int cm = 9, layers = 4;
        int[] gillInner = new int[cm], rim = new int[cm];
        for (int i = 0; i < cm; i++)
        {
            float a = i * Mathf.PI * 2f / cm;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            gillInner[i] = s.Add(top + d * stemR * 0.8f, 0.5f);
            rim[i] = s.Add(top + d * capR - Vector3.up * capH * 0.12f, 0.5f);
        }
        for (int i = 0; i < cm; i++)
        {
            int n = (i + 1) % cm;
            s.Tri(gillInner[i], rim[i], rim[n], Vector3.down);
            s.Tri(gillInner[i], rim[n], gillInner[n], Vector3.down);
        }
        int[] prev = new int[cm];
        for (int i = 0; i < cm; i++)
        {
            float a = i * Mathf.PI * 2f / cm;
            prev[i] = s.Add(top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * capR - Vector3.up * capH * 0.12f, 0.66f);
        }
        for (int l = 1; l <= layers; l++)
        {
            float ang = l / (float)(layers + 1) * Mathf.PI * 0.5f;
            int[] cur = new int[cm];
            for (int i = 0; i < cm; i++)
            {
                float a = i * Mathf.PI * 2f / cm;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                cur[i] = s.Add(top + d * (capR * Mathf.Cos(ang)) + Vector3.up * (capH * Mathf.Sin(ang) - capH * 0.12f), Mathf.Lerp(0.66f, 0.98f, l / (float)(layers + 1)));
            }
            for (int i = 0; i < cm; i++)
            {
                int n = (i + 1) % cm;
                Vector3 outw = s.v[prev[i]] - top + Vector3.up * capH;
                s.Tri(prev[i], cur[i], cur[n], outw);
                s.Tri(prev[i], cur[n], prev[n], outw);
            }
            prev = cur;
        }
        int apex = s.Add(top + Vector3.up * (capH * 0.88f), 1f);
        for (int i = 0; i < cm; i++) s.Tri(prev[i], apex, prev[(i + 1) % cm], Vector3.up);
    }

    private static Mesh BuildMushroomCluster(int variant)
    {
        Smooth s = new Smooth();
        int count = RI(3, 7);
        for (int i = 0; i < count; i++)
        {
            float big = i == 0 ? 1f : R(0.35f, 0.8f);
            Vector3 p = i == 0 ? Vector3.zero : new Vector3(R(-0.28f, 0.28f), 0f, R(-0.28f, 0.28f));
            AddMushroom(s, p, R(0.16f, 0.3f) * big, R(0.09f, 0.15f) * big, R(0.06f, 0.1f) * big, R(0f, 6.28f));
        }
        Mesh m = new Mesh { name = "MushroomCluster_" + variant };
        m.SetVertices(s.v); m.SetUVs(0, s.uv); m.SetTriangles(s.t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return StoreMesh(m);
    }

    private static Mesh BuildFern(int variant)
    {
        List<Vector3> v = new List<Vector3>(); List<Vector3> nrm = new List<Vector3>(); List<int> t = new List<int>();
        int fronds = RI(7, 11);
        for (int f = 0; f < fronds; f++)
        {
            float az = f * Mathf.PI * 2f / fronds + R(-0.3f, 0.3f);
            Vector3 dir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)), side = new Vector3(-dir.z, 0f, dir.x);
            float len = R(0.55f, 0.95f), rise = R(0.55f, 0.95f), droop = R(0.7f, 1.05f), width = R(0.1f, 0.16f);
            const int seg = 9;
            Vector3[] spine = new Vector3[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float s = i / (float)seg;
                spine[i] = dir * (len * s) + Vector3.up * (len * (rise * s - droop * s * s) + 0.02f);
            }
            for (int i = 0; i < seg; i++)
            {
                float s = (i + 0.5f) / seg;
                float w = width * Mathf.Sin(Mathf.PI * Mathf.Pow(s, 0.75f)) + 0.012f;
                Vector3 fwd = (spine[i + 1] - spine[i]);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 tip = spine[i] + fwd * 1.5f + side * (w * sgn) - Vector3.up * (w * 0.35f);
                    int b = v.Count;
                    v.Add(spine[i]); v.Add(spine[i + 1]); v.Add(tip);
                    Vector3 fn = Vector3.Cross(spine[i + 1] - spine[i], tip - spine[i]).normalized;
                    if (fn.y < 0f) fn = -fn;
                    Vector3 n = (Vector3.up * 0.65f + fn * 0.35f).normalized;
                    nrm.Add(n); nrm.Add(n); nrm.Add(n);
                    t.Add(b); t.Add(b + 1); t.Add(b + 2);
                }
            }
        }
        Mesh m = new Mesh { name = "Fern_" + variant };
        m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(t, 0);
        m.uv = new Vector2[v.Count];
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    private static Texture2D BuildMushroomRamp(bool emission)
    {
        Texture2D tex = new Texture2D(4, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++)
        {
            float v = (y + 0.5f) / 64f;
            Color c;
            if (!emission)
            {
                if (v < 0.42f) c = Color.Lerp(new Color(0.2f, 0.24f, 0.27f), new Color(0.38f, 0.5f, 0.55f), v / 0.42f);
                else if (v < 0.6f) c = new Color(0.55f, 0.85f, 0.92f);
                else c = Color.Lerp(new Color(0.16f, 0.36f, 0.44f), new Color(0.07f, 0.17f, 0.24f), (v - 0.6f) / 0.4f);
            }
            else
            {
                if (v < 0.42f) c = Color.Lerp(new Color(0.01f, 0.03f, 0.04f), new Color(0.06f, 0.2f, 0.26f), v / 0.42f);
                else if (v < 0.6f) c = new Color(0.32f, 0.92f, 1f);
                else c = Color.Lerp(new Color(0.1f, 0.34f, 0.42f), new Color(0.02f, 0.08f, 0.11f), (v - 0.6f) / 0.4f);
            }
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return SaveTex(tex, emission ? "T_Mushroom_E" : "T_Mushroom", true, false);
    }

    private static bool Occupied(Vector3 p, float radius)
    {
        foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 0.6f, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.name.StartsWith("Ground") || c.name == "Sea") continue;
            return true;
        }
        return false;
    }

    [MenuItem("Tools/Son Hasat/V-01 Orman (mantar, eğrelti, ateş böceği)")]
    public static void BuildForestLife()
    {
        rng = new System.Random(7741);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material mush = Mat("Mat_GlowMushroom", lit);
        mush.SetTexture("_BaseMap", BuildMushroomRamp(false));
        mush.SetTexture("_EmissionMap", BuildMushroomRamp(true));
        mush.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f));
        mush.SetColor("_EmissionColor", new Color(1.3f, 1.3f, 1.3f));
        mush.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mush.EnableKeyword("_EMISSION");
        mush.SetFloat("_Smoothness", 0.35f);
        Material fern = Mat("Mat_Fern", lit);
        fern.SetColor("_BaseColor", new Color(0.09f, 0.2f, 0.08f));
        fern.SetFloat("_Smoothness", 0.12f);
        fern.SetFloat("_Cull", 0f);
        fern.doubleSidedGI = true;
        Material fly = AdditiveParticle("Mat_Firefly", new Color(3.4f, 2.5f, 0.7f, 1f));

        Mesh[] mushMeshes = new Mesh[4], fernMeshes = new Mesh[4];
        for (int i = 0; i < 4; i++) { mushMeshes[i] = BuildMushroomCluster(i); fernMeshes[i] = BuildFern(i); }

        Transform decor = GameObject.Find("EnvDecor").transform;
        DestroyChild(decor, "Mushrooms"); DestroyChild(decor, "Ferns"); DestroyChild(decor, "Fireflies");
        Transform mushRoot = Child(decor, "Mushrooms").transform, fernRoot = Child(decor, "Ferns").transform, flyRoot = Child(decor, "Fireflies").transform;
        Physics.SyncTransforms();

        Totem[] totems = Object.FindObjectsByType<Totem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        DiseaseFlower[] flowers = Object.FindObjectsByType<DiseaseFlower>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Func<Vector3, float, bool> nearTotem = (p, d) => { foreach (Totem t in totems) if ((t.transform.position - p).sqrMagnitude < d * d) return true; return false; };
        System.Func<Vector3, float, bool> nearFlower = (p, d) => { foreach (DiseaseFlower f in flowers) if ((f.transform.position - p).sqrMagnitude < d * d) return true; return false; };

        Transform forest = GameObject.Find("Map/Forest").transform;
        int mushCount = 0, fernCount = 0;
        foreach (Transform tree in forest)
        {
            Vector3 c = tree.position; c.y = 0f;
            if (rng.NextDouble() < 0.6)
            {
                int n = RI(1, 4);
                float baseAng = R(0f, 6.28f);
                for (int i = 0; i < n; i++)
                {
                    float a = baseAng + i * R(0.5f, 1.1f);
                    // ağaç modelinin kökleri ~3.3 m'ye uzanır: mantarlar kök uçlarında
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R(2.7f, 3.9f) * tree.localScale.x);
                    if (nearTotem(p, 3.8f)) continue;
                    GameObject go = Child(mushRoot, "Mushrooms_" + (++mushCount));
                    go.transform.position = p;
                    go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                    go.transform.localScale = Vector3.one * R(1.5f, 2.9f);
                    AddMesh(go, mushMeshes[RI(0, 4)], mush, false);
                }
            }
            if (rng.NextDouble() < 0.7)
            {
                int n = RI(1, 4);
                for (int i = 0; i < n; i++)
                {
                    float a = R(0f, 6.28f);
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R(3.2f, 5.4f) * tree.localScale.x);
                    if (nearTotem(p, 4.2f) || Occupied(p, 0.3f)) continue;
                    GameObject go = Child(fernRoot, "Fern_" + (++fernCount));
                    go.transform.position = p;
                    go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                    go.transform.localScale = Vector3.one * R(0.8f, 1.5f);
                    AddMesh(go, fernMeshes[RI(0, 4)], fern, false);
                }
            }
        }
        // açıklıklara dağınık eğreltiler
        for (int tries = 0; tries < 600 && fernCount < 300; tries++)
        {
            Vector3 p = new Vector3(R(-86f, 86f), 0f, R(-58f, 76f));
            if (nearTotem(p, 4.2f) || Occupied(p, 0.4f)) continue;
            GameObject go = Child(fernRoot, "Fern_" + (++fernCount));
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * R(0.7f, 1.3f);
            AddMesh(go, fernMeshes[RI(0, 4)], fern, false);
        }

        // ateş böcekleri: hastalık çiçeklerinden uzak 18 nokta
        int flies = 0;
        for (int tries = 0; tries < 400 && flies < 18; tries++)
        {
            Vector3 p = new Vector3(R(-78f, 78f), 1.1f, R(-66f, 68f));
            if (nearFlower(p, 11f)) continue;
            bool close = false;
            foreach (Transform o in flyRoot) if ((o.position - p).sqrMagnitude < 20f * 20f) { close = true; break; }
            if (close) continue;
            GameObject go = Child(flyRoot, "Fireflies_" + (++flies));
            go.transform.position = p;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true; main.playOnAwake = true; main.prewarm = true; main.maxParticles = 16;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(0.9f, 1f, 0.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule em = ps.emission; em.rateOverTime = 1.5f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(15f, 1.8f, 15f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true; noise.strength = 0.45f; noise.frequency = 0.35f; noise.scrollSpeed = 0.25f;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.05f, 0.3f), new GradientAlphaKey(1f, 0.48f),
                        new GradientAlphaKey(0.05f, 0.66f), new GradientAlphaKey(1f, 0.84f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = fly; pr.shadowCastingMode = ShadowCastingMode.Off;
        }
        MarkDirty();
        Debug.Log("V01: orman — " + mushCount + " mantar kümesi, " + fernCount + " eğrelti, " + flies + " ateş böceği alanı");
    }

    // ------------------------------------------------------------------ V-02) zemin + kıyı

    // ---- prosedürel toprak dokusu (kenarları dikişsiz) ----

    private static float Hash2(int x, int y, int seed)
    {
        unchecked
        {
            uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            n = (n ^ (n >> 13)) * 1274126177u;
            n ^= n >> 16;
            return (n & 0xFFFFFF) / 16777215f;
        }
    }

    private static float PNoise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
        return Mathf.Lerp(Mathf.Lerp(Hash2(x0, y0, seed), Hash2(x1, y0, seed), fx), Mathf.Lerp(Hash2(x0, y1, seed), Hash2(x1, y1, seed), fx), fy);
    }

    private static float PFbm(float u, float v, int period, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * PNoise(u * period, v * period, period, seed + i * 17);
            norm += amp; amp *= 0.5f; period *= 2;
        }
        return sum / norm;
    }

    /// <summary>Döndürülmüş elips/yaprak damgası; doku kenarından taşan kısım karşı kenardan devam eder.</summary>
    private static void Stamp(Color[] col, float[] hgt, int size, float cx, float cy, float len, float wid, float ang, Color c, float height, int shape)
    {
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        int rad = Mathf.CeilToInt(Mathf.Max(len, wid)) + 2;
        for (int dy = -rad; dy <= rad; dy++)
            for (int dx = -rad; dx <= rad; dx++)
            {
                float a = (dx * ca + dy * sa) / len, b = (-dx * sa + dy * ca) / wid;
                float alpha, dome;
                if (shape == 0)                       // çakıl: elips kubbe
                {
                    float d = a * a + b * b;
                    if (d >= 1f) continue;
                    dome = Mathf.Sqrt(1f - d);
                    alpha = Mathf.Clamp01((1f - d) * 6f);
                }
                else if (shape == 1)                  // yaprak: iki ucu sivri
                {
                    if (Mathf.Abs(a) >= 1f) continue;
                    float lim = 1f - a * a;
                    float tEdge = 1f - Mathf.Abs(b) / lim;
                    if (tEdge <= 0f) continue;
                    alpha = Mathf.Clamp01(tEdge * 5f);
                    dome = 0.5f + 0.5f * tEdge;
                    if (Mathf.Abs(b) < 0.1f) dome *= 0.6f;   // orta damar
                }
                else                                  // dal: ince çubuk
                {
                    if (Mathf.Abs(a) >= 1f || Mathf.Abs(b) >= 1f) continue;
                    alpha = Mathf.Clamp01((1f - Mathf.Abs(b)) * 3f) * Mathf.Clamp01((1f - Mathf.Abs(a)) * 12f);
                    dome = Mathf.Sqrt(1f - b * b);
                }
                int px = (((int)cx + dx) % size + size) % size, py = (((int)cy + dy) % size + size) % size;
                int idx = py * size + px;
                float shade = shape == 1 && Mathf.Abs(b) < 0.1f ? 0.7f : 0.85f + 0.15f * dome;
                col[idx] = Color.Lerp(col[idx], c * shade, alpha);
                hgt[idx] = Mathf.Lerp(hgt[idx], hgt[idx] * 0.4f + height * dome, alpha);
            }
    }

    private static void ImportTex(string path, bool normal, bool clamp)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 8;
        imp.maxTextureSize = 1024;
        imp.SaveAndReimport();
    }

    [MenuItem("Tools/Son Hasat/V-02 Toprak dokusu üret")]
    public static void BuildSoilTextures()
    {
        EnsureDir();
        rng = new System.Random(4411);
        const int S = 1024;
        Color[] col = new Color[S * S];
        float[] hgt = new float[S * S];
        Color dark = new Color(0.15f, 0.105f, 0.075f), mid = new Color(0.27f, 0.195f, 0.135f), pale = new Color(0.36f, 0.28f, 0.2f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float big = PFbm(u, v, 4, 5, 11), med = PFbm(u, v, 16, 4, 53), grain = PNoise(u * 256f, v * 256f, 256, 97), grit = PNoise(u * 512f, v * 512f, 512, 131);
                float tone = Mathf.Clamp01(big * 0.55f + med * 0.45f);
                Color c = Color.Lerp(dark, mid, Mathf.SmoothStep(0.25f, 0.75f, tone));
                c = Color.Lerp(c, pale, Mathf.SmoothStep(0.62f, 0.9f, med) * 0.5f);
                c *= 0.86f + 0.28f * grain;
                if (grit > 0.9f) c = Color.Lerp(c, pale, (grit - 0.9f) * 5f);               // açık kum taneleri
                if (grit < 0.1f) c *= 0.75f;                                                 // koyu gözenekler
                c.a = 1f;
                col[y * S + x] = c;
                hgt[y * S + x] = med * 0.35f + grain * 0.2f + grit * 0.08f;
            }
        // dallar
        for (int i = 0; i < 70; i++)
        {
            Color c = Color.Lerp(new Color(0.2f, 0.13f, 0.08f), new Color(0.34f, 0.25f, 0.17f), (float)rng.NextDouble());
            Stamp(col, hgt, S, R(0f, S), R(0f, S), R(18f, 62f), R(1.4f, 2.6f), R(0f, Mathf.PI), c, 0.75f, 2);
        }
        // çakıllar
        for (int i = 0; i < 260; i++)
        {
            float g = R(0.17f, 0.33f);   // koyu tut: açık çakıllar karanlıkta beyaz benek gibi parlıyordu
            Color c = new Color(g * R(0.95f, 1.1f), g * R(0.9f, 1f), g * R(0.78f, 0.95f));
            float r = R(2.5f, 9f) * (rng.NextDouble() < 0.08 ? 1.8f : 1f);
            Stamp(col, hgt, S, R(0f, S), R(0f, S), r, r * R(0.6f, 1f), R(0f, Mathf.PI), c, 0.6f, 0);
        }
        // dökülmüş yapraklar (soluk, çürümüş tonlar)
        Color[] leafCols = { new Color(0.36f, 0.25f, 0.11f), new Color(0.3f, 0.17f, 0.09f), new Color(0.22f, 0.16f, 0.09f), new Color(0.4f, 0.31f, 0.14f), new Color(0.19f, 0.2f, 0.1f) };
        for (int i = 0; i < 300; i++)
        {
            float len = R(7f, 15f);
            Stamp(col, hgt, S, R(0f, S), R(0f, S), len, len * R(0.38f, 0.55f), R(0f, Mathf.PI * 2f), leafCols[RI(0, leafCols.Length)] * R(0.8f, 1.15f), 0.55f, 1);
        }
        // yükseklikten hafif gölge (çukurlar koyu) + normal haritası
        Color[] nrm = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int xl = (x + S - 1) % S, xr = (x + 1) % S, yd = (y + S - 1) % S, yu = (y + 1) % S;
                float dx = hgt[y * S + xr] - hgt[y * S + xl], dy = hgt[yu * S + x] - hgt[yd * S + x];
                Vector3 n = new Vector3(-dx * 2.4f, -dy * 2.4f, 1f).normalized;
                nrm[y * S + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                float ao = 0.8f + 0.4f * Mathf.Clamp01(hgt[y * S + x]);
                Color c = col[y * S + x] * ao; c.a = 1f;
                col[y * S + x] = c;
            }
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(col); tex.Apply();
        File.WriteAllBytes(Dir + "/T_Soil.png", tex.EncodeToPNG());
        tex.SetPixels(nrm); tex.Apply();
        File.WriteAllBytes(Dir + "/T_Soil_N.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        // yosun: kabarık, benekli yeşil
        const int M = 512;
        Color[] moss = new Color[M * M];
        for (int y = 0; y < M; y++)
            for (int x = 0; x < M; x++)
            {
                float u = x / (float)M, v = y / (float)M;
                float a = PFbm(u, v, 8, 4, 201), b = PNoise(u * 128f, v * 128f, 128, 233), d = PNoise(u * 256f, v * 256f, 256, 251);
                Color c = Color.Lerp(new Color(0.09f, 0.16f, 0.06f), new Color(0.19f, 0.29f, 0.09f), Mathf.SmoothStep(0.3f, 0.8f, a * 0.6f + b * 0.4f));
                c *= 0.8f + 0.4f * d;
                if (d > 0.9f) c = Color.Lerp(c, new Color(0.33f, 0.4f, 0.14f), 0.6f);
                c.a = 1f;
                moss[y * M + x] = c;
            }
        Texture2D mt = new Texture2D(M, M, TextureFormat.RGBA32, false);
        mt.SetPixels(moss); mt.Apply();
        File.WriteAllBytes(Dir + "/T_Moss.png", mt.EncodeToPNG());
        Object.DestroyImmediate(mt);

        ImportTex(Dir + "/T_Soil.png", false, false);
        ImportTex(Dir + "/T_Soil_N.png", true, false);
        ImportTex(Dir + "/T_Moss.png", false, false);
    }

    [MenuItem("Tools/Son Hasat/V-02 Zemin materyali")]
    public static void BuildGround()
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Soil.png") == null) BuildSoilTextures();
        Material src = FindMat("Mat_Map_ForestGround"), sand = FindMat("Mat_Map_Sand");
        Material m = Mat("Mat_Ground_Island", Shader.Find("Goblin/GroundBlend"));
        foreach (string tex in new[] { "_BaseMap", "_BumpMap", "_MacroMap" }) m.SetTexture(tex, src.GetTexture(tex));
        foreach (string f in new[] { "_BumpScale", "_Tiling", "_MacroScale", "_MacroStrength", "_Smoothness", "_BlendSharpness" }) m.SetFloat(f, src.GetFloat(f));
        m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        m.SetTexture("_SandMap", sand.GetTexture("_BaseMap"));
        m.SetTexture("_SandBump", sand.GetTexture("_BumpMap"));
        m.SetColor("_SandColor", new Color(0.92f, 0.88f, 0.8f));
        // V-02 toprak: yeni üretilmiş doku (çakıl, dal, yaprak) + ayrı yosun katmanı
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Soil.png"));
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Soil_N.png"));
        m.SetTexture("_MossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Moss.png"));
        m.SetFloat("_Tiling", 3.2f);
        m.SetFloat("_BumpScale", 1.1f);
        m.SetFloat("_Smoothness", 0.06f);
        m.SetColor("_BaseColor", new Color(0.86f, 0.86f, 0.86f));
        EditorUtility.SetDirty(m);
        int count = 0;
        foreach (Renderer r in GameObject.Find("Map/Ground").GetComponentsInChildren<Renderer>())
            if (r.sharedMaterial == src || r.sharedMaterial == m) { r.sharedMaterial = m; count++; }
        MarkDirty();
        Debug.Log("V02: zemin materyali " + count + " kutuya atandı");
    }

    private static void RingMesh(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3[] centers, float[] radii, int sides, float seed, float uvPerMeter)
    {
        int start = v.Count;
        float dist = 0f;
        for (int r = 0; r < centers.Length; r++)
        {
            if (r > 0) dist += (centers[r] - centers[r - 1]).magnitude;
            Vector3 axis = (centers[Mathf.Min(r + 1, centers.Length - 1)] - centers[Mathf.Max(r - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, axis).normalized, up = Vector3.Cross(axis, side);
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                Vector3 d = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                float knob = 0.82f + 0.36f * N3(new Vector3(Mathf.Cos(a) * 1.7f, Mathf.Sin(a) * 1.7f, r * 0.6f + seed));
                v.Add(centers[r] + d * radii[r] * knob);
                uv.Add(new Vector2(i / (float)sides, dist * uvPerMeter));
            }
        }
        int stride = sides + 1;
        for (int r = 0; r < centers.Length - 1; r++)
            for (int i = 0; i < sides; i++)
            {
                int a = start + r * stride + i, b = a + 1, c = a + stride, d = c + 1;
                Vector3 outw = v[a] - centers[r];
                if (Vector3.Dot(Vector3.Cross(v[c] - v[a], v[d] - v[a]), outw) < 0f) { t.Add(a); t.Add(d); t.Add(c); t.Add(a); t.Add(b); t.Add(d); }
                else { t.Add(a); t.Add(c); t.Add(d); t.Add(a); t.Add(d); t.Add(b); }
            }
        // uç kapakları
        for (int end = 0; end < 2; end++)
        {
            int r = end == 0 ? 0 : centers.Length - 1;
            Vector3 outw = end == 0 ? centers[0] - centers[1] : centers[r] - centers[r - 1];
            int ci = v.Count;
            v.Add(centers[r] + outw.normalized * radii[r] * 0.25f); uv.Add(new Vector2(0.5f, 0.5f));
            int ringStart = v.Count;
            for (int i = 0; i <= sides; i++) { v.Add(v[start + r * stride + i]); uv.Add(new Vector2(0.5f + 0.4f * Mathf.Cos(i * 6.283f / sides), 0.5f + 0.4f * Mathf.Sin(i * 6.283f / sides))); }
            for (int i = 0; i < sides; i++)
            {
                int a = ringStart + i, b = ringStart + i + 1;
                if (Vector3.Dot(Vector3.Cross(v[a] - v[ci], v[b] - v[ci]), outw) < 0f) { t.Add(ci); t.Add(b); t.Add(a); }
                else { t.Add(ci); t.Add(a); t.Add(b); }
            }
        }
    }

    /// <summary>Dalga odunu: +Z boyunca uzanan, hafif eğri, bir-iki kırık dallı kütük (birim boy ~1 m; ölçekle büyütülür).</summary>
    private static Mesh BuildDriftwood(int variant)
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        const int rings = 9;
        Vector3[] c = new Vector3[rings]; float[] rad = new float[rings];
        float bend = R(-0.09f, 0.09f), lift = R(0f, 0.05f);
        for (int r = 0; r < rings; r++)
        {
            float s = r / (float)(rings - 1);
            c[r] = new Vector3(bend * Mathf.Sin(Mathf.PI * s), lift * s * s, s - 0.5f);
            rad[r] = Mathf.Lerp(0.075f, 0.045f, s) * (r == 0 || r == rings - 1 ? 0.8f : 1f);
        }
        RingMesh(v, uv, t, c, rad, 8, variant * 3.1f, 2f);
        int branches = RI(1, 3);
        for (int b = 0; b < branches; b++)
        {
            float s = R(0.25f, 0.75f);
            Vector3 origin = new Vector3(bend * Mathf.Sin(Mathf.PI * s), lift * s * s, s - 0.5f);
            Vector3 dir = new Vector3(R(-1f, 1f), R(0.25f, 0.9f), R(-0.3f, 0.5f)).normalized;
            float len = R(0.14f, 0.3f);
            Vector3[] bc = new Vector3[4]; float[] br = new float[4];
            for (int k = 0; k < 4; k++) { bc[k] = origin + dir * (len * k / 3f) + Vector3.up * (0.02f * k * k / 9f); br[k] = Mathf.Lerp(0.03f, 0.012f, k / 3f); }
            RingMesh(v, uv, t, bc, br, 5, variant + b * 7.7f, 2f);
        }
        Mesh m = new Mesh { name = "Driftwood_" + variant };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return StoreMesh(m);
    }

    private static Mesh BuildKelp(int variant)
    {
        List<Vector3> v = new List<Vector3>(); List<Vector3> nrm = new List<Vector3>(); List<int> t = new List<int>();
        int strips = RI(5, 9);
        for (int s = 0; s < strips; s++)
        {
            float az = R(0f, 6.28f), len = R(0.5f, 1.3f), width = R(0.05f, 0.1f), wig = R(0.06f, 0.16f), ph = R(0f, 6.28f);
            Vector3 dir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)), side = new Vector3(-dir.z, 0f, dir.x);
            const int seg = 9;
            for (int i = 0; i <= seg; i++)
            {
                float k = i / (float)seg;
                Vector3 p = dir * (len * k) + side * (wig * Mathf.Sin(k * 7f + ph)) + Vector3.up * (0.012f + 0.03f * Mathf.Abs(Mathf.Sin(k * 5f + ph)) + s * 0.004f);
                float w = width * (0.35f + 0.65f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.15f)));
                v.Add(p - side * w); v.Add(p + side * w);
                nrm.Add(Vector3.up); nrm.Add(Vector3.up);
                if (i == seg) continue;
                int b = v.Count - 2;
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
            }
        }
        Mesh m = new Mesh { name = "Kelp_" + variant };
        m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(t, 0);
        m.uv = new Vector2[v.Count];
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    private static Mesh BuildPebbles(int variant)
    {
        Flat f = new Flat();
        int count = RI(10, 19);
        for (int n = 0; n < count; n++)
        {
            Vector3 c = new Vector3(R(-0.8f, 0.8f), 0f, R(-0.8f, 0.8f));
            float rx = R(0.035f, 0.11f), rz = rx * R(0.7f, 1.3f), ry = rx * R(0.35f, 0.6f);
            c.y = ry * 0.45f;
            float seed = R(0f, 40f), yaw = R(0f, 6.28f);
            const int lon = 6, lat = 4;
            Vector3[,] pt = new Vector3[lat + 1, lon];
            for (int a = 0; a <= lat; a++)
                for (int b = 0; b < lon; b++)
                {
                    float th = a / (float)lat * Mathf.PI, ph = b * Mathf.PI * 2f / lon + yaw;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float k = 0.85f + 0.3f * N3(d * 1.6f + Vector3.one * seed);
                    pt[a, b] = c + new Vector3(d.x * rx, d.y * ry, d.z * rz) * k;
                }
            for (int a = 0; a < lat; a++)
                for (int b = 0; b < lon; b++)
                {
                    int nb = (b + 1) % lon;
                    Vector3 outw = (pt[a, b] + pt[a + 1, nb]) * 0.5f - c;
                    if (a > 0) f.Tri(pt[a, b], pt[a, nb], pt[a + 1, nb], outw);
                    if (a < lat - 1) f.Tri(pt[a, b], pt[a + 1, nb], pt[a + 1, b], outw);
                }
        }
        return StoreMesh(f.Build("Pebbles_" + variant));
    }

    private static Mesh BuildBeachGrass(int variant)
    {
        List<Vector3> v = new List<Vector3>(); List<Vector3> nrm = new List<Vector3>(); List<int> t = new List<int>();
        int blades = RI(14, 24);
        for (int b = 0; b < blades; b++)
        {
            float az = R(0f, 6.28f);
            Vector3 lean = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)), side = new Vector3(-lean.z, 0f, lean.x);
            Vector3 root = lean * R(0f, 0.16f) + side * R(-0.1f, 0.1f);
            float h = R(0.45f, 1.0f), bend = R(0.15f, 0.55f), width = R(0.012f, 0.022f);
            const int seg = 4;
            Vector3 n = (Vector3.up * 0.6f + lean * 0.4f).normalized;
            for (int i = 0; i <= seg; i++)
            {
                float k = i / (float)seg;
                Vector3 p = root + Vector3.up * (h * k * (1f - 0.25f * k * bend)) + lean * (h * bend * k * k);
                float w = width * (1f - k * 0.9f);
                v.Add(p - side * w); v.Add(p + side * w);
                nrm.Add(n); nrm.Add(n);
                if (i == seg) continue;
                int q = v.Count - 2;
                t.Add(q); t.Add(q + 2); t.Add(q + 1); t.Add(q + 1); t.Add(q + 2); t.Add(q + 3);
            }
        }
        Mesh m = new Mesh { name = "BeachGrass_" + variant };
        m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(t, 0);
        m.uv = new Vector2[v.Count];
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    [MenuItem("Tools/Son Hasat/V-02 Kıyı süsleri")]
    public static void BuildShore()
    {
        rng = new System.Random(2202);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material wood = Mat("Mat_Driftwood", lit);
        Material oldWood = FindMat("Mat_OldWood");
        if (oldWood != null) wood.CopyPropertiesFromMaterial(oldWood);
        wood.SetColor("_BaseColor", new Color(0.58f, 0.64f, 0.72f));   // ahşap dokusunun turuncusunu griye çeker
        wood.SetFloat("_Smoothness", 0.25f);
        Material kelp = Mat("Mat_Kelp", lit);
        kelp.SetColor("_BaseColor", new Color(0.05f, 0.07f, 0.035f));
        kelp.SetFloat("_Smoothness", 0.78f);
        kelp.SetFloat("_Cull", 0f);
        Material cliff = FindMat("Mat_Map_Cliff");
        Material pebble = Mat("Mat_Pebble", cliff.shader);
        pebble.CopyPropertiesFromMaterial(cliff);
        pebble.SetColor("_BaseColor", new Color(0.78f, 0.75f, 0.7f));
        pebble.SetFloat("_Tiling", 1.1f);
        pebble.SetFloat("_Smoothness", 0.4f);
        Material grass = Mat("Mat_BeachGrass", lit);
        grass.SetColor("_BaseColor", new Color(0.3f, 0.31f, 0.15f));
        grass.SetFloat("_Smoothness", 0.1f);
        grass.SetFloat("_Cull", 0f);
        foreach (Material m in new[] { wood, kelp, pebble, grass }) EditorUtility.SetDirty(m);

        Mesh[] logs = new Mesh[4], kelps = new Mesh[4], pebbles = new Mesh[4], grasses = new Mesh[4];
        for (int i = 0; i < 4; i++) { logs[i] = BuildDriftwood(i); kelps[i] = BuildKelp(i); pebbles[i] = BuildPebbles(i); grasses[i] = BuildBeachGrass(i); }

        Transform decor = GameObject.Find("EnvDecor").transform;
        DestroyChild(decor, "Shore");
        Transform root = Child(decor, "Shore").transform;
        Physics.SyncTransforms();
        // iskele, harita panosu, not ve doğma noktaları çevresi boş kalır
        System.Func<Vector3, float, bool> bad = (p, r) => (p.x > 2f && p.x < 20f && p.z < -79f) || Occupied(p, r);

        int nLog = 0, nKelp = 0, nPeb = 0, nGrass = 0;
        for (int tries = 0; tries < 200 && nLog < 11; tries++)
        {
            Vector3 p = new Vector3(R(-88f, 88f), 0f, R(-92f, -77f));
            if (bad(p, 1.2f)) continue;
            float len = R(2f, 4.6f), thick = R(1.6f, 2.8f);
            GameObject go = Child(root, "Driftwood_" + (++nLog));
            go.transform.position = p + Vector3.up * (0.03f * thick);
            go.transform.rotation = Quaternion.Euler(R(-3f, 3f), 90f + R(-40f, 40f), R(0f, 360f));
            go.transform.localScale = new Vector3(thick, thick, len);
            AddMesh(go, logs[RI(0, 4)], wood, true);
        }
        for (int tries = 0; tries < 300 && nKelp < 36; tries++)
        {
            Vector3 p = new Vector3(R(-90f, 90f), 0f, R(-93.5f, -85f));
            if (bad(p, 0.4f)) continue;
            GameObject go = Child(root, "Kelp_" + (++nKelp));
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * R(0.8f, 1.5f);
            AddMesh(go, kelps[RI(0, 4)], kelp, false);
        }
        for (int tries = 0; tries < 400 && nPeb < 50; tries++)
        {
            Vector3 p = new Vector3(R(-90f, 90f), 0f, R(-93.5f, -70f));
            if (bad(p, 0.5f)) continue;
            GameObject go = Child(root, "Pebbles_" + (++nPeb));
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * R(0.8f, 1.6f);
            AddMesh(go, pebbles[RI(0, 4)], pebble, false);
        }
        for (int tries = 0; tries < 700 && nGrass < 120; tries++)
        {
            // çoğu kum–orman geçişinde, bir kısmı kumun içinde
            Vector3 p = new Vector3(R(-90f, 90f), 0f, nGrass < 90 ? R(-76f, -64f) : R(-88f, -76f));
            if (bad(p, 0.3f)) continue;
            GameObject go = Child(root, "BeachGrass_" + (++nGrass));
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * R(0.8f, 1.4f);
            AddMesh(go, grasses[RI(0, 4)], grass, false);
        }
        MarkDirty();
        Debug.Log("V02: kıyı — " + nLog + " dalga odunu, " + nKelp + " yosun, " + nPeb + " çakıl öbeği, " + nGrass + " kumul otu");
    }

    // ------------------------------------------------------------------ V-03) giriş sinematiği

    private static void AddBox(Flat f, Vector3 c, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        Vector3[] p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
        f.Quad(p[0], p[1], p[2], p[3], Vector3.back); f.Quad(p[4], p[5], p[6], p[7], Vector3.forward);
        f.Quad(p[0], p[2], p[4], p[6], Vector3.left); f.Quad(p[1], p[3], p[5], p[7], Vector3.right);
        f.Quad(p[0], p[1], p[4], p[5], Vector3.down); f.Quad(p[2], p[3], p[6], p[7], Vector3.up);
    }

    private static Material AlphaParticle(string name, Color color)
    {
        Material m = AdditiveParticle(name, color);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.DisableKeyword("_BLENDMODE_ADD");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        EditorUtility.SetDirty(m);
        return m;
    }

    [MenuItem("Tools/Son Hasat/V-03 Giriş sinematiği")]
    public static void BuildIntro()
    {
        rng = new System.Random(3303);
        Transform props = GameObject.Find("IntroCinematic").transform.Find("IntroProps");
        Transform boat = props.Find("BoatRig/Boat");
        foreach (string n in new[] { "V03_Lantern", "V03_Wake", "V03_Splash" }) DestroyChild(boat, n);
        DestroyChild(props, "V03_SeaMist");

        // tekne gövdesinin yerel sınırları (pruva = +Z)
        Bounds hull = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (MeshFilter mf in boat.Find("BoatModel").GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = boat.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!any) { hull = new Bounds(p, Vector3.zero); any = true; } else hull.Encapsulate(p);
            }
        }
        float bowZ = hull.max.z, sternZ = hull.min.z, rimY = hull.max.y;

        // KULLANICI (2026-11-10): teknedeki fener ve ışığı İSTENMİYOR ("çok saçma") → üretilmiyor. Geri eklenmesin.
        /*
        Flat frame = new Flat();
        AddBox(frame, new Vector3(0f, 0.55f, 0f), new Vector3(0.05f, 1.1f, 0.05f));            // direk
        AddBox(frame, new Vector3(0f, 1.08f, 0.14f), new Vector3(0.04f, 0.04f, 0.32f));         // kol
        AddBox(frame, new Vector3(0f, 0.98f, 0.27f), new Vector3(0.015f, 0.16f, 0.015f));       // askı
        AddBox(frame, new Vector3(0f, 0.9f, 0.27f), new Vector3(0.17f, 0.025f, 0.17f));         // üst kapak
        AddBox(frame, new Vector3(0f, 0.66f, 0.27f), new Vector3(0.17f, 0.025f, 0.17f));        // alt kapak
        for (int i = 0; i < 4; i++)
            AddBox(frame, new Vector3((i & 1) == 0 ? -0.075f : 0.075f, 0.78f, 0.27f + ((i & 2) == 0 ? -0.075f : 0.075f)), new Vector3(0.016f, 0.22f, 0.016f));
        Mesh frameMesh = StoreMesh(frame.Build("IntroLantern_Frame"));
        Flat glass = new Flat();
        AddBox(glass, new Vector3(0f, 0.78f, 0.27f), new Vector3(0.12f, 0.2f, 0.12f));
        Mesh glassMesh = StoreMesh(glass.Build("IntroLantern_Glass"));

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material iron = Mat("Mat_LanternIron", lit);
        iron.SetColor("_BaseColor", new Color(0.09f, 0.075f, 0.065f));
        iron.SetFloat("_Smoothness", 0.35f);
        Material glow = Mat("Mat_LanternGlow", lit);
        glow.SetColor("_BaseColor", new Color(1f, 0.75f, 0.4f));
        glow.SetColor("_EmissionColor", new Color(2.4f, 1.15f, 0.3f));
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        glow.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(iron); EditorUtility.SetDirty(glow);

        GameObject lantern = Child(boat, "V03_Lantern");
        lantern.transform.localPosition = new Vector3(0f, rimY - 0.2f, bowZ - 0.45f);
        lantern.transform.localScale = Vector3.one * 0.72f;
        AddMesh(lantern, frameMesh, iron, true);
        GameObject glassGo = Child(lantern.transform, "Glass");
        MeshRenderer glassR = AddMesh(glassGo, glassMesh, glow, false);
        GameObject lightGo = Child(lantern.transform, "Light");
        lightGo.transform.localPosition = new Vector3(0f, 0.78f, 0.27f);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.36f); l.intensity = 5.5f; l.range = 8f; l.shadows = LightShadows.Soft;
        l.shadowStrength = 0.85f;
        */

        // ---- köpük izi (kıçtan, dünya uzayında kalır)
        Material foam = AlphaParticle("Mat_SeaFoam", new Color(0.75f, 0.85f, 0.95f, 0.3f));
        GameObject wake = Child(boat, "V03_Wake");
        wake.transform.localPosition = new Vector3(0f, 0.22f, sternZ + 0.2f);
        ParticleSystem wp = wake.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule wm = wp.main;
        wm.loop = true; wm.playOnAwake = true; wm.maxParticles = 160;
        wm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
        wm.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.15f);
        wm.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        wm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        wm.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule we = wp.emission; we.rateOverTime = 1.5f; we.rateOverDistance = 5f;
        ParticleSystem.ShapeModule ws = wp.shape; ws.shapeType = ParticleSystemShapeType.Box; ws.scale = new Vector3(1.1f, 0.02f, 0.3f);
        ParticleSystem.SizeOverLifetimeModule wsz = wp.sizeOverLifetime; wsz.enabled = true;
        wsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
        ParticleSystem.ColorOverLifetimeModule wc = wp.colorOverLifetime; wc.enabled = true;
        Gradient wg = new Gradient();
        wg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        wc.color = new ParticleSystem.MinMaxGradient(wg);
        ParticleSystemRenderer wr = wake.GetComponent<ParticleSystemRenderer>();
        wr.sharedMaterial = foam; wr.renderMode = ParticleSystemRenderMode.HorizontalBillboard; wr.shadowCastingMode = ShadowCastingMode.Off;

        // ---- çarpma sıçraması (pruvadan, bir kez)
        Material spray = AlphaParticle("Mat_SeaSpray", new Color(0.8f, 0.9f, 1f, 0.55f));
        GameObject splash = Child(boat, "V03_Splash");
        splash.transform.localPosition = new Vector3(0f, 0.1f, bowZ - 0.2f);
        splash.transform.localRotation = Quaternion.Euler(-70f, 0f, 0f);
        ParticleSystem sp = splash.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule sm = sp.main;
        sm.loop = false; sm.playOnAwake = false; sm.maxParticles = 140; sm.duration = 1f;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7.5f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.32f);
        sm.gravityModifier = 1.3f;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule se = sp.emission; se.rateOverTime = 0f;
        se.SetBursts(new[] { new ParticleSystem.Burst(0f, 90), new ParticleSystem.Burst(0.12f, 40) });
        ParticleSystem.ShapeModule ss = sp.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 42f; ss.radius = 0.7f;
        ParticleSystem.ColorOverLifetimeModule sc = sp.colorOverLifetime; sc.enabled = true;
        Gradient sg = new Gradient();
        sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        sc.color = new ParticleSystem.MinMaxGradient(sg);
        ParticleSystemRenderer sr = splash.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = spray; sr.shadowCastingMode = ShadowCastingMode.Off;

        // ---- deniz sisi: rota boyunca alçak, yavaş sürüklenen kümeler
        Material mist = AlphaParticle("Mat_SeaMist", new Color(0.36f, 0.43f, 0.56f, 0.06f));
        GameObject mistGo = Child(props, "V03_SeaMist");
        mistGo.transform.position = new Vector3(10f, 1.6f, -128f);
        ParticleSystem mp = mistGo.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule mm = mp.main;
        mm.loop = true; mm.playOnAwake = true; mm.prewarm = true; mm.maxParticles = 150;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(22f, 34f);
        mm.startSpeed = 0f;
        mm.startSize = new ParticleSystem.MinMaxCurve(9f, 22f);
        mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        mm.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule me = mp.emission; me.rateOverTime = 4.5f;
        ParticleSystem.ShapeModule ms = mp.shape; ms.shapeType = ParticleSystemShapeType.Box; ms.scale = new Vector3(110f, 2.6f, 80f);
        ParticleSystem.VelocityOverLifetimeModule mv = mp.velocityOverLifetime; mv.enabled = true; mv.space = ParticleSystemSimulationSpace.World;
        mv.x = new ParticleSystem.MinMaxCurve(0.25f, 0.7f); mv.y = new ParticleSystem.MinMaxCurve(0f, 0f); mv.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        ParticleSystem.RotationOverLifetimeModule mr = mp.rotationOverLifetime; mr.enabled = true; mr.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        ParticleSystem.ColorOverLifetimeModule mc = mp.colorOverLifetime; mc.enabled = true;
        Gradient mg = new Gradient();
        mg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        mc.color = new ParticleSystem.MinMaxGradient(mg);
        ParticleSystemRenderer mrr = mistGo.GetComponent<ParticleSystemRenderer>();
        mrr.sharedMaterial = mist; mrr.shadowCastingMode = ShadowCastingMode.Off; mrr.sortingFudge = 50f;

        // ---- sürücü bileşen
        IntroBoatFx fx = boat.GetComponent<IntroBoatFx>();
        if (fx == null) fx = boat.gameObject.AddComponent<IntroBoatFx>();
        SerializedObject so = new SerializedObject(fx);
        so.FindProperty("lantern").objectReferenceValue = null;
        so.FindProperty("lanternGlass").objectReferenceValue = null;
        so.FindProperty("splash").objectReferenceValue = sp;
        so.ApplyModifiedPropertiesWithoutUndo();
        MarkDirty();
        Debug.Log("V03: sinematik — tekne yerel sınırı " + hull.min + " .. " + hull.max);
    }

    // ------------------------------------------------------------------ V-05) yağmur

    private static Material LitParticle(string name, Texture2D tex, Color color, Color emission)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
        Material m = Mat(name, sh);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
        // "Preserve Specular" açıkken URP ön-çarpımlı karışım kullanır: ışık alfa ile maskelenmez, düşük alfalı
        // parçacıklar fenerde dörtgen kare olarak parlar → kapalı, düz alfa karışımı
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3000;
        // Işıma KAPALI: parçacık shader'ında ışıma alfa ile maskelenmiyor → karanlıkta dörtgen kare olarak görünüyor
        m.SetColor("_EmissionColor", Color.black);
        m.DisableKeyword("_EMISSION");
        // kameraya çok yakın damlalar dev çubuk gibi görünmesin
        if (m.HasProperty("_CameraFadingEnabled"))
        {
            m.SetFloat("_CameraFadingEnabled", 1f);
            m.SetFloat("_CameraNearFadeDistance", 0.9f);
            m.SetFloat("_CameraFarFadeDistance", 3f);
            m.EnableKeyword("_FADING_ON");
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    [MenuItem("Tools/Son Hasat/V-05 Yağmur")]
    public static void BuildRain()
    {
        // ince damla çizgisi: enine yumuşak, ucu sivri, kuyruğu sönen
        Texture2D streak = new Texture2D(32, 128, TextureFormat.RGBA32, true);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 32; x++)
            {
                float u = (x + 0.5f) / 32f * 2f - 1f, v = (y + 0.5f) / 128f;
                float across = Mathf.Exp(-u * u * 7f);
                float along = Mathf.SmoothStep(0f, 1f, v / 0.75f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.08f);
                streak.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(across * along)));
            }
        streak.Apply();
        Texture2D streakTex = SaveTex(streak, "T_RainStreak", true, true);

        // yerdeki halka dalgacık
        Texture2D ring = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 64f * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.07f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((r - 0.45f) / 0.06f, 2f));
                ring.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a) * Mathf.SmoothStep(1f, 0.9f, r)));
            }
        ring.Apply();
        Texture2D ringTex = SaveTex(ring, "T_RainRipple", true, true);

        Material rainMat = LitParticle("Mat_RainLit", streakTex, new Color(0.78f, 0.86f, 1f, 0.42f), new Color(0.035f, 0.045f, 0.06f));
        Material rippleMat = LitParticle("Mat_RainRipple", ringTex, new Color(0.8f, 0.88f, 1f, 0.5f), new Color(0.02f, 0.026f, 0.035f));

        GameObject weather = GameObject.Find("Weather");
        ParticleSystem rain = weather.transform.Find("Rain").GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = rain.main;
        main.maxParticles = 4000;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.82f, 0.96f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 1f));
        main.gravityModifier = 1f;
        ParticleSystem.ShapeModule shape = rain.shape;
        shape.scale = new Vector3(28f, 0.2f, 28f);
        ParticleSystem.VelocityOverLifetimeModule vel = rain.velocityOverLifetime;
        vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); vel.y = new ParticleSystem.MinMaxCurve(-14.5f, -11.5f); vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        ParticleSystemRenderer rr = rain.GetComponent<ParticleSystemRenderer>();
        rr.sharedMaterial = rainMat; rr.renderMode = ParticleSystemRenderMode.Stretch; rr.lengthScale = 2f; rr.velocityScale = 0.035f;
        rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;
        EditorUtility.SetDirty(rain); EditorUtility.SetDirty(rr);

        // halka dalgacıklar: RainSplash'in altında, onunla birlikte oyuncuyu izler
        Transform splash = weather.transform.Find("RainSplash");
        DestroyChild(splash, "Ripples");
        GameObject rip = Child(splash, "Ripples");
        ParticleSystem rp = rip.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule rm = rp.main;
        rm.loop = true; rm.playOnAwake = true; rm.maxParticles = 500;
        rm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        rm.startSpeed = 0f;
        rm.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
        rm.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule re = rp.emission; re.rateOverTime = 0f;
        ParticleSystem.ShapeModule rs = rp.shape;
        rs.shapeType = ParticleSystemShapeType.Box; rs.scale = new Vector3(14f, 0.01f, 14f);
        ParticleSystem.SizeOverLifetimeModule rsz = rp.sizeOverLifetime; rsz.enabled = true;
        rsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f));
        ParticleSystem.ColorOverLifetimeModule rc = rp.colorOverLifetime; rc.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        rc.color = new ParticleSystem.MinMaxGradient(g);
        ParticleSystemRenderer rpr = rip.GetComponent<ParticleSystemRenderer>();
        rpr.sharedMaterial = rippleMat; rpr.renderMode = ParticleSystemRenderMode.HorizontalBillboard; rpr.shadowCastingMode = ShadowCastingMode.Off;

        SerializedObject so = new SerializedObject(splash.GetComponent<RainSplash>());
        so.FindProperty("ripples").objectReferenceValue = rp;
        so.ApplyModifiedPropertiesWithoutUndo();

        RainWetness wet = weather.GetComponent<RainWetness>();
        if (wet == null) wet = weather.AddComponent<RainWetness>();
        SerializedObject wso = new SerializedObject(wet);
        wso.FindProperty("weather").objectReferenceValue = weather.GetComponent<WeatherController>();
        wso.ApplyModifiedPropertiesWithoutUndo();
        MarkDirty();
        Debug.Log("V05: yağmur kuruldu");
    }

    // ------------------------------------------------------------------ V-06) çiçek zehri

    private static Gradient FadeInOut(float peak)
    {
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.2f), new GradientAlphaKey(peak, 0.7f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    [MenuItem("Tools/Son Hasat/V-06 Çiçek zehri")]
    public static void BuildSpores()
    {
        Texture2D soft = FindTex("T_SoftCircle");
        // ışıklı (lit) materyaller: karanlıkta görünmez, fener tutulunca belirir (oynanış kuralı korunur)
        Material mote = LitParticle("Mat_SporeMote", soft, new Color(0.6f, 0.8f, 0.35f, 0.9f), Color.black);
        Material haze = LitParticle("Mat_SporeHaze", soft, new Color(0.34f, 0.5f, 0.3f, 0.07f), Color.black);
        // parlama KAPALI: ışıklı parçacıkta yansıma alfa ile maskelenmiyor → fenerde dörtgen kareler ve beyaz patlama
        foreach (Material m in new[] { mote, haze })
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(m);
        }

        const string path = "Assets/_Project/Prefabs/Environment/DiseaseFlower.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform sporesT = root.transform.Find("Spores");
            ParticleSystem spores = sporesT.GetComponent<ParticleSystem>();
            DestroyChild(sporesT, "Miasma"); DestroyChild(sporesT, "Breath");

            // ---- ana spor zerreleri (oran ve yarıçapı DiseaseFlower sürer)
            ParticleSystem.MainModule main = spores.main;
            main.maxParticles = 200;
            main.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.95f, 0.4f, 1f), new Color(0.95f, 1f, 0.7f, 1f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.VelocityOverLifetimeModule vel = spores.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f); vel.y = new ParticleSystem.MinMaxCurve(0.03f, 0.13f); vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            ParticleSystem.ColorOverLifetimeModule col = spores.colorOverLifetime;
            col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
            ParticleSystem.SizeOverLifetimeModule sz = spores.sizeOverLifetime;
            sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.6f)));
            ParticleSystem.NoiseModule noise = spores.noise;
            noise.enabled = true; noise.strength = 0.12f; noise.frequency = 0.5f; noise.scrollSpeed = 0.2f;
            ParticleSystemRenderer pr = sporesT.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard; pr.mesh = null; pr.sharedMaterial = mote;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;

            // ---- zehir pusu: yere yakın, yavaş dönen büyük yumuşak kümeler (yeşil ↔ morumsu)
            GameObject mi = Child(sporesT, "Miasma");
            mi.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            ParticleSystem mp = mi.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule mm = mp.main;
            mm.loop = true; mm.playOnAwake = true; mm.prewarm = true; mm.maxParticles = 14;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9.5f);
            mm.startSpeed = 0f;
            mm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            mm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 1f, 0.55f, 1f), new Color(0.72f, 0.55f, 0.95f, 1f));
            mm.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule me = mp.emission; me.rateOverTime = 1.3f;
            ParticleSystem.ShapeModule ms = mp.shape;
            ms.shapeType = ParticleSystemShapeType.Circle; ms.radius = 2f; ms.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule mv = mp.velocityOverLifetime; mv.enabled = true; mv.space = ParticleSystemSimulationSpace.World;
            mv.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); mv.y = new ParticleSystem.MinMaxCurve(0.01f, 0.05f); mv.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            ParticleSystem.RotationOverLifetimeModule mr = mp.rotationOverLifetime; mr.enabled = true; mr.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            ParticleSystem.ColorOverLifetimeModule mc = mp.colorOverLifetime; mc.enabled = true; mc.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
            ParticleSystem.SizeOverLifetimeModule msz = mp.sizeOverLifetime; msz.enabled = true;
            msz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.25f));
            ParticleSystemRenderer mpr = mi.GetComponent<ParticleSystemRenderer>();
            mpr.sharedMaterial = haze; mpr.shadowCastingMode = ShadowCastingMode.Off; mpr.receiveShadows = false; mpr.sortingFudge = 20f;

            // ---- çiçeğin "nefesi": başından yukarı süzülen sporlar
            GameObject br = Child(sporesT, "Breath");
            br.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            br.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem bp = br.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule bm = bp.main;
            bm.loop = true; bm.playOnAwake = true; bm.prewarm = true; bm.maxParticles = 30;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.38f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            bm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 1f, 0.5f, 1f), new Color(0.85f, 0.7f, 1f, 1f));
            bm.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule be = bp.emission; be.rateOverTime = 5f;
            ParticleSystem.ShapeModule bs = bp.shape; bs.shapeType = ParticleSystemShapeType.Cone; bs.angle = 28f; bs.radius = 0.12f;
            ParticleSystem.NoiseModule bn = bp.noise; bn.enabled = true; bn.strength = 0.18f; bn.frequency = 0.7f; bn.scrollSpeed = 0.3f;
            ParticleSystem.ColorOverLifetimeModule bc = bp.colorOverLifetime; bc.enabled = true; bc.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
            ParticleSystemRenderer bpr = br.GetComponent<ParticleSystemRenderer>();
            bpr.sharedMaterial = mote; bpr.shadowCastingMode = ShadowCastingMode.Off; bpr.receiveShadows = false;

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        MarkDirty();
        Debug.Log("V06: çiçek zehri kuruldu");
    }

    [MenuItem("Tools/Son Hasat/V-06 Zehir sınırı halkası")]
    public static void BuildZoneBoundary()
    {
        // karanlıkta da görünmeli (tehlike sınırı) → ışıksız, toplamalı; top mesh yerine yumuşak mor spor + perde
        Material spore = AdditiveParticle("Mat_ZoneSpore", new Color(2.2f, 0.7f, 3.6f, 1f));
        Material veil = AdditiveParticle("Mat_ZoneVeil", new Color(0.5f, 0.14f, 0.9f, 0.09f));

        GameObject go = GameObject.Find("ZoneBoundary");
        ParticleSystem ring = go.GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ring.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.45f, 1f, 1f), new Color(1f, 0.6f, 0.95f, 1f));
        ParticleSystem.VelocityOverLifetimeModule vel = ring.velocityOverLifetime;
        vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.95f); vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        ParticleSystem.NoiseModule noise = ring.noise;
        noise.enabled = true; noise.strength = 0.3f; noise.frequency = 0.45f; noise.scrollSpeed = 0.25f;
        ParticleSystem.ColorOverLifetimeModule col = ring.colorOverLifetime;
        col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
        ParticleSystem.SizeOverLifetimeModule sz = ring.sizeOverLifetime;
        sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.3f)));
        ParticleSystemRenderer rr = go.GetComponent<ParticleSystemRenderer>();
        rr.renderMode = ParticleSystemRenderMode.Billboard; rr.mesh = null; rr.sharedMaterial = spore;
        rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;

        DestroyChild(go.transform, "Wisps");
        GameObject wg = Child(go.transform, "Wisps");
        ParticleSystem wp = wg.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule wm = wp.main;
        wm.loop = true; wm.playOnAwake = true; wm.maxParticles = 400;
        wm.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        wm.startSpeed = 0f;
        wm.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
        wm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        wm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.5f, 1f, 1f), new Color(0.55f, 0.9f, 0.6f, 1f));
        wm.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule we = wp.emission; we.rateOverTime = 0f;
        ParticleSystem.ShapeModule ringShape = ring.shape;
        ParticleSystem.ShapeModule ws = wp.shape;
        ws.shapeType = ringShape.shapeType; ws.radius = ringShape.radius; ws.radiusThickness = ringShape.radiusThickness;
        ws.rotation = ringShape.rotation; ws.arc = ringShape.arc; ws.position = new Vector3(0f, 0f, 0f);
        ParticleSystem.VelocityOverLifetimeModule wv = wp.velocityOverLifetime; wv.enabled = true; wv.space = ParticleSystemSimulationSpace.World;
        wv.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); wv.y = new ParticleSystem.MinMaxCurve(0.12f, 0.4f); wv.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        ParticleSystem.RotationOverLifetimeModule wr = wp.rotationOverLifetime; wr.enabled = true; wr.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        ParticleSystem.ColorOverLifetimeModule wc = wp.colorOverLifetime; wc.enabled = true; wc.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
        ParticleSystemRenderer wpr = wg.GetComponent<ParticleSystemRenderer>();
        wpr.sharedMaterial = veil; wpr.shadowCastingMode = ShadowCastingMode.Off; wpr.receiveShadows = false; wpr.sortingFudge = 30f;

        SerializedObject so = new SerializedObject(go.GetComponent<ZoneBoundary>());
        so.FindProperty("wisps").objectReferenceValue = wp;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ring);
        MarkDirty();
        Debug.Log("V06: zehir sınırı halkası kuruldu");
    }

    // ------------------------------------------------------------------ V-07) Bölüm 2 mağarası

    private static void BuildCaveSlabTexture()
    {
        EnsureDir();
        rng = new System.Random(7707);
        const int S = 1024;
        Color[] col = new Color[S * S];
        float[] hgt = new float[S * S];
        Color dark = new Color(0.085f, 0.095f, 0.115f), mid = new Color(0.19f, 0.2f, 0.225f), ochre = new Color(0.24f, 0.19f, 0.14f), calcite = new Color(0.36f, 0.38f, 0.4f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float big = PFbm(u, v, 3, 5, 301), med = PFbm(u, v, 12, 4, 347), strat = PFbm(u * 0.35f + med * 0.08f, v, 24, 3, 389), grain = PNoise(u * 256f, v * 256f, 256, 401);
                Color c = Color.Lerp(dark, mid, Mathf.SmoothStep(0.25f, 0.8f, big * 0.5f + med * 0.5f));
                c = Color.Lerp(c, ochre, Mathf.SmoothStep(0.6f, 0.85f, strat) * 0.45f);          // mineral damarları
                c = Color.Lerp(c, calcite, Mathf.SmoothStep(0.78f, 0.95f, med) * 0.4f);          // kireç lekeleri
                c *= 0.88f + 0.24f * grain;
                c.a = 1f;
                col[y * S + x] = c;
                hgt[y * S + x] = 0.45f + med * 0.3f + grain * 0.1f;
            }
        // çatlaklar: rastgele yürüyüşle ilerleyen koyu, çukur çizgiler
        for (int i = 0; i < 46; i++)
        {
            float px = R(0f, S), py = R(0f, S), ang = R(0f, Mathf.PI * 2f), wid = R(1.1f, 2.4f);
            int steps = RI(10, 34);
            for (int k = 0; k < steps; k++)
            {
                Stamp(col, hgt, S, px, py, 9f, wid, ang, new Color(0.025f, 0.028f, 0.035f), 0f, 2);
                px += Mathf.Cos(ang) * 13f; py += Mathf.Sin(ang) * 13f;
                ang += R(-0.5f, 0.5f);
                if (rng.NextDouble() < 0.08) wid = Mathf.Max(0.9f, wid * 0.7f);
            }
        }
        // gömülü taşlar
        for (int i = 0; i < 150; i++)
        {
            float g = R(0.12f, 0.26f), r = R(3f, 11f);
            Stamp(col, hgt, S, R(0f, S), R(0f, S), r, r * R(0.6f, 1f), R(0f, Mathf.PI), new Color(g, g * 1.03f, g * 1.12f), 0.85f, 0);
        }
        Color[] nrm = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int xl = (x + S - 1) % S, xr = (x + 1) % S, yd = (y + S - 1) % S, yu = (y + 1) % S;
                float dx = hgt[y * S + xr] - hgt[y * S + xl], dy = hgt[yu * S + x] - hgt[yd * S + x];
                Vector3 n = new Vector3(-dx * 3f, -dy * 3f, 1f).normalized;
                nrm[y * S + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(col); tex.Apply();
        File.WriteAllBytes(Dir + "/T_CaveSlab.png", tex.EncodeToPNG());
        tex.SetPixels(nrm); tex.Apply();
        File.WriteAllBytes(Dir + "/T_CaveSlab_N.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        ImportTex(Dir + "/T_CaveSlab.png", false, false);
        ImportTex(Dir + "/T_CaveSlab_N.png", true, false);
    }

    private static ParticleSystem CaveBoxSystem(Transform parent, string name, Vector3 pos, Vector3 box, Material mat, float rate, int max)
    {
        GameObject go = Child(parent, name);
        go.transform.position = pos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule m = ps.main;
        m.loop = true; m.playOnAwake = true; m.maxParticles = max; m.startSpeed = 0f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule e = ps.emission; e.rateOverTime = rate;
        ParticleSystem.ShapeModule s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = box;
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    [MenuItem("Tools/Son Hasat/V-07 Mağara (Bölüm 2 sahnesi açıkken)")]
    public static void BuildCave()
    {
        GameObject layoutGo = GameObject.Find("Ch2_Layout");
        if (layoutGo == null) { Debug.LogError("V07: Ch2_Transition sahnesi açık değil"); return; }
        Transform layout = layoutGo.transform;

        // ---- zemin: ıslak kaya
        BuildCaveSlabTexture();
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Moss.png") == null) BuildSoilTextures();
        Material rock = FindMat("Mat_Cave_Rock"), sandSrc = FindMat("Mat_Map_Sand");
        Material floor = Mat("Mat_CaveFloorV", Shader.Find("Goblin/GroundBlend"));
        floor.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_CaveSlab.png"));
        floor.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_CaveSlab_N.png"));
        floor.SetTexture("_MacroMap", rock.GetTexture("_MacroMap"));
        floor.SetTexture("_MossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_Moss.png"));
        floor.SetTexture("_SandMap", sandSrc.GetTexture("_BaseMap"));
        floor.SetTexture("_SandBump", sandSrc.GetTexture("_BumpMap"));
        floor.SetColor("_BaseColor", new Color(0.78f, 0.84f, 0.95f));
        floor.SetFloat("_Tiling", 3.6f); floor.SetFloat("_BumpScale", 1.25f);
        floor.SetFloat("_MacroScale", 0.06f); floor.SetFloat("_MacroStrength", 0.4f);
        floor.SetFloat("_Smoothness", 0.58f); floor.SetFloat("_BlendSharpness", 6f);   // nemli kaya: kristal ışığını yansıtır
        floor.SetFloat("_MossAmount", 0f);
        floor.SetColor("_MudTint", new Color(0.42f, 0.48f, 0.6f)); floor.SetFloat("_MudAmount", 1f); floor.SetFloat("_MudSmoothness", 0.94f);   // su birikmiş, parlak lekeler
        floor.SetFloat("_ShoreZ", -100000f); floor.SetFloat("_WetZ", -100000f);
        EditorUtility.SetDirty(floor);
        Material oldFloor = FindMat("Mat_Ch2_Floor");
        int floors = 0;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.sharedMaterial == oldFloor || r.sharedMaterial == floor) { r.sharedMaterial = floor; floors++; }

        // ---- duvar/tavan/kayalar: soluk mermer gibi duran doku yerine zeminle uyumlu koyu kaya (yalnızca bu sahnede;
        //      Mat_Cave_Rock'ın kendisi Bölüm 1'de de kullanıldığı için değiştirilmez)
        Material wall = Mat("Mat_CaveWallV", rock.shader);
        wall.CopyPropertiesFromMaterial(rock);
        // Denendi: zemin dokusu (T_CaveSlab) duvarda düz beyaz sıva gibi durdu → kayanın kendi dokusu ve kabartması
        // korunur, yalnızca koyu, soğuk tona çekilir ve biraz nemlendirilir.
        wall.SetColor("_BaseColor", new Color(0.4f, 0.44f, 0.55f));
        wall.SetFloat("_BumpScale", 1.5f);
        wall.SetFloat("_MacroScale", 0.09f); wall.SetFloat("_MacroStrength", 0.55f); wall.SetFloat("_Smoothness", 0.3f);
        EditorUtility.SetDirty(wall);
        int walls = 0;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Material[] ms = r.sharedMaterials; bool changed = false;
            for (int i = 0; i < ms.Length; i++) if (ms[i] == rock) { ms[i] = wall; changed = true; }
            if (changed) { r.sharedMaterials = ms; walls++; }
        }
        Debug.Log("V07: " + walls + " kaya/duvar parçası yeni materyale geçti");

        // ---- köprü tahtaları: ahşap dokusu
        Material plank = FindMat("Mat_BridgePlank");
        Texture2D wood = FindTex("T_OldWood");
        if (plank != null && wood != null)
        {
            plank.SetTexture("_BaseMap", wood);
            plank.SetColor("_BaseColor", new Color(0.46f, 0.42f, 0.38f));
            plank.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(plank);
        }

        DestroyChild(layout, "V07_Atmosphere"); DestroyChild(layout, "V07_Sparkles");
        Transform atmo = Child(layout, "V07_Atmosphere").transform, sparkRoot = Child(layout, "V07_Sparkles").transform;

        // ---- tavandan damlayan su + yerde halkalar
        Texture2D streak = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_RainStreak.png"), ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_RainRipple.png");
        Material dropMat = LitParticle("Mat_CaveDrop", streak, new Color(0.8f, 0.9f, 1f, 0.6f), Color.black);
        Material rippleMat = LitParticle("Mat_CaveRipple", ringTex, new Color(0.8f, 0.9f, 1f, 0.55f), Color.black);
        // (ad, merkez x, merkez z, en, boy, tavan y, zemin y, oran)
        float[][] rooms =
        {
            new[] { 0f, 0f, 38f, 38f, -288.2f, -300f, 5f },
            new[] { -5f, 43f, 15f, 38f, -294f, -300f, 2.5f },
            new[] { 0f, 143f, 28f, 28f, -287f, -300f, 3.5f },
            new[] { 0f, 81.5f, 13f, 22f, -287f, -321.5f, 2.5f },
        };
        string[] roomNames = { "Cavern", "Passage", "MirrorRoom", "Chasm" };
        for (int i = 0; i < rooms.Length; i++)
        {
            float[] rm = rooms[i];
            float h = rm[4] - rm[5], fall = Mathf.Sqrt(2f * h / 9.81f);
            ParticleSystem d = CaveBoxSystem(atmo, "Drips_" + roomNames[i], new Vector3(rm[0], rm[4], rm[1]), new Vector3(rm[2], 0.1f, rm[3]), dropMat, rm[6], 60);
            ParticleSystem.MainModule dm = d.main;
            dm.startLifetime = fall; dm.gravityModifier = 1f; dm.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.026f);
            ParticleSystemRenderer dr = d.GetComponent<ParticleSystemRenderer>();
            dr.renderMode = ParticleSystemRenderMode.Stretch; dr.lengthScale = 2f; dr.velocityScale = 0.03f;
            if (roomNames[i] == "Chasm") continue;   // uçurumda damlalar karanlığa düşer, halka yok
            ParticleSystem rp = CaveBoxSystem(atmo, "Ripples_" + roomNames[i], new Vector3(rm[0], rm[5] + 0.025f, rm[1]), new Vector3(rm[2], 0.01f, rm[3]), rippleMat, rm[6], 40);
            ParticleSystem.MainModule rpm = rp.main;
            rpm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); rpm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            ParticleSystem.SizeOverLifetimeModule rsz = rp.sizeOverLifetime; rsz.enabled = true; rsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.12f, 1f, 1f));
            ParticleSystem.ColorOverLifetimeModule rc = rp.colorOverLifetime; rc.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            rc.color = new ParticleSystem.MinMaxGradient(g);
            rp.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        // ---- alçak sis (fenerde görünür) + uçurum dibinde soluk mavi pus (karanlıkta da sezilir)
        // sis için kenarı tamamen sönen (gauss) leke: T_SoftCircle'ın kenarı sert kaldı, üst üste binince halka halka göründü
        Texture2D blob = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 64f * 2f - 1f, r2 = u * u + v * v;
                float a = Mathf.Exp(-r2 * 4.5f) * Mathf.SmoothStep(1f, 0.55f, Mathf.Sqrt(r2));
                blob.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        blob.Apply();
        Texture2D soft = SaveTex(blob, "T_SoftBlob", true, true);
        Material mist = LitParticle("Mat_CaveMist", soft, new Color(0.5f, 0.57f, 0.68f, 0.05f), Color.black);
        mist.SetFloat("_Smoothness", 0f);
        if (mist.HasProperty("_SpecularHighlights")) mist.SetFloat("_SpecularHighlights", 0f);
        mist.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        // yumuşak parçacık: zeminle kesiştiği yerde keskin çizgi oluşmasın
        if (mist.HasProperty("_SoftParticlesEnabled"))
        {
            mist.SetFloat("_SoftParticlesEnabled", 1f);
            mist.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            mist.SetFloat("_SoftParticlesFarFadeDistance", 1.4f);
            if (mist.HasProperty("_SoftParticleFadeParams")) mist.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 1.4f, 0f, 0f));
            mist.EnableKeyword("_SOFTPARTICLES_ON");
        }
        EditorUtility.SetDirty(mist);
        Material abyss = AdditiveParticle("Mat_AbyssMist", new Color(0.1f, 0.24f, 0.45f, 0.06f));
        abyss.SetTexture("_BaseMap", soft);
        // ayna odasında sis YOK: ışın bulmacası net görünmeli (denendi, kubbe gibi durdu)
        // Giriş mağarasındaki alçak sis de KALDIRILDI: çok düşük alfada 8 bit bantlanma → iç içe halkalar görünüyordu
        // (yumuşak doku ve yumuşak parçacık denendi, düzelmedi). Fener huzmesi zaten havadaki pusu veriyor.
        float[][] mists = new float[0][];
        string[] mistNames = new string[0];
        for (int i = 0; i < mists.Length; i++)
        {
            float[] mv = mists[i];
            ParticleSystem mp = CaveBoxSystem(atmo, mistNames[i], new Vector3(mv[0], mv[1], mv[2]), new Vector3(mv[3], mv[4], mv[5]), mist, mv[6], 60);
            ParticleSystem.MainModule mm = mp.main;
            mm.prewarm = true; mm.startLifetime = new ParticleSystem.MinMaxCurve(11f, 17f); mm.startSize = new ParticleSystem.MinMaxCurve(4.5f, 8f);
            mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.VelocityOverLifetimeModule vel = mp.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0.02f); vel.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            ParticleSystem.RotationOverLifetimeModule rot = mp.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            ParticleSystem.ColorOverLifetimeModule mc = mp.colorOverLifetime; mc.enabled = true; mc.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
            mp.GetComponent<ParticleSystemRenderer>().sortingFudge = 30f;
        }
        ParticleSystem ab = CaveBoxSystem(atmo, "Mist_Abyss", new Vector3(0f, -316.5f, 81.5f), new Vector3(13f, 5f, 22f), abyss, 2.2f, 50);
        ParticleSystem.MainModule abm = ab.main;
        abm.prewarm = true; abm.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f); abm.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
        abm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        ParticleSystem.VelocityOverLifetimeModule abv = ab.velocityOverLifetime; abv.enabled = true; abv.space = ParticleSystemSimulationSpace.World;
        abv.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); abv.y = new ParticleSystem.MinMaxCurve(0.02f, 0.1f); abv.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        ParticleSystem.ColorOverLifetimeModule abc = ab.colorOverLifetime; abc.enabled = true; abc.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));

        // ---- kristal kümelerinde parıltı zerreleri
        Material spark = AdditiveParticle("Mat_CrystalSpark", new Color(0.7f, 2.1f, 2.8f, 1f));
        int clusters = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("CrystalCluster")) continue;
            GameObject go = Child(sparkRoot, "Sparkle_" + (++clusters));
            go.transform.position = t.position + Vector3.up * 0.7f;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule m = ps.main;
            m.loop = true; m.playOnAwake = true; m.prewarm = true; m.maxParticles = 14;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule e = ps.emission; e.rateOverTime = 3.2f;
            ParticleSystem.ShapeModule s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.75f;
            ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f); v.y = new ParticleSystem.MinMaxCurve(0.04f, 0.2f); v.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            ParticleSystem.ColorOverLifetimeModule c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = spark; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        MarkDirty();
        Debug.Log("V07: mağara — " + floors + " zemin parçası, " + clusters + " kristal kümesi");
    }

    // ------------------------------------------------------------------ V-08) ana menü / lobi

    private static Sprite SaveSprite(Texture2D tex, string name)
    {
        EnsureDir();
        string path = Dir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static UnityEngine.UI.Image UiImage(Transform parent, string name, Sprite sprite, Color color)
    {
        Transform old = parent.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        UnityEngine.UI.Image img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    private static void TextShadow(Transform t, Color color, Vector2 distance)
    {
        if (t == null) return;
        UnityEngine.UI.Shadow s = t.GetComponent<UnityEngine.UI.Shadow>();
        if (s == null) s = t.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        s.effectColor = color; s.effectDistance = distance; s.useGraphicAlpha = true;
    }

    /// <summary>Kamp ateşine duman + ışık halesi (prefab: ana menü ve lobi ortak kullanır).</summary>
    private static void BuildCampfireFx()
    {
        string[] guids = AssetDatabase.FindAssets("CampfireBackdrop t:Prefab", new[] { "Assets/_Project" });
        if (guids.Length == 0) { Debug.LogWarning("V08: CampfireBackdrop.prefab bulunamadı"); return; }
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Texture2D blobTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_SoftBlob.png");
        if (blobTex == null) blobTex = FindTex("T_SoftCircle");
        Material smoke = AlphaParticle("Mat_FireSmoke", new Color(0.3f, 0.29f, 0.31f, 0.2f));
        smoke.SetTexture("_BaseMap", blobTex);
        Material glow = AdditiveParticle("Mat_FireGlow", new Color(2.4f, 0.7f, 0.16f, 0.085f));
        glow.SetTexture("_BaseMap", blobTex);
        EditorUtility.SetDirty(smoke); EditorUtility.SetDirty(glow);

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform fire = root.transform.Find("Campfire");
            if (fire == null) return;
            DestroyChild(fire, "V08_Smoke"); DestroyChild(fire, "V08_Glow");

            GameObject sg = Child(fire, "V08_Smoke");
            sg.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            sg.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem sp = sg.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule sm = sp.main;
            sm.loop = true; sm.playOnAwake = true; sm.prewarm = true; sm.maxParticles = 60;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule se = sp.emission; se.rateOverTime = 6f;
            ParticleSystem.ShapeModule ss = sp.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 7f; ss.radius = 0.12f;
            ParticleSystem.SizeOverLifetimeModule ssz = sp.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.4f));
            ParticleSystem.ColorOverLifetimeModule sc = sp.colorOverLifetime; sc.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.5f), 0f), new GradientColorKey(Color.white, 0.25f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            sc.color = new ParticleSystem.MinMaxGradient(g);
            ParticleSystem.NoiseModule sn = sp.noise; sn.enabled = true; sn.strength = 0.22f; sn.frequency = 0.4f; sn.scrollSpeed = 0.25f;
            ParticleSystem.VelocityOverLifetimeModule sv = sp.velocityOverLifetime; sv.enabled = true; sv.space = ParticleSystemSimulationSpace.World;
            sv.x = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); sv.y = new ParticleSystem.MinMaxCurve(0f, 0f); sv.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            ParticleSystemRenderer spr = sg.GetComponent<ParticleSystemRenderer>();
            spr.sharedMaterial = smoke; spr.shadowCastingMode = ShadowCastingMode.Off; spr.receiveShadows = false;

            GameObject gg = Child(fire, "V08_Glow");
            gg.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            ParticleSystem gp = gg.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule gm = gp.main;
            gm.loop = true; gm.playOnAwake = true; gm.prewarm = true; gm.maxParticles = 8;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f); gm.startSpeed = 0f;
            gm.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.1f);
            gm.simulationSpace = ParticleSystemSimulationSpace.Local;
            ParticleSystem.EmissionModule ge = gp.emission; ge.rateOverTime = 3f;
            ParticleSystem.ShapeModule gs = gp.shape; gs.shapeType = ParticleSystemShapeType.Sphere; gs.radius = 0.05f;
            ParticleSystem.ColorOverLifetimeModule gc = gp.colorOverLifetime; gc.enabled = true; gc.color = new ParticleSystem.MinMaxGradient(FadeInOut(1f));
            ParticleSystemRenderer gpr = gg.GetComponent<ParticleSystemRenderer>();
            gpr.sharedMaterial = glow; gpr.shadowCastingMode = ShadowCastingMode.Off; gpr.receiveShadows = false; gpr.sortingFudge = -5f;

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Son Hasat/V-08 Ana menü (MainMenu sahnesi açıkken)")]
    public static void BuildMenu()
    {
        GameObject canvasGo = GameObject.Find("MenuCanvas");
        if (canvasGo == null) { Debug.LogError("V08: MainMenu sahnesi açık değil"); return; }
        Transform canvas = canvasGo.transform;

        // yatay karartma (sol koyu → sağ saydam) ve iki ucu sönen ayraç çizgisi
        Texture2D shadeTex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
        Texture2D divTex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; x++)
        {
            float u = x / 255f;
            float a = Mathf.SmoothStep(1f, 0f, u) * Mathf.SmoothStep(1f, 0f, u * 0.6f);
            float d = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 1.6f);
            for (int y = 0; y < 4; y++) { shadeTex.SetPixel(x, y, new Color(1f, 1f, 1f, a)); divTex.SetPixel(x, y, new Color(1f, 1f, 1f, d)); }
        }
        shadeTex.Apply(); divTex.Apply();
        Sprite shade = SaveSprite(shadeTex, "UI_LeftShade"), divider = SaveSprite(divTex, "UI_Divider");

        UnityEngine.UI.Image leftShade = UiImage(canvas, "V08_LeftShade", shade, new Color(0.01f, 0.012f, 0.02f, 0.9f));
        RectTransform lr = leftShade.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0.62f, 1f); lr.offsetMin = lr.offsetMax = Vector2.zero;
        Transform bg = canvas.Find("Background");
        leftShade.transform.SetSiblingIndex(bg != null ? bg.GetSiblingIndex() + 1 : 0);

        Transform title = canvas.Find("Title"), subtitle = canvas.Find("Subtitle");
        TextShadow(title, new Color(0f, 0f, 0f, 0.75f), new Vector2(4f, -5f));
        TextShadow(subtitle, new Color(0f, 0f, 0f, 0.7f), new Vector2(2f, -2f));
        UnityEngine.UI.Image div = UiImage(canvas, "V08_Divider", divider, new Color(0.93f, 0.84f, 0.62f, 0.7f));
        RectTransform dr = div.rectTransform;
        RectTransform tr = title as RectTransform, sr = subtitle as RectTransform;
        dr.anchorMin = tr.anchorMin; dr.anchorMax = tr.anchorMax; dr.pivot = new Vector2(0.5f, 0.5f);
        dr.anchoredPosition = new Vector2(tr.anchoredPosition.x, (tr.anchoredPosition.y - 80f + sr.anchoredPosition.y + 32f) * 0.5f + 4f);
        dr.sizeDelta = new Vector2(560f, 3f);
        div.transform.SetSiblingIndex(subtitle.GetSiblingIndex());
        foreach (string b in new[] { "PlayButton", "SettingsButton", "QuitButton" })
        {
            Transform label = canvas.Find(b + "/Label");
            TextShadow(label, new Color(1f, 0.95f, 0.8f, 0.35f), new Vector2(0f, -1.5f));   // parşömen üstünde kabartma hissi
        }

        // ay kadrajda: sağ üstte, ağaçların üzerinde; ay ışığı da o yönden gelir
        Material sky = RenderSettings.skybox;
        Vector3 moonDir = new Vector3(0.559f, 0.281f, 0.78f).normalized;
        if (sky != null && sky.HasProperty("_MoonDir"))
        {
            sky.SetVector("_MoonDir", moonDir);
            sky.SetFloat("_MoonSize", 0.9989f);
            sky.SetFloat("_MoonGlow", 0.85f);
            EditorUtility.SetDirty(sky);
        }
        GameObject moonLight = GameObject.Find("CampfireBackdrop/Moonlight");
        if (moonLight != null) moonLight.transform.rotation = Quaternion.LookRotation(-moonDir);

        BuildCampfireFx();
        MarkDirty();
        Debug.Log("V08: ana menü kuruldu");
    }

    // ------------------------------------------------------------------ B3-01) Bölüm 3 düşüş sahnesi

    private const string Ch3Path = "Assets/_Project/Scenes/Ch3_Dream.unity";

    private static ParticleSystem StaticCloud(Transform parent, string name, Vector3 pos, Vector3 box, Material mat, int count, float sizeMin, float sizeMax, Color a, Color b)
    {
        GameObject go = Child(parent, name);
        go.transform.position = pos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule m = ps.main;
        m.loop = false; m.playOnAwake = true; m.duration = 1f; m.maxParticles = count;
        m.startLifetime = 100000f; m.startSpeed = 0f;
        m.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new ParticleSystem.MinMaxGradient(a, b);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule e = ps.emission; e.rateOverTime = 0f;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        ParticleSystem.ShapeModule s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = box;
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    /// <summary>Bölüm 3 sahnesini (düşüş sütunu + boş iniş alanı) sıfırdan kurar ve Build Settings'e ekler.</summary>
    [MenuItem("Tools/Son Hasat/B3-01 Düşüş sahnesini kur")]
    public static void BuildCh3()
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const float groundY = -1010f, startY = -560f;
        Vector3 axis = new Vector3(0f, startY, 700f);

        // rüya gökyüzü
        Material night = FindMat("Mat_NightSky");
        Material sky = Mat("Mat_DreamSky", night.shader);
        sky.CopyPropertiesFromMaterial(night);
        sky.SetColor("_HorizonColor", new Color(0.4f, 0.3f, 0.5f));
        sky.SetColor("_ZenithColor", new Color(0.07f, 0.05f, 0.17f));
        sky.SetFloat("_HorizonHeight", 0.55f);
        sky.SetFloat("_StarBrightness", 2.4f);
        sky.SetVector("_MoonDir", new Vector4(0.3f, 0.5f, 0.8f, 0f));
        sky.SetColor("_MoonColor", new Color(1f, 0.9f, 0.96f));
        sky.SetFloat("_MoonSize", 0.9965f);
        sky.SetFloat("_MoonGlow", 1.4f);
        sky.SetColor("_CloudColor", new Color(0.5f, 0.38f, 0.56f));
        sky.SetFloat("_CloudCover", 0.5f);
        sky.SetFloat("_MilkyWay", 2f);
        EditorUtility.SetDirty(sky);

        Texture2D blob = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_SoftBlob.png");
        if (blob == null) blob = FindTex("T_SoftCircle");
        Material cloud = AlphaParticle("Mat_DreamCloud", new Color(1f, 1f, 1f, 0.3f));
        cloud.SetTexture("_BaseMap", blob);
        Material mote = AdditiveParticle("Mat_DreamMote", new Color(1.8f, 1.4f, 2.2f, 1f));
        Material groundMat = Mat("Mat_DreamGround", Shader.Find("Universal Render Pipeline/Lit"));
        groundMat.SetColor("_BaseColor", new Color(0.5f, 0.46f, 0.58f));
        groundMat.SetFloat("_Smoothness", 0.25f);
        EditorUtility.SetDirty(cloud); EditorUtility.SetDirty(groundMat);

        // iniş alanı (şimdilik boş, gri zemin)
        GameObject env = new GameObject("Ch3_Environment");
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "LandingGround";
        ground.transform.SetParent(env.transform, false);
        ground.transform.position = new Vector3(axis.x, groundY - 0.5f, axis.z);
        ground.transform.localScale = new Vector3(420f, 1f, 420f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;

        // bulut katmanları + parıltı zerreleri (hız hissi)
        Transform fx = Child(env.transform, "FallFx").transform;
        Color[] pastel = { new Color(1f, 0.82f, 0.9f, 1f), new Color(0.82f, 0.8f, 1f, 1f), new Color(1f, 0.9f, 0.8f, 1f), new Color(0.9f, 0.95f, 1f, 1f) };
        const int layers = 7;
        for (int i = 0; i < layers; i++)
        {
            float y = Mathf.Lerp(groundY + 70f, startY - 30f, i / (float)(layers - 1));
            StaticCloud(fx, "Clouds_" + i, new Vector3(axis.x, y, axis.z), new Vector3(300f, 22f, 300f), cloud, 46, 28f, 70f, pastel[i % 4], pastel[(i + 1) % 4]);
        }
        StaticCloud(fx, "Motes", new Vector3(axis.x, (groundY + startY) * 0.5f, axis.z), new Vector3(190f, startY - groundY, 190f), mote, 900, 0.25f, 0.75f,
            new Color(1f, 0.8f, 0.95f, 1f), new Color(0.75f, 0.85f, 1f, 1f));

        // başlangıç noktaları (birbirini görecek kadar yakın)
        GameObject starts = new GameObject("Ch3_FallStarts");
        Transform[] pts = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject p = Child(starts.transform, "FallStart_" + (i + 1));
            p.transform.position = axis + new Vector3((i % 2 == 0 ? -1 : 1) * 4f, 0f, (i < 2 ? -1 : 1) * 4f);
            pts[i] = p.transform;
        }

        // sekans (sahne içi NetworkObject)
        GameObject seq = new GameObject("FreefallSequence");
        seq.transform.position = axis;
        seq.AddComponent<Unity.Netcode.NetworkObject>();
        FreefallSequence fs = seq.AddComponent<FreefallSequence>();
        SerializedObject so = new SerializedObject(fs);
        SerializedProperty arr = so.FindProperty("startPoints");
        arr.arraySize = pts.Length;
        for (int i = 0; i < pts.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = pts[i];
        so.FindProperty("groundY").floatValue = groundY;
        so.FindProperty("dreamSky").objectReferenceValue = sky;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, Ch3Path);
        AssetDatabase.SaveAssets();
        // sahne içi NetworkObject kimliği kayıtla üretilir: yeniden açıp bir kez daha kaydet
        scene = EditorSceneManager.OpenScene(Ch3Path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == Ch3Path)) { list.Add(new EditorBuildSettingsScene(Ch3Path, true)); EditorBuildSettings.scenes = list.ToArray(); }
        Debug.Log("B3-01: " + Ch3Path + " kuruldu");
    }

    /// <summary>Player.prefab'a PlayerFreefall ekler; Bölüm 2 çıkışını (ChapterEnd) Bölüm 3'e bağlar.</summary>
    [MenuItem("Tools/Son Hasat/B3-01 Oyuncu ve Bölüm 2 çıkışını bağla")]
    public static void WireCh3()
    {
        const string playerPath = "Assets/_Project/Prefabs/Player.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            PlayerFreefall pf = root.GetComponent<PlayerFreefall>();
            if (pf == null) pf = root.AddComponent<PlayerFreefall>();
            Transform pivot = null;
            foreach (Camera c in root.GetComponentsInChildren<Camera>(true)) pivot = c.transform;
            SerializedObject so = new SerializedObject(pf);
            so.FindProperty("cameraPivot").objectReferenceValue = pivot;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        UnityEngine.SceneManagement.Scene ch2 = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Ch2_Transition.unity", OpenSceneMode.Single);
        ChapterEnd end = Object.FindFirstObjectByType<ChapterEnd>(FindObjectsInactive.Include);
        if (end != null)
        {
            SerializedObject eso = new SerializedObject(end);
            eso.FindProperty("nextSceneName").stringValue = "Ch3_Dream";
            eso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(ch2);
            EditorSceneManager.SaveScene(ch2);
        }
        else Debug.LogError("B3-01: Ch2'de ChapterEnd bulunamadı");
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------ B3-02 rüya köyü (mantar evler)

    /// <summary>Döner yüzey: profil (yarıçap, y) y ekseni çevresinde döndürülür. bend: tepeye doğru karesel yana kayma (eğrilik); wobble: yarıçap dalgası.</summary>
    private static Mesh Lathe(string name, Vector2[] prof, int seg, Vector2 bend, float wobble, float seed)
    {
        int n = prof.Length, w = seg + 1;
        float y0 = prof[0].y, y1 = prof[0].y;
        foreach (Vector2 q in prof) { y0 = Mathf.Min(y0, q.y); y1 = Mathf.Max(y1, q.y); }
        Vector3[] v = new Vector3[n * w];
        Vector2[] uv = new Vector2[n * w];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < w; j++)
            {
                float a = j / (float)seg * Mathf.PI * 2f;
                float t = Mathf.InverseLerp(y0, y1, prof[i].y);
                float r = prof[i].x * (1f + wobble * (Mathf.Sin(a * 3f + seed) * 0.5f + Mathf.Sin(a * 5f + i * 0.7f + seed * 1.3f) * 0.5f));
                v[i * w + j] = new Vector3(Mathf.Cos(a) * r + bend.x * t * t, prof[i].y, Mathf.Sin(a) * r + bend.y * t * t);
                uv[i * w + j] = new Vector2(j / (float)seg, i / (float)(n - 1));
            }
        int[] tri = new int[(n - 1) * seg * 6];
        int k = 0;
        for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < seg; j++)
            {
                int a0 = i * w + j, b0 = a0 + 1, c0 = a0 + w, d0 = c0 + 1;
                tri[k++] = a0; tri[k++] = c0; tri[k++] = b0;
                tri[k++] = b0; tri[k++] = c0; tri[k++] = d0;
            }
        Mesh m = new Mesh { name = name };
        m.vertices = v; m.uv = uv; m.triangles = tri;
        m.RecalculateNormals();
        Vector3[] nr = m.normals;
        for (int i = 0; i < n; i++) { Vector3 avg = (nr[i * w] + nr[i * w + seg]).normalized; nr[i * w] = avg; nr[i * w + seg] = avg; }
        m.normals = nr;
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    private static GameObject MeshObj(Transform parent, string name, Mesh mesh, Material mat, bool shadows = true)
    {
        GameObject go = Child(parent, name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    private static GameObject Prim(Transform parent, string name, PrimitiveType type, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static Material LitMat(string name, Color col, float smooth, Texture2D tex = null, float tiling = 1f)
    {
        Material m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one * tiling);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material GlowMat(string name, Color baseCol, Color emission)
    {
        Material m = LitMat(name, baseCol, 0.2f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Kağıt gibi mat yüzey: parlama ve yansıma kapalı (KULLANICI 2026-11-15: kağıtlar fener tutunca parlıyordu).</summary>
    private static void NoShine(Material m)
    {
        if (m == null) return;
        m.SetFloat("_Smoothness", 0f);
        if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
        if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(m);
    }

    /// <summary>GLSL tarzı smoothstep(kenar0, kenar1, x). Dikkat: Mathf.SmoothStep(from, to, t) farklıdır (aradeğerleme yapar).</summary>
    private static float Sm(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Mantar şapkası dokusu: koyu zemin üstünde soluk, düzensiz benekler (u: çevre, v: kenardan tepeye).</summary>
    private static Texture2D DreamCapTex()
    {
        const int S = 256;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        System.Random rnd = new System.Random(77);
        Vector3[] spots = new Vector3[34];
        for (int i = 0; i < spots.Length; i++) spots[i] = new Vector3((float)rnd.NextDouble(), 0.12f + (float)rnd.NextDouble() * 0.8f, 0.025f + (float)rnd.NextDouble() * 0.05f);
        Color[] px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, vv = y / (float)S;
                float nse = Mathf.PerlinNoise(u * 14f, vv * 14f) * 0.5f + Mathf.PerlinNoise(u * 40f + 3f, vv * 40f) * 0.25f;
                float g = 0.55f + nse * 0.5f;
                g *= Mathf.Lerp(0.55f, 1f, Sm(0f, 0.25f, vv));        // kenara doğru kararır
                float spot = 0f;
                foreach (Vector3 sp in spots)
                {
                    float du = Mathf.Abs(u - sp.x); du = Mathf.Min(du, 1f - du);
                    float stretch = Mathf.Lerp(1f, 3.2f, sp.y);                       // tepeye yakın benekler u'da gerilir (kutup sıkışması)
                    float d = Mathf.Sqrt(du * du / (stretch * stretch) + (vv - sp.y) * (vv - sp.y));
                    spot = Mathf.Max(spot, 1f - Sm(sp.z * 0.7f, sp.z, d));
                }
                float sv = 0.85f + nse * 0.3f;
                Color c = Color.Lerp(new Color(g, g, g, 1f), new Color(sv, sv * 0.97f, sv * 0.88f, 1f), spot * 0.85f);
                c.a = 1f;
                px[y * S + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return SaveTex(t, "T_DreamCap", false, false);
    }

    /// <summary>Kemerli levha: dikdörtgen + üstünde yarım daire, z boyunca kalınlık. Orijin taban ortası; toplam yükseklik h + w/2.</summary>
    private static Mesh ArchMesh(string name, float w, float h, float depth, int arcSeg)
    {
        List<Vector2> o = new List<Vector2>();
        o.Add(new Vector2(-w * 0.5f, 0f));
        for (int k = 0; k <= arcSeg; k++)
        {
            float a = Mathf.PI * (1f - k / (float)arcSeg);
            o.Add(new Vector2(Mathf.Cos(a) * w * 0.5f, h + Mathf.Sin(a) * w * 0.5f));
        }
        o.Add(new Vector2(w * 0.5f, 0f));
        int n = o.Count;
        float zf = depth * 0.5f, zb = -depth * 0.5f;
        Vector2 mid = new Vector2(0f, h * 0.5f);
        List<Vector3> v = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> t = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            uv.Add(new Vector2(a.x, a.y)); uv.Add(new Vector2(b.x, b.y)); uv.Add(new Vector2(c.x, c.y));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 p = o[i], q = o[(i + 1) % n];
            Tri(new Vector3(mid.x, mid.y, zf), new Vector3(p.x, p.y, zf), new Vector3(q.x, q.y, zf), Vector3.forward);
            Tri(new Vector3(mid.x, mid.y, zb), new Vector3(p.x, p.y, zb), new Vector3(q.x, q.y, zb), Vector3.back);
            Vector2 em = (p + q) * 0.5f - mid;
            Vector3 outw = new Vector3(em.x, em.y, 0f);
            Tri(new Vector3(p.x, p.y, zf), new Vector3(q.x, q.y, zf), new Vector3(q.x, q.y, zb), outw);
            Tri(new Vector3(p.x, p.y, zf), new Vector3(q.x, q.y, zb), new Vector3(p.x, p.y, zb), outw);
        }
        Mesh m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    /// <summary>Kapının tabanını ve eşiğini yer hizasına oturtur. Ev hafif yan yatık olduğu için kapı tabanı havalanıyor ya da gömülüyordu
    /// (KULLANICI 2026-11-15). Üst nesnenin (ev kökü ya da oda) orijini yerdedir. Ev yerleştirilip döndürüldükten SONRA çağrılmalıdır.</summary>
    private static void GroundDoor(Transform door)
    {
        if (door == null || door.parent == null) return;
        float groundY = door.parent.position.y;
        Vector3 p = door.position; p.y = groundY; door.position = p;
        Transform step = door.Find("Step");
        if (step == null) return;
        step.rotation = Quaternion.Euler(0f, step.eulerAngles.y, 0f);      // eşik yere düz
        Vector3 sp = step.position; sp.y = groundY + 0.03f; step.position = sp;
    }

    /// <summary>Mantar eve özgü kapı (KULLANICI 2026-11-14): gövdeden dışarı taşan derin kemerli giriş, kemerli kanat (menteşe solda),
    /// üstünde küçük mantar şapkası saçak. Orijin eşiğin ortası (yer hizası, gövde yüzeyi); +Z dışarı. portalMat/lampMat boşsa o parça kurulmaz.</summary>
    private static Transform DoorVisual(Transform parent, string name, Vector3 lp, Quaternion lr, Material frameMat, Material leafMat, Material capMat,
        Material portalMat, Material lampMat, out Transform pivot, out Renderer leaf, out Renderer portal)
    {
        Mesh frameMesh = ArchMesh("DreamDoorFrame", 1.36f, 1.34f, 1.1f, 12);
        Mesh leafMesh = ArchMesh("DreamDoorLeaf", 1.0f, 1.3f, 0.09f, 12);
        Vector2[] aw = { new Vector2(0.15f, 0f), new Vector2(1.05f, -0.2f), new Vector2(1.08f, -0.1f), new Vector2(0.85f, 0.14f), new Vector2(0.4f, 0.32f), new Vector2(0.001f, 0.36f) };
        Mesh awning = Lathe("DreamDoorAwning", aw, 16, Vector2.zero, 0.05f, 2f);

        Transform door = Child(parent, name).transform;
        door.localPosition = lp; door.localRotation = lr;
        // ev hafif yan yatık olduğu için kapı tabanı yerden havalanıyor ya da gömülüyordu → taban her zaman yer hizasına alınır
        // (üst nesnenin orijini yerdedir: ev kökü ya da oda)
        // giriş: ön yüzü eşikte, gövdenin içine doğru derin (gövde yukarı doğru daraldığı için üstte dışarı taşar)
        MeshObj(door, "Frame", frameMesh, frameMat).transform.localPosition = new Vector3(0f, 0f, -0.43f);
        GameObject step = Prim(door, "Step", PrimitiveType.Cylinder, frameMat, new Vector3(0f, 0.02f, 0.5f), Quaternion.identity, new Vector3(1.9f, 0.06f, 1.2f));
        GameObject a = MeshObj(door, "Awning", awning, capMat);
        a.transform.localPosition = new Vector3(0f, 2.12f, -0.05f);
        a.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
        a.transform.localScale = new Vector3(1f, 1f, 0.85f);

        portal = null;
        if (portalMat != null)
        {
            GameObject p = MeshObj(door, "Portal", leafMesh, portalMat, false);
            p.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            portal = p.GetComponent<Renderer>();
        }
        pivot = Child(door, "LeafPivot").transform;
        pivot.localPosition = new Vector3(-0.5f, 0f, 0.14f);
        GameObject l = MeshObj(pivot, "Leaf", leafMesh, leafMat);
        l.transform.localPosition = new Vector3(0.5f, 0f, 0.05f);
        leaf = l.GetComponent<Renderer>();
        Prim(pivot, "Knob", PrimitiveType.Sphere, frameMat, new Vector3(0.84f, 0.9f, 0.12f), Quaternion.identity, Vector3.one * 0.11f);
        Prim(pivot, "HingeA", PrimitiveType.Cube, frameMat, new Vector3(0.14f, 0.45f, 0.1f), Quaternion.identity, new Vector3(0.3f, 0.07f, 0.03f));
        Prim(pivot, "HingeB", PrimitiveType.Cube, frameMat, new Vector3(0.14f, 1.3f, 0.1f), Quaternion.identity, new Vector3(0.3f, 0.07f, 0.03f));
        GroundDoor(door);
        if (lampMat != null)
            Prim(door, "Lantern", PrimitiveType.Sphere, lampMat, new Vector3(0f, 1.98f, 0.82f), Quaternion.identity, Vector3.one * 0.26f).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        return door;
    }

    [MenuItem("Tools/Son Hasat/B3-02b Şapka dokusunu yenile")]
    public static void RefreshDreamCapTex() { DreamCapTex(); AssetDatabase.SaveAssets(); }

    /// <summary>Tek mantar ev: eğri gövde, sarkık şapka, kapı, yuvarlak pencereler, baca. Kökün +Z'si kapı yönüdür.</summary>
    private static GameObject MushroomHouse(Transform parent, int idx, float r, float h, float capR, Material stemMat, Material capMat, Material doorMat, Material frameMat, Material winMat, Material winDark, System.Random rnd)
    {
        GameObject root = Child(parent, "MushroomHouse_" + idx);
        float F() { return (float)rnd.NextDouble(); }
        Vector2 bend = new Vector2((F() - 0.5f) * 1.6f, (F() - 0.7f) * 1.1f);
        Vector2[] sp = { new Vector2(r * 1.2f, -0.4f), new Vector2(r * 1.14f, 0.5f), new Vector2(r * 0.95f, h * 0.35f), new Vector2(r * 0.88f, h * 0.65f), new Vector2(r * 0.97f, h * 0.9f), new Vector2(r * 1.05f, h) };
        MeshObj(root.transform, "Stem", Lathe("DreamStem_" + idx, sp, 20, bend, 0.05f, idx * 1.7f), stemMat);      // dalga 0.05 ve tohum idx * 1.7: aşağıdaki SurfR ile aynı olmalı
        float RadiusAt(float y) { for (int i = 0; i < sp.Length - 1; i++) if (y <= sp[i + 1].y) return Mathf.Lerp(sp[i].x, sp[i + 1].x, Mathf.InverseLerp(sp[i].y, sp[i + 1].y, y)); return sp[sp.Length - 1].x; }
        // KULLANICI (2026-11-15): pencere ve kapılar tam oturmuyordu. Sebep: gövde yüzeyi dalgalı (±%5) ve yukarı doğru daralıyor,
        // parçalar ise düz profil yarıçapına ve dik konuyordu. SurfR, Lathe ile AYNI dalga formülünü kullanır (a: Lathe açısı, x = cos, z = sin).
        const float stemWobble = 0.05f;
        float stemSeed = idx * 1.7f;
        float SurfR(float y, float a)
        {
            int i = 0;
            while (i < sp.Length - 2 && y > sp[i + 1].y) i++;
            float k = Mathf.InverseLerp(sp[i].y, sp[i + 1].y, y);
            float w0 = 1f + stemWobble * (Mathf.Sin(a * 3f + stemSeed) * 0.5f + Mathf.Sin(a * 5f + i * 0.7f + stemSeed * 1.3f) * 0.5f);
            float w1 = 1f + stemWobble * (Mathf.Sin(a * 3f + stemSeed) * 0.5f + Mathf.Sin(a * 5f + (i + 1) * 0.7f + stemSeed * 1.3f) * 0.5f);
            return Mathf.Lerp(sp[i].x * w0, sp[i + 1].x * w1, k);
        }
        // Lathe: x = cos, z = sin → bend.x dünya x'i, bend.y dünya z'si
        Vector3 Offset(float y) { float t = Mathf.InverseLerp(-0.4f, h, y); return new Vector3(bend.x, 0f, bend.y) * t * t; }

        float R = capR;
        Vector2[] cp = { new Vector2(R * 0.25f, 0.02f * R), new Vector2(R * 0.7f, -0.02f * R), new Vector2(R, -0.2f * R), new Vector2(R * 1.03f, -0.08f * R), new Vector2(R * 0.93f, 0.2f * R), new Vector2(R * 0.72f, 0.45f * R), new Vector2(R * 0.42f, 0.64f * R), new Vector2(R * 0.16f, 0.72f * R), new Vector2(0.001f, 0.74f * R) };
        GameObject cap = MeshObj(root.transform, "Cap", Lathe("DreamCap_" + idx, cp, 26, new Vector2((F() - 0.5f) * 0.5f, (F() - 0.5f) * 0.5f), 0.045f, idx * 2.3f + 1f), capMat);
        cap.transform.localPosition = Offset(h) + new Vector3(0f, h - 0.05f, 0f);
        cap.transform.localRotation = Quaternion.Euler((F() - 0.5f) * 14f, F() * 360f, (F() - 0.5f) * 14f);

        // kapı: eşik gövdenin yer hizasındaki yüzeyinde (gövde tabanda geniş; eskiden kapının altı gövdeye gömülüyordu)
        // kapı yönü +Z → Lathe açısı 90°. Kapı, gövdenin o yöndeki eğimine göre geriye yatırılır (üstü gövdeye yaslanır).
        const float doorA = Mathf.PI * 0.5f;
        float ZSurf(float y) { return SurfR(y, doorA) + Offset(y).z; }
        float lean = Mathf.Clamp(Mathf.Atan(-(ZSurf(1.9f) - ZSurf(0.1f)) / 1.8f) * Mathf.Rad2Deg, 0f, 32f);
        // KULLANICI (2026-11-15, ikinci kez): kapılar yine oturmadı. Gövde düz bir koni değil: dizde dışa bombeli, sonra hızla daralıyor;
        // kapı yalnızca eşikte yüzeye değdirilince ortası gövdeye gömülüyordu. Kapı düzlemi artık gövdenin en çıkıntılı yerine yaslanır
        // (tüm yükseklik boyunca yüzeyin dışında kalır); arkada kalan boşlukları derin kasa kapatır.
        float tanLean = Mathf.Tan(lean * Mathf.Deg2Rad);
        float dz = float.MinValue;
        for (float yy = 0f; yy <= 2.05f; yy += 0.1f) dz = Mathf.Max(dz, ZSurf(yy) + tanLean * yy);
        dz -= 0.04f;
        float tilt = (F() - 0.5f) * 5f;
        DoorVisual(root.transform, "Door", new Vector3(Offset(0.05f).x, 0f, dz), Quaternion.Euler(-lean, 0f, tilt), frameMat, doorMat, capMat, null, null, out _, out _, out _);

        // pencereler
        bool lit = idx % 5 != 3;                    // birkaç ev tamamen karanlık
        int wins = 2 + rnd.Next(2);
        for (int i = 0; i < wins; i++)
        {
            float ang = (i % 2 == 0 ? 1f : -1f) * (38f + F() * 50f) + (i >= 2 ? 150f : 0f);
            float y = Mathf.Lerp(h * 0.42f, h * 0.78f, F());
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            float wa = Mathf.Atan2(dir.z, dir.x);
            float Along(float yy) { return SurfR(yy, wa) + Vector3.Dot(Offset(yy), dir); }
            Vector3 pos = Offset(y) + dir * SurfR(y, wa) + Vector3.up * y;
            float size = 0.55f + F() * 0.35f;
            // yüzey normali: gövdenin o noktadaki eğimine göre yukarı/aşağı yatar
            Vector3 normal = (dir - Vector3.up * ((Along(y + 0.25f) - Along(y - 0.25f)) / 0.5f)).normalized;
            Quaternion face = Quaternion.LookRotation(normal) * Quaternion.Euler(90f, 0f, 0f);
            Transform wt = Child(root.transform, "Window_" + i).transform;
            wt.localPosition = pos; wt.localRotation = face * Quaternion.Euler(0f, (F() - 0.5f) * 30f, 0f);
            Prim(wt, "Rim", PrimitiveType.Cylinder, frameMat, Vector3.zero, Quaternion.identity, new Vector3(size * 1.25f, 0.13f, size * 1.25f));
            Prim(wt, "Glass", PrimitiveType.Cylinder, lit && F() > 0.25f ? winMat : winDark, new Vector3(0f, 0.075f, 0f), Quaternion.identity, new Vector3(size, 0.07f, size));
            Prim(wt, "BarA", PrimitiveType.Cube, frameMat, new Vector3(0f, 0.155f, 0f), Quaternion.identity, new Vector3(size, 0.03f, 0.06f));
            Prim(wt, "BarB", PrimitiveType.Cube, frameMat, new Vector3(0f, 0.155f, 0f), Quaternion.identity, new Vector3(0.06f, 0.03f, size));
        }

        // baca
        Vector3 chim = cap.transform.localPosition + cap.transform.localRotation * new Vector3(R * 0.42f, R * 0.55f, 0f);
        Prim(root.transform, "Chimney", PrimitiveType.Cylinder, frameMat, chim + Vector3.up * 0.5f, Quaternion.Euler((F() - 0.5f) * 24f, 0f, (F() - 0.5f) * 24f), new Vector3(0.5f, 0.9f, 0.5f));

        CapsuleCollider col = root.AddComponent<CapsuleCollider>();
        col.radius = r * 1.12f; col.height = h + r * 2f; col.center = new Vector3(bend.x * 0.25f, h * 0.5f, bend.y * 0.25f);
        return root;
    }

    /// <summary>Bölüm 3 mekanı: eğri mantar köyü (KULLANICI 2026-11-14). Ch3_Dream sahnesi açıkken çalıştırılır; kendi nesnelerini silip yeniden kurar.</summary>
    [MenuItem("Tools/Son Hasat/B3-02 Rüya köyünü kur")]
    public static void BuildDreamVillage()
    {
        FreefallSequence fs = Object.FindFirstObjectByType<FreefallSequence>(FindObjectsInactive.Include);
        GameObject env = GameObject.Find("Ch3_Environment");
        if (fs == null || env == null) { Debug.LogError("B3-02: önce Ch3_Dream sahnesini aç"); return; }
        Vector3 axis = fs.transform.position;
        SerializedObject fso = new SerializedObject(fs);
        float groundY = fso.FindProperty("groundY").floatValue;
        Vector3 c = new Vector3(axis.x, groundY, axis.z);
        const float villageR = 56f;
        System.Random rnd = new System.Random(3302);
        float F() { return (float)rnd.NextDouble(); }

        // gökyüzü: ay yok
        Material sky = FindMat("Mat_DreamSky");
        if (sky != null)
        {
            sky.SetVector("_MoonDir", new Vector4(0.3f, -0.9f, 0.3f, 0f));   // ufkun altı (tam dikey olmasın: gölgelendiricide NaN)
            sky.SetFloat("_MoonGlow", 0f);
            sky.SetFloat("_MoonSize", 0.9999f);
            EditorUtility.SetDirty(sky);
        }

        // malzemeler
        Texture2D capTex = DreamCapTex();
        Texture2D soil = FindTex("T_Soil");
        Material groundMat = LitMat("Mat_DreamGround", new Color(0.8f, 0.7f, 0.95f), 0.1f, soil, 420f);
        Material stemMat = LitMat("Mat_DreamStem", new Color(0.62f, 0.56f, 0.52f), 0.18f);
        Material[] capMats =
        {
            LitMat("Mat_DreamCap_A", new Color(0.5f, 0.1f, 0.12f), 0.35f, capTex),
            LitMat("Mat_DreamCap_B", new Color(0.2f, 0.26f, 0.42f), 0.35f, capTex),
            LitMat("Mat_DreamCap_C", new Color(0.46f, 0.3f, 0.1f), 0.35f, capTex),
            LitMat("Mat_DreamCap_D", new Color(0.34f, 0.14f, 0.38f), 0.35f, capTex),
        };
        Material doorMat = LitMat("Mat_DreamDoor", new Color(0.16f, 0.09f, 0.07f), 0.15f);
        Material frameMat = LitMat("Mat_DreamFrame", new Color(0.07f, 0.055f, 0.07f), 0.2f);
        Material winMat = GlowMat("Mat_DreamWindow", new Color(0.9f, 0.7f, 0.3f), new Color(1.5f, 0.85f, 0.25f));
        Material winDark = LitMat("Mat_DreamWindowDark", new Color(0.02f, 0.02f, 0.035f), 0.85f);
        Material stoneMat = LitMat("Mat_DreamStone", new Color(0.3f, 0.29f, 0.36f), 0.2f);
        Material shroomMat = GlowMat("Mat_DreamShroom", new Color(0.3f, 0.6f, 0.62f), new Color(0.1f, 0.5f, 0.55f));
        Material lampMat = GlowMat("Mat_DreamLamp", new Color(0.7f, 0.8f, 0.4f), new Color(1.1f, 1.5f, 0.45f));
        Material wallMat = LitMat("Mat_DreamBramble", new Color(0.1f, 0.075f, 0.12f), 0.1f);
        Material farMat = LitMat("Mat_DreamGiant", new Color(0.12f, 0.085f, 0.16f), 0.05f);

        // zemin: kenarı görünmesin diye çok geniş
        Transform ground = env.transform.Find("LandingGround");
        if (ground != null)
        {
            ground.localScale = new Vector3(1700f, 1f, 1700f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        }

        DestroyChild(env.transform, "Ch3_Village");
        Transform village = Child(env.transform, "Ch3_Village").transform;
        village.position = c;

        // evler: iç halka + dış halka, kapılar meydana bakar
        Transform houses = Child(village, "Houses").transform;
        Transform paths = Child(village, "Paths").transform;
        int hi = 0;
        List<Vector3> housePos = new List<Vector3>();
        void Ring(int count, float radius, float jitter, float startAng)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = startAng + i * 360f / count + (F() - 0.5f) * 14f;
                float rad = radius + (F() - 0.5f) * jitter;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                float r = 1.7f + F() * 1.2f, h = 3f + F() * 2.6f, capR = r * (1.9f + F() * 0.45f);
                GameObject house = MushroomHouse(houses, hi, r, h, capR, stemMat, capMats[hi % capMats.Length], doorMat, frameMat, winMat, winDark, rnd);
                house.transform.localPosition = dir * rad;
                house.transform.localRotation = Quaternion.LookRotation(-dir) * Quaternion.Euler((F() - 0.5f) * 9f, (F() - 0.5f) * 50f, (F() - 0.5f) * 9f);
                GroundDoor(house.transform.Find("Door"));      // ev yan yatırıldıktan sonra kapı tabanı yeniden yere oturtulur
                housePos.Add(dir * rad);
                // kapıdan meydana basamak taşları
                Vector3 from = house.transform.localPosition + house.transform.localRotation * new Vector3(0f, 0f, r + 1.6f);
                from.y = 0f;
                Vector3 to = dir * 7.5f;
                int steps = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(from, to) / 1.7f));
                Vector3 side = Vector3.Cross(Vector3.up, (to - from).normalized);
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    Vector3 p = Vector3.Lerp(from, to, t) + side * (Mathf.Sin(t * Mathf.PI * 2f + hi) * 1.1f * Mathf.Sin(t * Mathf.PI) + (F() - 0.5f) * 0.5f);
                    Prim(paths, "Stone", PrimitiveType.Sphere, stoneMat, new Vector3(p.x, 0.01f, p.z), Quaternion.Euler(0f, F() * 360f, 0f), new Vector3(0.8f + F() * 0.6f, 0.14f, 0.6f + F() * 0.5f))
                        .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                hi++;
            }
        }
        Ring(6, 21f, 5f, 20f);
        Ring(9, 40f, 7f, 0f);

        // meydan: taş halka + fener mantarları (yalnızca biri gerçek ışık)
        Transform plaza = Child(village, "Plaza").transform;
        for (int i = 0; i < 26; i++)
        {
            Vector3 p = Quaternion.Euler(0f, i * 360f / 26f + F() * 6f, 0f) * Vector3.forward * (7.2f + F() * 0.8f);
            Prim(plaza, "RingStone", PrimitiveType.Sphere, stoneMat, new Vector3(p.x, 0.02f, p.z), Quaternion.Euler(0f, F() * 360f, 0f), new Vector3(1f + F() * 0.6f, 0.2f, 0.8f + F() * 0.4f))
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        Vector2[] lampStem = { new Vector2(0.24f, -0.2f), new Vector2(0.17f, 1.2f), new Vector2(0.12f, 3.2f), new Vector2(0.1f, 4.4f) };
        Vector2[] lampCap = { new Vector2(0.1f, 0f), new Vector2(0.75f, -0.3f), new Vector2(0.8f, -0.18f), new Vector2(0.55f, 0.18f), new Vector2(0.2f, 0.36f), new Vector2(0.001f, 0.4f) };
        for (int i = 0; i < 5; i++)
        {
            float ang = 36f + i * 72f;
            Vector3 p = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * (i == 0 ? 10.5f : 27f + F() * 14f);
            Vector2 bend = new Vector2((F() - 0.5f) * 2.2f, (F() - 0.5f) * 2.2f);
            Transform lamp = Child(plaza, "LampShroom_" + i).transform;
            lamp.localPosition = p;
            MeshObj(lamp, "Stem", Lathe("DreamLampStem_" + i, lampStem, 10, bend, 0.04f, i), stemMat);
            GameObject lc = MeshObj(lamp, "Cap", Lathe("DreamLampCap_" + i, lampCap, 14, Vector2.zero, 0.05f, i + 3f), lampMat, false);
            lc.transform.localPosition = new Vector3(bend.x, 4.35f, bend.y);
            lc.transform.localRotation = Quaternion.Euler((F() - 0.5f) * 30f, 0f, (F() - 0.5f) * 30f);
            CapsuleCollider lcol = lamp.gameObject.AddComponent<CapsuleCollider>();
            lcol.radius = 0.25f; lcol.height = 4.4f; lcol.center = new Vector3(bend.x * 0.25f, 2.2f, bend.y * 0.25f);
            if (i == 0)
            {
                Light l = Child(lamp, "Light").AddComponent<Light>();
                l.transform.localPosition = new Vector3(bend.x, 3.9f, bend.y);
                l.type = LightType.Point; l.color = new Color(0.75f, 1f, 0.45f); l.intensity = 9f; l.range = 13f; l.shadows = LightShadows.None;
            }
        }

        // küçük parlayan mantarlar (yol bulmaya yardım eden soluk noktalar)
        Transform shrooms = Child(village, "GlowShrooms").transform;
        Vector2[] sm = { new Vector2(0.07f, -0.05f), new Vector2(0.05f, 0.3f), new Vector2(0.09f, 0.3f), new Vector2(0.24f, 0.25f), new Vector2(0.22f, 0.34f), new Vector2(0.11f, 0.43f), new Vector2(0.001f, 0.46f) };
        Mesh[] smMesh = { Lathe("DreamShroom_0", sm, 9, new Vector2(0.05f, 0f), 0.06f, 1f), Lathe("DreamShroom_1", sm, 9, new Vector2(-0.04f, 0.06f), 0.06f, 2f) };
        for (int i = 0; i < 46; i++)
        {
            float ang = F() * 360f, rad = 9f + F() * (villageR - 12f);
            Vector3 center = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * rad;
            bool blocked = false;
            foreach (Vector3 hp in housePos) if ((hp - center).sqrMagnitude < 4.5f * 4.5f) blocked = true;
            if (blocked) continue;
            int n = 2 + rnd.Next(4);
            for (int k = 0; k < n; k++)
            {
                GameObject g = MeshObj(shrooms, "Shroom", smMesh[rnd.Next(2)], shroomMat, false);
                g.transform.localPosition = center + new Vector3((F() - 0.5f) * 1.4f, 0f, (F() - 0.5f) * 1.4f);
                g.transform.localRotation = Quaternion.Euler((F() - 0.5f) * 20f, F() * 360f, (F() - 0.5f) * 20f);
                g.transform.localScale = Vector3.one * (0.6f + F() * 1.5f);
            }
        }

        // sınır: sık dev saplar (8 çeşit mesh) + görünmez duvar
        Transform wall = Child(village, "Boundary").transform;
        const int kinds = 8;
        Mesh[] stalkMesh = new Mesh[kinds], stalkCap = new Mesh[kinds];
        Vector3[] stalkTop = new Vector3[kinds];
        for (int i = 0; i < kinds; i++)
        {
            float r = 1.9f + F() * 1.5f, h = 9f + F() * 12f;
            Vector2[] p = { new Vector2(r * 1.3f, -0.5f), new Vector2(r, 2f), new Vector2(r * 0.7f, h * 0.5f), new Vector2(r * 0.45f, h * 0.85f), new Vector2(r * 0.5f, h) };
            Vector2 bend = new Vector2((F() - 0.5f) * 5f, (F() - 0.5f) * 5f);
            stalkMesh[i] = Lathe("DreamStalk_" + i, p, 10, bend, 0.08f, i);
            Vector2[] cp = { new Vector2(r * 0.3f, 0f), new Vector2(r * 1.7f, -0.5f * r), new Vector2(r * 1.75f, -0.25f * r), new Vector2(r * 1.2f, 0.45f * r), new Vector2(r * 0.4f, 0.8f * r), new Vector2(0.001f, 0.85f * r) };
            stalkCap[i] = Lathe("DreamStalkCap_" + i, cp, 12, Vector2.zero, 0.06f, i);
            stalkTop[i] = new Vector3(bend.x, h - 0.1f, bend.y);
        }
        const int stalks = 64;
        for (int i = 0; i < stalks; i++)
        {
            float ang = i * 360f / stalks + (F() - 0.5f) * 3f;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            int kind = rnd.Next(kinds);
            GameObject st = MeshObj(wall, "Stalk_" + i, stalkMesh[kind], wallMat);
            st.transform.localPosition = dir * (villageR + 2.5f + (F() - 0.5f) * 2.5f);
            st.transform.localRotation = Quaternion.Euler(0f, F() * 360f, 0f);
            st.transform.localScale = Vector3.one * (0.85f + F() * 0.4f);
            GameObject sc = MeshObj(st.transform, "Cap", stalkCap[kind], wallMat);
            sc.transform.localPosition = stalkTop[kind];
            sc.transform.localRotation = Quaternion.Euler((F() - 0.5f) * 24f, F() * 360f, (F() - 0.5f) * 24f);
        }
        const int segs = 40;
        float segLen = 2f * Mathf.PI * villageR / segs + 0.6f;
        for (int i = 0; i < segs; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 360f / segs, 0f) * Vector3.forward;
            GameObject w = Child(wall, "Wall_" + i);
            w.transform.localPosition = dir * villageR + Vector3.up * 15f;
            w.transform.localRotation = Quaternion.LookRotation(dir);
            BoxCollider bc = w.AddComponent<BoxCollider>();
            bc.size = new Vector3(segLen, 32f, 1.5f);
        }

        // uzak dev mantarlar: gökyüzüne karşı siluet
        Transform far = Child(village, "Giants").transform;
        for (int i = 0; i < 30; i++)
        {
            float ang = i * 12f + F() * 9f, rad = 80f + F() * 110f;
            float r = 2.5f + F() * 4f, h = 26f + F() * 46f, R = r * (3.2f + F() * 2f);
            Vector2[] p = { new Vector2(r * 1.5f, -1f), new Vector2(r, h * 0.2f), new Vector2(r * 0.75f, h * 0.7f), new Vector2(r * 0.85f, h) };
            Vector2 bend = new Vector2((F() - 0.5f) * 14f, (F() - 0.5f) * 14f);
            Transform g = Child(far, "Giant_" + i).transform;
            g.localPosition = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * rad;
            MeshObj(g, "Stem", Lathe("DreamGiantStem_" + i, p, 12, bend, 0.05f, i), farMat, false);
            Vector2[] cp = { new Vector2(R * 0.2f, 0f), new Vector2(R, -0.22f * R), new Vector2(R * 1.02f, -0.1f * R), new Vector2(R * 0.8f, 0.25f * R), new Vector2(R * 0.4f, 0.5f * R), new Vector2(0.001f, 0.56f * R) };
            GameObject gc = MeshObj(g, "Cap", Lathe("DreamGiantCap_" + i, cp, 18, Vector2.zero, 0.05f, i), farMat, false);
            gc.transform.localPosition = new Vector3(bend.x, h - 0.5f, bend.y);
            gc.transform.localRotation = Quaternion.Euler((F() - 0.5f) * 16f, F() * 360f, (F() - 0.5f) * 16f);
        }

        // düşüş: köyün içine inilsin
        fso.FindProperty("steerRadius").floatValue = 42f;
        fso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fs);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(env.scene);
        EditorSceneManager.SaveScene(env.scene);
        Debug.Log("B3-02: rüya köyü kuruldu (" + hi + " ev)");
    }

    /// <summary>B3-03: köye denemelik boyut mantarları koyar (boyut ışığı mekaniğini denemek için; asıl bulmacalar B3-04'te). Ch3_Dream açıkken.</summary>
    [MenuItem("Tools/Son Hasat/B3-03 Boyut nesnelerini kur")]
    public static void BuildResizables()
    {
        GameObject village = GameObject.Find("Ch3_Village");
        if (village == null) { Debug.LogError("B3-03: önce Ch3_Dream sahnesini aç ve B3-02'yi kur"); return; }
        Material mat = GlowMat("Mat_DreamResizable", new Color(0.3f, 0.27f, 0.42f), new Color(0.2f, 0.18f, 0.3f));
        Vector2[] prof = { new Vector2(0.3f, -0.1f), new Vector2(0.24f, 0.25f), new Vector2(0.2f, 0.85f), new Vector2(0.3f, 0.9f), new Vector2(0.8f, 0.82f), new Vector2(0.84f, 0.9f), new Vector2(0.7f, 1.02f), new Vector2(0.35f, 1.1f), new Vector2(0.001f, 1.12f) };
        Mesh mesh = Lathe("DreamResizeShroom", prof, 18, new Vector2(0.06f, 0.03f), 0.04f, 4f);

        DestroyChild(village.transform, "Resizables");
        Transform root = Child(village.transform, "Resizables").transform;
        Vector3[] pos = { new Vector3(5f, 0f, 9f), new Vector3(-9f, 0f, 6f), new Vector3(-4f, 0f, -11f) };
        float[] start = { 0f, 1f, 0.5f };
        for (int i = 0; i < pos.Length; i++)
        {
            GameObject go = MeshObj(root, "ResizeShroom_" + (i + 1), mesh, mat);
            go.transform.localPosition = pos[i];
            go.transform.localRotation = Quaternion.Euler(0f, i * 97f, 0f);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<Unity.Netcode.NetworkObject>();
            ResizableObject ro = go.AddComponent<ResizableObject>();
            SerializedObject so = new SerializedObject(ro);
            so.FindProperty("minScale").floatValue = 0.5f;
            so.FindProperty("maxScale").floatValue = 3f;
            so.FindProperty("startSize").floatValue = start[i];
            so.FindProperty("aimOffset").vector3Value = new Vector3(0f, 0.75f, 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        UnityEngine.SceneManagement.Scene scene = village.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        // sahne içi NetworkObject kimliği kayıtla üretilir: yeniden açıp bir kez daha kaydet
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-03: " + pos.Length + " boyut mantarı kuruldu");
    }

    /// <summary>B3-04: iki evi açılabilir yapar (KULLANICI: 2 ev, ikişer yatak). Ev 1: kapı küçülmüş → büyüt. Ev 2: kapının önünü dev mantar kapatmış → küçült.
    /// Evin süs kapısını (Door) silip yerine çalışan kapıyı (DreamDoor) kurar. Ch3_Dream açıkken; B3-02 yeniden kurulursa bu da yeniden çalıştırılmalı.</summary>
    [MenuItem("Tools/Son Hasat/B3-04 Açılabilen evleri kur")]
    public static void BuildDreamDoors()
    {
        GameObject village = GameObject.Find("Ch3_Village");
        Transform houses = village != null ? village.transform.Find("Houses") : null;
        if (houses == null) { Debug.LogError("B3-04: önce Ch3_Dream sahnesini aç ve B3-02 köyünü kur"); return; }
        Material frameMat = FindMat("Mat_DreamFrame");
        Material leafGlow = GlowMat("Mat_DreamDoorGlow", new Color(0.3f, 0.2f, 0.34f), new Color(0.15f, 0.12f, 0.25f));
        Material leafPlain = FindMat("Mat_DreamDoor");
        Material portal = GlowMat("Mat_DreamPortal", new Color(0.5f, 0.4f, 0.7f), new Color(1.6f, 1.25f, 2.2f));
        Material lamp = FindMat("Mat_DreamLamp");
        Material resizeMat = GlowMat("Mat_DreamResizable", new Color(0.3f, 0.27f, 0.42f), new Color(0.2f, 0.18f, 0.3f));
        Mesh shroom = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath)) { Mesh mm = o as Mesh; if (mm != null && mm.name == "DreamResizeShroom") shroom = mm; }
        if (shroom == null) { Debug.LogError("B3-04: önce B3-03 nesnelerini kur (DreamResizeShroom mesh yok)"); return; }

        int[] pick = { 0, 3 };     // iç halkada karşılıklı iki ev
        for (int n = 0; n < pick.Length; n++)
        {
            Transform house = houses.Find("MushroomHouse_" + pick[n]);
            if (house == null) { Debug.LogError("B3-04: MushroomHouse_" + pick[n] + " yok"); continue; }
            Transform old = house.Find("DreamDoor");
            if (old == null) old = house.Find("Door");
            Vector3 lp = old != null ? old.localPosition : new Vector3(0f, 0f, 2f);
            Quaternion lr = old != null ? old.localRotation : Quaternion.identity;
            DestroyChild(house, "Door"); DestroyChild(house, "DreamDoor"); DestroyChild(house, "DoorBlocker");
            bool growDoor = n == 0;

            Renderer capR = house.Find("Cap") != null ? house.Find("Cap").GetComponent<Renderer>() : null;
            Transform pivot; Renderer l1, p1;
            Transform door = DoorVisual(house, "DreamDoor", lp, lr, frameMat, growDoor ? leafGlow : leafPlain, capR != null ? capR.sharedMaterial : frameMat, portal, lamp, out pivot, out l1, out p1);

            door.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
            ResizableObject cond;
            if (growDoor)
            {
                // bulmaca 1: kapı küçücük kalmış → büyüten ışıkla büyüt
                cond = door.gameObject.AddComponent<ResizableObject>();
                SerializedObject so = new SerializedObject(cond);
                so.FindProperty("minScale").floatValue = 0.3f;
                so.FindProperty("maxScale").floatValue = 1f;
                so.FindProperty("startSize").floatValue = 0f;
                so.FindProperty("changeSeconds").floatValue = 3f;
                so.FindProperty("aimOffset").vector3Value = new Vector3(0f, 0.9f, 0.3f);
                SerializedProperty gr = so.FindProperty("glowRenderers");
                gr.arraySize = 1; gr.GetArrayElementAtIndex(0).objectReferenceValue = l1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                // bulmaca 2: kapının önünü dev mantar kapatmış → küçülten ışıkla küçült
                GameObject blocker = MeshObj(house, "DoorBlocker", shroom, resizeMat);
                Vector3 flatF = lr * Vector3.forward; flatF.y = 0f;
                blocker.transform.localPosition = new Vector3(lp.x, 0f, lp.z) + flatF.normalized * 1.45f;
                blocker.transform.localRotation = Quaternion.Euler(0f, 40f, 0f);
                blocker.AddComponent<MeshCollider>().sharedMesh = shroom;
                blocker.AddComponent<Unity.Netcode.NetworkObject>();
                cond = blocker.AddComponent<ResizableObject>();
                SerializedObject so = new SerializedObject(cond);
                so.FindProperty("minScale").floatValue = 0.3f;
                so.FindProperty("maxScale").floatValue = 2.6f;
                so.FindProperty("startSize").floatValue = 1f;
                so.FindProperty("changeSeconds").floatValue = 3f;
                so.FindProperty("aimOffset").vector3Value = new Vector3(0f, 0.6f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            DreamHouseDoor dd = door.gameObject.AddComponent<DreamHouseDoor>();
            SerializedObject dso = new SerializedObject(dd);
            dso.FindProperty("condition").objectReferenceValue = cond;
            dso.FindProperty("requireBig").boolValue = growDoor;
            dso.FindProperty("leafPivot").objectReferenceValue = pivot;
            SerializedProperty pr = dso.FindProperty("portalRenderers");
            pr.arraySize = 1; pr.GetArrayElementAtIndex(0).objectReferenceValue = p1;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }

        UnityEngine.SceneManagement.Scene scene = village.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-04: açılabilen evler kuruldu");
    }

    /// <summary>B3-05: açılabilen iki evin içini kurar (dışarıdan büyük ayrı oda; köyün altında), kapılara giriş/çıkış bağlar, her odaya 2 kilitli yatak koyar.
    /// Bulmacalar sonraki adımda eklenir. Ch3_Dream açıkken; B3-04'ten sonra çalıştırılır.</summary>
    [MenuItem("Tools/Son Hasat/B3-05 Ev içlerini kur")]
    public static void BuildDreamInteriors()
    {
        GameObject env = GameObject.Find("Ch3_Environment");
        GameObject village = GameObject.Find("Ch3_Village");
        Transform houses = village != null ? village.transform.Find("Houses") : null;
        if (env == null || houses == null) { Debug.LogError("B3-05: önce Ch3_Dream sahnesini aç ve B3-02..04 kur"); return; }

        Material frameMat = FindMat("Mat_DreamFrame");
        Material doorMat = FindMat("Mat_DreamDoor");
        Material lamp = FindMat("Mat_DreamLamp");
        Material wallMat = LitMat("Mat_DreamInterior", new Color(0.62f, 0.52f, 0.46f), 0.12f);
        Material floorMat = LitMat("Mat_DreamFloor", new Color(0.3f, 0.2f, 0.16f), 0.25f);
        Material sheetMat = LitMat("Mat_DreamSheet", new Color(0.78f, 0.74f, 0.7f), 0.1f);
        Material lockMat = GlowMat("Mat_DreamLock", new Color(0.12f, 0.05f, 0.12f), new Color(1.4f, 0.35f, 0.9f));
        Material[] blanket = { FindMat("Mat_DreamCap_A"), FindMat("Mat_DreamCap_B"), FindMat("Mat_DreamCap_C"), FindMat("Mat_DreamCap_D") };

        const float R = 9f, H = 6.2f;
        // içe bakan kabuk: profil yukarıdan aşağı sıralanınca yüzeyler içeri döner
        Vector2[] shell = { new Vector2(0.001f, H), new Vector2(R * 0.45f, H * 0.97f), new Vector2(R * 0.8f, H * 0.82f), new Vector2(R * 0.98f, H * 0.5f), new Vector2(R, 0f), new Vector2(R * 1.02f, -0.3f) };
        Mesh shellMesh = Lathe("DreamInteriorShell", shell, 28, Vector2.zero, 0.02f, 3f);
        Mesh awning = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath)) { Mesh mm = o as Mesh; if (mm != null && mm.name == "DreamDoorAwning") awning = mm; }

        DestroyChild(env.transform, "Ch3_Interiors");
        Transform interiors = Child(env.transform, "Ch3_Interiors").transform;
        int[] pick = { 0, 3 };
        int bedIndex = 0;
        for (int n = 0; n < pick.Length; n++)
        {
            Transform house = houses.Find("MushroomHouse_" + pick[n]);
            Transform outer = house != null ? house.Find("DreamDoor") : null;
            DreamHouseDoor hd = outer != null ? outer.GetComponent<DreamHouseDoor>() : null;
            if (hd == null) { Debug.LogError("B3-05: MushroomHouse_" + pick[n] + " kapısı yok (önce B3-04)"); continue; }

            Transform room = Child(interiors, "Interior_" + (n + 1)).transform;
            room.position = village.transform.position + new Vector3(n == 0 ? -45f : 45f, -50f, 0f);

            GameObject sh = MeshObj(room, "Shell", shellMesh, wallMat, false);
            sh.AddComponent<MeshCollider>().sharedMesh = shellMesh;
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor"; floor.transform.SetParent(room, false);
            floor.transform.localPosition = new Vector3(0f, -0.2f, 0f); floor.transform.localScale = new Vector3(R * 2.2f, 0.4f, R * 2.2f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            // halı
            Prim(room, "Rug", PrimitiveType.Cylinder, blanket[(n + 2) % 4], new Vector3(0f, 0.01f, 0f), Quaternion.identity, new Vector3(6f, 0.01f, 6f)).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // tavan lambası
            Prim(room, "LampCord", PrimitiveType.Cylinder, frameMat, new Vector3(0f, H - 0.75f, 0f), Quaternion.identity, new Vector3(0.04f, 0.7f, 0.04f));
            Prim(room, "LampBulb", PrimitiveType.Sphere, lamp, new Vector3(0f, H - 1.6f, 0f), Quaternion.identity, Vector3.one * 0.45f).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Light l = Child(room, "Light").AddComponent<Light>();
            l.transform.localPosition = new Vector3(0f, H - 1.9f, 0f);
            l.type = LightType.Point; l.color = new Color(1f, 0.82f, 0.55f); l.intensity = 7f; l.range = 17f; l.shadows = LightShadows.None;

            // çıkış kapısı (ön yüzü odaya bakar)
            Transform pv; Renderer lf, pt;
            Transform exit = DoorVisual(room, "ExitDoor", new Vector3(0f, 0f, -(R - 0.35f)), Quaternion.identity, frameMat, doorMat, blanket[n % 4], null, null, out pv, out lf, out pt);
            BoxCollider ec = exit.gameObject.AddComponent<BoxCollider>();
            ec.center = new Vector3(0f, 1f, 0.15f); ec.size = new Vector3(1.3f, 2f, 0.4f);
            DreamDoorExit ex = exit.gameObject.AddComponent<DreamDoorExit>();
            SerializedObject eso = new SerializedObject(ex);
            eso.FindProperty("door").objectReferenceValue = hd;
            eso.ApplyModifiedPropertiesWithoutUndo();

            // giriş/çıkış noktaları
            Transform inside = Child(room, "InsidePoint").transform;
            inside.localPosition = new Vector3(0f, 1.1f, -(R - 2.4f)); inside.localRotation = Quaternion.identity;
            DestroyChild(house, "OutsidePoint");
            Transform outside = Child(house, "OutsidePoint").transform;
            Vector3 f = outer.forward; f.y = 0f; f.Normalize();
            outside.position = new Vector3(outer.position.x, village.transform.position.y + 1.1f, outer.position.z) + f * 2.2f;
            outside.rotation = Quaternion.LookRotation(f);
            // dış kapıya etkileşim alanı
            BoxCollider oc = outer.GetComponent<BoxCollider>();
            if (oc == null) oc = outer.gameObject.AddComponent<BoxCollider>();
            oc.center = new Vector3(0f, 1f, 0.2f); oc.size = new Vector3(1.2f, 2f, 0.3f);
            SerializedObject dso = new SerializedObject(hd);
            dso.FindProperty("insidePoint").objectReferenceValue = inside;
            dso.FindProperty("outsidePoint").objectReferenceValue = outside;
            dso.ApplyModifiedPropertiesWithoutUndo();

            // iki yatak (baş uçları duvara, ayak uçları odaya)
            for (int b = 0; b < 2; b++)
            {
                float ang = b == 0 ? -42f : 42f;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                Transform bed = Child(room, "Bed_" + (bedIndex + 1)).transform;
                bed.localPosition = dir * (R - 2.5f);
                bed.localRotation = Quaternion.LookRotation(-dir);         // +Z ayak ucu (oda merkezi)
                Prim(bed, "Frame", PrimitiveType.Cube, doorMat, new Vector3(0f, 0.25f, 0f), Quaternion.identity, new Vector3(1.3f, 0.3f, 2.3f));
                foreach (float sx in new[] { -0.58f, 0.58f })
                    foreach (float sz in new[] { -1.08f, 1.08f })
                        Prim(bed, "Leg", PrimitiveType.Cube, frameMat, new Vector3(sx, 0.08f, sz), Quaternion.identity, new Vector3(0.12f, 0.16f, 0.12f));
                Prim(bed, "Mattress", PrimitiveType.Cube, sheetMat, new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(1.18f, 0.2f, 2.16f));
                Prim(bed, "Pillow", PrimitiveType.Sphere, sheetMat, new Vector3(0f, 0.66f, -0.78f), Quaternion.identity, new Vector3(0.8f, 0.2f, 0.42f));
                Prim(bed, "Blanket", PrimitiveType.Cube, blanket[bedIndex % 4], new Vector3(0f, 0.62f, 0.32f), Quaternion.identity, new Vector3(1.24f, 0.08f, 1.5f));
                if (awning != null)
                {
                    // başlık: dik duran mantar şapkası
                    GameObject hb = MeshObj(bed, "Headboard", awning, blanket[bedIndex % 4]);
                    hb.transform.localPosition = new Vector3(0f, 0.75f, -1.22f);
                    hb.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    hb.transform.localScale = new Vector3(0.75f, 0.5f, 0.95f);
                }
                BoxCollider bc = bed.gameObject.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.35f, 0f); bc.size = new Vector3(1.3f, 0.7f, 2.3f);

                // kilit: yatağı saran kuru kökler ve otlar (KULLANICI 2026-11-15; eski pembe kafesin yerine)
                Transform lockRoot;
                List<Renderer> lr = BuildBedOvergrowth(bed, 500 + (bedIndex + 1) * 37, out lockRoot);

                bed.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
                DreamBed db = bed.gameObject.AddComponent<DreamBed>();
                SerializedObject bso = new SerializedObject(db);
                bso.FindProperty("lockRoot").objectReferenceValue = lockRoot;
                bso.FindProperty("lockGlow").colorValue = new Color(1.1f, 0.4f, 1.3f);
                SerializedProperty arr = bso.FindProperty("lockRenderers");
                arr.arraySize = lr.Count;
                for (int k = 0; k < lr.Count; k++) arr.GetArrayElementAtIndex(k).objectReferenceValue = lr[k];
                bso.ApplyModifiedPropertiesWithoutUndo();
                bedIndex++;
            }
        }

        UnityEngine.SceneManagement.Scene scene = env.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05: ev içleri kuruldu (" + bedIndex + " yatak)");
    }

    /// <summary>B3-05a: Ev 1'e terazi bulmacasını kurar (Yatak 1'i açar). Ch3_Dream açıkken; B3-05 ev içlerinden sonra.</summary>
    [MenuItem("Tools/Son Hasat/B3-05a Terazi bulmacasını kur")]
    public static void BuildScalePuzzle()
    {
        GameObject roomGo = GameObject.Find("Interior_1");
        Transform bedT = roomGo != null ? roomGo.transform.Find("Bed_1") : null;
        if (bedT == null) { Debug.LogError("B3-05a: önce Ch3_Dream sahnesini aç ve B3-05 ev içlerini kur"); return; }
        Transform room = roomGo.transform;
        Material frameMat = FindMat("Mat_DreamFrame");
        Material wood = FindMat("Mat_DreamDoor");
        Material stone = FindMat("Mat_DreamStone");
        Material pumpkin = GlowMat("Mat_DreamPumpkin", new Color(0.85f, 0.42f, 0.1f), new Color(0.3f, 0.12f, 0.03f));
        Material indicatorMat = GlowMat("Mat_DreamIndicator", new Color(0.2f, 0.2f, 0.2f), new Color(0.5f, 0.2f, 0.1f));

        DestroyChild(room, "ScalePuzzle");
        Transform root = Child(room, "ScalePuzzle").transform;
        // yatağın önünde, oda merkezine doğru
        Vector3 bedDir = bedT.localPosition; bedDir.y = 0f; bedDir.Normalize();
        root.localPosition = bedDir * 3.3f;
        root.localRotation = Quaternion.LookRotation(-bedDir);

        Prim(root, "Base", PrimitiveType.Cylinder, frameMat, new Vector3(0f, 0.05f, 0f), Quaternion.identity, new Vector3(0.9f, 0.05f, 0.9f));
        Prim(root, "Post", PrimitiveType.Cylinder, wood, new Vector3(0f, 0.9f, 0f), Quaternion.identity, new Vector3(0.14f, 0.85f, 0.14f));
        CapsuleCollider col = root.gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.22f; col.height = 1.9f; col.center = new Vector3(0f, 0.95f, 0f);
        Renderer ind = Prim(root, "Indicator", PrimitiveType.Sphere, indicatorMat, new Vector3(0f, 1.98f, 0f), Quaternion.identity, Vector3.one * 0.18f).GetComponent<Renderer>();
        ind.shadowCastingMode = ShadowCastingMode.Off;

        Transform beam = Child(root, "Beam").transform;
        beam.localPosition = new Vector3(0f, 1.75f, 0f);
        Prim(beam, "Bar", PrimitiveType.Cube, wood, Vector3.zero, Quaternion.identity, new Vector3(2.5f, 0.08f, 0.1f));
        Transform[] pans = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            Transform pan = Child(beam, i == 0 ? "PanLeft" : "PanRight").transform;
            pan.localPosition = new Vector3(i == 0 ? -1.15f : 1.15f, 0f, 0f);
            Prim(pan, "Rod", PrimitiveType.Cylinder, frameMat, new Vector3(0f, -0.35f, 0f), Quaternion.identity, new Vector3(0.03f, 0.35f, 0.03f));
            Prim(pan, "Dish", PrimitiveType.Cylinder, frameMat, new Vector3(0f, -0.72f, 0f), Quaternion.identity, new Vector3(0.85f, 0.02f, 0.85f));
            pans[i] = pan;
        }
        // sol kefe: üç turun taşları (görünen boy ağırlığı GÖSTERMEZ: küçük kara taş en ağır, iri ak taş en hafif)
        Material stoneDark = LitMat("Mat_DreamStoneDark", new Color(0.05f, 0.05f, 0.07f), 0.35f);
        Material stonePale = LitMat("Mat_DreamStonePale", new Color(0.6f, 0.6f, 0.62f), 0.1f);
        GameObject[] stones =
        {
            Prim(pans[0], "Stone_1", PrimitiveType.Sphere, stone, new Vector3(0f, -0.52f, 0f), Quaternion.Euler(10f, 30f, 0f), new Vector3(0.4f, 0.34f, 0.38f)),
            Prim(pans[0], "Stone_2", PrimitiveType.Sphere, stoneDark, new Vector3(0f, -0.61f, 0f), Quaternion.Euler(0f, 70f, 12f), new Vector3(0.22f, 0.18f, 0.2f)),
            Prim(pans[0], "Stone_3", PrimitiveType.Sphere, stonePale, new Vector3(0f, -0.42f, 0f), Quaternion.Euler(8f, 10f, 0f), new Vector3(0.62f, 0.56f, 0.6f)),
        };
        float[] roundTargets = { 0.5f, 0.86f, 0.2f };
        // tur lambaları
        Renderer[] lamps = new Renderer[3];
        for (int i = 0; i < 3; i++)
        {
            lamps[i] = Prim(root, "RoundLamp_" + (i + 1), PrimitiveType.Sphere, indicatorMat, new Vector3((i - 1) * 0.16f, 1.62f, 0.1f), Quaternion.identity, Vector3.one * 0.09f).GetComponent<Renderer>();
            lamps[i].shadowCastingMode = ShadowCastingMode.Off;
        }
        // ipucu notu (direğin dibinde)
        Material paperMat = GlowMat("Mat_DreamNote", new Color(0.45f, 0.41f, 0.33f), new Color(0.5f, 0.4f, 0.2f));
        NoShine(paperMat);
        GameObject note = GameObject.CreatePrimitive(PrimitiveType.Cube);
        note.name = "HintNote"; note.transform.SetParent(root, false);
        note.transform.localPosition = new Vector3(0.75f, 0.12f, 0.75f); note.transform.localRotation = Quaternion.Euler(0f, 25f, 0f); note.transform.localScale = new Vector3(0.34f, 0.02f, 0.46f);
        note.GetComponent<Renderer>().sharedMaterial = paperMat;
        ReadableNote rn = note.AddComponent<ReadableNote>();
        SerializedObject nso = new SerializedObject(rn);
        nso.FindProperty("body").stringValue = "Görünüşe aldanma.\n\nKüçük kara taş hepsinden ağırdır.\nİri ak taş tüy gibi hafiftir.\nBoz taş ikisinin ortasıdır.\n\nKabak, karşısındaki taşla denk gelene dek büyür ya da küçülür.\nTerazi üç kez dengelenmeli.";
        nso.FindProperty("paperRenderer").objectReferenceValue = note.GetComponent<Renderer>();
        nso.ApplyModifiedPropertiesWithoutUndo();
        // sağ kefe: boyutu değişen kabak
        Transform pk = Child(pans[1], "Pumpkin").transform;
        pk.localPosition = new Vector3(0f, -0.7f, 0f);
        Prim(pk, "Body", PrimitiveType.Sphere, pumpkin, new Vector3(0f, 0.14f, 0f), Quaternion.identity, new Vector3(0.36f, 0.28f, 0.36f));
        Prim(pk, "Stem", PrimitiveType.Cube, pumpkin, new Vector3(0f, 0.3f, 0f), Quaternion.Euler(0f, 0f, 12f), new Vector3(0.04f, 0.08f, 0.04f));
        pk.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
        ResizableObject ro = pk.gameObject.AddComponent<ResizableObject>();
        SerializedObject rso = new SerializedObject(ro);
        rso.FindProperty("minScale").floatValue = 0.4f;
        rso.FindProperty("maxScale").floatValue = 1.6f;
        rso.FindProperty("startSize").floatValue = 0f;
        rso.FindProperty("changeSeconds").floatValue = 7f;
        rso.FindProperty("aimOffset").vector3Value = new Vector3(0f, 0.14f, 0f);
        rso.FindProperty("lightRange").floatValue = 12f;
        rso.ApplyModifiedPropertiesWithoutUndo();

        GameObject logic = Child(root, "Logic");
        logic.AddComponent<Unity.Netcode.NetworkObject>();
        ScalePuzzle sp = logic.AddComponent<ScalePuzzle>();
        SerializedObject so = new SerializedObject(sp);
        so.FindProperty("weight").objectReferenceValue = ro;
        so.FindProperty("bed").objectReferenceValue = bedT.GetComponent<DreamBed>();
        so.FindProperty("beam").objectReferenceValue = beam;
        so.FindProperty("panLeft").objectReferenceValue = pans[0];
        so.FindProperty("panRight").objectReferenceValue = pans[1];
        so.FindProperty("indicator").objectReferenceValue = ind;
        SerializedProperty tg = so.FindProperty("targets"), st = so.FindProperty("stones"), lp2 = so.FindProperty("roundLamps");
        tg.arraySize = 3; st.arraySize = 3; lp2.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            tg.GetArrayElementAtIndex(i).floatValue = roundTargets[i];
            st.GetArrayElementAtIndex(i).objectReferenceValue = stones[i];
            lp2.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.SceneManagement.Scene scene = roomGo.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05a: terazi bulmacası kuruldu");
    }

    /// <summary>B3-05b: Ev 1'e yüksekteki anahtar bulmacasını kurar (Yatak 2'yi açar): büyütülerek basamak olan 3 mantar + tavana yakın asılı anahtar.
    /// Ch3_Dream açıkken; B3-05 ev içlerinden ve B3-03'ten (DreamResizeShroom mesh) sonra.</summary>
    // KULLANICI (2026-11-14): bu bulmaca beğenilmedi, yerine resimli şifre geldi → menüden kaldırıldı
    public static void BuildKeyPuzzle()
    {
        GameObject roomGo = GameObject.Find("Interior_1");
        Transform bedT = roomGo != null ? roomGo.transform.Find("Bed_2") : null;
        if (bedT == null) { Debug.LogError("B3-05b: önce Ch3_Dream sahnesini aç ve B3-05 ev içlerini kur"); return; }
        Transform room = roomGo.transform;
        Mesh shroom = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath)) { Mesh mm = o as Mesh; if (mm != null && mm.name == "DreamResizeShroom") shroom = mm; }
        if (shroom == null) { Debug.LogError("B3-05b: DreamResizeShroom mesh yok (önce B3-03)"); return; }
        Material resizeMat = GlowMat("Mat_DreamResizable", new Color(0.3f, 0.27f, 0.42f), new Color(0.2f, 0.18f, 0.3f));
        Material frameMat = FindMat("Mat_DreamFrame");
        Material keyMat = GlowMat("Mat_DreamKey", new Color(0.9f, 0.7f, 0.2f), new Color(1.8f, 1.3f, 0.35f));

        DestroyChild(room, "KeyPuzzle");
        Transform root = Child(room, "KeyPuzzle").transform;

        // basamaklar: oda ortasından duvara doğru büyüyen üç mantar (tam boyda ~1 m, ~2 m, ~3 m; her biri bir öncekinden zıplanır)
        Vector3 n = Quaternion.Euler(0f, 95f, 0f) * Vector3.forward;
        float[] radius = { 0f, 2.2f, 5.8f };
        float[] maxScale = { 0.9f, 1.8f, 2.7f };
        for (int i = 0; i < 3; i++)
        {
            GameObject st = MeshObj(root, "Step_" + (i + 1), shroom, resizeMat);
            st.transform.localPosition = n * radius[i];
            st.transform.localRotation = Quaternion.Euler(0f, i * 70f, 0f);
            st.AddComponent<MeshCollider>().sharedMesh = shroom;
            st.AddComponent<Unity.Netcode.NetworkObject>();
            ResizableObject ro = st.AddComponent<ResizableObject>();
            SerializedObject so = new SerializedObject(ro);
            so.FindProperty("minScale").floatValue = 0.3f;
            so.FindProperty("maxScale").floatValue = maxScale[i];
            so.FindProperty("startSize").floatValue = 0f;
            so.FindProperty("changeSeconds").floatValue = 3f;
            so.FindProperty("aimOffset").vector3Value = new Vector3(0f, 0.75f, 0f);
            so.FindProperty("lightRange").floatValue = 14f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // anahtar: en yüksek basamağın tam üstünde, tavandan sarkan ipte
        Vector3 top = n * radius[2];
        const float keyY = 5.0f;
        Prim(root, "Cord", PrimitiveType.Cylinder, frameMat, top + Vector3.up * (keyY + 0.55f), Quaternion.identity, new Vector3(0.025f, 0.35f, 0.025f));
        Transform keyRoot = Child(root, "Key").transform;
        keyRoot.localPosition = top + Vector3.up * keyY;
        Transform vis = Child(keyRoot, "Visual").transform;
        List<Renderer> gl = new List<Renderer>();
        gl.Add(Prim(vis, "Ring", PrimitiveType.Cylinder, keyMat, new Vector3(0f, 0.1f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.22f, 0.025f, 0.22f)).GetComponent<Renderer>());
        gl.Add(Prim(vis, "Shaft", PrimitiveType.Cube, keyMat, new Vector3(0f, -0.2f, 0f), Quaternion.identity, new Vector3(0.05f, 0.42f, 0.05f)).GetComponent<Renderer>());
        gl.Add(Prim(vis, "ToothA", PrimitiveType.Cube, keyMat, new Vector3(0.07f, -0.36f, 0f), Quaternion.identity, new Vector3(0.1f, 0.05f, 0.05f)).GetComponent<Renderer>());
        gl.Add(Prim(vis, "ToothB", PrimitiveType.Cube, keyMat, new Vector3(0.06f, -0.27f, 0f), Quaternion.identity, new Vector3(0.08f, 0.05f, 0.05f)).GetComponent<Renderer>());
        foreach (Renderer r in gl) r.shadowCastingMode = ShadowCastingMode.Off;
        BoxCollider kc = keyRoot.gameObject.AddComponent<BoxCollider>();
        kc.center = new Vector3(0f, -0.1f, 0f); kc.size = new Vector3(0.5f, 0.8f, 0.5f);
        keyRoot.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
        HighKeyPuzzle kp = keyRoot.gameObject.AddComponent<HighKeyPuzzle>();
        SerializedObject kso = new SerializedObject(kp);
        kso.FindProperty("bed").objectReferenceValue = bedT.GetComponent<DreamBed>();
        kso.FindProperty("keyVisual").objectReferenceValue = vis;
        SerializedProperty arr = kso.FindProperty("glowRenderers");
        arr.arraySize = gl.Count;
        for (int i = 0; i < gl.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = gl[i];
        kso.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.SceneManagement.Scene scene = roomGo.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05b: anahtar bulmacası kuruldu");
    }

    /// <summary>Dişli: XY düzleminde, z boyunca kalınlık; bölüm dairesi yarıçapı 1. Diş boyu diş sayısına göre ayarlanır (aynı modül):
    /// yarıçapı diş sayısıyla orantılı ölçeklenen dişlilerin dişleri aynı aralıkta olur ve birbirine geçer. Yerel 0 açısı diş ARASI ortasıdır.</summary>
    private static Mesh GearMesh(string name, int teeth, float depth)
    {
        float inner = 1f - 2.5f / teeth, outer = 1f + 2f / teeth;
        List<Vector2> o = new List<Vector2>();
        for (int i = 0; i < teeth; i++)
        {
            float a0 = i / (float)teeth * Mathf.PI * 2f, step = Mathf.PI * 2f / teeth;
            float[] at = { 0f, 0.2f, 0.37f, 0.63f, 0.8f };
            float[] rr = { inner, inner, outer, outer, inner };
            for (int k = 0; k < at.Length; k++)
            {
                float a = a0 + step * at[k];
                o.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr[k]);
            }
        }
        int n = o.Count;
        float zf = depth * 0.5f, zb = -depth * 0.5f;
        List<Vector3> v = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> t = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            uv.Add(new Vector2(a.x, a.y) * 0.5f); uv.Add(new Vector2(b.x, b.y) * 0.5f); uv.Add(new Vector2(c.x, c.y) * 0.5f);
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 p = o[i], q = o[(i + 1) % n];
            Tri(new Vector3(0f, 0f, zf), new Vector3(p.x, p.y, zf), new Vector3(q.x, q.y, zf), Vector3.forward);
            Tri(new Vector3(0f, 0f, zb), new Vector3(p.x, p.y, zb), new Vector3(q.x, q.y, zb), Vector3.back);
            Vector2 em = (p + q) * 0.5f;
            Vector3 outw = new Vector3(em.x, em.y, 0f);
            Tri(new Vector3(p.x, p.y, zf), new Vector3(q.x, q.y, zf), new Vector3(q.x, q.y, zb), outw);
            Tri(new Vector3(p.x, p.y, zf), new Vector3(q.x, q.y, zb), new Vector3(p.x, p.y, zb), outw);
        }
        Mesh m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    /// <summary>B3-05c: Ev 2'ye dişli bulmacasını kurar (Yatak 3'ü açar). Ch3_Dream açıkken; B3-05 ev içlerinden sonra.</summary>
    [MenuItem("Tools/Son Hasat/B3-05c Dişli bulmacasını kur")]
    public static void BuildGearPuzzle()
    {
        GameObject roomGo = GameObject.Find("Interior_2");
        Transform bedT = roomGo != null ? roomGo.transform.Find("Bed_3") : null;
        if (bedT == null) { Debug.LogError("B3-05c: önce Ch3_Dream sahnesini aç ve B3-05 ev içlerini kur"); return; }
        Transform room = roomGo.transform;
        Material frameMat = FindMat("Mat_DreamFrame");
        Material wood = FindMat("Mat_DreamDoor");
        Material brass = LitMat("Mat_DreamBrass", new Color(0.3f, 0.22f, 0.09f), 0.3f);
        brass.SetFloat("_Metallic", 0.5f); EditorUtility.SetDirty(brass);
        Material gearGlow = GlowMat("Mat_DreamGearGlow", new Color(0.2f, 0.18f, 0.28f), new Color(0.2f, 0.18f, 0.3f));
        gearGlow.SetFloat("_Metallic", 0.2f); gearGlow.SetFloat("_Smoothness", 0.15f); EditorUtility.SetDirty(gearGlow);

        DestroyChild(room, "GearPuzzle");
        Transform root = Child(room, "GearPuzzle").transform;
        Vector3 dir = Quaternion.Euler(0f, -100f, 0f) * Vector3.forward;
        root.localPosition = dir * 6.6f;
        root.localRotation = Quaternion.LookRotation(-dir);      // +Z oda merkezine bakar

        // pano
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Board"; board.transform.SetParent(root, false);
        board.transform.localPosition = new Vector3(0f, 1.7f, -0.12f); board.transform.localScale = new Vector3(7.2f, 2.9f, 0.2f);
        board.GetComponent<Renderer>().sharedMaterial = wood;
        foreach (float sx in new[] { -3.3f, 3.3f })
            Prim(root, "Leg", PrimitiveType.Cube, frameMat, new Vector3(sx, 0.3f, -0.12f), Quaternion.identity, new Vector3(0.2f, 0.6f, 0.5f));

        const float y = 1.75f, z = 0.08f;
        // KULLANICI (zorlaştırma): 5 değişen dişli. Doğru yarıçaplar: sürücü, 5 dişli, çıkış (her 5 cm yarıçapa 1 diş → dişler birbirine geçer)
        float[] allR = { 0.4f, 0.55f, 0.3f, 0.7f, 0.35f, 0.5f, 0.3f };
        int count = allR.Length;
        float[] axleX = new float[count];
        float span = 0f;
        for (int i = 1; i < count; i++) { span += allR[i - 1] + allR[i]; axleX[i] = span; }
        for (int i = 0; i < count; i++) axleX[i] -= span * 0.5f;       // panoda ortala
        int[] teethN = new int[count];
        Mesh[] gm = new Mesh[count];
        for (int i = 0; i < count; i++) { teethN[i] = Mathf.RoundToInt(allR[i] / 0.05f); gm[i] = GearMesh("DreamGear_" + teethN[i], teethN[i], 0.14f); }
        const float minS = 0.25f, maxS = 1.0f;
        float[] start = { 1f, 0.9f, 0f, 1f, 0.05f };

        Transform driver = MeshObj(root, "Driver", gm[0], brass).transform;
        driver.localPosition = new Vector3(axleX[0], y, z); driver.localScale = new Vector3(allR[0], allR[0], 1f);
        Transform output = MeshObj(root, "Output", gm[count - 1], brass).transform;
        output.localPosition = new Vector3(axleX[count - 1], y, z); output.localScale = new Vector3(allR[count - 1], allR[count - 1], 1f);
        for (int i = 0; i < count; i++)
            Prim(root, "Axle", PrimitiveType.Cylinder, frameMat, new Vector3(axleX[i], y, z + 0.06f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.1f, 0.12f, 0.1f));

        int mid = count - 2;
        ResizableObject[] ros = new ResizableObject[mid];
        float[] targets = new float[mid];
        for (int i = 0; i < mid; i++)
        {
            GameObject g = MeshObj(root, "Gear_" + (i + 1), gm[i + 1], gearGlow);
            g.transform.localPosition = new Vector3(axleX[i + 1], y, z + (i % 2 == 0 ? 0.02f : -0.02f));
            g.AddComponent<Unity.Netcode.NetworkObject>();
            ResizableObject ro = g.AddComponent<ResizableObject>();
            SerializedObject so = new SerializedObject(ro);
            so.FindProperty("minScale").floatValue = minS;
            so.FindProperty("maxScale").floatValue = maxS;
            so.FindProperty("startSize").floatValue = start[i];
            so.FindProperty("changeSeconds").floatValue = 7f;
            so.FindProperty("aimOffset").vector3Value = new Vector3(0f, 0f, 0.15f);
            so.FindProperty("lightRange").floatValue = 12f;
            so.FindProperty("lightHalfAngle").floatValue = 7f;      // dar: yan yana dişliler ayrı ayrı hedeflenebilsin
            so.ApplyModifiedPropertiesWithoutUndo();
            ros[i] = ro;
            // ResizableObject ölçeği SmoothStep ile verir → doğru yarıçapı veren boy ters SmoothStep
            float yy = Mathf.InverseLerp(minS, maxS, allR[i + 1]);
            targets[i] = 0.5f - Mathf.Sin(Mathf.Asin(1f - 2f * yy) / 3f);
        }

        GameObject logic = Child(root, "Logic");
        logic.transform.localPosition = new Vector3(0f, y, z);
        logic.AddComponent<Unity.Netcode.NetworkObject>();
        GearPuzzle gp = logic.AddComponent<GearPuzzle>();
        SerializedObject gso = new SerializedObject(gp);
        SerializedProperty ga = gso.FindProperty("gears"), ta = gso.FindProperty("targets");
        ga.arraySize = mid; ta.arraySize = mid;
        for (int i = 0; i < mid; i++) { ga.GetArrayElementAtIndex(i).objectReferenceValue = ros[i]; ta.GetArrayElementAtIndex(i).floatValue = targets[i]; }
        gso.FindProperty("tolerance").floatValue = 0.035f;
        gso.FindProperty("bed").objectReferenceValue = bedT.GetComponent<DreamBed>();
        gso.FindProperty("driver").objectReferenceValue = driver;
        gso.FindProperty("output").objectReferenceValue = output;
        SerializedProperty ra = gso.FindProperty("radii"), tn = gso.FindProperty("teeth");
        ra.arraySize = count; tn.arraySize = count;
        for (int i = 0; i < count; i++) { ra.GetArrayElementAtIndex(i).floatValue = allR[i]; tn.GetArrayElementAtIndex(i).intValue = teethN[i]; }
        gso.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.SceneManagement.Scene scene = roomGo.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05c: dişli bulmacası kuruldu (" + mid + " dişli)");
    }

    /// <summary>B3-05d: Ev 2'ye çan bulmacasını kurar (Yatak 4'ü açar): 3 boyu değişen çan + müzik kutusu. Ch3_Dream açıkken; B3-05 ev içlerinden sonra.</summary>
    [MenuItem("Tools/Son Hasat/B3-05d Çan bulmacasını kur")]
    public static void BuildBellPuzzle()
    {
        GameObject roomGo = GameObject.Find("Interior_2");
        Transform bedT = roomGo != null ? roomGo.transform.Find("Bed_4") : null;
        if (bedT == null) { Debug.LogError("B3-05d: önce Ch3_Dream sahnesini aç ve B3-05 ev içlerini kur"); return; }
        Transform room = roomGo.transform;
        Material frameMat = FindMat("Mat_DreamFrame");
        Material wood = FindMat("Mat_DreamDoor");
        Material brass = FindMat("Mat_DreamBrass");
        if (brass == null) brass = LitMat("Mat_DreamBrass", new Color(0.62f, 0.45f, 0.18f), 0.55f);
        Material bellMat = GlowMat("Mat_DreamBell", new Color(0.3f, 0.26f, 0.4f), new Color(0.2f, 0.18f, 0.3f));
        bellMat.SetFloat("_Metallic", 0.4f); bellMat.SetFloat("_Smoothness", 0.3f); EditorUtility.SetDirty(bellMat);
        Vector2[] prof = { new Vector2(0.34f, -0.55f), new Vector2(0.3f, -0.5f), new Vector2(0.24f, -0.3f), new Vector2(0.17f, -0.1f), new Vector2(0.08f, 0f), new Vector2(0.001f, 0.02f) };
        Mesh bellMesh = Lathe("DreamBell", prof, 16, Vector2.zero, 0f, 0f);

        DestroyChild(room, "BellPuzzle");
        Transform root = Child(room, "BellPuzzle").transform;
        Vector3 dir = Quaternion.Euler(0f, 100f, 0f) * Vector3.forward;
        root.localPosition = dir * 6.3f;
        root.localRotation = Quaternion.LookRotation(-dir);

        // çerçeve
        foreach (float sx in new[] { -2.2f, 2.2f })
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post"; post.transform.SetParent(root, false);
            post.transform.localPosition = new Vector3(sx, 1.45f, 0f); post.transform.localScale = new Vector3(0.16f, 2.9f, 0.16f);
            post.GetComponent<Renderer>().sharedMaterial = wood;
        }
        Prim(root, "Beam", PrimitiveType.Cube, wood, new Vector3(0f, 2.9f, 0f), Quaternion.identity, new Vector3(4.7f, 0.14f, 0.18f));

        GameObject logic = Child(root, "Logic");
        logic.transform.localPosition = new Vector3(0f, 2f, 0f);
        logic.AddComponent<Unity.Netcode.NetworkObject>();
        BellPuzzle bp = logic.AddComponent<BellPuzzle>();

        float[] targets = { 0.5f, 0.92f, 0.1f };     // ezgi: orta, kalın, ince
        float[] start = { 0.9f, 0.2f, 0.55f };
        ResizableObject[] ros = new ResizableObject[3];
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 1.25f;
            Prim(root, "Cord", PrimitiveType.Cylinder, frameMat, new Vector3(x, 2.65f, 0f), Quaternion.identity, new Vector3(0.025f, 0.2f, 0.025f));
            GameObject bell = MeshObj(root, "Bell_" + (i + 1), bellMesh, bellMat);
            bell.transform.localPosition = new Vector3(x, 2.45f, 0f);
            Prim(bell.transform, "Clapper", PrimitiveType.Sphere, frameMat, new Vector3(0f, -0.5f, 0f), Quaternion.identity, Vector3.one * 0.1f);
            BoxCollider bc = bell.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.28f, 0f); bc.size = new Vector3(0.62f, 0.6f, 0.62f);
            bell.AddComponent<Unity.Netcode.NetworkObject>();
            ResizableObject ro = bell.AddComponent<ResizableObject>();
            SerializedObject so = new SerializedObject(ro);
            so.FindProperty("minScale").floatValue = 0.5f;
            so.FindProperty("maxScale").floatValue = 1.6f;
            so.FindProperty("startSize").floatValue = start[i];
            so.FindProperty("changeSeconds").floatValue = 5f;
            so.FindProperty("aimOffset").vector3Value = new Vector3(0f, -0.3f, 0f);
            so.FindProperty("lightRange").floatValue = 12f;
            so.FindProperty("lightHalfAngle").floatValue = 10f;
            SerializedProperty gr = so.FindProperty("glowRenderers");
            gr.arraySize = 1; gr.GetArrayElementAtIndex(0).objectReferenceValue = bell.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            ros[i] = ro;
            BellPart part = bell.AddComponent<BellPart>();
            SerializedObject pso = new SerializedObject(part);
            pso.FindProperty("puzzle").objectReferenceValue = bp;
            pso.FindProperty("index").intValue = i;
            pso.ApplyModifiedPropertiesWithoutUndo();
        }

        // müzik kutusu: KULLANICI (zorlaştırma) → odada DEĞİL, köy meydanında. Dışarıdaki oyuncu ezgiyi dinleyip içeridekine anlatır.
        GameObject village = GameObject.Find("Ch3_Village");
        Transform plaza = village != null ? village.transform.Find("Plaza") : null;
        if (plaza == null) { Debug.LogError("B3-05d: Ch3_Village/Plaza yok (önce B3-02)"); return; }
        DestroyChild(plaza, "MusicBox");
        Transform box = Child(plaza, "MusicBox").transform;
        box.localPosition = new Vector3(8.5f, 0f, -6f);
        box.localRotation = Quaternion.Euler(0f, -55f, 0f);
        Material lampGlow = FindMat("Mat_DreamLamp");
        if (lampGlow != null) Prim(box, "Glow", PrimitiveType.Sphere, lampGlow, new Vector3(0f, 1.3f, 0f), Quaternion.identity, Vector3.one * 0.14f).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        Prim(box, "Stand", PrimitiveType.Cube, wood, new Vector3(0f, 0.45f, 0f), Quaternion.identity, new Vector3(0.5f, 0.9f, 0.5f));
        Prim(box, "Box", PrimitiveType.Cube, brass, new Vector3(0f, 1.02f, 0f), Quaternion.identity, new Vector3(0.42f, 0.24f, 0.32f));
        Prim(box, "Crank", PrimitiveType.Cylinder, frameMat, new Vector3(0.27f, 1.02f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.04f, 0.07f, 0.04f));
        Prim(box, "Knob", PrimitiveType.Sphere, frameMat, new Vector3(0.34f, 1.1f, 0f), Quaternion.identity, Vector3.one * 0.07f);
        BoxCollider mc = box.gameObject.AddComponent<BoxCollider>();
        mc.center = new Vector3(0f, 0.6f, 0f); mc.size = new Vector3(0.55f, 1.2f, 0.55f);
        BellPart mp = box.gameObject.AddComponent<BellPart>();
        SerializedObject mso = new SerializedObject(mp);
        mso.FindProperty("puzzle").objectReferenceValue = bp;
        mso.FindProperty("index").intValue = -1;
        mso.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject bso = new SerializedObject(bp);
        SerializedProperty ba = bso.FindProperty("bells"), ta = bso.FindProperty("targets");
        ba.arraySize = 3; ta.arraySize = 3;
        for (int i = 0; i < 3; i++) { ba.GetArrayElementAtIndex(i).objectReferenceValue = ros[i]; ta.GetArrayElementAtIndex(i).floatValue = targets[i]; }
        bso.FindProperty("bed").objectReferenceValue = bedT.GetComponent<DreamBed>();
        bso.FindProperty("musicBox").objectReferenceValue = box;
        bso.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.SceneManagement.Scene scene = roomGo.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05d: çan bulmacası kuruldu");
    }

    /// <summary>Simge dokusu (64x64, siyah zemin üstünde beyaz çizgi): 0 halka, 1 üçgen, 2 çarpı, 3 hilal, 4 baklava, 5 iki çubuk, 6 göz, 7 dört nokta.</summary>
    private static Texture2D SymbolTex(int k, bool paper = false)
    {
        const int S = 64;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        Color[] px = new Color[S * S];
        float Seg(Vector2 p, Vector2 a, Vector2 b) { Vector2 ab = b - a; float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab)); return (p - a - ab * h).magnitude; }
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / S * 2f - 1f, (y + 0.5f) / S * 2f - 1f);   // -1..1
                float d = 10f;                                                                   // çizgiye uzaklık
                const float w = 0.11f;
                switch (k)
                {
                    case 0: d = Mathf.Abs(p.magnitude - 0.6f); break;
                    case 1:
                    {
                        Vector2 a = new Vector2(0f, 0.68f), b = new Vector2(-0.66f, -0.52f), c = new Vector2(0.66f, -0.52f);
                        d = Mathf.Min(Seg(p, a, b), Mathf.Min(Seg(p, b, c), Seg(p, c, a))); break;
                    }
                    case 2: d = Mathf.Min(Seg(p, new Vector2(-0.58f, -0.58f), new Vector2(0.58f, 0.58f)), Seg(p, new Vector2(-0.58f, 0.58f), new Vector2(0.58f, -0.58f))); break;
                    case 3:
                    {
                        float o = p.magnitude - 0.66f, i2 = (p - new Vector2(0.3f, 0.12f)).magnitude - 0.56f;
                        d = Mathf.Max(o, -i2) + w;      // dolu hilal
                        break;
                    }
                    case 4: d = Mathf.Abs(Mathf.Abs(p.x) + Mathf.Abs(p.y) - 0.7f) * 0.707f; break;
                    case 5: d = Mathf.Min(Seg(p, new Vector2(-0.3f, -0.62f), new Vector2(-0.3f, 0.62f)), Seg(p, new Vector2(0.3f, -0.62f), new Vector2(0.3f, 0.62f))); break;
                    case 6:
                    {
                        float e = Mathf.Abs(new Vector2(p.x / 0.74f, p.y / 0.4f).magnitude - 1f) * 0.4f;
                        d = Mathf.Min(e, p.magnitude - 0.06f); break;
                    }
                    default:
                        foreach (Vector2 c in new[] { new Vector2(-0.38f, -0.38f), new Vector2(0.38f, -0.38f), new Vector2(-0.38f, 0.38f), new Vector2(0.38f, 0.38f) })
                            d = Mathf.Min(d, (p - c).magnitude - 0.08f);
                        break;
                }
                float v = 1f - Sm(w * 0.75f, w * 1.15f, d);
                if (paper) v = 1f - v * 0.88f;      // kağıt: açık zemin üstünde koyu simge
                px[y * S + x] = new Color(v, v, v, 1f);
            }
        t.SetPixels(px); t.Apply();
        return SaveTex(t, (paper ? "T_DreamSymbolPaper_" : "T_DreamSymbol_") + k, true, false);
    }

    /// <summary>B3-05b (yeni): Ev 1'in ikinci yatağına resimli şifre bulmacasını kurar; eski yüksekteki anahtar bulmacasını kaldırır.
    /// Doğru simgeler köydeki üç evin kapısına kazınır. Ch3_Dream açıkken; B3-05 ev içlerinden sonra. B3-02 yeniden kurulursa bu da yeniden çalıştırılmalı.</summary>
    [MenuItem("Tools/Son Hasat/B3-05b Resimli şifre bulmacasını kur")]
    public static void BuildSymbolPuzzle()
    {
        GameObject roomGo = GameObject.Find("Interior_1");
        Transform bedT = roomGo != null ? roomGo.transform.Find("Bed_2") : null;
        GameObject village = GameObject.Find("Ch3_Village");
        Transform houses = village != null ? village.transform.Find("Houses") : null;
        if (bedT == null || houses == null) { Debug.LogError("B3-05b: önce Ch3_Dream sahnesini aç ve B3-02..05 kur"); return; }
        Transform room = roomGo.transform;
        Material frameMat = FindMat("Mat_DreamFrame");
        Material wood = FindMat("Mat_DreamDoor");
        Material indicatorMat = GlowMat("Mat_DreamIndicator", new Color(0.2f, 0.2f, 0.2f), new Color(0.5f, 0.2f, 0.1f));

        const int symbolCount = 8;
        Material[] glow = new Material[symbolCount], carve = new Material[symbolCount];
        for (int k = 0; k < symbolCount; k++)
        {
            Texture2D tex = SymbolTex(k);
            glow[k] = GlowMat("Mat_DreamSymbol_" + k, new Color(0.12f, 0.1f, 0.14f), new Color(1.3f, 1.05f, 0.5f));
            glow[k].SetTexture("_BaseMap", tex); glow[k].SetTexture("_EmissionMap", tex);
            EditorUtility.SetDirty(glow[k]);
            // kapıdaki oyma: parlamaz, yalnızca fenerle görülür
            carve[k] = LitMat("Mat_DreamCarve_" + k, new Color(0.45f, 0.41f, 0.34f), 0f, SymbolTex(k, true));      // fenerde patlamasın: koyu, mat
            NoShine(carve[k]);
        }

        DestroyChild(room, "KeyPuzzle");          // beğenilmeyen eski bulmaca
        DestroyChild(room, "SymbolPuzzle");
        Transform root = Child(room, "SymbolPuzzle").transform;
        Vector3 bedDir = bedT.localPosition; bedDir.y = 0f; bedDir.Normalize();
        root.localPosition = bedDir * 3.3f;
        root.localRotation = Quaternion.LookRotation(-bedDir);       // +Z oda merkezine bakar

        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "Post"; post.transform.SetParent(root, false);
        post.transform.localPosition = new Vector3(0f, 0.45f, -0.05f); post.transform.localScale = new Vector3(0.3f, 0.9f, 0.2f);
        post.GetComponent<Renderer>().sharedMaterial = wood;
        Prim(root, "Board", PrimitiveType.Cube, wood, new Vector3(0f, 1.4f, -0.05f), Quaternion.identity, new Vector3(2.3f, 1.15f, 0.12f));
        Renderer ind = Prim(root, "Indicator", PrimitiveType.Sphere, indicatorMat, new Vector3(0f, 2.08f, -0.05f), Quaternion.identity, Vector3.one * 0.16f).GetComponent<Renderer>();
        ind.shadowCastingMode = ShadowCastingMode.Off;

        GameObject logic = Child(root, "Logic");
        logic.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        logic.AddComponent<Unity.Netcode.NetworkObject>();
        SymbolPuzzle sp = logic.AddComponent<SymbolPuzzle>();

        int[] solution = { 3, 6, 1 };          // hilal, göz, üçgen
        Renderer[] wheels = new Renderer[3];
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 0.7f;
            Transform w = Child(root, "Wheel_" + (i + 1)).transform;
            w.localPosition = new Vector3(x, 1.5f, 0.02f);
            Prim(w, "Rim", PrimitiveType.Cylinder, frameMat, new Vector3(0f, 0f, -0.01f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.6f, 0.02f, 0.6f));
            // Quad'ın görünen yüzü -Z'dir → +Z'ye baksın diye 180° çevrilir
            GameObject face = Prim(w, "Face", PrimitiveType.Quad, glow[0], new Vector3(0f, 0f, 0.025f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 0.46f);
            face.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            wheels[i] = face.GetComponent<Renderer>();
            BoxCollider bc = w.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0f, 0.02f); bc.size = new Vector3(0.6f, 0.6f, 0.12f);
            SymbolWheel sw = w.gameObject.AddComponent<SymbolWheel>();
            SerializedObject wso = new SerializedObject(sw);
            wso.FindProperty("puzzle").objectReferenceValue = sp;
            wso.FindProperty("index").intValue = i;
            wso.ApplyModifiedPropertiesWithoutUndo();
            // sıra çentikleri: 1, 2, 3 nokta
            for (int d = 0; d <= i; d++)
                Prim(root, "Notch", PrimitiveType.Sphere, frameMat, new Vector3(x + (d - i * 0.5f) * 0.11f, 1.05f, 0.03f), Quaternion.identity, Vector3.one * 0.06f);
        }

        SerializedObject so = new SerializedObject(sp);
        so.FindProperty("bed").objectReferenceValue = bedT.GetComponent<DreamBed>();
        so.FindProperty("indicator").objectReferenceValue = ind;
        SerializedProperty sa = so.FindProperty("solution"), wa = so.FindProperty("wheels"), ma = so.FindProperty("symbols");
        sa.arraySize = 3; wa.arraySize = 3; ma.arraySize = symbolCount;
        for (int i = 0; i < 3; i++) { sa.GetArrayElementAtIndex(i).intValue = solution[i]; wa.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i]; }
        for (int k = 0; k < symbolCount; k++) ma.GetArrayElementAtIndex(k).objectReferenceValue = glow[k];
        so.ApplyModifiedPropertiesWithoutUndo();

        // ipuçları: köydeki üç süs evin kapısına oyma + sıra çentikleri
        int[] clueHouse = { 4, 9, 12 };
        for (int h = 0; h < houses.childCount; h++)
        {
            Transform lp = houses.GetChild(h).Find("Door/LeafPivot");
            if (lp != null) { DestroyChild(lp, "Carving"); }
        }
        for (int i = 0; i < 3; i++)
        {
            Transform house = houses.Find("MushroomHouse_" + clueHouse[i]);
            Transform lp = house != null ? house.Find("Door/LeafPivot") : null;
            if (lp == null) { Debug.LogError("B3-05b: MushroomHouse_" + clueHouse[i] + " kapısı yok"); continue; }
            Transform cv = Child(lp, "Carving").transform;
            cv.localPosition = new Vector3(0.5f, 1.2f, 0.105f);
            Prim(cv, "Symbol", PrimitiveType.Quad, carve[solution[i]], Vector3.zero, Quaternion.Euler(0f, 180f, 0f), Vector3.one * 0.5f).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            for (int d = 0; d <= i; d++)
                Prim(cv, "Notch", PrimitiveType.Sphere, frameMat, new Vector3((d - i * 0.5f) * 0.1f, -0.36f, 0f), Quaternion.identity, Vector3.one * 0.055f);
        }

        UnityEngine.SceneManagement.Scene scene = roomGo.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05b: resimli şifre bulmacası kuruldu");
    }

    /// <summary>B3-06: uyanışı kurar: Bölüm 3 sonu (elle tetiklenen ChapterEnd) + herkes yatınca tetikleyen izleyici. Ch3_Dream açıkken.</summary>
    [MenuItem("Tools/Son Hasat/B3-06 Uyanışı kur")]
    public static void BuildDreamWake()
    {
        GameObject env = GameObject.Find("Ch3_Environment");
        if (env == null) { Debug.LogError("B3-06: önce Ch3_Dream sahnesini aç"); return; }
        DestroyChild(env.transform, "DreamWake");
        GameObject go = Child(env.transform, "DreamWake");
        go.AddComponent<Unity.Netcode.NetworkObject>();
        ChapterEnd end = go.AddComponent<ChapterEnd>();
        go.AddComponent<DreamWakeWatcher>();
        // B3-07 uyku engeli: dinlenme noktası = meydandaki ışıklı fener mantarı
        DreamSleepiness sleep = go.AddComponent<DreamSleepiness>();
        GameObject lampGo = GameObject.Find("LampShroom_0");
        SerializedObject sso = new SerializedObject(sleep);
        SerializedProperty rp = sso.FindProperty("restPoints");
        rp.arraySize = lampGo != null ? 1 : 0;
        if (lampGo != null) rp.GetArrayElementAtIndex(0).objectReferenceValue = lampGo.transform;
        sso.ApplyModifiedPropertiesWithoutUndo();
        if (lampGo == null) Debug.LogWarning("B3-06: LampShroom_0 bulunamadı; uyku için dinlenme noktası yok");

        // B3-07 uyku efektleri (yerel): göz kapakları + halüsinasyon silueti
        Material shade = LitMat("Mat_DreamPhantom", new Color(0.004f, 0.004f, 0.008f), 0f);
        Material eyes = GlowMat("Mat_DreamPhantomEye", new Color(0.9f, 0.9f, 1f), new Color(2.2f, 2.2f, 2.6f));
        GameObject ph = Child(go.transform, "Phantom");
        Prim(ph.transform, "Body", PrimitiveType.Capsule, shade, new Vector3(0f, 1.05f, 0f), Quaternion.identity, new Vector3(0.5f, 1.05f, 0.32f));
        Prim(ph.transform, "Head", PrimitiveType.Sphere, shade, new Vector3(0f, 2.25f, 0f), Quaternion.identity, new Vector3(0.42f, 0.5f, 0.4f));
        Prim(ph.transform, "ArmL", PrimitiveType.Capsule, shade, new Vector3(-0.36f, 1.2f, 0f), Quaternion.Euler(0f, 0f, -6f), new Vector3(0.12f, 0.75f, 0.12f));
        Prim(ph.transform, "ArmR", PrimitiveType.Capsule, shade, new Vector3(0.36f, 1.2f, 0f), Quaternion.Euler(0f, 0f, 6f), new Vector3(0.12f, 0.75f, 0.12f));
        foreach (float ex in new[] { -0.08f, 0.08f })
            Prim(ph.transform, "Eye", PrimitiveType.Sphere, eyes, new Vector3(ex, 2.28f, 0.19f), Quaternion.identity, Vector3.one * 0.045f).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        ph.SetActive(false);
        DreamSleepFx fx = go.AddComponent<DreamSleepFx>();
        SerializedObject fso2 = new SerializedObject(fx);
        fso2.FindProperty("phantom").objectReferenceValue = ph;
        fso2.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject so = new SerializedObject(end);
        so.FindProperty("manualOnly").boolValue = true;
        so.FindProperty("nextSceneName").stringValue = "";
        so.FindProperty("subtitle").stringValue = "Bölüm 3: Düşler";
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.SceneManagement.Scene scene = env.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        string path = scene.path;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-06: uyanış kuruldu");
    }

    /// <summary>B3-08: karanlık evlere izleyen gözler koyar (süs evlerden bazıları; açılabilen ve ipucu taşıyan evler hariç). Ch3_Dream açıkken.
    /// B3-02 köyü yeniden kurulursa bu da yeniden çalıştırılmalı.</summary>
    [MenuItem("Tools/Son Hasat/B3-08 İzleyen gözleri kur")]
    public static void BuildWatchingEyes()
    {
        GameObject village = GameObject.Find("Ch3_Village");
        Transform houses = village != null ? village.transform.Find("Houses") : null;
        if (houses == null) { Debug.LogError("B3-08: önce Ch3_Dream sahnesini aç ve B3-02 köyünü kur"); return; }
        Material eyeMat = GlowMat("Mat_DreamWatchEye", new Color(0.9f, 0.8f, 0.4f), new Color(1.6f, 1.3f, 0.5f));
        Material pupilMat = LitMat("Mat_DreamWatchPupil", new Color(0.01f, 0.01f, 0.01f), 0.6f);
        Material winDark = FindMat("Mat_DreamWindowDark");

        // önce eski gözleri temizle
        for (int h = 0; h < houses.childCount; h++)
        {
            Transform house = houses.GetChild(h);
            WatchingEyes old = house.GetComponent<WatchingEyes>();
            if (old != null) Object.DestroyImmediate(old);
            for (int c = house.childCount - 1; c >= 0; c--)
            {
                Transform w = house.GetChild(c);
                if (w.name.StartsWith("Window_")) DestroyChild(w, "Eyes");
            }
        }

        int[] pick = { 2, 6, 8, 11, 13 };      // 0 ve 3 açılabilen ev; 4, 9, 12 şifre ipucu taşıyor
        int made = 0;
        foreach (int idx in pick)
        {
            Transform house = houses.Find("MushroomHouse_" + idx);
            if (house == null) continue;
            List<Transform> roots = new List<Transform>();
            List<Renderer> rends = new List<Renderer>();
            for (int c = 0; c < house.childCount; c++)
            {
                Transform w = house.GetChild(c);
                if (!w.name.StartsWith("Window_")) continue;
                // ev karanlık görünsün
                Transform glass = w.Find("Glass");
                if (glass != null && winDark != null) glass.GetComponent<Renderer>().sharedMaterial = winDark;
                // pencerenin yerel +Y'si dışarı bakar; gözler camın hemen önünde, yan yana
                Transform er = Child(w, "Eyes").transform;
                er.localPosition = new Vector3(0f, 0.18f, 0f);
                foreach (float ex in new[] { -0.15f, 0.15f })
                {
                    Renderer white = Prim(er, "Eye", PrimitiveType.Sphere, eyeMat, new Vector3(ex, 0f, 0f), Quaternion.identity, new Vector3(0.2f, 0.06f, 0.13f)).GetComponent<Renderer>();
                    white.shadowCastingMode = ShadowCastingMode.Off;
                    rends.Add(white);
                    Renderer pupil = Prim(er, "Pupil", PrimitiveType.Sphere, pupilMat, new Vector3(ex, 0.03f, 0f), Quaternion.identity, new Vector3(0.05f, 0.03f, 0.11f)).GetComponent<Renderer>();
                    pupil.shadowCastingMode = ShadowCastingMode.Off;
                    rends.Add(pupil);
                }
                roots.Add(er);
            }
            if (roots.Count == 0) continue;
            WatchingEyes we = house.gameObject.AddComponent<WatchingEyes>();
            SerializedObject so = new SerializedObject(we);
            SerializedProperty ea = so.FindProperty("eyes"), ra = so.FindProperty("eyeRenderers");
            ea.arraySize = roots.Count; ra.arraySize = rends.Count;
            for (int i = 0; i < roots.Count; i++) ea.GetArrayElementAtIndex(i).objectReferenceValue = roots[i];
            for (int i = 0; i < rends.Count; i++) ra.GetArrayElementAtIndex(i).objectReferenceValue = rends[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            made++;
        }

        UnityEngine.SceneManagement.Scene scene = village.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("B3-08: " + made + " eve izleyen gözler kuruldu");
    }

    /// <summary>Çürük dokusu: açık zemin üstünde koyu lekeler ve yukarıdan aşağı süzülen koyu akıntılar (u: çevre, v: aşağıdan yukarı).
    /// cracks: şapka için tepeden kenara inen ince çatlaklar da eklenir.</summary>
    private static Texture2D DreamRotTex(string name, int seed, bool cracks)
    {
        const int S = 256;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        System.Random rnd = new System.Random(seed);
        int n = cracks ? 22 : 16;
        Vector3[] streak = new Vector3[n];          // x: u, y: uzunluk (0..1), z: genişlik
        for (int i = 0; i < n; i++) streak[i] = new Vector3((float)rnd.NextDouble(), 0.25f + (float)rnd.NextDouble() * 0.7f, cracks ? 0.004f + (float)rnd.NextDouble() * 0.006f : 0.012f + (float)rnd.NextDouble() * 0.03f);
        Color[] px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float g = 0.72f + Mathf.PerlinNoise(u * 18f, v * 18f) * 0.28f;
                // iri çürük lekeleri (u yönünde sarmalı: iki örneğin karışımı dikişi gizler)
                float b0 = Mathf.PerlinNoise(u * 5f + seed, v * 4f), b1 = Mathf.PerlinNoise((u - 1f) * 5f + seed, v * 4f);
                float blot = Mathf.Lerp(b0, b1, u);
                g *= Mathf.Lerp(1f, 0.38f, Sm(0.52f, 0.72f, blot));
                foreach (Vector3 s in streak)
                {
                    float du = Mathf.Abs(u - s.x - Mathf.Sin(v * 9f + s.x * 30f) * 0.01f); du = Mathf.Min(du, 1f - du);
                    float along = 1f - v;                                   // tepeden (v = 1) aşağı
                    if (along > s.y) continue;
                    float fade = 1f - along / s.y;                          // uca doğru incelir ve solar
                    float line = 1f - Sm(s.z * fade * 0.5f, s.z * fade, du);
                    g *= Mathf.Lerp(1f, cracks ? 0.15f : 0.3f, line * (cracks ? 1f : fade));
                }
                g *= Mathf.Lerp(0.6f, 1f, Sm(0f, 0.2f, v));                // dibe doğru kirli
                px[y * S + x] = new Color(g, g, g, 1f);
            }
        t.SetPixels(px); t.Apply();
        return SaveTex(t, name, false, false);
    }

    /// <summary>Kuru ot öbeği: tabandan çıkan, uca doğru incelip yana eğilen ince yapraklar (çift yüzlü; normaller yukarı → yumuşak, düz aydınlanma).</summary>
    private static Mesh GrassTuftMesh(string name, int seed, int blades)
    {
        System.Random rnd = new System.Random(seed);
        float F() { return (float)rnd.NextDouble(); }
        List<Vector3> v = new List<Vector3>();
        List<Vector3> nrm = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> t = new List<int>();
        const int seg = 3;
        for (int b = 0; b < blades; b++)
        {
            float a = F() * Mathf.PI * 2f, rad = F() * 0.22f;
            Vector3 foot = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
            float la = F() * Mathf.PI * 2f;
            Vector3 lean = new Vector3(Mathf.Cos(la), 0f, Mathf.Sin(la));
            Vector3 side = Vector3.Cross(Vector3.up, lean).normalized;
            float h = 0.3f + F() * 0.65f, w = 0.022f + F() * 0.02f, bendAmt = 0.15f + F() * 0.45f;
            int i0 = v.Count;
            for (int k = 0; k <= seg; k++)
            {
                float tt = k / (float)seg;
                Vector3 c = foot + Vector3.up * (h * tt) + lean * (bendAmt * tt * tt);
                float hw = w * (1f - tt * 0.92f);
                v.Add(c - side * hw); v.Add(c + side * hw);
                nrm.Add(Vector3.up); nrm.Add(Vector3.up);
                uv.Add(new Vector2(0f, tt)); uv.Add(new Vector2(1f, tt));
            }
            for (int k = 0; k < seg; k++)
            {
                int q = i0 + k * 2;
                t.Add(q); t.Add(q + 2); t.Add(q + 1); t.Add(q + 1); t.Add(q + 2); t.Add(q + 3);      // ön
                t.Add(q); t.Add(q + 1); t.Add(q + 2); t.Add(q + 1); t.Add(q + 3); t.Add(q + 2);      // arka
            }
        }
        Mesh m = new Mesh { name = name };
        m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return StoreMesh(m);
    }

    /// <summary>B3-09 (KULLANICI 2026-11-15): köy "tatlı" duruyordu → görünüm olarak gerilim. Mekanik YOK, yalnızca görsel:
    /// çürük ve koyu renkler, şapkalardan sarkan lifler, tahtayla kapatılmış pencereler, boş korkuluk direkleri, kuru dikenli ağaçlar,
    /// yıkık çitler, titreyen fener, daha koyu ve sisli ortam. Ch3_Dream açıkken; EN SON çalıştırılır (B3-02 yeniden kurulursa yeniden).</summary>
    [MenuItem("Tools/Son Hasat/B3-09 Gerilim cilasını kur")]
    public static void BuildDreamDread()
    {
        GameObject village = GameObject.Find("Ch3_Village");
        Transform houses = village != null ? village.transform.Find("Houses") : null;
        FreefallSequence fs = Object.FindFirstObjectByType<FreefallSequence>(FindObjectsInactive.Include);
        if (houses == null || fs == null) { Debug.LogError("B3-09: önce Ch3_Dream sahnesini aç ve B3-02 köyünü kur"); return; }
        System.Random rnd = new System.Random(909);
        float F() { return (float)rnd.NextDouble(); }

        // ---------- renkler: çürük, koyu, ıslak ----------
        Texture2D stemRot = DreamRotTex("T_DreamStemRot", 11, false);
        Texture2D capRot = DreamRotTex("T_DreamCapRot", 23, true);
        Color[] capCol = { new Color(0.3f, 0.05f, 0.07f), new Color(0.1f, 0.13f, 0.21f), new Color(0.2f, 0.15f, 0.06f), new Color(0.17f, 0.07f, 0.2f) };
        string[] capName = { "Mat_DreamCap_A", "Mat_DreamCap_B", "Mat_DreamCap_C", "Mat_DreamCap_D" };
        for (int i = 0; i < 4; i++) LitMat(capName[i], capCol[i], 0.55f, capRot);
        LitMat("Mat_DreamStem", new Color(0.5f, 0.47f, 0.43f), 0.22f, stemRot);
        Texture2D soil = FindTex("T_Soil");
        LitMat("Mat_DreamGround", new Color(0.55f, 0.48f, 0.62f), 0.12f, soil, 420f);
        GlowMat("Mat_DreamWindow", new Color(0.7f, 0.5f, 0.2f), new Color(0.85f, 0.5f, 0.12f));          // soluk, hasta sarısı
        GlowMat("Mat_DreamShroom", new Color(0.2f, 0.16f, 0.22f), new Color(0.055f, 0.03f, 0.075f));     // KULLANICI: turkuaz/yeşil kötü durdu → koyu, çok hafif mor ışıltı (gölgelenmesi görünür)
        GlowMat("Mat_DreamLamp", new Color(0.6f, 0.65f, 0.35f), new Color(0.8f, 1.05f, 0.3f));
        Material dark = LitMat("Mat_DreamDread", new Color(0.035f, 0.03f, 0.04f), 0.1f);
        Material rag = LitMat("Mat_DreamRag", new Color(0.16f, 0.13f, 0.1f), 0.05f);
        Material plank = LitMat("Mat_DreamPlank", new Color(0.13f, 0.09f, 0.07f), 0.1f);
        Material sack = LitMat("Mat_DreamSack", new Color(0.3f, 0.26f, 0.2f), 0.05f);
        Material winDark = FindMat("Mat_DreamWindowDark");

        // ---------- ortam: daha koyu, daha sisli ----------
        SerializedObject fso = new SerializedObject(fs);
        fso.FindProperty("groundAmbient").colorValue = new Color(0.035f, 0.03f, 0.05f);
        fso.FindProperty("groundFogColor").colorValue = new Color(0.115f, 0.085f, 0.165f);
        fso.FindProperty("groundFogDensity").floatValue = 0.052f;      // sis türü ExponentialSquared (Bölüm 1: 0.11): ~25–30 m görüş
        fso.FindProperty("groundSunFactor").floatValue = 0.12f;
        // yerdeki gök: ufuk sis renginde → uzaktaki siluetler parlak ufka karşı seçilmesin; tepede yıldızlar kalır
        Material dreamSkyMat = FindMat("Mat_DreamSky");
        if (dreamSkyMat != null)
        {
            Material gsky = Mat("Mat_DreamSkyGround", dreamSkyMat.shader);
            gsky.CopyPropertiesFromMaterial(dreamSkyMat);
            gsky.SetColor("_HorizonColor", new Color(0.13f, 0.085f, 0.16f));
            gsky.SetColor("_ZenithColor", new Color(0.03f, 0.02f, 0.07f));
            gsky.SetFloat("_HorizonHeight", 0.6f);
            gsky.SetColor("_CloudColor", new Color(0.16f, 0.11f, 0.19f));
            gsky.SetFloat("_StarBrightness", 1.3f);
            EditorUtility.SetDirty(gsky);
            fso.FindProperty("groundSky").objectReferenceValue = gsky;
        }      // KULLANICI: görüş kısıtı biraz daha (çok değil)
        fso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fs);

        // ---------- evler: sarkan lifler + tahtalı pencereler ----------
        for (int h = 0; h < houses.childCount; h++)
        {
            Transform house = houses.GetChild(h);
            Transform cap = house.Find("Cap");
            if (cap != null)
            {
                for (int c = cap.childCount - 1; c >= 0; c--) if (cap.GetChild(c).name == "Tendril") Object.DestroyImmediate(cap.GetChild(c).gameObject);
                Bounds b = cap.GetComponent<MeshFilter>().sharedMesh.bounds;
                float rim = Mathf.Min(b.extents.x, b.extents.z) * 0.93f;
                int count = 9 + rnd.Next(9);
                for (int i = 0; i < count; i++)
                {
                    float a = F() * Mathf.PI * 2f, len = 0.4f + F() * F() * 2.8f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * rim, b.min.y + 0.12f - len * 0.5f, Mathf.Sin(a) * rim);
                    Prim(cap, "Tendril", PrimitiveType.Cylinder, dark, p, Quaternion.Euler((F() - 0.5f) * 10f, 0f, (F() - 0.5f) * 10f), new Vector3(0.035f + F() * 0.04f, len * 0.5f, 0.035f + F() * 0.04f))
                        .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            bool special = house.Find("DreamDoor") != null || house.GetComponent<WatchingEyes>() != null;
            for (int c = 0; c < house.childCount; c++)
            {
                Transform w = house.GetChild(c);
                if (!w.name.StartsWith("Window_")) continue;
                DestroyChild(w, "Boards");
                if (special || F() > 0.45f) continue;
                Transform glass = w.Find("Glass");
                float size = glass != null ? glass.localScale.x : 0.7f;
                if (glass != null && winDark != null) glass.GetComponent<Renderer>().sharedMaterial = winDark;
                Transform boards = Child(w, "Boards").transform;
                boards.localPosition = new Vector3(0f, 0.2f, 0f);
                int planks = 2 + rnd.Next(2);
                for (int k = 0; k < planks; k++)
                    Prim(boards, "Plank", PrimitiveType.Cube, plank, new Vector3((F() - 0.5f) * 0.1f, k * 0.035f, (k - (planks - 1) * 0.5f) * size * 0.42f),
                        Quaternion.Euler(0f, (F() - 0.5f) * 34f, 0f), new Vector3(size * 1.55f, 0.035f, 0.15f));
            }
        }

        // ---------- köye dağılan parçalar ----------
        DestroyChild(village.transform, "Dread");
        Transform root = Child(village.transform, "Dread").transform;
        List<Vector3> taken = new List<Vector3>();
        foreach (Transform hh in houses) taken.Add(hh.localPosition);
        bool Free(Vector3 p, float r) { foreach (Vector3 q in taken) if ((q - p).sqrMagnitude < r * r) return false; return true; }
        Vector3 Spot(float rMin, float rMax, float clear)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                Vector3 p = Quaternion.Euler(0f, F() * 360f, 0f) * Vector3.forward * (rMin + F() * (rMax - rMin));
                if (Free(p, clear)) { taken.Add(p); return p; }
            }
            return Vector3.zero;
        }

        // KULLANICI (2026-11-15): boş korkuluk direkleri kaldırıldı ("çok amatör model duruyor"); geri eklenmesin

        // kuru, dikenli ağaçlar
        for (int i = 0; i < 12; i++)
        {
            Vector3 p = Spot(14f, 53f, 5f);
            if (p == Vector3.zero) continue;
            float hgt = 4f + F() * 3.5f, rad = 0.22f + F() * 0.16f;
            Vector2 bend = new Vector2((F() - 0.5f) * 3f, (F() - 0.5f) * 3f);
            Vector2[] prof = { new Vector2(rad * 1.6f, -0.2f), new Vector2(rad, 0.6f), new Vector2(rad * 0.7f, hgt * 0.5f), new Vector2(rad * 0.3f, hgt * 0.9f), new Vector2(0.02f, hgt) };
            Transform tree = MeshObj(root, "DeadTree_" + i, Lathe("DreamDeadTree_" + i, prof, 7, bend, 0.12f, i), dark).transform;
            tree.localPosition = p;
            int branches = 4 + rnd.Next(4);
            for (int k = 0; k < branches; k++)
            {
                float tt = 0.35f + F() * 0.6f, len = 0.8f + F() * 1.8f;
                Vector3 at = new Vector3(bend.x * tt * tt, hgt * tt, bend.y * tt * tt);
                Quaternion rot = Quaternion.Euler(0f, F() * 360f, 0f) * Quaternion.Euler(0f, 0f, 35f + F() * 45f);
                Prim(tree, "Branch", PrimitiveType.Cylinder, dark, at + rot * Vector3.up * (len * 0.5f), rot, new Vector3(0.05f + (1f - tt) * 0.08f, len * 0.5f, 0.05f + (1f - tt) * 0.08f));
                // dal ucunda çatal
                Quaternion r2 = rot * Quaternion.Euler(0f, 0f, (F() - 0.5f) * 90f);
                Prim(tree, "Twig", PrimitiveType.Cylinder, dark, at + rot * Vector3.up * len + r2 * Vector3.up * 0.35f, r2, new Vector3(0.03f, 0.35f, 0.03f));
            }
            CapsuleCollider tc = tree.gameObject.AddComponent<CapsuleCollider>();
            tc.radius = rad * 1.2f; tc.height = 2.6f; tc.center = new Vector3(0f, 1.2f, 0f);
        }

        // yıkık çit parçaları
        for (int i = 0; i < 16; i++)
        {
            Vector3 p = Spot(15f, 52f, 4f);
            if (p == Vector3.zero) continue;
            Transform fence = Child(root, "BrokenFence_" + i).transform;
            fence.localPosition = p;
            fence.localRotation = Quaternion.Euler(0f, F() * 360f, 0f);
            int posts = 3 + rnd.Next(3);
            for (int k = 0; k < posts; k++)
                Prim(fence, "Post", PrimitiveType.Cube, plank, new Vector3(k * 0.9f, 0.5f, 0f), Quaternion.Euler((F() - 0.5f) * 30f, 0f, (F() - 0.5f) * 36f), new Vector3(0.09f, 0.9f + F() * 0.5f, 0.09f));
            Prim(fence, "Rail", PrimitiveType.Cube, plank, new Vector3((posts - 1) * 0.45f, 0.7f, 0.05f), Quaternion.Euler(0f, 0f, (F() - 0.5f) * 16f), new Vector3((posts - 1) * 0.95f, 0.07f, 0.05f));
            if (F() > 0.4f) Prim(fence, "FallenRail", PrimitiveType.Cube, plank, new Vector3((posts - 1) * 0.3f, 0.05f, 0.4f), Quaternion.Euler(0f, F() * 60f, 0f), new Vector3(1.6f, 0.06f, 0.06f));
        }

        // ---------- zemin dolgusu (KULLANICI: "zemin güzel ama etraf çok boş") ----------
        // B3-03'ün deneme mantarları artık gereksiz (bulmacalar kuruldu) ve meydanda soluk iri kütle gibi duruyordu
        DestroyChild(village.transform, "Resizables");
        Material grassMat = LitMat("Mat_DreamDryGrass", new Color(0.17f, 0.155f, 0.11f), 0f);
        Material rockMat = LitMat("Mat_DreamRock", new Color(0.13f, 0.125f, 0.15f), 0.18f);
        Material rootMat = LitMat("Mat_DreamRoot", new Color(0.09f, 0.07f, 0.065f), 0.12f);
        Transform ground = Child(root, "GroundClutter").transform;
        const float clutterR = 54f;
        bool NearHouse(Vector3 p, float pad)
        {
            foreach (Transform hh in houses)
            {
                CapsuleCollider hc = hh.GetComponent<CapsuleCollider>();
                float rr = (hc != null ? hc.radius : 2.5f) + pad;
                if ((hh.localPosition - p).sqrMagnitude < rr * rr) return true;
            }
            return false;
        }
        Vector3 Scatter(float rMin, float pad)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                // alan içinde düzgün dağılım (karekök)
                Vector3 p = Quaternion.Euler(0f, F() * 360f, 0f) * Vector3.forward * Mathf.Lerp(rMin, clutterR, Mathf.Sqrt(F()));
                if (!NearHouse(p, pad)) return p;
            }
            return Vector3.zero;
        }

        // kuru ot öbekleri
        Mesh[] tuft = { GrassTuftMesh("DreamTuft_0", 3, 7), GrassTuftMesh("DreamTuft_1", 8, 10), GrassTuftMesh("DreamTuft_2", 15, 5) };
        for (int i = 0; i < 460; i++)
        {
            Vector3 p = Scatter(2.5f, 0.2f);
            if (p == Vector3.zero) continue;
            GameObject g = MeshObj(ground, "Tuft", tuft[rnd.Next(tuft.Length)], grassMat, false);
            g.transform.localPosition = p;
            g.transform.localRotation = Quaternion.Euler(0f, F() * 360f, 0f);
            float sc = 0.7f + F() * 1.3f;
            g.transform.localScale = new Vector3(sc, sc * (0.7f + F() * 0.8f), sc);
        }
        // taşlar (kümeler hâlinde)
        for (int i = 0; i < 34; i++)
        {
            Vector3 c = Scatter(6f, 0.6f);
            if (c == Vector3.zero) continue;
            int n = 1 + rnd.Next(4);
            for (int k = 0; k < n; k++)
            {
                float sz = 0.18f + F() * F() * 0.75f;
                Vector3 p = c + new Vector3((F() - 0.5f) * 1.6f, sz * 0.18f, (F() - 0.5f) * 1.6f);
                Prim(ground, "Rock", PrimitiveType.Sphere, rockMat, p, Quaternion.Euler(F() * 40f, F() * 360f, F() * 40f), new Vector3(sz * (0.8f + F() * 0.7f), sz * (0.45f + F() * 0.35f), sz * (0.8f + F() * 0.6f)));
            }
        }
        // parlamayan küçük koyu mantar kümeleri
        Mesh sm0 = null, sm1 = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath)) { Mesh mm = o as Mesh; if (mm == null) continue; if (mm.name == "DreamShroom_0") sm0 = mm; if (mm.name == "DreamShroom_1") sm1 = mm; }
        if (sm0 != null && sm1 != null)
            for (int i = 0; i < 60; i++)
            {
                Vector3 c = Scatter(5f, 0.3f);
                if (c == Vector3.zero) continue;
                int n = 2 + rnd.Next(4);
                Material cm = FindMat(capName[rnd.Next(4)]);
                for (int k = 0; k < n; k++)
                {
                    GameObject g = MeshObj(ground, "DarkShroom", rnd.Next(2) == 0 ? sm0 : sm1, cm, false);
                    g.transform.localPosition = c + new Vector3((F() - 0.5f) * 0.9f, 0f, (F() - 0.5f) * 0.9f);
                    g.transform.localRotation = Quaternion.Euler((F() - 0.5f) * 26f, F() * 360f, (F() - 0.5f) * 26f);
                    g.transform.localScale = Vector3.one * (0.4f + F() * 0.9f);
                }
            }
        // evlerin dibinden yere yayılan kökler
        Vector2[] rootProf = { new Vector2(0.17f, 0f), new Vector2(0.13f, 0.9f), new Vector2(0.07f, 2.2f), new Vector2(0.015f, 3.4f) };
        Mesh[] rootMesh = new Mesh[4];
        for (int i = 0; i < rootMesh.Length; i++) rootMesh[i] = Lathe("DreamRoot_" + i, rootProf, 6, new Vector2((F() - 0.5f) * 1.6f, (F() - 0.5f) * 0.5f), 0.1f, i);
        foreach (Transform hh in houses)
        {
            CapsuleCollider hc = hh.GetComponent<CapsuleCollider>();
            float rr = hc != null ? hc.radius : 2.5f;
            int n = 4 + rnd.Next(4);
            for (int k = 0; k < n; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, F() * 360f, 0f) * Vector3.forward;
                GameObject g = MeshObj(ground, "Root", rootMesh[rnd.Next(rootMesh.Length)], rootMat, false);
                g.transform.localPosition = hh.localPosition + dir * (rr * 0.8f) + Vector3.up * 0.12f;
                // kökün ekseni (yerel +Y) dışarı ve hafif aşağı bakar → ucu toprağa girer
                g.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f + 4f + F() * 5f, 0f, 0f) * Quaternion.Euler(0f, F() * 360f, 0f);
                float sc = 0.7f + F() * 0.9f;
                g.transform.localScale = new Vector3(sc, sc * (0.8f + F() * 0.5f), sc);
            }
        }

        // rüya sisi: yerde süzülen yumuşak tabakalar (yerel)
        Texture2D blob = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T_SoftBlob.png");
        if (blob == null) blob = FindTex("T_SoftCircle");
        Material mist = AlphaParticle("Mat_DreamMist", new Color(1f, 1f, 1f, 1f));
        mist.SetTexture("_BaseMap", blob);
        if (mist.HasProperty("_Cull")) mist.SetFloat("_Cull", 0f);          // yatay tabaka alttan da görünsün
        EditorUtility.SetDirty(mist);
        DreamMist dm = Child(root, "DreamMist").AddComponent<DreamMist>();
        SerializedObject mso = new SerializedObject(dm);
        mso.FindProperty("material").objectReferenceValue = mist;
        mso.ApplyModifiedPropertiesWithoutUndo();

        // fener ışığı titresin
        foreach (Light l in village.GetComponentsInChildren<Light>(true))
            if (l.GetComponent<DreamLampFlicker>() == null) l.gameObject.AddComponent<DreamLampFlicker>();

        AssetDatabase.SaveAssets();
        UnityEngine.SceneManagement.Scene scene = village.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-09: gerilim cilası kuruldu");
    }

    /// <summary>B3-10 (KULLANICI 2026-11-15): rüya alemine öğretici not (diğer bölümlerdeki not prefabı) + Bölüm 2 çıkışındaki beyaz geçiş ekranına
    /// "BÖLÜM 3: Düşler" yazısı. Not okunup kapatılınca ekranda "R: fener ışığını değiştir" bilgilendirmesi çıkar.
    /// Ch3_Dream açıkken çalıştırılır (Bölüm 2 sahnesini de açıp kaydeder, sonra Ch3'e döner). B3-02 yeniden kurulursa bu da yeniden çalıştırılmalı.</summary>
    [MenuItem("Tools/Son Hasat/B3-10 Öğretici notu ve bölüm başlığını kur")]
    public static void BuildDreamNote()
    {
        GameObject village = GameObject.Find("Ch3_Village");
        Transform plaza = village != null ? village.transform.Find("Plaza") : null;
        if (plaza == null) { Debug.LogError("B3-10: önce Ch3_Dream sahnesini aç ve B3-02 köyünü kur"); return; }
        const string prefabPath = "Assets/_Project/Prefabs/Environment/ReadableNote.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) { Debug.LogError("B3-10: not prefabı yok: " + prefabPath); return; }

        DestroyChild(plaza, "Note_Dream");
        GameObject note = (GameObject)PrefabUtility.InstantiatePrefab(prefab, plaza);
        note.name = "Note_Dream";
        // iniş noktasının hemen önünde, meydana (oyunculara) dönük
        note.transform.localPosition = new Vector3(0f, 0f, 6.2f);
        note.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        ReadableNote rn = note.GetComponent<ReadableNote>();
        SerializedObject so = new SerializedObject(rn);
        so.FindProperty("title").stringValue = "";
        so.FindProperty("body").stringValue =
            "Buraya düşen ilk sen değilsin.\n\n" +
            "Bu köyde hiçbir şey olduğu boyda kalmaz. Fenerin de burada başka türlü yanar: " +
            "bir ışığı büyütür, öteki küçültür. Soluk soluk parlayan şeylere tut, göreceksin.\n\n" +
            "Uyku bastırırsa meydandaki fener mantarının dibine gel; ışığı insanı ayık tutar. " +
            "Karanlık pencerelere yaklaşma. Bakarlarsa ışığını üstlerine tut.\n\n" +
            "Kapısında fener yanan evlerde yataklar var. Kilitlerini aç.\n" +
            "Herkes yatmadan kimse uyanamaz.";
        so.FindProperty("afterReadHint").stringValue = "R tuşu ile el feneri modunu değiştir  (normal → büyüten → küçülten)";
        so.FindProperty("afterReadHintSeconds").floatValue = 9f;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(rn);

        UnityEngine.SceneManagement.Scene scene = village.scene;
        string ch3 = scene.path;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // Bölüm 2 çıkışındaki geçiş ekranına başlık
        UnityEngine.SceneManagement.Scene ch2 = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Ch2_Transition.unity", OpenSceneMode.Single);
        ChapterEnd end = Object.FindFirstObjectByType<ChapterEnd>(FindObjectsInactive.Include);
        if (end != null)
        {
            SerializedObject eso = new SerializedObject(end);
            eso.FindProperty("nextTitle").stringValue = "BÖLÜM 3";      // Bölüm 2 başlığıyla aynı yazım: "BÖLÜM N: Ad"
            eso.FindProperty("nextSubtitle").stringValue = "Düşler";
            eso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(ch2);
            EditorSceneManager.SaveScene(ch2);
        }
        else Debug.LogError("B3-10: Ch2'de ChapterEnd bulunamadı");
        EditorSceneManager.OpenScene(ch3, OpenSceneMode.Single);
        Debug.Log("B3-10: öğretici not ve bölüm başlığı kuruldu");
    }

    /// <summary>Bir eğri boyunca tek parça boru (kök) ekler: her noktada 'radii' yarıçaplı halka; çerçeve paralel taşımayla (bükülmesiz).</summary>
    private static void AppendTube(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, List<Vector3> pts, List<float> radii, int sides)
    {
        int count = pts.Count;
        if (count < 2) return;
        int start = v.Count;
        Vector3 prevT = (pts[1] - pts[0]).normalized;
        Vector3 N = Vector3.Cross(prevT, Mathf.Abs(prevT.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        float len = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 T = (i < count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
            if (i > 0) { N = (N - T * Vector3.Dot(N, T)).normalized; len += Vector3.Distance(pts[i], pts[i - 1]); }
            Vector3 B = Vector3.Cross(T, N);
            for (int k = 0; k <= sides; k++)
            {
                float a = k / (float)sides * Mathf.PI * 2f;
                Vector3 d = N * Mathf.Cos(a) + B * Mathf.Sin(a);
                v.Add(pts[i] + d * radii[i]);
                n.Add(d);
                uv.Add(new Vector2(k / (float)sides, len * 2f));
            }
        }
        int w = sides + 1;
        for (int i = 0; i < count - 1; i++)
            for (int k = 0; k < sides; k++)
            {
                int a0 = start + i * w + k, b0 = a0 + 1, c0 = a0 + w, d0 = c0 + 1;
                // halka açısı T çevresinde sağ el yönünde artar → dışa bakan yüz için sıra a-b-c (Lathe'nin tersi)
                t.Add(a0); t.Add(b0); t.Add(c0);
                t.Add(b0); t.Add(d0); t.Add(c0);
            }
    }

    /// <summary>B3 (KULLANICI 2026-11-15: "yataklardaki kafesi güzelleştir; köyün otları sanki yatağı sarmış gibi olsun"):
    /// yatağın kilidi = yatağı saran tek parça, eğri, sivrilen kuru kökler (bir mesh) + kuru ot + köklerde sönük mor tomurcuklar (kilit parıltısı).
    /// "Lock" yatağın tabanındadır; DreamBed kilit açılınca onu yere doğru çekip büzer. Döner: parlayan tomurcuklar.</summary>
    private static List<Renderer> BuildBedOvergrowth(Transform bed, int seed, out Transform lockRoot)
    {
        System.Random rnd = new System.Random(seed);
        float F() { return (float)rnd.NextDouble(); }
        Material rootMat = LitMat("Mat_DreamBedRoot", new Color(0.16f, 0.125f, 0.105f), 0f, FindTex("T_DreamStemRot"), 1f);
        NoShine(rootMat);
        Material grassMat = LitMat("Mat_DreamDryGrass", new Color(0.17f, 0.155f, 0.11f), 0f);
        Material bud = GlowMat("Mat_DreamLock", new Color(0.12f, 0.05f, 0.12f), new Color(1.1f, 0.4f, 1.3f));
        Mesh[] tuft = { GrassTuftMesh("DreamTuft_0", 3, 7), GrassTuftMesh("DreamTuft_1", 8, 10), GrassTuftMesh("DreamTuft_2", 15, 5) };

        DestroyChild(bed, "Lock");
        lockRoot = Child(bed, "Lock").transform;
        lockRoot.localPosition = Vector3.zero;

        List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        List<Vector2> UV = new List<Vector2>();
        List<int> T = new List<int>();
        List<Vector3> budSpots = new List<Vector3>();
        const int sides = 7;

        // yatağı enine ve boyuna aşan kökler: yerden çıkar, yatağın üstünden kavisle geçer, karşıda yere girer; düzensiz kıvrımlı
        void Arch(Vector3 a, Vector3 b, float peak, float rEnd, float rMid, int samples)
        {
            Vector3 side = Vector3.Cross(b - a, Vector3.up).normalized;
            float lean = (F() - 0.5f) * 0.5f, f1 = 2f + F() * 3f, f2 = 5f + F() * 5f, p1 = F() * 6.28f, p2 = F() * 6.28f;
            float skew = 0.35f + F() * 0.3f;              // tepe ortada değil
            List<Vector3> pts = new List<Vector3>();
            List<float> rad = new List<float>();
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float tt = t < skew ? 0.5f * t / skew : 0.5f + 0.5f * (t - skew) / (1f - skew);
                float h = Mathf.Sin(tt * Mathf.PI);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y += h * peak + Mathf.Sin(t * f2 + p2) * 0.04f * h;
                p += side * (h * lean + Mathf.Sin(t * f1 + p1) * 0.07f * h);
                pts.Add(p);
                rad.Add(Mathf.Lerp(rEnd, rMid, h) * (0.85f + 0.3f * Mathf.PerlinNoise(t * 6f, p1)));
            }
            pts[0] += Vector3.down * 0.08f; pts[samples] += Vector3.down * 0.08f;      // uçlar toprağa girsin
            AppendTube(V, N, UV, T, pts, rad, sides);
            budSpots.Add(pts[Mathf.RoundToInt(samples * skew)] + Vector3.up * rMid);
        }
        // serbest uçlu kıvrık sürgün: yerden çıkar, yatağın kenarından yukarı kıvrılır, ucu sivrilir
        void Tendril(Vector3 a, Vector3 dir, float height, float r0, int samples)
        {
            float curl = (F() < 0.5f ? -1f : 1f) * (2.5f + F() * 2f), ph = F() * 6.28f;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            List<Vector3> pts = new List<Vector3>();
            List<float> rad = new List<float>();
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 p = a + Vector3.up * (Mathf.Sin(t * Mathf.PI * 0.6f) * height) + dir * (t * 0.7f)
                          + side * (Mathf.Sin(t * curl + ph) * 0.12f * t) + Vector3.up * (Mathf.Cos(t * curl + ph) * 0.06f * t);
                pts.Add(p);
                rad.Add(Mathf.Lerp(r0, 0.004f, t * t));
            }
            pts[0] += Vector3.down * 0.06f;
            AppendTube(V, N, UV, T, pts, rad, 5);
        }
        // yatak ayağına sarılan sarmal
        void Spiral(Vector3 leg, float r0)
        {
            float turns = 1.6f + F() * 0.8f, ph = F() * 6.28f, top = 0.45f + F() * 0.25f;
            List<Vector3> pts = new List<Vector3>();
            List<float> rad = new List<float>();
            const int samples = 22;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples, a = ph + t * turns * Mathf.PI * 2f;
                float rr = Mathf.Lerp(0.3f, 0.11f, Mathf.Clamp01(t * 3f));          // yerden yaklaşıp ayağa sarılır
                pts.Add(new Vector3(leg.x + Mathf.Cos(a) * rr, -0.04f + t * top, leg.z + Mathf.Sin(a) * rr));
                rad.Add(Mathf.Lerp(r0, r0 * 0.35f, t));
            }
            AppendTube(V, N, UV, T, pts, rad, 5);
        }

        int across = 5;
        for (int i = 0; i < across; i++)
        {
            float z = Mathf.Lerp(-0.95f, 0.95f, i / (float)(across - 1)) + (F() - 0.5f) * 0.3f;
            Vector3 a = new Vector3(-0.85f - F() * 0.25f, 0f, z + (F() - 0.5f) * 0.6f);
            Vector3 b = new Vector3(0.85f + F() * 0.25f, 0f, z + (F() - 0.5f) * 0.6f);
            if (F() < 0.5f) { Vector3 tmp = a; a = b; b = tmp; }
            Arch(a, b, 0.95f + F() * 0.45f, 0.075f + F() * 0.03f, 0.035f + F() * 0.015f, 26);
        }
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -0.3f : 0.32f) + (F() - 0.5f) * 0.2f;
            Arch(new Vector3(x + (F() - 0.5f) * 0.7f, 0f, -1.35f - F() * 0.2f), new Vector3(x + (F() - 0.5f) * 0.7f, 0f, 1.35f + F() * 0.2f),
                 0.85f + F() * 0.3f, 0.065f, 0.03f, 34);
        }
        foreach (float sx in new[] { -0.58f, 0.58f })
            foreach (float sz in new[] { -1.08f, 1.08f })
                Spiral(new Vector3(sx, 0f, sz), 0.04f + F() * 0.015f);
        for (int i = 0; i < 7; i++)
        {
            float a = F() * Mathf.PI * 2f;
            Vector3 p = new Vector3(Mathf.Cos(a) * 0.95f, 0f, Mathf.Sin(a) * 1.4f);
            Vector3 inward = (-p).normalized;
            Tendril(p, inward, 0.7f + F() * 0.6f, 0.035f + F() * 0.015f, 16);
        }

        Mesh m = new Mesh { name = "DreamBedRoots_" + seed };
        m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
        m.RecalculateBounds();
        GameObject roots = MeshObj(lockRoot, "Roots", StoreMesh(m), rootMat);

        // kuru ot: yatağın dibinde ve üstünde
        for (int i = 0; i < 18; i++)
        {
            float a = F() * Mathf.PI * 2f;
            Vector3 p = new Vector3(Mathf.Cos(a) * (0.75f + F() * 0.4f), 0f, Mathf.Sin(a) * (1.25f + F() * 0.35f));
            GameObject g = MeshObj(lockRoot, "Tuft", tuft[rnd.Next(tuft.Length)], grassMat, false);
            g.transform.localPosition = p;
            g.transform.localRotation = Quaternion.Euler(0f, F() * 360f, 0f);
            float sc = 0.8f + F() * 0.9f;
            g.transform.localScale = new Vector3(sc, sc * (0.8f + F() * 0.6f), sc);
        }
        for (int i = 0; i < 6; i++)
        {
            GameObject g = MeshObj(lockRoot, "Tuft", tuft[rnd.Next(tuft.Length)], grassMat, false);
            g.transform.localPosition = new Vector3((F() - 0.5f) * 0.9f, 0.68f, (F() - 0.5f) * 1.8f);
            g.transform.localRotation = Quaternion.Euler(0f, F() * 360f, 0f);
            g.transform.localScale = Vector3.one * (0.5f + F() * 0.5f);
        }
        // kilit belirtisi: köklerin tepelerinde sönük, nabız gibi atan mor tomurcuklar
        List<Renderer> buds = new List<Renderer>();
        foreach (Vector3 s in budSpots)
        {
            if (F() < 0.3f) continue;
            Renderer r = Prim(lockRoot, "Bud", PrimitiveType.Sphere, bud, s, Quaternion.Euler(F() * 360f, F() * 360f, 0f),
                new Vector3(0.07f, 0.09f, 0.07f) * (0.8f + F() * 0.5f)).GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            buds.Add(r);
        }
        return buds;
    }

    /// <summary>B3: mevcut dört yatağın kilidini (eski pembe kafes) sarmaşık kilidiyle değiştirir; bulmacalara dokunmaz. Ch3_Dream açıkken.</summary>
    [MenuItem("Tools/Son Hasat/B3-05f Yatak sarmaşıklarını kur")]
    public static void BuildBedLocks()
    {
        DreamBed[] beds = Object.FindObjectsByType<DreamBed>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
        if (beds.Length == 0) { Debug.LogError("B3-05f: yatak yok (önce Ch3_Dream sahnesini aç, B3-05)"); return; }
        foreach (DreamBed db in beds)
        {
            int idx = 0;
            int.TryParse(db.name.Replace("Bed_", ""), out idx);
            Transform lockRoot;
            List<Renderer> buds = BuildBedOvergrowth(db.transform, 500 + idx * 37, out lockRoot);
            SerializedObject bso = new SerializedObject(db);
            bso.FindProperty("lockRoot").objectReferenceValue = lockRoot;
            bso.FindProperty("lockGlow").colorValue = new Color(1.1f, 0.4f, 1.3f);
            SerializedProperty arr = bso.FindProperty("lockRenderers");
            arr.arraySize = buds.Count;
            for (int k = 0; k < buds.Count; k++) arr.GetArrayElementAtIndex(k).objectReferenceValue = buds[k];
            bso.ApplyModifiedPropertiesWithoutUndo();
        }
        UnityEngine.SceneManagement.Scene scene = beds[0].gameObject.scene;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("B3-05f: " + beds.Length + " yatağa sarmaşık kilidi kuruldu");
    }

    // ------------------------------------------------------------------ 4) mağara yüzü

    private static Vector2 Almond(float t, float a, float b)
    {
        float s = Mathf.Sin(t);
        return new Vector2(a * Mathf.Cos(t), b * s * (0.5f + 0.5f * s * s));
    }

    private static Mesh BuildEyeRim()
    {
        Flat f = new Flat();
        const int n = 18;
        for (int i = 0; i < n; i++)
        {
            float t0 = i * Mathf.PI * 2f / n, t1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3[] o = new Vector3[2], oi = new Vector3[2], inn = new Vector3[2];
            for (int k = 0; k < 2; k++)
            {
                float t = k == 0 ? t0 : t1;
                Vector2 po = Almond(t, 1.5f, 0.95f), pm = Almond(t, 1.28f, 0.74f), pi = Almond(t, 1.02f, 0.5f);
                float jitter = (N3(new Vector3(po.x * 1.3f, po.y * 1.3f, 4.2f)) - 0.5f) * 0.16f;
                o[k] = new Vector3(po.x * (1f + jitter), po.y * (1f + jitter), 0f);
                oi[k] = new Vector3(pm.x, pm.y, 0.42f + jitter);
                inn[k] = new Vector3(pi.x, pi.y, 0.3f);
            }
            Vector3 radial = new Vector3(o[0].x + o[1].x, o[0].y + o[1].y, 0f);
            f.Quad(o[0], o[1], oi[0], oi[1], radial + Vector3.forward * 2f);                       // dış pah
            f.Quad(oi[0], oi[1], inn[0], inn[1], Vector3.forward);                                  // ön yüz
            f.Quad(inn[0], inn[1], new Vector3(inn[0].x, inn[0].y, -0.05f), new Vector3(inn[1].x, inn[1].y, -0.05f), -radial); // göz çukuru iç duvarı
            f.Quad(o[0], o[1], new Vector3(o[0].x, o[0].y, -0.3f), new Vector3(o[1].x, o[1].y, -0.3f), radial);
        }
        return StoreMesh(f.Build("CaveFace_EyeRim"));
    }

    private static Mesh BuildEyeGlow()
    {
        Flat f = new Flat();
        const int n = 18;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = Almond(i * Mathf.PI * 2f / n, 1.04f, 0.52f), b = Almond((i + 1) * Mathf.PI * 2f / n, 1.04f, 0.52f);
            f.Tri(Vector3.zero, new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f), Vector3.forward);
        }
        return StoreMesh(f.Build("CaveFace_EyeGlow"));
    }

    private static Mesh BuildPupil()
    {
        Flat f = new Flat();
        Vector3[] p = { new Vector3(0f, 0.5f, 0f), new Vector3(0.15f, 0f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(-0.15f, 0f, 0f) };
        Vector3 tip = new Vector3(0f, 0f, 0.14f);
        for (int i = 0; i < 4; i++) f.Tri(p[i], p[(i + 1) % 4], tip, (p[i] + p[(i + 1) % 4]) + Vector3.forward);
        return StoreMesh(f.Build("CaveFace_Pupil"));
    }

    private static Mesh BuildSpike(string name, float radius, float length, float bend, float seed)
    {
        Flat f = new Flat();
        const int m = 6, rings = 5;
        Vector3[][] ring = new Vector3[rings][];
        Vector3[] centers = new Vector3[rings];
        for (int r = 0; r < rings; r++)
        {
            float t = r / (float)rings;
            centers[r] = Vector3.up * (r == 0 ? -0.25f : length * t) + Vector3.right * (bend * t * t);
            ring[r] = new Vector3[m];
            float rad = radius * Mathf.Pow(1f - t, 0.75f);
            for (int i = 0; i < m; i++)
            {
                float a = i * Mathf.PI * 2f / m + r * 0.25f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ring[r][i] = centers[r] + d * rad * (0.85f + 0.3f * N3(d * 2f + Vector3.up * (r + seed)));
            }
        }
        for (int r = 0; r < rings - 1; r++)
            for (int i = 0; i < m; i++)
            {
                int n = (i + 1) % m;
                f.Quad(ring[r][i], ring[r][n], ring[r + 1][i], ring[r + 1][n], (ring[r][i] + ring[r][n]) * 0.5f - centers[r]);
            }
        Vector3 apex = Vector3.up * length + Vector3.right * bend;
        for (int i = 0; i < m; i++)
        {
            int n = (i + 1) % m;
            f.Tri(ring[rings - 1][i], ring[rings - 1][n], apex, (ring[rings - 1][i] + ring[rings - 1][n]) * 0.5f - centers[rings - 1]);
        }
        return StoreMesh(f.Build(name));
    }

    [MenuItem("Tools/Son Hasat/V-01 Mağara yüzü")]
    public static void BuildCaveFace()
    {
        rng = new System.Random(6606);
        GameObject cave = GameObject.Find("CaveEntrance");
        if (cave == null) { Debug.LogError("V01: CaveEntrance yok"); return; }
        Component caveComp = cave.GetComponent<CaveEntrance>();

        // eski yüzü ve glowRenderers içindeki eski göz kayıtlarını temizle
        SerializedObject so = new SerializedObject(caveComp);
        SerializedProperty arr = so.FindProperty("glowRenderers");
        Transform oldFace = cave.transform.Find("CaveFace");
        for (int i = arr.arraySize - 1; i >= 0; i--)
        {
            Object r = arr.GetArrayElementAtIndex(i).objectReferenceValue;
            Renderer rr = r as Renderer;
            if (rr == null || (oldFace != null && rr.transform.IsChildOf(oldFace)))
            {
                arr.GetArrayElementAtIndex(i).objectReferenceValue = null;
                arr.DeleteArrayElementAtIndex(i);
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (oldFace != null) Object.DestroyImmediate(oldFace.gameObject);

        Material stone = ShrineStoneMat();
        Material eyeMat = Mat("Mat_CaveEye", Shader.Find("Universal Render Pipeline/Lit"));
        eyeMat.SetColor("_BaseColor", new Color(0.03f, 0.015f, 0.015f));
        eyeMat.SetFloat("_Smoothness", 0.88f);
        // URP, ışıma bayrağı None/siyah olan materyalden _EMISSION anahtarını söker → çok koyu ama siyah olmayan başlangıç değeri
        eyeMat.SetColor("_EmissionColor", new Color(0.02f, 0.003f, 0.002f));
        eyeMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        eyeMat.EnableKeyword("_EMISSION");

        Mesh forehead = BuildStone("CaveFace_Forehead", new StoneShape { w = 11.2f, h = 4.6f, d = 0.9f, taper = 0.74f, lean = 0f, slant = 0.08f, amp = 0.38f, freq = 0.75f, seed = new Vector3(3f, 9f, 5f) }, 14, 7, 2);
        StoneShape browShape = new StoneShape { w = 1.5f, h = 3.9f, d = 1.2f, taper = 0.4f, lean = 0.25f, slant = 0.2f, amp = 0.08f, freq = 1.4f, seed = new Vector3(11f, 2f, 7f) };
        Mesh brow = BuildStone("CaveFace_Brow", browShape, 3, 9, 3);
        Mesh rim = BuildEyeRim(), glow = BuildEyeGlow(), pupil = BuildPupil();

        // yerel +Z oyuncuya (güneye) bakar
        GameObject face = Child(cave.transform, "CaveFace");
        // KULLANICI: alın taşı arkadaki kaya kemerinin içine giriyordu (dokular iç içe) → incelip öne alındı
        face.transform.position = new Vector3(cave.transform.position.x, 0f, 78.95f);
        face.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject fh = Child(face.transform, "Forehead");
        fh.transform.localPosition = new Vector3(0f, 4.35f, 0f);
        AddMesh(fh, forehead, stone, true);

        List<Renderer> glows = new List<Renderer>();
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject eye = Child(face.transform, side < 0 ? "Eye_L" : "Eye_R");
            eye.transform.localPosition = new Vector3(side * 2.75f, 6.55f, 0.56f);
            eye.transform.localRotation = Quaternion.Euler(0f, 0f, side * 14f);
            AddMesh(eye, rim, stone, true);
            GameObject g = Child(eye.transform, "Glow");
            g.transform.localPosition = new Vector3(0f, 0f, 0.04f);
            glows.Add(AddMesh(g, glow, eyeMat, false));
            GameObject p = Child(eye.transform, "Pupil");
            p.transform.localPosition = new Vector3(-side * 0.12f, -0.03f, 0.05f);
            AddMesh(p, pupil, stone, false);

            GameObject b = Child(face.transform, side < 0 ? "Brow_L" : "Brow_R");
            b.transform.localPosition = new Vector3(side * 0.7f, 7.05f, 0.72f);
            b.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 180f : 0f, 0f) * Quaternion.Euler(0f, 0f, -68f);
            AddMesh(b, brow, stone, true);

            GameObject tusk = Child(face.transform, side < 0 ? "Tusk_L" : "Tusk_R");
            tusk.transform.localPosition = new Vector3(side * 2.95f, 0f, 0.45f);
            tusk.transform.localRotation = Quaternion.Euler(-6f, 0f, side * 10f);
            AddMesh(tusk, BuildSpike("CaveFace_Tusk_" + (side < 0 ? "L" : "R"), 0.46f, 2.9f, -side * 0.35f, side + 3f), stone, true);
            CapsuleCollider cc = tusk.AddComponent<CapsuleCollider>();
            cc.center = new Vector3(0f, 1.1f, 0f); cc.height = 2.4f; cc.radius = 0.3f; cc.direction = 1;
        }

        float[] fx = { -1.75f, -0.9f, 0f, 0.88f, 1.72f };
        float[] fl = { 1.7f, 1.05f, 0.8f, 1.1f, 1.65f };
        for (int i = 0; i < fx.Length; i++)
        {
            GameObject fang = Child(face.transform, "Fang_" + (i + 1));
            fang.transform.localPosition = new Vector3(fx[i], 4.5f, 0.12f + R(-0.06f, 0.06f));
            fang.transform.localRotation = Quaternion.Euler(R(-5f, 5f), R(0f, 360f), 180f + R(-6f, 6f));
            AddMesh(fang, BuildSpike("CaveFace_Fang_" + (i + 1), R(0.3f, 0.38f), fl[i], R(-0.06f, 0.06f), i * 1.7f), stone, true);
        }

        so = new SerializedObject(caveComp);
        arr = so.FindProperty("glowRenderers");
        foreach (Renderer r in glows)
        {
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = r;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        MarkDirty();
        Debug.Log("V01: mağara yüzü kuruldu");
    }
}
