using UnityEngine;
using UnityEngine.Serialization;

namespace HydroEngine
{
    public static class HydroWaveConstants
    {
        public const int MaxWaveCount = 12;
    }

    [System.Serializable]
    public struct HydroWave
    {
        [Tooltip("World-space XZ travel direction.")] public Vector2 direction;
        [Min(0.01f)] public float wavelength;
        [Min(0f)] public float amplitude;
        [Range(0f, 1f), Tooltip("Horizontal crest sharpness. Keep the sum moderate to avoid folding waves.")] public float steepness;
        [Tooltip("Independent phase offset, in radians.")] public float phaseOffset;
    }


    [CreateAssetMenu(menuName = "HydroEngine/Water Wave Profile", fileName = "HydroWaveProfile")]
    public sealed class HydroWaveProfile : ScriptableObject
    {
        [SerializeField, Tooltip("Directional components are sorted from long swell to short ripples. Wavelengths and amplitudes use world units; the default profile targets broad ocean surfaces.")] private HydroWave[] waves =
        {
            new HydroWave { direction = new Vector2(1f, 0.08f), wavelength = 180f, amplitude = 1.05f, steepness = 0.19f, phaseOffset = 0.31f },
            new HydroWave { direction = new Vector2(0.96f, 0.28f), wavelength = 112f, amplitude = 0.72f, steepness = 0.16f, phaseOffset = 2.13f },
            new HydroWave { direction = new Vector2(0.88f, -0.24f), wavelength = 68f, amplitude = 0.48f, steepness = 0.13f, phaseOffset = -1.56f },
            new HydroWave { direction = new Vector2(0.72f, 0.69f), wavelength = 42f, amplitude = 0.30f, steepness = 0.10f, phaseOffset = 1.46f },
            new HydroWave { direction = new Vector2(0.98f, -0.14f), wavelength = 25f, amplitude = 0.18f, steepness = 0.08f, phaseOffset = -1.07f },
            new HydroWave { direction = new Vector2(0.83f, 0.56f), wavelength = 15f, amplitude = 0.10f, steepness = 0.06f, phaseOffset = -2.51f },
            new HydroWave { direction = new Vector2(0.94f, 0.34f), wavelength = 8f, amplitude = 0.045f, steepness = 0.045f, phaseOffset = 0.92f },
            new HydroWave { direction = new Vector2(0.74f, -0.67f), wavelength = 4.8f, amplitude = 0.018f, steepness = 0.03f, phaseOffset = -0.45f }
        };
        [Header("Wind Spectrum Generator")]
        [SerializeField, Min(0.1f), Tooltip("Peak wave length in world units. The generated component energy is distributed around this scale.")] private float spectrumPeakWavelength = 70f;
        [SerializeField, Min(0f), Tooltip("Approximate significant wave height in world units. Individual component amplitudes are normalized to this energy level.")] private float spectrumSignificantHeight = 3f;
        [SerializeField, Tooltip("Primary wave travel direction in world XZ.")] private Vector2 spectrumWindDirection = new Vector2(1f, 0.2f);
        [SerializeField, Range(0f, 90f), Tooltip("Maximum directional spread around the wind direction.")] private float spectrumDirectionalSpread = 35f;
        [SerializeField, Range(0.01f, 0.9f), Tooltip("Peak-component Gerstner sharpness. Other components scale down with the square root of their spectral energy to avoid equally sharp tiny waves.")] private float spectrumSteepness = 0.5f;
        [SerializeField, Range(0.03f, 0.5f), Tooltip("Shortest generated wavelength as a fraction of the peak wavelength. Lower values add fine geometric peaks when the mesh can resolve them.")] private float spectrumMinimumWavelengthRatio = 0.08f;
        [SerializeField, Range(2f, 12f), Tooltip("Longest generated wavelength as a multiple of the peak wavelength.")] private float spectrumMaximumWavelengthRatio = 8f;
        [SerializeField, FormerlySerializedAs("spectrumSwellEnergy"), Range(0.05f, 4f), Tooltip("Relative amplitude multiplier for long swells. Its variance contribution scales with the square; Significant Height renormalizes the total.")] private float spectrumSwellAmplitude = 0.75f;
        [SerializeField, FormerlySerializedAs("spectrumPrimaryEnergy"), Range(0.05f, 4f), Tooltip("Relative amplitude multiplier around the peak wavelength. Its variance contribution scales with the square; Significant Height renormalizes the total.")] private float spectrumPrimaryAmplitude = 1f;
        [SerializeField, FormerlySerializedAs("spectrumChopEnergy"), Range(0.05f, 4f), Tooltip("Relative amplitude multiplier for short chop. Its variance contribution scales with the square; Significant Height renormalizes the total.")] private float spectrumChopAmplitude = 2f;
        [SerializeField, Tooltip("Fixed random seed makes generated directions, wavelengths, and phases reproducible.")] private int spectrumSeed = 19471;
        public float TimeScale = 1f;
        public HydroWave[] Waves => waves;

