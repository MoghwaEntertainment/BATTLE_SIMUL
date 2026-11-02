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
    /// 공격 연출(화면 전용): 공격자 → 대상으로 날아가는 투사체와 착탄 구체.
    /// 투사체 모양은 스킬의 첫 Damage_Type 으로 정한다(AttackLooks): 막대(PHYSICAL·SLASH), 화살(RANGED·THRUST),
    /// 망치(STRIKE), 색 구체(MAGIC 하늘·FIRE 빨강·WATER 파랑·HOLY 노랑), 노란 막대(LIGHTNING).
    /// 막대·화살·망치는 기존처럼 결과별 색(명중·가드·회피·빗나감), 구체·노란 막대는 속성 색(빗나감은 회색을 섞음).
    /// 코어에서 타격이 이미 확정된 뒤(HitResolved/Guarded/HitAvoided 이벤트) 생성되므로 판정에는 관여하지 않는다.
    /// 시계는 '화면에 표시 중인 전투 시간(tick + 보간)'이라 일시정지 시 멈추고, 배속을 따르며, 1 tick 진행에도 맞춰 움직인다.
    /// 막대가 대상에 닿는 순간 대상 점멸(피격 흰색/가드 방패 흰색)을 일으킨다.
    /// </summary>
    public sealed class AttackFx
    {
        private sealed class Item
        {
            public GameObject Root;
            public Transform Stick, Impact, Head, Orb;
            public Renderer StickR, ImpactR, HeadR, OrbR;
            public MeshFilter HeadMf;
            public bool Active;
            public AttackFxKind Kind;
            public AttackShape Shape;
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
        private const float BoltThickness = 0.10f;    // 번개(노란 막대)는 조금 굵게
        private const float ArrowHeadLength = 0.24f;  // 화살촉(삼각뿔) 길이
        private const float ArrowHeadWidth = 0.17f;
        private const float HammerHeadWidth = 0.42f;  // 망치 머리(사면체): 막대와 직각인 가로 폭
        private const float HammerHeadSize = 0.26f;   // 망치 머리 높이·두께
        private const float OrbDiameter = 0.32f;      // 마법 구체 지름

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
        /// look: 투사체 모양·색(AttackLooks.For(스킬)). eventTick: 이벤트가 발생한 tick. 직전 상태(eventTick-1)가 표시되는 시점에 발사한다.
        /// </summary>
        public bool Spawn(AttackFxKind kind, AttackLook look, CombatantView attacker, CombatantView target, long eventTick, double distanceHint)
        {
            if (!Enabled || _root == null || attacker == null) return false;
            if (kind != AttackFxKind.Miss && target == null) return false;
            var it = Acquire();
            if (it == null) return false;

            it.Kind = kind;
            it.Shape = look.Shape;
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
            // 속성 색이 있는 모양(구체·노란 막대)은 결과와 관계없이 속성 색, 빗나감만 회색을 섞는다
            Color body = stick;
            if (look.HasColor) body = kind == AttackFxKind.Miss ? Color.Lerp(look.Color, new Color(0.6f, 0.6f, 0.6f), 0.5f) : look.Color;
            CombatantView.SetColor(it.StickR, body);
            CombatantView.SetColor(it.HeadR, body);
            CombatantView.SetColor(it.OrbR, body);
            CombatantView.SetColor(it.ImpactR, impact);
            if (it.Shape == AttackShape.Arrow) it.HeadMf.sharedMesh = AttackLooks.ArrowHeadMesh;
            else if (it.Shape == AttackShape.Hammer) it.HeadMf.sharedMesh = AttackLooks.TetraMesh;

            it.Active = true;
            it.Root.SetActive(true);
            it.Impact.gameObject.SetActive(false);
            ShowProjectile(it, true);
            PlaceProjectile(it, it.Start, it.UseTarget ? target.CenterPoint : it.FixedEnd, 0f, 1f);
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
                    PlaceProjectile(it, it.Start, end, t, 1f);
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

                // 투사체: 대상에 박힌 채 줄어듦(빗나감은 끝점에서 줄어듦)
                float shrink = 1f - Mathf.Clamp01(post / LingerSec);
                ShowProjectile(it, shrink > 0.01f);
                if (shrink > 0.01f) PlaceProjectile(it, it.Start, end, 1f, shrink);

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

        /// <summary>모양에 맞는 부품만 켠다: 구체는 구만, 화살·망치는 막대+머리, 막대·노란 막대는 막대만.</summary>
        private static void ShowProjectile(Item it, bool visible)
        {
            bool orb = it.Shape == AttackShape.Orb;
            bool head = it.Shape == AttackShape.Arrow || it.Shape == AttackShape.Hammer;
            it.Stick.gameObject.SetActive(visible && !orb);
            it.Head.gameObject.SetActive(visible && head);
            it.Orb.gameObject.SetActive(visible && orb);
        }

        private static void PlaceProjectile(Item it, Vector3 start, Vector3 end, float t, float scale)
        {
            Vector3 dir = end - start;
            if (dir.sqrMagnitude < 1e-6f) dir = it.Attacker != null ? it.Attacker.transform.forward : Vector3.forward;
            dir.Normalize();
            // 투사체 앞끝이 경로 위 t 지점에 오도록 배치
            Vector3 tip = Vector3.Lerp(start, end, t);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            float s = Mathf.Max(0.4f, scale);

            if (it.Shape == AttackShape.Orb)
            {
                float d = OrbDiameter * s;
                it.Orb.position = tip - dir * (d * 0.5f);
                it.Orb.localScale = new Vector3(d, d, d);
                return;
            }

            Vector3 stickFront = tip;
            if (it.Shape == AttackShape.Arrow)
            {
                // 삼각뿔 꼭짓점이 앞끝, 막대는 촉 밑면에 살짝 겹치게
                float hl = ArrowHeadLength * s, hw = ArrowHeadWidth * s;
                it.Head.position = tip - dir * (hl * 0.5f);
                it.Head.rotation = rot;
                it.Head.localScale = new Vector3(hw, hw, hl);
                stickFront = tip - dir * (hl * 0.8f);
            }
            else if (it.Shape == AttackShape.Hammer)
            {
                // 사면체 머리를 막대 끝에 가로로 붙인다(앞면이 앞끝)
                float hs = HammerHeadSize * s, hw = HammerHeadWidth * s;
                Vector3 c = tip - dir * (hs * 0.5f);
                it.Head.position = c;
                it.Head.rotation = rot;
                it.Head.localScale = new Vector3(hw, hs, hs);
                stickFront = c;
            }

            float len = it.Length * scale;
            it.Stick.position = stickFront - dir * (len * 0.5f);
            it.Stick.rotation = rot;
            float th = (it.Shape == AttackShape.Bolt ? BoltThickness : StickThickness) * s;
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
            it.Orb = MakePrim(PrimitiveType.Sphere, it.Root.transform, "Orb");
            var head = AttackLooks.MakeMeshObject(AttackLooks.ArrowHeadMesh, it.Root.transform, "Head");
            it.Head = head.transform;
            it.HeadMf = head.GetComponent<MeshFilter>();
            it.StickR = it.Stick.GetComponent<Renderer>();
            it.ImpactR = it.Impact.GetComponent<Renderer>();
            it.OrbR = it.Orb.GetComponent<Renderer>();
            it.HeadR = head.GetComponent<Renderer>();
            DisableShadows(it.StickR);
            DisableShadows(it.ImpactR);
            DisableShadows(it.OrbR);
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
