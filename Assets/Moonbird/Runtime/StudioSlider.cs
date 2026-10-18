using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Moonbird
{
    // Start history before Slider handles its initial click, not after the value changes.
    public sealed class StudioSlider : Slider
    {
        public Action Begin, End;
        public override void OnPointerDown(PointerEventData data) { Begin?.Invoke(); base.OnPointerDown(data); }
        public override void OnPointerUp(PointerEventData data) { base.OnPointerUp(data); End?.Invoke(); }
        protected override void OnDisable() { End?.Invoke(); base.OnDisable(); }
    }
}
