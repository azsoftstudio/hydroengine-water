using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;

namespace HydroEngine
{
    /// <summary>
    /// Renders the scene mirrored across this water surface into a low-resolution texture.
    /// Attach this to a HydroSurface to reflect clouds, shorelines, and nearby geometry.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HydroSurface))]
    [AddComponentMenu("HydroEngine/Planar Reflection")]
    public sealed class HydroPlanarReflection : MonoBehaviour
    {
        [SerializeField, Range(0.125f, 1f), Tooltip("Reflection texture size relative to the source camera. Half resolution is a good starting point.")]
        private float resolutionScale = 0.5f;
        [SerializeField, FormerlySerializedAs("clipPlaneOffset"), Range(0f, 0.25f), Tooltip("Extends reflected geometry slightly below the water plane, in world units, to hide thin low-resolution leaks at object-water contact. Increase gradually; too much can show submerged parts of objects.")]
        private float waterlineOverlap = 0.03f;
        [SerializeField, Tooltip("Layers included in the reflection. HydroEngine water is hidden separately to prevent recursion.")]
        private LayerMask reflectionLayers = -1;
        [SerializeField, Min(0f), Tooltip("Optional maximum reflection camera distance. Zero uses the source camera's far clip plane.")]
        private float maximumDistance;
        [SerializeField, Tooltip("Render real-time shadows in the reflection camera. This improves fidelity but adds GPU cost.")]
        private bool renderShadows = true;

        private HydroSurface surface;
        private MeshRenderer waterRenderer;
        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private MaterialPropertyBlock propertyBlock;
        private UniversalAdditionalCameraData reflectionCameraData;
        private bool isRendering;

        private static readonly int ReflectionTextureId = Shader.PropertyToID("_HydroPlanarReflectionTexture");
        private static readonly int ReflectionEnabledId = Shader.PropertyToID("_HydroPlanarReflectionEnabled");
        private static readonly int RenderingReflectionId = Shader.PropertyToID("_HydroRenderingPlanarReflection");

        /// <summary>Last rendered reflection texture, exposed for the Inspector preview.</summary>
        public Texture CurrentReflectionTexture => reflectionTexture;

        private void OnEnable()
        {
            surface = GetComponent<HydroSurface>();
            waterRenderer = GetComponent<MeshRenderer>();
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseReflectionResources();
            SetReflectionPropertyBlock(null, false);
        }

        private void OnValidate()
        {
            resolutionScale = Mathf.Clamp(resolutionScale, 0.125f, 1f);
            waterlineOverlap = Mathf.Clamp(waterlineOverlap, 0f, 0.25f);
            maximumDistance = Mathf.Max(0f, maximumDistance);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (!isActiveAndEnabled || isRendering || sourceCamera == null ||
                sourceCamera == reflectionCamera || sourceCamera.cameraType == CameraType.Preview ||
                sourceCamera.cameraType == CameraType.Reflection || sourceCamera.stereoEnabled ||
                Shader.GetGlobalFloat(RenderingReflectionId) > 0.5f ||
                surface == null || waterRenderer == null || !waterRenderer.enabled)
                return;

            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                SetReflectionPropertyBlock(null, false);
                return;
            }

            EnsureReflectionResources(sourceCamera);
            if (reflectionCamera == null || reflectionTexture == null)
            {
                SetReflectionPropertyBlock(null, false);
                return;
            }

            ConfigureReflectionCamera(sourceCamera);

            isRendering = true;
            bool previousInvertCulling = GL.invertCulling;
            float previousReflectionState = Shader.GetGlobalFloat(RenderingReflectionId);
            bool reflectionRendered = false;
            try
            {
                // Hide every HydroEngine water surface from the reflection camera to avoid
                // recursive reflections, including other water objects on the same layer.
                Shader.SetGlobalFloat(RenderingReflectionId, 1f);
                GL.invertCulling = !previousInvertCulling;

                // This callback runs inside URP's render loop. SubmitRenderRequest is an
                // outside-the-loop API, so render synchronously with the provided context.
                reflectionCamera.targetTexture = reflectionTexture;
#pragma warning disable 618
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore 618
                reflectionRendered = true;
            }
            finally
            {
                GL.invertCulling = previousInvertCulling;
                Shader.SetGlobalFloat(RenderingReflectionId, previousReflectionState);
                isRendering = false;
            }

            SetReflectionPropertyBlock(reflectionRendered ? reflectionTexture : null, reflectionRendered);
        }

