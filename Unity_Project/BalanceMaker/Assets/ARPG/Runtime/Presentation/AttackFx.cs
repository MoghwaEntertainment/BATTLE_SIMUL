using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Runtime
{
    public enum AttackFxKind
    {
        Hit = 0,      // 명중(HIT/HIT_GROGGY)
        Guard = 1,    // 가드됨
        Avoided = 2,  // 회피 무적으로 무효
        Miss = 3,     // 판정 범위에 대상 없음
    }

    /// <summary>
    /// 공격 연출(화면 전용): 공격자 → 대상으로 날아가는 막대(작대기)와 착탄 구체.
    /// 코어에서 타격이 이미 확정된 뒤(HitResolved/Guarded/HitAvoided 이벤트) 생성되므로 판정에는 관여하지 않는다.
    /// 시계는 '화면에 표시 중인 전투 시간(tick + 보간)'이라 일시정지 시 멈추고, 배속을 따르며, 1 tick 진행에도 맞춰 움직인다.
    /// 막대가 대상에 닿는 순간 대상 점멸(피격 흰색/가드 방패 흰색)을 일으킨다.
    /// </summary>
    public sealed class AttackFx
    {
        private sealed class Item
        {
            public GameObject Root;
            public Transform Stick, Impact;
            public Renderer StickR, ImpactR;
            public bool Active;
            public AttackFxKind Kind;
            public CombatantView Attacker, Target;
            public Vector3 Start, FixedEnd;
            public bool UseTarget;
            public double LaunchTick;
            public double FlightTicks;
            public bool Arrived;
            public float Length;
        }

        // 연출 상수(전투 시간 기준 초)
        private const float SpeedMps = 30f;
        private const float MinFlightSec = 0.10f;
        private const float MaxFlightSec = 0.35f;
        private const float LingerSec = 0.18f;   // 착탄 후 막대가 박혀 있다가 줄어드는 시간
        private const float ImpactSec = 0.22f;   // 착탄 구체가 커졌다 사라지는 시간
        private const float StickThickness = 0.07f;

        public bool Enabled = true;
        public int MaxActive = 256;

        private readonly Transform _root;
        private readonly List<Item> _items = new List<Item>();
        private double _tickSec = 1.0 / 30.0;
        private double _clock = double.NegativeInfinity;

        public AttackFx(Transform root)
        {
            _root = root;
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _items.Count; i++) if (_items[i].Active) n++;
                return n;
            }
        }

        public void SetTickSec(double tickSec)
        {
            if (tickSec > 0) _tickSec = tickSec;
        }

        /// <summary>모든 연출 제거(회차 전환·표시 객체 재생성 시).</summary>
        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++) Deactivate(_items[i]);
            _clock = double.NegativeInfinity;
        }

        /// <summary>
        /// 연출 생성. 꺼져 있거나 생성할 수 없으면 false 를 돌려주며, 호출자는 즉시 점멸로 대신한다.
        /// eventTick: 이벤트가 발생한 tick. 직전 상태(eventTick-1)가 표시되는 시점에 발사한다.
        /// </summary>
        public bool Spawn(AttackFxKind kind, CombatantView attacker, CombatantView target, long eventTick, double distanceHint)
        {
            if (!Enabled || _root == null || attacker == null) return false;
            if (kind != AttackFxKind.Miss && target == null) return false;
            var it = Acquire();
            if (it == null) return false;

            it.Kind = kind;
            it.Attacker = attacker;
            it.Target = target;
            it.Start = attacker.MuzzlePoint;
            float dist;
            if (kind != AttackFxKind.Miss)
            {
                it.UseTarget = true;
                dist = Vector3.Distance(it.Start, target.CenterPoint);
            }
            else
            {
                // 빗나감: 공격자 정면으로 대상까지의 거리(없으면 2m)만큼 날아가 사라진다
                float len = (double.IsNaN(distanceHint) || distanceHint <= 0) ? 2f : Mathf.Clamp((float)distanceHint, 1f, 12f);
                it.UseTarget = false;
                it.FixedEnd = it.Start + attacker.transform.forward * len;
                dist = len;
            }
            float flightSec = Mathf.Clamp(dist / SpeedMps, MinFlightSec, MaxFlightSec);
            it.FlightTicks = flightSec / _tickSec;
            it.LaunchTick = eventTick - 1;
            it.Arrived = false;
            it.Length = Mathf.Clamp(dist * 0.3f, 0.45f, 1.0f);

            Color stick, impact;
            KindColors(kind, out stick, out impact);
            CombatantView.SetColor(it.StickR, stick);
            CombatantView.SetColor(it.ImpactR, impact);

            it.Active = true;
            it.Root.SetActive(true);
            it.Impact.gameObject.SetActive(false);
            it.Stick.gameObject.SetActive(true);
            PlaceStick(it, it.Start, it.UseTarget ? target.CenterPoint : it.FixedEnd, 0f, 1f);
            return true;
        }

        /// <summary>표시 중인 전투 시간(tick 단위, 보간 포함)으로 진행. 시간이 뒤로 가면 무시한다.</summary>
        public void Advance(double displayTick)
        {
            if (displayTick > _clock) _clock = displayTick;
            Update();
        }

        /// <summary>전투 종료 후 남은 연출을 마저 진행할 때 사용(tick 단위 증가량).</summary>
        public void AdvanceBy(double deltaTicks)
        {
            if (deltaTicks > 0 && !double.IsNegativeInfinity(_clock)) _clock += deltaTicks;
            Update();
        }

        private void Update()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (!it.Active) continue;
                if (it.Attacker == null || (it.UseTarget && it.Target == null))
                {
                    Deactivate(it);
                    continue;
                }
                double elapsed = _clock - it.LaunchTick;
                if (elapsed < 0) elapsed = 0;
                Vector3 end = it.UseTarget ? it.Target.CenterPoint : it.FixedEnd;

                if (elapsed <= it.FlightTicks)
                {
                    float t = it.FlightTicks > 0 ? (float)(elapsed / it.FlightTicks) : 1f;
                    PlaceStick(it, it.Start, end, t, 1f);
                    continue;
                }

                if (!it.Arrived)
                {
                    it.Arrived = true;
                    if (it.Kind == AttackFxKind.Hit) it.Target.FlashHit();
                    else if (it.Kind == AttackFxKind.Guard) it.Target.FlashGuard();
                }

                float post = (float)((elapsed - it.FlightTicks) * _tickSec);
                if (post >= Mathf.Max(LingerSec, ImpactSec))
                {
                    Deactivate(it);
                    continue;
                }

                // 막대: 대상에 박힌 채 줄어듦(빗나감은 끝점에서 줄어듦)
                float shrink = 1f - Mathf.Clamp01(post / LingerSec);
                it.Stick.gameObject.SetActive(shrink > 0.01f);
                if (shrink > 0.01f) PlaceStick(it, it.Start, end, 1f, shrink);

                // 착탄 구체: 커졌다가 마지막 30% 구간에서 줄어듦
                float k = Mathf.Clamp01(post / ImpactSec);
                bool showImpact = k < 1f;
                it.Impact.gameObject.SetActive(showImpact);
                if (showImpact)
                {
                    float size = (it.Kind == AttackFxKind.Miss ? 0.35f : 0.5f) + 0.6f * k;
                    if (k > 0.7f) size *= (1f - k) / 0.3f;
                    it.Impact.position = end;
                    it.Impact.localScale = new Vector3(size, size, size);
                }
            }
        }

        private static void PlaceStick(Item it, Vector3 start, Vector3 end, float t, float scale)
        {
            Vector3 dir = end - start;
            if (dir.sqrMagnitude < 1e-6f) dir = it.Attacker != null ? it.Attacker.transform.forward : Vector3.forward;
            dir.Normalize();
            // 막대 끝(앞쪽)이 경로 위 t 지점에 오도록 배치
            Vector3 tip = Vector3.Lerp(start, end, t);
            float len = it.Length * scale;
            it.Stick.position = tip - dir * (len * 0.5f);
            it.Stick.rotation = Quaternion.LookRotation(dir, Vector3.up);
            float th = StickThickness * Mathf.Max(0.4f, scale);
            it.Stick.localScale = new Vector3(th, th, len);
        }

        private static void KindColors(AttackFxKind kind, out Color stick, out Color impact)
        {
            switch (kind)
            {
                case AttackFxKind.Guard:
                    stick = new Color(0.5f, 1f, 1f);
                    impact = new Color(0.35f, 0.95f, 1f);
                    break;
                case AttackFxKind.Avoided:
                    stick = new Color(0.8f, 0.95f, 1f);
                    impact = new Color(0.75f, 0.9f, 1f);
                    break;
                case AttackFxKind.Miss:
                    stick = new Color(0.6f, 0.6f, 0.6f);
                    impact = new Color(0.5f, 0.5f, 0.5f);
                    break;
                default:
                    stick = new Color(1f, 0.95f, 0.55f);
                    impact = new Color(1f, 0.7f, 0.15f);
                    break;
            }
        }

        private Item Acquire()
        {
            int active = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Active) return _items[i];
                active++;
            }
            if (active >= MaxActive) return null;
            var it = new Item();
            it.Root = new GameObject("AttackFx_" + _items.Count);
            it.Root.transform.SetParent(_root, false);
            it.Stick = MakePrim(PrimitiveType.Cube, it.Root.transform, "Stick");
            it.Impact = MakePrim(PrimitiveType.Sphere, it.Root.transform, "Impact");
            it.StickR = it.Stick.GetComponent<Renderer>();
            it.ImpactR = it.Impact.GetComponent<Renderer>();
            DisableShadows(it.StickR);
            DisableShadows(it.ImpactR);
            it.Root.SetActive(false);
            _items.Add(it);
            return it;
        }

        private static Transform MakePrim(PrimitiveType type, Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col); // 선택 레이캐스트에 걸리지 않도록
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void DisableShadows(Renderer r)
        {
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static void Deactivate(Item it)
        {
            it.Active = false;
            it.Attacker = null;
            it.Target = null;
            if (it.Root != null) it.Root.SetActive(false);
        }
    }
}
