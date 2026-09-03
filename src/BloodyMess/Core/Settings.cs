using System;
using System.Windows.Forms;

namespace BloodyMess.Core
{
    /// <summary>
    /// How much of a mess this makes.
    ///
    /// A NAMED LEVEL EXISTS because "how gory" is the only question most people want to
    /// answer, and answering it with thirty numbers is answering it badly. Every number below
    /// is still there for anybody who wants it; the level just scales the ones that decide
    /// how much blood there is, so a single word moves all of them together.
    /// </summary>
    internal enum GoreLevel
    {
        /// <summary>Close to stock. A bit more than the game gives you, and no more.</summary>
        Tame,

        /// <summary>Visibly bloodier. Spray, pools and prints, at sane sizes.</summary>
        Bloody,

        /// <summary>What the mod is named for. The default.</summary>
        Mess,

        /// <summary>Silly. Everything doubled again. Watch the decal budget.</summary>
        Abattoir
    }

    /// <summary>
    /// Everything the player can tune, loaded from BloodyMess.ini.
    ///
    /// Every value has a working default in this file, so a missing or half-written ini is
    /// never fatal -- the mod runs on the defaults and says which settings it could not find.
    /// That is not defensive padding: the ini is deliberately never overwritten by a deploy
    /// (it is the file players hand-edit), so an install that has been through an update is
    /// the NORMAL case for a file that lacks the newest keys.
    ///
    /// THE REASON THIS FILE IS AS BIG AS IT IS: the gore mod this replaces on this machine,
    /// RealisticGoreV, shipped no ini at all. Its bleed loop was hardcoded, every ped and the
    /// player bled forever, and there was no way to turn any of it down -- so it had to be
    /// deleted rather than tuned. Every behaviour in Bloody Mess is reachable from here, and
    /// every system can be switched off on its own.
    /// </summary>
    internal sealed class Settings
    {
        // ---- General ---------------------------------------------------------

        public bool Enabled = true;
        public bool AnnounceOnLoad = true;
        public LogLevel LogLevel = LogLevel.Info;

        /// <summary>
        /// The settings menu key.
        ///
        /// F10 is free on BOTH installs on this machine -- it was freed on Legacy when
        /// OnTheBlock was removed and on Enhanced when PullMeOverRemade went. F2 is Hoodrich,
        /// F3 Overspray, F4 is NativeTrainer and cannot be rebound, F6 is ENT, F7 is Bare
        /// Minimum, F8 is the Dealien menu. Check the hotkey map before moving this.
        /// </summary>
        public Keys MenuKey = Keys.F10;

        /// <summary>
        /// Whether a controller can OPEN the menu. Off.
        ///
        /// TWO GESTURES WERE TRIED AND BOTH WERE WRONG. The View/Back button turned out to be
        /// d-pad down on a pad, which people hold in normal play; both shoulder buttons held
        /// together turned out to ruin shooting from a car, where they are drive-by and aim.
        /// There is no spare chord on a controller during combat, which is exactly when this
        /// mod is worth looking at.
        ///
        /// So the menu is keyboard-opened, on MenuKey. Once it is open a controller still
        /// drives it perfectly well -- d-pad, A and B all work -- which is the part that
        /// actually mattered.
        /// </summary>
        public bool ControllerMenu = false;

        /// <summary>Milliseconds the View/Back button must be held to open the menu.</summary>
        public int ControllerHoldMs = 350;

        // ---- Intensity -------------------------------------------------------

        public GoreLevel Level = GoreLevel.Mess;

        /// <summary>Fine trim on top of the level, for when a level is nearly right.</summary>
        public float Multiplier = 1f;

        /// <summary>
        /// What the named level is worth as a number.
        ///
        /// Applied to COUNTS and SIZES, never to the budget: turning the gore up must not be
        /// able to turn the safety off. See Budget below.
        /// </summary>
        public float Scale
        {
            get
            {
                float baseScale;
                switch (Level)
                {
                    case GoreLevel.Tame: baseScale = 0.5f; break;
                    case GoreLevel.Bloody: baseScale = 1f; break;
                    case GoreLevel.Abattoir: baseScale = 3f; break;
                    default: baseScale = 1.8f; break;
                }
                return baseScale * Multiplier;
            }
        }

        // ---- Wounds (blood on the ped itself) --------------------------------

        public bool WoundsEnabled = true;

        /// <summary>Extra wound decals stamped on the body per hit, before the level scales it.</summary>
        public int WoundsPerHit = 1;

        /// <summary>Size of those wound decals. 1.0 is the game's own.</summary>
        public float WoundScale = 1.1f;

        /// <summary>Whether the game's built-in damage packs are applied on heavy hits.</summary>
        public bool DamagePacks = true;

        /// <summary>Whether the player character bleeds the same way everyone else does.</summary>
        public bool PlayerWounds = true;

        // ---- Spray (blood thrown into the world) ------------------------------

