using ARPG.Core;
using UnityEngine;

namespace ARPG.Runtime
{
    /// <summary>공격 도형(화면 전용). 스킬 Damage_Type 의 첫 값으로 정한다(복합 타입은 앞의 것).</summary>
    public enum AttackShape
    {
        Stick = 0,   // 막대 — PHYSICAL·SLASH(기존 표시 그대로)
        Arrow = 1,   // 막대 + 앞쪽 삼각뿔(화살) — RANGED·THRUST
        Hammer = 2,  // 막대 + 끝의 사면체(망치) — STRIKE
        Orb = 3,     // 구체 — MAGIC(하늘)·FIRE(빨강)·WATER(파랑)·HOLY(노랑)
        Bolt = 4,    // 노란 막대 — LIGHTNING
    }

    /// <summary>공격 도형과 고정 색. HasColor 가 false 면 기존 색 규칙(결과별 색, 들고 있는 무기는 회색)을 따른다.</summary>
    public struct AttackLook
    {
        public AttackShape Shape;
        public bool HasColor;
        public Color Color;

        public static readonly AttackLook Default = new AttackLook { Shape = AttackShape.Stick, HasColor = false };
    }

    /// <summary>
    /// Damage_Type → 공격 도형 매핑과 도형용 메시(삼각뿔·사면체).
    /// 던지는 연출(AttackFx)과 휘두르는 무기(CombatantView)가 같은 규칙을 쓴다. 판정에는 관여하지 않는다.
    /// </summary>
    public static class AttackLooks
    {
        public static readonly Color MagicColor = new Color(0.45f, 0.85f, 1f);     // 하늘색
        public static readonly Color FireColor = new Color(1f, 0.18f, 0.12f);      // 빨강
        public static readonly Color WaterColor = new Color(0.12f, 0.32f, 1f);     // 파랑
        public static readonly Color LightningColor = new Color(1f, 0.88f, 0f);    // 진한 노랑(명중 막대의 옅은 노랑과 구별)
        public static readonly Color HolyColor = new Color(1f, 0.9f, 0.2f);        // 노랑

        /// <summary>스킬의 첫 Damage_Type 으로 도형 결정. 스킬이 없거나 타입이 비어 있으면 기본 막대.</summary>
        public static AttackLook For(SkillDefinition s)
        {
            if (s == null || s.DamageTypes == null || s.DamageTypes.Length == 0) return AttackLook.Default;
            return For(s.DamageTypes[0]);
        }

        public static AttackLook For(DamageType t)
        {
            switch (t)
            {
                case DamageType.RANGED:
                case DamageType.THRUST: return Make(AttackShape.Arrow, false, Color.white);
                case DamageType.STRIKE: return Make(AttackShape.Hammer, false, Color.white);
                case DamageType.MAGIC: return Make(AttackShape.Orb, true, MagicColor);
                case DamageType.FIRE: return Make(AttackShape.Orb, true, FireColor);
                case DamageType.WATER: return Make(AttackShape.Orb, true, WaterColor);
                case DamageType.HOLY: return Make(AttackShape.Orb, true, HolyColor);
                case DamageType.LIGHTNING: return Make(AttackShape.Bolt, true, LightningColor);
                default: return AttackLook.Default; // PHYSICAL, SLASH
            }
        }

        private static AttackLook Make(AttackShape shape, bool hasColor, Color c)
        {
            return new AttackLook { Shape = shape, HasColor = hasColor, Color = c };
        }

        // ------------------------------------------------------------------ 메시
        private static Mesh _arrowHead, _tetra;

        /// <summary>사각뿔(화살촉): 밑면 z=-0.5(가로·세로 1), 꼭짓점 z=+0.5. localScale = (폭, 높이, 길이).</summary>
        public static Mesh ArrowHeadMesh
        {
            get
            {
                if (_arrowHead == null)
                {
                    var apex = new Vector3(0f, 0f, 0.5f);
                    var b0 = new Vector3(-0.5f, -0.5f, -0.5f);
                    var b1 = new Vector3(0.5f, -0.5f, -0.5f);
                    var b2 = new Vector3(0.5f, 0.5f, -0.5f);
                    var b3 = new Vector3(-0.5f, 0.5f, -0.5f);
                    _arrowHead = BuildFlat("ArrowHead", new[]
                    {
                        apex, b0, b1,  apex, b1, b2,  apex, b2, b3,  apex, b3, b0,
                        b0, b1, b2,  b0, b2, b3,
                    }, new Vector3(0f, 0f, -0.25f));
                }
                return _arrowHead;
            }
        }

        /// <summary>정사면체(망치 머리): 정육면체 [-0.5, 0.5] 의 꼭짓점 4개를 쓴다. localScale 로 늘려 쓴다.</summary>
        public static Mesh TetraMesh
        {
            get
            {
                if (_tetra == null)
                {
                    var p0 = new Vector3(0.5f, 0.5f, 0.5f);
                    var p1 = new Vector3(0.5f, -0.5f, -0.5f);
                    var p2 = new Vector3(-0.5f, 0.5f, -0.5f);
                    var p3 = new Vector3(-0.5f, -0.5f, 0.5f);
                    _tetra = BuildFlat("Tetrahedron", new[]
                    {
                        p0, p1, p2,  p0, p1, p3,  p0, p2, p3,  p1, p2, p3,
                    }, Vector3.zero);
                }
                return _tetra;
            }
        }

        /// <summary>면마다 꼭짓점을 따로 두어 각진 음영을 만든다. 삼각형 감김은 바깥(center 반대쪽)을 향하도록 자동으로 맞춘다.</summary>
        private static Mesh BuildFlat(string name, Vector3[] tris, Vector3 center)
        {
            var verts = new Vector3[tris.Length];
            var idx = new int[tris.Length];
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0f)
                {
                    Vector3 t = b; b = c; c = t;
                }
                verts[i] = a; verts[i + 1] = b; verts[i + 2] = c;
                idx[i] = i; idx[i + 1] = i + 1; idx[i + 2] = i + 2;
            }
            var m = new Mesh { name = name };
            m.vertices = verts;
            m.triangles = idx;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>기본 큐브를 만들고 메시만 바꿔 끼운다(렌더 파이프라인 기본 재질을 그대로 쓰기 위해). 충돌체는 제거.</summary>
        public static GameObject MakeMeshObject(Mesh mesh, Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = mesh;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
