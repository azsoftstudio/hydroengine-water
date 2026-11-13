using UnityEngine;

namespace HydroEngine
{
    /// <summary>
    /// Owns the shared profile, simulation clock, and world-to-field mapping for one
    /// contiguous ocean. It intentionally does not create a wave backend yet; until a
    /// backend is attached, the existing HydroSurface remains still.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HydroWaveSimulation : MonoBehaviour
    {
        [SerializeField] private HydroWaveSimulationProfile profile;
        [SerializeField, Min(0f), Tooltip("Scales this ocean field's simulation clock.")]
        private float timeScale = 1f;
        [SerializeField, Tooltip("World XZ coordinate used as the precision origin for field evaluation.")]
        private Vector2 worldOriginXZ;
        [SerializeField, Tooltip("Field-meter coordinate mapped to World Origin XZ. Keep this stable when rebasing the world.")]
        private Vector2 fieldOriginMeters;

        private float simulationTime;

        public HydroWaveSimulationProfile Profile => profile;
        public HydroWaveModel SelectedModel => profile != null ? profile.Model : HydroWaveModel.Still;
        public float SimulationTime => simulationTime;
        public HydroWaveFieldDomain Domain => CreateDomain();
        public float TimeScale { get => timeScale; set => timeScale = Mathf.Max(0f, value); }

        private void OnEnable()
        {
            simulationTime = 0f;
        }

        private void Update()
        {
            simulationTime += Time.deltaTime * timeScale;
        }

        private void OnValidate()
        {
            timeScale = Mathf.Max(0f, timeScale);
        }

        /// <summary>
        /// Shifts the precision origin without changing the field coordinate of any
        /// world point. Call after a floating-origin shift, not when moving the ocean mesh.
        /// </summary>
        public void RebaseWorldOrigin(Vector2 newWorldOriginXZ)
        {
            HydroWaveFieldDomain rebased = Domain.Rebase(newWorldOriginXZ);
            worldOriginXZ = rebased.WorldOriginXZ;
            fieldOriginMeters = rebased.FieldOriginMeters;
        }

        private HydroWaveFieldDomain CreateDomain()
        {
            float unitsPerMeter = profile != null ? profile.WorldUnitsPerMeter : 1f;
            return new HydroWaveFieldDomain(worldOriginXZ, fieldOriginMeters, unitsPerMeter);
        }
    }
}
