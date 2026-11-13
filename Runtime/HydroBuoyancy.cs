using UnityEngine;

namespace HydroEngine
{
    /// <summary>Applies point-sampled buoyancy and water-relative drag to a Rigidbody.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class HydroBuoyancy : MonoBehaviour
    {
        [SerializeField] private HydroSurface surface;
        [SerializeField, Tooltip("Local points distributed across the object's submerged hull.")] private Vector3[] buoyancyPoints =
        {
            new Vector3(-0.35f, -0.3f, -0.35f),
            new Vector3(-0.35f, -0.3f, 0.35f),
            new Vector3(0.35f, -0.3f, -0.35f),
            new Vector3(0.35f, -0.3f, 0.35f)
        };
        [SerializeField, Min(0f), Tooltip("Total water volume displaced when all points are submerged, in cubic world units.")] private float displacedVolume = 0.9f;
        [SerializeField, Min(0.01f), Tooltip("Depth over which each sample point ramps from dry to fully submerged.")] private float buoyancyDepth = 0.5f;
        [SerializeField, Min(0f), Tooltip("Mass density of the fluid. Fresh water is approximately 1000 kg/m^3 when one Unity unit represents one meter.")] private float waterDensity = 1000f;
        [SerializeField, Min(0f), Tooltip("Linear drag applied relative to the sampled wave velocity and added current.")] private float waterDrag = 28f;
        [SerializeField, Range(0.1f, 1.5f), Tooltip("Mass-aware critical-damping ratio for vertical bobbing. Higher values settle faster; values above 1 can feel sluggish.")] private float verticalDampingRatio = 0.9f;
        [SerializeField, Min(0f), Tooltip("Angular damping applied while any buoyancy points are submerged.")] private float angularDamping = 18f;
        [SerializeField] private bool useWaveVelocity = true;

        private Rigidbody body;

        public HydroSurface Surface { get => surface; set => surface = value; }
        public float DisplacedVolume { get => displacedVolume; set => displacedVolume = Mathf.Max(0f, value); }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void OnValidate()
        {
            displacedVolume = Mathf.Max(0f, displacedVolume);
            buoyancyDepth = Mathf.Max(0.01f, buoyancyDepth);
            waterDensity = Mathf.Max(0f, waterDensity);
            waterDrag = Mathf.Max(0f, waterDrag);
            verticalDampingRatio = Mathf.Clamp(verticalDampingRatio, 0.1f, 1.5f);
            angularDamping = Mathf.Max(0f, angularDamping);
        }

        private void FixedUpdate()
        {
            if (surface == null || body == null || buoyancyPoints == null || buoyancyPoints.Length == 0) return;

            float pointVolume = displacedVolume / buoyancyPoints.Length;
            float submergedTotal = 0f;
            float dragPerPoint = waterDrag / buoyancyPoints.Length;
            float gravityMagnitude = Physics.gravity.magnitude;
            Vector3 upDirection = gravityMagnitude > 0.0001f ? -Physics.gravity / gravityMagnitude : Vector3.up;
            float criticalDamping = gravityMagnitude > 0.0001f && displacedVolume > 0f
                ? 2f * verticalDampingRatio * Mathf.Sqrt(body.mass * waterDensity * displacedVolume * gravityMagnitude / buoyancyDepth)
                : 0f;
            float dampingPerPoint = criticalDamping / buoyancyPoints.Length;
            for (int i = 0; i < buoyancyPoints.Length; i++)
            {
                Vector3 point = transform.TransformPoint(buoyancyPoints[i]);
                if (!surface.SampleSurface(point, out float waterHeight, out _, out Vector3 waterVelocity, useWaveVelocity)) continue;

                float submersion = Mathf.Clamp01((waterHeight - point.y) / buoyancyDepth);
                if (submersion <= 0f) continue;
                submergedTotal += submersion;

                Vector3 buoyancyForce = -Physics.gravity * (waterDensity * pointVolume * submersion);
                body.AddForceAtPosition(buoyancyForce, point, ForceMode.Force);

                Vector3 relativeVelocity = body.GetPointVelocity(point) - waterVelocity;
                body.AddForceAtPosition(-relativeVelocity * (dragPerPoint * submersion), point, ForceMode.Force);

                // Apply mass-aware damping as soon as a probe enters the water to stop
                // the buoyancy spring from launching the body back out of the surface.
                float verticalRelativeSpeed = Vector3.Dot(relativeVelocity, upDirection);
                float contactWeight = Mathf.Clamp01(submersion * 10f);
                Vector3 dampingForce = -upDirection * (verticalRelativeSpeed * dampingPerPoint * contactWeight);
                body.AddForceAtPosition(dampingForce, point, ForceMode.Force);
            }

            float submergedFraction = submergedTotal / buoyancyPoints.Length;
            if (submergedFraction > 0f)
                body.AddTorque(-body.angularVelocity * (angularDamping * submergedFraction), ForceMode.Force);
        }
    }
}