        public bool SprayEnabled = true;

        /// <summary>
        /// Splatter decals thrown along the shot line per hit, before scaling.
        ///
        /// KEPT LOW DELIBERATELY. This is the airborne half of the mod -- the burst that
        /// reads as spray in the moment you fire -- and turning it up is what made headshots
        /// look like a paint bomb. The blood you actually want to see afterwards is on the
        /// ground, and that is GroundDrops below, which is set far higher.
        /// </summary>
        public int SprayPerHit = 2;

        /// <summary>
        /// Splatters dropped straight down around the victim on every hit.
        ///
        /// SEPARATE FROM THE THROWN SPRAY ON PURPOSE. The thrown spray follows the shot line
        /// and lands wherever that takes it, which can be several metres away; this is the
        /// blood that simply falls out of the wound and hits the floor where the ped is
        /// standing. It is what makes the ground under somebody you have just shot actually
        /// look like it, rather than being clean while a fan of drops sits behind them.
        /// </summary>
        public int SprayGroundDrops = 20;

        /// <summary>How far behind the ped the spray is allowed to reach, in metres.</summary>
        public float SprayRange = 4.5f;

        /// <summary>How wide the cone is. 0 is a line, 1 is a wide fan.</summary>
        public float SpraySpread = 0.45f;

        // MUCH SMALLER THAN 0.1.0, which shipped 0.12-0.55 and then multiplied that by a
        // damage factor capped at 2.5 and again by the gore level -- a kill produced splatters
        // nearly two metres across. See Spray for the damage curve that went with it.
        public float SprayMinSize = 0.10f;
        public float SprayMaxSize = 0.32f;
        public float SprayOpacity = 0.85f;

        /// <summary>
        /// How many rounds a body keeps bleeding for after it is already dead.
        ///
        /// Shooting a corpse produced nothing at all before this, because every hit is found by
        /// watching health fall and a corpse has none left to lose. A separate detector handles
        /// it (see Victims.CorpseShot); this is the cap on how long it keeps answering.
        ///
        /// CAPPED BECAUSE A CORPSE IS UNLIMITED. A living ped stops taking hits when it dies; a
        /// body will absorb magazine after magazine, and without a limit somebody standing over
        /// one would own the entire decal budget within a minute. 0 turns it off.
        /// </summary>
        public int CorpseShots = 60;

        /// <summary>Whether spray is allowed to land on walls, not just the ground.</summary>
        public bool SprayOnWalls = true;

        /// <summary>Adds the fine mist decal over the top of the splatter.</summary>
        public bool SprayMist = true;

        /// <summary>
        /// How often a splatter gets a mist halo over it, from 0 to 1.
        ///
        /// The mist is the cheap part of this mod and the part that reads best: it is drawn on
        /// ground already found for the splatter underneath it, so it costs no extra raycast.
        /// </summary>
        public float SprayMistChance = 0.85f;

        /// <summary>
        /// Our OWN extra blood burst at the wound, ON TOP of the one the game already plays.
        ///
        /// OFF, AND THIS TIME FOR THE RIGHT REASON. It was switched on in 0.2.7 to answer
        /// "wounds should spray like vanilla" -- but that release ALSO fixed the actual cause,
        /// which was this mod pushing SET_PARTICLE_FX_BLOOD_SCALE(false) into the game every
        /// five seconds and suppressing the game's own blood. With the suppression gone, GTA's
        /// own spray is back at full strength, and ours on top of it is simply too much again.
        ///
        /// Vanilla spray means the game's spray. This adds a second one, so it stays off.
        /// </summary>
        public bool SprayParticles = false;

        /// <summary>
        /// Multiplier on the wound spray, over and above each weapon's own fxScale.
        ///
        /// One knob for "more" or "less" spray without editing eleven weapon entries in
        /// gore.json. 1.0 is the tuned default.
        /// </summary>
        public float SprayParticleScale = 1f;

        // ---- Pools -----------------------------------------------------------

        public bool PoolsEnabled = true;

        /// <summary>Seconds after a ped goes down before a pool starts forming.</summary>
        public float PoolDelay = 2.5f;

        /// <summary>Seconds from first appearing to fully grown.</summary>
        public float PoolGrowSeconds = 30f;

        public float PoolStartSize = 0.35f;
        public float PoolMaxSize = 1.7f;

        /// <summary>
        /// How many times a pool is redrawn on its way to full size.
        ///
        /// A decal cannot be resized, so growth is a redraw: the old handle is removed and a
        /// bigger one takes its place. Each pool therefore costs ONE decal no matter how many
        /// steps it takes, which is the whole reason it is done this way.
        /// </summary>
        public int PoolSteps = 6;

        /// <summary>Whether badly wounded peds who are still alive pool as well as the dead.</summary>
        public bool PoolsFromWounded = true;

