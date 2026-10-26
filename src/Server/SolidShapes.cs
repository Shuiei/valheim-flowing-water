using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// The solid shapes of the game's objects, as the water sees them: what players bump into, read once per kind of
/// object from its prefab's colliders (those on the layers the players' game casts its ground on, triggers left out;
/// nothing that moves by itself), then placed where each object stands. A box collider is a box; a mesh collider is
/// its mesh (<see cref="SolidMesh"/>), or the box round it where the mesh cannot be read; a sphere or a capsule is
/// the box round it. A place's own objects (a ruin, a cave's rock) come from its location's prefab through its proxy.
/// A rock that is mined bit by bit leaves out the bits that are gone. An open door holds nothing back.
/// </summary>
internal sealed class SolidShapes
{
    /// <summary>What is solid: the layers the players' game casts the ground under its water on too.</summary>
    public static readonly string[] SolidLayers = ["Default", "static_solid", "Default_small", "piece", "terrain"];

    private static readonly int LocationProxyHash = "LocationProxy".GetStableHashCode();

    private static readonly Shape None = new([], false, 0);

    private readonly Dictionary<int, Shape> _byPrefab = [];

    private readonly Dictionary<int, Shape> _byLocation = [];

    // The game's locations by the hash of their name (as a proxy names its), read once there are any.
    private Dictionary<int, ZoneSystem.ZoneLocation> _locations;

    // Each collider mesh read, by the game's mesh, shared by every kind of object that uses it.
    private readonly Dictionary<Mesh, SolidMesh> _meshes = [];

    // The kinds of object told of once: a mesh that cannot be read, a rock whose bits do not match its health.
    private readonly HashSet<int> _unreadableTold = [];

    private readonly HashSet<int> _healthTold = [];

    private readonly int _mask = LayerMask.GetMask(SolidLayers);

    private float _farthest;

    private bool _prefabsMeasured;

    /// <summary>How many kinds of object (prefabs) the water takes as solid, so far.</summary>
    public int Kinds { get; private set; }

    /// <summary>How many of them by the shape of a mesh.</summary>
    public int MeshKinds { get; private set; }

    /// <summary>
    /// How far the solid shapes of the game's objects reach from their place, metres: the farthest of its prefabs,
    /// unscaled (measured the first time this is asked). Known before a zone's objects are read, so that a zone
    /// holding a long piece is never left out; an object scaled to reach farther, or a place's prefab, is told by
    /// <see cref="Reach(ZDO)"/>.
    /// </summary>
    public float Farthest
    {
        get
        {
            if (!_prefabsMeasured && ZNetScene.instance != null)
            {
                _prefabsMeasured = true;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string farthest = "nothing";
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab != null)
                    {
                        float reach = OfPrefab(prefab.name.GetStableHashCode()).Reach;
                        if (reach > _farthest)
                        {
                            _farthest = reach;
                            farthest = prefab.name;
                        }
                    }
                }

                FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: {Kinds} kinds of object are solid for the water ({MeshKinds} by the shape of their mesh), read in {watch.ElapsedMilliseconds} ms; they reach up to {_farthest:0.0} m from their place ({farthest})"));
            }

