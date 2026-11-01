using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARPG.Core.AI
{
    /// <summary>AI 조건식이 읽는 상황 변수(Condition_Ref). 임의 코드 실행 없이 허용 변수만 해석한다.</summary>
    public interface IAiContext
    {
        double DistM { get; }
        string DistBand { get; }          // melee / mid / far
        bool Perceived { get; }
        double HpPct { get; }
        bool PoiseBroken { get; }
        double StaminaPct { get; }
        int Phase { get; }
        string TargetStateName { get; }
        bool TargetHealRecover { get; }
        bool TargetAttackRecover { get; }
        bool IncomingAttack { get; }
        string LastAction { get; }
        bool ActionUsedRecent(string actionId, int n);
        bool OnCooldown(string actionId);
    }

    public sealed class AiCondition
    {
        private enum Var
        {
            Dist, DistM, Perceived, HpPct, PoiseBroken, StaminaPct, Phase, TargetState,
            TargetHealRecover, TargetAttackRecover, IncomingAttack, LastAction,
            FnActionUsedRecent, FnOnCooldown,
        }

        private enum Op { Eq, Ne, Lt, Le, Gt, Ge }

        private sealed class Term
        {
            public Var Var;
            public Op Op;
            public double Number;
            public bool Bool;
            public string Text;
            public string FnArgId;
            public int FnArgN;
        }

        private readonly Term[] _terms;
        public string Source { get; private set; }
        public bool IsEmpty { get { return _terms.Length == 0; } }

        private AiCondition(string source, Term[] terms)
        {
            Source = source;
            _terms = terms;
        }

        public static readonly AiCondition Always = new AiCondition(string.Empty, new Term[0]);

        public static bool TryParse(string source, out AiCondition condition, out string error)
        {
            condition = Always;
            error = null;
            if (string.IsNullOrWhiteSpace(source)) return true;
            var terms = new List<Term>();
            foreach (string raw in source.Split('&'))
            {
                string t = raw.Trim();
                if (t.Length == 0) { error = "빈 조건 항목"; return false; }
                Term term;
                if (!TryParseTerm(t, out term, out error)) return false;
                terms.Add(term);
            }
            condition = new AiCondition(source.Trim(), terms.ToArray());
            return true;
        }

        private static readonly string[] OpTokens = { "==", "!=", "<=", ">=", "<", ">" };

        private static bool TryParseTerm(string t, out Term term, out string error)
        {
            term = new Term();
            error = null;

            // 함수형: name(args) [==true|==false]
            int paren = t.IndexOf('(');
            if (paren > 0)
            {
                int close = t.IndexOf(')', paren);
                if (close < 0) { error = "닫는 괄호 없음: " + t; return false; }
                string name = t.Substring(0, paren).Trim();
                string args = t.Substring(paren + 1, close - paren - 1);
                string rest = t.Substring(close + 1).Trim();
                string[] parts = args.Split(',');
                if (name == "action_used_recent")
                {
                    if (parts.Length != 2) { error = "action_used_recent(id,N) 인자 오류: " + t; return false; }
                    int n;
                    if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n <= 0)
                    { error = "N 은 양의 정수여야 함: " + t; return false; }
                    term.Var = Var.FnActionUsedRecent;
                    term.FnArgId = parts[0].Trim();
                    term.FnArgN = n;
                }
                else if (name == "on_cooldown")
                {
                    if (parts.Length != 1) { error = "on_cooldown(id) 인자 오류: " + t; return false; }
                    term.Var = Var.FnOnCooldown;
                    term.FnArgId = parts[0].Trim();
                }
                else { error = "알 수 없는 함수: " + name; return false; }

                term.Op = Op.Eq;
                term.Bool = true;
                if (rest.Length > 0)
                {
                    if (rest == "==true") term.Bool = true;
                    else if (rest == "==false") term.Bool = false;
                    else if (rest == "!=true") term.Bool = false;
                    else if (rest == "!=false") term.Bool = true;
                    else { error = "함수 비교식 오류: " + t; return false; }
                }
                return true;
            }

            int opIdx = -1;
            string opTok = null;
            foreach (string o in OpTokens)
            {
                int i = t.IndexOf(o, StringComparison.Ordinal);
                if (i > 0) { opIdx = i; opTok = o; break; }
            }
            if (opIdx < 0) { error = "비교 연산자 없음: " + t; return false; }
            string lhs = t.Substring(0, opIdx).Trim();
            string rhs = t.Substring(opIdx + opTok.Length).Trim();
            switch (opTok)
            {
                case "==": term.Op = Op.Eq; break;
                case "!=": term.Op = Op.Ne; break;
                case "<=": term.Op = Op.Le; break;
                case ">=": term.Op = Op.Ge; break;
                case "<": term.Op = Op.Lt; break;
                default: term.Op = Op.Gt; break;
            }

            switch (lhs)
            {
                case "dist":
                    term.Var = Var.Dist;
                    if (rhs != "melee" && rhs != "mid" && rhs != "far") { error = "dist 값은 melee/mid/far: " + t; return false; }
                    return RequireEquality(term, rhs, t, out error);
                case "target_state":
                    term.Var = Var.TargetState;
                    TargetState ts;
                    if (!Enum.TryParse(rhs, false, out ts)) { error = "target_state 값 오류: " + t; return false; }
                    return RequireEquality(term, rhs, t, out error);
                case "last_action":
                    term.Var = Var.LastAction;
                    return RequireEquality(term, rhs, t, out error);
                case "dist_m": term.Var = Var.DistM; return ParseNumber(term, rhs, t, out error);
                case "hp_pct": term.Var = Var.HpPct; return ParseNumber(term, rhs, t, out error);
                case "stamina_pct": term.Var = Var.StaminaPct; return ParseNumber(term, rhs, t, out error);
                case "phase": term.Var = Var.Phase; return ParseNumber(term, rhs, t, out error);
                case "perceived": term.Var = Var.Perceived; return ParseBool(term, rhs, t, out error);
                case "poise_broken": term.Var = Var.PoiseBroken; return ParseBool(term, rhs, t, out error);
                case "target_heal_recover": term.Var = Var.TargetHealRecover; return ParseBool(term, rhs, t, out error);
                case "target_attack_recover": term.Var = Var.TargetAttackRecover; return ParseBool(term, rhs, t, out error);
                case "incoming_attack": term.Var = Var.IncomingAttack; return ParseBool(term, rhs, t, out error);
                default:
                    error = "알 수 없는 변수: " + lhs;
                    return false;
            }
        }

        private static bool RequireEquality(Term term, string rhs, string t, out string error)
        {
            error = null;
            if (term.Op != Op.Eq && term.Op != Op.Ne) { error = "문자열 비교는 == / != 만 허용: " + t; return false; }
            term.Text = rhs;
            return true;
        }

        private static bool ParseNumber(Term term, string rhs, string t, out string error)
        {
            error = null;
            double v;
            if (!double.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { error = "숫자 값 오류: " + t; return false; }
            term.Number = v;
            return true;
        }

        private static bool ParseBool(Term term, string rhs, string t, out string error)
        {
            error = null;
            if (term.Op != Op.Eq && term.Op != Op.Ne) { error = "bool 비교는 == / != 만 허용: " + t; return false; }
            if (rhs == "true") term.Bool = true;
            else if (rhs == "false") term.Bool = false;
            else { error = "bool 값 오류: " + t; return false; }
            return true;
        }

        public bool Evaluate(IAiContext ctx)
        {
            for (int i = 0; i < _terms.Length; i++)
            {
                if (!EvalTerm(_terms[i], ctx)) return false;
            }
            return true;
        }

        private static bool EvalTerm(Term t, IAiContext c)
        {
            switch (t.Var)
            {
                case Var.Dist: return CmpText(t, c.DistBand);
                case Var.TargetState: return CmpText(t, c.TargetStateName);
                case Var.LastAction: return CmpText(t, c.LastAction ?? string.Empty);
                case Var.DistM: return CmpNum(t, c.DistM);
                case Var.HpPct: return CmpNum(t, c.HpPct);
                case Var.StaminaPct: return CmpNum(t, c.StaminaPct);
                case Var.Phase: return CmpNum(t, c.Phase);
                case Var.Perceived: return CmpBool(t, c.Perceived);
                case Var.PoiseBroken: return CmpBool(t, c.PoiseBroken);
                case Var.TargetHealRecover: return CmpBool(t, c.TargetHealRecover);
                case Var.TargetAttackRecover: return CmpBool(t, c.TargetAttackRecover);
                case Var.IncomingAttack: return CmpBool(t, c.IncomingAttack);
                case Var.FnActionUsedRecent: return c.ActionUsedRecent(t.FnArgId, t.FnArgN) == t.Bool;
                case Var.FnOnCooldown: return c.OnCooldown(t.FnArgId) == t.Bool;
                default: return false;
            }
        }

        private static bool CmpText(Term t, string v)
        {
            bool eq = string.Equals(v, t.Text, StringComparison.Ordinal);
            return t.Op == Op.Eq ? eq : !eq;
        }

        private static bool CmpBool(Term t, bool v)
        {
            return t.Op == Op.Eq ? v == t.Bool : v != t.Bool;
        }

        private static bool CmpNum(Term t, double v)
        {
            switch (t.Op)
            {
                case Op.Eq: return Math.Abs(v - t.Number) < 1e-9;
                case Op.Ne: return Math.Abs(v - t.Number) >= 1e-9;
                case Op.Lt: return v < t.Number;
                case Op.Le: return v <= t.Number;
                case Op.Gt: return v > t.Number;
                case Op.Ge: return v >= t.Number;
                default: return false;
            }
        }
    }
}
