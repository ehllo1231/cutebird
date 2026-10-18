using System.Collections.Generic;
using UnityEngine;

namespace Moonbird
{
    public sealed class WorldView : MonoBehaviour
    {
        public static readonly Color Sky = Hex("202C46");
        public static readonly Color Mint = Hex("93D9C5");
        public static readonly Color Accent = Hex("BBA9F5");
        public static readonly Color Yellow = Hex("FFE49A");
        public Material SpriteMaterial { get; private set; }
        public Sprite Square { get; private set; }
        public Sprite Circle { get; private set; }
        public Transform BirdVisual { get; private set; }
        public Rigidbody2D BirdBody { get; private set; }
        public CapsuleCollider2D BirdCollider { get; private set; }
        public Transform SpawnMarker { get; private set; }
        public Transform Goal { get; private set; }
        public CircleCollider2D GoalCollider { get; private set; }
        public Transform KillLine { get; private set; }
        public MapDocument Document { get; private set; }

        sealed class PlatformView
        {
            public Transform root;
            public SpriteRenderer body, top, shadow;
            public BoxCollider2D collider;
        }
        readonly Dictionary<string, PlatformView> platforms = new Dictionary<string, PlatformView>();
        readonly List<SpriteRenderer> grid = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> selection = new List<SpriteRenderer>();
        PhysicsMaterial2D physicsMaterial;
        Transform gridRoot, selectionRoot;
        bool isEditing;

        public void Initialize()
        {
            SpriteMaterial = Resources.Load<Material>("MoonbirdSprite");
            Square = MakeSprite(false);
            Circle = MakeSprite(true);
            physicsMaterial = new PhysicsMaterial2D("Moonbird surfaces") { friction = 0, bounciness = 0 };
            gridRoot = new GameObject("Editor grid").transform;
            selectionRoot = new GameObject("Selection handles").transform;
            gridRoot.SetParent(transform);
            selectionRoot.SetParent(transform);
            for (var i = 0; i < 8; i++) selection.Add(Draw("Handle", selectionRoot, Square, Accent, 20));
            SpawnMarker = new GameObject("Start marker").transform;
            SpawnMarker.SetParent(transform);
            var startRing = Draw("Start ring", SpawnMarker, Circle, new Color(.58f, .85f, .77f, .18f), 0);
            startRing.transform.localScale = Vector3.one * 1.5f;
            Draw("Start dot", SpawnMarker, Circle, Mint, 1).transform.localScale = Vector3.one * .18f;
            Goal = new GameObject("The moon").transform;
            Goal.SetParent(transform);
            Draw("Moon glow", Goal, Circle, new Color(1, .89f, .6f, .08f), 0).transform.localScale = Vector3.one * 2.6f;
            Draw("Moon", Goal, Circle, Yellow, 1).transform.localScale = Vector3.one * 1.5f;
            var cutout = Draw("Crescent", Goal, Circle, Sky, 2);
            cutout.transform.localScale = Vector3.one * 1.3f;
            cutout.transform.localPosition = new Vector3(.38f, .18f);
            GoalCollider = Goal.gameObject.AddComponent<CircleCollider2D>();
            GoalCollider.radius = .8f;
            GoalCollider.isTrigger = true;
            KillLine = Draw("Fall boundary", transform, Square, new Color(1, .55f, .58f, .5f), 0).transform;
            KillLine.localScale = new Vector3(300, .025f, 1);
            BuildBird();
            BuildStars();
        }

