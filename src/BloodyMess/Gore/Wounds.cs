using System;
using GTA;
using GTA.Math;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// Blood on the body itself: wounds, torn clothing, and the burst at the point of impact.
    ///
    /// This is the half of the mod that happens ON the ped, as opposed to Spray, which is the
    /// half that happens around them. It is deliberately built out of the game's own wound
    /// decals and damage packs rather than anything custom -- they already line up with the
    /// ped's UV layout, they already look like the rest of the game, and they cost nothing
    /// from the world decal pool, which is the resource everything else here is fighting over.
    /// </summary>
    internal sealed class Wounds
    {
        private readonly Settings _cfg;
        private readonly Profiles _profiles;
        private readonly Random _random = new Random();

        /// <summary>The particle asset every blood effect in the game lives in.</summary>
        private ParticleEffectAsset _core = new ParticleEffectAsset("core");

        private bool _warnedAboutAsset;

        public Wounds(Settings cfg, Profiles profiles)
        {
            _cfg = cfg;
            _profiles = profiles;
        }

        /// <summary>
        /// Asks the game to stream the particle asset in.
        ///
        /// Called every tick rather than once at startup, because assets are unloaded when the
        /// player changes area and a request that was granted an hour ago is not a request
        /// that still holds. It is a cheap call and the alternative is effects that silently
        /// stop appearing after a mission.
        /// </summary>
        public void KeepAssetLoaded()
        {
            try
            {
                if (_core.IsLoaded) return;

                _core.Request();

                if (!_warnedAboutAsset)
                {
                    Log.Debug("Requesting the 'core' particle asset.");
                    _warnedAboutAsset = true;
                }
            }
            catch (Exception ex)
            {
                Log.Once("ptfx-request", "Could not request the core particle asset: " + ex.Message);
            }
        }

        public void Apply(Hit hit)
        {
            if (!_cfg.WoundsEnabled) return;
            if (hit.Ped == null || !hit.Ped.Exists()) return;
            if (hit.Victim != null && hit.Victim.IsPlayer && !_cfg.PlayerWounds) return;

            var profile = _profiles.For(hit.Group);

            Stamp(hit, profile);
            Pack(hit, profile);
            Burst(hit, profile);
            SplashTheShooter(hit);
        }

        /// <summary>
        /// Puts extra wound decals on the body.
        ///
        /// The game already stamps one wound per hit. This adds more of them around the same
        /// zone, which is what makes a burst of fire read as a burst rather than as one neat
        /// hole -- and it is why the count is per hit rather than per second.
        /// </summary>
        private void Stamp(Hit hit, Profile profile)
        {
            var count = (int)Math.Round(_cfg.WoundsPerHit * _cfg.Scale);
            if (count <= 0) return;

            // A big hit gets the heavier wound texture. The threshold is in health points, and
            // 35 is about where a rifle round sits -- pistols stay small, rifles and shotguns
            // do not.
            var heavy = hit.Fatal || hit.Damage >= 35f;
            var wound = heavy ? profile.BigWound : profile.Wound;

            for (var i = 0; i < count; i++)
            {
                try
                {
                    // The position is in the zone's own texture space. Kept near the middle:
                    // the far corners of a zone's UV map are the seams, where a wound decal
                    // wraps somewhere it should not.
                    var u = 0.5f + (float)(_random.NextDouble() - 0.5) * 0.5f;
                    var v = 0.5f + (float)(_random.NextDouble() - 0.5) * 0.5f;

                    hit.Ped.ApplyBloodDamage(
                        hit.Zone,
                        new Vector2(u, v),
                        wound,
                        (float)(_random.NextDouble() * 360.0),
                        _cfg.WoundScale * (0.75f + (float)_random.NextDouble() * 0.5f),
                        0,
                        // Wound age. Zero is fresh and bright, which is what a hit that just
                        // landed should look like.
                        0f);
                }
                catch (Exception ex)
                {
                    Log.Once("apply-blood", "ApplyBloodDamage failed: " + ex.Message);
                    return;
                }
            }
        }

        /// <summary>
        /// Applies one of the game's pre-authored damage packs on a kill.
        ///
        /// These are the sets the story missions use -- torn clothes and matching wounds, made
        /// by hand rather than scattered. Only on a fatal hit, because a pack is a whole
        /// costume change and putting one on somebody who is still walking around looks like
        /// the mod glitched rather than like they were shot.
        /// </summary>
        private void Pack(Hit hit, Profile profile)
        {
            if (!_cfg.DamagePacks) return;
            if (!hit.Fatal) return;
            if (string.IsNullOrEmpty(profile.DamagePack)) return;

            try
            {
                Function.Call(Hash.APPLY_PED_DAMAGE_PACK, hit.Ped.Handle, profile.DamagePack,
                              // Damage and multiplier. The pack decides what it draws; these
                              // scale how much of it lands.
                              100f, _cfg.Scale);
            }
            catch (Exception ex)
            {
                Log.Once("damage-pack", "APPLY_PED_DAMAGE_PACK failed: " + ex.Message);
            }
        }

        /// <summary>The particle burst at the wound, and its twin out the other side.</summary>
        private void Burst(Hit hit, Profile profile)
        {
            if (!_cfg.SprayParticles) return;
            if (!_core.IsLoaded) { KeepAssetLoaded(); return; }

            // NOT SCALED BY THE GORE LEVEL ANY MORE. At Mess this multiplied the burst by
            // 1.43, and on a shotgun or sniper profile that stacked to nearly 3x the game's
            // own effect -- which is what made a headshot look like a paint bomb rather than
            // a gunshot. The level belongs to how much blood ends up on the GROUND; the
            // burst in the air stays close to stock.
            var scale = profile.FxScale * _cfg.SprayParticleScale;

            var entry = hit.Headshot && !string.IsNullOrEmpty(profile.HeadFx)
                ? profile.HeadFx
                : profile.EntryFx;

            Play(entry, hit.Position, scale);

            // The exit burst is thrown a little way along the shot line so it reads as coming
            // out of the far side rather than as a second effect on top of the first.
            // The exit burst is now FATAL HITS ONLY. Playing a second effect on every
            // wounding shot doubled the spray on anything automatic, which is exactly the
            // "way too much" everyone sees first.
            if (hit.Fatal && !string.IsNullOrEmpty(profile.ExitFx) && hit.Damage >= 15f)
            {
                Play(profile.ExitFx, hit.Position + hit.Direction * 0.35f, scale);
            }
        }

        private void Play(string effect, Vector3 position, float scale)
        {
            if (string.IsNullOrEmpty(effect)) return;

            try
            {
                World.CreateParticleEffectNonLooped(_core, effect, position,
                                                    Vector3.Zero, scale, InvertAxisFlags.None);
            }
            catch (Exception ex)
            {
                Log.Once("ptfx-" + effect, "Particle effect '" + effect + "' failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Blood back onto whoever was standing close enough to wear it.
        ///
        /// This is the bit that puts blood on Franklin. Killing somebody at arm's length and
        /// walking away spotless is the thing that gives the stock game away, and the engine
        /// already has the decal for it -- BackSplash is what the story missions use.
        /// </summary>
        private void SplashTheShooter(Hit hit)
        {
            if (!hit.Fatal) return;
            if (!_cfg.PlayerWounds) return;

            try
            {
                var player = Game.Player.Character;

                if (player == null || !player.Exists()) return;
                if (player.Handle == hit.Ped.Handle) return;

                // Two and a half metres is roughly "close enough to be in the spray". Beyond
                // that, being splattered is not something the player would expect and would
                // read as the mod dirtying them at random.
                if (player.Position.DistanceTo(hit.Ped.Position) > 2.5f) return;

                var zone = _random.NextDouble() < 0.4 ? PedDamageZone.Head : PedDamageZone.Torso;

                player.ApplyBloodDamage(
                    zone,
                    new Vector2(0.5f, 0.5f),
                    PedBloodDamage.BackSplash,
                    (float)(_random.NextDouble() * 360.0),
                    _cfg.WoundScale,
                    0,
                    0f);
            }
            catch (Exception ex)
            {
                Log.Once("backsplash", "Back splash failed: " + ex.Message);
            }
        }

        /// <summary>Lets the game unload the particle asset when the script stops.</summary>
        public void Shutdown()
        {
            try { _core.MarkAsNoLongerNeeded(); }
            catch { /* it is being torn down anyway */ }
        }
    }
}
