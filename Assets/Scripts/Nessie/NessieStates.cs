using UnityEngine;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Player;

namespace UrquhartsShadow.Nessie
{
    public enum NessieStateId { Dormant, Lurk, Stalk, Investigate, Sabotage, Breach, DeckStrike, Ram, Retreat, Finale }

    /// <summary>Base class for Nessie behaviours. All run on the server only.</summary>
    public abstract class NessieState
    {
        protected NessieAI AI;
        protected NessieBody Body => AI.Body;
        protected NessieSenses Senses => AI.Senses;
        protected NessieDifficultyProfile P => AI.Profile;
        protected float Elapsed => Time.time - EnterTime;
        protected float EnterTime;
        public abstract NessieStateId Id { get; }

        public void Init(NessieAI ai) => AI = ai;
        public virtual void Enter() => EnterTime = Time.time;
        public virtual void Exit() { }
        public abstract void Tick(float dt);

        protected Vector3 BoatPos => AI.VesselPosition;
        protected float DistToBoat => Body.FlatDistanceTo(BoatPos);
    }

    /// <summary>Before the grace period, after victory, or between nights: deep and far away.</summary>
    public class DormantState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Dormant;
        public override void Tick(float dt)
        {
            Body.Hover(P.PreferredDepth + 40f, dt);
        }
    }

    /// <summary>Default idle: drift between trenches at depth, occasionally call. Decides what to do next.</summary>
    public class LurkState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Lurk;
        private Vector3 _wander;
        private float _nextCall;

        public override void Enter()
        {
            base.Enter();
            PickWander();
            _nextCall = Time.time + Random.Range(GameConfig.Instance.Audio.NessieCallMinInterval, GameConfig.Instance.Audio.NessieCallMaxInterval);
        }

        private void PickWander()
        {
            var lb = LochBounds.Instance;
            _wander = lb != null ? lb.RandomPointInLoch(0f, BoatPos, 120f, 400f) : BoatPos + Random.insideUnitSphere * 200f;
        }

        public override void Tick(float dt)
        {
            Body.SteerTowards(_wander, P.PreferredDepth, P.CruiseSpeed, dt);
            if (Body.FlatDistanceTo(_wander) < 15f) PickWander();
            if (Time.time > _nextCall)
            {
                Body.CallServer();
                AI.EmitHydrophoneCall();
                _nextCall = Time.time + Random.Range(GameConfig.Instance.Audio.NessieCallMinInterval, GameConfig.Instance.Audio.NessieCallMaxInterval);
            }
            if (Elapsed > P.ReactionSeconds) AI.Decide();
        }
    }

    /// <summary>Circle the vessel at depth, watching for an opening. Escalates into strikes/rams/breaches.</summary>
    public class StalkState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Stalk;
        private float _angle;
        private float _radius;
        private float _nextDecision;

        public override void Enter()
        {
            base.Enter();
            _radius = Random.Range(25f, 60f);
            _angle = Random.value * 360f;
            _nextDecision = Time.time + P.ReactionSeconds * 2f;
        }

        public override void Tick(float dt)
        {
            _angle += (P.CruiseSpeed / _radius) * Mathf.Rad2Deg * dt * 0.6f;
            Vector3 offset = new Vector3(Mathf.Cos(_angle * Mathf.Deg2Rad), 0f, Mathf.Sin(_angle * Mathf.Deg2Rad)) * _radius;
            float depth = Mathf.Lerp(P.PreferredDepth * 0.5f, 8f, AI.EffectiveAggression);
            Body.SteerTowards(BoatPos + offset, depth, P.CruiseSpeed, dt);

            if (Time.time >= _nextDecision)
            {
                _nextDecision = Time.time + P.ReactionSeconds * 2f;
                float roll = Random.value;
                if (roll < AI.EffectiveAggression * P.DeckStrikeChance && AI.CanDeckStrike) { AI.ChangeState(NessieStateId.DeckStrike); return; }
                if (roll < AI.EffectiveAggression * 0.6f && AI.CanRam) { AI.ChangeState(NessieStateId.Ram); return; }
                if (roll < AI.EffectiveAggression * 0.6f + P.SabotageDrive * 0.4f && Senses.ChooseSabotageTarget() != null) { AI.ChangeState(NessieStateId.Sabotage); return; }
                if (Elapsed > 60f) { AI.ChangeState(NessieStateId.Lurk); return; }
            }
        }
    }

    /// <summary>Go and look at a stimulus (a sonar ping, a splash, an engine). Bites whatever is there.</summary>
    public class InvestigateState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Investigate;
        public Vector3 Target;
        public NessieSenses.StimulusKind Kind;

        public override void Tick(float dt)
        {
            float depth = Kind == NessieSenses.StimulusKind.Splash ? 3f : P.PreferredDepth * 0.4f;
            Body.SteerTowards(Target, depth, P.HuntSpeed, dt);

            if (Kind == NessieSenses.StimulusKind.Splash)
            {
                // Someone is in the water. Cruel but fair.
                var victim = AI.NearestSwimmer(Body.transform.position, 12f);
                if (victim != null)
                {
                    Body.TriggerAnimServer("Bite");
                    victim.Vitals.KillServer("was taken beneath the waves");
                    AI.ChangeState(NessieStateId.Retreat, 15f);
                    return;
                }
            }

            if (Body.FlatDistanceTo(Target) < 10f || Elapsed > 45f)
            {
                var t = Senses.ChooseSabotageTarget();
                if (t != null && Vector3.Distance(t.transform.position, Target) < 30f) AI.ChangeState(NessieStateId.Sabotage);
                else AI.ChangeState(DistToBoat < 80f ? NessieStateId.Stalk : NessieStateId.Lurk);
            }
        }
    }

    /// <summary>Grab a beacon / the ROV and drag it to the bottom, or gnaw its tether until it is lost.</summary>
    public class SabotageState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Sabotage;
        private INessieTarget _target;
        private bool _holding;
        private Vector3 _dragTo;

        public override void Enter()
        {
            base.Enter();
            _target = Senses.ChooseSabotageTarget();
            _holding = false;
            if (_target == null) { AI.ChangeState(NessieStateId.Lurk); return; }
            var lb = LochBounds.Instance;
            var trench = lb != null ? lb.NearestTrench(_target.transform.position) : null;
            _dragTo = trench != null ? trench.position : _target.transform.position + Random.insideUnitSphere * 80f;
        }

        public override void Exit()
        {
            if (_holding && _target != null) _target.OnSabotageReleased(AI);
            _holding = false;
        }

        public override void Tick(float dt)
        {
            if (_target == null || (_target as Object) == null || !_target.CanBeSabotaged) { AI.ChangeState(NessieStateId.Lurk); return; }

            if (!_holding)
            {
                float targetDepth = OceanSurface.Instance != null ? OceanSurface.Instance.DepthAt(_target.transform.position) : P.MinDepth;
                Body.SteerTowards(_target.transform.position, Mathf.Max(P.MinDepth, targetDepth), P.HuntSpeed, dt);
                if (Body.DistanceTo(_target.transform.position) < 6f)
                {
                    _holding = true;
                    _target.OnSabotageBegin(AI);
                    Body.TriggerAnimServer("Bite");
                }
                if (Elapsed > 60f) AI.ChangeState(NessieStateId.Lurk);
                return;
            }

            // Drag toward the trench while the device counts down.
            Body.SteerTowards(_dragTo, P.PreferredDepth + 30f, P.BeaconDragSpeed, dt);
            if (_target.OnSabotageTick(AI, dt))
            {
                _holding = false;
                Senses.UnregisterTarget(_target);
                AI.ChangeState(Random.value < AI.EffectiveAggression ? NessieStateId.Stalk : NessieStateId.Lurk);
            }
        }
    }

    /// <summary>
    /// The evidence opportunity: rise, expose the head and back for ~2 seconds, dive.
    /// A camera-aware Nessie dives early if she notices a lens on her.
    /// </summary>
    public class BreachState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Breach;
        private Vector3 _spot;
        private float _surfaceAt;
        private float _diveAt;
        private bool _surfaced;

        public override void Enter()
        {
            base.Enter();
            var lb = LochBounds.Instance;
            _spot = lb != null ? lb.RandomPointInLoch(0f, BoatPos, P.BreachDistanceMin, P.BreachDistanceMax)
                               : BoatPos + Random.insideUnitSphere.normalized * P.BreachDistanceMin;
            _surfaced = false;
            _surfaceAt = 0f;
        }

        public override void Exit()
        {
            if (_surfaced) Body.SetSurfacedServer(false);
            _surfaced = false;
        }

        public override void Tick(float dt)
        {
            if (!_surfaced)
            {
                Body.SteerTowards(_spot, 6f, P.HuntSpeed, dt);
                if (Body.FlatDistanceTo(_spot) < 6f)
                {
                    _surfaced = true;
                    _surfaceAt = Time.time;
                    float window = GameConfig.Instance.Evidence.BreachWindowSeconds * P.BreachDurationScale;
                    _diveAt = _surfaceAt + window;
                    Body.SetSurfacedServer(true);
                    AI.RegisterBreach();
                }
                else if (Elapsed > 40f) AI.ChangeState(NessieStateId.Lurk);
                return;
            }

            Body.SteerTowards(_spot + Body.transform.forward * 4f, 0.2f, 1.5f, dt);

            // Camera awareness: dive early if watched (scaled by difficulty).
            if (Senses.CameraOnMeRecently(0.5f) && Random.value < P.CameraAwareness * dt * 4f)
            {
                AI.ChangeState(NessieStateId.Retreat, P.RetreatSecondsAfterCapture * 0.5f);
                return;
            }
            if (Time.time >= _diveAt)
                AI.ChangeState(Random.value < AI.EffectiveAggression ? NessieStateId.Stalk : NessieStateId.Lurk);
        }
    }

    /// <summary>Surge up beside the rail and sweep a player off the deck.</summary>
    public class DeckStrikeState : NessieState
    {
        public override NessieStateId Id => NessieStateId.DeckStrike;
        private PlayerCharacter _victim;
        private bool _struck;

        public override void Enter()
        {
            base.Enter();
            _victim = Senses.ChooseDeckTarget(BoatPos, 60f);
            _struck = false;
            if (_victim == null) { AI.ChangeState(NessieStateId.Stalk); return; }
        }

        public override void Exit() { if (_struck) Body.SetSurfacedServer(false); }

        public override void Tick(float dt)
        {
            if (_victim == null || !_victim.Vitals.IsAlive) { AI.ChangeState(NessieStateId.Stalk); return; }
            Vector3 rail = AI.Vessel != null ? AI.Vessel.NearestRailPoint(_victim.transform.position) : _victim.transform.position;
            Vector3 approach = rail + (rail - BoatPos).normalized * 4f;

            if (!_struck)
            {
                Body.SteerTowards(approach, 2f, P.BurstSpeed, dt);
                if (Body.FlatDistanceTo(approach) < 5f)
                {
                    _struck = true;
                    Body.SetSurfacedServer(true);
                    Body.RoarServer();
                    Body.TriggerAnimServer("Bite");
                    AI.MarkDeckStrike();
                    if (Vector3.Distance(_victim.transform.position, rail) < P.DeckStrikeRange)
                    {
                        Vector3 dir = (_victim.transform.position - BoatPos); dir.y = 0f; dir.Normalize();
                        float force = GameConfig.Instance.Player.KnockbackForce;
                        _victim.Vitals.KnockbackServer(dir * force + Vector3.up * force * 0.5f);
                        _victim.Vitals.DamageServer(20f, "was struck by the creature");
                        // Anyone else close by gets shoved too.
                        foreach (var p in GameManager.Instance.AlivePlayers())
                            if (p != _victim && Vector3.Distance(p.transform.position, rail) < 4f)
                                p.Vitals.KnockbackServer(dir * force * 0.6f + Vector3.up * 2f);
                    }
                }
                else if (Elapsed > 25f) AI.ChangeState(NessieStateId.Stalk);
            }
            else if (Elapsed > 0f && Time.time - EnterTime > 0f)
            {
                Body.Hover(0.5f, dt);
                if (Senses.FlashedRecently(1f)) { AI.ChangeState(NessieStateId.Retreat, P.RetreatSecondsAfterFlash); return; }
                // Stay up for a beat so the team can film her (risk / reward), then dive.
                if (Body.Surfaced.Value && Time.time - EnterTime > 3.5f) AI.ChangeState(NessieStateId.Retreat, 10f);
            }
        }
    }

    /// <summary>Charge the hull from below. Damage scales with profile.</summary>
    public class RamState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Ram;
        private Vector3 _windup;
        private bool _charging;

        public override void Enter()
        {
            base.Enter();
            Vector3 side = Random.value < 0.5f ? AI.Vessel.transform.right : -AI.Vessel.transform.right;
            _windup = BoatPos + side * 45f;
            _charging = false;
        }

        public override void Tick(float dt)
        {
            if (!_charging)
            {
                Body.SteerTowards(_windup, 12f, P.HuntSpeed, dt);
                if (Body.FlatDistanceTo(_windup) < 6f) _charging = true;
                if (Elapsed > 30f) AI.ChangeState(NessieStateId.Stalk);
                return;
            }
            Body.SteerTowards(BoatPos, 3f, P.BurstSpeed, dt);
            if (DistToBoat < Body.BodyLength * 0.5f + 3f)
            {
                float dmg = GameConfig.Instance.Vessel.RamDamage * P.RamDamageMultiplier;
                AI.Vessel.ApplyRamDamageServer(dmg, Body.transform.position, Body.Velocity);
                Body.TriggerAnimServer("Ram");
                AI.MarkRam();
                AI.ChangeState(NessieStateId.Retreat, 8f);
            }
        }
    }

    /// <summary>Dive deep and away for a while (after being photographed, flashed, or after an attack).</summary>
    public class RetreatState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Retreat;
        public float Duration = 20f;
        private Vector3 _away;

        public override void Enter()
        {
            base.Enter();
            Vector3 dir = (Body.transform.position - BoatPos); dir.y = 0f;
            if (dir.sqrMagnitude < 1f) dir = Random.insideUnitSphere; dir.y = 0f;
            _away = Body.transform.position + dir.normalized * 200f;
            if (LochBounds.Instance != null) _away = LochBounds.Instance.Clamp(_away);
        }

        public override void Tick(float dt)
        {
            Body.SteerTowards(_away, P.PreferredDepth + 30f, P.BurstSpeed, dt);
            if (Elapsed > Duration) AI.ChangeState(NessieStateId.Lurk);
        }
    }

    /// <summary>Night 5 has ended without the evidence. She comes for the hull. Nothing stops her now.</summary>
    public class FinaleState : NessieState
    {
        public override NessieStateId Id => NessieStateId.Finale;
        private float _nextHit;
        private float _angle;

        public override void Enter()
        {
            base.Enter();
            _nextHit = Time.time + 4f;
            Body.RoarServer();
        }

        public override void Tick(float dt)
        {
            // Tight circle around the boat at the surface, hitting the hull repeatedly.
            _angle += 60f * dt;
            Vector3 offset = new Vector3(Mathf.Cos(_angle * Mathf.Deg2Rad), 0f, Mathf.Sin(_angle * Mathf.Deg2Rad)) * 18f;
            Body.SteerTowards(BoatPos + offset, 1f, P.HuntSpeed, dt);
            if (!Body.Surfaced.Value && Elapsed > 2f) Body.SetSurfacedServer(true);
            if (Time.time >= _nextHit)
            {
                _nextHit = Time.time + Random.Range(3f, 6f);
                AI.Vessel.ApplyRamDamageServer(GameConfig.Instance.Vessel.MaxHullIntegrity / 4f, Body.transform.position, Body.Velocity);
                Body.TriggerAnimServer("Ram");
                if (Random.value < 0.5f) Body.RoarServer();
            }
        }
    }
}
