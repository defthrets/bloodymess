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
    /// The settings menu, on F10 or by holding the controller's View/Back button.
    ///
    /// THIS MENU IS THE POINT OF THE MOD, not a nicety on the side. The gore mod that used to
    /// be installed on this machine shipped no configuration whatsoever -- its bleed loop was
    /// hardcoded, everybody bled forever, and the only way to turn it down was to delete the
    /// dll. Every system here can be switched off from this screen, live, while looking at
    /// what it does.
    ///
    /// TABS RATHER THAN ONE LONG LIST. Twenty-odd rows in a single column meant scrolling past
    /// things you did not care about to reach the four you did, and it made the panel tall
    /// enough to cover the scene you were trying to judge. Six short tabs of four to six rows
    /// each fit on screen at once, so nothing scrolls and the panel stays out of the way.
    ///
    /// Left and right are already spoken for -- they change the value on the selected row --
    /// so tabs are switched with Q and E, or the shoulder buttons on a pad.
    ///
    /// THE ICONS ARE DRAWN FROM RECTANGLES, not from a font and not from image files. GTA's
    /// HUD font has no reliable glyph coverage for geometric symbols, so they come out blank
    /// or as the wrong character depending on which font is in use; and shipping PNGs would
    /// give this mod the asset dependency it deliberately does not have. Four or five
    /// DRAW_RECT calls per icon always render, on every machine, and cost nothing.
    ///
    /// Changes apply immediately and write themselves back to BloodyMess.ini. Only keys that
    /// actually changed are written, one line each, so the page of comments in the ini
    /// survives being edited from in game.
    /// </summary>
    internal sealed class SettingsPanel
    {
        // ---- shape -----------------------------------------------------------

        private const float Left = 0.018f;
        private const float Top = 0.13f;
        private const float Width = 0.30f;

        private const float RowHeight = 0.030f;
        private const float HeaderHeight = 0.070f;
        private const float TabStripHeight = 0.040f;
        private const float FooterHeight = 0.030f;

        /// <summary>Seconds the open/close slide takes.</summary>
        private const float OpenSeconds = 0.16f;

        /// <summary>How quickly the moving parts chase their targets. Higher is snappier.</summary>
        private const float Chase = 14f;

        // ---- palette ---------------------------------------------------------

        private static readonly Color Backdrop = Color.FromArgb(232, 10, 6, 6);
        private static readonly Color Accent = Color.FromArgb(255, 168, 22, 22);
        private static readonly Color AccentDim = Color.FromArgb(120, 120, 18, 18);
        private static readonly Color TextBright = Color.FromArgb(255, 240, 236, 236);
        private static readonly Color TextNormal = Color.FromArgb(225, 196, 190, 190);
        private static readonly Color TextDim = Color.FromArgb(160, 132, 126, 126);
        private static readonly Color OnColour = Color.FromArgb(255, 196, 40, 40);
        private static readonly Color OffColour = Color.FromArgb(120, 70, 66, 66);

        // ---- model -----------------------------------------------------------

        /// <summary>One row.</summary>
        private sealed class Option
        {
            public string Name = "";

            /// <summary>Where it lives in the ini. Empty for an action or readout row.</summary>
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

            /// <summary>Reads as a switch, so it draws a sliding pill rather than a number.</summary>
            public Func<bool> Toggle;

            /// <summary>0..1 of the way along its range, for the fill bar. Null for no bar.</summary>
            public Func<float> Fraction;

            /// <summary>Animated position of the pill knob or the fill bar. Chases the real value.</summary>
            public float Anim;
        }

        /// <summary>One tab: a name, a drawn icon, and the rows under it.</summary>
        private sealed class Tab
        {
            public string Name = "";
            public Action<float, float, float, Color> Icon;
            public readonly List<Option> Options = new List<Option>();
        }

        private readonly Settings _cfg;
        private readonly Decals _decals;
        private readonly BloodField _field;
        private readonly Footprints _footprints;
        private readonly Globals _globals;
        private readonly Spray _spray;

        private readonly List<Tab> _tabs = new List<Tab>();

        /// <summary>Keys whose ini value has changed since the last write.</summary>
        private readonly HashSet<Option> _dirty = new HashSet<Option>();

        private bool _open;
        private int _tab;
        private int _selected;

        // ---- animation state -------------------------------------------------

        /// <summary>0 closed, 1 fully open. Everything fades and slides off this.</summary>
        private float _openAmount;

        /// <summary>Row the selection bar is actually drawn at, chasing _selected.</summary>
        private float _barRow;

        /// <summary>Tab the underline is actually drawn at, chasing _tab.</summary>
        private float _barTab;

        /// <summary>Brightens the selection briefly after a change, so a nudge is visible.</summary>
        private float _touch;

        private bool _menuKeyDown;
        private bool _upDown, _downDown, _leftDown, _rightDown, _enterDown, _backDown;
        private bool _prevTabDown, _nextTabDown;

        private int _padHeldSince;
        private bool _padConsumed;

        private int _lastChange;
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

            BuildTabs();
        }

        public bool IsOpen => _open;

        private Tab Current => _tabs[_tab];

        /// <summary>
        /// A width that looks the same on screen as the given height.
        ///
        /// Every coordinate the game takes is a fraction of the screen, and the screen is not
        /// square -- so a width of 0.02 and a height of 0.02 draws a wide rectangle, not a box.
        /// Everything meant to be square or circular goes through here.
        /// </summary>
        private static float Square(float height)
        {
            try
            {
                var aspect = GTA.UI.Screen.AspectRatio;
                if (aspect > 0.1f) return height / aspect;
            }
            catch
            {
                // Fall through to 16:9, which is right far more often than it is wrong.
            }

            return height * 0.5625f;
        }

        // ---- loop ------------------------------------------------------------

        public void Update()
        {
            if (Toggled() || PadToggled())
            {
                _open = !_open;
                if (!_open) Flush();
            }

            Animate();

            if (!_open)
            {
                // Keep drawing while it slides shut, then stop entirely.
                if (_openAmount > 0.002f) Render();

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

        /// <summary>
        /// Moves everything that animates one frame closer to where it should be.
        ///
        /// Framerate-independent easing: the step is scaled by the frame time, so the menu
        /// feels the same at 30 and at 144. A fixed per-frame fraction would crawl on a slow
        /// machine and snap on a fast one.
        /// </summary>
        private void Animate()
        {
            var dt = SafeDelta();

            var target = _open ? 1f : 0f;
            var openStep = dt / Math.Max(0.001f, OpenSeconds);

            if (_openAmount < target) _openAmount = Math.Min(target, _openAmount + openStep);
            else if (_openAmount > target) _openAmount = Math.Max(target, _openAmount - openStep);

            var k = 1f - (float)Math.Exp(-Chase * dt);

            _barRow += (_selected - _barRow) * k;
            _barTab += (_tab - _barTab) * k;

            if (_touch > 0f) _touch = Math.Max(0f, _touch - dt * 3.5f);

            foreach (var tab in _tabs)
            {
                foreach (var option in tab.Options)
                {
                    var want = option.Toggle != null
                        ? (option.Toggle() ? 1f : 0f)
                        : option.Fraction != null ? Clamp01(option.Fraction()) : 0f;

                    option.Anim += (want - option.Anim) * k;
                }
            }
        }

        private static float SafeDelta()
        {
            try
            {
                var dt = Game.LastFrameTime;

                // A paused or hitching game hands back zero or several seconds; either would
                // make the animation jump rather than move.
                if (dt <= 0f || dt > 0.25f) return 0.016f;
                return dt;
            }
            catch
            {
                return 0.016f;
            }
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
            if (Edge(Keys.Up, ref _upDown) || Pad(GTA.Control.FrontendUp, GTA.Control.PhoneUp)) Move(-1);
            if (Edge(Keys.Down, ref _downDown) || Pad(GTA.Control.FrontendDown, GTA.Control.PhoneDown)) Move(1);
            if (Edge(Keys.Left, ref _leftDown) || Pad(GTA.Control.FrontendLeft, GTA.Control.PhoneLeft)) Change(-1);
            if (Edge(Keys.Right, ref _rightDown) || Pad(GTA.Control.FrontendRight, GTA.Control.PhoneRight)) Change(1);
            if (Edge(Keys.Enter, ref _enterDown) || Pad(GTA.Control.FrontendAccept, GTA.Control.PhoneSelect)) Activate();

            if (Edge(Keys.Q, ref _prevTabDown) || Pad(GTA.Control.FrontendLb, GTA.Control.FrontendLb)) Tabs(-1);
            if (Edge(Keys.E, ref _nextTabDown) || Pad(GTA.Control.FrontendRb, GTA.Control.FrontendRb)) Tabs(1);

            if (Edge(Keys.Back, ref _backDown) || Pad(GTA.Control.FrontendCancel, GTA.Control.PhoneCancel))
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

        /// <summary>
        /// A controller button, checked against BOTH of the game's names for the d-pad.
        ///
        /// The d-pad reports as PhoneUp/Down/Left/Right (172-175) in ordinary gameplay and as
        /// FrontendUp/Down/Left/Right (187-190) in menu context, and which one a given frame
        /// answers to depends on what else has claimed the input. Reading both costs one extra
        /// native call and removes the guess entirely.
        /// </summary>
        private static bool Pad(GTA.Control a, GTA.Control b)
        {
            try { return Game.IsControlJustPressed(a) || Game.IsControlJustPressed(b); }
            catch { return false; }
        }

        /// <summary>
        /// Opens and closes the menu from a controller: HOLD BOTH SHOULDER BUTTONS.
        ///
        /// IT USED TO BE THE VIEW/BACK BUTTON, WHICH WAS A BAD CHOICE. That is
        /// Control.MultiplayerInfo, and on a controller the game maps it to D-PAD DOWN -- the
        /// player-list gesture, which people hold in normal play. The menu opened by itself.
        ///
        /// Two shoulder buttons held together is not a gesture any on-foot or driving action
        /// uses, so it cannot fire by accident, and it is still one motion to reach.
        /// </summary>
        private bool PadToggled()
        {
            if (!_cfg.ControllerMenu) return false;

            bool down;

            try
            {
                down = Game.IsControlPressed(GTA.Control.FrontendLb)
                    && Game.IsControlPressed(GTA.Control.FrontendRb);
            }
            catch { return false; }

            if (!down)
            {
                _padHeldSince = 0;
                _padConsumed = false;
                return false;
            }

            if (_padConsumed) return false;

            if (_padHeldSince == 0)
            {
                _padHeldSince = Game.GameTime;
                return false;
            }

            if (Game.GameTime - _padHeldSince < _cfg.ControllerHoldMs) return false;

            _padConsumed = true;
            return true;
        }

        private void Tabs(int step)
        {
            _tab += step;

            if (_tab < 0) _tab = _tabs.Count - 1;
            if (_tab >= _tabs.Count) _tab = 0;

            // The new tab is a different length, so the selection has to be brought back
            // inside it rather than left pointing past the end of a shorter list.
            if (_selected >= Current.Options.Count) _selected = Math.Max(0, Current.Options.Count - 1);

            _touch = 1f;
        }

        private void Move(int step)
        {
            var count = Current.Options.Count;
            if (count == 0) return;

            _selected += step;

            if (_selected < 0) _selected = count - 1;
            if (_selected >= count) _selected = 0;
        }

        private void Change(int direction)
        {
            var options = Current.Options;
            if (_selected < 0 || _selected >= options.Count) return;

            var option = options[_selected];
            if (option.Nudge == null) return;

            option.Nudge(direction);
            _touch = 1f;

            if (option.Persist != null && !string.IsNullOrEmpty(option.Key)) _dirty.Add(option);
            _lastChange = Game.GameTime;

            // Engine settings have to be pushed out again to take effect; the rest are read
            // straight off the settings object by whichever system owns them.
            _globals.Apply();
        }

        private void Activate()
        {
            var options = Current.Options;
            if (_selected < 0 || _selected >= options.Count) return;

            var option = options[_selected];

            if (option.Activate != null)
            {
                option.Activate();
                _touch = 1f;
                return;
            }

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
                GTA.Control.CharacterWheel, GTA.Control.VehicleCinCam,

                // The controller inputs the menu is driving with. Without these the d-pad
                // still works the phone and the weapon wheel underneath the open menu.
                GTA.Control.PhoneUp, GTA.Control.PhoneDown,
                GTA.Control.PhoneLeft, GTA.Control.PhoneRight,
                GTA.Control.PhoneSelect, GTA.Control.PhoneCancel,

                // The shoulder buttons now change tab.
                GTA.Control.FrontendLb, GTA.Control.FrontendRb
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
            // Eased so it decelerates into place rather than arriving at a constant speed.
            var t = _openAmount * _openAmount * (3f - 2f * _openAmount);

            // Slides in from the left as it fades. Two cues rather than one make the panel
            // read as arriving rather than simply appearing.
            var x = Left - (1f - t) * 0.05f;
            var alpha = t;

            var rows = Current.Options.Count;
            var bodyHeight = RowHeight * rows;
            var height = HeaderHeight + TabStripHeight + bodyHeight + FooterHeight;

            Draw.Bar(x, Top - HeaderHeight - TabStripHeight, Width, height, Fade(Backdrop, alpha));

            // A hairline down the left edge as a spine for the whole panel.
            Draw.Bar(x, Top - HeaderHeight - TabStripHeight, Square(0.004f), height,
                     Fade(Accent, alpha));

            Header(x, alpha);
            TabStrip(x, alpha);
            Rows(x, alpha, rows);
            Footer(x, alpha, bodyHeight);
        }

        private void Header(float x, float alpha)
        {
            var y = Top - HeaderHeight - TabStripHeight;

            IconBlood(x + Square(0.018f), y + 0.019f, 0.020f, Fade(Accent, alpha));

            Draw.Text(Build.Name, x + Square(0.034f), y + 0.007f, 0.42f, Fade(TextBright, alpha));

            Draw.Text(Build.Version, x + Width - 0.008f, y + 0.010f, 0.28f,
                      Fade(TextDim, alpha), 4, false, true);

            // The live counters, kept in the header so they are visible on every tab rather
            // than only on the one that happens to be about the budget.
            Draw.Text(_decals.LiveSplatters + "/" + _cfg.MaxSplatters + " splats    " +
                      _decals.LivePools + "/" + _cfg.MaxPools + " pools    " +
                      _field.Count + " wet",
                      x + Square(0.034f), y + 0.036f, 0.26f, Fade(TextDim, alpha));
        }

        private void TabStrip(float x, float alpha)
        {
            var y = Top - TabStripHeight;
            var tabWidth = Width / _tabs.Count;

            for (var i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                var active = i == _tab;
                var cx = x + tabWidth * i + tabWidth * 0.5f;

                var colour = active ? TextBright : TextDim;

                if (tab.Icon != null) tab.Icon(cx, y + 0.010f, 0.014f, Fade(colour, alpha));

                Draw.Text(tab.Name, cx, y + 0.019f, 0.24f, Fade(colour, alpha), 4, true);
            }

            // The underline SLIDES between tabs rather than jumping, which is the cue that
            // says these are one strip and not six separate buttons.
            var barX = x + tabWidth * _barTab;
            Draw.Bar(barX + tabWidth * 0.15f, y + TabStripHeight - 0.004f,
                     tabWidth * 0.7f, Square(0.003f), Fade(Accent, alpha));

            Draw.Bar(x, y + TabStripHeight - 0.001f, Width, Square(0.0012f),
                     Fade(AccentDim, alpha));
        }

        private void Rows(float x, float alpha, int rows)
        {
            // Drawn at the ANIMATED row, so the highlight slides between rows.
            var barY = Top + RowHeight * _barRow;

            var glow = 90 + (int)(_touch * 70f);
            Draw.Bar(x, barY, Width, RowHeight, Fade(Color.FromArgb(glow, 150, 26, 26), alpha));
            Draw.Bar(x, barY, Square(0.004f), RowHeight, Fade(Accent, alpha));

            for (var i = 0; i < rows; i++)
            {
                var option = Current.Options[i];
                var y = Top + RowHeight * i;
                var chosen = i == _selected;

                Draw.Text(option.Name, x + Square(0.018f), y + 0.005f, 0.30f,
                          Fade(chosen ? TextBright : TextNormal, alpha));

                var right = x + Width - 0.010f;

                if (option.Toggle != null)
                {
                    Pill(right, y + RowHeight * 0.5f, option.Anim, alpha);
                    continue;
                }

                if (option.Show == null) continue;

                Draw.Text(option.Show(), right, y + 0.005f, 0.30f,
                          Fade(chosen ? Color.FromArgb(255, 255, 196, 196) : TextNormal, alpha),
                          4, false, true);

                // A thin fill under a number says where in its range it sits, which a bare
                // figure never does: "40" means nothing until you know the range is 8 to 2000.
                if (option.Fraction == null) continue;

                var barWidth = 0.055f;
                var barLeft = right - barWidth;
                var barTop = y + RowHeight - 0.006f;

                Draw.Bar(barLeft, barTop, barWidth, Square(0.0015f), Fade(OffColour, alpha));
                Draw.Bar(barLeft, barTop, barWidth * Clamp01(option.Anim), Square(0.0015f),
                         Fade(Accent, alpha));
            }
        }

        /// <summary>An on/off pill whose knob slides across as the value changes.</summary>
        private void Pill(float right, float centreY, float amount, float alpha)
        {
            var w = Square(0.026f);
            var h = 0.011f;
            var left = right - w;

            Draw.Bar(left, centreY - h * 0.5f, w, h,
                     Fade(Blend(OffColour, OnColour, amount), alpha));

            var knob = Square(0.010f);
            var knobLeft = left + (w - knob) * Clamp01(amount);

            Draw.Bar(knobLeft, centreY - h * 0.5f, knob, h,
                     Fade(Color.FromArgb(255, 245, 240, 240), alpha));
        }

        private void Footer(float x, float alpha, float bodyHeight)
        {
            var y = Top + bodyHeight + 0.004f;

            Draw.Bar(x, y - 0.003f, Width, Square(0.0012f), Fade(AccentDim, alpha));
            Draw.Text(Hint(), x + Square(0.018f), y + 0.002f, 0.23f, Fade(TextDim, alpha));
        }

        private string Hint()
        {
            if (Game.GameTime < _flashUntil && !string.IsNullOrEmpty(_flash)) return _flash;
            if (_writeGaveUp) return "cannot write the ini - changes apply but will not stick";

            return "Q/E or LB/RB tabs   arrows change   enter/A pick   back/B or LB+RB close";
        }

        private void Flash(string message)
        {
            _flash = message;
            _flashUntil = Game.GameTime + 2500;
        }

        private static Color Fade(Color colour, float alpha)
        {
            return Color.FromArgb((int)(colour.A * Clamp01(alpha)), colour.R, colour.G, colour.B);
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Clamp01(t);
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        // ---- the icons, drawn from rectangles --------------------------------
        //
        // Each takes a CENTRE, a height and a colour. See the note on the class for why these
        // are not font glyphs and not image files.

        /// <summary>A droplet: bars that taper to a point at the top.</summary>
        private static void IconBlood(float cx, float cy, float size, Color colour)
        {
            var w = Square(size);

            Draw.Bar(cx - w * 0.10f, cy - size * 0.50f, w * 0.20f, size * 0.22f, colour);
            Draw.Bar(cx - w * 0.22f, cy - size * 0.28f, w * 0.44f, size * 0.26f, colour);
            Draw.Bar(cx - w * 0.34f, cy - size * 0.02f, w * 0.68f, size * 0.34f, colour);
            Draw.Bar(cx - w * 0.22f, cy + size * 0.32f, w * 0.44f, size * 0.16f, colour);
        }

        /// <summary>Two sliders, for the setup tab.</summary>
        private static void IconGeneral(float cx, float cy, float size, Color colour)
        {
            var w = Square(size);

            Draw.Bar(cx - w * 0.5f, cy - size * 0.26f, w, size * 0.10f, colour);
            Draw.Bar(cx - w * 0.12f, cy - size * 0.40f, w * 0.20f, size * 0.38f, colour);

            Draw.Bar(cx - w * 0.5f, cy + size * 0.16f, w, size * 0.10f, colour);
            Draw.Bar(cx + w * 0.14f, cy + size * 0.02f, w * 0.20f, size * 0.38f, colour);
        }

        /// <summary>A pool: a wide flat lens.</summary>
        private static void IconPool(float cx, float cy, float size, Color colour)
        {
            var w = Square(size);

            Draw.Bar(cx - w * 0.30f, cy - size * 0.24f, w * 0.60f, size * 0.16f, colour);
            Draw.Bar(cx - w * 0.50f, cy - size * 0.08f, w, size * 0.20f, colour);
            Draw.Bar(cx - w * 0.34f, cy + size * 0.12f, w * 0.68f, size * 0.14f, colour);
        }

        /// <summary>Two footprints, offset the way a stride falls.</summary>
        private static void IconPrints(float cx, float cy, float size, Color colour)
        {
            var w = Square(size);

            Draw.Bar(cx - w * 0.44f, cy - size * 0.48f, w * 0.30f, size * 0.34f, colour);
            Draw.Bar(cx - w * 0.44f, cy - size * 0.10f, w * 0.30f, size * 0.12f, colour);

            Draw.Bar(cx + w * 0.14f, cy - size * 0.04f, w * 0.30f, size * 0.34f, colour);
            Draw.Bar(cx + w * 0.14f, cy + size * 0.34f, w * 0.30f, size * 0.12f, colour);
        }

        /// <summary>A spark, for the odds and ends.</summary>
        private static void IconExtras(float cx, float cy, float size, Color colour)
        {
            var w = Square(size);

            Draw.Bar(cx - w * 0.09f, cy - size * 0.50f, w * 0.18f, size, colour);
            Draw.Bar(cx - w * 0.50f, cy - size * 0.09f, w, size * 0.18f, colour);
        }

        /// <summary>A bar chart, for the budget.</summary>
        private static void IconBudget(float cx, float cy, float size, Color colour)
        {
            var w = Square(size);

            Draw.Bar(cx - w * 0.46f, cy + size * 0.06f, w * 0.24f, size * 0.44f, colour);
            Draw.Bar(cx - w * 0.12f, cy - size * 0.20f, w * 0.24f, size * 0.70f, colour);
            Draw.Bar(cx + w * 0.22f, cy - size * 0.46f, w * 0.24f, size * 0.96f, colour);
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

        // ---- the tabs themselves ---------------------------------------------

        private Tab _building;

        private void BuildTabs()
        {
            NewTab("Setup", IconGeneral);

            Switch("Mod enabled", "General", "Enabled",
                   () => _cfg.Enabled, v => _cfg.Enabled = v);

            _building.Options.Add(new Option
            {
                Name = "Gore level",
                Section = "Intensity",
                Key = "Level",
                Show = () => _cfg.Level.ToString(),
                Fraction = () => (int)_cfg.Level / 3f,
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
            Switch("Controller menu", "General", "ControllerMenu",
                   () => _cfg.ControllerMenu, v => _cfg.ControllerMenu = v);

            NewTab("Blood", IconBlood);

            Switch("Wounds on bodies", "Wounds", "Enabled",
                   () => _cfg.WoundsEnabled, v => _cfg.WoundsEnabled = v);
            Switch("Blood on the player", "Wounds", "PlayerWounds",
                   () => _cfg.PlayerWounds, v => _cfg.PlayerWounds = v);
            Switch("Spray and splatter", "Spray", "Enabled",
                   () => _cfg.SprayEnabled, v => _cfg.SprayEnabled = v);
            Switch("Spray onto walls", "Spray", "OnWalls",
                   () => _cfg.SprayOnWalls, v => _cfg.SprayOnWalls = v);
            Number("Blood on the ground", "Spray", "GroundDrops",
                   () => _cfg.SprayGroundDrops, v => _cfg.SprayGroundDrops = (int)v, 0f, 64f, 2f);
            Number("Shots into a corpse", "Spray", "CorpseShots",
                   () => _cfg.CorpseShots, v => _cfg.CorpseShots = (int)v, 0f, 200f, 5f);
            Number("Mist", "Spray", "MistChance",
                   () => _cfg.SprayMistChance, v => _cfg.SprayMistChance = v, 0f, 1f, 0.05f);
            Switch("Spray from the wound", "Spray", "Particles",
                   () => _cfg.SprayParticles, v => _cfg.SprayParticles = v);
            Number("...how big", "Spray", "ParticleScale",
                   () => _cfg.SprayParticleScale, v => _cfg.SprayParticleScale = v, 0.1f, 4f, 0.1f);

            NewTab("Pools", IconPool);

            Switch("Pools under bodies", "Pools", "Enabled",
                   () => _cfg.PoolsEnabled, v => _cfg.PoolsEnabled = v);
            Switch("Draw our own pool", "Pools", "DrawOurOwn",
                   () => _cfg.PoolsDrawOurOwn, v => _cfg.PoolsDrawOurOwn = v);
            Number("Pool size", "Pools", "MaxSize",
                   () => _cfg.PoolMaxSize, v => _cfg.PoolMaxSize = v, 0.2f, 4f, 0.1f);
            Switch("Trails from the wounded", "Drips", "Enabled",
                   () => _cfg.DripsEnabled, v => _cfg.DripsEnabled = v);
            Number("Blood colour", "Appearance", "BloodRed",
                   () => _cfg.BloodRed, v => _cfg.BloodRed = v, 0.05f, 1f, 0.02f);
            Number("Pool colour", "Appearance", "PoolRed",
                   () => _cfg.PoolRed, v => _cfg.PoolRed = v, 0.05f, 1f, 0.02f);
            Switch("Varied pool textures", "Pools", "VariedTextures",
                   () => _cfg.PoolVariedTextures, v => _cfg.PoolVariedTextures = v);

            NewTab("Prints", IconPrints);

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

            NewTab("Extras", IconExtras);

            Switch("Legs drop them alive", "Legs", "Enabled",
                   () => _cfg.LegsEnabled, v => _cfg.LegsEnabled = v);
            Number("...how often", "Legs", "Chance",
                   () => _cfg.LegsChance, v => _cfg.LegsChance = v, 0f, 1f, 0.05f);
            Number("...for how long", "Legs", "Seconds",
                   () => _cfg.LegsSeconds, v => _cfg.LegsSeconds = v, 2f, 300f, 5f);
            Switch("...then they bleed out", "Legs", "BleedOut",
                   () => _cfg.LegsBleedOut, v => _cfg.LegsBleedOut = v);
            Switch("Heads come off", "Heads", "Enabled",
                   () => _cfg.HeadsEnabled, v => _cfg.HeadsEnabled = v);
            Number("...how often", "Heads", "Chance",
                   () => _cfg.HeadsChance, v => _cfg.HeadsChance = v, 0f, 1f, 0.05f);
            Switch("Bigger blood particles", "Game", "BiggerBloodParticles",
                   () => _cfg.BiggerBloodParticles, v => _cfg.BiggerBloodParticles = v);
            Switch("Shotgun spread decals", "Game", "ShotgunDecals",
                   () => _cfg.ShotgunDecals, v => _cfg.ShotgunDecals = v);
            Switch("Clown blood", "Game", "ClownBlood",
                   () => _cfg.ClownBlood, v => _cfg.ClownBlood = v);
            Switch("Alien blood", "Game", "AlienBlood",
                   () => _cfg.AlienBlood, v => _cfg.AlienBlood = v);

            NewTab("Budget", IconBudget);

            // CEILING MATCHES THE INI'S. This row used to stop at 900 while the ini already
            // accepted 2000, so the menu quietly refused to set what the file itself held.
            Number("Max splatters", "Budget", "MaxSplatters",
                   () => _cfg.MaxSplatters, v => _cfg.MaxSplatters = (int)v, 8f, 2000f, 25f);
            Number("Max pools", "Budget", "MaxPools",
                   () => _cfg.MaxPools, v => _cfg.MaxPools = (int)v, 0f, 300f, 5f);
            Number("Probes per frame", "Budget", "ProbesPerFrame",
                   () => _cfg.ProbesPerFrame, v => _cfg.ProbesPerFrame = (int)v, 1f, 64f, 1f);
            Number("Blood fades after", "Budget", "FadeSeconds",
                   () => _cfg.FadeSeconds, v => _cfg.FadeSeconds = v, 0f, 1200f, 15f);

            _building.Options.Add(new Option
            {
                Name = "Blood laid",
                Show = () => _spray.Placed + "   (" + _spray.Queued + " queued)"
            });

            _building.Options.Add(new Option
            {
                Name = "Faded away",
                Show = () => _decals.Expired.ToString(CultureInfo.InvariantCulture)
            });

            _building.Options.Add(new Option
            {
                Name = "Clear all blood now",
                Show = () => _footprints.Printed + " prints",
                Activate = () =>
                {
                    var removed = _decals.ClearAll();
                    _field.Clear();
                    Flash("Removed " + removed + " decal(s).");
                }
            });
        }

        private void NewTab(string name, Action<float, float, float, Color> icon)
        {
            _building = new Tab { Name = name, Icon = icon };
            _tabs.Add(_building);
        }

        private void Switch(string name, string section, string key,
                            Func<bool> read, Action<bool> write)
        {
            _building.Options.Add(new Option
            {
                Name = name,
                Section = section,
                Key = key,
                Toggle = read,
                Nudge = _ => write(!read()),
                Persist = () => read() ? "true" : "false"
            });
        }

        private void Number(string name, string section, string key,
                            Func<float> read, Action<float> write,
                            float min, float max, float step)
        {
            _building.Options.Add(new Option
            {
                Name = name,
                Section = section,
                Key = key,
                Show = () => Format(read(), step),
                Fraction = () => max > min ? (read() - min) / (max - min) : 0f,
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
