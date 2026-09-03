using System;
using GTA;
using BloodyMess.Core;
using BloodyMess.Gore;
using BloodyMess.UI;

namespace BloodyMess
{
    /// <summary>
    /// Script entry point and the only owner of the update loop.
    ///
    /// ONE Script subclass, deliberately. SHVDN instantiates every Script it finds and ticks
    /// them in an order it does not define; a single entry point means the order our own
    /// subsystems run in is ours to decide, and there is exactly one place that has to be
    /// exception-safe.
    ///
    /// The order below is not arbitrary. Hits are detected first, because everything else in
    /// the frame is a reaction to one. Wounds and spray consume those hits. Pools and drips
    /// then run off the state the hits left behind, and both of them put blood on the ground.
    /// Footprints and tyre tracks run LAST, because they read that ground -- run earlier, they
    /// would be reading the previous frame's blood and a player standing in a pool as it forms
    /// would walk out of it clean.
    /// </summary>
    public sealed class Main : Script
    {
        /// <summary>Consecutive tick failures before the script parks itself rather than spamming.</summary>
        private const int MaxConsecutiveFailures = 10;

        /// <summary>
        /// Core.Settings, spelt out in full every time.
        ///
        /// Script -- the SHVDN base class -- has its own inherited Settings property, and it
        /// shadows our type in expression position. Written bare, Settings.Load() does not
        /// compile and the error points at something else entirely.
        /// </summary>
        private readonly Core.Settings _cfg;

        private readonly Profiles _profiles;
        private readonly Decals _decals;
        private readonly BloodField _field;
        private readonly Victims _victims;

        private readonly Globals _globals;
        private readonly Wounds _wounds;
        private readonly Heads _heads;
        private readonly Legs _legs;
        private readonly Spray _spray;
        private readonly Pools _pools;
        private readonly Drips _drips;
        private readonly Footprints _footprints;
        private readonly Wheels _wheels;

        private readonly SettingsPanel _settings;

        private int _failures;
        private bool _parked;
        private bool _greeted;