            return _farthest;
        }
    }

    /// <summary>
    /// An object's scale (one where it was not scaled): as the game reads it when an object wakes, its scale, or
    /// else the one number a mod scaling pieces may have written for all three.
    /// </summary>
    public static Vector3 Scale(ZDO zdo) => zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.one * zdo.GetFloat(ZDOVars.s_scaleScalarHash, 1f));

    /// <summary>Adds the solids of the object this ZDO is (none if it is no solid object) to the list.</summary>
    public void AddSolids(ZDO zdo, List<Solid> solids)
    {
        Shape shape = Of(zdo);
        if (shape.Parts.Length == 0 || (shape.Door && zdo.GetInt(ZDOVars.s_state) != 0))
        {
            return;
        }

        Vector3 scale = Scale(zdo);
        if (scale.x == 0f || scale.y == 0f || scale.z == 0f)
        {
            return;
        }

        var place = Matrix4x4.TRS(zdo.GetPosition(), zdo.GetRotation(), scale);
        bool[] gone = shape.Areas > 0 ? Gone(zdo, shape) : null;
        foreach (Part part in shape.Parts)
        {
            if (gone != null && part.Area >= 0 && gone[part.Area])
            {
                continue;
            }

            Matrix4x4 m = place * part.Local;
            if (part.Mesh != null)
            {
                solids.Add(new SolidMeshInstance(part.Mesh, Rows(m)));
            }
            else
            {
                Vector3 x = m.MultiplyVector(Vector3.right);
                Vector3 y = m.MultiplyVector(Vector3.up);
                Vector3 z = m.MultiplyVector(Vector3.forward);
                solids.Add(new SolidBox(
                    ToVec(m.MultiplyPoint3x4(part.Centre)),
                    ToVec(x.normalized),
                    ToVec(y.normalized),
                    ToVec(z.normalized),
                    new Vec3(part.Half.x * x.magnitude, part.Half.y * y.magnitude, part.Half.z * z.magnitude)));
            }
        }
    }

    /// <summary>Whether the water takes this object as solid (it has a shape).</summary>
    public bool Has(ZDO zdo) => Of(zdo).Parts.Length > 0;

    /// <summary>How far the object's solids reach from its place at most, metres (its scale taken in).</summary>
    public float Reach(ZDO zdo) => Reach(zdo, Scale(zdo));

    /// <summary>How far the object's solids reach from its place at most, metres, at this scale (<see cref="Scale"/>, read already).</summary>
    public float Reach(ZDO zdo, Vector3 scale) => Of(zdo).Reach * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

    /// <summary>
    /// What of the object's own state changes its solids, as one number: a door open or closed, which bits of a
    /// rock are gone (not their health: a pickaxe's blow that takes no bit changes nothing).
    /// </summary>
    public int State(ZDO zdo)
    {
        Shape shape = Of(zdo);
        int state = shape.Door ? zdo.GetInt(ZDOVars.s_state) : 0;
        if (shape.Areas > 0 && Gone(zdo, shape) is { } gone)
        {
            // FNV-1a over the bits gone.
            uint hash = 2166136261;
            foreach (bool bit in gone)
            {
                hash = (hash ^ (bit ? 1u : 0u)) * 16777619;
            }

            state = unchecked((state * 31) + (int)hash);
        }

        return state;
    }

    // The matrix's first three rows, as SolidMeshInstance takes them.
    private static float[] Rows(Matrix4x4 m) => [m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23];

    private static Vec3 ToVec(Vector3 v) => new(v.x, v.y, v.z);

    // The object's shape: its prefab's, or for a place's proxy the place's location prefab's.
    private Shape Of(ZDO zdo)
    {
        int prefab = zdo.GetPrefab();
        return prefab == LocationProxyHash ? OfLocation(zdo.GetInt(ZDOVars.s_location)) : OfPrefab(prefab);
    }

    // A prefab's shape, read once. With no scene to read it from (the world closing, or not open yet), no shape, and
    // nothing kept: it is read when there is one.
    private Shape OfPrefab(int prefab)
    {
        if (_byPrefab.TryGetValue(prefab, out Shape shape))
        {
            return shape;
        }

        if (ZNetScene.instance == null)
        {
            return None;
        }

        GameObject source = ZNetScene.instance.GetPrefab(prefab);
        shape = source == null ? None : Read(source, prefab, false);
        _byPrefab[prefab] = shape;
        return shape;
    }

    // A location's shape (its prefab's static objects: those with a net view of their own are objects of their own),
    // read once; its prefab is loaded for it and kept.
    private Shape OfLocation(int location)
    {
        if (_byLocation.TryGetValue(location, out Shape shape))
        {
            return shape;
        }

        if (ZoneSystem.instance == null)
        {
            return None;
        }

        if (_locations == null || _locations.Count == 0)
        {
            _locations = [];
            foreach (ZoneSystem.ZoneLocation known in ZoneSystem.instance.m_locations)
            {
                if (known?.m_prefabName != null)
                {
                    _locations[known.m_prefabName.GetStableHashCode()] = known;
                }
            }
        }

        shape = None;
        if (_locations.TryGetValue(location, out ZoneSystem.ZoneLocation found) && found.m_prefab.IsValid)
        {
            if (!found.m_prefab.IsLoaded)
            {
                found.m_prefab.Load();
            }

            GameObject source = found.m_prefab.Asset;
            shape = source == null ? None : Read(source, location, true);
        }

        _byLocation[location] = shape;
        return shape;
    }

    // The prefab's solid colliders as parts in its own space.
    private Shape Read(GameObject prefab, int key, bool location)
    {
        // What moves by itself (a log rolling, a thing dropped) is no ground for the water.
        Rigidbody body = prefab.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            return None;
        }

        var parts = new List<Part>();
        Matrix4x4 toPrefab = prefab.transform.worldToLocalMatrix;
        int areas = 0;
        bool meshes = false;
        if (prefab.GetComponent<MineRock5>() != null)
        {
            // A rock mined bit by bit: each bit a child with its collider, in the order the game keeps their health.
            foreach (Transform child in prefab.transform)
            {
                if (child.GetComponent<Collider>() == null)
                {
                    continue;
                }

                int area = areas++;
                foreach (Collider collider in child.GetComponentsInChildren<Collider>(true))
                {
                    meshes |= Add(parts, collider, toPrefab, area, key, prefab.name);
                }
            }
        }
        else
        {
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                // A place's object with a net view of its own (a breakable wall, a rock) is a saved object of its own,
                // read as that: here only what stands still with the place.
                if (location && collider.GetComponentInParent<ZNetView>(true) is { } view && view.gameObject != prefab)
                {
                    continue;
                }

                meshes |= Add(parts, collider, toPrefab, -1, key, prefab.name);
            }
        }

        if (parts.Count > 0)
        {
            Kinds++;
            MeshKinds += meshes ? 1 : 0;
        }

        return new Shape([.. parts], prefab.GetComponent<Door>() != null, areas);
    }

    // One collider as a part, when it is solid: true when it is a mesh.
    private bool Add(List<Part> parts, Collider collider, Matrix4x4 toPrefab, int area, int key, string name)
    {
        if (collider.isTrigger || (_mask & (1 << collider.gameObject.layer)) == 0)
        {
            return false;
        }

        Matrix4x4 local = toPrefab * collider.transform.localToWorldMatrix;
        if (Mathf.Abs(local.determinant) < 1e-12f)
        {
            // Flattened (a collider scaled to nothing on an axis): no inside.
            return false;
        }

        if (collider is MeshCollider mesh && mesh.sharedMesh != null)
        {
            if (mesh.sharedMesh.isReadable && mesh.sharedMesh.vertexCount >= 3 && mesh.sharedMesh.triangles.Length >= 3)
            {
                parts.Add(new Part(local, MeshOf(mesh.sharedMesh), area));
                return true;
            }

            if (mesh.sharedMesh.isReadable)
            {
                // Readable but with nothing in it: nothing solid.
                return false;
            }

            if (_unreadableTold.Add(key))
            {
                FlowingWaterPlugin.Log.LogWarning($"Water: the collider mesh {mesh.sharedMesh.name} of {name} cannot be read: it is taken as the box round it");
            }

            parts.Add(new Part(local, mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size * 0.5f, area));
            return false;
        }

        (Vector3 centre, Vector3 size) = collider switch
        {
            BoxCollider box => (box.center, box.size),
            SphereCollider sphere => (sphere.center, Vector3.one * (sphere.radius * 2f)),
            CapsuleCollider capsule => (capsule.center, Capsule(capsule)),
            _ => (Vector3.zero, Vector3.zero),
        };
        if (size != Vector3.zero)
        {
            parts.Add(new Part(local, centre, size * 0.5f, area));
        }

        return false;
    }

    private SolidMesh MeshOf(Mesh mesh)
    {
        if (_meshes.TryGetValue(mesh, out SolidMesh known))
        {
            return known;
        }

        Vector3[] vertices = mesh.vertices;
        float[] xyz = new float[vertices.Length * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            xyz[i * 3] = vertices[i].x;
            xyz[(i * 3) + 1] = vertices[i].y;
            xyz[(i * 3) + 2] = vertices[i].z;
        }

        var solid = new SolidMesh(xyz, mesh.triangles);
        _meshes[mesh] = solid;
        return solid;
    }

    // Which bits of a mined rock are gone, by the health the game keeps for each (a package of their count and
    // health each); none known when there is no health yet or it does not match the bits (told of once).
    private bool[] Gone(ZDO zdo, Shape shape)
    {
        byte[] data = zdo.GetByteArray(ZDOVars.s_health);
        if (data == null)
        {
            return null;
        }

        if (data.Length < sizeof(int) + (shape.Areas * sizeof(float)))
        {
            Mismatch(zdo, shape, data.Length < sizeof(int) ? -1 : BitConverter.ToInt32(data, 0));
            return null;
        }

        var package = new ZPackage(data);
        int count = package.ReadInt();
        if (count != shape.Areas)
        {
            Mismatch(zdo, shape, count);
            return null;
        }

        bool[] gone = new bool[count];
        for (int i = 0; i < count; i++)
        {
            gone[i] = package.ReadSingle() <= 0f;
        }

        return gone;
    }

    // A rock whose health does not match its bits, told of once a kind (count: the bits its health is kept for, -1: none).
    private void Mismatch(ZDO zdo, Shape shape, int count)
    {
        if (_healthTold.Add(zdo.GetPrefab()))
        {
            FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water: a rock of kind {ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name ?? "?"} ({zdo.GetPrefab()}) keeps the health of {count} bits but has {shape.Areas}: all of it is taken as solid"));
        }
    }

    private static Vector3 Capsule(CapsuleCollider capsule)
    {
        var size = Vector3.one * (capsule.radius * 2f);
        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
        return size;
    }

    // One collider of a prefab, in the prefab's space: a mesh placed by its matrix, or a box (its centre and half
    // sizes in the collider's own space, placed by the matrix). Area: which bit of a mined rock it is (-1: none).
    private sealed class Part
    {
        public Part(Matrix4x4 local, SolidMesh mesh, int area)
        {
            Local = local;
            Mesh = mesh;
            Area = area;
        }

        public Part(Matrix4x4 local, Vector3 centre, Vector3 half, int area)
        {
            Local = local;
            Centre = centre;
            Half = half;
            Area = area;
        }

        public Matrix4x4 Local { get; }

        public SolidMesh Mesh { get; }

        public Vector3 Centre { get; }

        public Vector3 Half { get; }

        public int Area { get; }

        // How far it reaches from the prefab's origin at most: the farthest corner of its box, or of its mesh's.
        public float Reach()
        {
            float reach = 0f;
            Vector3 min = Mesh != null ? new Vector3(Mesh.Min.X, Mesh.Min.Y, Mesh.Min.Z) : Centre - Half;
            Vector3 max = Mesh != null ? new Vector3(Mesh.Max.X, Mesh.Max.Y, Mesh.Max.Z) : Centre + Half;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
                reach = Mathf.Max(reach, Local.MultiplyPoint3x4(point).magnitude);
            }

            return reach;
        }
    }

    private sealed class Shape
    {
        public Shape(Part[] parts, bool door, int areas)
        {
            Parts = parts;
            Door = door;
            Areas = areas;
            foreach (Part part in parts)
            {
                Reach = Mathf.Max(Reach, part.Reach());
            }
        }

        public Part[] Parts { get; }

        public bool Door { get; }

        // How many bits a mined rock has (0: not such a rock).
        public int Areas { get; }

        // How far its parts reach from the prefab's origin at most, unscaled.
        public float Reach { get; }
    }
}
