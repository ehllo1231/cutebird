using UnityEngine;
using UnityEngine.UI;

namespace Moonbird
{
    // Screen-space input geometry; it never participates in UI hit testing or physics.
    public sealed class DeveloperAimGraphic : MaskableGraphic
    {
        Vector2 origin, cursor;
        LaunchSolution solution;

        public void SetAim(Vector2 press, Vector2 pointer, LaunchSolution launch)
        {
            var canvas = GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, press, camera, out origin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, pointer, camera, out cursor);
            solution = launch;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var pullColor = solution.IsValid ? WorldView.Mint : new Color(1, .6f, .5f);
            var reference = new Color(.8f, .85f, 1, .45f);
            for (var y = 10; y < 114; y += 12)
                Line(mesh, origin + Vector2.up * y, origin + Vector2.up * (y + 5), 1.5f, reference);
            Line(mesh, origin, cursor, 2.5f, pullColor);
            Ring(mesh, origin, 7, WorldView.Yellow);
            Ring(mesh, cursor, 4, pullColor);
            Line(mesh, origin + Vector2.left * 12, origin + Vector2.right * 12, 1, WorldView.Yellow);
            Line(mesh, origin + Vector2.down * 12, origin + Vector2.up * 12, 1, WorldView.Yellow);
            if (!solution.IsValid) return;
            var angle = solution.Angle * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            var tip = origin + direction * 112;
            Line(mesh, origin, tip, 3, WorldView.Yellow);
            var side = new Vector2(-direction.y, direction.x);
            Line(mesh, tip, tip - direction * 13 + side * 7, 3, WorldView.Yellow);
            Line(mesh, tip, tip - direction * 13 - side * 7, 3, WorldView.Yellow);
            var previous = origin + Vector2.up * 38;
            for (var i = 1; i <= 24; i++)
            {
                var a = angle * i / 24;
                var next = origin + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 38;
                Line(mesh, previous, next, 2, WorldView.Yellow);
                previous = next;
            }
        }

        static void Ring(VertexHelper mesh, Vector2 center, float radius, Color color)
        {
            for (var i = 0; i < 24; i++)
            {
                var a = i * Mathf.PI * 2 / 24;
                var b = (i + 1) * Mathf.PI * 2 / 24;
                Line(mesh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, 2, color);
            }
        }

        static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            if ((to - from).sqrMagnitude < .0001f) return;
            var d = (to - from).normalized;
            var normal = new Vector2(-d.y, d.x) * width / 2;
            var index = mesh.currentVertCount;
            mesh.AddVert(from - normal, color, Vector2.zero);
            mesh.AddVert(from + normal, color, Vector2.zero);
            mesh.AddVert(to + normal, color, Vector2.zero);
            mesh.AddVert(to - normal, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
