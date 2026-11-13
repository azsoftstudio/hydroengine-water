using System;
using UnityEngine;

namespace HydroEngine
{
    /// <summary>
    /// Selects the environmental wave model. Still is the safe baseline; Gerstner and
    /// FFT will become user-selectable when their backends satisfy this contract.
    /// </summary>
    public enum HydroWaveModel
    {
        Still = 0,
        Gerstner = 1,
        FftOcean = 2
    }

    [Flags]
    public enum HydroWaveCapabilities
    {
        None = 0,
        RenderDisplacement = 1 << 0,
        SurfaceQueries = 1 << 1,
        SurfaceVelocity = 1 << 2,
        Compression = 1 << 3,
        ComputeShader = 1 << 4,
        AsynchronousQueries = 1 << 5
    }

    public enum HydroWaveQueryMode
    {
        Unavailable,
        Synchronous,
        Asynchronous
    }

    public enum HydroWaveQueryStatus
    {
        Unavailable,
        Pending,
        Ready
    }

    /// <summary>
    /// Maps Unity world XZ coordinates into a stable, meter-based ocean field domain.
    /// The origin is a precision/rebasing aid; it does not follow an individual mesh.
    /// </summary>
    public readonly struct HydroWaveFieldDomain
    {
        public readonly Vector2 WorldOriginXZ;
        public readonly Vector2 FieldOriginMeters;
        public readonly float WorldUnitsPerMeter;

        public HydroWaveFieldDomain(Vector2 worldOriginXZ, Vector2 fieldOriginMeters, float worldUnitsPerMeter)
        {
            WorldOriginXZ = worldOriginXZ;
            FieldOriginMeters = fieldOriginMeters;
            WorldUnitsPerMeter = Mathf.Max(0.0001f, worldUnitsPerMeter);
        }

        /// <summary>Maps a world position to field meters, where wave phases are evaluated.</summary>
        public Vector2 WorldToFieldMeters(Vector2 worldXZ)
            => FieldOriginMeters + (worldXZ - WorldOriginXZ) / WorldUnitsPerMeter;

        /// <summary>Maps field meters back to Unity world XZ.</summary>
        public Vector2 FieldMetersToWorld(Vector2 fieldMeters)
            => WorldOriginXZ + (fieldMeters - FieldOriginMeters) * WorldUnitsPerMeter;

        /// <summary>
        /// Returns an equivalent domain with a nearer world origin. Call this when the
        /// world origin rebases; the field coordinate of the new origin is preserved.
        /// </summary>
        public HydroWaveFieldDomain Rebase(Vector2 newWorldOriginXZ)
            => new HydroWaveFieldDomain(newWorldOriginXZ, WorldToFieldMeters(newWorldOriginXZ), WorldUnitsPerMeter);
    }

    /// <summary>Stable identity for a query that may complete asynchronously.</summary>
    public readonly struct HydroWaveQueryHandle : IEquatable<HydroWaveQueryHandle>
    {
        public readonly int Id;
        public readonly uint Generation;

        public HydroWaveQueryHandle(int id, uint generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool Equals(HydroWaveQueryHandle other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is HydroWaveQueryHandle other && Equals(other);
        public override int GetHashCode() => (Id * 397) ^ (int)Generation;
    }

    /// <summary>
    /// A gameplay sample at a requested world-space XZ coordinate. On success,
    /// Position.x/z match that query coordinate and Position.y is the surface height.
    /// SampleTime identifies the simulation state that produced the sample.
    /// </summary>
    public readonly struct HydroSurfaceSample
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector3 Velocity;

        /// <summary>Unitless horizontal compression: max(0, 1 - det(dX/dx)).</summary>
        public readonly float Compression;

        public readonly float SampleTime;

        public HydroSurfaceSample(Vector3 position, Vector3 normal, Vector3 velocity,
            float compression, float sampleTime)
        {
            Position = position;
            Normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
            Velocity = velocity;
            Compression = Mathf.Max(0f, compression);
            SampleTime = sampleTime;
        }
    }

    /// <summary>Immutable field conventions supplied when a backend starts.</summary>
    public readonly struct HydroWaveBackendContext
    {
        /// <summary>Gravitational acceleration in meters per second squared.</summary>
        public readonly float Gravity;

        /// <summary>Shared world-to-field coordinate mapping for this ocean field.</summary>
        public readonly HydroWaveFieldDomain Domain;

        public HydroWaveBackendContext(HydroWaveFieldDomain domain, float gravity)
        {
            Domain = domain;
            Gravity = Mathf.Max(0f, gravity);
        }
    }

    /// <summary>
    /// One authoritative simulation step. Time is absolute and shared by all outputs;
    /// the transform matrices are the current surface transform for this step.
    /// </summary>
    public readonly struct HydroWaveStep
    {
        public readonly float Time;
        public readonly float DeltaTime;
        public readonly Matrix4x4 LocalToWorld;
        public readonly Matrix4x4 WorldToLocal;

        public HydroWaveStep(float time, float deltaTime, Matrix4x4 localToWorld)
        {
            Time = time;
            DeltaTime = Mathf.Max(0f, deltaTime);
            LocalToWorld = localToWorld;
            WorldToLocal = localToWorld.inverse;
        }
    }

    /// <summary>
    /// Backend lifecycle and rendering contract. Implementations bind only HydroEngine
    /// material properties into the supplied block; they never mutate a shared material
    /// or renderer-global state. Simulation time is provided by the owner.
    /// </summary>
    public interface IHydroWaveBackend : IDisposable
    {
        HydroWaveModel Model { get; }
        HydroWaveCapabilities Capabilities { get; }
        bool IsReady { get; }
        float SimulationTime { get; }

        void Initialize(HydroWaveBackendContext context);
        void Step(HydroWaveStep step);
        void BindRenderData(MaterialPropertyBlock properties);
    }

    /// <summary>
    /// Query contract is separate because GPU readback may be asynchronous. A provider
    /// must not report a different model or silently substitute another model's samples.
    /// </summary>
    public interface IHydroWaveQueryProvider
    {
        HydroWaveQueryMode QueryMode { get; }

        bool TrySampleSurface(Vector2 worldXZ, out HydroSurfaceSample sample);

        bool RequestSurfaceSample(Vector2 worldXZ, out HydroWaveQueryHandle handle);

        HydroWaveQueryStatus PollSurfaceSample(HydroWaveQueryHandle handle, out HydroSurfaceSample sample);

        void ReleaseSurfaceSample(HydroWaveQueryHandle handle);
    }
}
