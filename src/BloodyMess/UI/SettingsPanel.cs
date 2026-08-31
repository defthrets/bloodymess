using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using GTA;
using BloodyMess.Core;
using BloodyMess.Gore;

namespace BloodyMess.UI
{
    /// <summary>
    /// The settings menu, on F10 by default.
    ///
    /// THIS MENU IS THE POINT OF THE MOD, not a nicety on the side. The gore mod that used to
    /// be installed on this machine shipped no configuration whatsoever -- its bleed loop was
    /// hardcoded, everybody bled forever, and the only way to turn it down was to delete the
    /// dll. Every system here can be switched off from this screen, live, while looking at
    /// what it does.
    ///
    /// Changes apply immediately and write themselves back to BloodyMess.ini. The write is
    /// careful with the file rather than casual: only keys that actually changed are written,
    /// one line each, so the page of comments in the ini survives being edited from in game.
    /// </summary>
    internal sealed class SettingsPanel
    {
        /// <summary>One row.</summary>
        private sealed class Option
        {
            public string Name = "";
            public string Note = "";

            /// <summary>A heading. Holds nothing, does nothing, and the selection skips it.</summary>
            public bool Heading;

            /// <summary>Where it lives in the ini. Empty for an action row.</summary>
            public string Section = "";
            public string Key = "";

            /// <summary>The value as the player sees it.</summary>
            public Func<string> Show;

            /// <summary>Called with -1 or +1. Null for an action row.</summary>
            public Action<int> Nudge;

            /// <summary>The value as the ini should hold it. Null for a row that saves nothing.</summary>
            public Func<string> Persist;

            /// <summary>For a row that DOES something rather than holds a value.</summary>
            public Action Activate;
        }

        private const float Left = 0.015f;
        private const float Top = 0.14f;
        private const float Width = 0.28f;
        private const float RowHeight = 0.028f;

        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly BloodField _field;
        private readonly Footprints _footprints;
        private readonly Globals _globals;
        private readonly Spray _spray;

        private readonly List<Option> _options = new List<Option>();

        /// <summary>Keys whose ini value has changed since the last write.</summary>
        private readonly HashSet<Option> _dirty = new HashSet<Option>();

        private bool _open;
        private int _selected;

        private bool _menuKeyDown;
        private bool _upDown, _downDown, _leftDown, _rightDown, _enterDown, _backDown;

        /// <summary>Game time of the last change, so the write waits for the player to stop.</summary>
        private int _lastChange;

        /// <summary>Set when a write fails, so it is not retried every second forever.</summary>
        private bool _writeGaveUp;

        private string _flash = "";
        private int _flashUntil;

        public SettingsPanel(Settings cfg, Decals decals, BloodField field,
                             Footprints footprints, Globals globals, Spray spray)
        {
            _cfg = cfg;
            _decals = decals;
            _field = field;
            _footprints = footprints;
            _globals = globals;
            _spray = spray;

            BuildOptions();
        }

        public bool IsOpen => _open;

        public void Update()
        {
            if (Toggled())
            {
                _open = !_open;
                if (!_open) Flush();
            }

            if (!_open)
            {
                // The write is deferred rather than done per keypress: holding a direction
                // fires a nudge every few frames and each write re-reads and rewrites the
                // whole ini. Flushed a moment after the player stops.
                if (_dirty.Count > 0 && Game.GameTime - _lastChange > 1200) Flush();
                return;
            }

            Input();
            Suppress();
            Render();
        }

        // ---- input -----------------------------------------------------------

        /// <summary>
        /// Edge-detects the menu key.
        ///
        /// Game.IsKeyPressed is a LEVEL, not an edge: held for a fifth of a second it is true
        /// across a dozen frames, which would open and close the menu repeatedly for as long
        /// as the key is held down.
        /// </summary>
        private bool Toggled()
        {
            bool down;

            try { down = Game.IsKeyPressed(_cfg.MenuKey); }
            catch { return false; }

            var edge = down && !_menuKeyDown;
            _menuKeyDown = down;
            return edge;
        }

