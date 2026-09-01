using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// The decal type numbers ADD_DECAL takes.
    ///
    /// These are the game's own DecalTypes enum, taken from the native reference rather than
    /// guessed at. Only the blood ones are listed, because they are the only ones this mod has
    /// any business drawing. A number the game does not recognise draws nothing at all -- no
    /// error, no log line -- which is exactly why they are named here once instead of being
    /// typed as literals at eight call sites.
    /// </summary>
    internal static class DecalType
    {
        /// <summary>The general splatter. The workhorse: spray, drips and footprints.</summary>
        public const int SplattersBlood = 1010;

        /// <summary>Directional splatter, which reads as thrown rather than dropped.</summary>
        public const int SplattersBloodDir = 1015;

        /// <summary>Fine mist. Sits over a splatter and takes the hard edge off it.</summary>
        public const int SplattersBloodMist = 1017;

        /// <summary>A second splatter set. Mixed in so a wall is not eight copies of one texture.</summary>
        public const int SplattersBlood2 = 1110;

        /// <summary>
        /// A pool on a hard surface, using the game's BLOOD pool id.
        ///
        /// FOUR TEXTURE VARIANTS and the engine picks one at random: `pool_solid`, which is
        /// colourless, and three `fxdecal_blood_pool` textures which are already dark red.
        /// Tinted, those come out as two visibly different shades -- a bright red pool beside
        /// a dark maroon one, from the same setting. Good textures, inconsistent colour.
        /// </summary>
        public const int SolidPoolBlood = 9001;

        /// <summary>
        /// A pool on a hard surface with exactly ONE texture variant.
        ///
        /// This is the game's oil-pool id, and the id is only a row in decals.dat selecting a
        /// texture -- nothing about it is oil-specific once we supply the colour. It resolves
        /// to `pool_solid` every single time, so the tint is the ONLY thing deciding what a
        /// pool looks like and every pool matches every other pool. That consistency is worth
        /// more than the extra texture detail in the 9001 set, which cannot be selected for.
        /// </summary>
        public const int SolidPoolPlain = 9002;

        /// <summary>A pool on something that soaks -- grass, dirt, carpet. Flat and matte.</summary>
        public const int PorousPoolBlood = 9006;
    }

    /// <summary>
    /// The colour a decal texture is multiplied by.
    ///
    /// THIS IS NOT DECORATION, IT IS WHAT MAKES BLOOD RED. Most of the game's blood splatter
    /// textures -- fxdecal_splatter_mist and the rest of that family -- are GREYSCALE MASKS.
    /// The colour comes from the coefficients handed to ADD_DECAL, and passing white through
    /// one produces a white splatter. This was shipped wrong in 0.1.0, with a comment
    /// confidently explaining that the textures were "already the right colour", and the
    /// result was blood that came out like milk.
    ///
    /// Pools are tinted too, and by their OWN colour (see Settings.PoolRed). They used to
    /// pass None on the grounds that fxdecal_blood_pool is properly coloured art -- which is
    /// true of three of that decal's four texture variants and false of the fourth, so an
    /// untinted pool came out white one time in four.
    /// </summary>
    internal struct Tint
    {
        public float R, G, B;

        public Tint(float r, float g, float b) { R = r; G = g; B = b; }

        /// <summary>Leaves the texture exactly as the artist drew it.</summary>
        public static Tint None => new Tint(1f, 1f, 1f);
    }

    /// <summary>
    /// Every decal this mod puts in the world, and the only thing allowed to put one there.
    ///
    /// THIS CLASS IS THE REASON THE MOD IS SAFE TO RUN. The game has a fixed decal pool, and
    /// script decals compete with the game's own bullet holes, tyre marks and scuffs for it.
    /// Going over it does not throw an exception: decals just stop appearing, or the engine
    /// starts recycling ones that are still wanted. Both look like the mod being broken rather
    /// than the mod being greedy, and neither shows up in a log.
    ///
    /// So nothing calls ADD_DECAL directly. Everything comes through here, which:
    ///
    ///   - refuses anything past a per-second rate, so one shotgun blast into a crowd cannot
    ///     spend the whole pool in a frame;
    ///   - refuses anything beyond a range from the camera, because a decal nobody can see is
    ///     a decal spent on nothing;
    ///   - evicts its own oldest decal when it hits the cap, rather than letting the engine
    ///     choose which of ITS decals to drop;
    ///   - keeps pools in a separate lane, so a long firefight cannot evict the pool under the
    ///     first body -- that pool is what the footprints are picked up from.
    /// </summary>
    internal sealed class Decals
    {
        /// <summary>Which cap a decal counts against.</summary>
        public enum Lane
        {
            /// <summary>Spray, drips, prints, tyre tracks. Short-lived, evicted oldest-first.</summary>
            Splatter,

            /// <summary>Pools. Fewer, longer-lived, and the footprint system reads them.</summary>
            Pool
        }

        private struct Entry
        {
            public int Handle;
            public int Frame;

            /// <summary>Game time in milliseconds when this decal was laid.</summary>
            public int BornAt;
        }

        private readonly Settings _cfg;

        private readonly List<Entry> _splatters = new List<Entry>();
        private readonly List<Entry> _pools = new List<Entry>();

        /// <summary>Decals added in the current one-second window, and when that window opened.</summary>
        private int _thisSecond;
        private int _windowOpenedAt;

        private int _frame;

        public Decals(Settings cfg)
        {
            _cfg = cfg;
        }

        public int LiveSplatters => _splatters.Count;
        public int LivePools => _pools.Count;

        /// <summary>Decals refused this session because the budget was already spent.</summary>
        public int Refused { get; private set; }

        /// <summary>Blood removed by age this session, rather than by eviction.</summary>
        public int Expired { get; private set; }

        /// <summary>Advances the frame counter, reopens the rate window, and ages blood out.</summary>
        public void Tick()
        {
            _frame++;

            var now = Game.GameTime;
            if (now - _windowOpenedAt >= 1000)
            {
                _windowOpenedAt = now;
                _thisSecond = 0;
            }

            Expire(now);
        }

        /// <summary>
        /// Takes blood back out of the world once it is old enough.
        ///
        /// WHY THIS IS DONE HERE RATHER THAN WITH ADD_DECAL'S OWN TIMEOUT: that parameter's
        /// unit is not reliably documented -- the native reference disagrees with itself about
        /// seconds versus milliseconds -- which is exactly why this mod passes a deliberately
        /// enormous value for it and does not rely on it. Every decal we lay is already
        /// stamped with the time it was laid, so ageing them out here is exact, in units we
        /// chose, and cannot be wrong by a factor of a thousand.
        ///
        /// The lists are APPENDED IN ORDER, so the oldest entry is always at the front. That
        /// makes this a walk off the head until something is young enough rather than a scan
        /// of the whole ledger, so the cost is proportional to what actually expired -- which
        /// is usually nothing.
        ///
        /// Without this, blood only ever left when the cap evicted it, so a session would
        /// climb to the ceiling and simply stay there.
        /// </summary>
        private void Expire(int now)
        {
            if (_cfg.FadeSeconds <= 0f) return;

            var life = (int)(_cfg.FadeSeconds * 1000f);

            Expire(_splatters, now - life, ref _splatterCursor);

            // Pools last longer, because there are far fewer of them and a body somebody is
            // still standing over should not go clean while the spatter around it remains.
            Expire(_pools, now - life * 2, ref _poolCursor);
        }

        private void Expire(List<Entry> list, int cutoff, ref int cursor)
        {
            var removed = 0;

            while (list.Count > 0 && list[0].BornAt <= cutoff)
            {
                var oldest = list[0];
                list.RemoveAt(0);

                try { Function.Call(Hash.REMOVE_DECAL, oldest.Handle); }
                catch { /* already gone */ }

                Expired++;
                removed++;
            }

            if (removed == 0) return;

            // ENTRIES CAME OFF THE FRONT, so everything behind them shuffled down and the
            // prune cursor now points further along the list than it did. It is pulled back by
            // exactly what was removed, which leaves it on the same entry it was on.
            //
            // Resetting it to zero instead -- and doing so every tick, removal or not -- would
            // quietly undo the round-robin pruning: the sweep would restart at the head every
            // frame and never reach the far end of the list.
            cursor = Math.Max(0, cursor - removed);
        }

        /// <summary>True when another decal would be within both the rate and the range limits.</summary>
        public bool CanAfford(Vector3 position)
        {
            if (_thisSecond >= _cfg.DecalsPerSecond) return false;

            try
            {
                var camera = GameplayCamera.Position;
                if (camera.DistanceToSquared(position) > _cfg.DecalRange * _cfg.DecalRange) return false;
            }
            catch
            {
                // No camera to measure against is not a reason to refuse; the caps still hold.
            }

            return true;
        }

        /// <summary>
        /// Lays a decal flat on the ground, pointing along a heading.
        ///
        /// The projection direction is straight down and the side vector is the heading, which
        /// is what makes a footprint point the way somebody is walking rather than lying at a
        /// fixed angle like a sticker.
        /// </summary>
        public int OnGround(Lane lane, int type, Vector3 position, float heading,
                            float width, float length, float opacity, Tint tint,
                            float timeout = -1f)
        {
            var side = new Vector3((float)Math.Cos(heading), (float)Math.Sin(heading), 0f);
            return Add(lane, type, position, new Vector3(0f, 0f, -1f), side,
                       width, length, opacity, tint, timeout);
        }

        /// <summary>
        /// Lays a decal against a surface with a known normal -- a wall, a car door, a ceiling.
        ///
        /// The decal projects INTO the surface, so the direction is the negated normal. Getting
        /// that backwards puts the decal on the far side of the wall, where it is invisible and
        /// still costs a slot.
        /// </summary>
        public int OnSurface(Lane lane, int type, Vector3 position, Vector3 normal,
                             float width, float height, float opacity, Tint tint,
                             float timeout = -1f)
        {
            if (normal.LengthSquared() < 0.0001f) normal = new Vector3(0f, 0f, 1f);
            normal.Normalize();

            // Any vector perpendicular to the normal will do for the side; this picks one that
            // is stable whether the surface is a floor or a wall.
            var reference = Math.Abs(normal.Z) > 0.9f ? Vector3.WorldNorth : Vector3.WorldUp;
            var side = Vector3.Cross(normal, reference);

            if (side.LengthSquared() < 0.0001f) side = Vector3.WorldEast;
            side.Normalize();

            return Add(lane, type, position, -normal, side, width, height, opacity, tint, timeout);
        }

        /// <summary>
        /// The one call to ADD_DECAL in the mod.
        ///
        /// The colour comes from the caller -- see Tint, and note that most of the splatter
        /// textures are greyscale masks that are WHITE unless something tints them.
        /// </summary>
        private int Add(Lane lane, int type, Vector3 position, Vector3 direction, Vector3 side,
                        float width, float height, float opacity, Tint tint, float timeout)
        {
            if (!CanAfford(position)) { Refused++; return 0; }

            var list = lane == Lane.Pool ? _pools : _splatters;
            var cap = lane == Lane.Pool ? _cfg.MaxPools : _cfg.MaxSplatters;

            if (cap <= 0) { Refused++; return 0; }

            EvictDownTo(list, cap - 1);

            if (timeout < 0f) timeout = _cfg.DecalTimeout;

            try
            {
                var handle = Function.Call<int>(
                    Hash.ADD_DECAL, type,
                    position.X, position.Y, position.Z,
                    direction.X, direction.Y, direction.Z,
                    side.X, side.Y, side.Z,
                    width, height,
                    tint.R, tint.G, tint.B,
                    Clamp01(opacity),
                    // See Settings.DecalTimeout for why this is an enormous number and not
                    // zero: the unit this parameter is in is not reliably documented, and the
                    // budget is what really decides when a decal goes.
                    timeout,
                    false, false, false);

                if (handle == 0) { Refused++; return 0; }

                list.Add(new Entry { Handle = handle, Frame = _frame, BornAt = Game.GameTime });
                _thisSecond++;
                return handle;
            }
            catch (Exception ex)
            {
                Log.Once("add-decal", "ADD_DECAL failed: " + ex.Message);
                return 0;
            }
        }

        /// <summary>Takes a decal back out of the world, by handle.</summary>
        public void Remove(int handle)
        {
            if (handle == 0) return;

            try { Function.Call(Hash.REMOVE_DECAL, handle); }
            catch { /* already gone */ }

            Forget(_pools, handle);
            Forget(_splatters, handle);
        }

        private static void Forget(List<Entry> list, int handle)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Handle != handle) continue;
                list.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// Drops oldest-first until the list fits.
        ///
        /// Oldest-first rather than nearest-first on purpose: a player who has just walked away
        /// from a scene should be able to turn round and find it as they left it, and distance
        /// eviction is what makes blood vanish the moment you look away.
        /// </summary>
        private void EvictDownTo(List<Entry> list, int size)
        {
            while (list.Count > size && list.Count > 0)
            {
                var oldest = list[0];
                list.RemoveAt(0);

                try { Function.Call(Hash.REMOVE_DECAL, oldest.Handle); }
                catch { /* nothing to do about it */ }
            }
        }

        /// <summary>
        /// Forgets decals the engine has already taken back, so the ledger does not slowly
        /// fill with handles for decals that stopped existing when the player changed area.
        ///
        /// Only a slice is checked per call. IS_DECAL_ALIVE is a native call each, and running
        /// a hundred and eighty of them every frame to tidy a list is worse than the untidy
        /// list.
        /// </summary>
        public void Prune(int budgetPerCall = 12)
        {
            Prune(_splatters, budgetPerCall, ref _splatterCursor);
            Prune(_pools, budgetPerCall / 2, ref _poolCursor);
        }

        // A CURSOR EACH, not one shared between the two lists. Sharing it meant the position
        // reached in the splatter list was immediately reset by the much shorter pool list, so
        // the far end of the splatter list was never reached and never checked.
        private int _splatterCursor;
        private int _poolCursor;

        private static void Prune(List<Entry> list, int count, ref int cursor)
        {
            if (list.Count == 0 || count <= 0) return;

            for (var n = 0; n < count && list.Count > 0; n++)
            {
                if (cursor >= list.Count) cursor = 0;

                bool alive;
                try { alive = Function.Call<bool>(Hash.IS_DECAL_ALIVE, list[cursor].Handle); }
                catch { alive = true; }

                if (alive) { cursor++; continue; }

                list.RemoveAt(cursor);
            }
        }

        /// <summary>
        /// Removes every decal this mod put down, and nothing else.
        ///
        /// By handle rather than by REMOVE_DECALS_IN_RANGE, which would also take the game's
        /// own bullet holes and every other mod's decals with it. Called on shutdown and from
        /// the settings menu, so a reload does not leave a scene nobody can clear.
        /// </summary>
        public int ClearAll()
        {
            var removed = _splatters.Count + _pools.Count;

            foreach (var entry in _splatters)
            {
                try { Function.Call(Hash.REMOVE_DECAL, entry.Handle); } catch { }
            }
            foreach (var entry in _pools)
            {
                try { Function.Call(Hash.REMOVE_DECAL, entry.Handle); } catch { }
            }

            _splatters.Clear();
            _pools.Clear();
            _splatterCursor = 0;
            _poolCursor = 0;

            return removed;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