        /// <summary>
        /// Draw our own pool under a body, instead of leaving it to the game.
        ///
        /// OFF, AND THE REASON IS THAT OURS CANNOT LOOK AS GOOD. GTA already grows a blood
        /// pool under a corpse, and it uses the good `fxdecal_blood_pool` artwork -- textured,
        /// glossy, the right colour without help. The decal ids a script can reach either mix
        /// that artwork with a colourless `pool_solid` at random (id 9001, so pools come out in
        /// two different shades) or are colourless every time (id 9002, so they come out as a
        /// flat untextured blob whatever colour you tint them). Neither beats what the engine
        /// does on its own.
        ///
        /// So by default this mod adds no pool of its own. What it still does is REGISTER the
        /// ground under a body as wet, which is what the footprints are picked up from -- the
        /// pool being the game's rather than ours makes no difference to that, and it costs no
        /// decals at all.
        ///
        /// Turn it on if you want a bigger or differently coloured pool than the game's, and
        /// tune it with MaxSize and the pool colour in [Appearance].
        /// </summary>
        public bool PoolsDrawOurOwn = false;

        /// <summary>
        /// Use the game's varied blood-pool textures instead of the single plain one.
        ///
        /// OFF, because "varied" here means UNCONTROLLED. The blood pool id has four texture
        /// variants and the engine picks one at random -- one colourless, three already dark
        /// red -- so with a tint applied you get bright red pools sitting next to dark maroon
        /// ones with no setting able to bring them together. Off, every pool uses one
        /// colourless texture and the tint alone decides the colour, so they all match.
        ///
        /// Turn it on if you would rather have the extra texture detail and can live with
        /// pools not matching each other.
        /// </summary>
        public bool PoolVariedTextures = false;

        // ---- Drips (a wounded ped still walking) ------------------------------

        public bool DripsEnabled = true;

        /// <summary>Metres between drops.</summary>
        public float DripDistance = 1.1f;

        public float DripSize = 0.13f;
        public float DripOpacity = 0.7f;

        // ---- Footprints ------------------------------------------------------

        public bool FootprintsEnabled = true;

        /// <summary>Prints left after one walk through blood, before scaling.</summary>
        public int FootprintSteps = 14;

        /// <summary>How close a foot has to get to wet blood to pick any up, in metres.</summary>
        public float FootprintPickupRadius = 1.1f;

        /// <summary>Metres of travel per print. Roughly one pace.</summary>
        public float FootprintStride = 0.62f;

        public float FootprintWidth = 0.14f;
        public float FootprintLength = 0.33f;
        public float FootprintOpacity = 0.8f;

        /// <summary>
        /// Seconds a patch of blood stays wet enough to walk out of.
        ///
        /// The decal itself never dries -- it stays exactly where it was until the budget takes
        /// it. This is only about whether feet can still pick it up, and it exists because
        /// without it a street where a fight happened half an hour ago goes on printing
        /// footprints forever.
        /// </summary>
        public float FootprintWetSeconds = 150f;

        public bool FootprintsForPlayer = true;
        public bool FootprintsForNpcs = true;

        /// <summary>
        /// How many NPCs may be tracked for prints at once.
        ///
        /// A gunfight in a crowd is exactly when this mod is doing the most work and the game
        /// has the least to spare, so the ped side of it is capped rather than left to the
        /// size of the crowd.
        /// </summary>
        public int FootprintMaxNpcs = 6;

        /// <summary>
        /// Use the game's own foot decal effect alongside our print.
        ///
        /// SET_PARTICLE_FX_FOOT_OVERRIDE_NAME points the engine's footstep effect at
        /// ped_foot_decal_blood. It is the same system that makes wet feet leave prints, so
        /// it looks right, but it is an override on a shared global -- it is cleared the
        /// moment nobody is bloody-footed, and it is off by default because a mod that
        /// silently owns a global is a mod that fights other mods.
        /// </summary>
        public bool FootprintGameEffect = false;

        // ---- Wheels ----------------------------------------------------------

        /// <summary>Tyres pick blood up out of a pool and lay it back down for a few metres.</summary>
        public bool WheelTracksEnabled = true;

        public int WheelTrackSteps = 10;
        public float WheelTrackSize = 0.3f;
        public float WheelTrackOpacity = 0.7f;

        // ---- Budget ----------------------------------------------------------
        //
        // THE SAFETY, AND IT IS NOT SCALED BY THE GORE LEVEL ON PURPOSE.
        //
        // The game has a fixed decal pool. On this machine Enhanced raises it through
        // DecalPatch.ini (Level=4, 2048) and Legacy has no DecalPatch at all, so the same
        // numbers have to be survivable on the smaller one. Going over the engine's cap does
        // not throw -- decals simply stop appearing, or the game starts recycling the wrong
        // ones -- which is a bug that looks like the mod not working.

