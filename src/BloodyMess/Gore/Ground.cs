using System;
using GTA;
using GTA.Math;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>What the ground under a point turned out to be.</summary>
    internal struct GroundHit
    {
        /// <summary>False when there was nothing under the point at all -- mid-air, or deep water.</summary>
        public bool Found;

        /// <summary>Where the surface is. A decal goes slightly above this, never on it exactly.</summary>
        public Vector3 Position;

        /// <summary>Which way the surface faces. A ramp is not flat and a decal on one should not be.</summary>
        public Vector3 Normal;

        /// <summary>True for surfaces that soak rather than pool -- grass, dirt, sand, carpet.</summary>
        public bool Porous;

        /// <summary>The pool decal this surface should use.</summary>
        public int PoolType => Porous ? DecalType.PorousPoolBlood : DecalType.SolidPoolBlood;
    }

    /// <summary>
    /// Finds the ground, and works out what it is made of.
    ///
    /// Blood behaves differently on tarmac and on grass, and the game already has two pool
    /// decals for exactly that distinction -- solidPool_blood sits wet on top of a hard
    /// surface, porousPool_blood soaks flat into a soft one. Using the wrong one is the
    /// difference between a puddle and a stain, and it is free to get right: the shape test
    /// that finds the ground height also returns the material.
    /// </summary>
    internal static class Ground
    {
        /// <summary>
        /// How far above the surface a decal is placed.
        ///
        /// Z-fighting is the reason. A decal at exactly the surface height flickers against
        /// the surface itself; a couple of centimetres up is invisible to look at and stable.
        /// </summary>
        public const float Lift = 0.03f;

        /// <summary>
        /// Probes downward from a point and reports what is under it.
        ///
        /// Starts slightly ABOVE the point given, because the usual caller passes a foot or a
        /// ped's root position, which is already on the surface -- a ray that starts exactly
        /// at the ground can start inside it and find nothing.
        /// </summary>
        public static GroundHit Probe(Vector3 point, float reach = 2.5f, Entity ignore = null)
        {
            var result = new GroundHit { Position = point, Normal = new Vector3(0f, 0f, 1f) };

            try
            {
                var from = point + new Vector3(0f, 0f, 0.5f);

                // Peds and vehicles are deliberately NOT in the flags. Blood belongs on the
                // world, and a ray that stops on the body it came out of puts the pool inside
                // the corpse -- which reads as no pool at all.
                var hit = World.Raycast(from, new Vector3(0f, 0f, -1f), reach + 0.5f,
                                        IntersectFlags.Map | IntersectFlags.Objects, ignore);

                if (!hit.DidHit)
                {
                    // The shape test found nothing, which happens indoors on some interiors
                    // and over map seams. The height API is less precise but rarely fails, so
                    // it is worth one more try before giving up on a decal entirely.
                    float z;
                    if (World.GetGroundHeight(point, out z))
                    {
                        result.Found = true;
                        result.Position = new Vector3(point.X, point.Y, z + Lift);
                        return result;
                    }

                    return result;
                }

                result.Found = true;
                result.Position = hit.HitPosition + new Vector3(0f, 0f, Lift);
                result.Normal = hit.SurfaceNormal;
                result.Porous = IsPorous(hit.MaterialHash);
            }
            catch (Exception ex)
            {
                Log.Once("ground-probe", "Ground probe failed: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Whether blood soaks into this rather than sitting on it.
        ///
        /// Listed by name against SHVDN's own MaterialHash enum rather than by hashed strings.
        /// The names come from the game, so a typo is a compile error instead of a hash that
        /// silently never matches anything -- which is the usual way a table like this rots.
        ///
        /// Anything not listed is treated as hard, which is the right way round: a wet pool on
        /// grass reads as slightly wrong, a soaked stain on a road reads as broken.
        /// </summary>
        public static bool IsPorous(MaterialHash material)
        {
            switch (material)
            {
                case MaterialHash.Grass:
                case MaterialHash.GrassShort:
                case MaterialHash.GrassLong:
                case MaterialHash.Soil:
                case MaterialHash.DirtTrack:
                case MaterialHash.MudHard:
                case MaterialHash.MudSoft:
                case MaterialHash.MudDeep:
                case MaterialHash.MudPothole:
                case MaterialHash.SandLoose:
                case MaterialHash.SandCompact:
                case MaterialHash.SandWet:
                case MaterialHash.SandDryDeep:
                case MaterialHash.SandWetDeep:
                case MaterialHash.SandTrack:
                case MaterialHash.GravelSmall:
                case MaterialHash.GravelLarge:
                case MaterialHash.GravelDeep:
                case MaterialHash.Bushes:
                case MaterialHash.BushesNoinst:
                case MaterialHash.Leaves:
                case MaterialHash.Woodchips:
                case MaterialHash.Hay:
                case MaterialHash.CarpetSolid:
                case MaterialHash.CarpetSolidDusty:
                case MaterialHash.CarpetFloorboard:
                case MaterialHash.SnowLoose:
                case MaterialHash.SnowDeep:
                case MaterialHash.SnowCompact:
                    return true;

                default:
                    return false;
            }
        }
    }
}
