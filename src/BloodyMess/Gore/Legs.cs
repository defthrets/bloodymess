using System;
using GTA;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// Shot in the legs, still alive, and down on the ground.
    ///
    /// THE GAME ALREADY HAS THIS. TASK_WRITHE is Rockstar's own wounded-on-the-ground state --
    /// the one the story missions use -- and it comes with its own animations, its own crawl,
    /// and the ability to keep firing from the floor. Hand-rolling a crawl out of movement
    /// clipsets would look worse and break on any ped whose skeleton is unusual.
    ///
    /// THIS IS THE ONE PART OF THE MOD THAT CHANGES HOW THE GAME PLAYS rather than how it
    /// looks. Everything else here is blood: switch it off and the fight is identical. This
    /// makes people survive shots that would have killed them, so it is the one system worth
    /// being suspicious of if something elsewhere starts behaving oddly -- particularly the
    /// police and gang mods sharing this scripts folder, which reasonably expect a ped that
    /// has been shot enough to die to actually die.
    ///
    /// Kept honest in three ways:
    ///
    ///   - health is clamped up ONCE, at the moment of going down, and never again. Every
    ///     round after that lands normally, so a downed ped is still perfectly killable. A
    ///     mod that made people unkillable would be a bug, not a feature.
    ///   - it only ever applies to peds, never the player.
    ///   - it is one ini switch, and the fight goes back to stock.
    /// </summary>
    internal sealed class Legs
    {
        private readonly Settings _cfg;
        private readonly Random _random = new Random();

        public Legs(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Peds put on the ground this session. Shown in the settings menu.</summary>
        public int Downed { get; private set; }

        /// <summary>
        /// Decides whether this hit takes the legs out from under somebody.
        ///
        /// Returns true when they went down, so the caller knows the hit was survived.
        /// </summary>
        public bool Try(Hit hit)
        {
            if (!_cfg.LegsEnabled) return false;

            if (hit.Zone != PedDamageZone.LeftLeg && hit.Zone != PedDamageZone.RightLeg) return false;

            var ped = hit.Ped;
            if (ped == null || !ped.Exists()) return false;

            var victim = hit.Victim;
            if (victim == null) return false;

            // Once each. Putting somebody who is already crawling back into the task restarts
            // the animation, which reads as them flinching back to the start every time they
            // are hit again.
            if (victim.Crippled) return false;
            if (victim.IsPlayer) return false;

            if (hit.Damage < _cfg.LegsMinDamage) return false;
            if (_random.NextDouble() > _cfg.LegsChance) return false;

            try
            {
                if (ped.IsDead) return false;

                // KEEP THEM ALIVE, ONCE. Injury alone must not finish them, and the health is
                // lifted clear of the fatal threshold so the shot that just landed does not.
                // This is the only time either is touched -- see the note on the class.
                Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, ped.Handle, false);

                if (ped.HealthFloat < _cfg.LegsHealth) ped.HealthFloat = _cfg.LegsHealth;

                // The game's own wounded state. The target is the player: given one, a ped who
                // is still armed will keep shooting from the floor, which is what writhe is
                // for and is far better than lying there inert.
                //
                // SIX ARGUMENTS ARE PUSHED for a native documented with four. Extra arguments
                // are harmless -- the native reads what it expects and ignores the rest -- but
                // pushing too FEW leaves it reading whatever the last call left in the
                // argument buffer, which is a genuinely unpredictable bug. When the reference
                // is uncertain, err long.
                var target = Game.Player.Character;
                var targetHandle = target != null && target.Exists() ? target.Handle : 0;

                Function.Call(Hash.TASK_WRITHE, ped.Handle, targetHandle,
                              (int)(_cfg.LegsSeconds * 1000f), 0, 0, false);
            }
            catch (Exception ex)
            {
                Log.Once("writhe", "Could not put a ped into writhe: " + ex.Message);
                return false;
            }

            victim.Crippled = true;
            victim.CrippledAt = Game.GameTime;
            Downed++;

            return true;
        }

        /// <summary>
        /// Finishes off anybody who has been crawling long enough, if that is wanted.
        ///
        /// Without this they simply stand back up when the task runs out, healed by nothing
        /// and walking on a leg that was shot out from under them a minute ago -- which looks
        /// far worse than either dying or staying down.
        /// </summary>
        public void Update(Victims victims)
        {
            if (!_cfg.LegsEnabled || !_cfg.LegsBleedOut) return;

            var now = Game.GameTime;
            var life = (int)(_cfg.LegsSeconds * 1000f);

            foreach (var victim in victims.Tracked)
            {
                if (!victim.Crippled) continue;
                if (now - victim.CrippledAt < life) continue;

                victim.Crippled = false;

                var ped = victim.Ped;
                if (ped == null || !ped.Exists()) continue;

                try
                {
                    if (!ped.IsDead) ped.Kill();
                }
                catch
                {
                    // If they cannot be killed they simply get up; not worth failing over.
                }
            }
        }
    }
}