        /// <summary>
        /// Live splatter, spray, drip and print decals allowed at once.
        ///
        /// THIS IS THE NO-PATCH FALLBACK, deliberately. It is what runs when there is no ini,
        /// and it has to survive a stock decal pool of 256-512 slots shared with the game's own
        /// bullet holes and tyre marks. The shipped ini sets it far higher, because a decal
        /// limit patch takes the pool to 2048 and 140 leaves nine tenths of it unused.
        /// </summary>
        public int MaxSplatters = 220;

        /// <summary>Live pools allowed at once. Separate lane, so a firefight cannot evict them.</summary>
        public int MaxPools = 50;

        /// <summary>New decals allowed per second, however busy things get.</summary>
        public int DecalsPerSecond = 40;

        /// <summary>Nothing is drawn further than this from the camera, in metres.</summary>
        public float DecalRange = 65f;

        /// <summary>
        /// Seconds a splatter stays in the world before the mod takes it back.
        ///
        /// WITHOUT THIS, BLOOD ONLY EVER LEFT WHEN THE CAP EVICTED IT -- so a session climbed
        /// to MaxSplatters and sat there, holding a thousand decals of the game's fixed pool
        /// for a firefight that finished twenty minutes ago.
        ///
        /// Three minutes keeps a scene intact for as long as anyone is realistically standing
        /// in it, and has the street clean by the time they come back. Pools get twice this,
        /// because there are far fewer of them and a body should not go clean while the
        /// spatter around it is still there. Set to 0 for the old behaviour, where blood stays
        /// until something newer pushes it out.
        /// </summary>
        public float FadeSeconds = 180f;

        /// <summary>
        /// Ground probes allowed per frame.
        ///
        /// THE FRAME-TIME SAFETY, and the counterpart to the decal budget. Every ground
        /// splatter costs one shape test to find the surface under it; done all at once, a
        /// single kill could fire three dozen inside one tick. That does not error -- it
        /// spikes the frame, and GTA's euphoria ragdolls are framerate-sensitive, so the
        /// symptom is peds dying strangely rather than anything that looks like a decal
        /// problem. Six per frame drains a whole kill's worth over about a quarter of a
        /// second and is invisible to look at.
        /// </summary>
        public int ProbesPerFrame = 10;

        /// <summary>
        /// The lifetime passed straight to ADD_DECAL.
        ///
        /// DELIBERATELY A HUGE NUMBER RATHER THAN ZERO. The native reference does not agree
        /// with itself about this parameter's unit -- seconds in some places, milliseconds in
        /// others -- and it does not say what zero means. Zero could plausibly be "never
        /// expires", which is what was wanted, or "expires immediately", which would mean
        /// nothing this mod draws ever appears and no log line anywhere would say why.
        ///
        /// Six hundred thousand sidesteps the question: read as milliseconds it is ten
        /// minutes, read as seconds it is a week. Either way it outlasts the session, and the
        /// decal budget is what actually decides when blood goes.
        /// </summary>
        public float DecalTimeout = 600000f;

        // ---- Engine toggles --------------------------------------------------

        /// <summary>
        /// The game's own blood particles, turned up.
        ///
        /// SET_PARTICLE_FX_BLOOD_SCALE takes a BOOL, not a scale, despite the name -- checked
        /// against the native list rather than assumed, because passing a float to a bool
        /// parameter is the kind of thing that works on one build and not the next.
        /// </summary>
        /// <summary>
        /// OFF BY DEFAULT NOW. This is the single biggest contributor to the "way too much
        /// spray" look: it turns up the game's OWN blood particles everywhere, on top of
        /// whatever this mod adds. Vanilla particles plus our own restrained burst is
        /// already slightly more than stock, which is the target.
        /// </summary>
        public bool BiggerBloodParticles = false;

        /// <summary>Bullet impact particle scale. The game's own, applied to everything.</summary>
        public float BulletImpactScale = 1.1f;

        /// <summary>Range multiplier for bullet impact decals, so hits stay visible further out.</summary>
        public float BulletImpactRange = 2f;

        /// <summary>Shotguns keep their composite decal spread. Off makes shotguns look weaker.</summary>
        public bool ShotgunDecals = true;

        /// <summary>Novelty. Peds burst into confetti instead of blood.</summary>
        public bool ClownBlood = false;

        /// <summary>Novelty. Green.</summary>
        public bool AlienBlood = false;

        // ---- Legs ------------------------------------------------------------

        /// <summary>
        /// Whether a hit to the legs puts somebody on the ground alive instead of killing them.
        ///
        /// THE ONLY PART OF THIS MOD THAT CHANGES HOW THE GAME PLAYS. Everything else is
        /// blood -- switch it off and the fight is identical. This makes people survive shots
        /// that would have killed them, using the game's own TASK_WRITHE wounded state, so it
        /// is the first thing to suspect if the police or gang mods sharing this scripts
        /// folder start behaving oddly: they reasonably expect a ped shot enough to die to die.
        /// </summary>
        public bool LegsEnabled = true;

