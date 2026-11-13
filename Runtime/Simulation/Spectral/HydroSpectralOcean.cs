using UnityEngine;

namespace HydroEngine
{
    /// <summary>GPU Phillips-spectrum ocean patch evaluated by a separable radix-2 inverse FFT.</summary>
    internal sealed class HydroSpectralOcean : System.IDisposable
    {
        private readonly ComputeShader compute;
        private readonly int resolution;
        private readonly float patchLength;
        private readonly Vector2 windDirection;
        private readonly float windSpeed;
        private readonly float amplitude;
        private readonly float choppiness;
        private readonly int initializeKernel, evolveKernel, reverseKernel, butterflyKernel, outputKernel;
        private RenderTexture h0, spectrumA, spectrumB, zA, zB, displacement;
        private RenderTexture currentSpectrum, currentZ;
        private float lastUpdate = float.NegativeInfinity;

        public Texture DisplacementTexture => displacement;
        public float PatchLength => patchLength;
        public int Resolution => resolution;
        public bool IsReady => IsCreated(h0) && IsCreated(spectrumA) && IsCreated(spectrumB)
            && IsCreated(zA) && IsCreated(zB) && IsCreated(displacement);

        public HydroSpectralOcean(ComputeShader shader, int size, float worldSize, Vector2 wind,
            float speed, float waveAmplitude, float horizontalChoppiness)
        {
            compute = shader;
            resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(size, 64, 256));
            patchLength = Mathf.Max(16f, worldSize);
            windDirection = wind.sqrMagnitude > 0.0001f ? wind.normalized : Vector2.right;
            windSpeed = Mathf.Max(0.1f, speed);
            amplitude = Mathf.Max(0f, waveAmplitude);
            choppiness = Mathf.Clamp(horizontalChoppiness, 0f, 2f);
            initializeKernel = compute.FindKernel("InitializeSpectrum");
            evolveKernel = compute.FindKernel("EvolveSpectrum");
            reverseKernel = compute.FindKernel("BitReverse");
            butterflyKernel = compute.FindKernel("Butterfly");
            outputKernel = compute.FindKernel("BuildDisplacement");
            Allocate();
            if (IsReady) Initialize();
            else Dispose();
        }

        public Texture Update(float time, float minimumInterval)
        {
            if (compute == null || !IsReady) return null;
            if (time - lastUpdate < minimumInterval) return displacement;
            lastUpdate = time;
            compute.SetFloat("_WaveTime", time);
            compute.SetTexture(evolveKernel, "_H0", h0);
            compute.SetTexture(evolveKernel, "_WriteSpectrum", spectrumA);
            compute.SetTexture(evolveKernel, "_WriteZ", zA);
            Dispatch(evolveKernel);

            RunBitReverse(0, spectrumA, zA, spectrumB, zB);
            RunBitReverse(1, spectrumB, zB, spectrumA, zA);
            currentSpectrum = spectrumA;
            currentZ = zA;
            RunAxis(0);
            RunAxis(1);

            compute.SetTexture(outputKernel, "_HeightSpectrum", currentSpectrum);
            compute.SetTexture(outputKernel, "_XZSpectrum", currentSpectrum);
            compute.SetTexture(outputKernel, "_ZSpectrum", currentZ);
            compute.SetTexture(outputKernel, "_Displacement", displacement);
            compute.SetFloat("_InverseNormalization", 1f / (resolution * (float)resolution));
            Dispatch(outputKernel);
            return displacement;
        }

        private void Initialize()
        {
            compute.SetInt("_Resolution", resolution);
            compute.SetFloat("_PatchLength", patchLength);
            compute.SetFloat("_WindSpeed", windSpeed);
            compute.SetVector("_WindDirection", new Vector4(windDirection.x, windDirection.y, 0f, 0f));
            compute.SetFloat("_Amplitude", amplitude);
            compute.SetFloat("_Choppiness", choppiness);
            compute.SetTexture(initializeKernel, "_H0", h0);
            Dispatch(initializeKernel);
        }

        private void RunAxis(int axis)
        {
            compute.SetInt("_Axis", axis);
            for (int stage = 2; stage <= resolution; stage <<= 1)
            {
                compute.SetInt("_StageLength", stage);
                compute.SetTexture(butterflyKernel, "_ReadSpectrum", currentSpectrum);
                compute.SetTexture(butterflyKernel, "_ReadZ", currentZ);
                RenderTexture nextSpectrum = currentSpectrum == spectrumA ? spectrumB : spectrumA;
                RenderTexture nextZ = currentZ == zA ? zB : zA;
                compute.SetTexture(butterflyKernel, "_WriteSpectrum", nextSpectrum);
                compute.SetTexture(butterflyKernel, "_WriteZ", nextZ);
                Dispatch(butterflyKernel);
                currentSpectrum = nextSpectrum;
                currentZ = nextZ;
            }
        }

        private void RunBitReverse(int axis, RenderTexture sourceSpectrum, RenderTexture sourceZ,
            RenderTexture targetSpectrum, RenderTexture targetZ)
        {
            compute.SetInt("_Axis", axis);
            compute.SetTexture(reverseKernel, "_ReadSpectrum", sourceSpectrum);
            compute.SetTexture(reverseKernel, "_ReadZ", sourceZ);
            compute.SetTexture(reverseKernel, "_WriteSpectrum", targetSpectrum);
            compute.SetTexture(reverseKernel, "_WriteZ", targetZ);
            Dispatch(reverseKernel);
        }

        private void Dispatch(int kernel) => compute.Dispatch(kernel, resolution / 8, resolution / 8, 1);

        private void Allocate()
        {
            h0 = Create("Hydro FFT Initial Spectrum", RenderTextureFormat.ARGBHalf);
            spectrumA = Create("Hydro FFT Spectrum A", RenderTextureFormat.ARGBHalf);
            spectrumB = Create("Hydro FFT Spectrum B", RenderTextureFormat.ARGBHalf);
            zA = Create("Hydro FFT Z A", RenderTextureFormat.RGHalf);
            zB = Create("Hydro FFT Z B", RenderTextureFormat.RGHalf);
            displacement = Create("Hydro FFT Displacement", RenderTextureFormat.ARGBHalf);
        }

        private RenderTexture Create(string label, RenderTextureFormat format)
        {
            var texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
            {
                name = label,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (texture.Create()) return texture;
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
            return null;
        }

        public void Dispose()
        {
            Release(h0); Release(spectrumA); Release(spectrumB); Release(zA); Release(zB); Release(displacement);
            h0 = spectrumA = spectrumB = zA = zB = displacement = null;
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }

        private static bool IsCreated(RenderTexture texture) => texture != null && texture.IsCreated();
    }
}