        private void Input()
        {
            if (Edge(Keys.Up, ref _upDown)) Move(-1);
            if (Edge(Keys.Down, ref _downDown)) Move(1);
            if (Edge(Keys.Left, ref _leftDown)) Change(-1);
            if (Edge(Keys.Right, ref _rightDown)) Change(1);
            if (Edge(Keys.Enter, ref _enterDown)) Activate();

            if (Edge(Keys.Back, ref _backDown))
            {
                _open = false;
                Flush();
            }
        }

        private static bool Edge(Keys key, ref bool wasDown)
        {
            bool down;

            try { down = Game.IsKeyPressed(key); }
            catch { return false; }

            var edge = down && !wasDown;
            wasDown = down;
            return edge;
        }

        private void Move(int step)
        {
            if (_options.Count == 0) return;

            for (var attempts = 0; attempts < _options.Count; attempts++)
            {
                _selected += step;

                if (_selected < 0) _selected = _options.Count - 1;
                if (_selected >= _options.Count) _selected = 0;

                if (!_options[_selected].Heading) return;
            }
        }

        private void Change(int direction)
        {
            if (_selected < 0 || _selected >= _options.Count) return;

            var option = _options[_selected];
            if (option.Nudge == null) return;

            option.Nudge(direction);

            if (option.Persist != null && !string.IsNullOrEmpty(option.Key)) _dirty.Add(option);
            _lastChange = Game.GameTime;

            // Engine settings have to be pushed out again to take effect; the rest are read
            // straight off the settings object by whichever system owns them.
            _globals.Apply();
        }

        private void Activate()
        {
            if (_selected < 0 || _selected >= _options.Count) return;

            var option = _options[_selected];

            if (option.Activate != null) { option.Activate(); return; }

            // Enter on a switch is the same as pushing it right. Nobody should have to know
            // which key a boolean wants.
            Change(1);
        }

        /// <summary>
        /// Blocks the game controls the menu is using, one frame at a time.
        ///
        /// Per frame and per control rather than DisableAllControlsThisFrame, which also
        /// disables looking around, the pause menu and the phone -- a menu that takes the
        /// whole game hostage to read six switches is a menu people close in irritation.
        /// </summary>
        private static void Suppress()
        {
            GTA.Control[] blocked =
            {
                GTA.Control.Phone, GTA.Control.SelectWeapon, GTA.Control.Attack,
                GTA.Control.Attack2, GTA.Control.Aim, GTA.Control.MeleeAttack1,
                GTA.Control.CharacterWheel, GTA.Control.VehicleCinCam
            };

            foreach (var control in blocked)
            {
                try { Game.DisableControlThisFrame(control); }
                catch { /* not fatal */ }
            }
        }

        // ---- drawing ---------------------------------------------------------

        private void Render()
        {
            var rows = _options.Count;
            var height = RowHeight * (rows + 3);

            Draw.Bar(Left, Top - RowHeight * 2f, Width, height, Color.FromArgb(215, 8, 4, 4));
            Draw.Bar(Left, Top - RowHeight * 2f, Width, 0.004f, Color.FromArgb(255, 150, 12, 12));

            Draw.Text(Build.Name + " " + Build.Version, Left + 0.008f, Top - RowHeight * 1.8f,
                      0.34f, Color.FromArgb(255, 220, 60, 60));

            Draw.Text(_decals.LiveSplatters + "/" + _cfg.MaxSplatters + " splats  " +
                      _decals.LivePools + "/" + _cfg.MaxPools + " pools",
                      Left + Width - 0.008f, Top - RowHeight * 1.8f, 0.28f,
                      Color.FromArgb(190, 170, 170, 170), 4, false, true);

            // THE LINE THAT ANSWERS "why is there no blood on the ground".
            //
            // Three completely different failures look identical in game: nothing was
            // attempted, the budget refused it, or no ground was found under the drop.
            // Reading these three numbers after a shot says which, immediately, instead of
            // guessing at it from a screenshot.
            Draw.Text("laid " + _spray.Placed +
                      "   no-ground " + _spray.NoGround +
                      "   over-budget " + _spray.Refused +
                      "   wet spots " + _field.Count,
                      Left + 0.008f, Top - RowHeight * 1.1f, 0.24f,
                      Color.FromArgb(170, 150, 150, 150));

            var y = Top;

            for (var i = 0; i < rows; i++)
            {
                var option = _options[i];

                if (option.Heading)
                {
                    Draw.Text(option.Name.ToUpperInvariant(), Left + 0.008f, y + 0.004f, 0.26f,
                              Color.FromArgb(200, 190, 90, 90));
                    y += RowHeight;
                    continue;
                }

                var chosen = i == _selected;

                if (chosen)
                {
                    Draw.Bar(Left, y, Width, RowHeight, Color.FromArgb(90, 190, 30, 30));
                }

                var colour = chosen ? Color.White : Color.FromArgb(225, 205, 205, 205);

                Draw.Text((chosen ? "> " : "  ") + option.Name,
                          Left + 0.008f, y + 0.003f, 0.3f, colour);

                if (option.Show != null)
                {
                    Draw.Text(option.Show(), Left + Width - 0.008f, y + 0.003f, 0.3f,
                              chosen ? Color.FromArgb(255, 255, 190, 190)
                                     : Color.FromArgb(210, 180, 180, 180),
                              4, false, true);
                }

                y += RowHeight;
            }

            Draw.Text(Footer(), Left + 0.008f, y + 0.006f, 0.26f,
                      Color.FromArgb(180, 160, 160, 160));
        }