        /// <summary>Chance from 0 to 1 that a leg hit downs them rather than passing through.</summary>
        public float LegsChance = 0.6f;

        /// <summary>Health points a leg hit must do before it can take them down.</summary>
        public float LegsMinDamage = 10f;

        /// <summary>
        /// Health they are lifted to at the moment of going down.
        ///
        /// Clamped ONCE and never again, so every round afterwards lands normally and a downed
        /// ped stays perfectly killable. Low, so it takes very little to finish them.
        /// </summary>
        public float LegsHealth = 20f;

        /// <summary>Seconds they crawl before bleeding out.</summary>
        public float LegsSeconds = 45f;

        /// <summary>
        /// Whether they die at the end of that, rather than standing back up.
        ///
        /// On, because the alternative is a ped getting to their feet and walking away on a
        /// leg that was shot out from under them a minute ago, which looks worse than either
        /// outcome.
        /// </summary>
        public bool LegsBleedOut = true;

        // ---- Heads -----------------------------------------------------------

        /// <summary>
        /// Whether a headshot can take the head clean off.
        ///
        /// OFF BY DEFAULT, and that is a deliberate reversal. It is the one piece of
        /// dismemberment GTA V supports, but a head popping is a NON-VANILLA EVENT -- it
        /// changes what a headshot is rather than making the existing one bloodier, and it
        /// drags a neck spurt along with it that the stock game never shows. The point of
        /// this mod is more of what the game already does, not new things it does not.
        ///
        /// The whole feature is still here and still works; turn it on if you want it.
        /// </summary>
        public bool HeadsEnabled = false;

        /// <summary>
        /// A global multiplier on the per-weapon chance in gore.json.
        ///
        /// 1.0 leaves each weapon at its own odds; 0.5 halves all of them; 0 is the same as
        /// switching the feature off.
        /// </summary>
        public float HeadsChance = 1f;

        /// <summary>Health points a head hit has to do before it can take the head off.</summary>
        public float HeadsMinDamage = 25f;

        /// <summary>Whether it can happen to the player as well.</summary>
        public bool HeadsIncludePlayer = false;

        /// <summary>The spurt from the neck afterwards.</summary>
        public bool HeadsNeckEffect = true;

        // ---- Appearance ------------------------------------------------------

        /// <summary>
        /// The colour blood is tinted, as ADD_DECAL's red, green and blue coefficients.
        ///
        /// THIS IS WHAT MAKES BLOOD RED RATHER THAN WHITE. The game's splatter textures --
        /// the fxdecal_splatter_mist family -- are greyscale masks, so the colour has to come
        /// from here. 0.1.0 passed 1,1,1 through them and the blood came out looking like
        /// milk. Pools are not tinted: their textures are properly coloured already.
        ///
        /// Defaults are a dark arterial red. Raise BloodRed towards 1 for brighter, more
        /// cartoonish blood; raise green and blue together to make it browner and older.
        /// </summary>
        public float BloodRed = 0.30f;
        public float BloodGreen = 0.025f;
        public float BloodBlue = 0.022f;

        /// <summary>
        /// The colour POOLS are tinted, kept separate from the splatter colour above.
        ///
        /// THEY NEED THEIR OWN, and this is not fussiness. Reading the game's decals.dat:
        /// pool id 9001 has FOUR texture variants -- `pool_solid`, a colourless generic pool
        /// shared with water and oil, plus three proper `fxdecal_blood_pool` textures. The
        /// engine picks one, so roughly one pool in four comes out through the colourless
        /// one. On porous ground, id 9006 has ONLY the colourless `pool_porous_solid`.
        ///
        /// A tint dark enough to turn the colourless variant into believable blood also
        /// crushes the three that are already dark red towards black, and a tint gentle
        /// enough to leave those alone renders the colourless one as a bright scarlet oval
        /// that looks like a rug. There is no single value that serves both, so there are two.
        /// </summary>
        // DARKER AGAIN. With every pool now coming through one colourless texture, the tint
        // is the whole colour -- and 0.38 through a plain white pool is a bright pillar-box
        // red. This sits where the game's own blood pools sit.
        // Only used when DrawOurOwn is on. Brighter than the 0.24 that made our pools read as
        // near-black holes beside the game's own: this is roughly where GTA's own pools sit.
        public float PoolRed = 0.5f;
        public float PoolGreen = 0.07f;
        public float PoolBlue = 0.06f;

        // ---- loading ---------------------------------------------------------

