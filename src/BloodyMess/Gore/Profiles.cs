using System;
using System.Collections.Generic;
using GTA;
using BloodyMess.Core;

namespace BloodyMess.Gore
{
    /// <summary>What one kind of weapon does to a body.</summary>
    internal sealed class Profile
    {
        public string Name = "default";

        /// <summary>Particle effect at the entry wound. A game effect name from the core asset.</summary>
        public string EntryFx = "blood_entry";

        /// <summary>Particle effect thrown out the far side.</summary>
        public string ExitFx = "blood_exit";

        /// <summary>Used instead of EntryFx when the hit was to the head.</summary>
        public string HeadFx = "blood_headshot";

        /// <summary>Which of the game's wound decals is stamped on the body.</summary>
        public PedBloodDamage Wound = PedBloodDamage.BulletSmall;

        /// <summary>The heavier wound, used on a fatal or a big hit.</summary>
        public PedBloodDamage BigWound = PedBloodDamage.BulletLarge;

        /// <summary>
        /// A built-in damage pack applied on a fatal hit, or empty for none.
        ///
        /// These are the game's own pre-authored sets of wounds and torn clothing. A name the
        /// game does not recognise is IGNORED by the native rather than raising anything, so a
        /// wrong one here costs an effect and nothing else.
        /// </summary>
        public string DamagePack = "";

        /// <summary>How hard this throws blood, against the settings' spray count.</summary>
        public float Spray = 1f;

        /// <summary>Multiplier on the particle effect size.</summary>
        public float FxScale = 1f;
    }

    /// <summary>
    /// The weapon-to-gore table, loaded from gore.json.
    ///
    /// A DATA FILE RATHER THAN A SWITCH STATEMENT, because the interesting part of a mod like
    /// this is the tuning, and tuning that needs a rebuild is tuning nobody ever does. It is
    /// also the honest place to put the effect names: they are the game's, not ours, and
    /// somebody who finds a better one for shotguns should be able to use it without a
    /// compiler.
    ///
    /// A missing or broken file is not fatal. The built-in defaults below cover every weapon
    /// group, so the mod runs unchanged and says once that it fell back.
    /// </summary>
    internal sealed class Profiles
    {
        private readonly Dictionary<WeaponGroup, Profile> _byGroup =
            new Dictionary<WeaponGroup, Profile>();

        private Profile _default = new Profile();

        public int Count => _byGroup.Count;

        /// <summary>True when the shipped table was read, rather than the built-in fallback.</summary>
        public bool FromFile { get; private set; }

        public Profile For(WeaponGroup group)
        {
            return _byGroup.TryGetValue(group, out var profile) ? profile : _default;
        }

        public void Load()
        {
            Defaults();

            try
            {
                var doc = JsonFile.Read(Paths.GoreFile);

                if (doc == null)
                {
                    Log.Warn("No gore.json - using the built-in weapon table.");
                    return;
                }

                var fallback = doc["default"];
                if (fallback != null && !fallback.IsNull) _default = Parse("default", fallback, _default);

                var groups = doc["groups"];

                if (groups == null || groups.IsNull)
                {
                    Log.Warn("gore.json has no \"groups\" object - using the built-in weapon table.");
                    return;
                }

                var loaded = 0;

                foreach (var key in groups.Keys)
                {
                    if (!Enum.TryParse(key, true, out WeaponGroup group))
                    {
                        Log.Warn("gore.json: '" + key + "' is not a weapon group - ignored. " +
                                 "Valid names are the game's own: Pistol, SMG, AssaultRifle, MG, " +
                                 "Shotgun, Sniper, Heavy, Thrown, Melee, Stungun, Unarmed.");
                        continue;
                    }

                    var current = _byGroup.TryGetValue(group, out var existing) ? existing : _default;
                    _byGroup[group] = Parse(key, groups[key], current);
                    loaded++;
                }

                FromFile = loaded > 0;
                Log.Info("Gore table: " + loaded + " weapon group(s) from gore.json.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not read gore.json - using the built-in weapon table.", ex);
            }
        }

        /// <summary>
        /// Reads one profile, falling back FIELD BY FIELD to what was there before.
        ///
        /// Per-field rather than per-profile so that a file which only wants to change the
        /// shotgun's spray does not have to restate its effect names to keep them.
        /// </summary>
        private static Profile Parse(string name, Json node, Profile fallback)
        {
            var profile = new Profile
            {
                Name = name,
                EntryFx = fallback.EntryFx,
                ExitFx = fallback.ExitFx,
                HeadFx = fallback.HeadFx,
                Wound = fallback.Wound,
                BigWound = fallback.BigWound,
                DamagePack = fallback.DamagePack,
                Spray = fallback.Spray,
                FxScale = fallback.FxScale
            };

            if (node == null || node.IsNull) return profile;

            profile.EntryFx = node["entryFx"].AsString(profile.EntryFx);
            profile.ExitFx = node["exitFx"].AsString(profile.ExitFx);
            profile.HeadFx = node["headFx"].AsString(profile.HeadFx);
            profile.DamagePack = node["damagePack"].AsString(profile.DamagePack);
            profile.Spray = node["spray"].AsFloat(profile.Spray);
            profile.FxScale = node["fxScale"].AsFloat(profile.FxScale);

            profile.Wound = ParseWound(node["wound"].AsString(""), profile.Wound, name, "wound");
            profile.BigWound = ParseWound(node["bigWound"].AsString(""), profile.BigWound, name, "bigWound");

            if (profile.Spray < 0f) profile.Spray = 0f;
            if (profile.FxScale < 0.05f) profile.FxScale = 0.05f;

            return profile;
        }

