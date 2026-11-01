using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ARPG.Runtime
{
    /// <summary>
    /// 사선 시점 카메라. 우클릭 드래그 회전, 휠 확대, 중클릭 드래그/WASD 이동, F 선택 개체 추적.
    /// 프로젝트의 Active Input Handling 이 Input System 이므로 Input System API 를 사용한다.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Vector3 Focus = Vector3.zero;
        public float Distance = 20f;
        public float Yaw = 0f;
        public float Pitch = 55f;
        public bool BlockPointer;     // UI 위에서는 마우스 입력 무시
        public bool BlockKeyboard;    // 텍스트 입력 중
        public Transform Follow;

        private void LateUpdate()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            if (mouse != null && !BlockPointer)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    Yaw += delta.x * 0.25f;
                    Pitch = Mathf.Clamp(Pitch - delta.y * 0.25f, 10f, 89f);
                }
                if (mouse.middleButton.isPressed)
                {
                    Vector3 right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
                    Vector3 fwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
                    Focus -= (right * delta.x + fwd * delta.y) * Distance * 0.0015f;
                    Follow = null;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) Distance = Mathf.Clamp(Distance * (scroll > 0 ? 0.9f : 1.1f), 3f, 120f);
            }
            if (kb != null && !BlockKeyboard)
            {
                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move += Vector3.forward;
                if (kb.sKey.isPressed) move += Vector3.back;
                if (kb.aKey.isPressed) move += Vector3.left;
                if (kb.dKey.isPressed) move += Vector3.right;
                if (move != Vector3.zero)
                {
                    Focus += Quaternion.Euler(0, Yaw, 0) * move.normalized * Distance * 0.8f * dt;
                    Follow = null;
                }
                if (kb.qKey.isPressed) Yaw -= 90f * dt;
                if (kb.eKey.isPressed) Yaw += 90f * dt;
            }
#endif
            if (Follow != null) Focus = Vector3.Lerp(Focus, new Vector3(Follow.position.x, 0, Follow.position.z), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            transform.position = Focus + rot * (Vector3.back * Distance);
            transform.rotation = rot;
        }
    }

    /// <summary>LineRenderer 기반 디버그 도형(판정 원, 대상 선, 격자).</summary>
    public sealed class DebugDraw
    {
        private readonly Transform _root;
        private readonly Material _mat;
        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private int _used;
        public bool Available { get { return _mat != null; } }

        public DebugDraw(Transform root)
        {
            _root = root;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null) _mat = new Material(sh);
        }

        public void Begin() { _used = 0; }

        public void End()
        {
            for (int i = _used; i < _pool.Count; i++) if (_pool[i].enabled) _pool[i].enabled = false;
        }

        private LineRenderer Next(Color c, float width)
        {
            if (_mat == null) return null;
            LineRenderer lr;
            if (_used < _pool.Count) lr = _pool[_used];
            else
            {
                var go = new GameObject("DebugLine");
                go.transform.SetParent(_root, false);
                lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _mat;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                _pool.Add(lr);
            }
            _used++;
            lr.enabled = true;
            lr.startColor = c;
            lr.endColor = c;
            lr.startWidth = width;
            lr.endWidth = width;
            return lr;
        }

        public void Circle(Vector3 center, float radius, Color c, float width = 0.05f, int segments = 48)
        {
            var lr = Next(c, width);
            if (lr == null) return;
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius));
            }
        }

        public void Line(Vector3 a, Vector3 b, Color c, float width = 0.04f)
        {
            var lr = Next(c, width);
            if (lr == null) return;
            lr.loop = false;
            lr.positionCount = 2;
            lr.SetPosition(0, a + Vector3.up * 0.05f);
            lr.SetPosition(1, b + Vector3.up * 0.05f);
        }
    }
}