        public static Settings Load()
        {
            var cfg = new Settings();

            try
            {
                var ini = IniFile.Load(Paths.Ini);

                if (ini == null)
                {
                    Log.Warn("No " + Paths.Stem + ".ini beside the dll - running on defaults.");
                    return cfg;
                }

                cfg.Enabled = ini.GetBool("General", "Enabled", cfg.Enabled);
                cfg.AnnounceOnLoad = ini.GetBool("General", "AnnounceOnLoad", cfg.AnnounceOnLoad);
                cfg.LogLevel = ParseLevel(ini.GetString("General", "LogLevel", "Info"), cfg.LogLevel);
                cfg.MenuKey = ini.GetKey("General", "MenuKey", cfg.MenuKey);
                cfg.ControllerMenu = ini.GetBool("General", "ControllerMenu", cfg.ControllerMenu);
                cfg.ControllerHoldMs = ini.GetInt("General", "ControllerHoldMs",
                                                  cfg.ControllerHoldMs, 100, 3000);

                cfg.Level = ParseGoreLevel(ini.GetString("Intensity", "Level", "Mess"), cfg.Level);
                cfg.Multiplier = ini.GetFloat("Intensity", "Multiplier", cfg.Multiplier, 0.1f, 5f);

                cfg.WoundsEnabled = ini.GetBool("Wounds", "Enabled", cfg.WoundsEnabled);
                cfg.WoundsPerHit = ini.GetInt("Wounds", "PerHit", cfg.WoundsPerHit, 0, 12);
                cfg.WoundScale = ini.GetFloat("Wounds", "Scale", cfg.WoundScale, 0.2f, 4f);
                cfg.DamagePacks = ini.GetBool("Wounds", "DamagePacks", cfg.DamagePacks);
                cfg.PlayerWounds = ini.GetBool("Wounds", "PlayerWounds", cfg.PlayerWounds);

                cfg.SprayEnabled = ini.GetBool("Spray", "Enabled", cfg.SprayEnabled);
                cfg.SprayPerHit = ini.GetInt("Spray", "PerHit", cfg.SprayPerHit, 0, 40);
                cfg.SprayGroundDrops = ini.GetInt("Spray", "GroundDrops",
                                                  cfg.SprayGroundDrops, 0, 64);
                cfg.SprayRange = ini.GetFloat("Spray", "Range", cfg.SprayRange, 0.5f, 15f);
                cfg.SpraySpread = ini.GetFloat("Spray", "Spread", cfg.SpraySpread, 0f, 1.5f);
                cfg.SprayMinSize = ini.GetFloat("Spray", "MinSize", cfg.SprayMinSize, 0.02f, 2f);
                cfg.SprayMaxSize = ini.GetFloat("Spray", "MaxSize", cfg.SprayMaxSize, 0.05f, 4f);
                cfg.SprayOpacity = ini.GetFloat("Spray", "Opacity", cfg.SprayOpacity, 0.05f, 1f);
                cfg.SprayOnWalls = ini.GetBool("Spray", "OnWalls", cfg.SprayOnWalls);
                cfg.CorpseShots = ini.GetInt("Spray", "CorpseShots", cfg.CorpseShots, 0, 500);
                cfg.SprayMist = ini.GetBool("Spray", "Mist", cfg.SprayMist);
                cfg.SprayMistChance = ini.GetFloat("Spray", "MistChance",
                                                   cfg.SprayMistChance, 0f, 1f);
                cfg.SprayParticles = ini.GetBool("Spray", "Particles", cfg.SprayParticles);
                cfg.SprayParticleScale = ini.GetFloat("Spray", "ParticleScale",
                                                      cfg.SprayParticleScale, 0.1f, 4f);

                cfg.PoolsEnabled = ini.GetBool("Pools", "Enabled", cfg.PoolsEnabled);
                cfg.PoolDelay = ini.GetFloat("Pools", "Delay", cfg.PoolDelay, 0f, 60f);
                cfg.PoolGrowSeconds = ini.GetFloat("Pools", "GrowSeconds", cfg.PoolGrowSeconds, 1f, 300f);
                cfg.PoolStartSize = ini.GetFloat("Pools", "StartSize", cfg.PoolStartSize, 0.05f, 3f);
                cfg.PoolMaxSize = ini.GetFloat("Pools", "MaxSize", cfg.PoolMaxSize, 0.1f, 6f);
                cfg.PoolSteps = ini.GetInt("Pools", "Steps", cfg.PoolSteps, 1, 20);
                cfg.PoolsFromWounded = ini.GetBool("Pools", "FromWounded", cfg.PoolsFromWounded);
                cfg.PoolVariedTextures = ini.GetBool("Pools", "VariedTextures",
                                                     cfg.PoolVariedTextures);
                cfg.PoolsDrawOurOwn = ini.GetBool("Pools", "DrawOurOwn", cfg.PoolsDrawOurOwn);

                cfg.DripsEnabled = ini.GetBool("Drips", "Enabled", cfg.DripsEnabled);
                cfg.DripDistance = ini.GetFloat("Drips", "Distance", cfg.DripDistance, 0.2f, 10f);
                cfg.DripSize = ini.GetFloat("Drips", "Size", cfg.DripSize, 0.02f, 1f);
                cfg.DripOpacity = ini.GetFloat("Drips", "Opacity", cfg.DripOpacity, 0.05f, 1f);

                cfg.FootprintsEnabled = ini.GetBool("Footprints", "Enabled", cfg.FootprintsEnabled);
                cfg.FootprintSteps = ini.GetInt("Footprints", "Steps", cfg.FootprintSteps, 1, 60);
                cfg.FootprintPickupRadius = ini.GetFloat("Footprints", "PickupRadius",
                                                         cfg.FootprintPickupRadius, 0.1f, 5f);
                cfg.FootprintStride = ini.GetFloat("Footprints", "Stride", cfg.FootprintStride, 0.15f, 3f);
                cfg.FootprintWidth = ini.GetFloat("Footprints", "Width", cfg.FootprintWidth, 0.03f, 1f);
                cfg.FootprintLength = ini.GetFloat("Footprints", "Length", cfg.FootprintLength, 0.05f, 1.5f);
                cfg.FootprintOpacity = ini.GetFloat("Footprints", "Opacity", cfg.FootprintOpacity, 0.05f, 1f);
                cfg.FootprintWetSeconds = ini.GetFloat("Footprints", "WetSeconds",
                                                       cfg.FootprintWetSeconds, 1f, 3600f);
                cfg.FootprintsForPlayer = ini.GetBool("Footprints", "ForPlayer", cfg.FootprintsForPlayer);
                cfg.FootprintsForNpcs = ini.GetBool("Footprints", "ForNpcs", cfg.FootprintsForNpcs);
                cfg.FootprintMaxNpcs = ini.GetInt("Footprints", "MaxNpcs", cfg.FootprintMaxNpcs, 0, 30);
                cfg.FootprintGameEffect = ini.GetBool("Footprints", "GameFootEffect", cfg.FootprintGameEffect);

                cfg.WheelTracksEnabled = ini.GetBool("Wheels", "Enabled", cfg.WheelTracksEnabled);
                cfg.WheelTrackSteps = ini.GetInt("Wheels", "Steps", cfg.WheelTrackSteps, 1, 60);
                cfg.WheelTrackSize = ini.GetFloat("Wheels", "Size", cfg.WheelTrackSize, 0.05f, 2f);
                cfg.WheelTrackOpacity = ini.GetFloat("Wheels", "Opacity", cfg.WheelTrackOpacity, 0.05f, 1f);

                // CEILING RAISED FROM 900. The ini asks for more than that now, and GetInt
                // CLAMPS rather than failing -- so the old ceiling would have quietly capped
                // the budget and made "double the blood" not actually double it, with only a
                // line in the log to say so.
                cfg.MaxSplatters = ini.GetInt("Budget", "MaxSplatters", cfg.MaxSplatters, 8, 2000);
                cfg.MaxPools = ini.GetInt("Budget", "MaxPools", cfg.MaxPools, 0, 300);
                cfg.DecalsPerSecond = ini.GetInt("Budget", "PerSecond", cfg.DecalsPerSecond, 1, 400);
                cfg.DecalRange = ini.GetFloat("Budget", "Range", cfg.DecalRange, 5f, 300f);
                cfg.ProbesPerFrame = ini.GetInt("Budget", "ProbesPerFrame",
                                                cfg.ProbesPerFrame, 1, 64);
                cfg.FadeSeconds = ini.GetFloat("Budget", "FadeSeconds", cfg.FadeSeconds, 0f, 7200f);
                cfg.DecalTimeout = ini.GetFloat("Budget", "Timeout", cfg.DecalTimeout, 0f, 1000000f);

                cfg.BiggerBloodParticles = ini.GetBool("Game", "BiggerBloodParticles",
                                                       cfg.BiggerBloodParticles);
                cfg.BulletImpactScale = ini.GetFloat("Game", "BulletImpactScale",
                                                     cfg.BulletImpactScale, 0.1f, 10f);
                cfg.BulletImpactRange = ini.GetFloat("Game", "BulletImpactRange",
                                                     cfg.BulletImpactRange, 0.1f, 10f);
                cfg.ShotgunDecals = ini.GetBool("Game", "ShotgunDecals", cfg.ShotgunDecals);
                cfg.ClownBlood = ini.GetBool("Game", "ClownBlood", cfg.ClownBlood);
                cfg.AlienBlood = ini.GetBool("Game", "AlienBlood", cfg.AlienBlood);

                cfg.LegsEnabled = ini.GetBool("Legs", "Enabled", cfg.LegsEnabled);
                cfg.LegsChance = ini.GetFloat("Legs", "Chance", cfg.LegsChance, 0f, 1f);
                cfg.LegsMinDamage = ini.GetFloat("Legs", "MinDamage", cfg.LegsMinDamage, 0f, 500f);
                cfg.LegsHealth = ini.GetFloat("Legs", "Health", cfg.LegsHealth, 1f, 200f);
                cfg.LegsSeconds = ini.GetFloat("Legs", "Seconds", cfg.LegsSeconds, 2f, 600f);
                cfg.LegsBleedOut = ini.GetBool("Legs", "BleedOut", cfg.LegsBleedOut);

                cfg.HeadsEnabled = ini.GetBool("Heads", "Enabled", cfg.HeadsEnabled);
                cfg.HeadsChance = ini.GetFloat("Heads", "Chance", cfg.HeadsChance, 0f, 1f);
                cfg.HeadsMinDamage = ini.GetFloat("Heads", "MinDamage", cfg.HeadsMinDamage, 0f, 500f);
                cfg.HeadsIncludePlayer = ini.GetBool("Heads", "IncludePlayer", cfg.HeadsIncludePlayer);
                cfg.HeadsNeckEffect = ini.GetBool("Heads", "NeckEffect", cfg.HeadsNeckEffect);

                cfg.BloodRed = ini.GetFloat("Appearance", "BloodRed", cfg.BloodRed, 0f, 1f);
                cfg.BloodGreen = ini.GetFloat("Appearance", "BloodGreen", cfg.BloodGreen, 0f, 1f);
                cfg.BloodBlue = ini.GetFloat("Appearance", "BloodBlue", cfg.BloodBlue, 0f, 1f);

                cfg.PoolRed = ini.GetFloat("Appearance", "PoolRed", cfg.PoolRed, 0f, 1f);
                cfg.PoolGreen = ini.GetFloat("Appearance", "PoolGreen", cfg.PoolGreen, 0f, 1f);
                cfg.PoolBlue = ini.GetFloat("Appearance", "PoolBlue", cfg.PoolBlue, 0f, 1f);

                Log.Level = cfg.LogLevel;
                cfg.Validate();
            }
            catch (Exception ex)
            {
                Log.Error("Failed reading settings - using defaults.", ex);
            }

            return cfg;
        }

