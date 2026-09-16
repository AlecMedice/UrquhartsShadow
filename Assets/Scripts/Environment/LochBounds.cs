using UnityEngine;
using UrquhartsShadow.Config;

namespace UrquhartsShadow.Environment
{
    /// <summary>
    /// Keeps the vessel and Nessie inside the playable loch. Also exposes the "deep trench" nodes that
    /// the eDNA sampler and Nessie's preferred lurking spots use. Place trench markers as children.
    /// </summary>
    public class LochBounds : MonoBehaviour
    {
        public static LochBounds Instance { get; private set; }

        [SerializeField] private Transform[] trenchNodes;
        [SerializeField] private float lochFloorDepth = 220f;
        [SerializeField] private Transform dockPosition;

        public float Radius => GameConfig.Instance.Vessel.LochBoundsRadius;
        public Vector3 Center => transform.position;
        public float FloorDepth => lochFloorDepth;
        public Transform Dock => dockPosition;
        public Transform[] TrenchNodes => trenchNodes;

        private void Awake() => Instance = this;

        public Vector3 Clamp(Vector3 p)
        {
            var flat = new Vector3(p.x - Center.x, 0f, p.z - Center.z);
            if (flat.magnitude > Radius) flat = flat.normalized * Radius;
            return new Vector3(Center.x + flat.x, p.y, Center.z + flat.z);
        }

        public bool Inside(Vector3 p, float margin = 0f)
        {
            var flat = new Vector3(p.x - Center.x, 0f, p.z - Center.z);
            return flat.magnitude <= Radius - margin;
        }

        public Transform NearestTrench(Vector3 p)
        {
            Transform best = null; float bestD = float.MaxValue;
            if (trenchNodes == null) return null;
            foreach (var t in trenchNodes)
            {
                if (t == null) continue;
                float d = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(t.position.x, 0, t.position.z));
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        public Vector3 RandomPointInLoch(float minRadiusFrom, Vector3 from, float minDist, float maxDist)
        {
            for (int i = 0; i < 12; i++)
            {
                var dir = Random.insideUnitCircle.normalized;
                float d = Random.Range(minDist, maxDist);
                var p = from + new Vector3(dir.x, 0f, dir.y) * d;
                if (Inside(p, 30f)) return p;
            }
            return Clamp(from + Random.insideUnitSphere * minDist);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, Radius);
            if (trenchNodes != null)
                foreach (var t in trenchNodes) if (t != null) Gizmos.DrawWireSphere(t.position, 40f);
        }
    }
}
