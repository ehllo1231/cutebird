using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Moonbird
{
    public sealed class StudioApp : MonoBehaviour
    {
        public const string Version = "0.5.0";
        public MapDocument Document { get; private set; }
        public DocumentHistory History { get; } = new DocumentHistory();
        public bool Dirty { get; private set; }
        public bool GridVisible { get; private set; } = true;
        public bool SnapEnabled { get; private set; }
        public bool IsPlaying { get; private set; }
        public BirdMotor Motor { get; private set; }
        public Camera WorldCamera { get; private set; }
        public WorldView World { get; private set; }
        public StudioUI UI { get; private set; }
        public string FilePath { get; private set; }
        public bool DeveloperMode { get; private set; }
        public bool IsCharging => charging;
        public Vector2 ChargeStart => chargeStart;
        public Vector2 PointerPosition { get; private set; }
        public LaunchSolution? CurrentAim { get; private set; }
        public LaunchSolution? LastLaunch { get; private set; }
        public int LastJumpNumber { get; private set; }
        public EditorTool ActiveEditorTool { get; private set; } = EditorTool.Select;
        LaunchSolution? pendingLaunch;

        enum SelectedKind { None, Platform, Spawn, Goal }
        SelectedKind selected;
        string selectedId;
        string savedState;
        float recoveryDue = -1;
        bool settingsGesture, movedInGesture, dragging, resizing, panning, panWithMiddle, charging, quitApproved, refreshUI, skipRecovery;
        int resizeCorner;
        Vector2 pointerStart, positionStart, sizeStart, panStart, chargeStart;
        Vector3 cameraStart, editorCameraPosition;
        float editorZoom;
        AudioSource audioSource;
        AudioClip jumpSound, landSound, finishSound;
        string lastStatus;

        public PlatformData SelectedPlatform => selected == SelectedKind.Platform
            ? Document.platforms.Find(p => p.id == selectedId) : null;
        public string SelectionName => selected == SelectedKind.Spawn ? "아기 새의 시작점"
            : selected == SelectedKind.Goal ? "도착할 달"
            : SelectedPlatform != null ? "발판 " + (Document.platforms.IndexOf(SelectedPlatform) + 1).ToString("00") : "발판을 선택해 주세요";
        public Rect? SelectionBounds
        {
            get
            {
                var platform = SelectedPlatform;
                if (platform != null) return new Rect(platform.position - platform.size / 2, platform.size);
                if (selected == SelectedKind.Spawn) return new Rect(Document.spawn - Vector2.one * .55f, Vector2.one * 1.1f);
                if (selected == SelectedKind.Goal) return new Rect(Document.goal - Vector2.one * .8f, Vector2.one * 1.6f);
                return null;
            }
        }

        void Awake()
        {
            var smokeOutput = RuntimeSmoke.GetOutputDirectory();
            if (smokeOutput != null) MapStore.StorageRoot = Path.Combine(smokeOutput, "UserData");
            Application.targetFrameRate = 120;
            Application.runInBackground = smokeOutput != null;
            Time.fixedDeltaTime = 1f / 60;
            InputSystem.settings.scrollDeltaBehavior = InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
            var recovery = MapStore.LoadRecovery();
            Document = recovery?.document ?? MapDocument.CreateDefault();
            FilePath = recovery?.filePath;
            savedState = recovery?.savedState ?? "";
            Dirty = JsonUtility.ToJson(Document) != savedState;
            var cameraObject = new GameObject("World camera", typeof(Camera), typeof(AudioListener));
            WorldCamera = cameraObject.GetComponent<Camera>();
            WorldCamera.tag = "MainCamera";
            WorldCamera.orthographic = true;
            WorldCamera.orthographicSize = 8;
            WorldCamera.transform.position = new Vector3(0, 5, -10);
            WorldCamera.backgroundColor = WorldView.Sky;
            WorldCamera.clearFlags = CameraClearFlags.SolidColor;
            WorldCamera.nearClipPlane = .1f;
            WorldCamera.farClipPlane = 300;
            World = new GameObject("Authored world").AddComponent<WorldView>();
            World.Initialize();
            World.Apply(Document, true);
            Motor = World.BirdBody.gameObject.AddComponent<BirdMotor>();
            Motor.Initialize(World);
            Motor.Respawned += () => { CancelCharge(); pendingLaunch = null; };
            BuildAudio();
            Motor.Jumped += () => audioSource.PlayOneShot(jumpSound, .28f);
            Motor.Jumped += RecordLaunch;
            Motor.Landed += () => audioSource.PlayOneShot(landSound, .18f);
            Motor.Finished += Complete;
            UI = new StudioUI(this);
            UI.Refresh();
            Canvas.ForceUpdateCanvases();
            UpdateViewport();
            SetStatus(recovery != null ? "마지막 작업을 복구했어요. Ctrl+S로 JSON 파일에 저장할 수 있어요." : "기본 코스가 준비됐어요. 발판을 바꾸거나 바로 테스트해 보세요.");
            Application.wantsToQuit += WantsToQuit;
            gameObject.AddComponent<RuntimeSmoke>();
        }

        void Update()
        {
            if (UI == null) return;
            UpdateViewport();
            if (recoveryDue >= 0 && Time.unscaledTime >= recoveryDue) WriteRecovery();
            if (UI.HasDialog) return;
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) ToggleDeveloperMode();
            if (IsPlaying)
            {
                HandlePlayInput();
                UI.UpdateTestInfo(Motor.Grounded, Motor.ReachedMoon);
            }
            else
            {
                HandleEditorInput();
                World.UpdateEditorOverlays(WorldCamera, SelectionBounds, GridVisible,
                    SelectedPlatform != null && ActiveEditorTool == EditorTool.Select && !panning);
                UI.UpdateNavigation();
            }
            UI.UpdateDeveloper();
        }

        void LateUpdate()
        {
            if (refreshUI) { refreshUI = false; UI.Refresh(); }
            if (!IsPlaying || !Motor.Playing) return;
            var target = new Vector3(Motor.Body.position.x, Motor.Body.position.y + 2, -10);
            WorldCamera.transform.position = Vector3.Lerp(WorldCamera.transform.position, target, 1 - Mathf.Exp(-Time.deltaTime * 5));
        }

        void UpdateViewport()
        {
            if (IsPlaying) WorldCamera.rect = new Rect(0, 0, 1, 1);
            else
            {
                var scale = UI.Scale;
                var width = Mathf.Max(1, Screen.width - StudioUI.SideWidth * scale);
                var bottom = StudioUI.FooterHeight * scale;
                var height = Mathf.Max(1, Screen.height - (StudioUI.HeaderHeight + StudioUI.FooterHeight) * scale);
                WorldCamera.rect = new Rect(0, bottom / Screen.height, width / Screen.width, height / Screen.height);
            }
        }

        void HandlePlayInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { StopTest(); return; }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame && !UI.TextFocused) { RestartTest(); return; }
            var mouse = Mouse.current;
            if (mouse == null || Motor.ReachedMoon) return;
            var position = mouse.position.ReadValue();
            PointerPosition = position;
            if (mouse.rightButton.wasPressedThisFrame) CancelCharge();
            if (mouse.leftButton.wasPressedThisFrame && Motor.Grounded && WorldCamera.pixelRect.Contains(position) && !UI.PointerOverUI(position))
            {
                charging = true;
                chargeStart = position;
                Motor.ReadyPose = true;
            }
            if (charging && !Motor.Grounded) CancelCharge();
            if (charging) CurrentAim = LaunchMath.Evaluate(chargeStart, position, WorldCamera.pixelHeight, Document.jump);
            if (charging && mouse.leftButton.wasReleasedThisFrame)
            {
                if (WorldCamera.pixelRect.Contains(position) && !UI.PointerOverUI(position)
                    && CurrentAim.HasValue && CurrentAim.Value.IsValid && Motor.QueueLaunch(CurrentAim.Value.Velocity))
                    pendingLaunch = CurrentAim;
                CancelCharge();
            }
        }

        void RecordLaunch()
        {
            if (!pendingLaunch.HasValue) return;
            LastLaunch = pendingLaunch;
            LastJumpNumber++;
            pendingLaunch = null;
        }

        public void ToggleDeveloperMode()
        {
            DeveloperMode = !DeveloperMode;
            UI.UpdateDeveloper();
        }

        public void SetEditorTool(EditorTool tool)
        {
            if (IsPlaying) return;
            EndGestures();
            ActiveEditorTool = tool;
            UI.UpdateNavigation();
        }

        public void ZoomEditor(float steps)
        {
            if (!IsPlaying) ZoomAt(WorldCamera.pixelRect.center, steps);
        }

        void ZoomAt(Vector2 screen, float steps)
        {
            var before = ToWorld(screen);
            WorldCamera.orthographicSize = EditorNavigation.Zoom(WorldCamera.orthographicSize, steps);
            var difference = before - ToWorld(screen);
            WorldCamera.transform.position += new Vector3(difference.x, difference.y);
            UI.UpdateNavigation();
        }

        void HandleEditorInput()
        {
            var keyboard = Keyboard.current;
            var control = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed);
            if (keyboard != null && !UI.TextFocused)
            {
                if (control && keyboard.sKey.wasPressedThisFrame) SaveMap(keyboard.shiftKey.isPressed);
                else if (control && keyboard.oKey.wasPressedThisFrame) OpenMap();
                else if (control && keyboard.nKey.wasPressedThisFrame) NewMap();
                else if (control && keyboard.zKey.wasPressedThisFrame) { if (keyboard.shiftKey.isPressed) Redo(); else Undo(); }
                else if (control && keyboard.yKey.wasPressedThisFrame) Redo();
                else if (control && keyboard.dKey.wasPressedThisFrame) DuplicatePlatform();
                else if (!control && keyboard.vKey.wasPressedThisFrame) SetEditorTool(EditorTool.Select);
                else if (!control && keyboard.hKey.wasPressedThisFrame) SetEditorTool(EditorTool.Pan);
                else if (keyboard.deleteKey.wasPressedThisFrame) DeleteSelection();
                else if (keyboard.fKey.wasPressedThisFrame) FrameSelection();
                else if (keyboard.homeKey.wasPressedThisFrame) FrameAll();
                else if (keyboard.escapeKey.wasPressedThisFrame) { EndGestures(); Select(SelectedKind.None); }
            }
            if (UI.HasDialog) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            var screen = mouse.position.ReadValue();
            var inside = WorldCamera.pixelRect.Contains(screen) && !UI.PointerOverUI(screen);
            var point = ToWorld(screen);
            var wantsPan = ActiveEditorTool == EditorTool.Pan || (keyboard != null && keyboard.spaceKey.isPressed && !UI.TextFocused);
            if (inside && !dragging && !resizing && (mouse.middleButton.wasPressedThisFrame || (mouse.leftButton.wasPressedThisFrame && wantsPan)))
            {
                EventSystem.current.SetSelectedGameObject(null);
                EndSettingsGesture();
                panning = true;
                panWithMiddle = mouse.middleButton.wasPressedThisFrame;
                panStart = screen;
                cameraStart = WorldCamera.transform.position;
            }
            if (panning)
            {
                var units = WorldCamera.orthographicSize * 2 / WorldCamera.pixelHeight;
                var delta = (screen - panStart) * units;
                WorldCamera.transform.position = cameraStart - new Vector3(delta.x, delta.y);
                if (!(panWithMiddle ? mouse.middleButton.isPressed : mouse.leftButton.isPressed)) panning = false;
            }
            var scroll = mouse.scroll.ReadValue().y;
            if (inside && Mathf.Abs(scroll) > .001f && !dragging && !resizing && !panning)
            {
                ZoomAt(screen, scroll);
            }
            if (mouse.leftButton.wasPressedThisFrame && inside && !panning && !wantsPan)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EndSettingsGesture();
                movedInGesture = false;
                pointerStart = point;
                var bounds = SelectionBounds;
                var handle = WorldCamera.orthographicSize * 2 / WorldCamera.pixelHeight * 12;
                if (SelectedPlatform != null && bounds.HasValue)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var corner = new Vector2((i & 1) == 0 ? bounds.Value.xMin : bounds.Value.xMax,
                            (i & 2) == 0 ? bounds.Value.yMin : bounds.Value.yMax);
                        if (Vector2.Distance(corner, point) < handle)
                        {
                            resizing = true;
                            resizeCorner = i;
                            positionStart = SelectedPlatform.position;
                            sizeStart = SelectedPlatform.size;
                            return;
                        }
                    }
                }
                if (Vector2.Distance(point, Document.goal) < .9f) Select(SelectedKind.Goal);
                else if (Vector2.Distance(point, Document.spawn) < .7f) Select(SelectedKind.Spawn);
                else
                {
                    PlatformData hit = null;
                    for (var i = Document.platforms.Count - 1; i >= 0; i--)
                    {
                        var p = Document.platforms[i];
                        if (new Rect(p.position - p.size / 2, p.size).Contains(point)) { hit = p; break; }
                    }
                    Select(hit == null ? SelectedKind.None : SelectedKind.Platform, hit?.id);
                }
                if (SelectionBounds.HasValue)
                {
                    dragging = true;
                    positionStart = SelectionBounds.Value.center;
                }
            }
            if ((dragging || resizing) && mouse.leftButton.isPressed && Vector2.Distance(point, pointerStart) > .015f)
            {
                if (!movedInGesture) { History.Record(Document); movedInGesture = true; }
                if (resizing && SelectedPlatform != null)
                {
                    var fixedCorner = positionStart + new Vector2((resizeCorner & 1) == 0 ? sizeStart.x / 2 : -sizeStart.x / 2,
                        (resizeCorner & 2) == 0 ? sizeStart.y / 2 : -sizeStart.y / 2);
                    var moving = Snap(point);
                    var dx = Mathf.Clamp(((resizeCorner & 1) == 0 ? -1 : 1) * (moving.x - fixedCorner.x), .25f, 200);
                    var dy = Mathf.Clamp(((resizeCorner & 2) == 0 ? -1 : 1) * (moving.y - fixedCorner.y), .25f, 200);
                    SelectedPlatform.size = new Vector2(dx, dy);
                    SelectedPlatform.position = fixedCorner + new Vector2((resizeCorner & 1) == 0 ? -dx / 2 : dx / 2,
                        (resizeCorner & 2) == 0 ? -dy / 2 : dy / 2);
                }
                else SetSelectedPosition(Snap(positionStart + point - pointerStart));
                Changed();
            }
            if (mouse.leftButton.wasReleasedThisFrame) EndGestures();
        }

        Vector2 ToWorld(Vector2 screen) => WorldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
        Vector2 Snap(Vector2 value) => SnapEnabled ? new Vector2(Mathf.Round(value.x * 2) / 2, Mathf.Round(value.y * 2) / 2) : value;

        void Select(SelectedKind kind, string id = null) { selected = kind; selectedId = id; UI.Refresh(); }
        void SetSelectedPosition(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, -100000, 100000);
            position.y = Mathf.Clamp(position.y, -100000, 100000);
            if (SelectedPlatform != null) SelectedPlatform.position = position;
            else if (selected == SelectedKind.Spawn) Document.spawn = position;
            else if (selected == SelectedKind.Goal) Document.goal = position;
        }

        void Changed()
        {
            Dirty = JsonUtility.ToJson(Document) != savedState;
            recoveryDue = Time.unscaledTime + .75f;
            if (!IsPlaying) World.Apply(Document, true);
            UI.Refresh();
        }

        public void AddPlatform()
        {
            if (IsPlaying) return;
            if (Document.platforms.Count >= 2000) { SetStatus("발판은 최대 2000개까지 만들 수 있어요."); return; }
            EndGestures();
            History.Record(Document);
            var platform = new PlatformData(Snap(WorldCamera.transform.position), new Vector2(3, .45f));
            Document.platforms.Add(platform);
            Select(SelectedKind.Platform, platform.id);
            Changed();
            SetStatus("새 발판을 추가했어요. 드래그하거나 오른쪽에서 위치를 입력해 보세요.");
        }

        public void DuplicatePlatform()
        {
            var source = SelectedPlatform;
            if (IsPlaying || source == null || Document.platforms.Count >= 2000) return;
            EndGestures();
            History.Record(Document);
            var platform = new PlatformData(source.position + new Vector2(.5f, .5f), source.size);
            Document.platforms.Add(platform);
            Select(SelectedKind.Platform, platform.id);
            Changed();
        }

        public void DeleteSelection()
        {
            if (IsPlaying || SelectedPlatform == null) return;
            if (Document.platforms.Count == 1) { SetStatus("마지막 발판은 남겨 주세요."); return; }
            EndGestures();
            History.Record(Document);
            Document.platforms.Remove(SelectedPlatform);
            Select(SelectedKind.None);
            Changed();
        }

        public void SetMapName(string value)
        {
            if (IsPlaying || value == Document.name) return;
            value = value.Trim();
            if (value.Length == 0) { SetStatus("맵 이름을 입력해 주세요."); UI.Refresh(); return; }
            History.Record(Document);
            Document.name = value.Length > 120 ? value.Substring(0, 120) : value;
            Changed();
        }

        public void SetCoordinate(int index, string value)
        {
            if (IsPlaying || !SelectionBounds.HasValue) return;
            if (!TryNumber(value, out var n)) { SetStatus("올바른 숫자를 입력해 주세요."); UI.Refresh(); return; }
            var bounds = SelectionBounds.Value;
            var values = new[] { bounds.center.x, bounds.center.y, bounds.width, bounds.height };
            if (Mathf.Approximately(values[index], n)) return;
            if (index >= 2 && (SelectedPlatform == null || n < .25f || n > 200))
            { SetStatus("발판 크기는 0.25~200 사이로 입력해 주세요."); UI.Refresh(); return; }
            History.Record(Document);
            if (index < 2) { var position = bounds.center; position[index] = n; SetSelectedPosition(position); }
            else { var size = SelectedPlatform.size; size[index - 2] = n; SelectedPlatform.size = size; }
            Changed();
        }

        public void SetKillY(string value)
        {
            if (IsPlaying) return;
            if (!TryNumber(value, out var n)) { SetStatus("낙하 한계는 숫자로 입력해 주세요."); UI.Refresh(); return; }
            if (Mathf.Approximately(n, Document.killY)) return;
            History.Record(Document);
            Document.killY = n;
            Changed();
        }

        static bool TryNumber(string text, out float value)
        {
            return (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && MapDocument.Finite(value);
        }

        public void BeginSettingsGesture()
        {
            if (IsPlaying || settingsGesture) return;
            settingsGesture = true;
            movedInGesture = false;
        }
        public void EndSettingsGesture() { settingsGesture = false; movedInGesture = false; }

        public void SetJumpSetting(int index, float value)
        {
            if (IsPlaying) return;
            var current = new[] { Document.jump.sensitivity, Document.jump.maxSpeed, Document.jump.gravity, Document.jump.maxAngle, Document.jump.minStrength * 100 };
            value = index >= 3 ? Mathf.Round(value) : Mathf.Round(value * 100) / 100;
            if (Mathf.Approximately(current[index], value)) return;
            if (!settingsGesture || !movedInGesture) { History.Record(Document); movedInGesture = true; }
            if (index == 0) Document.jump.sensitivity = Mathf.Clamp(value, .1f, 3);
            else if (index == 1) Document.jump.maxSpeed = Mathf.Clamp(value, 5, 40);
            else if (index == 2) Document.jump.gravity = Mathf.Clamp(value, 5, 60);
            else if (index == 3) Document.jump.maxAngle = Mathf.Clamp(value, 0, 85);
            else if (index == 4) Document.jump.minStrength = Mathf.Clamp01(value / 100);
            Changed();
        }

        public void ResetJump()
        {
            if (IsPlaying) return;
            History.Record(Document);
            Document.jump = new JumpSettings();
            Changed();
        }

        public void Undo()
        {
            if (IsPlaying || !History.CanUndo) return;
            EndGestures();
            Document = History.Undo(Document);
            ValidateSelection();
            Changed();
        }
        public void Redo()
        {
            if (IsPlaying || !History.CanRedo) return;
            EndGestures();
            Document = History.Redo(Document);
            ValidateSelection();
            Changed();
        }
        void ValidateSelection() { if (selected == SelectedKind.Platform && SelectedPlatform == null) { selected = SelectedKind.None; selectedId = null; } }

        public void ToggleGrid() { GridVisible = !GridVisible; UI.Refresh(); }
        public void ToggleSnap() { SnapEnabled = !SnapEnabled; UI.Refresh(); }

        public void FrameSelection()
        {
            if (!SelectionBounds.HasValue) { FrameAll(); return; }
            var r = SelectionBounds.Value;
            WorldCamera.transform.position = new Vector3(r.center.x, r.center.y, -10);
            WorldCamera.orthographicSize = Mathf.Clamp(Mathf.Max(r.height * 1.3f, r.width / WorldCamera.aspect), 3, 70);
        }
        public void FrameAll()
        {
            var min = Vector2.Min(Document.spawn, Document.goal);
            var max = Vector2.Max(Document.spawn, Document.goal);
            foreach (var p in Document.platforms) { min = Vector2.Min(min, p.position - p.size / 2); max = Vector2.Max(max, p.position + p.size / 2); }
            var center = (min + max) / 2;
            WorldCamera.transform.position = new Vector3(center.x, center.y, -10);
            WorldCamera.orthographicSize = Mathf.Clamp(Mathf.Max((max.y - min.y) / 2 + 2, (max.x - min.x) / (2 * WorldCamera.aspect) + 2), 3, 70);
        }

        public void StartTest(bool fromSelection)
        {
            if (IsPlaying) return;
            EventSystem.current.SetSelectedGameObject(null);
            EndGestures();
            if (!Document.IsValid(out var error)) { SetStatus(error); return; }
            var platform = SelectedPlatform;
            if (fromSelection && platform == null) return;
            var spawn = fromSelection ? platform.position + Vector2.up * (platform.size.y / 2 + .52f) : Document.spawn;
            if (spawn.y <= Document.killY) { SetStatus("테스트 시작점이 낙하 한계보다 높아야 해요."); return; }
            editorCameraPosition = WorldCamera.transform.position;
            editorZoom = WorldCamera.orthographicSize;
            WriteRecovery();
            IsPlaying = true;
            World.Apply(Document, false);
            UI.SetPlaying(true);
            UpdateViewport();
            WorldCamera.orthographicSize = 6;
            WorldCamera.transform.position = new Vector3(spawn.x, spawn.y + 2, -10);
            Motor.Begin(Document, spawn);
        }

        public void StopTest()
        {
            if (!IsPlaying) return;
            UI.CloseDialog();
            CancelCharge();
            Motor.Stop();
            pendingLaunch = null;
            IsPlaying = false;
            World.Apply(Document, true);
            UI.SetPlaying(false);
            WorldCamera.transform.position = editorCameraPosition;
            WorldCamera.orthographicSize = editorZoom;
            UpdateViewport();
            UI.Refresh();
            SetStatus("편집으로 돌아왔어요. 배치와 점프 설정은 그대로예요.");
        }

        public void RestartTest()
        {
            if (!IsPlaying) return;
            if (Motor.ReachedMoon)
            {
                var origin = Document.spawn;
                Motor.Begin(Document, origin);
            }
            else Motor.Respawn();
            CancelCharge();
            WorldCamera.transform.position = new Vector3(Motor.Body.position.x, Motor.Body.position.y + 2, -10);
        }

        void Complete()
        {
            CancelCharge();
            audioSource.PlayOneShot(finishSound, .3f);
            UI.Dialog("드디어, 달에 닿았어!", "작은 날개로 여기까지 왔어요.\n점프 " + Motor.Jumps + "번  ·  바닥 밖으로 떨어진 횟수 " + Motor.Falls + "번",
                ("편집으로 돌아가기", StopTest), ("처음부터 다시", RestartTest));
        }

        public void NewMap()
        {
            if (IsPlaying) return;
            ConfirmUnsaved(() => ReplaceDocument(MapDocument.CreateDefault(), null, ""));
        }
        public void OpenMap()
        {
            if (IsPlaying) return;
            ConfirmUnsaved(() =>
            {
                EndGestures();
                try
                {
                    var path = WindowsFileDialog.Choose(false, FilePath);
                    if (string.IsNullOrEmpty(path)) return;
                    var loaded = MapStore.Load(path);
                    ReplaceDocument(loaded, path, JsonUtility.ToJson(loaded));
                    SetStatus("맵을 열었어요: " + Path.GetFileName(path));
                }
                catch (Exception e) { ShowError("맵을 열지 못했어요", e); }
            });
        }

        public bool SaveMap(bool saveAs)
        {
            if (IsPlaying) return false;
            EndGestures();
            try
            {
                var path = FilePath;
                if (saveAs || string.IsNullOrEmpty(path)) path = WindowsFileDialog.Choose(true, FilePath);
                if (string.IsNullOrEmpty(path)) { SetStatus("저장을 취소했어요. 작업은 그대로 남아 있어요."); return false; }
                MapStore.Save(path, Document);
                FilePath = path;
                savedState = JsonUtility.ToJson(Document);
                Dirty = false;
                WriteRecovery();
                UI.Refresh();
                SetStatus("저장했어요: " + path);
                return true;
            }
            catch (Exception e) { ShowError("저장하지 못했어요", e); return false; }
        }

        public void ReplaceDocument(MapDocument document, string path, string saved)
        {
            if (!document.IsValid(out var error)) throw new InvalidDataException(error);
            EndGestures();
            Document = document;
            FilePath = path;
            savedState = saved;
            History.Clear();
            CurrentAim = LastLaunch = pendingLaunch = null;
            LastJumpNumber = 0;
            selected = SelectedKind.None;
            selectedId = null;
            Changed();
            FrameAll();
            WriteRecovery();
        }

        void ConfirmUnsaved(Action action)
        {
            EndGestures();
            if (!Dirty) { action(); return; }
            UI.Dialog("작업을 저장할까요?", "저장하지 않은 변경 사항이 있어요.\nJSON 파일에 저장하면 나중에 다시 열 수 있어요.",
                ("저장 후 계속", () => { if (SaveMap(false)) action(); }),
                ("저장 안 함", action), ("취소", () => { }));
        }

        bool WantsToQuit()
        {
            if (quitApproved || !Dirty) { WriteRecovery(); return true; }
            if (IsPlaying) StopTest();
            UI.Dialog("작업을 저장하고 종료할까요?", "저장하지 않은 변경 사항이 있어요.\n저장 안 함을 선택하면 이번 변경 사항을 버려요.",
                ("저장 후 종료", () => { if (SaveMap(false)) { quitApproved = true; Application.Quit(); } }),
                ("저장 안 함", () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(savedState)) MapStore.Recover(MapStore.Parse(savedState), FilePath, savedState);
                        else if (File.Exists(MapStore.RecoveryPath)) File.Delete(MapStore.RecoveryPath);
                        skipRecovery = true;
                        quitApproved = true;
                        Application.Quit();
                    }
                    catch (Exception e) { ShowError("작업을 정리하지 못했어요", e); }
                }), ("취소", () => { }));
            return false;
        }

        void ShowError(string title, Exception error)
        {
            var message = error is UnauthorizedAccessException ? "이 위치에 접근할 수 없어요. 다른 폴더를 선택해 주세요." : error.Message;
            SetStatus(title + ": " + message);
            UI.Dialog(title, message, ("확인", () => { }));
        }

        void WriteRecovery()
        {
            recoveryDue = -1;
            if (Document == null || skipRecovery) return;
            try { MapStore.Recover(Document, FilePath, savedState); }
            catch (Exception e) { SetStatus("자동 복구본 저장에 실패했어요. JSON으로 저장해 주세요. " + e.Message); }
        }

        public void SetStatus(string value) { lastStatus = value; if (UI != null) UI.Status(value); }
        public void RequestUIRefresh() { refreshUI = true; }
        internal void AutomationQuit() { quitApproved = true; Application.Quit(); }
        void CancelCharge()
        {
            charging = false;
            CurrentAim = null;
            if (Motor != null) Motor.ReadyPose = false;
            UI?.UpdateDragGuide();
        }
        void EndGestures() { dragging = resizing = panning = false; EndSettingsGesture(); CancelCharge(); }
        void OnApplicationFocus(bool focus) { if (!focus) { EndGestures(); WriteRecovery(); } }
        void OnApplicationPause(bool pause) { if (pause) { EndGestures(); WriteRecovery(); } }
        void OnDestroy() { Application.wantsToQuit -= WantsToQuit; Time.timeScale = 1; }
        void OnApplicationQuit() { WriteRecovery(); }

        void BuildAudio()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            jumpSound = Tone("Jump", 640, .12f);
            landSound = Tone("Land", 270, .09f);
            finishSound = Tone("Moon", 880, .6f);
        }
        static AudioClip Tone(string name, float frequency, float duration)
        {
            const int rate = 22050;
            var samples = new float[(int)(rate * duration)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                var envelope = Mathf.Pow(1 - i / (float)samples.Length, 2);
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * envelope * .3f;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