        /// <summary>Regenerates this profile from a deterministic, wind-directed multi-band wave spectrum.</summary>
        public void GenerateWindSpectrum()
        {
            const int componentCount = HydroWaveConstants.MaxWaveCount;
            var random = new System.Random(spectrumSeed);
            waves = new HydroWave[componentCount];
            float[] energies = new float[componentCount];
            float energySum = 0f;
            float peak = Mathf.Max(0.1f, spectrumPeakWavelength);
            float minimumRatio = Mathf.Max(0.03f, spectrumMinimumWavelengthRatio);
            float maximumRatio = Mathf.Max(minimumRatio * 1.5f, spectrumMaximumWavelengthRatio);
            float logMinimum = Mathf.Log(minimumRatio);
            float logMaximum = Mathf.Log(maximumRatio);
            float maximumEnergy = 0f;
            Vector2 wind = spectrumWindDirection.sqrMagnitude > 0.0001f
                ? spectrumWindDirection.normalized
                : Vector2.right;

            for (int i = 0; i < componentCount; i++)
            {
                // Jitter log-spaced bands so the discrete sum does not form a harmonic ladder.
                float position = (i + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.36f) / componentCount;
                float ratio = Mathf.Exp(Mathf.Lerp(logMaximum, logMinimum, position));
                float logScale = Mathf.Log(ratio);
                // Broad lognormal base distributes energy over octaves. Smooth band
                // gains expose intuitive swell/primary/chop balance without changing Hs.
                float energy = Mathf.Exp(-0.5f * logScale * logScale);
                float swellWeight = SmoothBand(ratio, 2f, 5f);
                float chopWeight = 1f - SmoothBand(ratio, minimumRatio, 0.5f);
                float primaryWeight = Mathf.Max(0f, 1f - Mathf.Max(swellWeight, chopWeight));
                float bandGain = swellWeight * spectrumSwellAmplitude * spectrumSwellAmplitude
                    + primaryWeight * spectrumPrimaryAmplitude * spectrumPrimaryAmplitude
                    + chopWeight * spectrumChopAmplitude * spectrumChopAmplitude;
                energy *= bandGain;
                energies[i] = energy;
                energySum += energy;
                maximumEnergy = Mathf.Max(maximumEnergy, energy);

                // Cluster energy near the wind direction while retaining a randomized
                // tail out to the authored directional-spread limit.
                float spread = (float)random.NextDouble() * 2f - 1f;
                float shapedSpread = Mathf.Sign(spread) * spread * spread;
                float angle = shapedSpread * spectrumDirectionalSpread * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(
                    wind.x * Mathf.Cos(angle) - wind.y * Mathf.Sin(angle),
                    wind.x * Mathf.Sin(angle) + wind.y * Mathf.Cos(angle));
                float phase = ((float)random.NextDouble() * 2f - 1f) * Mathf.PI;
                waves[i] = new HydroWave
                {
                    direction = direction,
                    wavelength = peak * ratio,
                    amplitude = 0f,
                    steepness = 0f,
                    phaseOffset = phase
                };
            }

            // Hs ~= 4 sigma. For independent sinusoidal components, variance is sum(A^2 / 2).
            float targetVariance = spectrumSignificantHeight * spectrumSignificantHeight / 16f;
            for (int i = 0; i < componentCount; i++)
            {
                waves[i].amplitude = Mathf.Sqrt(2f * targetVariance * energies[i] / Mathf.Max(0.000001f, energySum));
                waves[i].steepness = spectrumSteepness * Mathf.Sqrt(energies[i] / Mathf.Max(0.000001f, maximumEnergy));
            }

            OnValidate();
        }

        private void OnValidate()
        {
            spectrumPeakWavelength = Mathf.Max(0.1f, spectrumPeakWavelength);
            spectrumSignificantHeight = Mathf.Max(0f, spectrumSignificantHeight);
            spectrumDirectionalSpread = Mathf.Clamp(spectrumDirectionalSpread, 0f, 90f);
            spectrumSteepness = Mathf.Clamp(spectrumSteepness, 0.01f, 0.9f);
            spectrumMinimumWavelengthRatio = Mathf.Clamp(spectrumMinimumWavelengthRatio, 0.03f, 0.5f);
            spectrumMaximumWavelengthRatio = Mathf.Max(spectrumMinimumWavelengthRatio * 1.5f, spectrumMaximumWavelengthRatio);
            spectrumSwellAmplitude = Mathf.Clamp(spectrumSwellAmplitude, 0.05f, 4f);
            spectrumPrimaryAmplitude = Mathf.Clamp(spectrumPrimaryAmplitude, 0.05f, 4f);
            spectrumChopAmplitude = Mathf.Clamp(spectrumChopAmplitude, 0.05f, 4f);
            if (spectrumWindDirection.sqrMagnitude < 0.0001f) spectrumWindDirection = Vector2.right;
            else spectrumWindDirection.Normalize();
            if (waves == null) waves = System.Array.Empty<HydroWave>();
            if (waves.Length > HydroWaveConstants.MaxWaveCount)
                System.Array.Resize(ref waves, HydroWaveConstants.MaxWaveCount);
            for (int i = 0; i < Mathf.Min(waves.Length, HydroWaveConstants.MaxWaveCount); i++)
            {
                waves[i].wavelength = Mathf.Max(0.01f, waves[i].wavelength);
                waves[i].amplitude = Mathf.Max(0f, waves[i].amplitude);
                waves[i].steepness = Mathf.Clamp01(waves[i].steepness);
                waves[i].phaseOffset = Mathf.Repeat(waves[i].phaseOffset + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
                if (waves[i].direction.sqrMagnitude < 0.0001f) waves[i].direction = Vector2.right;
                else waves[i].direction.Normalize();
            }
        }

        private static float SmoothBand(float value, float start, float end)
        {
            float t = Mathf.Clamp01((value - start) / Mathf.Max(0.0001f, end - start));
            return t * t * (3f - 2f * t);
        }
    }
}
