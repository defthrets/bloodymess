using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>
    /// Tyre tracks out of a pool.
    ///
    /// The same idea as the footprints and, deliberately, the same rules: a wheel that rolls
    /// through wet blood picks some up and lays it back down over the next few metres. It reads
    /// as the obvious consequence of driving through a scene, and the stock game does nothing
    /// at all about it -- you can park in a pool and drive away on clean tyres.
    ///
    /// Tracked separately from the peds rather than folded into Victims, because a vehicle is
    /// not a victim and the only thing this needs to know about one is where its wheels are.
    /// The tracking is deliberately thin: a handful of nearby vehicles, a charge each, dropped
    /// the moment they are out of range.
    /// </summary>
    internal sealed class Wheels
    {
        /// <summary>The wheel bones, in the game's own naming. Missing ones are simply skipped.</summary>
        private static readonly string[] WheelBones =
        {
            "wheel_lf", "wheel_rf", "wheel_lr", "wheel_rr"
        };

        /// <summary>How many vehicles are watched at once. A car park is not worth the rays.</summary>
        private const int MaxVehicles = 5;

        /// <summary>How far a vehicle is watched from. Shorter than the ped range; tracks are small.</summary>
        private const float Range = 40f;

        private const int RescanMs = 500;

        private sealed class Tracked
        {
            public Vehicle Vehicle;
            public int PrintsLeft;
            public int PrintsAtPickup = 1;
            public Vector3 LastTrackAt;
        }

        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly BloodField _field;

        private readonly Dictionary<int, Tracked> _vehicles = new Dictionary<int, Tracked>();
        private readonly List<int> _stale = new List<int>();

        private int _lastScan;

        public Wheels(Settings cfg, Decals decals, BloodField field)
        {
            _cfg = cfg;
            _decals = decals;
            _field = field;
        }

        public void Update()
        {
            if (!_cfg.WheelTracksEnabled) return;

            var now = Game.GameTime;

            if (now - _lastScan >= RescanMs)
            {
                _lastScan = now;
                Rescan();
            }

            foreach (var tracked in _vehicles.Values)
            {
                Roll(tracked);
            }
        }

        private void Rescan()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                var nearby = World.GetNearbyVehicles(player, Range);
                var kept = 0;

                _stale.Clear();
                foreach (var handle in _vehicles.Keys) _stale.Add(handle);

                foreach (var vehicle in nearby)
                {
                    if (vehicle == null || !vehicle.Exists()) continue;
                    if (kept >= MaxVehicles) break;

                    kept++;
                    _stale.Remove(vehicle.Handle);

                    if (_vehicles.TryGetValue(vehicle.Handle, out var existing))
                    {
                        existing.Vehicle = vehicle;
                        continue;
                    }

                    _vehicles[vehicle.Handle] = new Tracked
                    {
                        Vehicle = vehicle,
                        LastTrackAt = vehicle.Position
                    };
                }

                // Anything not seen this sweep is out of range or gone. Dropped rather than
                // kept, because handles get reused and a stale entry would eventually put one
                // car's bloody tyres on another car.
                foreach (var handle in _stale) _vehicles.Remove(handle);
            }
            catch (Exception ex)
            {
                Log.Once("wheel-scan", "Vehicle scan failed: " + ex.Message);
            }
        }

        private void Roll(Tracked tracked)
        {
            var vehicle = tracked.Vehicle;
            if (vehicle == null || !vehicle.Exists()) return;

            var wheels = WheelPositions(vehicle);
            if (wheels.Count == 0) return;

            PickUp(tracked, wheels);

            if (tracked.PrintsLeft <= 0) return;

            var position = vehicle.Position;
            var travelled = position.DistanceTo2D(tracked.LastTrackAt);

            // A wider spacing than a footstep: a tyre lays a continuous smear rather than
            // discrete marks, and one decal per metre is enough to read as one.
            if (travelled < 1.2f) return;

            if (travelled > 40f)
            {
                tracked.LastTrackAt = position;
                return;
            }

            tracked.LastTrackAt = position;

            var remaining = (float)tracked.PrintsLeft / Math.Max(1, tracked.PrintsAtPickup);
            var opacity = _cfg.WheelTrackOpacity * (0.15f + remaining * 0.85f);
            var heading = vehicle.Heading * (float)(Math.PI / 180.0);

            var laid = false;

            foreach (var wheel in wheels)
            {
                if (!_decals.CanAfford(wheel)) break;

                var ground = Ground.Probe(wheel, 1.5f, vehicle);
                if (!ground.Found) continue;

                // Long and narrow, along the direction of travel. A square decal under a wheel
                // looks like a stain somebody parked on, not like a track.
                var handle = _decals.OnGround(
                    Decals.Lane.Splatter, DecalType.SplattersBlood, ground.Position, heading,
                    _cfg.WheelTrackSize * 0.5f, _cfg.WheelTrackSize * 2f, opacity,
                    new Tint(_cfg.BloodRed, _cfg.BloodGreen, _cfg.BloodBlue));

                if (handle != 0) laid = true;
            }

            if (laid) tracked.PrintsLeft--;
        }

        private void PickUp(Tracked tracked, List<Vector3> wheels)
        {
            var wetness = 0f;

            foreach (var wheel in wheels)
            {
                var here = _field.WetnessAt(wheel, 0.6f);
                if (here > wetness) wetness = here;
            }

            if (wetness < 0.2f) return;

            var picked = (int)Math.Round(_cfg.WheelTrackSteps * _cfg.Scale * wetness);
            if (picked <= tracked.PrintsLeft) return;

            tracked.PrintsLeft = picked;
            tracked.PrintsAtPickup = picked;
        }

        private static readonly List<Vector3> Scratch = new List<Vector3>(4);

        /// <summary>
        /// Where this vehicle's wheels are.
        ///
        /// By bone name, and a name that is not on this model is skipped rather than guessed
        /// at -- bikes have two wheels and some vehicles name theirs differently, so anything
        /// that assumes four gets it wrong on a motorbike.
        /// </summary>
        private static List<Vector3> WheelPositions(Vehicle vehicle)
        {
            Scratch.Clear();

            try
            {
                foreach (var name in WheelBones)
                {
                    var bone = vehicle.Bones[name];
                    if (bone == null || !bone.IsValid) continue;

                    Scratch.Add(bone.Position);
                }
            }
            catch
            {
                Scratch.Clear();
            }

            return Scratch;
        }

        public void Clear()
        {
            _vehicles.Clear();
        }
    }
}
