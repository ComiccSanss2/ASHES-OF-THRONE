using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Light skill tree and its currency ("Souls"), per player, saved on this machine.
    /// Souls come from every monster and boss slain in the party; they buy ranks in five stats.
    /// Everything here goes back to zero when the player joins (or hosts) a lobby that starts
    /// from the very beginning — weapons and armour keep their looks, the trained body does not.
    /// </summary>
    public static class PlayerSkills
    {
        public enum Stat { Vitality, Endurance, Strength, Mind, Arcana }
        public const int StatCount = 5, MaxRank = 10;

        public static event System.Action Changed;
        /// <summary>Souls gained (amount, from a boss).</summary>
        public static event System.Action<int, bool> SoulsGained;

        public static int Souls
        {
            get => PlayerPrefs.GetInt(SaveSlots.K("df_souls"), 0);
            private set { PlayerPrefs.SetInt(SaveSlots.K("df_souls"), Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static int Rank(Stat s) => Mathf.Clamp(PlayerPrefs.GetInt(SaveSlots.K("df_skill_" + (int)s), 0), 0, MaxRank);
        public static int Cost(Stat s) => 80 + 45 * Rank(s);
        public static bool CanBuy(Stat s) => Rank(s) < MaxRank && Souls >= Cost(s);
        public static int TotalRanks { get { int n = 0; for (int i = 0; i < StatCount; i++) n += Rank((Stat)i); return n; } }

        public static bool TryBuy(Stat s)
        {
            if (!CanBuy(s)) return false;
            Souls -= Cost(s);
            PlayerPrefs.SetInt(SaveSlots.K("df_skill_" + (int)s), Rank(s) + 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        public static void AddSouls(int n, bool boss = false)
        {
            if (n <= 0) return;
            Souls += n;
            SoulsGained?.Invoke(n, boss);
            Changed?.Invoke();
        }

        /// <summary>A lobby starting from the beginning: the trained stats and the souls are lost.</summary>
        public static void ResetAll()
        {
            PlayerPrefs.SetInt(SaveSlots.K("df_souls"), 0);
            for (int i = 0; i < StatCount; i++) PlayerPrefs.SetInt(SaveSlots.K("df_skill_" + i), 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
            Debug.Log("[DF] skills and souls reset (the lobby starts from the beginning)");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static void DevGiveSouls(int n) => AddSouls(n);
#endif

        // ------------------------------------------------------------------ effects
        public static float HealthMul => 1f + 0.06f * Rank(Stat.Vitality);
        public static float StaminaMul => 1f + 0.06f * Rank(Stat.Endurance);
        public static float StaminaRegenMul => 1f + 0.04f * Rank(Stat.Endurance);
        public static float StrengthMul => 1f + 0.04f * Rank(Stat.Strength);
        public static float ManaMul => 1f + 0.08f * Rank(Stat.Mind);
        public static float ManaRegenMul => 1f + 0.05f * Rank(Stat.Mind);
        /// <summary>Classes without mana: Mind fills the ultimate faster.</summary>
        public static float UltChargeMul => 1f + 0.06f * Rank(Stat.Mind);
        public static float ArcanaMul => 1f + 0.05f * Rank(Stat.Arcana);

        public static string Name(Stat s) => s switch
        {
            Stat.Vitality => "Vitality", Stat.Endurance => "Endurance", Stat.Strength => "Strength", Stat.Mind => "Mind", _ => "Arcana",
        };

        public static string Describe(Stat s) => s switch
        {
            Stat.Vitality => "The flesh remembers every wound it survived.\n+6% maximum health per rank.",
            Stat.Endurance => "Breath that does not break.\n+6% maximum stamina and +4% stamina recovery per rank.",
            Stat.Strength => "The weight behind the blade, the pull behind the string.\n+4% physical damage per rank.",
            Stat.Mind => "A still mind holds more power.\n+8% maximum mana and +5% mana recovery per rank.\nClasses without mana: the ultimate fills 6% faster per rank.",
            _ => "The old words, spoken true.\n+5% magic damage per rank (bard spells, ultimates).",
        };

        public static string Effect(Stat s, int rank) => s switch
        {
            Stat.Vitality => $"+{6 * rank}% health",
            Stat.Endurance => $"+{6 * rank}% stamina",
            Stat.Strength => $"+{4 * rank}% damage",
            Stat.Mind => $"+{8 * rank}% mana",
            _ => $"+{5 * rank}% magic",
        };
    }

    /// <summary>
    /// Potions carried in this session (Usables): found on fallen monsters. The Ember Flask is the
    /// healing flask on PlayerVitals; these three are extra draughts with timed effects.
    /// </summary>
    public static class Potions
    {
        public enum Kind { IronDraught, VigorTonic, SoulSalve }
        public const int KindCount = 3;
        static readonly int[] _count = new int[KindCount];
        public static event System.Action Changed;

        public static int Count(Kind k) => _count[(int)k];
        public static void Add(Kind k, int n = 1) { _count[(int)k] = Mathf.Min(9, _count[(int)k] + n); Changed?.Invoke(); }
        public static void ResetSession() { for (int i = 0; i < KindCount; i++) _count[i] = 1; Changed?.Invoke(); }
        public static bool Take(Kind k) { if (_count[(int)k] <= 0) return false; _count[(int)k]--; Changed?.Invoke(); return true; }

        public static string Name(Kind k) => k switch { Kind.IronDraught => "Iron Draught", Kind.VigorTonic => "Vigor Tonic", _ => "Soul Salve" };
        public static string Describe(Kind k) => k switch
        {
            Kind.IronDraught => "Bitter and grey, it tastes of a forge. The skin hardens like old armour.\nDamage taken -30% for 30 seconds.",
            Kind.VigorTonic => "Green and fizzing. The lungs burn, then open.\nStamina recovers 60% faster for 30 seconds.",
            _ => "A pale wax that smells of incense. Wounds close slowly, as if someone prayed for you.\nRestores 45% health over 10 seconds.",
        };
    }
}
