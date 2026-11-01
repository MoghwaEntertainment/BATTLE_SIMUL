using System;
using System.Collections.Generic;
using ARPG.Core.AI;

namespace ARPG.Core
{
    public sealed class CharacterDefinition
    {
        public string Id;
        public bool Enabled;
        public bool IsMonster;
        public string DisplayName;
        public string BaseName;
        public string Kind;
        public int GameCycle;
        public int Level;
        public string MovesetId;
        public string AiProfileId;
        public string SourceAiId;
        public readonly double[] Stats = new double[(int)StatId.COUNT];

        public double Stat(StatId id) { return Stats[(int)id]; }
        public override string ToString() { return Id + " " + DisplayName; }
    }

    public sealed class SkillDefinition
    {
        public string Id;
        public bool Enabled;
        public bool Valid;
        public SkillType Type;
        public string WeaponType;
        public string Slot;
        public double CostHp, CostFp, CostStamina;
        public double AniSpeed;
        public double AniTimeMs;
        public double[] HitTimesMs;
        public double[] MotionValues;      // 배율(1.0 = 100%)
        public double CancelTimeMs;
        public double AddPoise, PoiseStartMs, PoiseEndMs;
        public double TrackingStartMs, TrackingEndMs, TrackingAngleDeg;
        public double MoveDistance;
        public double EffectiveRange;
        public double HitRange;
        public int HitCount;
        public int ProjectileId;
        public DamageType[] DamageTypes;

        public bool HasHits { get { return HitTimesMs != null && HitTimesMs.Length > 0 && DamageTypes != null && DamageTypes.Length > 0; } }
        public bool IsChargeAttack { get { return Slot == "CHARGE_ATK"; } }
        public bool IsAttack { get { return Type == SkillType.ATTACK; } }

        /// <summary>AI 사용 가능 거리(몸 표면 기준). 이동 거리와 타격 반경을 포함한다.</summary>
        public double Reach { get { return Math.Max(EffectiveRange, HitRange) + MoveDistance; } }

        public double FirstHitMs { get { return HitTimesMs != null && HitTimesMs.Length > 0 ? HitTimesMs[0] : AniTimeMs; } }
        public double LastHitMs { get { return HitTimesMs != null && HitTimesMs.Length > 0 ? HitTimesMs[HitTimesMs.Length - 1] : 0; } }
    }

    public sealed class MovesetNode
    {
        public string NodeId;
        public string GroupId;
        public string PrevNodeId;
        public string InputSlot;
        public string SkillId;
        public SkillDefinition Skill;
    }

    public sealed class ActionDefinition
    {
        public string ActionId;
        public bool Enabled;
        public bool IsInput;
        public string InputSlot;
        public double CooldownSec;
        public string Description;
    }

    public sealed class AiRule
    {
        public int BehaviorIndex;
        public bool Enabled;
        public string AiId;
        public int PhaseNo;
        public int EvalOrder;
        public AiCondition Condition1;
        public AiCondition Condition2;
        public double ChancePct;
        public string ActionId;
        public ActionDefinition Action;
        public string ForcedSkillId;
        public string Note;
    }

    public sealed class AiPhase
    {
        public int PhaseNo;
        public string Name;
        public double HpMinPct;
        public double HpMaxPct;
        public string EnterActionId;
        public double DmgMult = 1;
        public double SpeedMult = 1;
    }

    public sealed class AiProfile
    {
        public string Id;
        public readonly List<AiRule> Rules = new List<AiRule>();
        public readonly List<AiPhase> Phases = new List<AiPhase>();
    }

    public sealed class DataIssue
    {
        public string Where;
        public string Message;
        public bool Blocking;
        public override string ToString() { return (Blocking ? "[오류] " : "[경고] ") + Where + " " + Message; }
    }

