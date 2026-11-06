using System;

// Tools/ExcelExport/export_tables.py 가 만드는 JSON 과 1:1 대응하는 DTO.
// Unity JsonUtility 로 읽을 수 있도록 public 필드 + [Serializable] 만 사용한다(Core 는 UnityEngine 을 참조하지 않음).
namespace ARPG.Core
{
    [Serializable]
    public sealed class CharacterFileDto
    {
        public int schemaVersion;
        public string[] statNames;
        public CharacterDto[] characters;
    }

    [Serializable]
    public sealed class CharacterDto
    {
        public string id;
        public bool enabled;
        public bool isMonster;
        public string displayName;
        public string baseName;
        public string kind;
        public int gameCycle;
        public int level;
        public string movesetId;
        public string aiProfileId;
        public string sourceAiId;
        public int nameKey;
        public double[] stats;
    }

    [Serializable]
    public sealed class SkillFileDto
    {
        public int schemaVersion;
        public SkillDto[] skills;
    }

    [Serializable]
    public sealed class SkillDto
    {
        public string id;
        public bool enabled;
        public bool valid;
        public string skillType;
        public string weaponType;
        public string slot;
        public double costHp;
        public double costFp;
        public double costStamina;
        public string target;
        public double aniSpeed;
        public double aniTimeMs;
        public double[] hitTimesMs;
        public double cancelTimeMs;
        public double addPoise;
        public double poiseStartMs;
        public double poiseEndMs;
        public double trackingStartMs;
        public double trackingEndMs;
        public double trackingAngleDeg;
        public double moveDistance;
        public double effectiveRange;
        public double hitRange;
        public int hitCount;
        public int projectileId;
        public string[] damageTypes;
        public double[] motionValues;
        public double[] motionValuesRaw;
        public string triggerCondition;
        public string triggerType;
        public string buffTarget;
        public int buffId;
        public double triggerChance;
        public double buffDurationMs;
        public double accuracy;
        public bool supportSingle;
        public double extraPoise, extraFrost;
        public int nameKey;
    }

    /// <summary>buffs.json (BUFF.xlsx 의 BUFF 시트). 버프 하나 = 능력치 변경 여러 개.</summary>
    [Serializable]
    public sealed class BuffFileDto
    {
        public int schemaVersion;
        public BuffDto[] buffs;
    }

    [Serializable]
    public sealed class BuffDto
    {
        public int id;                 // Buff_ID (SKILL 의 Buff_ID 가 가리킴)
        public bool enabled;
        public string name;            // ~Name
        public BuffEffectDto[] effects;
        public bool dispellable;
    }

    [Serializable]
    public sealed class BuffEffectDto
    {
        public int abilityId;          // ABILITY Index (예: 17 = 물리 공격력)
        public string stat;            // ABILITY ENUM_Name = 캐릭터 스탯 이름 (예: PHYSICAL_ATTACK)
        public string name;            // ABILITY ~name (표시용)
        public double value;           // 더할 수치(스탯과 같은 단위, 음수 = 감소)
        public int dataType;           // ABILITY Data_Type (0 정수, 1 비율 10만분율) - 표시용
    }

    [Serializable]
    public sealed class MovesetFileDto
    {
        public int schemaVersion;
        public MovesetNodeDto[] nodes;
    }

    [Serializable]
    public sealed class MovesetNodeDto
    {
        public string nodeId;
        public bool enabled;
        public string groupId;
        public string prevNodeId;
        public string inputSlot;
        public string skillId;
    }

    [Serializable]
    public sealed class AiFileDto
    {
        public int schemaVersion;
        public ActionDefDto[] actions;
        public BehaviorDto[] behaviors;
        public PhaseDto[] phases;
        public AiMapDto[] maps;
    }

    [Serializable]
    public sealed class ActionDefDto
    {
        public string actionId;
        public bool enabled;
        public string actionType;
        public string inputSlot;
        public double cooldownSec;
        public string description;
    }

    [Serializable]
    public sealed class BehaviorDto
    {
        public int behaviorIndex;
        public bool enabled;
        public string aiId;
        public int phaseNo;
        public int evalOrder;
        public string condition1;
        public string condition2;
        public double chancePct;
        public string actionId;
        public string skillId;
        public string note;
    }

    [Serializable]
    public sealed class PhaseDto
    {
        public int phaseIndex;
        public bool enabled;
        public string aiId;
        public int phaseNo;
        public string phaseName;
        public double hpMinPct;
        public double hpMaxPct;
        public string enterActionId;
        public double dmgMult;
        public double speedMult;
        public string note;
    }

    [Serializable]
    public sealed class AiMapDto
    {
        public int mapIndex;
        public bool enabled;
        public string monsterName;
        public string kind;
        public string aiId;
        public string note;
    }

    [Serializable]
    public sealed class DataManifestDto
    {
        public int schemaVersion;
        public string toolVersion;
        public string generatedAt;
        public string dataHash;
        public string formulaPolicy;
        public int errorCount;
        public int disabledCount;
        public int warningCount;
    }
}
