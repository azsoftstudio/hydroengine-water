using UnityEngine;

namespace HydroEngine
{
    /// <summary>Relative energy and wavelength interval for one scale of ocean waves.</summary>
    [System.Serializable]
    public struct HydroWaveBandSettings
    {
        [SerializeField, Min(0.01f), Tooltip("Shortest wavelength in this band, in meters.")]
        private float minimumWavelengthMeters;
        [SerializeField, Min(0.01f), Tooltip("Longest wavelength in this band, in meters.")]
        private float maximumWavelengthMeters;
        [SerializeField, Min(0f), Tooltip("Relative wave energy before the selected model normalizes to Significant Wave Height.")]
        private float energy;

        public float MinimumWavelengthMeters => minimumWavelengthMeters;
        public float MaximumWavelengthMeters => maximumWavelengthMeters;
        public float Energy => energy;

        public HydroWaveBandSettings(float minimumWavelengthMeters, float maximumWavelengthMeters, float energy)
        {
            this.minimumWavelengthMeters = Mathf.Max(0.01f, minimumWavelengthMeters);
            this.maximumWavelengthMeters = Mathf.Max(this.minimumWavelengthMeters, maximumWavelengthMeters);
            this.energy = Mathf.Max(0f, energy);
        }

        public void Validate()
        {
            minimumWavelengthMeters = Mathf.Max(0.01f, minimumWavelengthMeters);
            maximumWavelengthMeters = Mathf.Max(minimumWavelengthMeters, maximumWavelengthMeters);
            energy = Mathf.Max(0f, energy);
        }
    }

    /// <summary>
    /// Shared authored sea state for one ocean field. Assign the same profile to surfaces
    /// that belong to the same ocean. Runtime time and GPU resources belong to the future
    /// HydroWaveSimulation owner, not this reusable asset.
    /// </summary>
    [CreateAssetMenu(menuName = "HydroEngine/Water/Wave Simulation Profile", fileName = "HydroWaveSimulationProfile")]
    public sealed class HydroWaveSimulationProfile : ScriptableObject
    {
        [Header("Wave Model")]
        [SerializeField, Tooltip("Still is the only active mode until a wave backend is connected. Gerstner and FFT Ocean define the intended selection workflow.")]
        private HydroWaveModel model = HydroWaveModel.Still;
        [SerializeField, Min(0.01f), Tooltip("Significant wave height (Hs), in meters. Used to scale total spectral energy.")]
        private float significantWaveHeightMeters = 2.5f;
        [SerializeField, Tooltip("Primary direction waves travel, in field-space XZ. This is the direction of motion, not the direction the wind comes from.")]
        private Vector2 travelDirection = new Vector2(1f, 0.2f);
        [SerializeField, Range(0f, 90f), Tooltip("Angular spread around the primary travel direction, in degrees.")]
        private float directionalSpreadDegrees = 35f;
        [SerializeField, Min(0.01f), Tooltip("Gravitational acceleration in meters per second squared.")]
        private float gravityMetersPerSecondSquared = 9.81f;
        [SerializeField, Tooltip("Seed used for deterministic phases and directional component generation.")]
        private int randomSeed = 19471;
        [SerializeField, Min(0.0001f), Tooltip("Unity world units that represent one meter.")]
        private float worldUnitsPerMeter = 1f;

        [Header("Long Waves")]
        [SerializeField] private HydroWaveBandSettings longWaves = new HydroWaveBandSettings(50f, 300f, 0.7f);
        [Header("Mid Waves")]
        [SerializeField] private HydroWaveBandSettings midWaves = new HydroWaveBandSettings(8f, 50f, 1f);
        [Header("Short Waves")]
        [SerializeField] private HydroWaveBandSettings shortWaves = new HydroWaveBandSettings(1f, 8f, 0.35f);

        [Header("Gerstner Options")]
        [SerializeField, Range(4, 64), Tooltip("Maximum deterministic wave components distributed across the enabled wavelength bands.")]
        private int gerstnerComponentCount = 24;
        [SerializeField, Range(0.01f, 0.9f), Tooltip("Upper bound for combined horizontal steepness before backend normalization.")]
        private float maximumGerstnerSteepness = 0.55f;

        [Header("FFT Options")]
        [SerializeField, Range(64, 512), Tooltip("FFT samples per side for each cascade. Must be a power of two in the backend.")]
        private int fftResolution = 128;
        [SerializeField, Range(2, 4), Tooltip("Number of overlapping wavelength cascades spanning the configured bands.")]
        private int fftCascadeCount = 3;

        public HydroWaveModel Model => model;
        public float SignificantWaveHeightMeters => significantWaveHeightMeters;
        public Vector2 TravelDirection => travelDirection;
        public float DirectionalSpreadDegrees => directionalSpreadDegrees;
        public float GravityMetersPerSecondSquared => gravityMetersPerSecondSquared;
        public int RandomSeed => randomSeed;
        public float WorldUnitsPerMeter => worldUnitsPerMeter;
        public HydroWaveBandSettings LongWaves => longWaves;
        public HydroWaveBandSettings MidWaves => midWaves;
        public HydroWaveBandSettings ShortWaves => shortWaves;
        public int GerstnerComponentCount => gerstnerComponentCount;
        public float MaximumGerstnerSteepness => maximumGerstnerSteepness;
        public int FftResolution => fftResolution;
        public int FftCascadeCount => fftCascadeCount;

        private void OnValidate()
        {
            significantWaveHeightMeters = Mathf.Max(0.01f, significantWaveHeightMeters);
            gravityMetersPerSecondSquared = Mathf.Max(0.01f, gravityMetersPerSecondSquared);
            worldUnitsPerMeter = Mathf.Max(0.0001f, worldUnitsPerMeter);
            directionalSpreadDegrees = Mathf.Clamp(directionalSpreadDegrees, 0f, 90f);
            if (travelDirection.sqrMagnitude < 0.0001f) travelDirection = Vector2.right;
            else travelDirection.Normalize();
            longWaves.Validate();
            midWaves.Validate();
            shortWaves.Validate();
            gerstnerComponentCount = Mathf.Clamp(gerstnerComponentCount, 4, 64);
            maximumGerstnerSteepness = Mathf.Clamp(maximumGerstnerSteepness, 0.01f, 0.9f);
            fftResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(fftResolution), 64, 512);
            fftCascadeCount = Mathf.Clamp(fftCascadeCount, 2, 4);
        }
    }
}
