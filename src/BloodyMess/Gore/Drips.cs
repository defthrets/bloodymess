using System;
using GTA;
using GTA.Math;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// What a wounded ped leaves behind them while they are still on their feet.
    ///
    /// THIS IS THE SYSTEM THAT HAS TO BE KEPT ON A LEASH. The gore mod this replaces on this
    /// machine, RealisticGoreV, had a bleed loop with no way to configure it: every ped and the
    /// player bled continuously, forever, from the moment they were scratched. It had to be
    /// deleted rather than turned down, which is the whole reason Bloody Mess exists.
    ///
    /// So bleeding here is bounded twice over. It runs on a WINDOW that is refreshed by being
    /// hurt and expires on its own -- nobody bleeds because they once were shot. And drops are
    /// spaced by DISTANCE TRAVELLED rather than by time, so standing still leaves nothing at
    /// all: a ped bleeding out on the spot is the pool's job, not this one's.
    /// </summary>
    internal sealed class Drips
    {
        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly BloodField _field;
        private readonly Random _random = new Random();

        public Drips(Settings cfg, Decals decals, BloodField field)
        {
            _cfg = cfg;
            _decals = decals;
            _field = field;
        }

        public void Update(Victims victims)
        {
            if (!_cfg.DripsEnabled) return;

            var now = Game.GameTime;

            foreach (var victim in victims.Tracked)
            {
                Bleed(victim, now);
            }
        }

        private void Bleed(Victim victim, int now)
        {
            if (victim.BleedUntil == 0) return;

            if (now > victim.BleedUntil)
            {
                victim.BleedUntil = 0;
                return;
            }

            var ped = victim.Ped;
            if (ped == null || !ped.Exists()) return;

            try
            {
                if (ped.IsInVehicle()) return;
            }
            catch
            {
                return;
            }

            var position = ped.Position;
            var travelled = position.DistanceTo2D(victim.LastDripAt);

            if (travelled < _cfg.DripDistance) return;

            // Teleports, respawns and getting out of a car all show up as an enormous jump.
            // Resetting rather than drawing avoids a drop appearing halfway across the map.
            if (travelled > 25f)
            {
                victim.LastDripAt = position;
                return;
            }

            victim.LastDripAt = position;

            if (!_decals.CanAfford(position)) return;

            var ground = Ground.Probe(position, 2f, ped);
            if (!ground.Found) return;

            var size = _cfg.DripSize * (0.7f + (float)_random.NextDouble() * 0.6f) *
                       (0.7f + _cfg.Scale * 0.3f);

            _decals.OnGround(Decals.Lane.Splatter, DecalType.SplattersBlood, ground.Position,
                             (float)(_random.NextDouble() * Math.PI * 2.0),
                             size, size, _cfg.DripOpacity);

            // Drops are registered as wet, but at their own small size -- so a blood trail can
            // be walked in and carried on, which is right, without a single drop behaving like
            // a puddle.
            _field.Add(ground.Position, size * 0.5f, _cfg.FootprintWetSeconds * 0.5f);
        }
    }
}
