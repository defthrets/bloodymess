using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// One tracked ped, and everything this mod remembers about how much of a state they are in.
    /// </summary>
    internal sealed class Victim
    {
        public Ped Ped;
        public int Handle;

        /// <summary>Health at the last check. The whole hit detector is a delta on this.</summary>
        public float LastHealth;

        /// <summary>Frame this victim was last seen alive in the scan. Used to drop stale entries.</summary>
        public int LastSeen;

        // ---- bleeding --------------------------------------------------------

        /// <summary>Game time in milliseconds until this ped stops dripping. 0 means not bleeding.</summary>
        public int BleedUntil;

        /// <summary>Where the last drip was left, so drips are spaced by distance and not by time.</summary>
        public Vector3 LastDripAt;

        // ---- down and pooling -------------------------------------------------

        /// <summary>Game time in milliseconds when this ped went down. 0 means still up.</summary>
        public int DownAt;

        /// <summary>The pool decal under them, if there is one yet.</summary>
        public int PoolHandle;

        /// <summary>How many growth steps the pool has taken.</summary>
        public int PoolStep;

        /// <summary>Where the pool is, which is where they fell rather than where they are now.</summary>
        public Vector3 PoolAt;

        public bool PoolFinished;

        // ---- feet -------------------------------------------------------------

        /// <summary>Prints this ped still has in them, counting down as they walk it off.</summary>
        public int PrintsLeft;

        /// <summary>Prints they had when they last stepped in something. The fade is measured against it.</summary>
        public int PrintsAtPickup = 1;

        /// <summary>Where the last print went, so the next one is a stride away.</summary>
        public Vector3 LastPrintAt;

        /// <summary>Which foot is next. Alternated so a trail reads as walking rather than hopping.</summary>
        public bool NextFootIsLeft;

        public bool IsPlayer;

        /// <summary>Their head has already been taken off. Stops it happening twice.</summary>
        public bool HeadGone;
    }

    /// <summary>One hit, worked out once and handed to every system that wants to react to it.</summary>
    internal struct Hit
    {
        public Victim Victim;
        public Ped Ped;

        /// <summary>Where the round went in, as best the game will say.</summary>
        public Vector3 Position;

        /// <summary>
        /// Which way the blood should go: through the body and out the far side.
        /// Unit length, and flattened towards horizontal so spray does not fire at the sky.
        /// </summary>
        public Vector3 Direction;

        /// <summary>Health actually lost. Drives how big a mess this is.</summary>
        public float Damage;

        /// <summary>True when this hit is what killed them.</summary>
        public bool Fatal;

        public bool Headshot;

        public PedDamageZone Zone;

        /// <summary>The bone that took it, when the game knew. Null when it did not.</summary>
        public PedBone Bone;

        /// <summary>What did it. Unarmed when nothing could be worked out.</summary>
        public WeaponGroup Group;

        /// <summary>
        /// The exact weapon, for natives that want a hash rather than a group.
        ///
        /// EXPLODE_PED_HEAD takes one and picks its gore effect from it, so a shotgun and a
        /// pistol produce visibly different bursts. Zero when nothing could be worked out.
        /// </summary>
        public uint WeaponHash;
    }

    /// <summary>
    /// Watches the peds near the player and notices when one of them is hurt.
    ///
    /// HIT DETECTION IS A HEALTH DELTA, not an event. SHVDN has no "ped was shot" callback, and
    /// the alternatives are worse: HAS_ENTITY_BEEN_DAMAGED_BY_WEAPON needs a weapon hash to ask
    /// about, so using it means asking about every weapon in the game, every frame, for every
    /// ped. A delta is one property read per ped per tick and it cannot miss a damage type
    /// nobody thought to enumerate.
    ///
    /// What a delta genuinely cannot tell you is WHO fired, and that matters because the spray
    /// direction comes from it. The game does record a last impact position per ped, so the
    /// direction is derived from that instead -- see Aim below -- with the attacker used only
    /// when the game will name one.
    /// </summary>
    internal sealed class Victims
    {
        /// <summary>How often the nearby ped list is rebuilt. Health is still read every tick.</summary>
        private const int RescanMs = 250;

        /// <summary>Frames a ped can go unseen before being forgotten.</summary>
        private const int StaleFrames = 600;

        /// <summary>Health lost in one tick below which nothing is worth drawing.</summary>
        private const float MinimumDamage = 1.5f;

        private readonly Settings _cfg;
        private readonly Dictionary<int, Victim> _victims = new Dictionary<int, Victim>();
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly List<int> _stale = new List<int>();

        private int _lastScan;
        private int _frame;

        public Victims(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Hits that landed this tick. Valid until the next Update.</summary>
        public IList<Hit> Hits => _hits;

        public IEnumerable<Victim> Tracked => _victims.Values;

        public int Count => _victims.Count;

        public void Update()
        {
            _frame++;
            _hits.Clear();

            var now = Game.GameTime;

            if (now - _lastScan >= RescanMs)
            {
                _lastScan = now;
                Rescan();
            }

            foreach (var victim in _victims.Values)
            {
                Check(victim, now);
            }

            DropStale();
        }

        /// <summary>
        /// Rebuilds the tracked set from whoever is nearby.
        ///
        /// Range is the decal range: a ped further away than we would draw for is a ped there
        /// is no point watching, and this is the only per-frame cost that grows with how busy
        /// the street is.
        /// </summary>
        private void Rescan()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                Adopt(player, true);

                var playerHandle = player.Handle;
                var nearby = World.GetNearbyPeds(player, _cfg.DecalRange);

                foreach (var ped in nearby)
                {
                    if (ped == null || !ped.Exists()) continue;

                    // The player can come back in this list, and adopting them a second time
                    // as an ordinary ped would clear the flag that was just set.
                    if (ped.Handle == playerHandle) continue;

                    Adopt(ped, false);
                }
            }
            catch (Exception ex)
            {
                Log.Once("victim-scan", "Ped scan failed: " + ex.Message);
            }
        }

        private void Adopt(Ped ped, bool isPlayer)
        {
            var handle = ped.Handle;

            if (_victims.TryGetValue(handle, out var existing))
            {
                existing.Ped = ped;
                existing.LastSeen = _frame;

                // Refreshed rather than left as first seen. Switching character promotes a ped
                // who was being tracked as an NPC a moment ago, and a stale false here would
                // have the mod pooling blood under the player and following them with it.
                existing.IsPlayer = isPlayer;
                return;
            }

            _victims[handle] = new Victim
            {
                Ped = ped,
                Handle = handle,
                IsPlayer = isPlayer,
                LastHealth = SafeHealth(ped),
                LastSeen = _frame,
                LastDripAt = ped.Position,
                LastPrintAt = ped.Position
            };
        }

        /// <summary>Looks one tracked ped over and records anything that happened to them.</summary>
        private void Check(Victim victim, int now)
        {
            var ped = victim.Ped;

            if (ped == null || !ped.Exists())
            {
                victim.LastSeen = 0;
                return;
            }

            victim.LastSeen = _frame;

            var health = SafeHealth(ped);
            var lost = victim.LastHealth - health;
            victim.LastHealth = health;

            // Health going UP is a respawn, a heal or a health pack. Nothing to draw, but the
            // baseline has already been reset above, which is the part that matters -- without
            // that, the next real hit reads as a hit for the entire difference.
            if (lost < MinimumDamage)
            {
                NoteDown(victim, ped, now);
                return;
            }

            var fatal = ped.IsDead || health <= 0f;

            var hit = new Hit
            {
                Victim = victim,
                Ped = ped,
                Damage = lost,
                Fatal = fatal,
                Group = WeaponUsed(ped, out var weaponHash),
                WeaponHash = weaponHash
            };

            Locate(ref hit, ped);

            _hits.Add(hit);

            // Anything that hurt enough to draw for is enough to start them bleeding. The
            // window is refreshed rather than added to, so being shot eight times does not
            // leave somebody dripping for a minute and a half.
            var seconds = fatal ? 6f : Math.Min(20f, 4f + lost * 0.25f);
            victim.BleedUntil = now + (int)(seconds * 1000f);

            NoteDown(victim, ped, now);
        }

        /// <summary>Records the moment a ped went down, which is when their pool starts.</summary>
        private static void NoteDown(Victim victim, Ped ped, int now)
        {
            if (victim.DownAt != 0) return;

            bool down;
            try { down = ped.IsDead || (!victim.IsPlayer && ped.IsInjured && !ped.IsAlive); }
            catch { return; }

            if (!down) return;

            victim.DownAt = now;
            victim.PoolAt = ped.Position;
        }

        /// <summary>
        /// Works out where the hit landed and which way the blood should go.
        ///
        /// The impact position is the game's own record of where the last round struck this
        /// ped. It is the entry point, so the line from the ped's middle to it points BACK at
        /// whoever fired -- and the exit, which is where a spray belongs, is the other way.
        /// That is the whole derivation, and it works whoever pulled the trigger, which an
        /// attacker lookup does not.
        ///
        /// When the game has no impact on file -- melee, explosions, a fall -- the direction
        /// falls back to away from the player, which is right often enough and never absurd.
        /// </summary>
        private static void Locate(ref Hit hit, Ped ped)
        {
            var centre = ped.Position + new Vector3(0f, 0f, 0.6f);
            hit.Position = centre;
            hit.Direction = ped.ForwardVector;
            hit.Zone = PedDamageZone.Torso;

            try
            {
                var bone = ped.Bones.LastDamaged;

                if (bone != null && bone.IsValid)
                {
                    hit.Bone = bone;
                    hit.Zone = ZoneOf(bone.Tag);
                    hit.Headshot = hit.Zone == PedDamageZone.Head;
                }
            }
            catch
            {
                // No damage bone on record; the zone stays Torso.
            }

            try
            {
                var impact = ped.LastWeaponImpactPosition;

                // Zero means "never hit", and anything metres away belongs to a different
                // event -- both would aim the spray at nothing in particular.
                if (impact != Vector3.Zero && impact.DistanceTo(ped.Position) < 3f)
                {
                    hit.Position = impact;

                    var through = centre - impact;
                    through.Z *= 0.35f;

                    if (through.LengthSquared() > 0.0025f)
                    {
                        hit.Direction = through.Normalized;
                        return;
                    }
                }
            }
            catch
            {
                // Fall through to the player-relative guess.
            }

            try
            {
                var player = Game.Player.Character;

                if (player != null && player.Exists() && player.Handle != ped.Handle)
                {
                    var away = ped.Position - player.Position;
                    away.Z = 0f;

                    if (away.LengthSquared() > 0.01f) hit.Direction = away.Normalized;
                }
            }
            catch
            {
                // Keep the forward vector.
            }
        }

        /// <summary>Which damage zone a bone belongs to.</summary>
        private static PedDamageZone ZoneOf(Bone tag)
        {
            switch (tag)
            {
                case Bone.SkelHead:
                case Bone.SkelNeck1:
                case Bone.SkelNeck2:
                case Bone.FacialForehead:
                    return PedDamageZone.Head;

                case Bone.SkelLeftUpperArm:
                case Bone.SkelLeftForearm:
                case Bone.SkelLeftHand:
                case Bone.SkelLeftClavicle:
                    return PedDamageZone.LeftArm;

                case Bone.SkelRightUpperArm:
                case Bone.SkelRightForearm:
                case Bone.SkelRightHand:
                case Bone.SkelRightClavicle:
                    return PedDamageZone.RightArm;

                case Bone.SkelLeftThigh:
                case Bone.SkelLeftCalf:
                case Bone.SkelLeftFoot:
                case Bone.SkelLeftToe0:
                    return PedDamageZone.LeftLeg;

                case Bone.SkelRightThigh:
                case Bone.SkelRightCalf:
                case Bone.SkelRightFoot:
                case Bone.SkelRightToe0:
                    return PedDamageZone.RightLeg;

                default:
                    return PedDamageZone.Torso;
            }
        }

        /// <summary>
        /// What did the damage, as far as anything can tell.
        ///
        /// GET_PED_CAUSE_OF_DEATH is the only weapon the game will name for a ped, and it is
        /// only filled in once they are dead. For a ped still standing the player's own weapon
        /// is the honest guess -- this mod exists for the player's gunfights, and when somebody
        /// else did it the worst case is a pistol-sized mess instead of a shotgun-sized one.
        /// </summary>
        private static WeaponGroup WeaponUsed(Ped ped, out uint weaponHash)
        {
            weaponHash = 0;

            try
            {
                if (ped.IsDead)
                {
                    var cause = Function.Call<uint>(Hash.GET_PED_CAUSE_OF_DEATH, ped.Handle);

                    if (cause != 0)
                    {
                        weaponHash = cause;
                        var group = Function.Call<int>(Hash.GET_WEAPONTYPE_GROUP, cause);
                        if (group != 0) return (WeaponGroup)group;
                    }
                }

                var player = Game.Player.Character;

                if (player != null && player.Exists() && player.Handle != ped.Handle)
                {
                    var current = player.Weapons.Current;
                    if (weaponHash == 0) weaponHash = (uint)current.Hash;
                    return current.Group;
                }
            }
            catch
            {
                // Nothing worth logging every frame of a firefight.
            }

            return WeaponGroup.Unarmed;
        }

        private static float SafeHealth(Ped ped)
        {
            try { return ped.HealthFloat; }
            catch { return 0f; }
        }

        /// <summary>
        /// Forgets peds who have wandered off or been cleaned up by the game.
        ///
        /// A dictionary keyed on ped handles is a slow leak if nothing prunes it: handles are
        /// reused, so a stale entry does not just waste memory, it eventually attaches one
        /// ped's bleeding to a completely different ped.
        /// </summary>
        private void DropStale()
        {
            _stale.Clear();

            // WHICH PED IS THE PLAYER RIGHT NOW, rather than which one used to be. Switching
            // character leaves the previous one in here flagged as the player, and because
            // handles are reused that flag eventually lands on somebody else entirely -- an
            // NPC who then gets treated as the player by every check that asks.
            var playerHandle = 0;

            try
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists()) playerHandle = player.Handle;
            }
            catch
            {
                // Without a player handle, nothing is exempt, which is the safe way round.
            }

            foreach (var pair in _victims)
            {
                var victim = pair.Value;

                if (pair.Key == playerHandle) continue;
                if (_frame - victim.LastSeen <= StaleFrames) continue;

                _stale.Add(pair.Key);
            }

            foreach (var handle in _stale) _victims.Remove(handle);
        }

        /// <summary>Drops everything. Called on reload so a new session starts clean.</summary>
        public void Clear()
        {
            _victims.Clear();
            _hits.Clear();
        }
    }
}
