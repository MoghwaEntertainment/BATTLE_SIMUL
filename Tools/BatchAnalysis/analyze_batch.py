#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""BalanceMaker 배치 결과 분석기 (표준 라이브러리만 사용)

사용:
  python analyze_batch.py <export 폴더>
  python analyze_batch.py <export 폴더> --baseline <비교할 이전 export 폴더>
  python analyze_batch.py <export 폴더> --out report.md --entity-csv entity_results_from_events.csv

export 폴더에는 manifest.json, results.csv, skill_stats.csv, summary.csv 가 있어야 한다.
events.csv 와 entity_results.csv 는 있으면 사용한다.
파일 이름 앞에 다른 글자가 붙어 있어도(예: 1234-results.csv) 찾는다.

보고서 내용
  1. 실행 정보   2. 승패·편향 검정   3. 전투 시간·시간 초과   4. 스킬   5. 클래스
  6. 이벤트·개체 기반: 클래스별 개체 결과(entity_results.csv 가 있으면 모든 판, 없으면 이벤트 기록 판),
     처치, 상태이상 누적, 전투 정지 감지, 미러 분기(같은 자리 A/B 의 판단이 처음 갈린 지점)
  7. --baseline 을 주면 주요 지표 비교표
"""
import argparse
import csv
import glob
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict

CLASS_BY_PREFIX = {
    "WARRIOR": "워리어", "GUARDIAN": "가디언", "ARCHER": "아쳐",
    "WITCH": "위치", "MAGE": "매지션", "PRIEST": "프리스트",
}
STALL_GAP_SEC = 10.0      # 마지막 교전 뒤 이 시간 넘게 피해가 없으면 '정지'로 본다
ASYM_MIN_HITS = 500       # 팀 간 비대칭 검사 최소 적중 수
ASYM_Z = 4.0              # 적중 수 차이 z 값 기준
ASYM_RATIO = 0.15         # 피해 비율 차이 기준


# ---------------------------------------------------------------- 읽기
def find_file(folder, name, required=True):
    exact = os.path.join(folder, name)
    if os.path.isfile(exact):
        return exact
    cands = sorted(glob.glob(os.path.join(folder, "*" + name)))
    if cands:
        return cands[0]
    if required:
        sys.exit(f"[오류] {folder} 에서 {name} 을 찾지 못했다")
    return None


def read_csv(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def num(v, default=None):
    if v is None or v == "":
        return default
    try:
        return float(v)
    except ValueError:
        return default


def load_run(folder):
    run = {"folder": folder}
    with open(find_file(folder, "manifest.json"), encoding="utf-8-sig") as f:
        run["manifest"] = json.load(f)
    run["results"] = read_csv(find_file(folder, "results.csv"))
    run["skills"] = read_csv(find_file(folder, "skill_stats.csv"))
    sp = find_file(folder, "summary.csv", required=False)
    run["summary"] = {r["metric"]: r["value"] for r in read_csv(sp)} if sp else {}
    ep = find_file(folder, "events.csv", required=False)
    run["events"] = read_csv(ep) if ep else []
    xp = find_file(folder, "entity_results.csv", required=False)
    run["entities_file"] = read_csv(xp) if xp else []
    return run


# ---------------------------------------------------------------- 통계
def binom_two_sided(k, n, p=0.5):
    """정확한 이항 검정 양측 p 값"""
    if n == 0:
        return 1.0
    def logpmf(i):
        return (math.lgamma(n + 1) - math.lgamma(i + 1) - math.lgamma(n - i + 1)
                + i * math.log(p) + (n - i) * math.log(1 - p))
    ref = logpmf(k)
    total = 0.0
    for i in range(n + 1):
        lp = logpmf(i)
        if lp <= ref + 1e-9:
            total += math.exp(lp)
    return min(1.0, total)


def wilson(k, n, z=1.96):
    if n == 0:
        return (0.0, 0.0)
    ph = k / n
    d = 1 + z * z / n
    c = (ph + z * z / (2 * n)) / d
    h = z * math.sqrt(ph * (1 - ph) / n + z * z / (4 * n * n)) / d
    return (c - h, c + h)


def quantile(xs, q):
    if not xs:
        return float("nan")
    s = sorted(xs)
    pos = (len(s) - 1) * q
    lo, hi = math.floor(pos), math.ceil(pos)
    return s[lo] + (s[hi] - s[lo]) * (pos - lo)


def mean(xs):
    return sum(xs) / len(xs) if xs else float("nan")


def class_of_skill(skill_id):
    return CLASS_BY_PREFIX.get(skill_id.split("_")[0], skill_id.split("_")[0])


def fmt(v, d=1):
    if v is None or (isinstance(v, float) and math.isnan(v)):
        return "—"
    if isinstance(v, float):
        return f"{v:,.{d}f}"
    return f"{v:,}" if isinstance(v, int) else str(v)


def pct(v, d=1):
    return "—" if v is None or (isinstance(v, float) and math.isnan(v)) else f"{v * 100:.{d}f}%"


def table(headers, rows):
    out = ["| " + " | ".join(headers) + " |", "|" + "---|" * len(headers)]
    for r in rows:
        out.append("| " + " | ".join(str(c) for c in r) + " |")
    return "\n".join(out)


# ---------------------------------------------------------------- 분석: 결과
def is_mirror(manifest):
    teams = manifest.get("scenario", {}).get("teams", [])
    if len(teams) != 2:
        return False
    def comp(t):
        return sorted((m["definitionId"], m.get("count", 1)) for m in t.get("members", []))
    return comp(teams[0]) == comp(teams[1])


def analyze_outcomes(run):
    res = run["results"]
    n = len(res)
    c = Counter(r["end_reason"] for r in res)
    w0 = sum(1 for r in res if r["end_reason"] == "TeamVictory" and r["winner_team"] == "0")
    w1 = sum(1 for r in res if r["end_reason"] == "TeamVictory" and r["winner_team"] == "1")
    dec = w0 + w1
    m = {
        "runs": n, "team0_wins": w0, "team1_wins": w1,
        "mutual": c.get("MutualWipe", 0), "timeout": c.get("Timeout", 0),
        "timeout_rate": c.get("Timeout", 0) / n if n else float("nan"),
        "decisive": dec, "team0_decisive_rate": w0 / dec if dec else float("nan"),
        "p_value": binom_two_sided(w0, dec) if dec else float("nan"),
        "ci": wilson(w0, dec), "mirror": is_mirror(run["manifest"]),
    }
    dur_dec = [num(r["duration_sec"]) for r in res if r["end_reason"] != "Timeout"]
    dur_all = [num(r["duration_sec"]) for r in res]
    m["dur_dec"] = dur_dec
    m["dur_all"] = dur_all
    t = [r for r in res if r["end_reason"] == "Timeout"]
    m["timeout_alive"] = Counter((r["team0_alive"], r["team1_alive"]) for r in t)
    m["timeout_hp"] = (mean([num(r["team0_hp"]) for r in t]), mean([num(r["team1_hp"]) for r in t]))
    return m


# ---------------------------------------------------------------- 분석: 스킬·클래스
def analyze_skills(run):
    rows = run["skills"]
    n_battles = int(run["manifest"].get("runsCompleted") or len(run["results"]))
    teams = sorted({r["team_id"] for r in rows})
    nt = max(1, len(teams))
    agg = defaultdict(lambda: {"uses": 0, "hits": 0, "avoided": 0, "guarded": 0, "dmg": 0.0})
    per_team = defaultdict(dict)
    for r in rows:
        a = agg[r["skill_id"]]
        a["uses"] += int(num(r["uses"], 0))
        a["hits"] += int(num(r["hits"], 0))
        a["avoided"] += int(num(r["avoided"], 0))
        a["guarded"] += int(num(r.get("guarded"), 0) or 0)
        a["dmg"] += num(r["damage_total"], 0.0)
        per_team[r["skill_id"]][r["team_id"]] = r
    total_dmg = sum(a["dmg"] for a in agg.values())
    skills = {}
    for sid, a in agg.items():
        skills[sid] = {
            "class": class_of_skill(sid),
            "uses_pb": a["uses"] / (n_battles * nt),
            "dmg_per_use": a["dmg"] / a["uses"] if a["uses"] else 0.0,
            "hits_per_use": a["hits"] / a["uses"] if a["uses"] else 0.0,
            "avoid_rate": a["avoided"] / (a["hits"] + a["avoided"]) if (a["hits"] + a["avoided"]) else float("nan"),
            "dmg_pb": a["dmg"] / (n_battles * nt),
            "share": a["dmg"] / total_dmg if total_dmg else 0.0,
        }
    # 팀 간 비대칭
    asym = []
    if len(teams) == 2:
        for sid, pt in per_team.items():
            if teams[0] not in pt or teams[1] not in pt:
                continue
            h0, h1 = num(pt[teams[0]]["hits"], 0), num(pt[teams[1]]["hits"], 0)
            d0, d1 = num(pt[teams[0]]["damage_total"], 0), num(pt[teams[1]]["damage_total"], 0)
            u0, u1 = num(pt[teams[0]]["uses"], 0), num(pt[teams[1]]["uses"], 0)
            if min(h0, h1) < ASYM_MIN_HITS:
                continue
            z = (h0 - h1) / math.sqrt(h0 + h1)
            ratio = d0 / d1 if d1 else float("inf")
            if abs(z) >= ASYM_Z or abs(ratio - 1) >= ASYM_RATIO:
                asym.append((sid, u0, u1, h0, h1, d0, d1, z, ratio))
    classes = defaultdict(float)
    for sid, s in skills.items():
        classes[s["class"]] += s["dmg_pb"]
    tot = sum(classes.values())
    class_share = {k: (v, v / tot if tot else 0.0) for k, v in classes.items()}
    return {"skills": skills, "asym": asym, "class_share": class_share, "total_dmg_pb": tot}


# ---------------------------------------------------------------- 분석: 이벤트
def parse_buildup(reason):
    m = re.search(r"([\d.]+)→([\d.]+)\s*/\s*저항\s*([\d.]+)", reason or "")
    if not m:
        return None
    return float(m.group(1)), float(m.group(2)), float(m.group(3))


def analyze_events(run):
    ev = run["events"]
    if not ev and not run["entities_file"]:
        return None
    by_battle = defaultdict(list)
    for e in ev:
        by_battle[int(e["battle_index"])].append(e)
    tick_rate = 30.0
    for e in ev:
        t, s = num(e["tick"], 0), num(e["sim_time_sec"], 0)
        if t and s:
            tick_rate = round(t / s)
            break

    entities = []          # 개체별 결과
    kills = Counter()      # (killer class, victim class)
    buildup = defaultdict(lambda: {"n": 0, "peak": 0.0, "peak_ratio": 0.0, "per_hit": Counter()})
    status_events = Counter()
    stalls = []
    divergences = []
    mirror = is_mirror(run["manifest"])

    for b, rows in sorted(by_battle.items()):
        rows.sort(key=lambda e: int(e["event_sequence"]))
        info = {}
        for e in rows:
            lab = e["actor_label"]
            if lab and lab not in info:
                info[lab] = {"name": e["actor_name"], "team": e["team_id"],
                             "def": e["actor_definition_id"], "max_hp": num(e["actor_hp_before"])}
            elif lab and info[lab]["max_hp"] is None:
                info[lab]["max_hp"] = num(e["actor_hp_before"])
        end_tick = max(int(e["tick"]) for e in rows)
        end_reason = next((e["result"] for e in rows if e["event_type"] == "BattleEnded"), "")
        deaths = {e["actor_label"]: int(e["tick"]) for e in rows if e["event_type"] == "EntityDied"}
        hits = [e for e in rows if e["event_type"] == "HitResolved" and num(e["damage"]) is not None]
        combat_ticks = [int(e["tick"]) for e in rows if e["event_type"] in ("HitResolved", "HitAvoided")]
        last_combat = max(combat_ticks) if combat_ticks else 0

        # 처치: 사망 tick 이전 마지막 적중
        for lab, dt in deaths.items():
            last = None
            for h in hits:
                if h["target_label"] == lab and int(h["tick"]) <= dt:
                    last = h
            if last is not None:
                kills[(last["actor_name"], info.get(lab, {}).get("name", lab))] += 1

        unaware = defaultdict(list)
        for e in rows:
            if e["event_type"] == "ActionStarted" and "미인지" in (e["reason"] or ""):
                unaware[e["actor_label"]].append(int(e["tick"]))

        for lab, inf in info.items():
            dt = deaths.get(lab)
            alive_end = dt if dt is not None else end_tick
            active_end = min(alive_end, last_combat) if last_combat else alive_end
            dealt = sum(num(h["damage"]) for h in hits if h["actor_label"] == lab)
            taken = sum(num(h["damage"]) for h in hits if h["target_label"] == lab)
            heal_recv = heal_done = 0.0
            for e in rows:
                if e["event_type"] == "BuffApplied":
                    hb, ha = num(e["actor_hp_before"]), num(e["actor_hp_after"])
                    if hb is not None and ha is not None and ha > hb:
                        if e["actor_label"] == lab:
                            heal_recv += ha - hb
                        if e["target_label"] == lab:
                            heal_done += ha - hb
            stg_recv = sum(1 for e in rows if e["event_type"] == "Staggered" and e["actor_label"] == lab)
            stg_cause = sum(1 for e in rows if e["event_type"] == "Staggered" and e["target_label"] == lab)
            k = 0
            for vlab, vdt in deaths.items():
                last = None
                for h in hits:
                    if h["target_label"] == vlab and int(h["tick"]) <= vdt:
                        last = h
                if last is not None and last["actor_label"] == lab:
                    k += 1
            ua = unaware.get(lab, [])
            entities.append({
                "battle_index": b, "label": lab, "team_id": inf["team"], "definition_id": inf["def"],
                "class_name": inf["name"], "max_hp": inf["max_hp"], "died": int(dt is not None),
                "death_tick": dt if dt is not None else "", "alive_sec": round(alive_end / tick_rate, 3),
                "active_sec": round(active_end / tick_rate, 3), "damage_dealt": round(dealt, 3),
                "damage_taken": round(taken, 3), "kills": k, "heal_done": round(heal_done, 3),
                "heal_received": round(heal_recv, 3), "stagger_caused": stg_cause,
                "stagger_received": stg_recv,
                "unaware_sec": round((max(ua) - min(ua)) / tick_rate, 3) if len(ua) > 1 else 0.0,
            })

        for e in rows:
            et = e["event_type"]
            if et.startswith("Status") and et != "StatusBuildup":
                status_events[(et, e["status_id"])] += 1
            if et == "StatusBuildup":
                p = parse_buildup(e["reason"])
                if p:
                    a, bb, res = p
                    s = buildup[e["status_id"] or "?"]
                    s["n"] += 1
                    s["peak"] = max(s["peak"], bb)
                    s["peak_ratio"] = max(s["peak_ratio"], bb / res if res else 0)
                    s["per_hit"][(e["skill_id"], round(bb - a, 1))] += 1

        # 전투 정지 감지
        if (end_tick - last_combat) / tick_rate > STALL_GAP_SEC:
            alive = [lab for lab in info if lab not in deaths or deaths[lab] > last_combat]
            reasons = Counter(e["reason"] for e in rows
                              if e["event_type"] == "ActionStarted" and int(e["tick"]) > last_combat)
            stalls.append({"battle": b, "last_combat_sec": last_combat / tick_rate,
                           "idle_sec": (end_tick - last_combat) / tick_rate, "end": end_reason,
                           "alive": [f"{lab}({info[lab]['name']})" for lab in alive],
                           "reasons": reasons.most_common(3)})

        # 미러 분기: A0k / B0k 의 행동 시작 순서를 비교
        if mirror:
            seq = defaultdict(list)
            for e in rows:
                if e["event_type"] == "ActionStarted" and e["actor_label"]:
                    rule = (e["reason"] or "").split(" ")[0]
                    seq[e["actor_label"]].append((int(e["tick"]), e["action_id"], rule, e["reason"]))
            for lab in seq:
                if not lab.startswith("A"):
                    continue
                other = "B" + lab[1:]
                if other not in seq:
                    continue
                for x, y in zip(seq[lab], seq[other]):
                    if x[:3] != y[:3]:
                        divergences.append({"battle": b, "slot": lab[1:], "class": info[lab]["name"],
                                            "tick": min(x[0], y[0]), "A": x, "B": y})
                        break

    # 개체별 결과 파일이 있으면 그쪽을 우선 사용(모든 판)
    ent_source = "events.csv(기록된 판)"
    ents = entities
    if run["entities_file"]:
        ents = run["entities_file"]
        ent_source = "entity_results.csv(모든 판)"
    per_class = defaultdict(lambda: defaultdict(list))
    for x in ents:
        c = x["class_name"]
        for key in ("alive_sec", "active_sec", "damage_dealt", "damage_taken", "kills",
                    "heal_done", "stagger_caused", "stagger_received", "unaware_sec", "died"):
            v = num(x.get(key))
            if v is None and key == "active_sec":
                v = num(x.get("alive_sec"))  # C# entity_results.csv 에는 active_sec 이 없다 → 생존 시간으로 대신
            if v is not None:
                per_class[c][key].append(v)

    # 같은 tick·같은 판단으로 여러 판에서 반복된 분기 = seed 와 무관한 비대칭 의심
    pattern = Counter((d["slot"], d["tick"], d["A"][1], d["A"][2], d["B"][1], d["B"][2]) for d in divergences)
    return {"battles": sorted(by_battle), "tick_rate": tick_rate, "entities": entities,
            "per_class": per_class, "ent_source": ent_source, "kills": kills, "buildup": buildup,
            "status_events": status_events, "stalls": stalls, "divergences": divergences,
            "div_pattern": pattern}


# ---------------------------------------------------------------- 보고서
def report(run, out, sk, evr):
    mf = run["manifest"]
    L = []
    L.append(f"# 배치 분석 보고서 — {mf.get('scenarioName', '')}")
    L.append("")
    L.append("## 1. 실행 정보")
    L.append(f"- runId: `{mf.get('runId')}`")
    L.append(f"- 시나리오: {mf.get('scenarioName')} — {mf.get('scenario', {}).get('description', '')}")
    teams = mf.get("scenario", {}).get("teams", [])
    for t in teams:
        mem = ", ".join(f"{m['definitionId']}x{m.get('count', 1)}" for m in t.get("members", []))
        L.append(f"- 팀 {t.get('teamId')} ({t.get('name', '')}): {mem}")
    L.append(f"- 미러 구성: {'예' if out['mirror'] else '아니오'}")
    L.append(f"- dataHash `{mf.get('dataHash')}` · rules `{mf.get('rulesVersion')}` (rulesHash `{mf.get('rulesHash')}`)"
             f" · masterSeed {mf.get('masterSeed')} · {mf.get('runsCompleted')}판")
    L.append("")

    L.append("## 2. 승패와 편향 검정")
    n = out["runs"]
    L.append(table(["팀 0 승", "팀 1 승", "동시 전멸", "시간 초과", "승패 난 판 중 팀 0", "95% 구간", "이항 검정 p"],
                   [[out["team0_wins"], out["team1_wins"], out["mutual"],
                     f"{out['timeout']} ({pct(out['timeout_rate'])})",
                     pct(out["team0_decisive_rate"]), f"{pct(out['ci'][0])} ~ {pct(out['ci'][1])}",
                     f"{out['p_value']:.4f}" if out["decisive"] else "—"]]))
    if out["mirror"] and out["decisive"] and out["p_value"] < 0.05:
        L.append("")
        L.append(f"**경고**: 미러 구성인데 승패가 50%에서 벗어났다(p = {out['p_value']:.4f}). 처리 순서 편향을 의심한다.")
    L.append("")

    L.append("## 3. 전투 시간과 시간 초과")
    dd, da = out["dur_dec"], out["dur_all"]
    L.append(table(["구분", "판 수", "평균(초)", "중앙값", "p10", "p90"],
                   [["시간 초과 아닌 판", len(dd), fmt(mean(dd)), fmt(quantile(dd, .5)), fmt(quantile(dd, .1)), fmt(quantile(dd, .9))],
                    ["전체", len(da), fmt(mean(da)), fmt(quantile(da, .5)), fmt(quantile(da, .1)), fmt(quantile(da, .9))]]))
    if out["timeout"]:
        L.append("")
        combos = ", ".join(f"{a}대{b} {c}판" for (a, b), c in out["timeout_alive"].most_common(5))
        L.append(f"- 시간 초과 판의 생존 수(팀0대팀1): {combos}")
        L.append(f"- 시간 초과 판의 남은 HP 평균: 팀0 {fmt(out['timeout_hp'][0])} · 팀1 {fmt(out['timeout_hp'][1])}")
    L.append("")

    L.append("## 4. 스킬 (양 팀 평균, 판당)")
    rows = []
    for sid, s in sorted(sk["skills"].items(), key=lambda kv: -kv[1]["dmg_pb"]):
        rows.append([sid, s["class"], fmt(s["uses_pb"], 2), fmt(s["dmg_per_use"]), fmt(s["hits_per_use"], 2),
                     pct(s["avoid_rate"]), fmt(s["dmg_pb"]), pct(s["share"])])
    L.append(table(["스킬", "클래스", "판당 사용", "사용당 피해", "사용당 적중", "피해진 비율", "판당 피해", "팀 피해 비중"], rows))
    if sk["asym"]:
        L.append("")
        L.append("팀 간 비대칭(적중 수 z ≥ 4 또는 피해 차 15% 이상):")
        L.append("")
        L.append(table(["스킬", "사용 0/1", "적중 0/1", "피해 0/1", "z", "피해 비"],
                       [[a[0], f"{int(a[1])}/{int(a[2])}", f"{int(a[3])}/{int(a[4])}",
                         f"{fmt(a[5], 0)}/{fmt(a[6], 0)}", f"{a[7]:.1f}", f"{a[8]:.2f}"] for a in sk["asym"]]))
    L.append("")

    L.append("## 5. 클래스별 피해 (skill_stats, 판당)")
    L.append(table(["클래스", "판당 피해", "비중"],
                   [[c, fmt(v), pct(s)] for c, (v, s) in sorted(sk["class_share"].items(), key=lambda kv: -kv[1][0])]))
    L.append("")

    if evr:
        bl = ", ".join(map(str, evr["battles"])) if evr["battles"] else "events.csv 없음"
        L.append(f"## 6. 이벤트·개체 기반 (이벤트 기록 판: {bl})")
        L.append("")
        L.append(f"### 클래스별 개체 결과 — 출처: {evr['ent_source']}")
        pc = evr["per_class"]
        rows = []
        for c, d in sorted(pc.items(), key=lambda kv: -sum(kv[1]["damage_dealt"])):
            act = sum(d["active_sec"]) if d["active_sec"] else float("nan")
            dps = sum(d["damage_dealt"]) / act if act else float("nan")
            dtps = sum(d["damage_taken"]) / act if act else float("nan")
            tot = lambda xs: int(sum(xs)) if xs else "—"
            rows.append([c, len(d["alive_sec"]), tot(d["died"]), fmt(mean(d["active_sec"])),
                         fmt(mean(d["damage_dealt"])), fmt(mean(d["damage_taken"])), fmt(dps), fmt(dtps),
                         tot(d["kills"]), fmt(mean(d["heal_done"])), fmt(mean(d["stagger_caused"])),
                         fmt(mean(d["unaware_sec"]))])
        L.append(table(["클래스", "개체", "사망", "교전 중 생존(초)", "준 피해", "받은 피해", "초당 준 피해", "초당 받은 피해",
                        "처치", "치유", "건 경직", "미인지(초)"], rows))
        L.append("")
        L.append("교전 중 생존 = 사망 또는 그 판의 마지막 교전까지(entity_results.csv 는 마지막 교전 정보가 없어 사망 또는 판 종료까지). "
                 "초당 피해도 이 시간으로 나눈다. — 는 그 열이 없는 경우.")
        L.append("")
        if evr["kills"]:
            killers = sorted({k for k, _ in evr["kills"]})
            victims = sorted({v for _, v in evr["kills"]})
            L.append("### 처치 (가로: 죽은 쪽, 세로: 처치한 쪽)")
            L.append(table(["처치 \\ 사망"] + victims,
                           [[k] + [evr["kills"].get((k, v), 0) for v in victims] for k in killers]))
            L.append("")
        if evr["buildup"]:
            L.append("### 상태이상 누적")
            rows = []
            for sid, s in sorted(evr["buildup"].items()):
                per = ", ".join(f"{k[0]} {k[1]:g}" for k, _ in s["per_hit"].most_common(3))
                trig = sum(c for (et, st), c in evr["status_events"].items() if st == sid and et != "StatusExpired")
                rows.append([sid, s["n"], fmt(s["peak"]), pct(s["peak_ratio"]), trig, per])
            L.append(table(["상태", "누적 이벤트", "최고 누적", "저항 대비 최고", "발동·적용 이벤트", "1타 누적(스킬)"], rows))
            L.append("")
        if evr["status_events"]:
            L.append("- 상태 이벤트: " + ", ".join(f"{et}/{st} {c}" for (et, st), c in evr["status_events"].most_common()))
            L.append("")
        L.append("### 전투 정지 감지")
        if not evr["battles"]:
            L.append("- events.csv 가 없어 확인하지 못함")
        elif evr["stalls"]:
            for s in evr["stalls"]:
                rs = "; ".join(f"{r} ×{c}" for r, c in s["reasons"])
                L.append(f"- 판 {s['battle']}: 마지막 교전 {s['last_combat_sec']:.1f}초, 이후 {s['idle_sec']:.1f}초 피해 없음"
                         f" ({s['end']}). 생존 {', '.join(s['alive'])}. 이후 행동: {rs}")
        else:
            L.append(f"- 없음 (마지막 교전 뒤 {STALL_GAP_SEC:g}초 넘게 피해 없는 판 없음)")
        L.append("")
        if out["mirror"] and evr["battles"]:
            L.append("### 미러 분기 (같은 자리 A/B 의 행동이 처음 갈린 지점)")
            if evr["divergences"]:
                rows = [[d["battle"], d["slot"], d["class"], d["tick"], f"{d['A'][1]} {d['A'][2]}",
                         f"{d['B'][1]} {d['B'][2]}"] for d in sorted(evr["divergences"], key=lambda d: (d["tick"], d["battle"]))]
                L.append(table(["판", "자리", "클래스", "tick", "A 행동", "B 행동"], rows))
                rep = [(k, c) for k, c in evr["div_pattern"].items() if c >= 2]
                if rep:
                    L.append("")
                    L.append("**seed 와 무관하게 반복된 분기** (판마다 seed 가 다른데 같은 tick·같은 판단):")
                    for k, c in rep:
                        L.append(f"- 자리 {k[0]} tick {k[1]}: A {k[2]} {k[3]} / B {k[4]} {k[5]} — {c}판")
            else:
                L.append("- 분기 없음")
            L.append("")
    return "\n".join(L)


def key_metrics(run, out, sk, evr):
    m = {
        "시간 초과 비율": out["timeout_rate"],
        "승패 난 판 중 팀 0": out["team0_decisive_rate"],
        "편향 검정 p": out["p_value"],
        "시간 초과 아닌 판 전투 시간 중앙값(초)": quantile(out["dur_dec"], .5),
    }
    for sid, s in sk["skills"].items():
        if s["dmg_pb"] > 0:
            m[f"비중 {sid}"] = s["share"]
    for c, (v, s) in sk["class_share"].items():
        m[f"클래스 비중 {c}"] = s
    if evr:
        for c, d in evr["per_class"].items():
            m[f"교전 중 생존(초) {c}"] = mean(d["active_sec"])
        trig = sum(c for (et, st), c in evr["status_events"].items() if et != "StatusExpired")
        m["상태 발동·적용 이벤트(기록 판)"] = float(trig)
        m["전투 정지 판(기록 판)"] = float(len(evr["stalls"]))
    return m


def compare(base, cur):
    rate_keys = ("비율", "팀 0", "비중")
    keys = list(dict.fromkeys(list(cur.keys()) + list(base.keys())))
    rows = []
    for k in keys:
        a, b = base.get(k), cur.get(k)
        is_rate = any(t in k for t in rate_keys)
        f = (lambda v: pct(v)) if is_rate else (lambda v: fmt(v, 3 if "p" in k else 1))
        delta = "—"
        if a is not None and b is not None and not (math.isnan(a) or math.isnan(b)):
            delta = f"{(b - a) * 100:+.1f}%p" if is_rate else f"{b - a:+.2f}"
        rows.append([k, f(a) if a is not None else "—", f(b) if b is not None else "—", delta])
    return table(["지표", "기준", "이번", "차이"], rows)


def analyze(folder):
    run = load_run(folder)
    out = analyze_outcomes(run)
    sk = analyze_skills(run)
    evr = analyze_events(run)
    return run, out, sk, evr


def main():
    ap = argparse.ArgumentParser(description="BalanceMaker 배치 결과 분석기")
    ap.add_argument("folder", help="이번 export 폴더")
    ap.add_argument("--baseline", help="비교할 이전 export 폴더")
    ap.add_argument("--out", help="보고서 저장 경로(.md)")
    ap.add_argument("--entity-csv", help="events.csv 에서 계산한 개체별 결과를 이 경로로 저장")
    args = ap.parse_args()

    run, out, sk, evr = analyze(args.folder)
    text = report(run, out, sk, evr)

    if args.baseline:
        brun, bout, bsk, bevr = analyze(args.baseline)
        bm, cm = brun["manifest"], run["manifest"]
        text += "\n\n## 7. 기준 실행과 비교\n\n"
        text += f"- 기준: `{bm.get('runId')}` · data `{bm.get('dataHash')}` · rules `{bm.get('rulesVersion')}`\n"
        text += f"- 이번: `{cm.get('runId')}` · data `{cm.get('dataHash')}` · rules `{cm.get('rulesVersion')}`\n"
        if bm.get("scenarioSignature") != cm.get("scenarioSignature"):
            text += "- **주의**: 시나리오 서명이 다르다. 같은 조건 비교가 아니다.\n"
        text += "\n" + compare(key_metrics(brun, bout, bsk, bevr), key_metrics(run, out, sk, evr)) + "\n"

    if args.entity_csv and evr and evr["entities"]:
        cols = list(evr["entities"][0].keys())
        with open(args.entity_csv, "w", encoding="utf-8-sig", newline="") as f:
            w = csv.DictWriter(f, fieldnames=cols)
            w.writeheader()
            w.writerows(evr["entities"])

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text + "\n")
        print(f"보고서 저장: {args.out}")
    else:
        sys.stdout.reconfigure(encoding="utf-8") if hasattr(sys.stdout, "reconfigure") else None
        print(text)


if __name__ == "__main__":
    main()
