using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;

namespace BloodyMess.Gore
{
    /// <summary>
    /// Where the wet blood is.
    ///
    /// THE FOOTPRINTS ARE THE REASON THIS EXISTS. A decal, once placed, is a handle and a
    /// promise -- the game will draw it, and it will tell you whether it is still alive, but it
    /// will not tell you where it is or let you ask what is underneath a foot. So the mod keeps
    /// its own note of every patch of blood it has put on the ground, and the foot check is a
    /// distance test against that note rather than anything the engine is asked about.
    ///
    /// Spots dry. That is not decoration: without it, a street where a fight happened an hour
    /// ago would still be printing footprints, and the note would grow without limit. A dried
    /// spot stops being pickup-able while its decal stays exactly where it was, which is also
    /// how blood behaves.
    /// </summary>
    internal sealed class BloodField
    {
        private struct Spot
        {
            public Vector3 Position;
            public float Radius;
            public int DryAt;
        }

        /// <summary>
        /// The hard cap on remembered spots.
        ///
        /// Here so that a very long session cannot turn a distance test into a performance
        /// problem. Oldest goes first.
        ///
        /// RAISED FROM 220, which a single street firefight was already reaching (131 spots
        /// off one exchange). At the cap the oldest wet blood is forgotten while its decal is
        /// still on the ground, so you can walk through a visible pool and pick nothing up --
        /// the footprints just stop working, with no clue as to why.
        /// </summary>
        private const int MaxSpots = 420;

        private readonly List<Spot> _spots = new List<Spot>();
        private int _lastSweep;

        public int Count => _spots.Count;

        /// <summary>
        /// Notes a patch of wet blood on the ground.
        ///
        /// Only ground blood is worth recording. Spray up a wall is not something anybody
        /// walks through, and recording it would have feet picking blood up out of mid-air
        /// next to a wall.
        /// </summary>
        public void Add(Vector3 position, float radius, float wetSeconds)
        {
            if (radius <= 0f || wetSeconds <= 0f) return;

            if (_spots.Count >= MaxSpots) _spots.RemoveAt(0);

            _spots.Add(new Spot
            {
                Position = position,
                Radius = radius,
                DryAt = Game.GameTime + (int)(wetSeconds * 1000f)
            });
        }

        /// <summary>
        /// How wet the ground is under a point, from 0 (dry) to 1 (standing in it).
        ///
        /// Returns the STRONGEST overlapping spot rather than a sum, so walking through the
        /// middle of a big pool and clipping the edge of five small ones are not the same
        /// thing. A sum would make a crowded scene pick up impossibly much.
        /// </summary>
        public float WetnessAt(Vector3 point, float reach)
        {
            var now = Game.GameTime;
            var best = 0f;

            for (var i = 0; i < _spots.Count; i++)
            {
                var spot = _spots[i];

                if (spot.DryAt <= now) continue;

                // Vertical distance is checked separately and tightly. Without it, a pool on
                // the floor below prints footprints on the floor above -- the horizontal test
                // alone cannot tell two storeys apart.
                if (Math.Abs(spot.Position.Z - point.Z) > 1.2f) continue;

                var range = spot.Radius + reach;
                var distanceSquared = spot.Position.DistanceToSquared2D(point);

                if (distanceSquared > range * range) continue;

                var closeness = 1f - (float)Math.Sqrt(distanceSquared) / range;
                if (closeness > best) best = closeness;
            }

            return best;
        }

        /// <summary>
        /// Throws away spots that have dried, a few times a minute.
        ///
        /// Not every frame: the list is small, the test is cheap, and a sweep that runs sixty
        /// times a second to find nothing is the definition of wasted work.
        /// </summary>
        public void Sweep()
        {
            var now = Game.GameTime;

            if (now - _lastSweep < 5000) return;
            _lastSweep = now;

            for (var i = _spots.Count - 1; i >= 0; i--)
            {
                if (_spots[i].DryAt > now) continue;
                _spots.RemoveAt(i);
            }
        }

        public void Clear()
        {
            _spots.Clear();
        }
    }
}
