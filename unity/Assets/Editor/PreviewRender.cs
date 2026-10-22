using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Renders the water on a made-up valley to PNG files, to judge the shader without the game.
// The mesh has the same layout as the plugin's: one vertex per metre, uv0 world x/z, uv1 depth,
// foam, shore, uv2 current. Run with a display (xvfb-run), not -nographics:
//   FW_PREVIEW_OUT=/some/folder Unity -batchmode -quit -projectPath unity -executeMethod PreviewRender.Render -logFile -
public static class PreviewRender
{
    private const int Size = 96;

    public static void Render()
    {
        try
        {
            string output = Environment.GetEnvironmentVariable("FW_PREVIEW_OUT") ?? Path.GetFullPath("preview");
            Directory.CreateDirectory(output);
            Material water = BuildBundles.Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            float[,] ground = Ground();
            Water(ground, out float[,] depth, out Vector2[,] current);
            MakeGround(ground);
            MakeWater(ground, depth, current, water);
            Light();

            Shot(output, "overview", new Vector3(48f, 70f, -30f), new Vector3(48f, 20f, 48f), 1280, 720);
            Shot(output, "stream", new Vector3(30f, 42f, 30f), new Vector3(50f, 30f, 55f), 1280, 720);
            Shot(output, "pool", new Vector3(60f, 24f, 10f), new Vector3(48f, 14f, 22f), 1280, 720);
            Shot(output, "above", new Vector3(52f, 40f, 48f), new Vector3(50f, 26f, 58f), 1280, 720);
            Debug.Log($"FlowingWater: preview written to {output}");
        }
        catch (Exception e)
        {
            Debug.LogError($"FlowingWater: preview failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    // A valley falling towards -z, with a meandering low line, bumps, and a basin near the bottom.
    private static float[,] Ground()
    {
        var h = new float[Size + 1, Size + 1];
        for (int z = 0; z <= Size; z++)
        {
            for (int x = 0; x <= Size; x++)
            {
                float thalweg = 48f + (10f * Mathf.Sin(z * 0.07f));
                float across = Mathf.Abs(x - thalweg);
                float height = 6f + (z * 0.32f) + (across * across * 0.012f) + (1.2f * Mathf.PerlinNoise(x * 0.11f, z * 0.11f));
                float basin = Vector2.Distance(new Vector2(x, z), new Vector2(48f, 20f));
                height -= 5f * Mathf.Exp(-basin * basin / 120f);
                h[x, z] = height;
            }
        }

        return h;
    }

    // A plausible stream: water along the low line, a pool filling the basin to its rim.
    private static void Water(float[,] ground, out float[,] depth, out Vector2[,] current)
    {
        depth = new float[Size + 1, Size + 1];
        current = new Vector2[Size + 1, Size + 1];
        const float poolLevel = 12.2f;
        for (int z = 0; z <= Size; z++)
        {
            float thalweg = 48f + (10f * Mathf.Sin(z * 0.07f));
            for (int x = 0; x <= Size; x++)
            {
                float across = Mathf.Abs(x - thalweg);
                float stream = z > 22 && z < 92 ? Mathf.Max(0f, 0.6f - (across * across * 0.012f)) : 0f;
                float pool = Mathf.Max(0f, poolLevel - ground[x, z]);
                float d = Mathf.Max(stream, pool);
                depth[x, z] = d;
                if (stream > pool && stream > 0f)
                {
                    float dx = 0.7f * Mathf.Cos(z * 0.07f);
                    var along = new Vector2(-dx, -1f);
                    current[x, z] = along.normalized * (1.8f * (stream / 0.6f));
                }
                else if (pool > 0f)
                {
                    current[x, z] = new Vector2(0f, -0.15f);
                }
            }
        }
    }

    private static void MakeGround(float[,] ground)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int z = 0; z <= Size; z++)
        {
            for (int x = 0; x <= Size; x++)
            {
                vertices.Add(new Vector3(x, ground[x, z], z));
            }
        }

        Grid(triangles, (x, z) => true);
        mesh.SetVertices(vertices);
        var uv = new List<Vector2>();
        foreach (Vector3 v in vertices)
        {
            uv.Add(new Vector2(v.x / 8f, v.z / 8f));
        }

        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        var go = new GameObject("Ground");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(Shader.Find("Standard")) { color = new Color(0.32f, 0.36f, 0.22f) };
        material.SetTexture("_MainTex", Checker());
        material.SetFloat("_Glossiness", 0.1f);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Stones and grass in a coarse pattern, so that what lies under the water shows through it.
    private static Texture2D Checker()
    {
        var texture = new Texture2D(64, 64) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.21f, y * 0.21f);
                texture.SetPixel(x, y, n > 0.55f ? new Color(0.62f, 0.6f, 0.55f) : new Color(0.75f + (n * 0.3f), 0.8f, 0.6f));
            }
        }

        texture.Apply();
        return texture;
    }

    private static void MakeWater(float[,] ground, float[,] depth, Vector2[,] current, Material water)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        var vertices = new List<Vector3>();
        var uv0 = new List<Vector2>();
        var uv1 = new List<Vector4>();
        var uv2 = new List<Vector2>();
        for (int z = 0; z <= Size; z++)
        {
            for (int x = 0; x <= Size; x++)
            {
                float d = depth[x, z];
                int wet = 0;
                float carried = float.MinValue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = Mathf.Clamp(x + dx, 0, Size);
                        int nz = Mathf.Clamp(z + dz, 0, Size);
                        if (depth[nx, nz] > 0.02f)
                        {
                            wet++;
                            carried = Mathf.Max(carried, ground[nx, nz] + depth[nx, nz]);
                        }
                    }
                }

                float y = d > 0.02f ? ground[x, z] + d : carried > float.MinValue ? Mathf.Min(carried, ground[x, z] - 0.02f) : ground[x, z] - 0.4f;
                float shore = d > 0.02f ? Mathf.Clamp01((wet - 1) / 8f) : 0f;
                vertices.Add(new Vector3(x, y, z));
                uv0.Add(new Vector2(x, z));
                uv1.Add(new Vector4(d, 0f, shore, 0f));
                uv2.Add(current[x, z]);
            }
        }

        var triangles = new List<int>();
        Grid(triangles, (x, z) => depth[x, z] > 0.02f || depth[x + 1, z] > 0.02f || depth[x, z + 1] > 0.02f || depth[x + 1, z + 1] > 0.02f);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetUVs(2, uv2);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        var go = new GameObject("Water");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = water;
    }

    private static void Grid(List<int> triangles, Func<int, int, bool> keep)
    {
        int pitch = Size + 1;
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!keep(x, z))
                {
                    continue;
                }

                int a = (z * pitch) + x;
                int b = a + 1;
                int c = a + pitch;
                int d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        }
    }

    private static void Light()
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.94f, 0.82f);
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(38f, -30f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.006f;
        RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.74f);
        DynamicGI.UpdateEnvironment();
    }

    private static void Shot(string folder, string name, Vector3 from, Vector3 at, int width, int height)
    {
        var camera = new GameObject("Camera " + name).AddComponent<Camera>();
        camera.transform.position = from;
        camera.transform.LookAt(at);
        camera.fieldOfView = 55f;
        camera.farClipPlane = 600f;
        camera.clearFlags = CameraClearFlags.Skybox;
        // As the game's camera: the water reads the scene's depth behind it.
        camera.depthTextureMode = DepthTextureMode.DepthNormals;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }
}
