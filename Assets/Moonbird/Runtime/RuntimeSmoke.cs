using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace Moonbird
{
    // Explicit command-line QA mode. Its recovery files never touch the user's session.
    public sealed class RuntimeSmoke : MonoBehaviour
    {
        readonly List<string> checks = new List<string>();
        static string output;

        public static string GetOutputDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-moonbirdSmoke") return output = Path.GetFullPath(args[i + 1]);
            return null;
        }

        IEnumerator Start()
        {
            if (GetOutputDirectory() == null) yield break;
            Directory.CreateDirectory(output);
            var app = GetComponent<StudioApp>();
            // A bounded coroutine lets a failure be reported without a silent hung process.
            var routine = Check(app);
            Exception failure = null;
            while (true)
            {
                object current = null;
                bool next;
                try { next = routine.MoveNext(); if (next) current = routine.Current; }
                catch (Exception e) { failure = e; break; }
                if (!next) break;
                yield return current;
            }
            var report = (failure == null ? "PASS\n" : "FAIL\n" + failure + "\n") + string.Join("\n", checks);
            File.WriteAllText(Path.Combine(output, "runtime-smoke.txt"), report);
            Debug.Log("MOONBIRD_RUNTIME_" + (failure == null ? "PASS" : "FAIL") + "\n" + report);
            app.AutomationQuit();
        }

        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("OK " + message);
        }

        IEnumerator Check(StudioApp app)
        {
            yield return null;
            app.ReplaceDocument(MapDocument.CreateDefault(), null, "");
            app.FrameAll();
            yield return new WaitForSecondsRealtime(.8f);
            Capture(app, "editor.png");
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse = InputSystem.AddDevice<Mouse>("SmokeMouse");
            mouse.MakeCurrent();
            var keyboard = InputSystem.AddDevice<Keyboard>("SmokeKeyboard");
            keyboard.MakeCurrent();
            var developerButton = app.UI.DeveloperEditorButton;
            var buttonCenter = ControlCenter((RectTransform)developerButton.transform);
            Require(developerButton.IsActive() && Screen.safeArea.Contains(buttonCenter), "Prominent developer button is visible in editor");
            yield return Drag(mouse, buttonCenter, buttonCenter);
            Require(app.DeveloperMode && app.UI.DeveloperHUDVisible, "Actual sidebar developer button opens diagnostics");
            yield return Drag(mouse, buttonCenter, buttonCenter);
            Require(!app.DeveloperMode, "Actual sidebar developer button turns diagnostics off");
            var navigationDocument = JsonUtility.ToJson(app.Document);
            var platformScreen = (Vector2)app.WorldCamera.WorldToScreenPoint(app.Document.platforms[1].position);
            var panButton = ControlCenter((RectTransform)app.UI.PanToolButton.transform);
            yield return Drag(mouse, panButton, panButton);
            Require(app.ActiveEditorTool == EditorTool.Pan, "Hand tool button activates camera drag mode");
            var cameraBeforePan = app.WorldCamera.transform.position;
            yield return Drag(mouse, platformScreen, platformScreen + new Vector2(120, 70));
            Require(Vector3.Distance(cameraBeforePan, app.WorldCamera.transform.position) > .5f
                && JsonUtility.ToJson(app.Document) == navigationDocument && app.SelectedPlatform == null && !app.History.CanUndo,
                "Hand drag over a platform moves only camera without selecting or editing");
            yield return Tap(keyboard, Key.V);
            Require(app.ActiveEditorTool == EditorTool.Select, "V shortcut returns to selection mode");
            app.FrameAll();
            platformScreen = app.WorldCamera.WorldToScreenPoint(app.Document.platforms[1].position);
            cameraBeforePan = app.WorldCamera.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return null;
            yield return Drag(mouse, platformScreen, platformScreen + new Vector2(100, -60));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            Require(app.ActiveEditorTool == EditorTool.Select && Vector3.Distance(cameraBeforePan, app.WorldCamera.transform.position) > .5f
                && JsonUtility.ToJson(app.Document) == navigationDocument, "Space temporarily pans without changing tool or map");
            yield return Tap(keyboard, Key.H);
            Require(app.ActiveEditorTool == EditorTool.Pan, "H shortcut activates hand tool");
            var selectButton = ControlCenter((RectTransform)app.UI.SelectToolButton.transform);
            yield return Drag(mouse, selectButton, selectButton);
            Require(app.ActiveEditorTool == EditorTool.Select, "Selection tool button restores object editing");
            app.FrameAll();
            var zoomPoint = app.WorldCamera.pixelRect.position + app.WorldCamera.pixelRect.size * new Vector2(.6f, .45f);
            var zoomBefore = app.WorldCamera.orthographicSize;
            var worldBefore = app.WorldCamera.ScreenToWorldPoint(new Vector3(zoomPoint.x, zoomPoint.y, 10));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = zoomPoint, scroll = Vector2.up });
            yield return null;
            yield return null;
            Require(Mathf.Abs(zoomBefore / app.WorldCamera.orthographicSize - 1.35f) < .001f,
                "One real wheel detent magnifies viewport by 35 percent");
            var worldAfter = app.WorldCamera.ScreenToWorldPoint(new Vector3(zoomPoint.x, zoomPoint.y, 10));
            Require(Vector3.Distance(worldBefore, worldAfter) < .001f, "Wheel zoom preserves point beneath cursor");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = zoomPoint, scroll = Vector2.down });
            yield return null;
            yield return null;
            Require(Mathf.Abs(app.WorldCamera.orthographicSize - zoomBefore) < .001f, "Opposite wheel detent restores zoom");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width - 100, Screen.height / 2), scroll = Vector2.up });
            yield return null;
            yield return null;
            Require(Mathf.Abs(app.WorldCamera.orthographicSize - zoomBefore) < .001f, "Wheel over settings panel does not zoom map");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = zoomPoint });
            yield return null;
            Require(JsonUtility.ToJson(app.Document) == navigationDocument && !app.History.CanUndo,
                "Navigation changes do not modify map or undo history");
            Capture(app, "editor-tools.png");
            var originalPosition = app.Document.platforms[1].position;
            var originalSize = app.Document.platforms[1].size;
            yield return Drag(mouse, app.WorldCamera.WorldToScreenPoint(originalPosition),
                app.WorldCamera.WorldToScreenPoint(originalPosition + Vector2.one * .5f));
            Require(Vector2.Distance(app.Document.platforms[1].position, originalPosition + Vector2.one * .5f) < .02f, "Real pointer drag moves platform");
            app.Undo();
            Require(Vector2.Distance(app.Document.platforms[1].position, originalPosition) < .02f, "One undo restores entire move gesture");
            var corner = originalPosition + originalSize / 2;
            yield return Drag(mouse, app.WorldCamera.WorldToScreenPoint(corner),
                app.WorldCamera.WorldToScreenPoint(corner + Vector2.one * .5f));
            Require(Vector2.Distance(app.Document.platforms[1].size, originalSize + Vector2.one * .5f) < .02f, "Real corner drag resizes platform");
            app.Undo();
            var before = app.Document.platforms.Count;
            app.AddPlatform();
            Require(app.Document.platforms.Count == before + 1, "Add platform");
            app.SetCoordinate(0, "5.25");
            app.SetCoordinate(1, "6.5");
            app.SetCoordinate(2, "4.75");
            Require(Mathf.Approximately(app.SelectedPlatform.position.x, 5.25f) && Mathf.Approximately(app.SelectedPlatform.size.x, 4.75f), "Position and resize via fields");
            app.DuplicatePlatform();
            Require(app.Document.platforms.Count == before + 2, "Duplicate platform");
            app.DeleteSelection();
            app.Undo();
            Require(app.Document.platforms.Count == before + 2, "Undo deletion");
            app.Redo();
            Require(app.Document.platforms.Count == before + 1, "Redo deletion");
            var slider = app.UI.Settings[0];
            var sliderRect = (RectTransform)slider.transform;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, sliderRect.TransformPoint(new Vector2(sliderRect.rect.xMin + sliderRect.rect.width * .6f, sliderRect.rect.center.y)));
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, sliderRect.TransformPoint(new Vector2(sliderRect.rect.xMin + sliderRect.rect.width * .8f, sliderRect.rect.center.y)));
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            app.Undo();
            Require(Mathf.Approximately(app.Document.jump.sensitivity, 1), "Real UI slider drag is a single undo step");
            app.ResetJump();
            var path = Path.Combine(output, "한글 맵 왕복.json");
            MapStore.Save(path, app.Document);
            var loaded = MapStore.Load(path);
            Require(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(app.Document), "Unicode path JSON round trip");
            var original = JsonUtility.ToJson(app.Document);
            app.StartTest(false);
            var deadline = Time.realtimeSinceStartup + 4;
            while (!app.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            Require(app.Motor.Grounded, "Bird settles on solid ground");
            Require(!app.DeveloperMode && !app.UI.DeveloperHUDVisible && !app.UI.DeveloperAimVisible, "Normal mode hides all developer readouts");
            Require(!app.UI.DragGuideVisible, "Gesture guide stays hidden before a press");
            var press = new Vector2(Screen.width * .5f, Screen.height * .55f);
            var release = press + new Vector2(70, -180) * Screen.height / 1080f;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 1 });
            yield return null;
            yield return null;
            Require(app.UI.DragGuideVisible && app.ChargeStart == press && !app.DeveloperMode
                && !app.UI.DeveloperHUDVisible && !app.UI.DeveloperAimVisible,
                "Normal play shows the held gesture with a fixed click origin");
            Require(!app.UI.PointerOverUI(press) && !app.UI.PointerOverUI((press + release) / 2),
                "Gesture markers and segment never intercept mouse input");
            Capture(app, "normal-drag.png");
            var normalAim = app.CurrentAim.Value;
            app.ToggleDeveloperMode();
            Require(app.UI.DeveloperHUDVisible && app.UI.DeveloperAimVisible && !app.UI.DragGuideVisible,
                "Developer mode replaces the simple gesture with diagnostics");
            Require(!app.UI.PointerOverUI(new Vector2(180, Screen.height - 200)), "Developer panel allows mouse clicks through");
            app.ToggleDeveloperMode();
            Require(app.UI.DragGuideVisible && !app.UI.DeveloperAimVisible
                && Vector2.Distance(app.CurrentAim.Value.Velocity, normalAim.Velocity) < .0001f,
                "Returning to normal mode restores the held gesture without changing the jump");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 3 });
            yield return null;
            yield return null;
            Require(!app.IsCharging && !app.UI.DragGuideVisible && app.Motor.Jumps == 0,
                "Right click immediately clears the guide and cancels the jump");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release });
            yield return null;
            yield return null;
            app.ToggleDeveloperMode();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 1 });
            yield return null;
            yield return null;
            var expectedAim = LaunchMath.Evaluate(press, release, Screen.height, app.Document.jump);
            Require(app.CurrentAim.HasValue && Mathf.Abs(app.CurrentAim.Value.Strength - expectedAim.Strength) < .0001f
                && app.UI.DeveloperAimVisible, "Live drag displays actual launch strength and geometry");
            Require(app.UI.DeveloperLiveText.Contains((expectedAim.Strength * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%"), "Tuning panel shows actual percentage");
            Capture(app, "developer-drag.png");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 0 });
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(app.Motor.Jumps == 1 && !app.Motor.Grounded && app.Motor.Body.linearVelocity.y > 0, "Real pointer drag launches through game input");
            Require(!app.UI.DragGuideVisible, "Released launch leaves no gesture guide");
            Require(app.LastLaunch.HasValue && app.LastJumpNumber == 1 && !app.CurrentAim.HasValue
                && Vector2.Distance(app.LastLaunch.Value.Velocity, expectedAim.Velocity) < .0001f,
                "Committed jump retains exact displayed angle and strength");
            Require(!app.Motor.QueueLaunch(new Vector2(0, 18)), "Airborne launch rejected");
            yield return new WaitForSecondsRealtime(.25f);
            Capture(app, "developer-last-jump.png");
            app.ToggleDeveloperMode();
            Require(!app.UI.DeveloperHUDVisible && !app.UI.DeveloperAimVisible && app.LastLaunch.HasValue,
                "Disabling developer mode hides overlays and retains last jump");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            Require(!app.IsCharging && !app.UI.DragGuideVisible, "Airborne presses do not show a gesture guide");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press });
            yield return null;
            Capture(app, "play.png");
            app.ToggleDeveloperMode();
            var ledge = app.Document.platforms[1];
            app.Motor.Body.position = ledge.position + Vector2.up * 2;
            app.Motor.Body.linearVelocity = Vector2.down * 4;
            deadline = Time.realtimeSinceStartup + 3;
            yield return new WaitForSecondsRealtime(.1f);
            while (!app.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            Require(app.Motor.Grounded && app.Motor.Body.position.y > ledge.position.y + .5f, "Falling bird lands on lower platform");
            app.Motor.Body.position = ledge.position + Vector2.down;
            app.Motor.Body.linearVelocity = Vector2.up * 18;
            yield return new WaitForSecondsRealtime(.15f);
            Require(app.Motor.Body.position.y < ledge.position.y - .4f && app.Motor.Body.linearVelocity.y <= 0, "Platform underside is solid");
            app.Motor.Body.position = ledge.position + Vector2.left * (ledge.size.x / 2 + 1);
            app.Motor.Body.linearVelocity = new Vector2(18, 1);
            yield return new WaitForSecondsRealtime(.15f);
            Require(app.Motor.Body.position.x < ledge.position.x - ledge.size.x / 2 - .3f && !app.Motor.Grounded, "Side collision does not count as grounded");
            app.Motor.Body.position = new Vector2(0, app.Document.killY - 2);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(app.Motor.Falls == 1 && app.Motor.Body.position.y > app.Document.killY, "Fall boundary respawns at start");
            app.StopTest();
            Require(JsonUtility.ToJson(app.Document) == original, "Play mode preserves authored document");
            Require(app.LastLaunch.HasValue && app.UI.DeveloperHUDVisible, "Last jump stays visible after returning to editor");
            app.SetJumpSetting(1, 24);
            Require(app.LastLaunch.Value.MaxSpeed == 18 && app.Document.jump.maxSpeed == 24,
                "Editing settings preserves the last jump's original settings");
            app.Undo();
            Capture(app, "developer-editor.png");
            app.StartTest(false);
            deadline = Time.realtimeSinceStartup + 3;
            while (!app.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press + Vector2.up * 80, buttons = 1 });
            yield return null;
            yield return null;
            Require(app.CurrentAim.HasValue && app.CurrentAim.Value.Status == LaunchStatus.WrongDirection
                && app.CurrentAim.Value.Strength == 0, "Upward drag is labeled as a cancelled zero-strength launch");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press + Vector2.up * 80, buttons = 0 });
            yield return null;
            yield return new WaitForFixedUpdate();
            Require(app.Motor.Jumps == 0 && app.LastJumpNumber == 1, "Cancelled drag never replaces committed jump record");
            app.StopTest();
            slider = app.UI.Settings[4];
            sliderRect = (RectTransform)slider.transform;
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, sliderRect.TransformPoint(new Vector2(sliderRect.rect.xMin + sliderRect.rect.width * .2f, sliderRect.rect.center.y)));
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, sliderRect.TransformPoint(new Vector2(sliderRect.rect.xMin + sliderRect.rect.width * .35f, sliderRect.rect.center.y)));
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Require(Mathf.Approximately(app.Document.jump.minStrength, .35f) && slider.wholeNumbers
                && app.UI.MinimumStrengthText == "35%", "Minimum strength slider uses whole percentages");
            app.Undo();
            Require(app.Document.jump.minStrength == 0 && app.UI.MinimumStrengthText == "0%", "Minimum slider drag is one undo step");
            app.Redo();
            Require(Mathf.Approximately(app.Document.jump.minStrength, .35f), "Redo restores minimum strength");
            MapStore.Save(path, app.Document);
            Require(Mathf.Approximately(MapStore.Load(path).jump.minStrength, .35f), "Minimum strength survives JSON save and load");
            yield return new WaitForSecondsRealtime(1);
            Require(Mathf.Approximately(MapStore.LoadRecovery().document.jump.minStrength, .35f), "Recovery includes minimum strength");
            Capture(app, "minimum-strength-editor.png");
            app.StartTest(false);
            deadline = Time.realtimeSinceStartup + 3;
            while (!app.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            Require(app.Motor.Grounded, "Minimum strength test starts grounded");
            release = press + Vector2.down * 24 * Screen.height / 1080f;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 1 });
            yield return null;
            yield return null;
            Require(app.CurrentAim.HasValue && app.CurrentAim.Value.MinimumApplied
                && Mathf.Abs(app.CurrentAim.Value.InputStrength - .1f) < .0001f
                && Mathf.Abs(app.CurrentAim.Value.Strength - .35f) < .0001f,
                "Weak 10 percent input previews minimum 35 percent jump");
            Require(app.UI.DeveloperStateText.Contains("최소 강도 적용")
                && app.UI.DeveloperDetailsText.Contains("입력 강도 10% → 적용 35%"), "Developer panel explains the minimum strength boost");
            Capture(app, "minimum-strength-drag.png");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 0 });
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(app.Motor.Jumps == 1 && app.Motor.Body.linearVelocity.y > 0 && app.LastLaunch.HasValue
                && Mathf.Abs(app.LastLaunch.Value.Speed - 6.3f) < .0001f
                && Mathf.Abs(app.LastLaunch.Value.MinStrength - .35f) < .0001f,
                "Real mouse release launches with minimum strength and records the applied speed");
            app.StopTest();
            app.ResetJump();
            Require(app.Document.jump.minStrength == 0 && app.UI.MinimumStrengthText == "0%"
                && Mathf.Abs(app.LastLaunch.Value.MinStrength - .35f) < .0001f,
                "Reset restores zero minimum while preserving the last jump's original minimum");
            app.ToggleDeveloperMode();
            app.StartTest(false);
            deadline = Time.realtimeSinceStartup + 3;
            while (!app.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release, buttons = 1 });
            yield return null;
            yield return null;
            Require(app.UI.DragGuideVisible, "Normal gesture guide works after developer tuning");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = release });
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(app.Motor.Jumps == 1 && !app.UI.DragGuideVisible && !app.UI.DeveloperHUDVisible,
                "Normal release launches the bird and hides the guide");
            app.RestartTest();
            deadline = Time.realtimeSinceStartup + 3;
            while (!app.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press, buttons = 1 });
            yield return null;
            yield return null;
            app.RestartTest();
            Require(!app.IsCharging && !app.UI.DragGuideVisible, "Restart clears a held guide immediately");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = press });
            yield return null;
            app.StopTest();
            Require(!app.UI.DragGuideVisible, "Editor never shows the play gesture guide");
            var preserved = JsonUtility.ToJson(app.Document);
            try { MapStore.Parse("{broken-json}"); Require(false, "Invalid JSON should throw"); }
            catch (InvalidDataException) { Require(JsonUtility.ToJson(app.Document) == preserved, "Invalid file preserves document"); }
            app.AddPlatform();
            app.StartTest(true);
            var selected = app.SelectedPlatform;
            Require(Mathf.Abs(app.Motor.Body.position.y - (selected.position.y + selected.size.y / 2 + .52f)) < .05f, "Test starts above selected platform");
            app.StopTest();
            app.StartTest(false);
            app.Motor.Body.position = app.Document.goal;
            yield return new WaitForSecondsRealtime(.15f);
            Require(app.Motor.ReachedMoon && app.UI.HasDialog, "Moon arrival opens success dialog");
            app.StopTest();
            yield return new WaitForSecondsRealtime(1);
            Require(MapStore.LoadRecovery() != null, "Automatic recovery session written");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-moonbirdDialogSmoke") >= 0)
            {
                // The Windows QA driver fills these real native dialogs from a separate process.
                var saved = WindowsFileDialog.Choose(true, Path.Combine(output, "파일 대화상자 한글 검증.json"));
                Require(!string.IsNullOrEmpty(saved), "Native save dialog returns chosen path");
                MapStore.Save(saved, app.Document);
                var opened = WindowsFileDialog.Choose(false, saved);
                Require(!string.IsNullOrEmpty(opened), "Native open dialog returns chosen path");
                Require(JsonUtility.ToJson(MapStore.Load(opened)) == JsonUtility.ToJson(app.Document), "Native dialog Unicode file round trip");
            }
        }

        static IEnumerator Drag(Mouse mouse, Vector2 from, Vector2 to)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = from, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = to, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = to, buttons = 0 });
            yield return null;
            yield return null;
        }

        static Vector2 ControlCenter(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        static IEnumerator Tap(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        void Capture(StudioApp app, string filename)
        {
            // A hidden window may have no presented backbuffer. Render the actual scene/UI
            // explicitly, preserving the editor viewport's world-to-pixel mapping.
            var camera = app.WorldCamera;
            var rect = camera.rect;
            var pixelRect = camera.pixelRect;
            var position = camera.transform.position;
            var zoom = camera.orthographicSize;
            var canvas = app.UI.Canvas;
            var renderMode = canvas.renderMode;
            var units = zoom * 2 / pixelRect.height;
            var texture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                camera.rect = new Rect(0, 0, 1, 1);
                camera.orthographicSize = units * Screen.height / 2;
                var offset = new Vector2(Screen.width / 2f, Screen.height / 2f) - pixelRect.center;
                camera.transform.position = position + new Vector3(offset.x * units, offset.y * units);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                var request = new RenderPipeline.StandardRequest { destination = texture };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture;
                var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                var center = image.GetPixel(image.width / 2, image.height / 2);
                Require(center.maxColorComponent > .02f, "Rendered capture is not black: " + filename);
                File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG());
                Destroy(image);
            }
            finally
            {
                RenderTexture.active = previous;
                canvas.renderMode = renderMode;
                canvas.worldCamera = null;
                camera.rect = rect;
                camera.orthographicSize = zoom;
                camera.transform.position = position;
                texture.Release();
                Destroy(texture);
                Canvas.ForceUpdateCanvases();
            }
        }
    }
}