        public void Apply(MapDocument map, bool editing)
        {
            Document = map;
            isEditing = editing;
            var existing = new HashSet<string>();
            foreach (var p in map.platforms)
            {
                existing.Add(p.id);
                if (!platforms.TryGetValue(p.id, out var view))
                {
                    view = new PlatformView();
                    view.root = new GameObject("Platform " + p.id).transform;
                    view.root.SetParent(transform);
                    view.body = Draw("Stone", view.root, Square, Hex("4C6876"), 2);
                    view.shadow = Draw("Shadow", view.root, Square, Hex("152138"), 1);
                    view.top = Draw("Soft grass", view.root, Square, Mint, 3);
                    view.collider = view.root.gameObject.AddComponent<BoxCollider2D>();
                    view.collider.sharedMaterial = physicsMaterial;
                    platforms.Add(p.id, view);
                }
                view.root.position = p.position;
                view.body.transform.localScale = new Vector3(p.size.x, p.size.y, 1);
                view.shadow.transform.localScale = new Vector3(p.size.x + .08f, p.size.y + .04f, 1);
                view.shadow.transform.localPosition = new Vector3(.04f, -.07f, 0);
                view.top.transform.localScale = new Vector3(p.size.x, Mathf.Min(.12f, p.size.y * .25f), 1);
                view.top.transform.localPosition = new Vector3(0, p.size.y * .5f - .04f);
                view.collider.size = p.size;
            }
            var removed = new List<string>();
            foreach (var pair in platforms)
                if (!existing.Contains(pair.Key)) { pair.Value.root.gameObject.SetActive(false); Destroy(pair.Value.root.gameObject); removed.Add(pair.Key); }
            foreach (var id in removed) platforms.Remove(id);
            SpawnMarker.position = map.spawn;
            SpawnMarker.gameObject.SetActive(editing);
            Goal.position = map.goal;
            KillLine.position = new Vector3(0, map.killY);
            KillLine.gameObject.SetActive(editing);
            gridRoot.gameObject.SetActive(editing);
            selectionRoot.gameObject.SetActive(editing);
            if (editing)
            {
                BirdBody.simulated = false;
                BirdBody.position = map.spawn;
                BirdBody.transform.position = map.spawn;
                BirdVisual.localScale = Vector3.one;
            }
            Physics2D.gravity = Vector2.down * map.jump.gravity;
            Physics2D.SyncTransforms();
        }

        public void UpdateEditorOverlays(Camera camera, Rect? selectedBounds, bool showGrid, bool canResize = true)
        {
            if (!isEditing) return;
            var h = camera.orthographicSize * 2;
            var w = h * camera.aspect;
            var c = camera.transform.position;
            var spacing = camera.orthographicSize > 25 ? 5f : camera.orthographicSize > 12 ? 2f : 1f;
            var index = 0;
            if (showGrid)
            {
                for (var x = Mathf.Floor((c.x - w / 2) / spacing) * spacing; x <= c.x + w / 2; x += spacing)
                    GridLine(index++, new Vector2(x, c.y), new Vector2(.012f * camera.orthographicSize / 7, h), Mathf.Abs(x) < .01f);
                for (var y = Mathf.Floor((c.y - h / 2) / spacing) * spacing; y <= c.y + h / 2; y += spacing)
                    GridLine(index++, new Vector2(c.x, y), new Vector2(w, .012f * camera.orthographicSize / 7), Mathf.Abs(y) < .01f);
            }
            for (; index < grid.Count; index++) grid[index].gameObject.SetActive(false);
            foreach (var item in selection) item.gameObject.SetActive(selectedBounds.HasValue);
            if (!selectedBounds.HasValue) return;
            var r = selectedBounds.Value;
            var thickness = h / camera.pixelHeight * 2;
            Place(selection[0], new Vector2(r.center.x, r.yMin), new Vector2(r.width, thickness));
            Place(selection[1], new Vector2(r.center.x, r.yMax), new Vector2(r.width, thickness));
            Place(selection[2], new Vector2(r.xMin, r.center.y), new Vector2(thickness, r.height));
            Place(selection[3], new Vector2(r.xMax, r.center.y), new Vector2(thickness, r.height));
            var handle = h / camera.pixelHeight * 10;
            for (var i = 0; i < 4; i++)
            {
                selection[i + 4].gameObject.SetActive(canResize);
                Place(selection[i + 4], new Vector2((i & 1) == 0 ? r.xMin : r.xMax, (i & 2) == 0 ? r.yMin : r.yMax), Vector2.one * handle);
            }
        }

        void GridLine(int index, Vector2 position, Vector2 size, bool axis)
        {
            if (index == grid.Count) grid.Add(Draw("Grid line", gridRoot, Square, Color.white, -5));
            grid[index].gameObject.SetActive(true);
            grid[index].color = axis ? new Color(.65f, .74f, .86f, .2f) : new Color(.65f, .74f, .86f, .065f);
            Place(grid[index], position, size);
        }

