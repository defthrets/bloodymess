using System;
using GTA;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// The engine's own gore switches, which cost nothing and are worth having on.
    ///
    /// Everything else in this mod adds work: rays, decals, particles. These are levers the
    /// game already has, sitting at their stock settings -- turning the blood particles up is
    /// one native call and no per-frame cost at all. It is the cheapest part of the mod and,
    /// on a weak machine, potentially the only part worth having.
    ///
    /// They are re-applied on a slow timer rather than once at startup. Mission scripts and
    /// other mods set these too, and a setting that was applied at load and quietly reset by a
    /// cutscene an hour later is a bug nobody can reproduce.
    /// </summary>
    internal sealed class Globals
    {
        private const int ReapplyMs = 5000;

        private readonly Settings _cfg;
        private int _lastApplied;

        public Globals(Settings cfg)
        {
            _cfg = cfg;
        }

        public void Update()
        {
            var now = Game.GameTime;

            if (_lastApplied != 0 && now - _lastApplied < ReapplyMs) return;
            _lastApplied = now;

            Apply();
        }

        /// <summary>Pushes every engine setting out. Also called when the menu changes one.</summary>
        public void Apply()
        {
            // SET_PARTICLE_FX_BLOOD_SCALE TAKES A BOOL, not a scale, despite its name -- checked
            // against the native list rather than assumed. Passing a float to a bool parameter
            // is the kind of thing that appears to work until a game update moves the stack.
            Call(Hash.SET_PARTICLE_FX_BLOOD_SCALE, _cfg.BiggerBloodParticles);

            Call(Hash.SET_PARTICLE_FX_BULLET_IMPACT_SCALE, _cfg.BulletImpactScale);
            Call(Hash.SET_DECAL_BULLET_IMPACT_RANGE_SCALE, _cfg.BulletImpactRange);

            // Note the sense of this one: the native DISABLES, so the setting has to be
            // inverted. Shotgun composite decals are the spread of holes a shotgun leaves, and
            // without them a shotgun blast marks a wall like a pistol shot.
            Call(Hash.DISABLE_COMPOSITE_SHOTGUN_DECALS, !_cfg.ShotgunDecals);

            Call(Hash.ENABLE_CLOWN_BLOOD_VFX, _cfg.ClownBlood);
            Call(Hash.ENABLE_ALIEN_BLOOD_VFX, _cfg.AlienBlood);
        }

        /// <summary>
        /// Puts the engine back the way it was found.
        ///
        /// Runs on a script reload as well as on shutdown. Leaving green blood or confetti
        /// switched on after somebody disables the mod would be a mod that broke their game and
        /// left no trace of how.
        /// </summary>
        public void Restore()
        {
            Call(Hash.SET_PARTICLE_FX_BLOOD_SCALE, false);
            Call(Hash.SET_PARTICLE_FX_BULLET_IMPACT_SCALE, 1f);
            Call(Hash.SET_DECAL_BULLET_IMPACT_RANGE_SCALE, 1f);
            Call(Hash.DISABLE_COMPOSITE_SHOTGUN_DECALS, false);
            Call(Hash.ENABLE_CLOWN_BLOOD_VFX, false);
            Call(Hash.ENABLE_ALIEN_BLOOD_VFX, false);
        }

        private static void Call(Hash native, bool value)
        {
            try { Function.Call(native, value); }
            catch (Exception ex) { Log.Once("global-" + native, native + " failed: " + ex.Message); }
        }

        private static void Call(Hash native, float value)
        {
            try { Function.Call(native, value); }
            catch (Exception ex) { Log.Once("global-" + native, native + " failed: " + ex.Message); }
        }
    }
}
