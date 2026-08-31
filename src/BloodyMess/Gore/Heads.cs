using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// Heads coming off.
    ///
    /// WHAT THIS CAN AND CANNOT BE, stated plainly because the gap matters. GTA V has exactly
    /// one native for this -- EXPLODE_PED_HEAD -- and it removes the head, leaving a bloody
    /// stump and a burst of gore. That is the whole of the engine's support.
    ///
    /// There is NO dismemberment in GTA V. No limb severing, no natives for it, nothing to
    /// call: that arrived in RDR2 and was never back-ported. And there is no severed-head prop
    /// in the game files, so a head that falls off and rolls away cannot be done without
    /// shipping a custom model, which this mod deliberately does not do -- it is a pure script
    /// with no asset dependencies, and that is why one build runs on both editions.
    ///
    /// So: heads pop, and nothing else comes off. Within that, the interesting part is WHEN,
    /// which is why the chance is per weapon group and lives in gore.json rather than being a
    /// coin flip in here. A shotgun at close range should do it nearly every time; a pistol
    /// should be a rare, memorable shot.
    /// </summary>
    internal sealed class Heads
    {
        private readonly Settings _cfg;
        private readonly Profiles _profiles;
        private readonly Random _random = new Random();

        /// <summary>The particle asset the neck burst comes from.</summary>
        private ParticleEffectAsset _core = new ParticleEffectAsset("core");

        public Heads(Settings cfg, Profiles profiles)
        {
            _cfg = cfg;
            _profiles = profiles;
        }

        /// <summary>Heads taken off this session. Shown in the settings menu.</summary>
        public int Exploded { get; private set; }

        /// <summary>
        /// Decides whether this hit takes the head off, and does it.
        ///
        /// Returns true when the head went, so the caller can skip stamping wound decals onto
        /// a head that is no longer there.
        /// </summary>
        public bool Try(Hit hit)
        {
            if (!_cfg.HeadsEnabled) return false;
            if (!hit.Headshot) return false;

            var ped = hit.Ped;
            if (ped == null || !ped.Exists()) return false;

            var victim = hit.Victim;

            // ONCE PER PED. Firing EXPLODE_PED_HEAD again on somebody who is already headless
            // re-plays the whole gore burst on a corpse, which looks like a glitch -- and in a
            // burst of automatic fire it would fire several times in a second.
            if (victim != null && victim.HeadGone) return false;

            if (victim != null && victim.IsPlayer && !_cfg.HeadsIncludePlayer) return false;

            // A graze to the head should not take it off. The threshold is in health points,
            // so it scales with however the rest of the mods on this machine tune damage.
            if (hit.Damage < _cfg.HeadsMinDamage) return false;

            var profile = _profiles.For(hit.Group);

            var chance = profile.HeadExplode * _cfg.HeadsChance;
            if (chance <= 0f) return false;
            if (_random.NextDouble() > chance) return false;

            try
            {
                // The weapon hash matters: the game picks the gore effect from it, so passing
                // the real weapon gives a shotgun blast a different burst to a pistol round.
                Function.Call(Hash.EXPLODE_PED_HEAD, ped.Handle, hit.WeaponHash);
            }
            catch (Exception ex)
            {
                Log.Once("explode-head", "EXPLODE_PED_HEAD failed: " + ex.Message);
                return false;
            }

            if (victim != null) victim.HeadGone = true;
            Exploded++;

            Neck(hit, ped);
            return true;
        }

        /// <summary>
        /// The spurt from what is left of the neck.
        ///
        /// Uses the game's own throat effect rather than the headshot one: the headshot effect
        /// is authored to come out of a head, and playing it where the head used to be leaves
        /// the burst hanging in the air above the stump.
        /// </summary>
        private void Neck(Hit hit, Ped ped)
        {
            if (!_cfg.HeadsNeckEffect) return;

            try
            {
                if (!_core.IsLoaded)
                {
                    _core.Request();
                    return;
                }

                var at = ped.Position + new Vector3(0f, 0f, 0.55f);

                try
                {
                    var bone = ped.Bones[Bone.SkelNeck1];
                    if (bone != null && bone.IsValid) at = bone.Position;
                }
                catch
                {
                    // Keep the estimate from the ped's own position.
                }

                World.CreateParticleEffectNonLooped(_core, "blood_throat", at,
                                                    Vector3.Zero, 1f, InvertAxisFlags.None);
            }
            catch (Exception ex)
            {
                Log.Once("neck-fx", "Neck effect failed: " + ex.Message);
            }
        }

        /// <summary>Lets the game unload the particle asset when the script stops.</summary>
        public void Shutdown()
        {
            try { _core.MarkAsNoLongerNeeded(); }
            catch { /* being torn down anyway */ }
        }
    }
}
