using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeployableBarbedWire.Client.BarbedWire;

internal sealed class BarbedWireVisualAssets : IDisposable
{
    private const string ResourceName = "DeployableBarbedWire.Client.Assets.spte_barbedwire.dat";
    private const string Magic = "SPTBW001";

    private static readonly byte[] EncryptionKey =
        Convert.FromBase64String("u3ltZTlzOVCFoGvA8ZVx2pQx2sa2rC0pV7xN8Kq4vKs=");

    private readonly Texture2D[] _textures;

    internal Mesh Mesh { get; }
    internal Material Material { get; }

    private BarbedWireVisualAssets(Mesh mesh, Material material, params Texture2D[] textures)
    {
        Mesh = mesh;
        Material = material;
        _textures = textures;
    }

    internal static BarbedWireVisualAssets Load()
    {
        using var archiveStream = DecryptResource();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, false);

        var mesh = ParseObj(ReadText(archive, "wire.obj"));
        var albedo = LoadTexture(archive, "albedo.png", "Deployable Barbed Wire Albedo", false);
        var metallic = LoadTexture(archive, "metallic.png", "Deployable Barbed Wire Metallic", true);
        var normal = LoadTexture(archive, "normal.png", "Deployable Barbed Wire Normal", true);
        var material = CreateMaterial(albedo, metallic, normal);

