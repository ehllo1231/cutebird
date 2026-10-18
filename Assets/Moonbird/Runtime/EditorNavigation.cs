using UnityEngine;

namespace Moonbird
{
    public enum EditorTool { Select, Pan }

    public static class EditorNavigation
    {
        public const float ZoomFactor = 1.35f;
        public const float MinSize = 1f, MaxSize = 100f;

        // Input System's uniform range supplies one unit per wheel detent.
        public static float Zoom(float size, float steps) => Mathf.Clamp(
            size * Mathf.Pow(ZoomFactor, -Mathf.Clamp(steps, -8, 8)), MinSize, MaxSize);
    }
}
