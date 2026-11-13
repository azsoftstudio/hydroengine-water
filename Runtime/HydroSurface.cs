using System.Collections.Generic;
using UnityEngine;

namespace HydroEngine
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HydroSurface : MonoBehaviour
    {
        [Header("Surface")]
        [SerializeField, Min(1f), Tooltip("Side length in local units. Transform scale changes the world-space footprint.")] private float size = 100f;
        [SerializeField, Tooltip("Allow the generated mesh density to use the maximum auto-resolution cap.")] private bool autoResolution = true;
        [SerializeField, Range(8, 512), Tooltip("Manual grid density, or minimum grid density when Auto Resolution is enabled.")] private int resolution = 64;
        [SerializeField, Range(64, 256), Tooltip("Hard upper limit for automatic mesh density.")] private int maximumAutoResolution = 192;
        [SerializeField, Tooltip("Render a camera-centered multi-resolution ocean mesh instead of the bounded single grid.")] private bool cameraCenteredLod;
        [SerializeField, Range(2, 6), Tooltip("Number of nested mesh levels, including the dense center patch.")] private int lodRingCount = 4;
        [SerializeField, Min(16f), Tooltip("World-space width of the finest center patch. Outer rings double in width at each level.")] private float lodPatchSize = 128f;
        [SerializeField, Range(32, 128), Tooltip("Grid resolution for the finest patch. Each outer ring uses twice this count across its width.")] private int lodResolution = 64;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private bool followTransform = true;

        private Mesh generatedMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private int generatedResolution;
        private int generatedVertexCount;
        public float SurfaceSize => size;
        public float WorldSizeX => cameraCenteredLod ? WorldLodCoverage : size * Mathf.Abs(transform.lossyScale.x);
        public float WorldSizeZ => cameraCenteredLod ? WorldLodCoverage : size * Mathf.Abs(transform.lossyScale.z);
        public int CurrentResolution => generatedResolution;
        public int CurrentVertexCount => generatedMesh != null ? generatedMesh.vertexCount : generatedVertexCount;
        public bool CameraCenteredLod => cameraCenteredLod;
        public int LodRingCount => lodRingCount;
        public float LodPatchSize => lodPatchSize;
        public float WorldLodCoverage => cameraCenteredLod ? lodPatchSize * Mathf.Pow(2f, lodRingCount - 1) : 0f;

        private void OnEnable()
        {
            meshFilter = GetComponent<MeshFilter>(); meshRenderer = GetComponent<MeshRenderer>();
            RebuildMesh(); ApplyMaterial();
        }
        private void OnDisable()
        {
            if (generatedMesh == null) return;
            if (Application.isPlaying) Destroy(generatedMesh); else DestroyImmediate(generatedMesh);
            generatedMesh = null;
        }
        private void OnValidate()
        {
            size = Mathf.Max(1f, size); resolution = Mathf.Clamp(resolution, 8, 512);
            maximumAutoResolution = Mathf.Clamp(maximumAutoResolution, 64, 256);
            lodRingCount = Mathf.Clamp(lodRingCount, 2, 6);
            lodPatchSize = Mathf.Max(16f, lodPatchSize);
            lodResolution = Mathf.Clamp(lodResolution, 32, 128);
            if (isActiveAndEnabled && meshFilter != null) RebuildMesh();
            if (isActiveAndEnabled && meshRenderer != null) ApplyMaterial();
        }
        private void Update()
        {
            if ((followTransform || cameraCenteredLod) && Camera.main != null)
            {
                Vector3 p = transform.position, c = Camera.main.transform.position;
                float step = cameraCenteredLod ? lodPatchSize / Mathf.Max(32, generatedResolution) : 0f;
                float x = cameraCenteredLod ? Mathf.Round(c.x / step) * step : c.x;
                float z = cameraCenteredLod ? Mathf.Round(c.z / step) * step : c.z;
                transform.position = new Vector3(x, p.y, z);
            }
            if (autoResolution && generatedResolution != CalculateTargetResolution()) RebuildMesh();
        }
        private void ApplyMaterial() { if (meshRenderer != null && waterMaterial != null) meshRenderer.sharedMaterial = waterMaterial; }
        private void RebuildMesh()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (generatedMesh != null) { if (Application.isPlaying) Destroy(generatedMesh); else DestroyImmediate(generatedMesh); }
            generatedResolution = CalculateTargetResolution();
            if (cameraCenteredLod)
                BuildCameraLodMesh();
            else
                BuildBoundedMesh();
            meshFilter.sharedMesh = generatedMesh;
        }

        private void BuildBoundedMesh()
        {
            int side = generatedResolution + 1;
            Vector3[] vertices = new Vector3[side * side];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[generatedResolution * generatedResolution * 6];
            for (int z = 0; z <= generatedResolution; z++)
            for (int x = 0; x <= generatedResolution; x++)
            {
                int i = z * side + x;
                float u = x / (float)generatedResolution, v = z / (float)generatedResolution;
                vertices[i] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
                uv[i] = new Vector2(u, v);
            }
            int t = 0;
            for (int z = 0; z < generatedResolution; z++)
            for (int x = 0; x < generatedResolution; x++)
            {
                int a = z * side + x, b = a + side;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }

            generatedMesh = new Mesh { name = "HydroEngine Bounded Water Grid" };
            generatedMesh.indexFormat = vertices.Length > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            generatedMesh.vertices = vertices;
            generatedMesh.uv = uv;
            generatedMesh.triangles = triangles;
            generatedMesh.RecalculateBounds();
            generatedVertexCount = vertices.Length;
        }

        private void BuildCameraLodMesh()
        {
            // The center patch has N cells across. Each square ring doubles its span and
            // uses 2N cells across, so its inner boundary matches the previous level.
            var vertices = new List<Vector3>();
            var levels = new List<Vector2>();
            var triangles = new List<int>();
            float scaleX = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x));
            float scaleZ = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.z));
            float halfX = lodPatchSize * 0.5f / scaleX;
            float halfZ = lodPatchSize * 0.5f / scaleZ;

            float baseStepX = 2f * halfX / generatedResolution;
            float baseStepZ = 2f * halfZ / generatedResolution;
            AppendGrid(vertices, levels, triangles, generatedResolution, baseStepX, baseStepZ,
                1, halfX, halfZ, false);
            for (int level = 1; level < lodRingCount; level++)
            {
                AppendGrid(vertices, levels, triangles, generatedResolution * 2,
                    baseStepX, baseStepZ, 1 << (level - 1), halfX, halfZ, true);
            }

            generatedMesh = new Mesh { name = "HydroEngine Camera LOD Water" };
            generatedMesh.indexFormat = vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            generatedMesh.SetVertices(vertices);
            generatedMesh.SetUVs(1, levels);
            generatedMesh.SetTriangles(triangles, 0, true);
            generatedMesh.RecalculateBounds();
            generatedVertexCount = vertices.Count;
        }

        private static void AppendGrid(List<Vector3> vertices, List<Vector2> levels, List<int> triangles,
            int divisions, float baseStepX, float baseStepZ, int latticeScale,
            float baseHalfX, float baseHalfZ, bool removeCenter)
        {
            int vertexStart = vertices.Count;
            int side = divisions + 1;
            int holeStart = divisions / 4;
            int holeEnd = divisions - holeStart;

            for (int z = 0; z <= divisions; z++)
            for (int x = 0; x <= divisions; x++)
            {
                int latticeX = (x - divisions / 2) * latticeScale;
                int latticeZ = (z - divisions / 2) * latticeScale;
                Vector3 vertex = new Vector3(latticeX * baseStepX, 0f, latticeZ * baseStepZ);
                vertices.Add(vertex);
                float radius = Mathf.Max(Mathf.Abs(vertex.x) / baseHalfX, Mathf.Abs(vertex.z) / baseHalfZ);
                levels.Add(new Vector2(Mathf.Log(Mathf.Max(1f, radius), 2f), 0f));
            }

            for (int z = 0; z < divisions; z++)
            for (int x = 0; x < divisions; x++)
            {
                if (removeCenter && x >= holeStart && x < holeEnd && z >= holeStart && z < holeEnd) continue;
                int a = vertexStart + z * side + x, b = a + side;
                int c = a + 1, d = b + 1;

                // Every outer ring is twice as wide as the previous one. From level 2
                // onward its inner edge therefore has half the neighboring edge vertices.
                // Split that coarse edge at its midpoint and stitch the strip to remove T-junctions.
                if (removeCenter && latticeScale > 1 && x >= holeStart && x < holeEnd)
                {
                    if (z == holeStart - 1) // Hole is across the cell's far (+Z) edge.
                    {
                        int midpoint = AddLodMidpoint(vertices, levels, b, d, baseHalfX, baseHalfZ);
                        triangles.Add(a); triangles.Add(b); triangles.Add(midpoint);
                        triangles.Add(a); triangles.Add(midpoint); triangles.Add(c);
                        triangles.Add(midpoint); triangles.Add(d); triangles.Add(c);
                        continue;
                    }
                    if (z == holeEnd) // Hole is across the cell's near (-Z) edge.
                    {
                        int midpoint = AddLodMidpoint(vertices, levels, a, c, baseHalfX, baseHalfZ);
                        triangles.Add(a); triangles.Add(b); triangles.Add(midpoint);
                        triangles.Add(midpoint); triangles.Add(b); triangles.Add(d);
                        triangles.Add(midpoint); triangles.Add(d); triangles.Add(c);
                        continue;
                    }
                }
                if (removeCenter && latticeScale > 1 && z >= holeStart && z < holeEnd)
                {
                    if (x == holeStart - 1) // Hole is across the cell's far (+X) edge.
                    {
                        int midpoint = AddLodMidpoint(vertices, levels, c, d, baseHalfX, baseHalfZ);
                        triangles.Add(a); triangles.Add(b); triangles.Add(d);
                        triangles.Add(a); triangles.Add(d); triangles.Add(midpoint);
                        triangles.Add(a); triangles.Add(midpoint); triangles.Add(c);
                        continue;
                    }
                    if (x == holeEnd) // Hole is across the cell's near (-X) edge.
                    {
                        int midpoint = AddLodMidpoint(vertices, levels, a, b, baseHalfX, baseHalfZ);
                        triangles.Add(c); triangles.Add(a); triangles.Add(midpoint);
                        triangles.Add(c); triangles.Add(midpoint); triangles.Add(b);
                        triangles.Add(c); triangles.Add(b); triangles.Add(d);
                        continue;
                    }
                }

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(c); triangles.Add(b); triangles.Add(d);
            }
        }

        private static int AddLodMidpoint(List<Vector3> vertices, List<Vector2> levels, int a, int b,
            float baseHalfX, float baseHalfZ)
        {
            Vector3 midpoint = (vertices[a] + vertices[b]) * 0.5f;
            vertices.Add(midpoint);
            float radius = Mathf.Max(Mathf.Abs(midpoint.x) / baseHalfX, Mathf.Abs(midpoint.z) / baseHalfZ);
            levels.Add(new Vector2(Mathf.Log(Mathf.Max(1f, radius), 2f), 0f));
            return vertices.Count - 1;
        }

        /// <summary>Samples the still water plane for gameplay systems.</summary>
        public bool SampleHeight(Vector3 worldPosition, out float height, out Vector3 normal)
        {
            height = transform.position.y;
            normal = Vector3.up;
            HydroSpectralOceanRenderer spectral = GetComponent<HydroSpectralOceanRenderer>();
            if (spectral != null && spectral.isActiveAndEnabled && spectral.IsReady) return false;
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float halfSize = size * 0.5f;
            if (!cameraCenteredLod && (Mathf.Abs(local.x) > halfSize || Mathf.Abs(local.z) > halfSize)) return false;
            if (cameraCenteredLod && (Mathf.Abs(worldPosition.x - transform.position.x) > WorldLodCoverage * 0.5f
                || Mathf.Abs(worldPosition.z - transform.position.z) > WorldLodCoverage * 0.5f)) return false;
            return true;
        }

        /// <summary>Samples the still water plane; velocity is zero until a wave backend is attached.</summary>
        public bool SampleSurface(Vector3 worldPosition, out float height, out Vector3 normal, out Vector3 velocity,
            bool includeWaveVelocity = true)
        {
            velocity = Vector3.zero;
            return SampleHeight(worldPosition, out height, out normal);
        }

        /// <summary>Samples water material velocity. The still surface has no wave velocity.</summary>
        public bool SampleSurfaceVelocity(Vector3 worldPosition, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            return SampleHeight(worldPosition, out _, out _);
        }
        private int CalculateTargetResolution()
        {
            int target = CalculateTargetResolutionRaw();
            if (!cameraCenteredLod) return target;
            int lodCap = Mathf.Max(32, lodResolution & ~1);
            return Mathf.Clamp(target + (target & 1), 32, lodCap);
        }

        private int CalculateTargetResolutionRaw()
        {
            int requested = Mathf.Clamp(resolution, 8, 512);
            if (!autoResolution) return requested;
            int cap = Mathf.Clamp(maximumAutoResolution, 64, 256);
            if (cameraCenteredLod) cap = Mathf.Min(cap, Mathf.Clamp(lodResolution, 32, 128));
            return Mathf.Clamp(requested, 8, cap);
        }
    }
}