    /// <summary>읽기 전용 정의 사전. 전투 중 HP 같은 가변 상태를 저장하지 않는다.</summary>
    public sealed class DataCatalog
    {
        public readonly Dictionary<string, CharacterDefinition> Characters = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
        public readonly List<CharacterDefinition> CharacterList = new List<CharacterDefinition>();
        public readonly Dictionary<string, SkillDefinition> Skills = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
        public readonly Dictionary<string, ActionDefinition> Actions = new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);
        public readonly Dictionary<string, AiProfile> AiProfiles = new Dictionary<string, AiProfile>(StringComparer.Ordinal);
        public readonly Dictionary<string, MovesetNode> MovesetNodes = new Dictionary<string, MovesetNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, MovesetNode> _movesetLookup = new Dictionary<string, MovesetNode>(StringComparer.Ordinal);
        private readonly HashSet<string> _movesetGroups = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<DataIssue> Issues = new List<DataIssue>();
        public string DataHash = "unknown";
        public string DataGeneratedAt = "";

        public int BlockingIssueCount
        {
            get
            {
                int n = 0;
                foreach (var i in Issues) if (i.Blocking) n++;
                return n;
            }
        }

        private void Issue(string where, string msg, bool blocking)
        {
            Issues.Add(new DataIssue { Where = where, Message = msg, Blocking = blocking });
        }

        public static DataCatalog Build(CharacterFileDto chars, SkillFileDto skills, MovesetFileDto movesets, AiFileDto ai, DataManifestDto manifest)
        {
            var cat = new DataCatalog();
            if (manifest != null)
            {
                cat.DataHash = manifest.dataHash ?? "unknown";
                cat.DataGeneratedAt = manifest.generatedAt ?? "";
            }
            cat.BuildSkills(skills);
            cat.BuildMovesets(movesets);
            cat.BuildAi(ai);
            cat.BuildCharacters(chars);
            return cat;
        }

        private void BuildSkills(SkillFileDto file)
        {
            if (file == null || file.skills == null) { Issue("skills.json", "스킬 데이터가 없습니다", true); return; }
            foreach (var s in file.skills)
            {
                if (s == null || string.IsNullOrEmpty(s.id)) continue;
                SkillType st;
                if (!EnumText.TryParseSkillType(s.skillType, out st)) Issue("skills.json " + s.id, "알 수 없는 Skill_Type " + s.skillType, false);
                var types = new List<DamageType>();
                if (s.damageTypes != null)
                {
                    foreach (string d in s.damageTypes)
                    {
                        DamageType dt;
                        if (EnumText.TryParseDamageType(d, out dt)) types.Add(dt);
                        else Issue("skills.json " + s.id, "알 수 없는 Damage_Type " + d, false);
                    }
                }
                var def = new SkillDefinition
                {
                    Id = s.id,
                    Enabled = s.enabled,
                    Valid = s.valid,
                    Type = st,
                    WeaponType = s.weaponType,
                    Slot = s.slot,
                    CostHp = s.costHp,
                    CostFp = s.costFp,
                    CostStamina = s.costStamina,
                    AniSpeed = s.aniSpeed > 0 ? s.aniSpeed : 1.0,
                    AniTimeMs = s.aniTimeMs,
                    HitTimesMs = s.hitTimesMs ?? new double[0],
                    MotionValues = s.motionValues ?? new double[0],
                    CancelTimeMs = s.cancelTimeMs,
                    AddPoise = s.addPoise,
                    PoiseStartMs = s.poiseStartMs,
                    PoiseEndMs = s.poiseEndMs,
                    TrackingStartMs = s.trackingStartMs,
                    TrackingEndMs = s.trackingEndMs,
                    TrackingAngleDeg = s.trackingAngleDeg,
                    MoveDistance = s.moveDistance,
                    EffectiveRange = s.effectiveRange,
                    HitRange = s.hitRange,
                    HitCount = s.hitCount,
                    ProjectileId = s.projectileId,
                    DamageTypes = types.ToArray(),
                };
                // 코어 측 이중 검증: 타격 목록 길이 일치(구현계획 3.3-6)
                if (def.HitCount > 0 && (def.HitTimesMs.Length != def.HitCount || def.MotionValues.Length != def.HitCount))
                {
                    if (def.Valid) Issue("skills.json " + s.id, "Hit_Count 와 Hit_Time/Motion_Value 목록 길이 불일치 -> 비활성", false);
                    def.Valid = false;
                }
                if (def.HitCount == 0)
                {
                    def.HitTimesMs = new double[0];
                    def.MotionValues = new double[0];
                }
                Skills[def.Id] = def;
            }
        }

        private static string Key(string group, string prev, string slot) { return group + "|" + prev + "|" + slot; }

