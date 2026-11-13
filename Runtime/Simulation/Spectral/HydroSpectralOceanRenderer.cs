using UnityEngine;

namespace HydroEngine
{
    /// <summary>Optional renderer bridge for the experimental GPU spectral simulation.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HydroSurface), typeof(MeshRenderer))]
    public sealed class HydroSpectralOceanRenderer : MonoBehaviour
    {
        [SerializeField] private ComputeShader computeShader;
        [SerializeField, Range(64, 256), Tooltip("Rounded to the nearest power of two for the radix-2 transform.")] private int resolution = 128;
        [SerializeField, Min(16f)] private float patchLength = 256f;
        [SerializeField] private Vector2 windDirection = new Vector2(1f, 0.2f);
        [SerializeField, Range(0.1f, 40f)] private float windSpeed = 12f;
        [SerializeField, Range(0f, 0.0001f)] private float amplitude = 0.0000081f;
        [SerializeField, Range(0f, 2f)] private float choppiness = 0.8f;
        [SerializeField, Range(0.016f, 0.2f)] private float updateInterval = 0.033f;

        private static readonly int TextureId = Shader.PropertyToID("_HydroSpectralDisplacement");
        private static readonly int EnabledId = Shader.PropertyToID("_HydroSpectralEnabled");
        private static readonly int PatchLengthId = Shader.PropertyToID("_HydroSpectralPatchLength");
        private static readonly int ResolutionId = Shader.PropertyToID("_HydroSpectralResolution");
        private HydroSpectralOcean simulation;
        private MeshRenderer targetRenderer;
        private MaterialPropertyBlock properties;

        public bool IsReady => simulation != null && simulation.IsReady;

        private void OnEnable()
        {
            targetRenderer = GetComponent<MeshRenderer>();
            properties = new MaterialPropertyBlock();
            Reinitialize();
        }

        private void OnDisable()
        {
            SetRendererState(null);
            if (simulation != null) { simulation.Dispose(); simulation = null; }
        }

        private void OnValidate()
        {
            resolution = Mathf.Clamp(resolution, 64, 256);
            patchLength = Mathf.Max(16f, patchLength);
            windSpeed = Mathf.Clamp(windSpeed, 0.1f, 40f);
            amplitude = Mathf.Max(0f, amplitude);
            choppiness = Mathf.Clamp(choppiness, 0f, 2f);
            updateInterval = Mathf.Clamp(updateInterval, 0.016f, 0.2f);
            if (isActiveAndEnabled) Reinitialize();
        }

        private void LateUpdate()
        {
            if (simulation == null || !simulation.IsReady) return;
            float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            Texture displacement = simulation.Update(time, updateInterval);
            SetRendererState(displacement);
        }

        private void Reinitialize()
        {
            if (simulation != null) { simulation.Dispose(); simulation = null; }
            if (!SystemInfo.supportsComputeShaders) { SetRendererState(null); return; }
            if (computeShader == null) computeShader = Resources.Load<ComputeShader>("HydroSpectralOcean");
            if (computeShader == null) { SetRendererState(null); return; }
            simulation = new HydroSpectralOcean(computeShader, resolution, patchLength,
                windDirection, windSpeed, amplitude, choppiness);
        }

        private void SetRendererState(Texture displacement)
        {
            if (targetRenderer == null) targetRenderer = GetComponent<MeshRenderer>();
            if (targetRenderer == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(properties);
            properties.SetTexture(TextureId, displacement != null ? displacement : Texture2D.blackTexture);
            properties.SetFloat(EnabledId, displacement != null ? 1f : 0f);
            properties.SetFloat(PatchLengthId, patchLength);
            properties.SetFloat(ResolutionId, simulation != null ? simulation.Resolution : 128);
            targetRenderer.SetPropertyBlock(properties);
        }
    }
}