        return new BarbedWireVisualAssets(mesh, material, albedo, metallic, normal);
    }

    public void Dispose()
    {
        if (Material != null)
        {
            UnityEngine.Object.Destroy(Material);
        }

        if (Mesh != null)
        {
            UnityEngine.Object.Destroy(Mesh);
        }

        foreach (var texture in _textures)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }

    private static MemoryStream DecryptResource()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (resource == null)
        {
            throw new InvalidOperationException("The embedded barbed wire visual asset is missing.");
        }

        var magic = new byte[Magic.Length];
        ReadExactly(resource, magic);
        if (Encoding.ASCII.GetString(magic) != Magic)
        {
            throw new InvalidDataException("The embedded barbed wire visual asset has an invalid header.");
        }

        var initializationVector = new byte[16];
        ReadExactly(resource, initializationVector);

        using var aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.IV = initializationVector;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var decrypted = new MemoryStream();
        using (var crypto = new CryptoStream(resource, aes.CreateDecryptor(), CryptoStreamMode.Read))
        {
            crypto.CopyTo(decrypted);
        }

        decrypted.Position = 0;
        return decrypted;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("The embedded barbed wire visual asset is incomplete.");
            }

            offset += read;
        }
    }

    private static string ReadText(ZipArchive archive, string name)
    {
        using var stream = OpenEntry(archive, name);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(ZipArchive archive, string name)
    {
        using var stream = OpenEntry(archive, name);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static Stream OpenEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry == null)
        {
            throw new InvalidDataException($"The embedded barbed wire asset does not contain '{name}'.");
        }

        return entry.Open();
    }

    private static Texture2D LoadTexture(ZipArchive archive, string name, string textureName, bool linear)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };

        if (!texture.LoadImage(ReadBytes(archive, name), true))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException($"The embedded texture '{name}' could not be decoded.");
        }

        return texture;
    }

    private static Material CreateMaterial(Texture albedo, Texture metallic, Texture normal)
    {
        var shader = Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException("The Standard Unity shader required by the barbed wire asset is unavailable.");
        }

        var material = new Material(shader)
        {
            name = "Deployable Barbed Wire Material",
            color = Color.white,
            renderQueue = 2450
        };

        material.SetTexture("_MainTex", albedo);
        material.SetTexture("_MetallicGlossMap", metallic);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_Mode", 1f);
        material.SetFloat("_Cutoff", 0.32f);
        material.SetFloat("_Metallic", 1f);
        material.SetFloat("_GlossMapScale", 0.85f);
        material.SetFloat("_BumpScale", 1f);
        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.EnableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_METALLICGLOSSMAP");
        material.EnableKeyword("_NORMALMAP");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        return material;
    }

    private static Mesh ParseObj(string source)
    {
        var sourcePositions = new List<Vector3>();
        var sourceNormals = new List<Vector3>();
        var sourceUvs = new List<Vector2>();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var vertexIndices = new Dictionary<ObjVertex, int>();

        using var reader = new StringReader(source);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var values = SplitValues(line);
                sourcePositions.Add(new Vector3(
                    ParseFloat(values[1]) * 0.01f,
                    ParseFloat(values[3]) * 0.01f,
                    ParseFloat(values[2]) * 0.01f));
                continue;
            }

            if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                var values = SplitValues(line);
                sourceNormals.Add(new Vector3(
                    ParseFloat(values[1]),
                    ParseFloat(values[3]),
                    ParseFloat(values[2])).normalized);
                continue;
            }

            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                var values = SplitValues(line);
                sourceUvs.Add(new Vector2(ParseFloat(values[1]), ParseFloat(values[2])));
                continue;
            }

            if (!line.StartsWith("f ", StringComparison.Ordinal))
            {
                continue;
            }

            var faceValues = SplitValues(line).Skip(1).Select(ParseVertex).ToArray();
            for (var index = 1; index < faceValues.Length - 1; index++)
            {
                triangles.Add(GetVertexIndex(faceValues[0], sourcePositions, sourceNormals, sourceUvs, vertices, normals, uvs, vertexIndices));
                triangles.Add(GetVertexIndex(faceValues[index + 1], sourcePositions, sourceNormals, sourceUvs, vertices, normals, uvs, vertexIndices));
                triangles.Add(GetVertexIndex(faceValues[index], sourcePositions, sourceNormals, sourceUvs, vertices, normals, uvs, vertexIndices));
            }
        }

        if (vertices.Count == 0 || triangles.Count == 0)
        {
            throw new InvalidDataException("The embedded barbed wire mesh contains no geometry.");
        }

        var frontVertexCount = vertices.Count;
        var frontTriangleCount = triangles.Count;
        for (var index = 0; index < frontVertexCount; index++)
        {
            vertices.Add(vertices[index]);
            normals.Add(-normals[index]);
            uvs.Add(uvs[index]);
        }

        for (var index = 0; index < frontTriangleCount; index += 3)
        {
            triangles.Add(triangles[index] + frontVertexCount);
            triangles.Add(triangles[index + 2] + frontVertexCount);
            triangles.Add(triangles[index + 1] + frontVertexCount);
        }

        var mesh = new Mesh
        {
            name = "Deployable Barbed Wire Mesh",
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static string[] SplitValues(string line)
    {
        return line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static ObjVertex ParseVertex(string value)
    {
        var indices = value.Split('/');
        if (indices.Length < 3)
        {
            throw new InvalidDataException($"Unsupported barbed wire face value '{value}'.");
        }

        return new ObjVertex(
            int.Parse(indices[0], CultureInfo.InvariantCulture) - 1,
            int.Parse(indices[1], CultureInfo.InvariantCulture) - 1,
            int.Parse(indices[2], CultureInfo.InvariantCulture) - 1);
    }

    private static int GetVertexIndex(
        ObjVertex sourceVertex,
        IReadOnlyList<Vector3> sourcePositions,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUvs,
        ICollection<Vector3> vertices,
        ICollection<Vector3> normals,
        ICollection<Vector2> uvs,
        IDictionary<ObjVertex, int> vertexIndices)
    {
        if (vertexIndices.TryGetValue(sourceVertex, out var existingIndex))
        {
            return existingIndex;
        }

        var index = vertices.Count;
        vertices.Add(sourcePositions[sourceVertex.Position]);
        normals.Add(sourceNormals[sourceVertex.Normal]);
        uvs.Add(sourceUvs[sourceVertex.Uv]);
        vertexIndices.Add(sourceVertex, index);
        return index;
    }

    private readonly struct ObjVertex : IEquatable<ObjVertex>
    {
        internal readonly int Position;
        internal readonly int Uv;
        internal readonly int Normal;

        internal ObjVertex(int position, int uv, int normal)
        {
            Position = position;
            Uv = uv;
            Normal = normal;
        }

        public bool Equals(ObjVertex other)
        {
            return Position == other.Position && Uv == other.Uv && Normal == other.Normal;
        }

        public override bool Equals(object obj)
        {
            return obj is ObjVertex other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Position;
                hash = hash * 397 ^ Uv;
                hash = hash * 397 ^ Normal;
                return hash;
            }
        }
    }
}
