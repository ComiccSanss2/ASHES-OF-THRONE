using System;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The player's weapons, kept on this computer between sessions (PlayerPrefs), per class:
    /// the starting weapon is always owned; the relic (Cinder Oath for the knight, Thornwraith for
    /// the archer) is won from the chest that appears when The Ashen King falls.
    /// What is equipped is replicated by PlayerController so everybody sees the right weapon.
    /// </summary>
    public static class Inventory
    {
        public const byte BaseWeapon = 0, Relic = 1, Dragon = 2, Radiant = 3;
        public const int WeaponCount = 4;

        public static event Action Changed;

        public static bool OwnsRelic(PlayerClass c) => PlayerPrefs.GetInt(SaveSlots.K("df_relic_" + (int)c), 0) == 1;

        public static void GrantRelic(PlayerClass c)
        {
            PlayerPrefs.SetInt(SaveSlots.K("df_relic_" + (int)c), 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static bool OwnsDragon(PlayerClass c) => PlayerPrefs.GetInt(SaveSlots.K("df_dragon_" + (int)c), 0) == 1;
        public static bool OwnsRadiant(PlayerClass c) => PlayerPrefs.GetInt(SaveSlots.K("df_radiant_" + (int)c), 0) == 1;
        public static bool Owns(PlayerClass c, byte w) => w == BaseWeapon || (w == Relic && OwnsRelic(c)) || (w == Dragon && OwnsDragon(c)) || (w == Radiant && OwnsRadiant(c));

        public static void GrantRadiant(PlayerClass c)
        {
            PlayerPrefs.SetInt(SaveSlots.K("df_radiant_" + (int)c), 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Unlocks a tier for a class (1 relic, 2 dragon, 3 radiant).</summary>
        public static void Grant(PlayerClass c, byte tier)
        {
            if (tier == Relic) GrantRelic(c); else if (tier == Dragon) GrantDragon(c); else if (tier == Radiant) GrantRadiant(c);
        }

        public static void GrantDragon(PlayerClass c)
        {
            PlayerPrefs.SetInt(SaveSlots.K("df_dragon_" + (int)c), 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static byte Equipped(PlayerClass c)
        {
            byte w = (byte)PlayerPrefs.GetInt(SaveSlots.K("df_equip_" + (int)c), 0);
            return w < WeaponCount && Owns(c, w) ? w : BaseWeapon;
        }

        public static void SetEquipped(PlayerClass c, byte weapon)
        {
            if (weapon >= WeaponCount || !Owns(c, weapon)) return;
            PlayerPrefs.SetInt(SaveSlots.K("df_equip_" + (int)c), weapon);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static string Name(PlayerClass c, byte w) => c switch
        {
            PlayerClass.Archer => w == Radiant ? "Starwoven" : w == Dragon ? "Wyrmwing" : w == Relic ? "Thornwraith" : "Hunter's War-bow",
            PlayerClass.Bard => w == Radiant ? "Seraph's Citole" : w == Dragon ? "Vaelthrax's Lament" : w == Relic ? "Voice of the Pyre" : "Wanderer's Mandolin",
            PlayerClass.Smoker => w == Radiant ? "Censer of Dawn" : w == Dragon ? "Wyrmflue" : w == Relic ? "The King's Last Pipe" : "Old Churchwarden",
            PlayerClass.Reaper => w == Radiant ? "Dawnsickle" : w == Dragon ? "Wyrmreaver" : w == Relic ? "Widow of the Pyre" : "Gravewarden's Scythe",
            _ => w == Radiant ? "Dawnbreaker" : w == Dragon ? "Wyrmfang" : w == Relic ? "Cinder Oath" : "Knight's Greatsword",
        };

        public static string Description(PlayerClass c, byte w)
        {
            if (c == PlayerClass.Smoker)
                return w == Radiant ? "A pipe of white porcelain and gold, its bowl a censer from the temple. Its smoke smells of incense and burns like dawn.\n+65% smoke damage · golden smoke · cheaper smoke"
                     : w == Dragon ? "Carved from a horn of Vaelthrax; the bowl never needs lighting. Its spirits leave embers where they strike.\n+50% smoke damage · ember smoke · cheaper smoke"
                     : w == Relic ? "The Ashen King smoked this by the pyre every night after the Queen died. The ash in its bowl is hers.\n+35% smoke damage · cheaper smoke"
                     : "A churchwarden pipe as long as a staff, stained black by forty years of cheap leaf. The smoke has a mind of its own.";
            if (c == PlayerClass.Reaper)
                return w == Radiant ? "A crescent of moon-silver on a gilded snath. It reaps the dark itself; even the Glass flinches from it.\n+65% damage · +45% poise damage · starlight arcs"
                     : w == Dragon ? "A curved fang of Vaelthrax lashed to a shaft of its own spine. The blade drips embers like blood.\n+50% damage · +35% poise damage · burning arcs"
                     : w == Relic ? "The Ashen King's executioner's scythe, its blade quenched in the pyre that took his queen.\n+35% damage · +25% poise damage"
                     : "The prison gravedigger's scythe, honed for the dead who would not lie still. Long, light, merciless.";
            if (w == Radiant)
                return c == PlayerClass.Bard
                    ? "Silver-strung and gilded, sealed in the vault before the Glass came. Its notes fall like light through a chapel window.\n+65% spell damage · golden notes · spells cost 35% less"
                    : c == PlayerClass.Archer
                    ? "Moon-silver limbs, a string spun from a star that fell on the prison roof. Each arrow is a little comet.\n+65% damage · starlight arrows · fastest shots"
                    : "Forged at dawn by the order that built this prison, for the day the Glass would wake. Never shed blood in anger.\n+65% damage · +45% poise damage · starlight slashes";
            if (w == Dragon)
                return c == PlayerClass.Bard
                    ? "Strung with the wyrm's sinew, its bowl scaled in crimson; the strings glow like coals.\n+50% spell damage · fire notes · spells cost 30% less"
                    : c == PlayerClass.Archer
                    ? "Two wing-bones of Vaelthrax, the membrane still warm. Its arrows leave the string burning.\n+50% damage · flaming arrows · fastest shots"
                    : "A fang of Vaelthrax set in obsidian, serrated like its jaw; the blood-red edge never cools.\n+50% damage · +35% poise damage · burning slashes";
            if (c == PlayerClass.Bard)
                return w == Relic
                    ? "A blackened mandolin bound in gold, an ember gem set in its head: every note burns.\n+35% spell damage · faster bolts · cheaper spells"
                    : "An old bowl-backed mandolin, worn smooth by a lifetime of roads. Its notes cut like blades.";
            if (c == PlayerClass.Archer)
                return w == Relic
                    ? "Bone and blackwood, strung with a dead king's sinew. Runes glow along its limbs.\n+35% damage · faster arrows"
                    : "A plain recurve of yew and leather. Reliable.";
            return w == Relic
                ? "A flamberge forged in the Ashen King's pyre; embers still run through the blade.\n+35% damage · +25% poise damage"
                : "Two-handed steel, notched by many battles.";
        }


        // ================================================================== armour
        // Sets won from the bosses (every class can wear them: same rig). 0 = none (the class's own clothes).
        public const byte ArmorNone = 0, ArmorAshen = 1, ArmorWyrm = 2, ArmorMirror = 3, ArmorDeath = 4;
        public const int ArmorSetCount = 5, ArmorSlotCount = 4;
        public enum Slot : byte { Helmet, Chest, Legs, Boots }

        public static bool OwnsArmorSet(byte set) => set == ArmorNone || PlayerPrefs.GetInt(SaveSlots.K("df_aset_" + set), 0) == 1;

        public static void GrantArmorSet(byte set)
        {
            if (set == ArmorNone || set >= ArmorSetCount) return;
            PlayerPrefs.SetInt(SaveSlots.K("df_aset_" + set), 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static byte EquippedArmor(PlayerClass c, Slot s)
        {
            byte set = (byte)PlayerPrefs.GetInt(SaveSlots.K($"df_armor_{(int)c}_{(int)s}"), 0);
            return set < ArmorSetCount && OwnsArmorSet(set) ? set : ArmorNone;
        }

        public static void SetArmor(PlayerClass c, Slot s, byte set)
        {
            if (set >= ArmorSetCount || !OwnsArmorSet(set)) return;
            PlayerPrefs.SetInt(SaveSlots.K($"df_armor_{(int)c}_{(int)s}"), set);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Four slots packed in 16 bits (4 bits each): what the network replicates.</summary>
        public static ushort PackArmor(PlayerClass c)
        {
            int v = 0;
            for (int i = 0; i < ArmorSlotCount; i++) v |= EquippedArmor(c, (Slot)i) << (i * 4);
            return (ushort)v;
        }

        public static byte Unpack(ushort packed, Slot s) => (byte)((packed >> ((int)s * 4)) & 0xF);

        public static string SetName(byte set) => set switch
        {
            ArmorAshen => "Regalia of the Ashen King", ArmorWyrm => "Wyrmscale", ArmorMirror => "Mirrorborn", ArmorDeath => "Deathshroud", _ => "Own clothes",
        };

        public static string SlotName(Slot s) => s switch { Slot.Helmet => "Helmet", Slot.Chest => "Armour", Slot.Legs => "Legs", _ => "Boots" };

        public static string ArmorName(byte set, Slot s) => set switch
        {
            ArmorAshen => s switch { Slot.Helmet => "Ashen Crown-Helm", Slot.Chest => "Ashen Cuirass", Slot.Legs => "Ashen Greaves", _ => "Ashen Sabatons" },
            ArmorWyrm => s switch { Slot.Helmet => "Horned Wyrm Helm", Slot.Chest => "Wyrmscale Hauberk", Slot.Legs => "Wyrmscale Legguards", _ => "Wyrm Talons" },
            ArmorMirror => s switch { Slot.Helmet => "Crown of Shattered Light", Slot.Chest => "Mirrorborn Cuirass", Slot.Legs => "Mirrorborn Greaves", _ => "Mirrorborn Sollerets" },
            ArmorDeath => s switch { Slot.Helmet => "Deathshroud Cowl", Slot.Chest => "Deathshroud Mantle", Slot.Legs => "Deathshroud Leggings", _ => "Deathshroud Treads" },
            _ => "—",
        };

        /// <summary>Damage taken reduction of one piece, once its boss has fallen in this lobby.</summary>
        public static float ArmorReduction(byte set) => set switch { ArmorAshen => 0.035f, ArmorWyrm => 0.045f, ArmorMirror => 0.055f, ArmorDeath => 0.065f, _ => 0f };

        public static string ArmorDescription(byte set, Slot s)
        {
            string lore = set switch
            {
                ArmorAshen => "Blackened steel edged in the King's gold, still warm from the pyre that took his queen.",
                ArmorWyrm => "Scales of Vaelthrax riveted on red leather. Embers smoulder between the plates.",
                ArmorMirror => "Silver-white plate set with shards of the broken Mirror. Every stolen reflection that flew home left a little light in it.",
                ArmorDeath => "Woven from the shadow the Reaper wore. It is cold, and it is quiet, and it does not want to be taken off.",
                _ => "",
            };
            return lore + $"\nDamage taken -{ArmorReduction(set) * 100f:0.#}% (once its boss has fallen in this lobby).";
        }

        public static string RelicName(PlayerClass c) => Name(c, Relic);
    }
}
