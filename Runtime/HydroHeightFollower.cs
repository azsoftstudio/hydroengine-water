using UnityEngine;

namespace HydroEngine
{
    /// <summary>Kinematic visual follower for query demos; use HydroBuoyancy for dynamic physics.</summary>
    public sealed class HydroHeightFollower : MonoBehaviour
    {
        [SerializeField] private HydroSurface surface;
        [SerializeField] private float heightOffset = 0.5f;
        [SerializeField, Min(0f)] private float positionSharpness = 8f;

        public HydroSurface Surface { get => surface; set => surface = value; }
        public float HeightOffset { get => heightOffset; set => heightOffset = value; }

        private void LateUpdate()
        {
            if (surface == null || !surface.SampleHeight(transform.position, out float height, out _)) return;
            Vector3 position = transform.position;
            float targetY = height + heightOffset;
            position.y = positionSharpness <= 0f
                ? targetY
                : Mathf.Lerp(position.y, targetY, 1f - Mathf.Exp(-positionSharpness * Time.deltaTime));
            transform.position = position;
        }
    }
}
