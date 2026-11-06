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
        public bool SupportSingle;
        public double ExtraPoise, ExtraFrost;
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

        // ---- 버프 연결(SKILL Additional_Effect) ----
        public int BuffId;                 // 0 = 없음
        public BuffDefinition Buff;        // BUFF 표에서 찾은 정의(없거나 비활성이면 null)
        public BuffTarget BuffTarget;      // 미설정이면 Default
        public BuffTrigger BuffTrigger;    // 미설정이면 Default
        public double BuffChance;          // 0~1 (Trigger_Chance 10만분율 → 배율). 0 = 미입력
        public double BuffDurationMs;      // 0 이면 걸리지 않음

        /// <summary>실제로 쓰는 대상: 미설정이면 BUFF 스킬은 자신, 그 외(ATTACK/DEBUFF)는 적.</summary>
        public BuffTarget EffectiveBuffTarget
        {
            get
            {
                if (BuffTarget != BuffTarget.Default) return BuffTarget;
                return Type == SkillType.BUFF ? BuffTarget.Self : BuffTarget.Enemy;
            }
        }

        /// <summary>실제로 쓰는 발동 시점: 미설정이면 타격이 있는 ATTACK 스킬은 적중 시, 그 외는 시전 시.</summary>
        public BuffTrigger EffectiveBuffTrigger
        {
            get
            {
                if (BuffTrigger != BuffTrigger.Default) return BuffTrigger;
                return IsAttack && HasHits ? BuffTrigger.OnHit : BuffTrigger.OnCast;
            }
        }

        public bool HasHits { get { return HitTimesMs != null && HitTimesMs.Length > 0 && DamageTypes != null && DamageTypes.Length > 0; } }
        public bool IsChargeAttack { get { return Slot == "CHARGE_ATK"; } }
        public bool IsAttack { get { return Type == SkillType.ATTACK; } }

        /// <summary>AI 사용 가능 거리(몸 표면 기준). 이동 거리와 타격 반경을 포함한다.</summary>
        public double Reach { get { return Math.Max(EffectiveRange, HitRange) + MoveDistance; } }

        public double FirstHitMs { get { return HitTimesMs != null && HitTimesMs.Length > 0 ? HitTimesMs[0] : AniTimeMs; } }
        public double LastHitMs { get { return HitTimesMs != null && HitTimesMs.Length > 0 ? HitTimesMs[HitTimesMs.Length - 1] : 0; } }
    }

    /// <summary>BUFF 표 한 줄. 걸려 있는 동안 Stats[i] 에 Values[i] 를 더한다.</summary>
    public sealed class BuffDefinition
    {
        public int Id;
        public bool Enabled;
        public string Name;
        public double HealRatio; // HP_RECOVERY: fraction of recipient MaxHp, instant per application.
        public bool Cleanse, Dispellable;
        public double FpCostRatio;
        public bool Taunts; // ADD_AGGRO: force recipient target to the caster while active.
        public StatId[] Stats = new StatId[0];
        public double[] Values = new double[0];
        public int[] AbilityIds = new int[0];

        /// <summary>로그용 요약. 예: "광폭화 SLASH_ATTACK_PCT +20000".</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder(Name ?? ("BUFF_" + Id));
            for (int i = 0; i < Stats.Length; i++)
            {
                sb.Append(i == 0 ? " " : ", ").Append(Stats[i]).Append(' ')
                  .Append(Values[i] >= 0 ? "+" : "").Append(Values[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (HealRatio > 0) sb.Append(" HP 회복 ").Append(HealRatio * 100).Append("%");
            if (Taunts) sb.Append(" 시전자 우선 공격");
            return sb.ToString();
        }
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
        public readonly Dictionary<int, BuffDefinition> Buffs = new Dictionary<int, BuffDefinition>();
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
            return Build(chars, skills, movesets, ai, manifest, null);
        }

        /// <param name="buffs">buffs.json. 없으면(null) 버프 없이 동작하고, 버프를 쓰는 스킬만 경고한다.</param>
        public static DataCatalog Build(CharacterFileDto chars, SkillFileDto skills, MovesetFileDto movesets, AiFileDto ai, DataManifestDto manifest,
                                        BuffFileDto buffs)
        {
            var cat = new DataCatalog();
            if (manifest != null)
            {
                cat.DataHash = manifest.dataHash ?? "unknown";
                cat.DataGeneratedAt = manifest.generatedAt ?? "";
            }
            cat.BuildSkills(skills);
            cat.BuildBuffs(buffs);
            cat.LinkSkillBuffs();
            cat.BuildMovesets(movesets);
            cat.BuildAi(ai);
            cat.BuildCharacters(chars);
            return cat;
        }

        private void BuildBuffs(BuffFileDto file)
        {
            if (file == null || file.buffs == null) return;
            foreach (var b in file.buffs)
            {
                if (b == null || b.id <= 0) continue;
                var stats = new List<StatId>();
                var values = new List<double>();
                var ids = new List<int>();
                double healRatio = 0, fpCostRatio = 0;
                bool cleanse = false;
                bool taunts = false;
                if (b.effects != null)
                {
                    foreach (var e in b.effects)
                    {
                        if (e == null) continue;
                        if (e.stat == "FP_COST_PCT") { fpCostRatio = Math.Max(fpCostRatio, e.value / 100000.0); continue; }
                        if (e.stat == "CLEANSE") { cleanse |= e.value > 0; continue; }
                        if (e.stat == "HP_RECOVERY" || e.stat == "ADD_AGGRO")
                        {
                            if (double.IsNaN(e.value) || double.IsInfinity(e.value) || e.value < 0 || (e.stat == "HP_RECOVERY" && e.value > 100000))
                            { Issue("buffs.json " + b.id, "잘못된 회복/어그로 값", true); continue; }
                            if (e.stat == "HP_RECOVERY") healRatio += e.value / 100000.0;
                            else taunts |= e.value > 0;
                            continue;
                        }
                        StatId sid;
                        if (!Enum.TryParse(e.stat ?? string.Empty, false, out sid) || sid == StatId.COUNT)
                        {
                            Issue("buffs.json " + b.id, "코어가 모르는 스탯 " + e.stat + " (무시)", false);
                            continue;
                        }
                        stats.Add(sid);
                        values.Add(e.value);
                        ids.Add(e.abilityId);
                    }
                }
                if (Buffs.ContainsKey(b.id))
                {
                    Issue("buffs.json " + b.id, "중복 Buff_ID (뒤의 것 무시)", false);
                    continue;
                }
                Buffs[b.id] = new BuffDefinition
                {
                    Id = b.id,
                    Enabled = b.enabled,
                    Name = string.IsNullOrEmpty(b.name) ? "BUFF_" + b.id : b.name,
                    HealRatio = healRatio, Cleanse = cleanse, FpCostRatio = fpCostRatio, Dispellable = b.dispellable,
                    Taunts = taunts,
                    Stats = stats.ToArray(),
                    Values = values.ToArray(),
                    AbilityIds = ids.ToArray(),
                };
            }
        }

        /// <summary>스킬의 Buff_ID → 버프 정의. 없거나 비활성이면 경고하고 효과 없이 둔다.</summary>
        private void LinkSkillBuffs()
        {
            foreach (var s in Skills.Values)
            {
                if (s.BuffId <= 0) continue;
                BuffDefinition b;
                if (!Buffs.TryGetValue(s.BuffId, out b)) { Issue("skills.json " + s.Id, "Buff_ID " + s.BuffId + " 가 buffs.json 에 없음 -> 효과 없음", false); continue; }
                if (!b.Enabled) { Issue("skills.json " + s.Id, "Buff_ID " + s.BuffId + " 비활성 -> 효과 없음", false); continue; }
                if (s.BuffDurationMs <= 0 && !((b.HealRatio > 0 || b.Cleanse) && b.Stats.Length == 0 && !b.Taunts && b.FpCostRatio == 0)) { Issue("skills.json " + s.Id, "Buff_Duration 0 -> 버프 걸지 않음", false); continue; }
                if (s.BuffTrigger == BuffTrigger.Unsupported) { Issue("skills.json " + s.Id, "미구현 Trigger_Condition -> 버프 걸지 않음", false); continue; }
                s.Buff = b;
            }
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
                    SupportSingle = s.supportSingle, ExtraPoise = s.extraPoise, ExtraFrost = s.extraFrost,
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
                    BuffId = s.buffId,
                    BuffTarget = EnumText.ParseBuffTarget(s.buffTarget),
                    BuffTrigger = EnumText.ParseBuffTrigger(s.triggerCondition),
                    // 10만분율 → 배율 변환은 변환기에서 끝남. 0(미입력)의 처리는 rules.buffZeroChanceAlways.
                    BuffChance = Math.Max(0.0, Math.Min(1.0, s.triggerChance)),
                    BuffDurationMs = s.buffDurationMs,
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