        private void EnsureReflectionResources(Camera sourceCamera)
        {
            int width = Mathf.Max(16, Mathf.RoundToInt(sourceCamera.pixelWidth * resolutionScale));
            int height = Mathf.Max(16, Mathf.RoundToInt(sourceCamera.pixelHeight * resolutionScale));
            if (reflectionTexture != null && (reflectionTexture.width != width || reflectionTexture.height != height))
                ReleaseReflectionTexture();

            if (reflectionTexture == null)
            {
                var descriptor = new RenderTextureDescriptor(width, height,
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.DefaultHDR)
                        ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, 16)
                {
                    msaaSamples = 1,
                    volumeDepth = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                reflectionTexture = new RenderTexture(descriptor)
                {
                    name = $"{name} Planar Reflection",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                reflectionTexture.Create();
            }

            if (reflectionCamera == null)
            {
                var cameraObject = new GameObject($"{name} Reflection Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                reflectionCamera = cameraObject.AddComponent<Camera>();
                reflectionCamera.enabled = false;
                reflectionCameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            }
        }

        private void ConfigureReflectionCamera(Camera sourceCamera)
        {
            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.enabled = false;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.useOcclusionCulling = false;
            // Do not exclude the water object's whole Unity layer: scene objects commonly
            // share Default with water. The shader's reflection-capture clip hides water only.
            reflectionCamera.cullingMask = sourceCamera.cullingMask & reflectionLayers.value;
            reflectionCamera.clearFlags = sourceCamera.clearFlags;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.allowDynamicResolution = false;
            reflectionCamera.farClipPlane = maximumDistance > 0f
                ? Mathf.Max(sourceCamera.nearClipPlane + 0.01f, Mathf.Min(sourceCamera.farClipPlane, maximumDistance))
                : sourceCamera.farClipPlane;

            reflectionCameraData.renderType = CameraRenderType.Base;
            reflectionCameraData.renderPostProcessing = false;
            reflectionCameraData.renderShadows = renderShadows;

            Vector3 planePosition = new Vector3(transform.position.x, transform.position.y, transform.position.z);
            Vector3 planeNormal = Vector3.up;
            Matrix4x4 reflectionMatrix = CalculateReflectionMatrix(planePosition.y);
            reflectionCamera.transform.position = reflectionMatrix.MultiplyPoint(sourceCamera.transform.position);
            Vector3 reflectedForward = reflectionMatrix.MultiplyVector(sourceCamera.transform.forward).normalized;
            Vector3 reflectedUp = reflectionMatrix.MultiplyVector(sourceCamera.transform.up).normalized;
            reflectionCamera.transform.rotation = Quaternion.LookRotation(reflectedForward, reflectedUp);
            reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * reflectionMatrix;

            float sideSign = sourceCamera.transform.position.y >= planePosition.y ? 1f : -1f;
            // Bias the reflection clip plane slightly into the water. This captures a
            // little extra geometry at contact so low-resolution edge pixels do not
            // leave a bright gap between an object and its reflection.
            Vector3 offsetPosition = planePosition - planeNormal * (waterlineOverlap * sideSign);
            Vector3 cameraPlanePosition = reflectionCamera.worldToCameraMatrix.MultiplyPoint(offsetPosition);
            Vector3 cameraPlaneNormal = reflectionCamera.worldToCameraMatrix.MultiplyVector(planeNormal).normalized * sideSign;
            Vector4 cameraSpacePlane = new Vector4(cameraPlaneNormal.x, cameraPlaneNormal.y, cameraPlaneNormal.z,
                -Vector3.Dot(cameraPlanePosition, cameraPlaneNormal));
            // The reflected view has its own camera-space clip plane and culling frustum.
            reflectionCamera.projectionMatrix = sourceCamera.projectionMatrix;
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(cameraSpacePlane);
            reflectionCamera.ResetCullingMatrix();
        }

        private void SetReflectionPropertyBlock(Texture texture, bool enabled)
        {
            if (waterRenderer == null) return;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            waterRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(ReflectionTextureId, texture != null ? texture : Texture2D.blackTexture);
            propertyBlock.SetFloat(ReflectionEnabledId, enabled ? 1f : 0f);
            waterRenderer.SetPropertyBlock(propertyBlock);
        }

        private void ReleaseReflectionResources()
        {
            ReleaseReflectionTexture();
            if (reflectionCamera != null)
            {
                if (Application.isPlaying) Destroy(reflectionCamera.gameObject);
                else DestroyImmediate(reflectionCamera.gameObject);
            }
            reflectionCamera = null;
            reflectionCameraData = null;
        }

        private void ReleaseReflectionTexture()
        {
            if (reflectionTexture == null) return;
            reflectionTexture.Release();
            if (Application.isPlaying) Destroy(reflectionTexture);
            else DestroyImmediate(reflectionTexture);
            reflectionTexture = null;
        }

        private static Matrix4x4 CalculateReflectionMatrix(float planeY)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m11 = -1f;
            matrix.m13 = 2f * planeY;
            return matrix;
        }
    }
}
