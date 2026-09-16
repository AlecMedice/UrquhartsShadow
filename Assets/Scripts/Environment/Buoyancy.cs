using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Environment
{
    /// <summary>
    /// Multi-point buoyancy against <see cref="OceanSurface"/>. Used by the research vessel
    /// (rocking with the waves, extra in storms) and the ROV. Runs only where the object is simulated
    /// (server for networked rigidbodies); clients receive the transform via NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Buoyancy : MonoBehaviour
    {
        [Tooltip("Points on the hull that push up when below the surface. Four corners work well.")]
        [SerializeField] private Transform[] floaters;
        [SerializeField] private float floatStrength = 12f;
        [SerializeField] private float waterDrag = 1.2f;
        [SerializeField] private float waterAngularDrag = 0.8f;
        [SerializeField] private float airDrag = 0.05f;
        [SerializeField] private float airAngularDrag = 0.05f;
        [Tooltip("Scale wave influence (rocking). Vessel uses config RockingAmplitude * storm multiplier.")]
        public float RockingScale = 1f;

        [Tooltip("Set false on clients for networked objects so physics only runs on the server.")]
        public bool Simulate = true;

        private Rigidbody _rb;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void FixedUpdate()
        {
            if (!Simulate) return;
            var ocean = OceanSurface.Instance;
            if (ocean == null || floaters == null || floaters.Length == 0) return;

            int submerged = 0;
            foreach (var f in floaters)
            {
                if (f == null) continue;
                float surface = ocean.SeaLevel + (ocean.GetHeightAt(f.position.x, f.position.z) - ocean.SeaLevel) * RockingScale;
                float depth = surface - f.position.y;
                if (depth > 0f)
                {
                    submerged++;
                    float force = Mathf.Clamp(depth, 0f, 2f) * floatStrength / floaters.Length;
                    _rb.AddForceAtPosition(Vector3.up * force * _rb.mass * Physics.gravity.magnitude / floatStrength, f.position, ForceMode.Force);
                }
            }
            bool inWater = submerged > 0;
            _rb.linearDamping = inWater ? waterDrag : airDrag;
            _rb.angularDamping = inWater ? waterAngularDrag : airAngularDrag;
        }
    }
}