        private static PedBloodDamage ParseWound(string text, PedBloodDamage fallback,
                                                 string group, string field)
        {
            if (string.IsNullOrEmpty(text)) return fallback;

            if (Enum.TryParse(text.Trim(), true, out PedBloodDamage parsed)) return parsed;

            Log.Warn("gore.json: " + group + "." + field + " = '" + text + "' is not a wound type. " +
                     "Valid: BulletSmall, BulletLarge, ShotgunSmall, ShotgunSmallMonolithic, " +
                     "ShotgunLarge, NonFatalHeadshot, Stab, BasicSlash, BackSplash, ScriptedBackSplash.");
            return fallback;
        }

        /// <summary>
        /// The table the mod runs on when there is no file, and the base every file entry is
        /// layered over.
        ///
        /// Kept in code as well as in the shipped json ON PURPOSE: a player who deletes or
        /// breaks the json should get a mod that still works, not a mod that draws nothing and
        /// makes them read a log to find out why.
        /// </summary>
        private void Defaults()
        {
            _default = new Profile();
            _byGroup.Clear();

            _byGroup[WeaponGroup.Pistol] = new Profile
            {
                Name = "Pistol",
                Spray = 1f,
                FxScale = 1f
            };

            _byGroup[WeaponGroup.SMG] = new Profile
            {
                Name = "SMG",
                Spray = 1.1f,
                FxScale = 1.05f
            };

            _byGroup[WeaponGroup.AssaultRifle] = new Profile
            {
                Name = "AssaultRifle",
                Wound = PedBloodDamage.BulletLarge,
                Spray = 1.4f,
                FxScale = 1.2f
            };

            _byGroup[WeaponGroup.MG] = new Profile
            {
                Name = "MG",
                Wound = PedBloodDamage.BulletLarge,
                Spray = 1.6f,
                FxScale = 1.3f
            };

            _byGroup[WeaponGroup.Shotgun] = new Profile
            {
                Name = "Shotgun",
                EntryFx = "blood_entry_shotgun",
                Wound = PedBloodDamage.ShotgunSmall,
                BigWound = PedBloodDamage.ShotgunLarge,
                DamagePack = "SCR_Dumpster",
                Spray = 2.4f,
                FxScale = 1.6f
            };

            _byGroup[WeaponGroup.Sniper] = new Profile
            {
                Name = "Sniper",
                EntryFx = "blood_entry_sniper",
                HeadFx = "blood_entry_head_sniper",
                Wound = PedBloodDamage.BulletLarge,
                BigWound = PedBloodDamage.ShotgunLarge,
                Spray = 2.6f,
                FxScale = 1.8f
            };

            _byGroup[WeaponGroup.Heavy] = new Profile
            {
                Name = "Heavy",
                Wound = PedBloodDamage.ShotgunLarge,
                BigWound = PedBloodDamage.ShotgunLarge,
                DamagePack = "Explosion_Med",
                Spray = 3f,
                FxScale = 2f
            };

            _byGroup[WeaponGroup.Thrown] = new Profile
            {
                Name = "Thrown",
                Wound = PedBloodDamage.ShotgunLarge,
                BigWound = PedBloodDamage.ShotgunLarge,
                DamagePack = "Explosion_Med",
                Spray = 2.6f,
                FxScale = 1.8f
            };

            _byGroup[WeaponGroup.Melee] = new Profile
            {
                Name = "Melee",
                EntryFx = "blood_stab",
                ExitFx = "blood_melee_blunt",
                HeadFx = "blood_melee_punch",
                Wound = PedBloodDamage.BasicSlash,
                BigWound = PedBloodDamage.Stab,
                Spray = 1.2f,
                FxScale = 1.1f
            };

            _byGroup[WeaponGroup.Unarmed] = new Profile
            {
                Name = "Unarmed",
                EntryFx = "blood_melee_punch",
                ExitFx = "blood_melee_punch",
                HeadFx = "blood_nose",
                Wound = PedBloodDamage.BasicSlash,
                BigWound = PedBloodDamage.BasicSlash,
                Spray = 0.4f,
                FxScale = 0.8f
            };

            // A taser draws no blood in the game and should not draw any here either. It gets
            // a profile rather than being left to the default precisely so it stays bloodless.
            _byGroup[WeaponGroup.Stungun] = new Profile
            {
                Name = "Stungun",
                EntryFx = "blood_stungun",
                ExitFx = "",
                HeadFx = "blood_stungun",
                Wound = PedBloodDamage.BulletSmall,
                BigWound = PedBloodDamage.BulletSmall,
                Spray = 0f,
                FxScale = 0.6f
            };
        }
    }
}
