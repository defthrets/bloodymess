using System;
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

        public void Throw(Hit hit)
        {
            if (!_cfg.SprayEnabled) return;
            if (hit.Ped == null || !hit.Ped.Exists()) return;

            var profile = _profiles.For(hit.Group);
            if (profile.Spray <= 0f) return;

            // Damage scales the count as well as the settings do, so a graze is a graze and a
            // rifle round is not. Capped so that one absurd hit -- a tank shell, a fall from a
            // helicopter -- cannot empty the whole budget on its own.
            var force = Math.Min(2.5f, 0.5f + hit.Damage / 45f);
            var count = (int)Math.Round(_cfg.SprayPerHit * _cfg.Scale * profile.Spray * force);

            if (hit.Headshot) count += 2;
            if (count <= 0) return;

            count = Math.Min(count, 14);

            for (var i = 0; i < count; i++)
            {
                if (!_decals.CanAfford(hit.Position)) return;
                One(hit, force);
            }
        }

        /// <summary>One splatter: pick a direction out of the cone, find what it lands on, mark it.</summary>
        private void One(Hit hit, float force)
        {
            var direction = Scatter(hit.Direction, _cfg.SpraySpread);
            var range = _cfg.SprayRange * (0.5f + (float)_random.NextDouble() * 0.5f);

            var size = _cfg.SprayMinSize +
                       (float)_random.NextDouble() * (_cfg.SprayMaxSize - _cfg.SprayMinSize);
            size *= force * (0.6f + _cfg.Scale * 0.3f);

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

                    _decals.OnSurface(Decals.Lane.Splatter, type, wall.HitPosition,
                                      wall.SurfaceNormal, size, size * 1.35f, opacity);

                    if (_cfg.SprayMist && _random.NextDouble() < 0.35)
                    {
                        _decals.OnSurface(Decals.Lane.Splatter, DecalType.SplattersBloodMist,
                                          wall.HitPosition, wall.SurfaceNormal,
                                          size * 2.2f, size * 2.2f, opacity * 0.45f);
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
            if (!ground.Found) return;

            var type = Pick(DecalType.SplattersBlood, DecalType.SplattersBlood2);

            _decals.OnGround(Decals.Lane.Splatter, type, ground.Position,
                             (float)(_random.NextDouble() * Math.PI * 2.0),
                             size * 1.2f, size * 1.2f, opacity);

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
            if (size < 0.18f) return;

            _field.Add(position, size * 0.6f, _cfg.FootprintWetSeconds);
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