        private string Footer()
        {
            if (Game.GameTime < _flashUntil && !string.IsNullOrEmpty(_flash)) return _flash;

            if (_writeGaveUp) return "Cannot write the ini - changes apply but will not stick";
            if (_dirty.Count > 0) return "arrows change  |  enter picks  |  backspace closes";

            return "arrows change  |  enter picks  |  backspace closes";
        }

        private void Flash(string message)
        {
            _flash = message;
            _flashUntil = Game.GameTime + 2500;
        }

        // ---- saving ----------------------------------------------------------

        /// <summary>
        /// Writes the changed keys, once, and stops trying if the file will not take them.
        ///
        /// Giving up rather than retrying is deliberate: the likeliest reason a write fails is
        /// the game folder being read-only, and that does not fix itself between frames.
        /// </summary>
        private void Flush()
        {
            if (_dirty.Count == 0 || _writeGaveUp) { _dirty.Clear(); return; }

            var written = 0;

            foreach (var option in _dirty)
            {
                if (option.Persist == null) continue;

                if (Settings.Save(option.Section, option.Key, option.Persist()))
                {
                    written++;
                    continue;
                }

                _writeGaveUp = true;
                Log.Warn("Could not write " + option.Section + "." + option.Key +
                         " to the ini. Settings will apply but not survive a restart.");
                break;
            }

            _dirty.Clear();

            if (written > 0) Log.Debug("Saved " + written + " setting(s) to the ini.");
        }

        // ---- the options themselves -------------------------------------------