        private void BuildMovesets(MovesetFileDto file)
        {
            if (file == null || file.nodes == null) { Issue("movesets.json", "무브셋 데이터가 없습니다", true); return; }
            foreach (var n in file.nodes)
            {
                if (n == null || !n.enabled) continue;
                SkillDefinition sk;
                Skills.TryGetValue(n.skillId ?? string.Empty, out sk);
                var node = new MovesetNode
                {
                    NodeId = n.nodeId,
                    GroupId = n.groupId,
                    PrevNodeId = string.IsNullOrEmpty(n.prevNodeId) ? "NONE" : n.prevNodeId,
                    InputSlot = n.inputSlot,
                    SkillId = n.skillId,
                    Skill = sk,
                };
                if (sk == null) Issue("movesets.json " + n.nodeId, "스킬 없음 " + n.skillId, false);
                MovesetNodes[node.NodeId] = node;
                _movesetLookup[Key(node.GroupId, node.PrevNodeId, node.InputSlot)] = node;
                _movesetGroups.Add(node.GroupId);
            }
        }

        public bool HasMovesetGroup(string group) { return group != null && _movesetGroups.Contains(group); }

        /// <summary>
        /// 이전 노드 + 입력 슬롯으로 다음 노드를 찾는다. 연계가 없으면 루트(NONE) 노드로 초기화.
        /// </summary>
        public MovesetNode ResolveMoveset(string group, string prevNodeId, string slot, out bool isChain)
        {
            isChain = false;
            MovesetNode node;
            if (!string.IsNullOrEmpty(prevNodeId) && prevNodeId != "NONE"
                && _movesetLookup.TryGetValue(Key(group, prevNodeId, slot), out node))
            {
                isChain = true;
                return node;
            }
            _movesetLookup.TryGetValue(Key(group, "NONE", slot), out node);
            return node;
        }

        private void BuildAi(AiFileDto file)
        {
            if (file == null) { Issue("ai.json", "AI 데이터가 없습니다", true); return; }
            if (file.actions != null)
            {
                foreach (var a in file.actions)
                {
                    if (a == null || string.IsNullOrEmpty(a.actionId)) continue;
                    Actions[a.actionId] = new ActionDefinition
                    {
                        ActionId = a.actionId,
                        Enabled = a.enabled,
                        IsInput = a.actionType == "INPUT",
                        InputSlot = a.inputSlot,
                        CooldownSec = a.cooldownSec,
                        Description = a.description,
                    };
                }
            }
            if (file.behaviors != null)
            {
                foreach (var b in file.behaviors)
                {
                    if (b == null || string.IsNullOrEmpty(b.aiId)) continue;
                    AiProfile prof = GetOrAddProfile(b.aiId);
                    AiCondition c1, c2;
                    string err;
                    bool enabled = b.enabled;
                    if (!AiCondition.TryParse(b.condition1, out c1, out err))
                    {
                        Issue("ai.json Behavior " + b.behaviorIndex, "Condition_1 해석 실패: " + err + " -> 행 비활성", false);
                        enabled = false;
                    }
                    if (!AiCondition.TryParse(b.condition2, out c2, out err))
                    {
                        Issue("ai.json Behavior " + b.behaviorIndex, "Condition_2 해석 실패: " + err + " -> 행 비활성", false);
                        enabled = false;
                    }
                    ActionDefinition act;
                    Actions.TryGetValue(b.actionId ?? string.Empty, out act);
                    if (act == null)
                    {
                        Issue("ai.json Behavior " + b.behaviorIndex, "Action_Id 없음 " + b.actionId, false);
                        enabled = false;
                    }
                    prof.Rules.Add(new AiRule
                    {
                        BehaviorIndex = b.behaviorIndex,
                        Enabled = enabled,
                        AiId = b.aiId,
                        PhaseNo = b.phaseNo,
                        EvalOrder = b.evalOrder,
                        Condition1 = c1,
                        Condition2 = c2,
                        ChancePct = b.chancePct,
                        ActionId = b.actionId,
                        Action = act,
                        ForcedSkillId = string.IsNullOrEmpty(b.skillId) || b.skillId == "NONE" ? null : b.skillId,
                        Note = b.note,
                    });
                }
            }
            if (file.phases != null)
            {
                foreach (var p in file.phases)
                {
                    if (p == null || !p.enabled || string.IsNullOrEmpty(p.aiId)) continue;
                    GetOrAddProfile(p.aiId).Phases.Add(new AiPhase
                    {
                        PhaseNo = p.phaseNo,
                        Name = p.phaseName,
                        HpMinPct = p.hpMinPct,
                        HpMaxPct = p.hpMaxPct,
                        EnterActionId = string.IsNullOrEmpty(p.enterActionId) ? "NONE" : p.enterActionId,
                        DmgMult = p.dmgMult > 0 ? p.dmgMult : 1,
                        SpeedMult = p.speedMult > 0 ? p.speedMult : 1,
                    });
                }
            }
        }

