using ARPG.Core;
using ARPG.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.Runtime
{
    /// <summary>
    /// 캡슐·기본 도형 표시. 코어 상태를 읽기만 하며 Transform 을 코어에 되돌리지 않는다.
    /// 모션의 기준은 코어의 행동 단계와 진행률이다(AnimationEvent 로 피해를 만들지 않음).
    /// </summary>
    public sealed class CombatantView : MonoBehaviour
    {
        public int EntityId;
        private Transform _body, _weaponPivot, _shield, _ring, _stunRing, _nose;
        private Renderer _bodyR, _weaponR, _shieldR, _ringR, _stunR, _noseR;
        private Color _teamColor;
        private float _hitFlashUntil;
        private float _guardFlashUntil;
        private ResourceBarsView _bars;
        private Vector3 _weaponPivotBase;
        private Vector3 _bodyBaseScale;

        // 들고 있는 무기 모양: 공격 스킬 실행 중에는 스킬의 첫 Damage_Type 모양(AttackLooks), 그 외에는 회색 막대
        private Transform _weapon, _weaponHead, _weaponOrb;
        private Renderer _weaponHeadR, _weaponOrbR;
        private MeshFilter _weaponHeadMf;
        private bool _weaponLookSet;
        private AttackShape _weaponShape;
        private Color _weaponColor;
        private static readonly Color WeaponBaseColor = new Color(0.85f, 0.85f, 0.9f);
        private const float WeaponLength = 1.1f;

        // 화면 보간용 행동 진행 시간(직전 tick 값 → 현재 tick 값 사이를 alpha 로 보간)
        private int _animInstance = -1;
        private long _animTick = long.MinValue;
        private double _animPrevMs, _animCurMs;

        /// <summary>1 tick 길이(ms). SimulationBootstrap 이 rules 값으로 설정한다.</summary>
        public static float TickMs = 1000f / 30f;
        public Collider PickCollider { get; private set; }
        public float Radius { get; private set; }

        /// <summary>공격 연출 막대의 출발점(무기 높이, 몸 앞).</summary>
        public Vector3 MuzzlePoint { get { return transform.TransformPoint(new Vector3(0, 1.2f, Radius + 0.2f)); } }

        /// <summary>공격 연출 막대의 도착점(몸통 중심).</summary>
        public Vector3 CenterPoint { get { return transform.position + new Vector3(0, 1.0f, 0); } }

        // 상태 색(몸 색과 머리 위 상태 글자에 같이 쓴다). 아군 팀색이 파랑이라 빙결은 밝은 얼음색 파랑으로 구분한다.
        public static readonly Color StunTint = new Color(1f, 0.85f, 0.15f);    // 기절: 노랑
        public static readonly Color StaggerTint = new Color(1f, 0.6f, 0.1f);   // 경직: 주황
        public static readonly Color FrostTint = new Color(0.6f, 0.88f, 1f);    // 빙결: 얼음 파랑
        public static readonly Color PoisonTint = new Color(0.72f, 0.35f, 0.95f); // 중독: 보라
        public static readonly Color WoundTint = new Color(1f, 0.32f, 0.32f);   // 상처: 빨강(즉시 피해라 1초만 글자 표시)

        /// <summary>상처 발동 글자를 이 tick 전까지 표시(ViewEventSink 가 설정).</summary>
        public long WoundTagUntilTick = -1;

        public static readonly Color AllyColor = new Color(0.25f, 0.55f, 1f);
        public static readonly Color EnemyColor = new Color(1f, 0.35f, 0.3f);
        public static readonly Color[] ExtraTeamColors = { new Color(0.3f, 0.85f, 0.4f), new Color(0.9f, 0.75f, 0.2f) };

        public static Color TeamColor(int team)
        {
            if (team == 0) return AllyColor;
            if (team == 1) return EnemyColor;
            return ExtraTeamColors[(team - 2) % ExtraTeamColors.Length];
        }

        public static CombatantView Create(Combatant c, Transform parent)
        {
            var root = new GameObject("CV_" + c.Label + "_" + c.Def.DisplayName);
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<CombatantView>();
            v.EntityId = c.EntityId;
            v._teamColor = TeamColor(c.TeamId);
            float r = (float)c.Radius;
            v.Radius = r;

            v._body = Prim(PrimitiveType.Capsule, root.transform, "Body", new Vector3(0, 0.9f, 0), new Vector3(r * 2f, 0.9f, r * 2f), true);
            v._bodyR = v._body.GetComponent<Renderer>();
            v._bodyBaseScale = v._body.localScale;
            v.PickCollider = v._body.GetComponent<Collider>();

            v._nose = Prim(PrimitiveType.Cube, v._body, "FacingMark", new Vector3(0, 0.45f, 0.55f), new Vector3(0.25f, 0.12f, 0.3f), false);
            v._noseR = v._nose.GetComponent<Renderer>();

            var pivot = new GameObject("WeaponPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(r + 0.1f, 1.2f, 0);
            v._weaponPivot = pivot;
            v._weaponPivotBase = pivot.localPosition;
            var weapon = Prim(PrimitiveType.Cube, pivot, "Weapon", new Vector3(0, 0, WeaponLength * 0.5f), new Vector3(0.07f, 0.07f, WeaponLength), false);
            v._weapon = weapon;
            v._weaponR = weapon.GetComponent<Renderer>();
            var head = AttackLooks.MakeMeshObject(AttackLooks.ArrowHeadMesh, pivot, "WeaponHead");
            v._weaponHead = head.transform;
            v._weaponHeadR = head.GetComponent<Renderer>();
            v._weaponHeadMf = head.GetComponent<MeshFilter>();
            v._weaponOrb = Prim(PrimitiveType.Sphere, pivot, "WeaponOrb", new Vector3(0, 0, 0.75f), new Vector3(0.34f, 0.34f, 0.34f), false);
            v._weaponOrbR = v._weaponOrb.GetComponent<Renderer>();

            v._shield = Prim(PrimitiveType.Cube, root.transform, "Shield", new Vector3(-r * 0.6f, 1.1f, r + 0.15f), new Vector3(0.7f, 0.8f, 0.07f), false);
            v._shieldR = v._shield.GetComponent<Renderer>();

            v._ring = Prim(PrimitiveType.Cylinder, root.transform, "TeamRing", new Vector3(0, 0.02f, 0), new Vector3(r * 3f, 0.01f, r * 3f), false);
            v._ringR = v._ring.GetComponent<Renderer>();

            v._stunRing = Prim(PrimitiveType.Cylinder, root.transform, "StunRing", new Vector3(0, 2.1f, 0), new Vector3(0.6f, 0.02f, 0.6f), false);
            v._stunR = v._stunRing.GetComponent<Renderer>();

            v.SetWeaponLook(AttackLook.Default);
            SetColor(v._shieldR, new Color(0.3f, 0.9f, 0.9f));
            SetColor(v._stunR, new Color(1f, 0.9f, 0.1f));
            SetColor(v._noseR, Color.Lerp(v._teamColor, Color.white, 0.5f));

            v._bars = ResourceBarsView.Create(root.transform, new Vector3(0, 2.35f, 0));
            return v;
        }

        private static Transform Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go.transform;
        }

        public static void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>
        /// 들고 있는 무기 모양 바꾸기(바뀔 때만 적용). 막대(기본·PHYSICAL·SLASH), 막대+삼각뿔 촉(RANGED·THRUST),
        /// 막대+끝 사면체(STRIKE), 색 구체(MAGIC·FIRE·WATER·HOLY), 노란 막대(LIGHTNING).
        /// </summary>
        private void SetWeaponLook(AttackLook look)
        {
            Color col = look.HasColor ? look.Color : WeaponBaseColor;
            if (_weaponLookSet && look.Shape == _weaponShape && col == _weaponColor) return;
            _weaponLookSet = true;
            _weaponShape = look.Shape;
            _weaponColor = col;

            bool orb = look.Shape == AttackShape.Orb;
            bool arrow = look.Shape == AttackShape.Arrow;
            bool hammer = look.Shape == AttackShape.Hammer;
            _weapon.gameObject.SetActive(!orb);
            _weaponOrb.gameObject.SetActive(orb);
            _weaponHead.gameObject.SetActive(arrow || hammer);
            float th = look.Shape == AttackShape.Bolt ? 0.1f : 0.07f;
            _weapon.localScale = new Vector3(th, th, WeaponLength);
            if (arrow)
            {
                // 삼각뿔 촉: 밑면이 막대 끝에 살짝 겹치고 꼭짓점이 앞
                _weaponHeadMf.sharedMesh = AttackLooks.ArrowHeadMesh;
                _weaponHead.localRotation = Quaternion.identity;
                _weaponHead.localScale = new Vector3(0.17f, 0.17f, 0.26f);
                _weaponHead.localPosition = new Vector3(0, 0, WeaponLength + 0.13f - 0.04f);
            }
            else if (hammer)
            {
                // 사면체 머리: 막대 끝에 가로(휘두르는 방향)로 붙인다
                _weaponHeadMf.sharedMesh = AttackLooks.TetraMesh;
                _weaponHead.localRotation = Quaternion.identity;
                _weaponHead.localScale = new Vector3(0.42f, 0.28f, 0.28f);
                _weaponHead.localPosition = new Vector3(0, 0, WeaponLength - 0.04f);
            }
            SetColor(_weaponR, col);
            SetColor(_weaponHeadR, col);
            SetColor(_weaponOrbR, col);
        }

        public void FlashHit() { _hitFlashUntil = Time.unscaledTime + 0.12f; }
        public void FlashGuard() { _guardFlashUntil = Time.unscaledTime + 0.15f; }

        private static Vector3 ToV3(Vec2 p) { return new Vector3((float)p.x, 0, (float)p.y); }

        public void Apply(Combatant c, float alpha, long tick, bool selected, Camera cam)
        {
            // 위치·방향: 최근 두 상태 사이 보간(화면 전용)
            Vector3 pos = Vector3.Lerp(ToV3(c.PrevPos), ToV3(c.Pos), alpha);
            Vector3 f0 = ToV3(c.PrevFacing), f1 = ToV3(c.Facing);
            Vector3 fwd = Vector3.Slerp(f0.sqrMagnitude > 1e-6f ? f0 : f1, f1, alpha);
            transform.position = pos;
            if (fwd.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            bool dead = c.Dead;
            bool staggered = c.IsStaggered(tick);
            bool stunned = c.IsStunned(tick);
            var a = c.Action;

            // 행동 진행 시간(화면 보간). 몸 동작·무기·구르기가 tick 단위로 끊기지 않게 한다
            double e = DisplayElapsed(a, tick, alpha);

            // 몸 기울기 (+X = 앞으로 숙임). 공격: 선딜 뒤로 젖힘 → 타격 직전 앞으로 숙이며 내딛기 → 후딜 복귀
            float lean = 0f, lunge = 0f;
            bool attacking = !dead && !staggered && !stunned && AttackPose(a, e, out lean, out lunge);
            bool rolling = !dead && !staggered && !stunned && a != null && a.Kind == ActionKind.Dodge;
            if (rolling)
            {
                ApplyRoll(c, a, e);
            }
            else
            {
                Quaternion bodyRot = Quaternion.identity;
                if (dead) bodyRot = Quaternion.Euler(-90f, 0, 0);
                else if (staggered) bodyRot = Quaternion.Euler(-18f, 0, 0);
                else if (stunned) bodyRot = Quaternion.Euler(-10f, 0, 8f);
                else if (attacking) bodyRot = Quaternion.Euler(lean, 0, 0);
                _body.localRotation = bodyRot;
                _body.localScale = _bodyBaseScale;
                _body.localPosition = dead ? new Vector3(0, 0.4f, -0.5f) : new Vector3(0, 0.9f, attacking ? lunge : 0f);
            }

            // 무기 기준점도 몸을 따라 숙이고 내딛는다(몸 중심 0.9m 기준 회전)
            if (attacking)
            {
                float rad = lean * Mathf.Deg2Rad;
                float h = _weaponPivotBase.y - 0.9f;
                _weaponPivot.localPosition = new Vector3(_weaponPivotBase.x, 0.9f + h * Mathf.Cos(rad), _weaponPivotBase.z + lunge + h * Mathf.Sin(rad));
            }
            else _weaponPivot.localPosition = _weaponPivotBase;

            // 무기: 선딜(뒤로) → 타격(휘두름) → 후딜(복귀). 공격 스킬 중에는 Damage_Type 모양으로 바뀐다
            float yaw = 0, pitch = 10;
            AttackLook weaponLook = AttackLook.Default;
            if (!dead && a != null && a.Kind == ActionKind.Skill && a.Skill != null && a.Skill.IsAttack && a.Skill.HitTimesMs.Length > 0)
            {
                weaponLook = AttackLooks.For(a.Skill);
                var hits = a.Skill.HitTimesMs;
                double first = hits[0], last = hits[hits.Length - 1];
                if (e < first)
                {
                    float t = first > 0 ? Mathf.Clamp01((float)(e / first)) : 1;
                    yaw = -75f * t;
                    pitch = 10 - 40 * t;
                }
                else if (e < last + 120)
                {
                    int idx = 0;
                    for (int i = 0; i < hits.Length; i++) if (e >= hits[i]) idx = i;
                    float t = Mathf.Clamp01((float)((e - hits[idx]) / 120.0));
                    float dir = (idx % 2 == 0) ? 1 : -1;
                    yaw = Mathf.Lerp(-75f * dir, 75f * dir, t);
                    pitch = 20;
                }
                else
                {
                    double rest = a.DurationMs - last - 120;
                    float t = rest > 0 ? Mathf.Clamp01((float)((e - last - 120) / rest)) : 1;
                    yaw = Mathf.Lerp(75f * ((hits.Length - 1) % 2 == 0 ? 1 : -1), 0, t);
                    pitch = 20 - 10 * t;
                }
            }
            _weaponPivot.localRotation = Quaternion.Euler(pitch, yaw, 0);
            SetWeaponLook(weaponLook);
            _weaponPivot.gameObject.SetActive(!dead && !rolling);

            bool guarding = !dead && a != null && a.Kind == ActionKind.Block;
            _shield.gameObject.SetActive(guarding);
            if (guarding) SetColor(_shieldR, Time.unscaledTime < _guardFlashUntil ? Color.white : new Color(0.3f, 0.9f, 0.9f));

            _stunRing.gameObject.SetActive(!dead && stunned);
            if (stunned) _stunRing.Rotate(0, 360f * Time.unscaledDeltaTime, 0, Space.Self);

            // 색: 사망 회색, 피격 점멸 흰색, 회피 무적 청록, 기절 노랑, 경직 주황(팀색 섞음), 빙결 얼음 파랑, 중독 보라
            // 상태이상(기절·빙결·중독)은 몸 전체를 상태 색으로 칠한다. 팀은 바닥 고리 색으로 구별.
            Color col = _teamColor;
            if (dead) col = new Color(0.35f, 0.35f, 0.35f);
            else if (Time.unscaledTime < _hitFlashUntil) col = Color.white;
            else if (c.InIFrame) col = new Color(0.4f, 1f, 1f);
            else if (stunned) col = StunTint;
            else if (staggered) col = Color.Lerp(_teamColor, StaggerTint, 0.6f);
            else if (c.FrostActive(tick)) col = FrostTint;
            else if (c.PoisonActive) col = PoisonTint;
            SetColor(_bodyR, col);
            SetColor(_ringR, selected ? new Color(1f, 0.95f, 0.3f) : Color.Lerp(_teamColor, Color.black, dead ? 0.7f : 0.3f));
            _ring.localScale = selected ? new Vector3((float)c.Radius * 3.6f, 0.01f, (float)c.Radius * 3.6f)
                                        : new Vector3((float)c.Radius * 3f, 0.01f, (float)c.Radius * 3f);

            _bars.gameObject.SetActive(!dead);
            if (!dead) _bars.Apply(c, cam);
        }

        // 공격 자세 상수(도 / m / ms)
        private const float WindupLean = -8f;
        private const float StrikeLean = 22f;
        private const float HitPulseLean = 6f;
        private const float WindupPull = -0.05f;
        private const float StrikeLunge = 0.18f;
        private const float HitPulseLunge = 0.04f;
        private const double AnticipationMs = 100.0;
        private const double HitHoldMs = 120.0;

        /// <summary>
        /// 공격 행동의 진행 시간으로 몸 기울기·내딛기 계산(화면 전용).
        /// 선딜: 뒤로 젖힘 → 첫 타격 직전 100ms: 앞으로 숙이며 내딛기 → 타격 구간: 숙인 채 타격마다 살짝 더 숙임 → 후딜: 원위치.
        /// </summary>
        private static bool AttackPose(ActionInstance a, double e, out float lean, out float lunge)
        {
            lean = 0f;
            lunge = 0f;
            if (a == null || a.Kind != ActionKind.Skill || a.Skill == null || !a.Skill.IsAttack) return false;
            var hits = a.Skill.HitTimesMs;
            if (hits == null || hits.Length == 0) return false;
            double first = hits[0], last = hits[hits.Length - 1];
            double ant = System.Math.Min(AnticipationMs, first);
            double windEnd = first - ant;

            if (e < windEnd)
            {
                float t = Smooth((float)(e / windEnd));
                lean = WindupLean * t;
                lunge = WindupPull * t;
            }
            else if (e < first)
            {
                float s = ant > 0 ? Mathf.Clamp01((float)((e - windEnd) / ant)) : 1f;
                float k = 1f - (1f - s) * (1f - s); // ease-out
                lean = Mathf.Lerp(WindupLean, StrikeLean, k);
                lunge = Mathf.Lerp(WindupPull, StrikeLunge, k);
            }
            else if (e < last + HitHoldMs)
            {
                int idx = 0;
                for (int i = 0; i < hits.Length; i++) if (e >= hits[i]) idx = i;
                float p = 1f - Mathf.Clamp01((float)((e - hits[idx]) / HitHoldMs));
                lean = StrikeLean + HitPulseLean * p;
                lunge = StrikeLunge + HitPulseLunge * p;
            }
            else
            {
                double rest = a.DurationMs - last - HitHoldMs;
                float t = rest > 0 ? Smooth((float)((e - last - HitHoldMs) / rest)) : 1f;
                lean = Mathf.Lerp(StrikeLean, 0f, t);
                lunge = Mathf.Lerp(StrikeLunge, 0f, t);
            }
            return true;
        }

        /// <summary>
        /// 화면에 보일 행동 진행 시간(ms). 코어는 tick 마다 값이 바뀌므로 직전 tick 값과 현재 값을 alpha 로 보간한다.
        /// 행동이 바뀌면(InstanceId) 한 tick 전 값부터 시작한다. 정지 중에는 alpha=1 이라 현재 값.
        /// </summary>
        private double DisplayElapsed(ActionInstance a, long tick, float alpha)
        {
            if (a == null)
            {
                _animInstance = -1;
                return 0;
            }
            if (a.InstanceId != _animInstance)
            {
                _animInstance = a.InstanceId;
                _animTick = tick;
                _animPrevMs = System.Math.Max(0.0, a.ElapsedMs - TickMs);
                _animCurMs = a.ElapsedMs;
            }
            else if (tick != _animTick)
            {
                _animTick = tick;
                _animPrevMs = _animCurMs;
                _animCurMs = a.ElapsedMs;
            }
            return _animPrevMs + (_animCurMs - _animPrevMs) * Mathf.Clamp01(alpha);
        }

        // 구르기 상수
        private const float RollTuck = 0.3f;   // 구르는 중간에 몸을 웅크리는 정도(높이 30% 감소)

        /// <summary>
        /// 구르기(Dodge): 진행률에 따라 몸이 X축(좌우 축)으로 한 바퀴(360°) 돈다.
        /// 회피 방향이 정면 쪽이면 앞구르기, 뒤쪽이면 뒷구르기. 도는 동안 몸 아래 끝이 바닥에 닿도록 높이를 맞추고 살짝 웅크린다.
        /// </summary>
        private void ApplyRoll(Combatant c, ActionInstance a, double e)
        {
            float p = a.DurationMs > 0 ? Mathf.Clamp01((float)(e / a.DurationMs)) : 1f;
            float sign = Vec2.Dot(a.FixedDir, c.Facing) >= 0 ? 1f : -1f;
            float ang = 360f * Smooth(p) * sign;
            float tuck = Mathf.Sin(p * Mathf.PI);
            float sy = _bodyBaseScale.y * (1f - RollTuck * tuck);
            float sx = _bodyBaseScale.x;
            _body.localScale = new Vector3(sx, sy, _bodyBaseScale.z);
            _body.localRotation = Quaternion.Euler(ang, 0, 0);
            _body.localPosition = new Vector3(0, GroundedCenterHeight(ang, sy, _bodyBaseScale.z), 0);
        }

        /// <summary>
        /// 캡슐(높이 2·반지름 0.5 원형을 (x, sy, sz) 로 늘린 모양)을 X축으로 ang 만큼 돌렸을 때
        /// 가장 낮은 점이 바닥(0)에 닿도록 하는 중심 높이.
        /// </summary>
        private static float GroundedCenterHeight(float angDeg, float sy, float sz)
        {
            float rad = angDeg * Mathf.Deg2Rad;
            float cy = Mathf.Abs(Mathf.Cos(rad)), sn = Mathf.Abs(Mathf.Sin(rad));
            float half = 0.5f * sy;     // 원기둥 부분 절반 길이
            float capY = 0.5f * sy;     // 끝 반구의 세로 반지름
            float capZ = 0.5f * sz;     // 끝 반구의 앞뒤 반지름
            return half * cy + Mathf.Sqrt(capY * cy * capY * cy + capZ * sn * capZ * sn);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
