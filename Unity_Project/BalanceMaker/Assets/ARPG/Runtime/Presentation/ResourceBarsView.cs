using ARPG.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.Runtime
{
    /// <summary>
    /// 머리 위 HP/FP/스태미나 바(World Space Canvas). 채움량만 0~1 로 제한하며 실제 음수 값은 코어에 보존된다.
    /// </summary>
    public sealed class ResourceBarsView : MonoBehaviour
    {
        private RectTransform _hp, _fp, _st;
        private Image _fpBg, _stBg;
        private static readonly Color HpColor = new Color(0.9f, 0.15f, 0.15f);
        private static readonly Color FpColor = new Color(0.2f, 0.45f, 1f);
        private static readonly Color StColor = new Color(0.2f, 0.85f, 0.3f);
        private static readonly Color BgColor = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color UnusedColor = new Color(0.35f, 0.35f, 0.35f, 0.35f);

        public static ResourceBarsView Create(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("ResourceBars", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(120, 34);
            rt.localScale = Vector3.one * 0.01f;
            var v = go.AddComponent<ResourceBarsView>();
            Image bg;
            v._hp = Row(rt, 0, HpColor, out bg);
            v._fp = Row(rt, 1, FpColor, out v._fpBg);
            v._st = Row(rt, 2, StColor, out v._stBg);
            return v;
        }

        private static RectTransform Row(RectTransform parent, int index, Color fill, out Image bgImage)
        {
            float h = 10, gap = 2;
            float top = -index * (h + gap);
            var bg = new GameObject("Bg" + index, typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            var brt = (RectTransform)bg.transform;
            brt.anchorMin = new Vector2(0, 1);
            brt.anchorMax = new Vector2(1, 1);
            brt.pivot = new Vector2(0.5f, 1);
            brt.offsetMin = new Vector2(0, top - h);
            brt.offsetMax = new Vector2(0, top);
            bgImage = bg.AddComponent<Image>();
            bgImage.color = BgColor;
            bgImage.raycastTarget = false;

            var fg = new GameObject("Fill", typeof(RectTransform));
            fg.transform.SetParent(brt, false);
            var frt = (RectTransform)fg.transform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 1);
            frt.offsetMin = new Vector2(0, 1);
            frt.offsetMax = new Vector2(0, -1);
            var img = fg.AddComponent<Image>();
            img.color = fill;
            img.raycastTarget = false;
            return frt;
        }

        /// <summary>최댓값 > 0 ? clamp(현재/최대, 0, 1) : 0. 0을 ∞로 표시하지 않는다.</summary>
        public static float Ratio(double cur, double max)
        {
            return max > 0 ? Mathf.Clamp01((float)(cur / max)) : 0f;
        }

        private static void SetFill(RectTransform fill, float ratio)
        {
            fill.anchorMax = new Vector2(ratio, 1);
        }

        public void Apply(Combatant c, Camera cam)
        {
            SetFill(_hp, Ratio(c.Hp, c.MaxHp));
            bool fpUsed = c.MaxFp > 0;
            bool stUsed = c.MaxStamina > 0 && c.ResourcesManaged;
            SetFill(_fp, fpUsed ? Ratio(c.Fp, c.MaxFp) : 0);
            SetFill(_st, stUsed ? Ratio(c.Stamina, c.MaxStamina) : 0);
            _fpBg.color = fpUsed ? BgColor : UnusedColor;
            _stBg.color = stUsed ? (c.Stamina < 0 ? new Color(0.45f, 0f, 0f, 0.75f) : BgColor) : UnusedColor;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
