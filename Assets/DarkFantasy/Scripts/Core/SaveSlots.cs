using System;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Up to four characters, each with its own name, class and progression (weapons, armour sets,
    /// souls and skills, unlocked flames). Every per-character PlayerPrefs key goes through
    /// <see cref="K"/>, which prefixes it with the selected slot ("s2_df_souls"). The first launch
    /// after this update moves the old single save into slot 1.
    /// </summary>
    public static class SaveSlots
    {
        public const int Max = 4;
        public static event Action Changed;

        static int _current = -2;

        /// <summary>The character being played (-1: none chosen yet).</summary>
        public static int Current
        {
            get
            {
                if (_current == -2)
                {
                    MigrateLegacy();
                    int last = PlayerPrefs.GetInt("df_slot_last", -1);
                    _current = last >= 0 && Exists(last) ? last : -1;
                }
                return _current;
            }
        }

        /// <summary>The per-character form of a key (unchanged when no character is chosen).</summary>
        public static string K(string key) => Current < 0 ? key : Prefix(Current) + key;
        static string Prefix(int slot) => $"s{slot}_";

        public static bool Exists(int i) => PlayerPrefs.GetInt(Prefix(i) + "df_exists", 0) == 1;
        public static string Name(int i) => PlayerPrefs.GetString(Prefix(i) + "df_name", "Nameless");
        public static PlayerClass Class(int i) => (PlayerClass)Mathf.Clamp(PlayerPrefs.GetInt(Prefix(i) + "df_class", 0), 0, 4);
        public static int Souls(int i) => PlayerPrefs.GetInt(Prefix(i) + "df_souls", 0);
        public static int Flame(int i) => PlayerPrefs.GetInt(Prefix(i) + "df_cp_unlocked", 0);
        public static int BossSets(int i)
        {
            int n = 0;
            for (int s = 1; s <= 4; s++) n += PlayerPrefs.GetInt(Prefix(i) + "df_aset_" + s, 0);
            return n;
        }
        public static int SkillRanks(int i)
        {
            int n = 0;
            for (int s = 0; s < 5; s++) n += PlayerPrefs.GetInt(Prefix(i) + "df_skill_" + s, 0);
            return n;
        }

        public static void Select(int i)
        {
            if (!Exists(i)) return;
            _current = i;
            PlayerPrefs.SetInt("df_slot_last", i);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void Create(int i, string name, PlayerClass c)
        {
            Wipe(i);
            string p = Prefix(i);
            PlayerPrefs.SetInt(p + "df_exists", 1);
            PlayerPrefs.SetString(p + "df_name", string.IsNullOrWhiteSpace(name) ? "Nameless" : name.Trim());
            PlayerPrefs.SetInt(p + "df_class", (int)c);
            PlayerPrefs.Save();
            Select(i);
        }

        public static void Delete(int i)
        {
            Wipe(i);
            if (_current == i) { _current = -1; PlayerPrefs.DeleteKey("df_slot_last"); }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Every per-character key (without the slot prefix).</summary>
        static System.Collections.Generic.IEnumerable<string> Keys()
        {
            foreach (var k in new[] { "df_exists", "df_name", "df_class", "df_souls", "df_cp_unlocked", "df_start_cp" }) yield return k;
            for (int s = 0; s < 5; s++) yield return "df_skill_" + s;
            for (int s = 0; s <= 4; s++) yield return "df_aset_" + s;
            for (int c = 0; c < 4; c++)
            {
                yield return "df_relic_" + c; yield return "df_dragon_" + c; yield return "df_radiant_" + c; yield return "df_equip_" + c;
                for (int s = 0; s < 4; s++) yield return $"df_armor_{c}_{s}";
            }
        }

        static void Wipe(int i) { foreach (var k in Keys()) PlayerPrefs.DeleteKey(Prefix(i) + k); }

        /// <summary>The save from before character slots becomes the first character.</summary>
        static void MigrateLegacy()
        {
            if (PlayerPrefs.GetInt("df_slots_migrated", 0) == 1) return;
            PlayerPrefs.SetInt("df_slots_migrated", 1);
            bool any = PlayerPrefs.HasKey("df_name") || PlayerPrefs.HasKey("df_souls") || PlayerPrefs.HasKey("df_relic_0") || PlayerPrefs.HasKey("df_cp_unlocked");
            if (!any || Exists(0)) { PlayerPrefs.Save(); return; }
            foreach (var k in Keys())
            {
                if (!PlayerPrefs.HasKey(k)) continue;
                string dst = Prefix(0) + k;
                if (k == "df_name") PlayerPrefs.SetString(dst, PlayerPrefs.GetString(k));
                else PlayerPrefs.SetInt(dst, PlayerPrefs.GetInt(k));
            }
            PlayerPrefs.SetInt(Prefix(0) + "df_exists", 1);
            if (string.IsNullOrWhiteSpace(PlayerPrefs.GetString(Prefix(0) + "df_name", ""))) PlayerPrefs.SetString(Prefix(0) + "df_name", "Wanderer");
            PlayerPrefs.SetInt("df_slot_last", 0);
            PlayerPrefs.Save();
        }

        public static string ClassName(PlayerClass c) => c switch
        {
            PlayerClass.Archer => "Archer", PlayerClass.Bard => "Bard", PlayerClass.Reaper => "Reaper", PlayerClass.Smoker => "Smoker", _ => "Knight",
        };
    }
}
