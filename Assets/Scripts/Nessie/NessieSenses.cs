using System.Collections.Generic;
using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Nessie
{
    /// <summary>
    /// Nessie's perception and short-term memory: stimuli (sonar pings, engine noise, lights, splashes),
    /// remembered player positions and known equipment. Decays according to the profile's MemorySeconds.
    /// Server only.
    /// </summary>
    public class NessieSenses
    {
        public enum StimulusKind { SonarPing, Noise, Light, Flash, Splash, CameraOnMe }

        public struct Stimulus
        {
            public StimulusKind Kind;
            public Vector3 Position;
            public float Strength;   // 0..1+ (flash >1)
            public float Time;
        }

        private readonly NessieAI _ai;
        private readonly List<Stimulus> _stimuli = new List<Stimulus>(64);
        private readonly List<INessieTarget> _targets = new List<INessieTarget>();
        private readonly Dictionary<ulong, (Vector3 pos, float time)> _playerMemory = new Dictionary<ulong, (Vector3, float)>();

        public IReadOnlyList<Stimulus> Stimuli => _stimuli;
        public IReadOnlyList<INessieTarget> KnownTargets => _targets;
        public float LastCameraOnMeTime { get; private set; } = -999f;
        public float LastFlashTime { get; private set; } = -999f;

        public NessieSenses(NessieAI ai) => _ai = ai;

        private NessieDifficultyProfile P => _ai.Profile;

        public void Push(StimulusKind kind, Vector3 pos, float strength)
        {
            _stimuli.Add(new Stimulus { Kind = kind, Position = pos, Strength = strength, Time = Time.time });
            if (kind == StimulusKind.CameraOnMe) LastCameraOnMeTime = Time.time;
            if (kind == StimulusKind.Flash) LastFlashTime = Time.time;
            if (_stimuli.Count > 64) _stimuli.RemoveAt(0);
        }

        public void RegisterTarget(INessieTarget t) { if (!_targets.Contains(t)) _targets.Add(t); }
        public void UnregisterTarget(INessieTarget t) => _targets.Remove(t);

        public void Tick()
        {
            float memory = P != null ? P.MemorySeconds : 20f;
            _stimuli.RemoveAll(s => Time.time - s.Time > memory);
            _targets.RemoveAll(t => t == null || (t as Object) == null);

            // Remember where players are when Nessie can perceive them.
            var gm = GameManager.Instance;
            if (gm == null) return;
            foreach (var p in gm.Players.Values)
            {
                if (p == null || p.Vitals == null || !p.Vitals.IsAlive) continue;
                float d = Vector3.Distance(p.transform.position, _ai.transform.position);
                bool nessieSurfaced = _ai.Body.IsNearSurface;
                float range = nessieSurfaced ? P.SightRangeSurface : P.SightRangeUnderwater;
                if (p.FlashlightOn.Value) range *= 1.6f;
                if (d <= range || p.Vitals.InWater.Value)
                    _playerMemory[p.OwnerClientId] = (p.transform.position, Time.time);
            }
            var stale = new List<ulong>();
            foreach (var kv in _playerMemory) if (Time.time - kv.Value.time > memory) stale.Add(kv.Key);
            foreach (var k in stale) _playerMemory.Remove(k);
        }

        /// <summary>Most interesting recent stimulus, weighted by curiosity and recency. Null if none.</summary>
        public Stimulus? MostInteresting()
        {
            Stimulus? best = null; float bestScore = 0f;
            float memory = P.MemorySeconds;
            foreach (var s in _stimuli)
            {
                if (s.Kind == StimulusKind.Flash || s.Kind == StimulusKind.CameraOnMe) continue;
                float recency = 1f - Mathf.Clamp01((Time.time - s.Time) / memory);
                float weight = s.Kind switch
                {
                    StimulusKind.SonarPing => P.Curiosity * (1f - P.Stealth * 0.7f),   // stealthy Nessie avoids pings
                    StimulusKind.Noise => 0.6f,
                    StimulusKind.Light => 0.4f * (1f - P.Stealth),
                    StimulusKind.Splash => 1.2f,                                      // someone in the water!
                    _ => 0.3f
                };
                float score = s.Strength * weight * recency;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return bestScore > 0.15f ? best : null;
        }

        /// <summary>Sonar pings a stealthy Nessie wants to stay away from.</summary>
        public bool IsInsideActiveSonar(Vector3 pos, out Vector3 pingPos)
        {
            pingPos = Vector3.zero;
            foreach (var s in _stimuli)
            {
                if (s.Kind != StimulusKind.SonarPing || Time.time - s.Time > 6f) continue;
                float range = s.Strength; // sonar pushes its range as strength
                if (Vector3.Distance(pos, s.Position) < range) { pingPos = s.Position; return true; }
            }
            return false;
        }

        /// <summary>Pick the best equipment to sabotage: valuable (cunning) or nearest.</summary>
        public INessieTarget ChooseSabotageTarget()
        {
            INessieTarget best = null; float bestScore = float.MinValue;
            bool preferValue = Random.value < P.TargetValuableGear;
            foreach (var t in _targets)
            {
                if (t == null || !t.CanBeSabotaged) continue;
                float d = Vector3.Distance(t.transform.position, _ai.transform.position);
                float score = preferValue ? t.SabotageValue * 100f - d * 0.1f : -d;
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        /// <summary>Pick a player to strike: isolated players near the rail score highest.</summary>
        public PlayerCharacter ChooseDeckTarget(Vector3 from, float maxRange)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Vessel == null) return null;
            PlayerCharacter best = null; float bestScore = float.MinValue;
            var alive = new List<PlayerCharacter>(gm.AlivePlayers());
            foreach (var p in alive)
            {
                if (p.IsSeated || p.Vitals.InWater.Value) continue;
                float d = Vector3.Distance(p.transform.position, from);
                if (d > maxRange) continue;
                float railDist = gm.Vessel.DistanceToRail(p.transform.position);
                float isolation = 1f;
                foreach (var o in alive) if (o != p && Vector3.Distance(o.transform.position, p.transform.position) < 6f) isolation -= 0.3f;
                float score = -d * 0.2f - railDist * 2f + Mathf.Max(0f, isolation) * P.IsolationPreference * 10f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        public bool TryGetRememberedPlayer(ulong id, out Vector3 pos)
        {
            if (_playerMemory.TryGetValue(id, out var m)) { pos = m.pos; return true; }
            pos = default; return false;
        }

        public bool CameraOnMeRecently(float seconds) => Time.time - LastCameraOnMeTime < seconds;
        public bool FlashedRecently(float seconds) => Time.time - LastFlashTime < seconds;
    }
}
