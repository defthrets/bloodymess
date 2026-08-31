using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// The blood that leaves the body: exit spray on the wall behind, and the fan of it on the
    /// ground.
    ///
    /// This is the system the mod is really for. The stock game puts a wound on the ped and a
    /// small burst of particles in the air, and then the world is exactly as clean as it was
    /// before -- shoot somebody against a white wall and the wall stays white. Everything here
    /// is about the far side of the target.
    ///
    /// EVERY SPLATTER IS A RAYCAST. There is no cheaper honest way to do it: blood has to land
    /// on the thing that is actually behind the ped, and only a shape test knows what that is.
    /// The cost is bounded by the per-hit count and by the decal budget above it, both of which
    /// are settings, and the rays are short.
    /// </summary>
    internal sealed class Spray
    {
        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly Profiles _profiles;
        private readonly BloodField _field;
        private readonly Random _random = new Random();

        public Spray(Settings cfg, Decals decals, Profiles profiles, BloodField field)
        {
            _cfg = cfg;
            _decals = decals;
            _profiles = profiles;
            _field = field;
        }

        /// <summary>The configured blood colour. Splatter textures are greyscale without it.</summary>
        private Tint Blood => new Tint(_cfg.BloodRed, _cfg.BloodGreen, _cfg.BloodBlue);

        // ---- counters, for working out why nothing is appearing ---------------
        //
        // These exist because "no blood on the ground" has three completely different
        // causes that look identical in game: nothing was attempted, the budget refused
        // it, or the ground could not be found under it. The settings menu shows these
        // live, so the answer takes one keypress instead of a guess.

        /// <summary>Ground and wall splatters actually laid this session.</summary>
        public int Placed { get; private set; }

        /// <summary>Splatters dropped because no ground could be found beneath them.</summary>
        public int NoGround { get; private set; }

        /// <summary>Splatters dropped because the decal budget or range said no.</summary>
        public int Refused { get; private set; }

        /// <summary>A ground splatter waiting for a free frame to be placed in.</summary>
        private struct Pending
        {
            public Vector3 At;
            public float Size;
            public float Opacity;
        }

        /// <summary>
        /// Ground splatters that have been decided on but not yet placed.
        ///
        /// THIS QUEUE EXISTS BECAUSE EVERY SPLATTER COSTS A RAYCAST. Finding the ground under
        /// a drop means a shape test, and with GroundDrops at 10 (18 after the gore level, cap
        /// 26) a single kill was firing three dozen synchronous shape tests inside ONE tick --
        /// times every hit that landed on the same frame.
        ///
        /// That does not throw and it does not show up as an error. It shows up as a frame
        /// spike, and GTA's euphoria ragdolls are framerate-sensitive, so the visible symptom
        /// was peds dying strangely -- stiff, snapping, sliding. SHVDN killing iFruitAddon2
        /// for "causing the game to freeze too long" in the same session was the other half of
        /// the same evidence.
        ///
        /// So the work is decided immediately and PAID FOR over the following frames, a few
        /// probes at a time. The same blood ends up on the ground; it just arrives over a
        /// quarter of a second instead of all inside one frame.
        /// </summary>
        private readonly Queue<Pending> _pending = new Queue<Pending>();

        /// <summary>How many ground splatters are still waiting. Shown in the menu.</summary>
        public int Queued => _pending.Count;

        /// <summary>
        /// Places a few of the queued ground splatters. Called once per tick.
        ///
        /// The queue is also capped: if somebody is killing faster than the probe budget can
        /// keep up with, the OLDEST pending drops are dropped rather than allowed to pile up
        /// into a backlog that keeps painting blood minutes after the fight ended.
        /// </summary>
        public void Update()
        {
            var budget = _cfg.ProbesPerFrame;

            while (budget-- > 0 && _pending.Count > 0)
            {
                var next = _pending.Dequeue();
                Splash(next.At, next.Size, next.Opacity);
            }

            while (_pending.Count > 500) _pending.Dequeue();
        }

        public void Throw(Hit hit)
        {
            if (!_cfg.SprayEnabled) return;
            if (hit.Ped == null || !hit.Ped.Exists()) return;

            var profile = _profiles.For(hit.Group);
            if (profile.Spray <= 0f) return;

            // Damage scales the count as well as the settings do, so a graze is a graze and a
            // rifle round is not. Capped so that one absurd hit -- a tank shell, a fall from a
            // helicopter -- cannot empty the whole budget on its own.
            //
            // THE CURVE IS DELIBERATELY GENTLE. 0.1.0 used damage/45 capped at 2.5, and since
            // a fatal hit reports the victim's whole remaining health -- around 200 -- every
            // single kill pinned that cap. All deaths therefore produced maximum-size spray,
            // which is exactly what "way too big" looked like in practice.
            var force = Math.Min(1.6f, 0.45f + hit.Damage / 120f);
            var count = (int)Math.Round(_cfg.SprayPerHit * _cfg.Scale * profile.Spray * force);

            if (hit.Headshot) count += 1;

            count = Math.Min(count, 10);

            for (var i = 0; i < count; i++)
            {
                if (!_decals.CanAfford(hit.Position)) break;
                One(hit, force);
            }

            Underfoot(hit, force);
        }

        /// <summary>
        /// The blood that just falls out of the wound onto the floor beneath them.
        ///
        /// Runs on EVERY hit, independent of the thrown spray and independent of the shot
        /// line, because that is the half of it that does not care which way the round was
        /// travelling. Without this the ground under a body can be perfectly clean while a
        /// fan of drops sits several metres behind it, which is exactly how the first few
        /// versions looked and why the mod read as doing nothing.
        /// </summary>
        private void Underfoot(Hit hit, float force)
        {
            var drops = (int)Math.Round(_cfg.SprayGroundDrops * _cfg.Scale);
            if (drops <= 0) return;

            drops = Math.Min(drops, 48);

            var feet = hit.Ped.Position;

            for (var i = 0; i < drops; i++)
            {
                if (!_decals.CanAfford(feet)) return;

                // Tight scatter around the ped, biased inwards. A ring would look like a
                // stencil; a cluster looks like something dripped.
                var angle = _random.NextDouble() * Math.PI * 2.0;
                var reach = (float)(_random.NextDouble() * _random.NextDouble() * 1.3);

                var spot = feet + new Vector3(
                    (float)(Math.Cos(angle) * reach),
                    (float)(Math.Sin(angle) * reach),
                    0f);

                var size = (_cfg.SprayMinSize +
                            (float)_random.NextDouble() * (_cfg.SprayMaxSize - _cfg.SprayMinSize))
                           * force;

                // QUEUED, NOT PLACED. See the note on _pending: doing the ground probe for
                // every drop inline is what was spiking the frame and making euphoria deaths
                // look broken.
                _pending.Enqueue(new Pending
                {
                    At = spot,
                    Size = size,
                    Opacity = _cfg.SprayOpacity * (0.75f + (float)_random.NextDouble() * 0.25f)
                });
            }
        }

        /// <summary>One splatter: pick a direction out of the cone, find what it lands on, mark it.</summary>
        private void One(Hit hit, float force)
        {
            var direction = Scatter(hit.Direction, _cfg.SpraySpread);

            // HOW FAR THE DROP FLIES, AND THIS WAS THE BUG THAT MADE THE MOD LOOK DEAD.
            //
            // 0.1.x used SprayRange * (0.5..1.0), so every single ground splatter landed
            // between 2.25 and 4.5 metres behind the target and there was never anything
            // near the body. You would shoot somebody, look at them, and see clean ground --
            // the blood was real, and three metres away behind you.
            //
            // The curve is now weighted towards the near field: squaring a 0..1 random
            // clusters most drops close to the wound and still throws a few out to full
            // range, which is how spatter actually falls.
            var roll = (float)_random.NextDouble();
            var range = 0.3f + roll * roll * (_cfg.SprayRange - 0.3f);

            // Size is damage-scaled but NOT level-scaled. The gore level decides how MANY
            // splatters there are; letting it scale their size as well compounded the two and
            // was the other half of why 0.1.0 drew dinner plates.
            var size = _cfg.SprayMinSize +
                       (float)_random.NextDouble() * (_cfg.SprayMaxSize - _cfg.SprayMinSize);
            size *= force;

            var opacity = _cfg.SprayOpacity * (0.7f + (float)_random.NextDouble() * 0.3f);

            try
            {
                // The ped that was hit is ignored, or every ray stops on the body it started
                // inside and the whole spray lands on the victim.
                var wall = World.Raycast(hit.Position, direction, range,
                                         IntersectFlags.Map | IntersectFlags.Objects
                                         | IntersectFlags.Vehicles,
                                         hit.Ped);

                if (wall.DidHit)
                {
                    var upright = Math.Abs(wall.SurfaceNormal.Z) < 0.6f;

                    if (upright && !_cfg.SprayOnWalls) return;

                    // A directional splatter on a wall, a plain one on the floor. The
                    // directional texture has a thrown look to it, which is right for
                    // something that hit a wall at speed and wrong for something that fell.
                    var type = upright
                        ? DecalType.SplattersBloodDir
                        : Pick(DecalType.SplattersBlood, DecalType.SplattersBlood2);

                    if (_decals.OnSurface(Decals.Lane.Splatter, type, wall.HitPosition,
                                          wall.SurfaceNormal, size, size * 1.35f,
                                          opacity, Blood) != 0)
                    {
                        Placed++;
                    }
                    else
                    {
                        Refused++;
                    }

                    if (_cfg.SprayMist && _random.NextDouble() < _cfg.SprayMistChance)
                    {
                        _decals.OnSurface(Decals.Lane.Splatter, DecalType.SplattersBloodMist,
                                          wall.HitPosition, wall.SurfaceNormal,
                                          size * 2.2f, size * 2.2f, opacity * 0.45f, Blood);
                    }

                    // Blood that hit a wall runs down it and ends up on the floor. Only the
                    // floor patch is registered as walkable wet blood.
                    if (upright) Splash(wall.HitPosition, size, opacity);
                    else Register(wall.HitPosition, size);

                    return;
                }

                // Nothing in the way, so it lands on the ground at the end of its flight.
                Splash(hit.Position + direction * range, size, opacity);
            }
            catch (Exception ex)
            {
                Log.Once("spray-cast", "Spray raycast failed: " + ex.Message);
            }
        }

        /// <summary>Puts a splatter flat on whatever ground is under a point.</summary>
        private void Splash(Vector3 near, float size, float opacity)
        {
            var ground = Ground.Probe(near);

            if (!ground.Found)
            {
                NoGround++;
                return;
            }

            var type = Pick(DecalType.SplattersBlood, DecalType.SplattersBlood2);

            var handle = _decals.OnGround(Decals.Lane.Splatter, type, ground.Position,
                                          (float)(_random.NextDouble() * Math.PI * 2.0),
                                          size * 1.2f, size * 1.2f, opacity, Blood);

            if (handle == 0) { Refused++; return; }

            Placed++;

            // MIST OVER THE TOP, on the ground as well as on walls. It is the fine halo that
            // makes a splatter read as blood that arrived at speed rather than as a sticker,
            // and it costs no extra raycast -- the ground under this point has just been
            // found, so it is reused.
            if (_cfg.SprayMist && _random.NextDouble() < _cfg.SprayMistChance)
            {
                _decals.OnGround(Decals.Lane.Splatter, DecalType.SplattersBloodMist,
                                 ground.Position,
                                 (float)(_random.NextDouble() * Math.PI * 2.0),
                                 size * 2.6f, size * 2.6f,
                                 opacity * 0.4f, Blood);
            }

            Register(ground.Position, size);
        }

        /// <summary>
        /// Tells the footprint system there is something here to step in.
        ///
        /// Only patches big enough to be worth walking through are registered. A fine speckle
        /// of mist is not a puddle, and treating it as one would have the player tracking blood
        /// out of every room they have ever fired a shot in.
        /// </summary>
        private void Register(Vector3 position, float size)
        {
            // THRESHOLD LOWERED FROM 0.18. Splatter sizes were cut hard in 0.1.1 to stop
            // them being dinner plates, which quietly pushed almost every splatter under
            // the old cut-off -- so nothing registered as wet and the footprints, which are
            // the whole point of the mod, had nothing to pick up.
            if (size < 0.09f) return;

            _field.Add(position, Math.Max(0.12f, size * 0.7f), _cfg.FootprintWetSeconds);
        }

        /// <summary>
        /// A direction pushed off course by up to the spread, in a cone.
        ///
        /// Built from two perpendicular vectors rather than by jittering the components, which
        /// would bias the scatter towards the diagonals and make a spray pattern that is
        /// visibly square.
        /// </summary>
        private Vector3 Scatter(Vector3 direction, float spread)
        {
            if (direction.LengthSquared() < 0.0001f) direction = Vector3.WorldNorth;
            direction.Normalize();

            if (spread <= 0f) return direction;

            var reference = Math.Abs(direction.Z) > 0.9f ? Vector3.WorldNorth : Vector3.WorldUp;

            var right = Vector3.Cross(direction, reference);
            if (right.LengthSquared() < 0.0001f) right = Vector3.WorldEast;
            right.Normalize();

            var up = Vector3.Cross(right, direction);
            up.Normalize();

            var angle = _random.NextDouble() * Math.PI * 2.0;
            var reach = (float)(_random.NextDouble() * spread);

            var offset = right * (float)(Math.Cos(angle) * reach) +
                         up * (float)(Math.Sin(angle) * reach);

            var scattered = direction + offset;
            scattered.Normalize();

            return scattered;
        }

        private int Pick(int a, int b) => _random.NextDouble() < 0.5 ? a : b;
    }
}
