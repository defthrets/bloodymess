using System;
using GTA;
using GTA.Math;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// The pool that spreads out from under a body.
    ///
    /// HOW A POOL GROWS, given that a decal cannot be resized: each step removes the previous
    /// handle and lays a larger decal in its place. A pool therefore costs exactly ONE decal
    /// no matter how many steps it takes to reach full size, which is the entire reason it is
    /// done this way rather than by stacking a dozen overlapping splatters -- that version
    /// looked much the same and cost twelve times as much out of a pool the whole mod shares.
    ///
    /// Pools are also what the footprints are picked up from, so each step re-registers itself
    /// with the blood field at its new size. A pool that grew and did not tell the field would
    /// be a puddle you could walk through the middle of without getting anything on your feet.
    /// </summary>
    internal sealed class Pools
    {
        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly BloodField _field;

        public Pools(Settings cfg, Decals decals, BloodField field)
        {
            _cfg = cfg;
            _decals = decals;
            _field = field;
        }

        public void Update(Victims victims)
        {
            if (!_cfg.PoolsEnabled) return;

            var now = Game.GameTime;

            foreach (var victim in victims.Tracked)
            {
                Grow(victim, now);
            }
        }

        private void Grow(Victim victim, int now)
        {
            if (victim.PoolFinished) return;
            if (victim.DownAt == 0) return;

            // The player does not pool. They are not a corpse -- they get up, and a pool that
            // followed them around the map afterwards would be absurd.
            if (victim.IsPlayer) return;

            var ped = victim.Ped;
            if (ped == null || !ped.Exists()) return;

            if (!_cfg.PoolsFromWounded && !SafeIsDead(ped)) return;

            var since = now - victim.DownAt;
            if (since < _cfg.PoolDelay * 1000f) return;

            // How far through the growth this pool should be by now.
            var elapsed = (since - _cfg.PoolDelay * 1000f) / 1000f;
            var progress = Clamp01(elapsed / _cfg.PoolGrowSeconds);

            var wantedStep = (int)Math.Ceiling(progress * _cfg.PoolSteps);
            if (wantedStep < 1) wantedStep = 1;

            if (wantedStep <= victim.PoolStep) return;

            // Only ever one step per pass. Catching up several steps at once would spend
            // several decals in a frame for a pool that ends up looking identical, and the
            // budget's rate limit exists precisely to stop that.
            victim.PoolStep = victim.PoolStep + 1;

            var fraction = (float)victim.PoolStep / _cfg.PoolSteps;

            // NOT MULTIPLIED BY THE GORE LEVEL. MaxSize is a measurement in metres and it is
            // meant to be believable; scaling it by the level as well took a 1.7m pool to over
            // three metres at Mess, which is wider than the road. The level drives how much
            // blood there is elsewhere -- it has no business resizing a body's own pool.
            var size = _cfg.PoolStartSize +
                       (_cfg.PoolMaxSize - _cfg.PoolStartSize) * fraction;

            Place(victim, ped, size);

            if (victim.PoolStep >= _cfg.PoolSteps) victim.PoolFinished = true;
        }

        /// <summary>
        /// Puts the pool down at its new size, taking the old one away first.
        ///
        /// The position is settled on the FIRST step and then reused, so a body that is nudged
        /// by traffic or falls down a step afterwards does not drag its pool along behind it.
        /// </summary>
        private void Place(Victim victim, Ped ped, float size)
        {
            if (victim.PoolStep <= 1 || victim.PoolAt == Vector3.Zero)
            {
                victim.PoolAt = ped.Position;
            }

            var ground = Ground.Probe(victim.PoolAt, 3f, ped);

            if (!ground.Found)
            {
                // Nothing underneath -- in water, or off the map. Stop trying rather than
                // probing this body every frame for the rest of the session.
                victim.PoolFinished = true;
                return;
            }

            // THE POOL IS THE GAME'S UNLESS ASKED OTHERWISE. We still mark the ground wet, so
            // the footprints have something to pick up -- that is the only thing this system
            // owes the rest of the mod, and it costs no decals whatsoever. See
            // Settings.PoolsDrawOurOwn for why ours is not drawn by default.
            if (!_cfg.PoolsDrawOurOwn)
            {
                _field.Add(ground.Position, size * 0.5f, _cfg.FootprintWetSeconds);
                return;
            }

            if (!_decals.CanAfford(ground.Position))
            {
                // Out of budget or out of range. Step back so the growth resumes rather than
                // silently skipping a size when the player walks back over.
                victim.PoolStep--;
                return;
            }

            // Porous ground has one variant anyway; hard ground gets the single-variant id
            // unless the player has asked for the varied blood textures and accepted that
            // pools will not match each other.
            var type = ground.Porous
                ? DecalType.PorousPoolBlood
                : _cfg.PoolVariedTextures ? DecalType.SolidPoolBlood : DecalType.SolidPoolPlain;

            var handle = _decals.OnGround(
                Decals.Lane.Pool, type, ground.Position,
                ped.Heading * (float)(Math.PI / 180.0),
                size, size * 0.85f,
                // A porous surface soaks it up, so it never looks as wet as tarmac does.
                ground.Porous ? 0.75f : 0.95f,
                // TINTED AFTER ALL. Reading the game's own decals.dat settles it: id 9001 has
                // FOUR texture variants and the first is `pool_solid`, a colourless generic
                // pool shared with water and oil -- so an untinted pool comes out WHITE
                // whenever the engine picks that one. 9006 is `pool_porous_solid`, also
                // colourless. Tinting costs nothing on the variants that are already red
                // (it darkens them slightly, which reads as older blood) and is the only
                // thing that makes the grey ones look like blood at all.
                new Tint(_cfg.PoolRed, _cfg.PoolGreen, _cfg.PoolBlue));

            if (handle == 0)
            {
                victim.PoolStep--;
                return;
            }

            if (victim.PoolHandle != 0) _decals.Remove(victim.PoolHandle);
            victim.PoolHandle = handle;

            // Half the width, because a pool decal is drawn centred and the radius is what the
            // foot test wants. Re-registered at every step so the wet area keeps up with it.
            _field.Add(ground.Position, size * 0.5f, _cfg.FootprintWetSeconds);
        }

        private static bool SafeIsDead(Ped ped)
        {
            try { return ped.IsDead; }
            catch { return false; }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
