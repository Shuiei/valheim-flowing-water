using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// The solid shapes of building pieces, as boxes: read once per prefab from its colliders (those players bump into,
/// on the game's piece layer), then placed where each piece stands. A box collider is its own box; any other
/// collider is taken as the box round it. An open door holds nothing back.
/// </summary>
internal sealed class PieceShapes
{
    private static readonly int StateHash = "state".GetStableHashCode();

    private static readonly int ScaleHash = "scale".GetStableHashCode();

    private readonly Dictionary<int, Shape> _byPrefab = [];

    private readonly int _pieceLayer = LayerMask.NameToLayer("piece");

    /// <summary>Adds the solid boxes of the piece this ZDO is (none if it is no solid piece) to the list.</summary>
    public void AddBoxes(ZDO zdo, List<SolidBox> boxes)
    {
        Shape shape = Of(zdo.GetPrefab());
        if (shape.Boxes.Length == 0 || (shape.Door && zdo.GetInt(StateHash) != 0))
        {
            return;
        }

        Vector3 position = zdo.GetPosition();
        Quaternion rotation = zdo.GetRotation();
        Vector3 scale = zdo.GetVec3(ScaleHash, Vector3.one);
        foreach (Box box in shape.Boxes)
        {
            Vector3 x = rotation * Vector3.Scale(box.AxisX, scale);
            Vector3 y = rotation * Vector3.Scale(box.AxisY, scale);
            Vector3 z = rotation * Vector3.Scale(box.AxisZ, scale);
            boxes.Add(new SolidBox(
                ToVec(position + (rotation * Vector3.Scale(box.Centre, scale))),
                ToVec(x.normalized),
                ToVec(y.normalized),
                ToVec(z.normalized),
                new Vec3(box.Half.x * x.magnitude, box.Half.y * y.magnitude, box.Half.z * z.magnitude)));
        }
    }

    /// <summary>How many solid boxes a prefab has (0: water ignores it).</summary>
    public int Count(int prefab) => Of(prefab).Boxes.Length;

    private Shape Of(int prefab)
    {
        if (_byPrefab.TryGetValue(prefab, out Shape shape))
        {
            return shape;
        }

        GameObject source = ZNetScene.instance?.GetPrefab(prefab);
        shape = source == null || source.GetComponent<Piece>() == null
            ? new Shape([], false)
            : new Shape(Read(source), source.GetComponent<Door>() != null);
        _byPrefab[prefab] = shape;
        return shape;
    }

    // The prefab's solid colliders as boxes in its own space: axes carry the colliders' scale, half sizes are of
    // the unscaled collider.
    private Box[] Read(GameObject prefab)
    {
        var boxes = new List<Box>();
        Matrix4x4 toPrefab = prefab.transform.worldToLocalMatrix;
        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger || collider.gameObject.layer != _pieceLayer)
            {
                continue;
            }

            Matrix4x4 local = toPrefab * collider.transform.localToWorldMatrix;
            (Vector3 centre, Vector3 size) = collider switch
            {
                BoxCollider box => (box.center, box.size),
                SphereCollider sphere => (sphere.center, Vector3.one * (sphere.radius * 2f)),
                CapsuleCollider capsule => (capsule.center, Capsule(capsule)),
                MeshCollider mesh when mesh.sharedMesh != null => (mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size),
                _ => (Vector3.zero, Vector3.zero),
            };
            if (size == Vector3.zero)
            {
                continue;
            }

            boxes.Add(new Box(
                local.MultiplyPoint3x4(centre),
                local.MultiplyVector(Vector3.right),
                local.MultiplyVector(Vector3.up),
                local.MultiplyVector(Vector3.forward),
                size * 0.5f));
        }

        return [.. boxes];
    }

    private static Vector3 Capsule(CapsuleCollider capsule)
    {
        var size = Vector3.one * (capsule.radius * 2f);
        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
        return size;
    }

    private static Vec3 ToVec(Vector3 v) => new(v.x, v.y, v.z);

    // A box in the prefab's space: axes scaled as the collider is (their lengths), half sizes unscaled.
    private readonly struct Box
    {
        public Box(Vector3 centre, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 half)
        {
            Centre = centre;
            AxisX = axisX;
            AxisY = axisY;
            AxisZ = axisZ;
            Half = half;
        }

        public Vector3 Centre { get; }

        public Vector3 AxisX { get; }

        public Vector3 AxisY { get; }

        public Vector3 AxisZ { get; }

        public Vector3 Half { get; }
    }

    private sealed class Shape
    {
        public Shape(Box[] boxes, bool door)
        {
            Boxes = boxes;
            Door = door;
        }

        public Box[] Boxes { get; }

        public bool Door { get; }
    }
}
