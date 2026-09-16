using UnityEngine;

namespace UrquhartsShadow.Nessie
{
    /// <summary>
    /// Anything Nessie can sabotage: sonar beacons (dragged to the bottom), the ROV (tether gnawed),
    /// hydrophone drops, eDNA sampler lines. Implemented by DeployableDevice and ROV.
    /// </summary>
    public interface INessieTarget
    {
        Transform transform { get; }
        /// <summary>How attractive this is to a cunning Nessie (battery left, evidence potential).</summary>
        float SabotageValue { get; }
        bool CanBeSabotaged { get; }
        /// <summary>Seconds of contact needed before the device is lost.</summary>
        float SabotageSeconds { get; }
        /// <summary>Server: Nessie has hold of it. Move it with her, spike its readings.</summary>
        void OnSabotageBegin(NessieAI nessie);
        /// <summary>Server: called each tick while held; returns true when destroyed/lost.</summary>
        bool OnSabotageTick(NessieAI nessie, float dt);
        /// <summary>Server: Nessie let go (retreated, flashed).</summary>
        void OnSabotageReleased(NessieAI nessie);
    }
}
