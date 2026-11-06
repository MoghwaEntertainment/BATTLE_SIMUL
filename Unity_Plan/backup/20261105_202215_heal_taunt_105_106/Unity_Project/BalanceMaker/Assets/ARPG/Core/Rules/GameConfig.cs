using System;
using System.Collections.Generic;

namespace ARPG.Core
{
    [Serializable] public sealed class GameConfigFileDto
    {
        public int schemaVersion;
        public GameConfigEntryDto[] entries;
    }
    [Serializable] public sealed class GameConfigEntryDto
    {
        public string id;
        public string valueType;
        public double rawValue;
        public double value; // RATE has already been divided by 100000 by the exporter.
    }

    /// <summary>Shared by Unity and CoreRunner. Apply after rules.json, atomically.</summary>
    public static class GameConfigRules
    {
        public static CombatRules Apply(CombatRules original, GameConfigFileDto config)
        {
            if (config == null || config.schemaVersion != 1 || config.entries == null)
                throw new ArgumentException("Invalid gameconfig.json schema");
            var r = original.Clone();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in config.entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.id) || !seen.Add(e.id))
                    throw new ArgumentException("Empty or duplicate GameConfig ID");
                double v = e.value;
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0 ||
                    (e.valueType != "RATE" && e.valueType != "FLAT"))
                    throw new ArgumentException("Invalid GameConfig value: " + e.id);
                bool time = e.id.EndsWith("_TIME", StringComparison.Ordinal);
                if ((time && (e.valueType != "FLAT" || v <= 0 || v * r.tickRate > int.MaxValue)) ||
                    (e.id.EndsWith("_PCT", StringComparison.Ordinal) && e.valueType != "RATE"))
                    throw new ArgumentException("Invalid GameConfig unit: " + e.id);
                switch (e.id)
                {
                    case "STUN_TIME": r.stunTicks = r.SecToTicks(v); break;
                    case "STUN_ADD_DAMAGE_PCT": r.groggyDamageMult = 1 + v; break;
                    case "WOUND_DAMAGE_PCT": r.woundMaxHpRatio = v; break;
                    case "POISON_TIME": r.poisonDurationSec = v; break;
                    case "POISON_DAMAGE_PCT": r.poisonMaxHpRatioPerSec = v; break;
                    case "FROST_TIME": r.frostDurationSec = v; break;
                    case "FROST_SLOWDOWN_PCT":
                        if (v > 1) throw new ArgumentException("FROST_SLOWDOWN_PCT must be 0..100000");
                        r.frostSpeedMult = 1 - v; break;
                    case "STAGGER_TIME": r.staggerTicks = r.SecToTicks(v); break;
                    case "GUARD_BREAK_TIME": r.guardBreakTicks = r.SecToTicks(v); break;
                    case "GUARD_BREAK_ADD_DAMAGE_PCT": r.guardBreakDamageMult = 1 + v; break;
                    // Preserve other game-wide entries for future consumers.
                }
            }
            return r;
        }
    }
}
