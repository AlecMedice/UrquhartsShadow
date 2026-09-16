using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Nessie;

namespace UrquhartsShadow.Tools
{
    /// <summary>
    /// Shared "is Nessie in frame and how good is the shot" logic for phones, the 35mm and the ROV.
    /// Runs on whichever client owns the camera; the result is sent to the server which re-checks
    /// range and breach state before granting evidence.
    /// </summary>
    public static class CameraEvidenceSensor
    {
        /// <summary>
        /// Returns quality 0..1 (0 = not in frame). Considers frustum, occlusion, distance, fog and
        /// whether the animal is actually exposed above water (or lit underwater for the ROV).
        /// </summary>
        public static float Evaluate(Camera cam, float fovDegrees, float maxRange, bool underwaterCamera, float lightRange = 0f)
        {
            var nessie = NessieAI.Instance;
            if (nessie == null || cam == null) return 0f;
            var body = nessie.Body;
            Vector3 target = body.Head.position;
            Vector3 toTarget = target - cam.transform.position;
            float dist = toTarget.magnitude;
            if (dist > maxRange) return 0f;

            float angle = Vector3.Angle(cam.transform.forward, toTarget);
            if (angle > fovDegrees * 0.5f) return 0f;

            if (!underwaterCamera && !body.IsBreached) return 0f;
            if (underwaterCamera && lightRange > 0f && dist > lightRange) return 0f;

            // Occlusion (hull, superstructure).
            int mask = ~LayerMask.GetMask(GameConstants.LayerNessie, GameConstants.LayerWater, GameConstants.LayerPlayer);
            if (Physics.Linecast(cam.transform.position, target, out var hit, mask, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(body.transform)) return 0f;

            // Framing: centred and close is better.
            float centre = 1f - Mathf.Clamp01(angle / (fovDegrees * 0.5f));
            float range = 1f - Mathf.Clamp01(dist / maxRange);
            float q = 0.35f + 0.35f * centre + 0.3f * range;

            var weather = GameManager.Instance?.Weather;
            if (weather != null && !underwaterCamera) q -= weather.FogPenalty(dist);
            return Mathf.Clamp01(q);
        }
    }
}
