using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// Blood carried out of a puddle on the soles of somebody's shoes.
    ///
    /// The whole feature is two ideas. A foot that comes down inside wet blood picks some up,
    /// which is a number on the ped -- how many prints they have left in them. Every stride
    /// after that spends one of those prints and lays a decal where the foot went, fading as
    /// the number runs down, until they have walked it all off. That is genuinely how it works
    /// on a real floor, and it is why a trail thins out and stops instead of ending abruptly.
    ///
    /// PRINTS ARE PACED BY DISTANCE, NOT BY GAIT. It is tempting to watch the foot bones and
    /// stamp a print at the bottom of each step, and it does not work: the bones keep moving
    /// while a ped is standing still, shuffling, turning on the spot or being shoved, so the
    /// same square metre of floor ends up with thirty prints on it. Measuring travel instead
    /// means a print costs a stride of actual ground covered, which is both correct and
    /// self-limiting. The foot BONE is still used -- for where the print goes, so that left and
    /// right prints fall either side of the line of travel rather than down the middle of it.
    /// </summary>
    internal sealed class Footprints
    {
        /// <summary>How wet a patch has to be under a foot before any of it comes with them.</summary>
        private const float PickupThreshold = 0.15f;

        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly BloodField _field;
        private readonly Random _random = new Random();

        /// <summary>Whether the game's own foot effect is currently overridden by us.</summary>
        private bool _footEffectOverridden;

        public Footprints(Settings cfg, Decals decals, BloodField field)
        {
            _cfg = cfg;
            _decals = decals;
            _field = field;
        }

        /// <summary>Prints laid this session. Shown in the settings menu, and useful in a log.</summary>
        public int Printed { get; private set; }

        public void Update(Victims victims)
        {
            if (!_cfg.FootprintsEnabled)
            {
                ClearFootEffect();
                return;
            }

            var npcs = 0;
            var anybodyBloody = false;

            foreach (var victim in victims.Tracked)
            {
                if (victim.IsPlayer)
                {
                    if (!_cfg.FootprintsForPlayer) continue;
                }
                else
                {
                    if (!_cfg.FootprintsForNpcs) continue;
                    if (npcs >= _cfg.FootprintMaxNpcs) continue;
                    npcs++;
                }

                Walk(victim);

                if (victim.IsPlayer && victim.PrintsLeft > 0) anybodyBloody = true;
            }

            FootEffect(anybodyBloody);
        }

        private void Walk(Victim victim)
        {
            var ped = victim.Ped;

            if (ped == null || !ped.Exists()) return;

            try
            {
                // Nothing to print from inside a car, and nothing to print while face down.
                // A ragdolling ped's foot bones are wherever the physics put them, which is
                // usually somewhere that would stamp a print through a wall.
                if (ped.IsInVehicle() || ped.IsRagdoll || ped.IsDead) return;
            }
            catch
            {
                return;
            }

            var left = BonePosition(ped, Bone.SkelLeftFoot);
            var right = BonePosition(ped, Bone.SkelRightFoot);

            PickUp(victim, left, right);

            if (victim.PrintsLeft <= 0) return;

            var position = ped.Position;
            var travelled = position.DistanceTo2D(victim.LastPrintAt);

            if (travelled < _cfg.FootprintStride) return;

            // A ped that has been teleported, or one whose print position was never set, would
            // otherwise stamp a print in the middle of nowhere on the first frame after.
            if (travelled > 12f)
            {
                victim.LastPrintAt = position;
                return;
            }

            victim.LastPrintAt = position;

            var foot = victim.NextFootIsLeft ? left : right;
            victim.NextFootIsLeft = !victim.NextFootIsLeft;

            Stamp(victim, ped, foot);
        }

        /// <summary>
        /// Notices a foot landing in something wet.
        ///
        /// Both feet are checked, and the result TOPS UP rather than adds: walking back and
        /// forth through the same pool keeps somebody's feet fully bloody, which is right, but
        /// it cannot stack into a trail that goes on for half the map.
        /// </summary>
        private void PickUp(Victim victim, Vector3 left, Vector3 right)
        {
            var wetness = Math.Max(
                _field.WetnessAt(left, _cfg.FootprintPickupRadius),
                _field.WetnessAt(right, _cfg.FootprintPickupRadius));

            if (wetness < PickupThreshold) return;

            var picked = (int)Math.Round(_cfg.FootprintSteps * _cfg.Scale * wetness);
            if (picked <= 0) return;

            if (picked <= victim.PrintsLeft) return;

            victim.PrintsLeft = picked;
            victim.PrintsAtPickup = picked;
        }

        /// <summary>Lays one print under a foot, and spends it.</summary>
        private void Stamp(Victim victim, Ped ped, Vector3 foot)
        {
            var ground = Ground.Probe(foot, 1.5f, ped);
            if (!ground.Found) return;

            if (!_decals.CanAfford(ground.Position)) return;

            // Fades as the blood wears off. The floor keeps a little of it right to the end --
            // a print that reaches zero opacity is a decal spent on nothing at all.
            var remaining = (float)victim.PrintsLeft / Math.Max(1, victim.PrintsAtPickup);
            var opacity = _cfg.FootprintOpacity * (0.2f + remaining * 0.8f);

            // Points the way they are walking. A print laid at a fixed angle looks like a
            // sticker, and it is the one thing people notice immediately.
            var heading = ped.Heading * (float)(Math.PI / 180.0);

            // A degree or two of slop, because a real footfall is never exactly on the line of
            // travel and a perfectly straight trail of identical prints looks printed.
            heading += (float)(_random.NextDouble() - 0.5) * 0.25f;

            var handle = _decals.OnGround(
                Decals.Lane.Splatter, DecalType.SplattersBlood, ground.Position, heading,
                _cfg.FootprintWidth, _cfg.FootprintLength, opacity,
                new Tint(_cfg.BloodRed, _cfg.BloodGreen, _cfg.BloodBlue));

            if (handle == 0) return;

            victim.PrintsLeft--;
            Printed++;
        }

        private static Vector3 BonePosition(Ped ped, Bone bone)
        {
            try
            {
                var b = ped.Bones[bone];
                if (b != null && b.IsValid) return b.Position;
            }
            catch
            {
                // Fall through to the ped's own position.
            }

            return ped.Position;
        }

        /// <summary>
        /// Points the game's own footstep effect at its blood decal, or puts it back.
        ///
        /// This is a GLOBAL OVERRIDE on a shared system -- it is the same setting that makes
        /// wet feet leave prints after swimming, and it applies to everyone, not just to the
        /// ped we set it for. That is why it is off by default and why it is cleared the moment
        /// the player's feet are clean again: a mod that quietly keeps hold of a global is a
        /// mod that other mods have to fight.
        /// </summary>
        private void FootEffect(bool wanted)
        {
            if (!_cfg.FootprintGameEffect) { ClearFootEffect(); return; }
            if (wanted == _footEffectOverridden) return;

            try
            {
                Function.Call(Hash.SET_PARTICLE_FX_FOOT_OVERRIDE_NAME,
                              wanted ? "ped_foot_decal_blood" : "");
                _footEffectOverridden = wanted;
            }
            catch (Exception ex)
            {
                Log.Once("foot-override", "Foot effect override failed: " + ex.Message);
            }
        }

        private void ClearFootEffect()
        {
            if (!_footEffectOverridden) return;

            try { Function.Call(Hash.SET_PARTICLE_FX_FOOT_OVERRIDE_NAME, ""); }
            catch { /* nothing to be done */ }

            _footEffectOverridden = false;
        }

        /// <summary>Hands the foot effect back on shutdown, reload included.</summary>
        public void Shutdown()
        {
            ClearFootEffect();
        }
    }
}