        public Main()
        {
            _cfg = Core.Settings.Load();

            _profiles = new Profiles();
            _profiles.Load();

            _decals = new Decals(_cfg);
            _field = new BloodField();
            _victims = new Victims(_cfg);

            _globals = new Globals(_cfg);
            _wounds = new Wounds(_cfg, _profiles);
            _heads = new Heads(_cfg, _profiles);
            _legs = new Legs(_cfg);
            _spray = new Spray(_cfg, _decals, _profiles, _field);
            _pools = new Pools(_cfg, _decals, _field);
            _drips = new Drips(_cfg, _decals, _field);
            _footprints = new Footprints(_cfg, _decals, _field);
            _wheels = new Wheels(_cfg, _decals, _field);

            _settings = new SettingsPanel(_cfg, _decals, _field, _footprints, _globals, _spray);

            Interval = 0;
            Tick += OnTick;
            Aborted += OnAborted;

            Log.Info(Build.Name + " " + Build.Version + " loaded. Menu " + _cfg.MenuKey +
                     ", level " + _cfg.Level + " (x" + _cfg.Scale.ToString("0.00") + "), " +
                     "budget " + _cfg.MaxSplatters + " splatters / " + _cfg.MaxPools + " pools.");

            if (!_cfg.Enabled)
            {
                Log.Warn("[General] Enabled is false - nothing will run until it is turned on.");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_parked) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Greet();

                // THE SETTINGS MENU RUNS EVEN WHEN THE MOD IS SWITCHED OFF, and it has to:
                // "Mod enabled" is a row inside it, so gating it behind that flag would make
                // turning the mod off a one-way trip that could only be undone by editing the
                // ini and reloading scripts.
                _settings.Update();

                if (!_cfg.Enabled) return;

                _decals.Tick();
                _decals.Prune();
                _field.Sweep();

                _globals.Update();
                _wounds.KeepAssetLoaded();

                _victims.Update();

                foreach (var hit in _victims.Hits)
                {
                    // BEFORE the wounds, so a head that is about to be removed does not get
                    // blood decals stamped onto it first.
                    var headless = _heads.Try(hit);

                    // Before the wounds, like the head, so the decision about whether they
                    // survive is made before anything is drawn on them.
                    if (!headless) _legs.Try(hit);

                    if (!headless) _wounds.Apply(hit);

                    _spray.Throw(hit);
                }

                // Drains the queued ground splatters a few at a time. See Spray._pending:
                // placing them all in the frame the shot landed spiked the frame time.
                _spray.Update();

                _legs.Update(_victims);
                _pools.Update(_victims);
                _drips.Update(_victims);

                // Last, and see the note on the class: these two read the ground that the
                // three above have just finished writing to.
                _footprints.Update(_victims);
                _wheels.Update();

                _failures = 0;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>Says hello once, after the game is running rather than in the constructor.</summary>
        private void Greet()
        {
            if (_greeted) return;
            _greeted = true;

            if (!_cfg.AnnounceOnLoad) return;

            try
            {
                // NAMES THE KEY, like every other mod in this scripts\ folder does -- Hoodrich
                // says F2, Overspray F3, Bare Minimum F7. A load message that only gives a
                // version tells somebody nothing they can act on.
                GTA.UI.Notification.PostTicker(
                    "~r~" + Build.Name + " " + Build.Version + " - by " + Build.By + "~s~ loaded.  " +
                    "Press ~r~" + _cfg.MenuKey + "~s~ for settings.", false, false);
            }
            catch
            {
                // Not being able to say hello is not a reason to stop.
            }
        }

        private void Fail(Exception ex)
        {
            _failures++;
            Log.Error("Tick failed (" + _failures + "/" + MaxConsecutiveFailures + ")", ex);

            if (_failures < MaxConsecutiveFailures) return;

            _parked = true;
            Log.Error("Ten ticks in a row have failed. " + Build.Name +
                      " has stopped itself rather than keep throwing. See above for the cause.");

            try
            {
                GTA.UI.Notification.PostTicker(
                    "~r~" + Build.Name + " stopped~s~ - see " + Paths.Stem + ".log.", false, false);
            }
            catch
            {
                // Nothing further to try.
            }

            Cleanup();
        }

        private void OnAborted(object sender, EventArgs e)
        {
            Cleanup();
        }

        /// <summary>
        /// Leaves the game exactly as it was found.
        ///
        /// Runs on a reload as well as on shutdown, because SHVDN reloads scripts on a
        /// keypress. THE DECALS ARE DELIBERATELY LEFT WHERE THEY ARE: a scene the player made
        /// is theirs, and wiping the street clean every time somebody reloads a script would
        /// be the mod undoing its own work. What is handed back is everything that would
        /// otherwise outlive the mod and could not be undone by hand -- the engine's blood
        /// switches, and the global footstep effect override.
        /// </summary>
        private void Cleanup()
        {
            try { _settings.Shutdown(); } catch (Exception ex) { Log.Error("Settings shutdown", ex); }
            try { _footprints.Shutdown(); } catch (Exception ex) { Log.Error("Footprint shutdown", ex); }
            try { _wounds.Shutdown(); } catch (Exception ex) { Log.Error("Particle shutdown", ex); }
            try { _heads.Shutdown(); } catch (Exception ex) { Log.Error("Head shutdown", ex); }
            try { _globals.Restore(); } catch (Exception ex) { Log.Error("Restoring engine settings", ex); }
            try { _victims.Clear(); } catch (Exception ex) { Log.Error("Clearing victims", ex); }
            try { _wheels.Clear(); } catch (Exception ex) { Log.Error("Clearing vehicles", ex); }

            Log.Info(Build.Name + " stopped cleanly.");
        }
    }
}
