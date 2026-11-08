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

        public static bool OwnsRelic(PlayerClass c) => PlayerPrefs.GetInt("df_relic_" + (int)c, 0) == 1;

        public static void GrantRelic(PlayerClass c)
        {
            PlayerPrefs.SetInt("df_relic_" + (int)c, 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static bool OwnsDragon(PlayerClass c) => PlayerPrefs.GetInt("df_dragon_" + (int)c, 0) == 1;
        public static bool OwnsRadiant(PlayerClass c) => PlayerPrefs.GetInt("df_radiant_" + (int)c, 0) == 1;
        public static bool Owns(PlayerClass c, byte w) => w == BaseWeapon || (w == Relic && OwnsRelic(c)) || (w == Dragon && OwnsDragon(c)) || (w == Radiant && OwnsRadiant(c));

        public static void GrantRadiant(PlayerClass c)
        {
            PlayerPrefs.SetInt("df_radiant_" + (int)c, 1);
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
            PlayerPrefs.SetInt("df_dragon_" + (int)c, 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static byte Equipped(PlayerClass c)
        {
            byte w = (byte)PlayerPrefs.GetInt("df_equip_" + (int)c, 0);
            return w < WeaponCount && Owns(c, w) ? w : BaseWeapon;
        }

        public static void SetEquipped(PlayerClass c, byte weapon)
        {
            if (weapon >= WeaponCount || !Owns(c, weapon)) return;
            PlayerPrefs.SetInt("df_equip_" + (int)c, weapon);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static string Name(PlayerClass c, byte w) => c switch
        {
            PlayerClass.Archer => w == Radiant ? "Starwoven" : w == Dragon ? "Wyrmwing" : w == Relic ? "Thornwraith" : "Hunter's War-bow",
            PlayerClass.Bard => w == Radiant ? "Seraph's Citole" : w == Dragon ? "Vaelthrax's Lament" : w == Relic ? "Voice of the Pyre" : "Wanderer's Mandolin",
            _ => w == Radiant ? "Dawnbreaker" : w == Dragon ? "Wyrmfang" : w == Relic ? "Cinder Oath" : "Knight's Greatsword",
        };

        public static string Description(PlayerClass c, byte w)
        {
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

        public static string RelicName(PlayerClass c) => Name(c, Relic);
    }
}