        static void Place(SpriteRenderer renderer, Vector2 position, Vector2 size)
        {
            renderer.transform.position = position;
            renderer.transform.localScale = new Vector3(size.x, size.y, 1);
        }

        void BuildBird()
        {
            var root = new GameObject("Baby bird");
            root.transform.SetParent(transform);
            BirdBody = root.AddComponent<Rigidbody2D>();
            BirdBody.freezeRotation = true;
            BirdBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            BirdBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            BirdBody.sharedMaterial = physicsMaterial;
            BirdBody.simulated = false;
            BirdCollider = root.AddComponent<CapsuleCollider2D>();
            BirdCollider.size = new Vector2(.72f, .9f);
            BirdCollider.sharedMaterial = physicsMaterial;
            BirdVisual = new GameObject("Bird art").transform;
            BirdVisual.SetParent(root.transform, false);
            BirdPart("Left foot", new Vector2(-.19f, -.41f), new Vector2(.23f, .10f), Hex("EBA16F"), 8);
            BirdPart("Right foot", new Vector2(.19f, -.41f), new Vector2(.23f, .10f), Hex("EBA16F"), 8);
            BirdPart("Body", Vector2.zero, new Vector2(.86f, .95f), Yellow, 9);
            BirdPart("Belly", new Vector2(0, -.13f), new Vector2(.6f, .55f), Hex("FFF1C4"), 10);
            BirdPart("Left wing", new Vector2(-.37f, -.06f), new Vector2(.22f, .42f), Hex("F1CB79"), 11);
            BirdPart("Right wing", new Vector2(.37f, -.06f), new Vector2(.22f, .42f), Hex("F1CB79"), 11);
            BirdPart("Left eye", new Vector2(-.15f, .13f), new Vector2(.085f, .12f), Hex("394354"), 12);
            BirdPart("Right eye", new Vector2(.15f, .13f), new Vector2(.085f, .12f), Hex("394354"), 12);
            BirdPart("Left blush", new Vector2(-.25f, .01f), new Vector2(.14f, .075f), Hex("F4ACA0"), 12);
            BirdPart("Right blush", new Vector2(.25f, .01f), new Vector2(.14f, .075f), Hex("F4ACA0"), 12);
            BirdPart("Beak", new Vector2(0, .015f), new Vector2(.14f, .085f), Hex("E9A365"), 13);
            var tuft = BirdPart("Tuft", new Vector2(-.07f, .48f), new Vector2(.15f, .28f), Yellow, 9);
            tuft.localRotation = Quaternion.Euler(0, 0, 25);
            BirdPart("Tuft small", new Vector2(.05f, .46f), new Vector2(.13f, .2f), Yellow, 9);
        }

        Transform BirdPart(string name, Vector2 position, Vector2 scale, Color color, int order)
        {
            var part = Draw(name, BirdVisual, Circle, color, order).transform;
            part.localPosition = position;
            part.localScale = new Vector3(scale.x, scale.y, 1);
            return part;
        }

        void BuildStars()
        {
            var random = new System.Random(413);
            for (var i = 0; i < 180; i++)
            {
                var star = Draw("Distant star", transform, Circle, new Color(.83f, .87f, .99f, .15f + (float)random.NextDouble() * .25f), -10).transform;
                star.position = new Vector3((float)random.NextDouble() * 110 - 55, (float)random.NextDouble() * 110 - 25);
                star.localScale = Vector3.one * (.025f + (float)random.NextDouble() * .045f);
            }
        }

        Sprite MakeSprite(bool circle)
        {
            const int n = 64;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = circle ? "Soft circle" : "Square", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f));
                    pixels[y * n + x] = new Color(1, 1, 1, circle ? Mathf.Clamp01(n / 2f - d) : 1);
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, n, n), Vector2.one * .5f, n);
        }

        SpriteRenderer Draw(string name, Transform parent, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            if (SpriteMaterial) renderer.sharedMaterial = SpriteMaterial;
            return renderer;
        }

        public static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
    }
}