        private AiProfile GetOrAddProfile(string id)
        {
            AiProfile p;
            if (!AiProfiles.TryGetValue(id, out p))
            {
                p = new AiProfile { Id = id };
                AiProfiles[id] = p;
            }
            return p;
        }

        private void BuildCharacters(CharacterFileDto file)
        {
            if (file == null || file.characters == null || file.statNames == null)
            {
                Issue("characters.json", "캐릭터 데이터가 없습니다", true);
                return;
            }
            // 이름으로 재매핑(열 순서 변경에 안전)
            var map = new int[file.statNames.Length];
            for (int i = 0; i < file.statNames.Length; i++)
            {
                StatId sid;
                map[i] = Enum.TryParse(file.statNames[i], false, out sid) && sid != StatId.COUNT ? (int)sid : -1;
                if (map[i] < 0) Issue("characters.json", "코어가 모르는 스탯 열 " + file.statNames[i] + " (무시)", false);
            }
            foreach (var c in file.characters)
            {
                if (c == null || string.IsNullOrEmpty(c.id)) continue;
                var def = new CharacterDefinition
                {
                    Id = c.id,
                    Enabled = c.enabled,
                    IsMonster = c.isMonster,
                    DisplayName = string.IsNullOrEmpty(c.displayName) ? c.id : c.displayName,
                    BaseName = c.baseName,
                    Kind = c.kind,
                    GameCycle = c.gameCycle,
                    Level = c.level,
                    MovesetId = c.movesetId,
                    AiProfileId = c.aiProfileId,
                    SourceAiId = c.sourceAiId,
                };
                if (c.stats != null)
                {
                    int n = Math.Min(c.stats.Length, map.Length);
                    for (int i = 0; i < n; i++) if (map[i] >= 0) def.Stats[map[i]] = c.stats[i];
                }
                if (def.Enabled && !HasMovesetGroup(def.MovesetId))
                {
                    Issue("characters.json " + def.Id, "무브셋 없음 " + def.MovesetId + " -> 비활성", false);
                    def.Enabled = false;
                }
                if (def.Enabled && (def.AiProfileId == null || !AiProfiles.ContainsKey(def.AiProfileId)))
                {
                    Issue("characters.json " + def.Id, "AI 프로필 없음 " + def.AiProfileId + " -> 비활성", false);
                    def.Enabled = false;
                }
                if (Characters.ContainsKey(def.Id))
                {
                    Issue("characters.json " + def.Id, "중복 ID", true);
                    continue;
                }
                Characters[def.Id] = def;
                CharacterList.Add(def);
            }
        }

        /// <summary>AI 행 평가 순서: 공통(Phase 0) 행 → 현재 페이즈 행, 각각 Eval_Order → Behavior_Index.</summary>
        public void SortRules(bool commonRowsFirst)
        {
            foreach (var p in AiProfiles.Values)
            {
                p.Rules.Sort((a, b) =>
                {
                    if (commonRowsFirst)
                    {
                        int ga = a.PhaseNo == 0 ? 0 : 1, gb = b.PhaseNo == 0 ? 0 : 1;
                        if (ga != gb) return ga.CompareTo(gb);
                    }
                    int c = a.EvalOrder.CompareTo(b.EvalOrder);
                    return c != 0 ? c : a.BehaviorIndex.CompareTo(b.BehaviorIndex);
                });
                p.Phases.Sort((a, b) => a.PhaseNo.CompareTo(b.PhaseNo));
            }
        }
    }
}
