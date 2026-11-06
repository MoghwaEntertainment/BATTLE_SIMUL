# -*- coding: utf-8 -*-
"""
ARPG 전투 시뮬레이터 - Excel -> 정규화 JSON 변환기

사용법 (BalanceMaker 프로젝트 폴더 기준):
    python Tools/ExcelExport/export_tables.py
    python Tools/ExcelExport/export_tables.py --tables "C:/.../Table_ARPG" --out "Assets/StreamingAssets/ARPG/Data"

원칙 (Unity_Plan/04_Unity_구현계획.md 3장):
  * 선택된 원본 시트만 읽는다. 백업 폴더와 '~' 참조 사본은 입력에서 제외한다.
  * Excel 수식은 재계산하지 않는다. openpyxl 이 읽는 값은 '마지막 저장 시 캐시값'이다.
    수식 셀의 캐시값이 비어 있으면 경고로 보고한다.
  * 단위 변환: Motion_Value/Trigger_Chance/Accuracy/Ani_Speed = 10만분율 -> 배율,
    시간 = ms 유지, 거리 = m 유지, 스탯 원본값은 그대로 유지(코어에서 10만분율 해석).
  * Hit_Time / Motion_Value 는 쉼표 구분 목록. Hit_Count 와 길이가 다르면 오류로 보고하고
    해당 스킬을 valid=false 로 내보낸다(임의 분할/복제 금지).
  * 필수 구조 오류(시트 누락, 중복 ID)가 있으면 출력 파일을 교체하지 않는다.
  * (1.2.0) BUFF.xlsx 가 있으면 BUFF 시트(Buff_ID + Buff_AbilityN/Buff_AbilityN_Stat 쌍)를 ABILITY.xlsm 의
    ENUM_Name 으로 스탯 이름에 연결해 buffs.json 으로 내보낸다. 없으면 빈 buffs.json.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import sys
import tempfile
import math

try:
    import openpyxl
except ImportError:  # pragma: no cover
    print("openpyxl 이 필요합니다: pip install openpyxl")
    sys.exit(2)

TOOL_VERSION = "1.6.0"
SCHEMA_VERSION = 1

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
DEFAULT_TABLES = os.path.normpath(os.path.join(PROJECT_DIR, "..", "..", "Table_ARPG"))
DEFAULT_OUT = os.path.join(PROJECT_DIR, "Assets", "StreamingAssets", "ARPG", "Data")
DEFAULT_SCENARIO_OUT = os.path.join(PROJECT_DIR, "Assets", "StreamingAssets", "ARPG", "Scenarios")

SKILL_TYPE_ENUM = ["ATTACK", "BUFF", "DEBUFF", "DEFENCE"]
# SKILL Additional_Effect 열의 enum 순서(Template_SKILL ValueRange). 숫자로 저장된 경우 이 순서로 이름을 찾는다.
TRIGGER_CONDITION_ENUM = ["NONE", "ON_CAST", "ON_HIT", "ON_KILL", "ON_GUARD", "ON_DAMAGED", "ON_END"]
TRIGGER_TYPE_ENUM = ["NONE", "INSTANT", "DURATION", "STACK", "DOT"]
BUFF_TARGET_ENUM = ["SELF", "ENEMY", "ALLY"]
BUFF_TRIGGERS_SUPPORTED = ("NONE", "ON_CAST", "ON_HIT", "ON_END")
BUFF_TYPES_SUPPORTED = ("NONE", "DURATION")
DAMAGE_TYPES = ["PHYSICAL", "SLASH", "THRUST", "STRIKE", "RANGED", "MAGIC", "FIRE", "WATER", "LIGHTNING", "HOLY"]
KIND_FALLBACK = {  # AI.xlsx README!B8
    "HUMAN": "AI_MELEE_STD",
    "BEAST": "AI_BEAST",
    "MONSTER": "AI_HEAVY",
    "UNDEAD": "AI_MINION",
    "DRAGON": "AI_BOSS_DRAGON",
}


class Report:
    def __init__(self):
        self.errors = []      # 전체 배포 차단
        self.disabled = []    # 개별 레코드 비활성(실행에서 제외)
        self.warnings = []
        self.infos = []

    def error(self, where, msg):
        self.errors.append((where, msg))

    def disable(self, where, msg):
        self.disabled.append((where, msg))

    def warn(self, where, msg):
        self.warnings.append((where, msg))

    def info(self, where, msg):
        self.infos.append((where, msg))


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def is_error_value(v):
    return isinstance(v, str) and v.strip().startswith("#")


def num(v, default=0.0):
    if v is None or v == "":
        return default
    if isinstance(v, bool):
        return float(v)
    if isinstance(v, (int, float)):
        return float(v)
    try:
        return float(str(v).strip())
    except ValueError:
        return None


def parse_list(v):
    """쉼표 구분 숫자 목록. 단일 숫자는 길이 1 목록."""
    if v is None or v == "":
        return []
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        return [float(v)]
    parts = [p.strip() for p in str(v).split(",")]
    out = []
    for p in parts:
        if p == "":
            raise ValueError("빈 항목")
        out.append(float(p))
    return out


def clean_int(x):
    return int(x) if float(x).is_integer() else x


def enum_text(v, enum, zero_is_unset=False):
    """SKILL 의 enum 열 값 -> 이름. 숫자면 템플릿 enum 순서로 찾는다. 빈칸·0(미설정)은 'NONE'."""
    if v is None or (isinstance(v, str) and v.strip() == ""):
        return "NONE"
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        i = int(v)
        if zero_is_unset and i == 0:
            return "NONE"
        return enum[i] if 0 <= i < len(enum) else f"UNKNOWN_{i}"
    s = str(v).strip().upper()
    return "NONE" if s in ("0", "") else s


class Sheet:
    """1~2행 복합 헤더를 '그룹.필드' 경로로 펼친다."""

    def __init__(self, wb, wb_formula, name, header_rows, report, file_label):
        if name not in wb.sheetnames:
            report.error(f"{file_label}!{name}", "시트가 없습니다")
            self.ok = False
            return
        self.ok = True
        self.name = name
        self.file_label = file_label
        ws = wb[name]
        rows = list(ws.iter_rows(values_only=True))
        h1 = rows[0] if rows else ()
        h2 = rows[1] if header_rows == 2 and len(rows) > 1 else ()
        self.columns = []
        group = None
        for i in range(len(h1)):
            top = h1[i]
            sub = h2[i] if i < len(h2) else None
            if top is not None:
                group = str(top).strip()
            if sub is not None:
                self.columns.append(f"{group}.{str(sub).strip()}" if group else str(sub).strip())
            elif top is not None:
                self.columns.append(str(top).strip())
            else:
                self.columns.append(None)
        self.leaf = {}
        for i, c in enumerate(self.columns):
            if c is None:
                continue
            self.leaf.setdefault(c, i)
            self.leaf.setdefault(c.split(".")[-1], i)
        self.data = rows[header_rows:]
        self.first_row = header_rows + 1
        # 수식 셀인데 캐시값이 없는 셀 집계
        self.formula_without_cache = 0
        if wb_formula is not None and name in wb_formula.sheetnames:
            wf = wb_formula[name]
            for r_i, row in enumerate(wf.iter_rows(min_row=self.first_row, values_only=True)):
                for c_i, val in enumerate(row):
                    if isinstance(val, str) and val.startswith("="):
                        try:
                            cached = self.data[r_i][c_i]
                        except IndexError:
                            cached = None
                        if cached is None:
                            self.formula_without_cache += 1
            if self.formula_without_cache:
                report.warn(f"{file_label}!{name}",
                            f"수식 캐시값이 비어 있는 셀 {self.formula_without_cache}개 (Excel에서 재계산 후 저장 필요)")

    def col(self, name):
        if name not in self.leaf:
            raise KeyError(f"{self.file_label}!{self.name}: 열 '{name}' 없음")
        return self.leaf[name]

    def rows(self):
        for i, r in enumerate(self.data):
            if r is None or all(v is None for v in r):
                continue
            yield self.first_row + i, r


def open_wb(path):
    wb = openpyxl.load_workbook(path, data_only=True, read_only=True)
    wbf = openpyxl.load_workbook(path, data_only=False, read_only=True)
    return wb, wbf


def export(tables_dir, out_dir, scenario_dir, write_scenarios=True, rules_path=None):
    report = Report()
    sources = {}

    def src(fname):
        p = os.path.join(tables_dir, fname)
        if not os.path.exists(p):
            report.error(fname, "파일이 없습니다")
            return None
        sources[fname] = {
            "file": fname,
            "sha256": sha256_file(p),
            "modified": datetime.datetime.fromtimestamp(os.path.getmtime(p)).isoformat(timespec="seconds"),
        }
        return p

    p_skill = src("SKILL.xlsm")
    p_pc = src("EX_PC_STAT.xlsm")
    p_mon = src("MONSTER.xlsm")
    p_item = src("ITEM.xlsm")
    p_ai = src("AI.xlsx")
    if report.errors:
        return report, None

    # ------------------------------------------------------------------ SKILL
    wb, wbf = open_wb(p_skill)
    sk = Sheet(wb, wbf, "SKILL", 2, report, "SKILL.xlsm")
    tmpl = Sheet(wb, None, "Template_SKILL", 2, report, "SKILL.xlsm")
    try:
        # Template_SKILL 에 남은 옛 설명(K26) 확인
        ws_t = wb["Template_SKILL"]
        for row in ws_t.iter_rows(values_only=True):
            if row and len(row) > 10 and row[1] == "Hit_Count" and row[10] and "/ 타격 횟수" in str(row[10]):
                report.info("SKILL.xlsm!Template_SKILL!K26",
                            "Hit_Count 설명에 '모션 밸류 / 타격 횟수' 옛 규칙이 남아 있음 -> 사용자 확정 규칙(타격별 Motion_Value, 나누지 않음)을 적용")
    except Exception:
        pass

    skills = []
    skill_rows = {}
    dup_identical = []
    if sk.ok:
        c = sk.col
        for rn, r in sk.rows():
            where = f"SKILL.xlsm!SKILL!A{rn}"
            sid = r[c("Index")]
            if sid is None:
                continue
            sid = str(sid).strip()
            if sid in skill_rows:
                first_rn, first_row = skill_rows[sid]
                if tuple(first_row) == tuple(r):
                    # 완전히 같은 행의 반복: 첫 행만 사용하고 경고
                    dup_identical.append((sid, first_rn, rn))
                else:
                    report.error(where, f"중복 스킬 ID {sid} (A{first_rn} 와 값이 다름)")
                continue
            skill_rows[sid] = (rn, r)
            enabled = (r[c("is_use")] == "All")
            issues = []
            valid = True

            st = r[c("Skill_Type")]
            if isinstance(st, (int, float)):
                idx = int(st)
                mapped = SKILL_TYPE_ENUM[idx] if 0 <= idx < len(SKILL_TYPE_ENUM) else "ATTACK"
                report.warn(where, f"{sid}: Skill_Type 이 숫자 {idx} 로 저장됨 -> 템플릿 enum 순서로 {mapped} 매핑")
                st = mapped
            st = str(st or "ATTACK").strip()

            dtypes_raw = str(r[c("Damage_Type")] or "NONE")
            dtypes = [d.strip() for d in dtypes_raw.split(",") if d.strip() and d.strip() != "NONE"]
            for d in dtypes:
                if d not in DAMAGE_TYPES:
                    report.disable(where, f"{sid}: 알 수 없는 Damage_Type '{d}'")
                    valid = False

            hit_count = int(num(r[c("Hit_Count")], 0) or 0)
            ani_time = num(r[c("Ani_Time")], 0)
            try:
                hit_times = parse_list(r[c("Hit_Time")])
                mvs_raw = parse_list(r[c("Motion_Value")])
            except ValueError as e:
                report.disable(where, f"{sid}: Hit_Time/Motion_Value 목록 해석 실패({e})")
                hit_times, mvs_raw, valid = [], [], False

            if hit_count == 0:
                if st == "ATTACK" and dtypes:
                    report.warn(where, f"{sid}: ATTACK 이지만 Hit_Count=0 -> 타격 없는 동작으로 실행")
                hit_times, mvs_raw = [], []
            else:
                if len(hit_times) != hit_count or len(mvs_raw) != hit_count:
                    msg = (f"{sid}: Hit_Count={hit_count}, Hit_Time {len(hit_times)}개, Motion_Value {len(mvs_raw)}개 "
                           f"-> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성")
                    report.disable(where, msg)
                    issues.append("multi-hit list length mismatch")
                    valid = False
                for t in hit_times:
                    if t < 0:
                        report.disable(where, f"{sid}: 음수 Hit_Time {t}")
                        valid = False
                    if ani_time and t > ani_time:
                        report.disable(where, f"{sid}: Hit_Time {t} > Ani_Time {ani_time}")
                        valid = False
                for a, b in zip(hit_times, hit_times[1:]):
                    if b < a:
                        report.disable(where, f"{sid}: Hit_Time 역순 {hit_times}")
                        valid = False
                    elif b == a:
                        report.warn(where, f"{sid}: 같은 시점 중복 타격 {hit_times} (허용 여부 별도 규칙)")
                for m in mvs_raw:
                    if m < 0:
                        report.disable(where, f"{sid}: 음수 Motion_Value {m}")
                        valid = False

            cancel = num(r[c("Cancel_Time")], 0)
            if ani_time and cancel > ani_time:
                report.warn(where, f"{sid}: Cancel_Time {cancel} > Ani_Time {ani_time}")
            slot = str(r[sk.col("~slot")] or "").strip() if "~slot" in sk.leaf else ""
            is_monster_skill = sid.startswith("MONSTER_")
            if st == "DEFENCE" and not is_monster_skill and cancel == 0:
                report.info(where, f"{sid}: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용")
            if st == "DEFENCE" and ani_time == 0:
                pass  # 아래에서 일괄 보고
            buff_id = int(num(r[c("Buff_ID")], 0) or 0)
            if st in ("BUFF", "DEBUFF") and buff_id == 0:
                report.warn(where, f"{sid}: {st} 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행")
            # 버프 연결 열(Additional_Effect). 숫자로 저장된 enum 은 템플릿 순서로 이름을 찾는다.
            # Buff_Target 의 0 은 'SELF' 가 아니라 '미설정'으로 보고 실행 시 Skill_Type 기본값을 쓴다.
            trig_cond = enum_text(r[c("Trigger_Condition")], TRIGGER_CONDITION_ENUM)
            trig_type = enum_text(r[c("Trigger_Type")], TRIGGER_TYPE_ENUM)
            buff_target = enum_text(r[c("Buff_Target")], BUFF_TARGET_ENUM, zero_is_unset=True)
            buff_dur = num(r[c("Buff_Duration")], 0) or 0
            if buff_id > 0:
                if trig_cond not in BUFF_TRIGGERS_SUPPORTED:
                    report.warn(where, f"{sid}: Trigger_Condition {trig_cond} 는 시뮬레이터 미구현 -> 버프가 걸리지 않음 "
                                       f"(지원: ON_CAST, ON_HIT, ON_END, NONE)")
                if trig_type not in BUFF_TYPES_SUPPORTED:
                    report.warn(where, f"{sid}: Trigger_Type {trig_type} 는 미구현 -> DURATION(지속 시간 동안 능력치 변경)으로 처리")
                if buff_target not in ("NONE",) + tuple(BUFF_TARGET_ENUM):
                    report.warn(where, f"{sid}: 알 수 없는 Buff_Target {buff_target} -> Skill_Type 기본값 사용")
                if buff_dur <= 0:
                    report.warn(where, f"{sid}: Buff_ID={buff_id} 이지만 Buff_Duration=0 -> 버프가 걸리지 않음")
                if trig_cond == "ON_HIT" and (st != "ATTACK" or hit_count == 0):
                    report.warn(where, f"{sid}: Trigger_Condition ON_HIT 이지만 타격이 있는 ATTACK 스킬이 아님 -> 버프가 걸리지 않음 "
                                       f"(ON_CAST 또는 ON_END 사용)")

            # [2026-11-06 사용자 요청] Max_Target: 한 번의 타격이 맞힐 수 있는 최대 대상 수. 빈칸/0 = 제한 없음(기존 판정 범위 그대로)
            max_targets = 0
            if "Max_Target" in sk.leaf:
                mt_raw = num(r[c("Max_Target")], 0) or 0
                if mt_raw < 0 or mt_raw != int(mt_raw):
                    report.warn(where, f"{sid}: Max_Target {mt_raw} 는 0 이상의 정수가 아님 -> 0(제한 없음)으로 처리")
                else:
                    max_targets = int(mt_raw)
            ani_speed_raw = num(r[c("Ani_Speed")], 100000)
            skills.append({
                "id": sid,
                "enabled": enabled,
                "valid": valid,
                "skillType": st,
                "weaponType": str(r[c("Weapon_Type")] or ""),
                "slot": slot,
                "costHp": num(r[c("Cost.HP")], 0),
                "costFp": num(r[c("Cost.FP")], 0),
                "costStamina": num(r[c("Cost.Stamina")], 0),
                "target": str(r[c("Skill_Target")] or "ENEMY"),
                "aniSpeed": (ani_speed_raw or 100000) / 100000.0,
                "aniTimeMs": ani_time,
                "hitTimesMs": hit_times,
                "cancelTimeMs": cancel,
                "addPoise": num(r[c("Add_Poise")], 0),
                "poiseStartMs": num(r[c("Poise_Start")], 0),
                "poiseEndMs": num(r[c("Poise_End")], 0),
                "trackingStartMs": num(r[c("Tracking_Start")], 0),
                "trackingEndMs": num(r[c("Tracking_End")], 0),
                "trackingAngleDeg": num(r[c("Tracking_Angle")], 0),
                "moveDistance": num(r[c("Move_Distance")], 0),
                "effectiveRange": num(r[c("Effective_Range")], 0),
                "hitRange": num(r[c("Hit_Range")], 0),
                "hitCount": hit_count,
                "projectileId": int(num(r[c("Projectile_ID")], 0) or 0),
                "damageTypes": dtypes,
                "motionValues": [m / 100000.0 for m in mvs_raw],
                "motionValuesRaw": mvs_raw,
                "triggerCondition": trig_cond,
                "triggerType": trig_type,
                "buffTarget": buff_target,
                "buffId": buff_id,
                "triggerChance": (num(r[c("Trigger_Chance")], 0) or 0) / 100000.0,
                "buffDurationMs": buff_dur,
                "supportSingle": bool(num(r[c("Support_Single")], 0)) if "Support_Single" in sk.leaf else False,
                "extraPoise": num(r[c("Skill_Poise")], 0) or 0 if "Skill_Poise" in sk.leaf else 0,
                "extraFrost": num(r[c("Skill_Frost_Buildup")], 0) or 0 if "Skill_Frost_Buildup" in sk.leaf else 0,
                "maxTargets": max_targets,
                "accuracy": (num(r[c("Accuracy")], 100000) or 0) / 100000.0,
                "nameKey": int(num(r[c("Name_Key")], 0) or 0),
            })
        if dup_identical:
            report.warn("SKILL.xlsm!SKILL",
                        f"값이 완전히 같은 중복 행 {len(dup_identical)}개 (예: {dup_identical[0][0]} A{dup_identical[0][1]}/A{dup_identical[0][2]}, "
                        f"범위 A{dup_identical[0][2]}~A{dup_identical[-1][2]}) -> 첫 행만 사용")
        defence_zero = [s["id"] for s in skills if s["skillType"] == "DEFENCE" and s["aniTimeMs"] == 0]
        if defence_zero:
            report.info("SKILL.xlsm!SKILL", f"DEFENCE 스킬 {len(defence_zero)}개가 Ani_Time=0 -> rules.json 의 blockHoldSecWhenAniTimeZero(임시값)로 가드 유지")
        mon_fp = [s["id"] for s in skills if s["id"].startswith("MONSTER_") and s["costFp"] > 0]
        if mon_fp:
            report.info("SKILL.xlsm!SKILL", f"몬스터 스킬 {len(mon_fp)}개에 FP 비용이 남아 있음(예: {mon_fp[0]}). "
                                            f"rules.json monsterIgnoreSkillCosts=true 이면 실행 시 무시")
        pc_st = [s for s in skills if not s["id"].startswith("MONSTER_")]
        report.info("SKILL.xlsm!SKILL", f"플레이어 스킬 {len(pc_st)}개 중 스태미나 비용 50: "
                                        f"{sum(1 for s in pc_st if s['costStamina'] == 50)}개")
    skill_map = {s["id"]: s for s in skills}

    # ------------------------------------------------------------------ MOVESET
    nodes = []
    node_ids = set()

    def read_moveset(path, sheet_name, label):
        wbm, wbmf = open_wb(path)
        sh = Sheet(wbm, wbmf, sheet_name, 1, report, label)
        if not sh.ok:
            return
        c = sh.col
        for rn, r in sh.rows():
            where = f"{label}!{sheet_name}!A{rn}"
            nid = r[c("Node_ID")]
            if nid is None:
                continue
            nid = str(nid).strip()
            if nid in node_ids:
                report.error(where, f"중복 Node_ID {nid}")
                continue
            node_ids.add(nid)
            skill_id = str(r[c("Skill_Id")] or "").strip()
            enabled = r[c("is_use")] == "All"
            if skill_id not in skill_map:
                report.disable(where, f"{nid}: Skill_Id '{skill_id}' 가 SKILL 에 없음")
                enabled = False
            nodes.append({
                "nodeId": nid,
                "enabled": enabled,
                "groupId": str(r[c("GroupIndex")] or "").strip(),
                "prevNodeId": str(r[c("Prev_Node_ID")] or "NONE").strip(),
                "inputSlot": str(r[c("Input_Slot")] or "NONE").strip(),
                "skillId": skill_id,
            })

    read_moveset(p_item, "Equip_Moveset", "ITEM.xlsm")
    read_moveset(p_mon, "Monster_Moveset", "MONSTER.xlsm")
    groups = {n["groupId"] for n in nodes if n["enabled"]}

    # ------------------------------------------------------------------ AI
    wba, wbaf = open_wb(p_ai)
    act = Sheet(wba, wbaf, "ACTION_DEF", 1, report, "AI.xlsx")
    beh = Sheet(wba, wbaf, "AI_BEHAVIOR", 1, report, "AI.xlsx")
    pha = Sheet(wba, wbaf, "AI_PHASE", 1, report, "AI.xlsx")
    amap = Sheet(wba, wbaf, "AI_MAP", 1, report, "AI.xlsx")
    actions, behaviors, phases, maps = [], [], [], []
    if act.ok:
        c = act.col
        for rn, r in act.rows():
            aid = r[c("Action_Id")]
            if aid is None:
                continue
            actions.append({
                "actionId": str(aid).strip(),
                "enabled": r[c("is_use")] == "All",
                "actionType": str(r[c("Action_Type")] or "BUILTIN").strip(),
                "inputSlot": str(r[c("Input_Slot")] or "NONE").strip(),
                "cooldownSec": num(r[c("Cooldown_Sec")], 0),
                "description": str(r[c("Description")] or ""),
            })
    action_ids = {a["actionId"] for a in actions}
    if beh.ok:
        c = beh.col
        seen = set()
        for rn, r in beh.rows():
            where = f"AI.xlsx!AI_BEHAVIOR!A{rn}"
            bi = r[c("Behavior_Index")]
            if bi is None:
                continue
            bi = int(bi)
            if bi in seen:
                report.error(where, f"중복 Behavior_Index {bi}")
                continue
            seen.add(bi)
            aid = str(r[c("Action_Id")] or "").strip()
            enabled = r[c("is_use")] == "All"
            if aid not in action_ids:
                report.disable(where, f"Behavior {bi}: Action_Id '{aid}' 가 ACTION_DEF 에 없음")
                enabled = False
            behaviors.append({
                "behaviorIndex": bi,
                "enabled": enabled,
                "aiId": str(r[c("AI_ID")] or "").strip(),
                "phaseNo": int(num(r[c("Phase_No")], 0) or 0),
                "evalOrder": int(num(r[c("Eval_Order")], 0) or 0),
                "condition1": str(r[c("Condition_1")] or "").strip(),
                "condition2": str(r[c("Condition_2")] or "").strip(),
                "chancePct": num(r[c("Chance_Pct")], 100),
                "actionId": aid,
                "skillId": str(r[c("Skill_Id")] or "NONE").strip(),
                "note": str(r[c("Note")] or ""),
            })
    if pha.ok:
        c = pha.col
        for rn, r in pha.rows():
            if r[c("Phase_Index")] is None:
                continue
            phases.append({
                "phaseIndex": int(r[c("Phase_Index")]),
                "enabled": r[c("is_use")] == "All",
                "aiId": str(r[c("AI_ID")] or "").strip(),
                "phaseNo": int(num(r[c("Phase_No")], 1) or 1),
                "phaseName": str(r[c("Phase_Name")] or ""),
                "hpMinPct": num(r[c("HP_Min_Pct")], 0),
                "hpMaxPct": num(r[c("HP_Max_Pct")], 100),
                "enterActionId": str(r[c("Enter_Action_Id")] or "NONE").strip(),
                "dmgMult": num(r[c("Dmg_Mult")], 1),
                "speedMult": num(r[c("Speed_Mult")], 1),
                "note": str(r[c("Note")] or ""),
            })
    ai_name_map = {}
    if amap.ok:
        c = amap.col
        for rn, r in amap.rows():
            if r[c("Map_Index")] is None:
                continue
            m = {
                "mapIndex": int(r[c("Map_Index")]),
                "enabled": r[c("is_use")] == "All",
                "monsterName": str(r[c("Monster_Name")] or "").strip(),
                "kind": str(r[c("Kind")] or "").strip(),
                "aiId": str(r[c("AI_ID")] or "").strip(),
                "note": str(r[c("Note")] or ""),
            }
            maps.append(m)
            if m["enabled"]:
                ai_name_map[m["monsterName"]] = m["aiId"]
    ai_profiles = {b["aiId"] for b in behaviors if b["enabled"]}

    # ------------------------------------------------------------------ CHARACTERS
    characters = []
    stat_names = None
    char_ids = set()
    name_fallback_count = 0
    ai_fallback = {}

    def read_chars(path, sheet_name, label, is_monster):
        nonlocal stat_names, name_fallback_count
        wbc, wbcf = open_wb(path)
        sh = Sheet(wbc, wbcf, sheet_name, 2, report, label)
        if not sh.ok:
            return
        cols = [col for col in sh.columns if col and col.startswith("Ability.")]
        names = [col.split(".", 1)[1] for col in cols]
        if stat_names is None:
            stat_names = names
        elif names != stat_names:
            report.error(f"{label}!{sheet_name}", "스탯 열 구성이 EX_PC 와 다릅니다")
            return
        idx_stats = [sh.columns.index(col) for col in cols]
        c = sh.col
        for rn, r in sh.rows():
            where = f"{label}!{sheet_name}!A{rn}"
            cid = r[c("Index")]
            if cid is None:
                continue
            cid = str(clean_int(cid)) if isinstance(cid, (int, float)) else str(cid).strip()
            if cid in char_ids:
                report.error(where, f"중복 캐릭터 ID {cid}")
                continue
            char_ids.add(cid)
            enabled = r[c("is_use")] == "All"
            disp = r[c("~Name")]
            label_name = str(r[c("~구분용")] or "").strip()
            if is_monster:
                base_name = None if (disp is None or is_error_value(disp)) else str(disp).strip()
                if base_name is None:
                    base_name = re.sub(r"_레벨_\d+$", "", re.sub(r"^\d+회차_", "", label_name))
                display = label_name or base_name
            else:
                display = label_name if (disp is None or is_error_value(disp)) else str(disp).strip()
                if disp is None or is_error_value(disp):
                    name_fallback_count += 1
                base_name = display
            kind = str(r[c("Kind")] or "").strip()
            moveset = str(r[c("Moveset_ID")] or "").strip()
            src_ai = str(r[c("AI_ID")] or "").strip()
            if is_monster:
                ai = ai_name_map.get(base_name)
                if ai is None:
                    ai = KIND_FALLBACK.get(kind)
                    ai_fallback[base_name] = ai
            else:
                ai = src_ai
            if moveset not in groups:
                report.disable(where, f"{cid} {display}: Moveset_ID '{moveset}' 없음 -> 캐릭터 비활성")
                enabled = False
            if ai not in ai_profiles:
                report.disable(where, f"{cid} {display}: AI 프로필 '{ai}' 없음 -> 캐릭터 비활성")
                enabled = False
            stats = []
            bad = 0
            for i in idx_stats:
                v = r[i]
                if v is None or is_error_value(v):
                    bad += 1
                    stats.append(0.0)
                else:
                    f = num(v, 0.0)
                    stats.append(0.0 if f is None else f)
            if bad:
                report.warn(where, f"{cid}: 빈/오류 스탯 {bad}개 -> 0 처리")
            characters.append({
                "id": cid,
                "enabled": enabled,
                "isMonster": is_monster,
                "displayName": display,
                "baseName": base_name,
                "kind": kind,
                "gameCycle": int(num(r[c("Game_Cycle")], 1) or 1),
                "level": int(num(r[c("Level")], 1) or 1),
                "movesetId": moveset,
                "aiProfileId": ai or "",
                "sourceAiId": src_ai,
                "nameKey": int(num(r[c("Name")], 0) or 0),
                "stats": stats,
            })

    read_chars(p_pc, "EX_PC", "EX_PC_STAT.xlsm", False)
    read_chars(p_mon, "Monster", "MONSTER.xlsm", True)
    if name_fallback_count:
        report.warn("EX_PC_STAT.xlsm!EX_PC!D", f"~Name 이 #N/A 인 PC {name_fallback_count}개 -> 표시 이름으로 ~구분용 사용(전투 ID 는 Index)")
    for nm, ai in sorted(ai_fallback.items()):
        report.warn("AI.xlsx!AI_MAP", f"몬스터 '{nm}' 매핑 없음 -> Kind 폴백 {ai}")

    # ------------------------------------------------------------------ BUFF (선택: BUFF.xlsx 가 없으면 버프 없음)
    buffs = read_buffs(tables_dir, sources, report, stat_names or [], skills)
    game_config = read_game_config(tables_dir, sources, report)

    # AI 거리 조건과 스킬 사거리가 맞지 않는 조합(예: 원거리형 AI + 사거리 2m 스킬) 경고
    rules, rules_src = load_rules(rules_path or os.path.join(out_dir, "..", "rules.json"))
    check_ai_skill_reach(report, characters, skills, nodes, actions, behaviors, rules, rules_src)

    if report.errors:
        return report, None

    # ------------------------------------------------------------------ WRITE
    os.makedirs(out_dir, exist_ok=True)
    payloads = {
        "characters.json": {"schemaVersion": SCHEMA_VERSION, "statNames": stat_names or [], "characters": characters},
        "skills.json": {"schemaVersion": SCHEMA_VERSION, "skills": skills},
        "movesets.json": {"schemaVersion": SCHEMA_VERSION, "nodes": nodes},
        "ai.json": {"schemaVersion": SCHEMA_VERSION, "actions": actions, "behaviors": behaviors,
                    "phases": phases, "maps": maps},
        "buffs.json": {"schemaVersion": SCHEMA_VERSION, "buffs": buffs},
        "gameconfig.json": {"schemaVersion": SCHEMA_VERSION, "entries": game_config},
    }
    tmpdir = tempfile.mkdtemp(prefix="arpg_export_", dir=out_dir)
    outputs = []
    try:
        for fname, payload in payloads.items():
            tp = os.path.join(tmpdir, fname)
            with open(tp, "w", encoding="utf-8", newline="\n") as f:
                json.dump(payload, f, ensure_ascii=False, separators=(",", ":"))
            outputs.append({"file": fname, "sha256": sha256_file(tp)})
        combined = hashlib.sha256("".join(o["sha256"] for o in outputs).encode()).hexdigest()
        manifest = {
            "schemaVersion": SCHEMA_VERSION,
            "toolVersion": TOOL_VERSION,
            "generatedAt": datetime.datetime.now().isoformat(timespec="seconds"),
            "dataHash": combined[:16],
            "formulaPolicy": "openpyxl 캐시값 사용(재계산 아님)",
            "sources": list(sources.values()),
            "outputs": outputs,
            "counts": {
                "gameConfigEntries": len(game_config),
                "characters": len(characters),
                "charactersEnabled": sum(1 for x in characters if x["enabled"]),
                "skills": len(skills),
                "skillsInvalid": sum(1 for s in skills if not s["valid"]),
                "movesetNodes": len(nodes),
                "aiBehaviors": len(behaviors),
                "buffs": len(buffs),
                "buffsEnabled": sum(1 for b in buffs if b["enabled"]),
                "skillsWithBuff": sum(1 for s in skills if s["buffId"] > 0),
            },
            "errorCount": len(report.errors),
            "disabledCount": len(report.disabled),
            "warningCount": len(report.warnings),
        }
        tp = os.path.join(tmpdir, "manifest.json")
        with open(tp, "w", encoding="utf-8", newline="\n") as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)
        for fname in list(payloads.keys()) + ["manifest.json"]:
            os.replace(os.path.join(tmpdir, fname), os.path.join(out_dir, fname))
    finally:
        for fn in os.listdir(tmpdir):
            os.remove(os.path.join(tmpdir, fn))
        os.rmdir(tmpdir)

    if write_scenarios:
        write_default_scenarios(scenario_dir, characters)
    return report, manifest


GAME_CONFIG_TYPES = {
    "STUN_TIME": "FLAT", "STUN_ADD_DAMAGE_PCT": "RATE", "WOUND_DAMAGE_PCT": "RATE",
    "POISON_TIME": "FLAT", "POISON_DAMAGE_PCT": "RATE", "FROST_TIME": "FLAT",
    "FROST_SLOWDOWN_PCT": "RATE", "STAGGER_TIME": "FLAT", "GUARD_BREAK_TIME": "FLAT",
    "GUARD_BREAK_ADD_DAMAGE_PCT": "RATE",
}


def read_game_config(tables_dir, sources, report):
    path = os.path.join(tables_dir, "GameConfig.xlsm")
    if not os.path.exists(path):
        report.info("GameConfig.xlsm", "파일 없음: 기존 rules.json 기본값 사용")
        return []
    sources["GameConfig.xlsm"] = {"file": "GameConfig.xlsm", "sha256": sha256_file(path),
        "modified": datetime.datetime.fromtimestamp(os.path.getmtime(path)).isoformat(timespec="seconds")}
    wb, wbf = open_wb(path)
    entries, seen = [], set()
    try:
        sheet = Sheet(wb, wbf, "GameConfig", 1, report, "GameConfig.xlsm")
        if not sheet.ok:
            return []
        try:
            ix, use, ty, val = [sheet.col(k) for k in ("Index", "is_use", "Value_Type", "Value")]
        except KeyError as e:
            report.error("GameConfig.xlsm!GameConfig", str(e))
            return []
        for rowno, row in sheet.rows():
            if row[use] != "All":
                continue
            where = f"GameConfig.xlsm!GameConfig!A{rowno}"
            key = str(row[ix] or "").strip()
            kind = str(row[ty] or "").strip().upper()
            raw = num(row[val], None)
            if not key or key in seen:
                report.error(where, f"빈 Index 또는 중복 Index: {key}")
                continue
            seen.add(key)
            if kind not in ("RATE", "FLAT") or raw is None or not math.isfinite(raw):
                report.error(where, "Value_Type은 RATE/FLAT, Value는 유한한 숫자여야 함(수식 캐시 확인)")
                continue
            if key in GAME_CONFIG_TYPES:
                if kind != GAME_CONFIG_TYPES[key] or raw < 0 or (key.endswith("_TIME") and raw <= 0):
                    report.error(where, f"{key}: 타입은 {GAME_CONFIG_TYPES[key]}, 시간은 양수, 비율은 0 이상 필요")
                    continue
                if key == "FROST_SLOWDOWN_PCT" and raw > 100000:
                    report.error(where, "감속 비율은 0~100000 범위")
                    continue
            else:
                report.warn(where, f"{key}: 변환하지만 현재 전투 규칙 연결은 없음")
            entries.append({"id": key, "valueType": kind, "rawValue": raw,
                            "value": raw / 100000.0 if kind == "RATE" else raw})
    finally:
        wb.close()
        wbf.close()
    return entries


BUFF_ABILITY_RE = re.compile(r"^Buff_Ability(\d+)$")


def read_buffs(tables_dir, sources, report, stat_names, skills):
    """
    BUFF.xlsx 의 BUFF 시트 -> buffs.json 의 buffs 목록.
    한 행 = 버프 하나. Buff_AbilityN(ABILITY Index) + Buff_AbilityN_Stat(더할 수치) 쌍을 N 개수와 관계없이 모두 읽는다.
    ABILITY.xlsm 의 ENUM_Name 으로 시뮬레이터 스탯 이름을 찾는다(EX_PC 의 Ability.* 열 이름과 같음).
    """
    referenced = sorted({s["buffId"] for s in skills if s["buffId"] > 0})
    p_buff = os.path.join(tables_dir, "BUFF.xlsx")
    if not os.path.exists(p_buff):
        if referenced:
            report.warn("BUFF.xlsx", f"파일이 없는데 SKILL 에서 Buff_ID {len(referenced)}종을 사용 -> 버프 없이 실행")
        else:
            report.info("BUFF.xlsx", "파일 없음 -> 버프 없음")
        return []

    def register(fname, path):
        sources[fname] = {
            "file": fname,
            "sha256": sha256_file(path),
            "modified": datetime.datetime.fromtimestamp(os.path.getmtime(path)).isoformat(timespec="seconds"),
        }

    register("BUFF.xlsx", p_buff)
    p_ab = os.path.join(tables_dir, "ABILITY.xlsm")
    if not os.path.exists(p_ab):
        report.error("ABILITY.xlsm", "파일이 없습니다 (BUFF 의 Buff_Ability 번호를 스탯 이름으로 바꾸려면 필요)")
        return []
    register("ABILITY.xlsm", p_ab)

    # ABILITY: Index -> (ENUM_Name, 이름, Data_Type)
    abilities = {}
    wba, wbaf = open_wb(p_ab)
    ab = Sheet(wba, wbaf, "Ability", 1, report, "ABILITY.xlsm")
    if not ab.ok:
        return []
    ca = ab.col
    for rn, r in ab.rows():
        idx = num(r[ca("Index")], None)
        if idx is None:
            continue
        name = r[ca("~name")] if "~name" in ab.leaf else None
        abilities[int(idx)] = (str(r[ca("ENUM_Name")] or "").strip(),
                               "" if name is None or is_error_value(name) else str(name),
                               int(num(r[ca("Data_Type")], 0) or 0))

    wb, wbf = open_wb(p_buff)
    # '~' 참고 열(~AbilityN_Name 등)은 읽지 않으므로 수식 캐시 검사에서 뺀다.
    sh = Sheet(wb, None, "BUFF", 1, report, "BUFF.xlsx")
    if not sh.ok:
        return []
    no_cache = 0
    for r_i, row in enumerate(wbf["BUFF"].iter_rows(min_row=sh.first_row, values_only=True)):
        for c_i, val in enumerate(row):
            col = sh.columns[c_i] if c_i < len(sh.columns) else None
            if col and not col.startswith("~") and isinstance(val, str) and val.startswith("="):
                cached = sh.data[r_i][c_i] if r_i < len(sh.data) and c_i < len(sh.data[r_i]) else None
                if cached is None:
                    no_cache += 1
    if no_cache:
        report.warn("BUFF.xlsx!BUFF", f"수식 캐시값이 비어 있는 셀 {no_cache}개 (Excel에서 재계산 후 저장 필요)")
    for need in ("Buff_ID", "is_use"):
        if need not in sh.leaf:
            report.error("BUFF.xlsx!BUFF", f"필수 열 '{need}' 없음")
            return []
    slots = []
    for i, col in enumerate(sh.columns):
        m = BUFF_ABILITY_RE.match(col or "")
        if not m:
            continue
        k = int(m.group(1))
        stat_col = f"Buff_Ability{k}_Stat"
        if stat_col not in sh.leaf:
            report.error("BUFF.xlsx!BUFF", f"'{col}' 에 짝이 되는 '{stat_col}' 열이 없습니다")
            return []
        slots.append((k, i, sh.leaf[stat_col]))
    slots.sort()
    if not slots:
        report.error("BUFF.xlsx!BUFF", "Buff_Ability1 / Buff_Ability1_Stat 열이 없습니다")
        return []

    stat_set = set(stat_names)
    c = sh.col
    buffs = []
    seen = {}
    for rn, r in sh.rows():
        where = f"BUFF.xlsx!BUFF!A{rn}"
        raw = r[c("Buff_ID")]
        if raw is None:
            continue
        bid_f = num(raw, None)
        if bid_f is None or not float(bid_f).is_integer() or bid_f <= 0:
            report.disable(where, f"Buff_ID '{raw}' 는 1 이상의 정수여야 함 -> 행 무시")
            continue
        bid = int(bid_f)
        if bid in seen:
            report.error(where, f"중복 Buff_ID {bid} (A{seen[bid]} 와 같음)")
            continue
        seen[bid] = rn
        enabled = r[c("is_use")] == "All"
        name = ""
        if "~Name" in sh.leaf:
            v = r[c("~Name")]
            name = "" if v is None or is_error_value(v) else str(v).strip()
        effects = []
        for k, a_i, s_i in slots:
            a_raw = r[a_i] if a_i < len(r) else None
            s_raw = r[s_i] if s_i < len(r) else None
            a = num(a_raw, 0)
            if a is None or not float(a).is_integer():
                report.warn(where, f"버프 {bid}: Buff_Ability{k} '{a_raw}' 는 정수가 아님 -> 무시")
                continue
            a = int(a)
            if a == 0:
                if num(s_raw, 0):
                    report.warn(where, f"버프 {bid}: Buff_Ability{k}=0 인데 수치 {s_raw} 가 있음 -> 무시")
                continue
            v = num(s_raw, None) if s_raw is not None else None
            if v is None:
                report.warn(where, f"버프 {bid}: Buff_Ability{k}_Stat 이 비었거나 숫자가 아님 -> 무시")
                continue
            if a not in abilities:
                report.warn(where, f"버프 {bid}: ABILITY 에 없는 Index {a} -> 무시")
                continue
            enum_name, aname, dtype = abilities[a]
            if enum_name not in stat_set and enum_name not in ("HP_RECOVERY", "ADD_AGGRO", "FP_COST_PCT", "CLEANSE"):
                report.warn(where, f"버프 {bid}: {a} {aname}({enum_name}) 는 캐릭터 스탯 열이 아님(면역·행동 제한 등은 시뮬레이터 미구현) -> 무시")
                continue
            if enum_name in ("HP_RECOVERY", "ADD_AGGRO", "FP_COST_PCT", "CLEANSE") and (not math.isfinite(v) or v < 0):
                report.error(where, f"{enum_name}: 0 이상의 유한한 값 필요")
                continue
            if enum_name == "HP_RECOVERY" and v > 100000:
                report.error(where, "HP_RECOVERY는 0~100000 십만분율 필요")
                continue
            if v == 0:
                continue
            effects.append({"abilityId": a, "stat": enum_name, "name": aname, "value": v, "dataType": dtype})
        if enabled and not effects:
            report.warn(where, f"버프 {bid}: 적용할 능력치가 없음")
        buffs.append({"id": bid, "enabled": enabled, "name": name or f"BUFF_{bid}", "dispellable": bool(num(r[c("Dispellable")], 0)) if "Dispellable" in sh.leaf else False, "effects": effects})

    # SKILL 연결 확인
    bmap = {b["id"]: b for b in buffs}
    for s in skills:
        if s["buffId"] <= 0:
            continue
        b = bmap.get(s["buffId"])
        if b is None:
            report.warn(f"SKILL.xlsm!SKILL {s['id']}", f"Buff_ID {s['buffId']} 가 BUFF 표에 없음 -> 효과 없음")
        elif not b["enabled"]:
            report.warn(f"SKILL.xlsm!SKILL {s['id']}", f"Buff_ID {s['buffId']} 는 비활성(is_use) 버프 -> 효과 없음")

    # 쓰이지 않는 피해 타입의 공격력 버프 안내(예: 물리 공격력 버프인데 Damage_Type PHYSICAL 스킬이 없음)
    used_types = {d for s in skills if s["enabled"] for d in s["damageTypes"]}
    for b in buffs:
        for e in b["effects"]:
            st = e["stat"]
            t = None
            if st.endswith("_ATTACK_PCT"):
                t = st[:-len("_ATTACK_PCT")]
            elif st.endswith("_ATTACK"):
                t = st[:-len("_ATTACK")]
            if t in DAMAGE_TYPES and t not in used_types:
                report.warn(f"BUFF.xlsx!BUFF 버프 {b['id']}",
                            f"{e['abilityId']} {e['name']}({st}) 를 바꾸지만 Damage_Type {t} 인 스킬이 없음 -> 지금은 피해에 영향 없음 "
                            f"(피해는 스킬의 Damage_Type 별 공격력으로 계산)")

    report.info("BUFF.xlsx", f"버프 {len(buffs)}개(활성 {sum(1 for b in buffs if b['enabled'])}), "
                             f"SKILL 에서 Buff_ID 를 쓰는 스킬 {sum(1 for s in skills if s['buffId'] > 0)}개")
    return buffs


def find_char(characters, display):
    for ch in characters:
        if ch["displayName"] == display and ch["enabled"]:
            return ch["id"]
    return None


def write_default_scenarios(scenario_dir, characters):
    """대표 시나리오 프리셋. 이미 있는 파일은 덮어쓰지 않는다(사용자 수정 보존)."""
    os.makedirs(scenario_dir, exist_ok=True)
    presets = [
        ("01_근접1대1_검사37_vs_기사단장37", "근접 1대1: 대미지메이커 예시 조합",
         [("검사_레벨_37", 1)], [("1회차_기사 단장_레벨_37", 1)], 100),
        ("02_근접1대1_검사37_vs_도적검병10", "근접 1대1: 낮은 레벨 일반 몬스터",
         [("검사_레벨_37", 1)], [("1회차_도적 검병_레벨_10", 1)], 100),
        ("03_기존결과_검사44x2_vs_대마도사+활병x4", "기존 4.전투시뮬레이터 결과 조합(재현 비교용 아님)",
         [("검사_레벨_44", 2)], [("1회차_대마도사_레벨_44", 1), ("1회차_왕국군 활병_레벨_24", 4)], 100),
        ("04_다대다_검사x3_vs_늑대x5", "다대다: 같은 캐릭터 여러 개체 식별 확인",
         [("검사_레벨_30", 3)], [("1회차_늑대_레벨_30", 5)], 100),
        ("05_보스_검사99x3_vs_화염룡", "보스 페이즈(AI_BOSS_DRAGON) 확인",
         [("검사_레벨_99", 3)], [("1회차_화염룡_레벨_99", 1)], 50),
    ]
    by_name = {}
    for ch in characters:
        if ch["enabled"]:
            by_name.setdefault(ch["displayName"], ch["id"])

    def resolve(display):
        if display in by_name:
            return by_name[display]
        # 레벨이 없으면 같은 이름의 가장 가까운 레벨
        m = re.match(r"^(.*_레벨_)(\d+)$", display)
        if not m:
            return None
        prefix, lv = m.group(1), int(m.group(2))
        best = None
        for name, cid in by_name.items():
            mm = re.match(r"^(.*_레벨_)(\d+)$", name)
            if mm and mm.group(1) == prefix:
                d = abs(int(mm.group(2)) - lv)
                if best is None or d < best[0]:
                    best = (d, cid)
        return best[1] if best else None

    for fname, desc, team_a, team_b, runs in presets:
        path = os.path.join(scenario_dir, fname + ".json")
        if os.path.exists(path):
            continue
        teams = []
        ok = True
        for tid, (tname, members) in enumerate([("아군", team_a), ("적군", team_b)]):
            ms = []
            for disp, cnt in members:
                cid = resolve(disp)
                if cid is None:
                    ok = False
                    break
                ms.append({"definitionId": cid, "count": cnt})
            teams.append({"teamId": tid, "name": tname, "members": ms})
        if not ok:
            continue
        sc = {"name": fname, "description": desc, "masterSeed": 12345, "runs": runs,
              "maxBattleSec": 300, "spawnDistance": 10, "spawnSpacing": 1.5, "teams": teams}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(sc, f, ensure_ascii=False, indent=2)


# ---------------------------------------------------------------------- AI 거리 조건 vs 스킬 사거리
RULE_DEFAULTS = {"distMeleeMax": 2.5, "distMidMax": 6.0, "perceptionRange": 18.0,
                 "bodyRadius": 0.4, "instantRangedThreshold": 6.0}
_DIST_RE = re.compile(r"^\s*(dist_m|dist)\s*(==|!=|<=|>=|<|>)\s*([A-Za-z_]+|-?\d+(?:\.\d+)?)\s*$")


def load_rules(path):
    """rules.json 의 거리·반경 값(없으면 기본값). 시뮬레이터와 같은 기준으로 검사하기 위함."""
    rules = dict(RULE_DEFAULTS)
    src = "기본값"
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        for k in RULE_DEFAULTS:
            if isinstance(data.get(k), (int, float)):
                rules[k] = float(data[k])
        src = os.path.basename(path)
    except (OSError, ValueError):
        pass
    return rules, src


def dist_interval(cond, rules):
    """AI 조건 하나가 참이 되는 '두 몸 중심 사이 거리' 구간 (lo, hi). 거리 조건이 아니면 None.
    Unity AiContextAdapter.DistBand 와 같은 기준: melee <= distMeleeMax < mid <= distMidMax < far."""
    m = _DIST_RE.match(cond or "")
    if not m:
        return None
    var, op, rhs = m.group(1), m.group(2), m.group(3)
    inf = float("inf")
    mm, mx = rules["distMeleeMax"], rules["distMidMax"]
    if var == "dist":
        bands = {"melee": (0.0, mm), "mid": (mm, mx), "far": (mx, inf)}
        band = bands.get(rhs.lower())
        if band is None:
            return None
        if op == "==":
            return band
        if op == "!=":
            if rhs.lower() == "melee":
                return (mm, inf)
            if rhs.lower() == "far":
                return (0.0, mx)
            return (0.0, inf)  # != mid: 근접·원거리 모두 → 근접에서 가능하므로 검사 대상 아님
        return None
    try:
        x = float(rhs)
    except ValueError:
        return None
    if op in ("<=", "<"):
        return (0.0, x)
    if op in (">=", ">"):
        return (x, inf)
    if op == "==":
        return (x, x)
    return (0.0, inf)


def skill_reach(sk, rules):
    """시뮬레이터 BuildPlan 과 같은 도달 거리(두 몸 중심 사이, m)."""
    eff, hit, move = sk["effectiveRange"], sk["hitRange"], sk["moveDistance"]
    core = eff if eff > rules["instantRangedThreshold"] else max(eff, hit) + move
    return rules["bodyRadius"] * 2 + core


def check_ai_skill_reach(report, characters, skills, nodes, actions, behaviors, rules, rules_src):
    """
    AI 행의 거리 조건과, 그 캐릭터가 실제로 쓰게 될 스킬의 사거리가 맞는지 검사한다.
    - 무브셋에 해당 입력 슬롯이 없음 / 슬롯 스킬이 모두 비활성 → 그 행은 실행되지 않음
    - 거리 조건 구간에서 공격이 닿지 않음 → 시뮬레이터에서 '사거리 밖'으로 다음 행으로 넘어감
    결과는 '경고'로만 보고한다(변환은 막지 않음). 같은 판정은 무브셋·캐릭터를 묶어 한 줄로 낸다.
    """
    skill_by_id = {s["id"]: s for s in skills}
    action_by_id = {a["actionId"]: a for a in actions}
    slot_nodes = {}
    for n in nodes:
        slot_nodes.setdefault((n["groupId"], n["inputSlot"]), []).append(n)

    users = {}  # (aiId, movesetId) -> 캐릭터 baseName 목록
    for ch in characters:
        if not ch.get("enabled") or not ch.get("aiProfileId"):
            continue
        users.setdefault((ch["aiProfileId"], ch.get("movesetId") or ""), []).append(ch.get("baseName") or ch.get("displayName") or ch["id"])

    rows_by_ai = {}
    for b in behaviors:
        if b.get("enabled") and b.get("chancePct", 100) > 0:
            rows_by_ai.setdefault(b["aiId"], []).append(b)

    far_cap = max(rules["perceptionRange"], rules["distMidMax"] + 1.0)
    grouped = {}  # (aiId, behaviorIndex, 판정, 문구) -> {"movesets": set, "names": list}
    for (ai_id, moveset), names in sorted(users.items()):
        for b in rows_by_ai.get(ai_id, []):
            act = action_by_id.get(b["actionId"])
            forced = b.get("skillId") if b.get("skillId") not in (None, "", "NONE") else None
            if not forced and (act is None or act.get("actionType") != "INPUT"):
                continue
            # 이 행이 쓰는 스킬 후보
            if forced:
                cand = [skill_by_id[forced]] if forced in skill_by_id else []
                slot_txt = f"Skill_Id {forced}"
                if not cand:
                    continue  # 없는 Skill_Id 는 다른 검사 몫
            else:
                slot = act.get("inputSlot")
                ns = [n for n in slot_nodes.get((moveset, slot), []) if n["enabled"]]
                slot_txt = f"{slot}"
                if not ns:
                    key = (ai_id, b["behaviorIndex"], "슬롯 없음", f"무브셋에 {slot} 슬롯이 없어 이 행은 실행되지 않음")
                    grouped.setdefault(key, {"movesets": set(), "names": [], "b": b})
                    grouped[key]["movesets"].add(moveset)
                    grouped[key]["names"] += names
                    continue
                cand = [skill_by_id[n["skillId"]] for n in ns if n["skillId"] in skill_by_id]
            usable = [s for s in cand if s["enabled"] and s["valid"]]
            if not usable:
                key = (ai_id, b["behaviorIndex"], "스킬 비활성", f"{slot_txt} 스킬이 모두 비활성/검증 실패라 이 행은 실행되지 않음")
                grouped.setdefault(key, {"movesets": set(), "names": [], "b": b})
                grouped[key]["movesets"].add(moveset)
                grouped[key]["names"] += names
                continue
            attacks = [s for s in usable if s["skillType"] == "ATTACK" and s["hitTimesMs"]]
            if not attacks:
                continue  # 방어·버프 등은 사거리 검사 없음
            lo, hi = 0.0, float("inf")
            for cond in (b.get("condition1"), b.get("condition2")):
                iv = dist_interval(cond, rules)
                if iv is not None:
                    lo, hi = max(lo, iv[0]), min(hi, iv[1])
            if lo <= 0.0:
                continue  # 붙어 있을 때도 참 → 사거리와 무관하게 실행 가능
            best = max(attacks, key=lambda s: skill_reach(s, rules))
            reach = skill_reach(best, rules)
            top = min(hi, far_cap)
            if reach <= lo + 1e-9:
                verdict = "항상 사거리 밖"
            else:
                cover = (min(reach, top) - lo) / (top - lo) if top > lo else 1.0
                if cover >= 0.5:
                    continue
                verdict = f"대부분 사거리 밖(조건 구간의 {cover * 100:.0f}%만 닿음)"
            hi_txt = "∞" if hi == float("inf") else f"{hi:g}m"
            msg = (f"거리 조건 {lo:g}m 초과~{hi_txt} 인데 {slot_txt} 최대 도달 {reach:.1f}m "
                   f"(사거리 {best['effectiveRange']:g} / 판정 {best['hitRange']:g} / 전진 {best['moveDistance']:g}"
                   f" + 몸 반지름 {rules['bodyRadius']:g}×2) → {verdict}")
            # 같은 행·같은 판정·같은 수치는 무브셋이 달라도 한 줄로 묶는다
            key = (ai_id, b["behaviorIndex"], verdict.split("(")[0], msg)
            grouped.setdefault(key, {"movesets": set(), "names": [], "b": b, "skills": []})
            grouped[key]["movesets"].add(moveset)
            grouped[key]["names"] += names
            grouped[key]["skills"].append(best["id"])

    count = 0
    for (ai_id, bi, _, msg), g in sorted(grouped.items(), key=lambda kv: (kv[0][0], kv[0][1])):
        b = g["b"]
        conds = " & ".join(c for c in (b.get("condition1"), b.get("condition2")) if c) or "조건 없음"
        uniq = sorted(set(g["names"]))
        who = ", ".join(uniq[:6]) + (f" 외 {len(uniq) - 6}종" if len(uniq) > 6 else "")
        ms = sorted(g["movesets"])
        ms_txt = ", ".join(ms[:4]) + (f" 외 {len(ms) - 4}개" if len(ms) > 4 else "")
        sk = sorted(set(g.get("skills", [])))
        sk_txt = f" / 스킬 예: {sk[0]}" if sk else ""
        report.warn(f"AI.xlsx!AI_BEHAVIOR #{bi}",
                    f"[AI·스킬 불일치] {ai_id} ({conds} → {b['actionId']} {b.get('chancePct', 100):g}%): {msg}. "
                    f"무브셋 {ms_txt}{sk_txt} / 대상 {who} (캐릭터 {len(g['names'])}개)")
        count += 1
    if count:
        report.info("AI.xlsx!AI_BEHAVIOR",
                    f"AI·스킬 불일치 경고 {count}건: 거리 기준 {rules_src} (근접 ≤{rules['distMeleeMax']:g}m, 중거리 ≤{rules['distMidMax']:g}m, "
                    f"몸 반지름 {rules['bodyRadius']:g}m, 즉발 원거리 사거리 >{rules['instantRangedThreshold']:g}m). "
                    f"원거리 끝은 인식 거리 {far_cap:g}m 로 보고 계산")
    return count


def write_report(report, manifest, path):
    lines = ["# 데이터 변환 검증 보고서", ""]
    lines.append(f"- 생성: {datetime.datetime.now().isoformat(timespec='seconds')}")
    lines.append(f"- 변환 도구: {TOOL_VERSION}")
    if manifest:
        lines.append(f"- dataHash: {manifest['dataHash']}")
        for k, v in manifest["counts"].items():
            lines.append(f"- {k}: {v}")
    lines.append(f"- 배포 차단 오류: {len(report.errors)} / 레코드 비활성: {len(report.disabled)} / "
                 f"경고: {len(report.warnings)} / 정보: {len(report.infos)}")
    lines.append("")
    for title, items in (("배포 차단 오류", report.errors), ("레코드 비활성(실행 제외)", report.disabled),
                         ("경고", report.warnings), ("정보", report.infos)):
        lines.append(f"## {title} ({len(items)})")
        lines.append("")
        for where, msg in items:
            lines.append(f"- `{where}` {msg}")
        lines.append("")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def main():
    ap = argparse.ArgumentParser(description="ARPG Excel -> JSON 변환기")
    ap.add_argument("--tables", default=DEFAULT_TABLES, help="Table_ARPG 폴더")
    ap.add_argument("--out", default=DEFAULT_OUT, help="JSON 출력 폴더")
    ap.add_argument("--scenarios", default=DEFAULT_SCENARIO_OUT, help="기본 시나리오 출력 폴더")
    ap.add_argument("--no-scenarios", action="store_true")
    ap.add_argument("--rules", default=None, help="거리 기준을 읽을 rules.json (기본: 출력 폴더의 상위 rules.json)")
    args = ap.parse_args()

    report, manifest = export(args.tables, args.out, args.scenarios, not args.no_scenarios, args.rules)
    os.makedirs(args.out, exist_ok=True)
    report_path = os.path.join(args.out, "validation_report.md")
    write_report(report, manifest, report_path)
    print(f"오류 {len(report.errors)} / 비활성 {len(report.disabled)} / 경고 {len(report.warnings)} / 정보 {len(report.infos)}")
    mismatch = sum(1 for _, m in report.warnings if m.startswith("[AI·스킬 불일치]"))
    if mismatch:
        print(f"AI·스킬 불일치 경고 {mismatch}건: AI 거리 조건에서 공격이 닿지 않는 행이 있습니다(보고서의 '경고' 참고)")
    print(f"보고서: {report_path}")
    if manifest is None:
        print("필수 검증 실패: 기존 JSON 을 유지했습니다.")
        sys.exit(1)
    print(f"dataHash={manifest['dataHash']}  characters={manifest['counts']['characters']}  skills={manifest['counts']['skills']}")


if __name__ == "__main__":
    main()
