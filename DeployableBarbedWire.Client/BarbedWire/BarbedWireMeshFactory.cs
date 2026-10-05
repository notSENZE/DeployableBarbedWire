using System.Collections.Generic;
using UnityEngine;

namespace DeployableBarbedWire.Client.BarbedWire;

internal static class BarbedWireMeshFactory
{
    internal const float Length = 2.21f;
    internal const float Height = 0.46f;
    internal const float Depth = 0.46f;

    internal static Mesh Create()
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        var clockwiseCoil = CreateHelix(false, 0f);
        var counterClockwiseCoil = CreateHelix(true, Mathf.PI * 0.35f);

        AddTube(clockwiseCoil, 0.008f, 5, vertices, normals, triangles);
        AddTube(counterClockwiseCoil, 0.006f, 5, vertices, normals, triangles);
        AddBlades(clockwiseCoil, 5, vertices, normals, triangles);
        AddBlades(counterClockwiseCoil, 7, vertices, normals, triangles);

        AddLongitudinalWire(0f, vertices, normals, triangles);
        AddLongitudinalWire(Mathf.PI * 2f / 3f, vertices, normals, triangles);
        AddLongitudinalWire(Mathf.PI * 4f / 3f, vertices, normals, triangles);

        var mesh = new Mesh { name = "Deployable Barbed Wire Fallback Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        var colors = new List<Color>(vertices.Count);
        for (var index = 0; index < vertices.Count; index++)
        {
            colors.Add(Color.white);
        }

        mesh.SetColors(colors);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static List<Vector3> CreateHelix(bool reverse, float phase)
    {
        const int segments = 192;
        const float turns = 8f;
        const float radius = Height * 0.5f;

        var points = new List<Vector3>(segments + 1);
        for (var index = 0; index <= segments; index++)
        {
            var progress = index / (float)segments;
            var direction = reverse ? -1f : 1f;
            var angle = progress * turns * Mathf.PI * 2f * direction + phase;
            points.Add(new Vector3(
                Mathf.Lerp(-Length * 0.5f, Length * 0.5f, progress),
                radius + Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius));
        }

        return points;
    }

    private static void AddLongitudinalWire(
        float angle,
        ICollection<Vector3> vertices,
        ICollection<Vector3> normals,
        ICollection<int> triangles)
    {
        var radius = Height * 0.5f;
        var y = radius + Mathf.Cos(angle) * radius;
        var z = Mathf.Sin(angle) * radius;
        AddTube(
            new List<Vector3>
            {
                new(-Length * 0.5f, y, z),
                new(Length * 0.5f, y, z)
            },
            0.005f,
            5,
            vertices,
            normals,
            triangles);
    }

    private static void AddBlades(
        IReadOnlyList<Vector3> coil,
        int spacing,
        ICollection<Vector3> vertices,
        ICollection<Vector3> normals,
        ICollection<int> triangles)
    {
        const float radius = Height * 0.5f;
        for (var index = spacing; index < coil.Count - spacing; index += spacing)
        {
            var point = coil[index];
            var tangent = (coil[index + 1] - coil[index - 1]).normalized;
            var radial = new Vector3(0f, point.y - radius, point.z).normalized;
            AddBlade(point, tangent, radial, vertices, normals, triangles);
            AddBlade(point, tangent, -radial, vertices, normals, triangles);
        }
    }

    private static void AddBlade(
        Vector3 center,
        Vector3 tangent,
        Vector3 outward,
        ICollection<Vector3> vertices,
        ICollection<Vector3> normals,
        ICollection<int> triangles)
    {
        var side = Vector3.Cross(tangent, outward).normalized;
        var inner = center - tangent * 0.045f;
        var left = center + outward * 0.018f + side * 0.018f;
        var tip = center + tangent * 0.055f + outward * 0.07f;
        var right = center + outward * 0.018f - side * 0.018f;
        var normal = Vector3.Cross(left - inner, tip - inner).normalized;

        AddBladeFace(inner, left, tip, right, normal, false, vertices, normals, triangles);
        AddBladeFace(inner, left, tip, right, -normal, true, vertices, normals, triangles);
    }

    private static void AddBladeFace(
        Vector3 inner,
        Vector3 left,
        Vector3 tip,
        Vector3 right,
        Vector3 normal,
        bool reverse,
        ICollection<Vector3> vertices,
        ICollection<Vector3> normals,
        ICollection<int> triangles)
    {
        var start = vertices.Count;
        vertices.Add(inner);
        vertices.Add(left);
        vertices.Add(tip);
        vertices.Add(right);

        for (var index = 0; index < 4; index++)
        {
            normals.Add(normal);
        }

        if (reverse)
        {
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
            return;
        }

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static void AddTube(
        IReadOnlyList<Vector3> points,
        float radius,
        int sides,
        ICollection<Vector3> vertices,
        ICollection<Vector3> normals,
        ICollection<int> triangles)
    {
        var vertexStart = vertices.Count;
        for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var previous = points[pointIndex == 0 ? pointIndex : pointIndex - 1];
            var next = points[pointIndex == points.Count - 1 ? pointIndex : pointIndex + 1];
            var tangent = (next - previous).normalized;
            var normal = Vector3.Cross(tangent, Vector3.up);
            if (normal.sqrMagnitude < 0.001f)
            {
                normal = Vector3.Cross(tangent, Vector3.forward);
            }

            normal.Normalize();
            var binormal = Vector3.Cross(tangent, normal).normalized;
            for (var side = 0; side < sides; side++)
            {
                var angle = side / (float)sides * Mathf.PI * 2f;
                var direction = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                vertices.Add(points[pointIndex] + direction * radius);
                normals.Add(direction);
            }
        }

        for (var pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
        {
            for (var side = 0; side < sides; side++)
            {
                var nextSide = (side + 1) % sides;
                var current = vertexStart + pointIndex * sides + side;
                var currentNext = vertexStart + pointIndex * sides + nextSide;
                var following = current + sides;
                var followingNext = currentNext + sides;

                triangles.Add(current);
                triangles.Add(following);
                triangles.Add(currentNext);
                triangles.Add(currentNext);
                triangles.Add(following);
                triangles.Add(followingNext);
            }
        }
    }
}