        private void BuildOptions()
        {
            Heading("General");

            Switch("Mod enabled", "General", "Enabled",
                   () => _cfg.Enabled, v => _cfg.Enabled = v);

            _options.Add(new Option
            {
                Name = "Gore level",
                Section = "Intensity",
                Key = "Level",
                Show = () => _cfg.Level.ToString(),
                Nudge = direction =>
                {
                    var values = (GoreLevel[])Enum.GetValues(typeof(GoreLevel));
                    var index = Array.IndexOf(values, _cfg.Level) + direction;

                    if (index < 0) index = values.Length - 1;
                    if (index >= values.Length) index = 0;

                    _cfg.Level = values[index];
                },
                Persist = () => _cfg.Level.ToString()
            });

            Number("Fine trim", "Intensity", "Multiplier",
                   () => _cfg.Multiplier, v => _cfg.Multiplier = v, 0.1f, 5f, 0.1f);

            Heading("Systems");

            Switch("Wounds on bodies", "Wounds", "Enabled",
                   () => _cfg.WoundsEnabled, v => _cfg.WoundsEnabled = v);
            Switch("Blood on the player", "Wounds", "PlayerWounds",
                   () => _cfg.PlayerWounds, v => _cfg.PlayerWounds = v);
            Switch("Spray and splatter", "Spray", "Enabled",
                   () => _cfg.SprayEnabled, v => _cfg.SprayEnabled = v);
            Switch("Spray onto walls", "Spray", "OnWalls",
                   () => _cfg.SprayOnWalls, v => _cfg.SprayOnWalls = v);
            Switch("Pools under bodies", "Pools", "Enabled",
                   () => _cfg.PoolsEnabled, v => _cfg.PoolsEnabled = v);
            Switch("Trails from the wounded", "Drips", "Enabled",
                   () => _cfg.DripsEnabled, v => _cfg.DripsEnabled = v);

            Heading("Footprints");

            Switch("Footprints", "Footprints", "Enabled",
                   () => _cfg.FootprintsEnabled, v => _cfg.FootprintsEnabled = v);
            Switch("...for the player", "Footprints", "ForPlayer",
                   () => _cfg.FootprintsForPlayer, v => _cfg.FootprintsForPlayer = v);
            Switch("...for everyone else", "Footprints", "ForNpcs",
                   () => _cfg.FootprintsForNpcs, v => _cfg.FootprintsForNpcs = v);
            Number("Prints per puddle", "Footprints", "Steps",
                   () => _cfg.FootprintSteps, v => _cfg.FootprintSteps = (int)v, 1f, 60f, 1f);
            Switch("Tyre tracks", "Wheels", "Enabled",
                   () => _cfg.WheelTracksEnabled, v => _cfg.WheelTracksEnabled = v);

            Heading("Engine");

            Switch("Bigger blood particles", "Game", "BiggerBloodParticles",
                   () => _cfg.BiggerBloodParticles, v => _cfg.BiggerBloodParticles = v);
            Switch("Shotgun spread decals", "Game", "ShotgunDecals",
                   () => _cfg.ShotgunDecals, v => _cfg.ShotgunDecals = v);
            Switch("Clown blood", "Game", "ClownBlood",
                   () => _cfg.ClownBlood, v => _cfg.ClownBlood = v);
            Switch("Alien blood", "Game", "AlienBlood",
                   () => _cfg.AlienBlood, v => _cfg.AlienBlood = v);

            Heading("Budget");

            Number("Max splatters", "Budget", "MaxSplatters",
                   () => _cfg.MaxSplatters, v => _cfg.MaxSplatters = (int)v, 8f, 900f, 10f);
            Number("Max pools", "Budget", "MaxPools",
                   () => _cfg.MaxPools, v => _cfg.MaxPools = (int)v, 0f, 300f, 5f);

            _options.Add(new Option
            {
                Name = "Clear all blood now",
                Show = () => _footprints.Printed + " prints laid",
                Activate = () =>
                {
                    var removed = _decals.ClearAll();
                    _field.Clear();
                    Flash("Removed " + removed + " decal(s).");
                }
            });

            // The selection must not start on a heading, which the first row always is.
            Move(1);
        }

        private void Heading(string name)
        {
            _options.Add(new Option { Name = name, Heading = true });
        }

        private void Switch(string name, string section, string key,
                            Func<bool> read, Action<bool> write)
        {
            _options.Add(new Option
            {
                Name = name,
                Section = section,
                Key = key,
                Show = () => read() ? "ON" : "off",
                Nudge = _ => write(!read()),
                Persist = () => read() ? "true" : "false"
            });
        }

        private void Number(string name, string section, string key,
                            Func<float> read, Action<float> write,
                            float min, float max, float step)
        {
            _options.Add(new Option
            {
                Name = name,
                Section = section,
                Key = key,
                Show = () => Format(read(), step),
                Nudge = direction =>
                {
                    var value = read() + step * direction;

                    if (value < min) value = min;
                    if (value > max) value = max;

                    // ROUNDED AFTER EVERY NUDGE. Repeated floating-point addition drifts, and
                    // a setting that reads 0.7000001 in the ini is a setting somebody will
                    // eventually file a bug about.
                    write((float)Math.Round(value, 3));
                },
                Persist = () => Format(read(), step)
            });
        }

        private static string Format(float value, float step)
        {
            return step >= 1f
                ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Writes anything outstanding. Called on shutdown and on a script reload.</summary>
        public void Shutdown()
        {
            _open = false;
            Flush();
        }
    }
}
