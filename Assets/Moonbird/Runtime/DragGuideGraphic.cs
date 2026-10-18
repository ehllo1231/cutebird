using UnityEngine;
using UnityEngine.UI;

namespace Moonbird
{
    // The player's gesture stays in screen space as the world camera follows the bird.
    // Its appearance depends only on the two pointer positions, never on jump strength.
    public sealed class DragGuideGraphic : MaskableGraphic
    {
        const int CircleSegments = 40;
        Vector2 origin, cursor;

        public void SetDrag(Vector2 press, Vector2 pointer)
        {
            var canvas = GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, press, camera, out var nextOrigin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, pointer, camera, out var nextCursor);
            if (origin == nextOrigin && cursor == nextCursor) return;
            origin = nextOrigin;
            cursor = nextCursor;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Segment(mesh, origin, cursor, 9, Tint(WorldView.Mint, .07f));
            Segment(mesh, origin, cursor, 5, Tint(WorldView.Mint, .2f));
            Segment(mesh, origin, cursor, 2.8f, Tint(WorldView.Mint, .9f));

            if ((cursor - origin).sqrMagnitude > 14 * 14)
            {
                Disc(mesh, cursor, 10, Tint(WorldView.Mint, .09f));
                Disc(mesh, cursor, 5, WorldView.Sky);
                Ring(mesh, cursor, 5, 1.4f, Tint(WorldView.Mint, .85f));
                Disc(mesh, cursor, 2.5f, WorldView.Mint);
            }
            Disc(mesh, origin, 16, Tint(WorldView.Yellow, .06f));
            Disc(mesh, origin, 12, Tint(WorldView.Yellow, .1f));
            Disc(mesh, origin, 8.5f, WorldView.Sky);
            Ring(mesh, origin, 8.5f, 1.8f, WorldView.Yellow);
            Disc(mesh, origin, 3.3f, WorldView.Yellow);
        }

        static Color Tint(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        static void Segment(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            if ((to - from).sqrMagnitude < .0001f) return;
            var normal = new Vector2(from.y - to.y, to.x - from.x).normalized * (width / 2);
            var index = mesh.currentVertCount;
            mesh.AddVert(from - normal, color, Vector2.zero);
            mesh.AddVert(from + normal, color, Vector2.zero);
            mesh.AddVert(to + normal, color, Vector2.zero);
            mesh.AddVert(to - normal, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
            Disc(mesh, from, width / 2, color);
            Disc(mesh, to, width / 2, color);
        }

        static void Disc(VertexHelper mesh, Vector2 center, float radius, Color color)
        {
            var index = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (var i = 0; i < CircleSegments; i++)
            {
                var angle = i * Mathf.PI * 2 / CircleSegments;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                mesh.AddTriangle(index, index + 1 + i, index + 1 + (i + 1) % CircleSegments);
            }
        }

        static void Ring(VertexHelper mesh, Vector2 center, float radius, float width, Color color)
        {
            var index = mesh.currentVertCount;
            for (var i = 0; i < CircleSegments; i++)
            {
                var angle = i * Mathf.PI * 2 / CircleSegments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + direction * (radius - width / 2), color, Vector2.zero);
                mesh.AddVert(center + direction * (radius + width / 2), color, Vector2.zero);
                var next = index + (i + 1) % CircleSegments * 2;
                mesh.AddTriangle(index + i * 2, index + i * 2 + 1, next + 1);
                mesh.AddTriangle(index + i * 2, next + 1, next);
            }
        }
    }
}