        /// <summary>
        /// Writes one setting back to the ini, so a change made in the menu survives a restart.
        ///
        /// Returns false rather than throwing. A settings screen that cannot write is a
        /// setting that does not stick, which is worth reporting; it is not worth taking the
        /// mod down over.
        /// </summary>
        public static bool Save(string section, string key, string value)
        {
            return IniFile.SetValue(Paths.Ini, section, key, value);
        }

        public static bool Save(string section, string key, bool value)
        {
            return Save(section, key, value ? "true" : "false");
        }

        public static bool Save(string section, string key, float value)
        {
            return Save(section, key, value.ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture));
        }

        public static bool Save(string section, string key, int value)
        {
            return Save(section, key,
                        value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Catches the settings that contradict each other, and says so rather than behaving
        /// oddly in silence.
        /// </summary>
        private void Validate()
        {
            if (SprayMinSize > SprayMaxSize)
            {
                Log.Warn("[Spray] MinSize (" + SprayMinSize.ToString("0.00") + ") is above MaxSize (" +
                         SprayMaxSize.ToString("0.00") + "). Swapping them.");

                var t = SprayMinSize;
                SprayMinSize = SprayMaxSize;
                SprayMaxSize = t;
            }

            if (PoolStartSize > PoolMaxSize)
            {
                Log.Warn("[Pools] StartSize (" + PoolStartSize.ToString("0.00") + ") is above MaxSize (" +
                         PoolMaxSize.ToString("0.00") + "). Swapping them - a pool has to grow, not shrink.");

                var t = PoolStartSize;
                PoolStartSize = PoolMaxSize;
                PoolMaxSize = t;
            }

            // Footprints need something to stand in. Say it plainly rather than leaving
            // somebody wondering why the feature they turned the mod on for does nothing.
            if (FootprintsEnabled && !PoolsEnabled && !SprayEnabled && !DripsEnabled)
            {
                Log.Warn("[Footprints] Enabled, but pools, spray and drips are all off - there " +
                         "will be no blood on the ground to walk through, so no prints.");
            }
        }

        private static GoreLevel ParseGoreLevel(string text, GoreLevel fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;

            switch (text.Trim().ToLowerInvariant())
            {
                case "tame":
                case "light": return GoreLevel.Tame;
                case "bloody": return GoreLevel.Bloody;
                case "mess": return GoreLevel.Mess;
                case "abattoir":
                case "abbatoir":
                case "slaughterhouse": return GoreLevel.Abattoir;
                default:
                    Log.Warn("[Intensity] Level = '" + text + "' is not a level name " +
                             "(Tame, Bloody, Mess, Abattoir) - using " + fallback + ".");
                    return fallback;
            }
        }

        private static LogLevel ParseLevel(string text, LogLevel fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;

            switch (text.Trim().ToLowerInvariant())
            {
                case "error": return Core.LogLevel.Error;
                case "warn":
                case "warning": return Core.LogLevel.Warn;
                case "info": return Core.LogLevel.Info;
                case "debug": return Core.LogLevel.Debug;
                default: return fallback;
            }
        }
    }
}
