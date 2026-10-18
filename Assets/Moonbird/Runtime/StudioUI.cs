using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Moonbird
{
    public sealed class StudioUI
    {
        public const float HeaderHeight = 72, FooterHeight = 30, SideWidth = 326;
        public Canvas Canvas { get; private set; }
        public bool HasDialog => dialog != null;
        public bool TextFocused => EventSystem.current && EventSystem.current.currentSelectedGameObject
            && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() is InputField input && input.isFocused;
        public float Scale => Canvas.scaleFactor;
        public InputField NameField { get; private set; }
        public readonly InputField[] Coordinates = new InputField[4];
        public readonly Slider[] Settings = new Slider[5];

        readonly StudioApp app;
        readonly Font font;
        readonly Color panel = WorldView.Hex("151E32"), ink = WorldView.Hex("E9EAF3"), muted = WorldView.Hex("95A2BD"), fieldColor = WorldView.Hex("253149");
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        GameObject editRoot, playRoot, dialog;
        Text selectionTitle, status, docTitle, snapText, gridText, selectionHint, testInfo;
        readonly Text[] settingValues = new Text[5];
        InputField killField;
        Button undo, redo, duplicate, delete, selectedTest;
        Button selectTool, panTool;
        Text navigationHint, navigationZoom;
        GameObject developerRoot;
        RectTransform developerPanel, developerPower;
        DeveloperAimGraphic aimGraphic;
        DragGuideGraphic dragGuide;
        Text developerEditToggle, developerPlayToggle, developerState, developerLive, developerDetails;
        Text developerSettings, developerLastTitle, developerLast, developerLastDetails;
        internal bool DeveloperHUDVisible => developerRoot.activeSelf;
        internal bool DeveloperAimVisible => aimGraphic.gameObject.activeSelf;
        internal bool DragGuideVisible => dragGuide.gameObject.activeInHierarchy;
        internal string DeveloperLiveText => developerLive.text;
        internal string DeveloperDetailsText => developerDetails.text;
        internal string DeveloperStateText => developerState.text;
        internal string MinimumStrengthText => settingValues[4].text;
        internal Button DeveloperEditorButton => developerEditToggle.transform.parent.GetComponent<Button>();
        internal Button SelectToolButton => selectTool;
        internal Button PanToolButton => panTool;

        public StudioUI(StudioApp app)
        {
            this.app = app;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Segoe UI", "Arial" }, 18);
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("Moonbird Studio UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas = canvasGo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = .5f;
            var events = new GameObject("UI events", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            editRoot = Full(canvasGo.transform, "Editor");
            playRoot = Full(canvasGo.transform, "Test");
            BuildEditor();
            BuildTest();
            BuildDeveloper();
            SetPlaying(false);
        }

        void BuildEditor()
        {
            var header = Panel(editRoot.transform, "Header", panel);
            Stretch(header, 0, 1, 1, 1, 0, -HeaderHeight, 0, 0);
            Label(header, "달을 꿈꾸는 새", 22, 9, 270, 30, 21, ink, FontStyle.Bold);
            Label(header, "MOONBIRD STUDIO  v" + StudioApp.Version + "  ·  F3 개발자 모드", 23, 43, 320, 20, 10, muted);
            var tools = Box(header, "Toolbar", 0, 0, 840, HeaderHeight);
            tools.anchorMin = tools.anchorMax = new Vector2(1, 1);
            tools.pivot = new Vector2(1, 1);
            tools.anchoredPosition = new Vector2(-16, 0);
            var x = 0f;
            ToolbarButton(tools, "새 맵", ref x, 62, app.NewMap);
            ToolbarButton(tools, "열기", ref x, 58, app.OpenMap);
            ToolbarButton(tools, "저장", ref x, 58, () => app.SaveMap(false));
            ToolbarButton(tools, "다른 이름 저장", ref x, 106, () => app.SaveMap(true));
            undo = ToolbarButton(tools, "되돌림", ref x, 56, app.Undo);
            redo = ToolbarButton(tools, "다시", ref x, 44, app.Redo);
            x += 12;
            selectedTest = ToolbarButton(tools, "선택 발판 테스트", ref x, 128, () => app.StartTest(true));
            ToolbarButton(tools, "▶  처음부터 테스트", ref x, 148, () => app.StartTest(false), WorldView.Mint);

            var sidebar = Panel(editRoot.transform, "Inspector", panel);
            Stretch(sidebar, 1, 0, 1, 1, -SideWidth, FooterHeight, 0, -HeaderHeight);
            var viewport = Full(sidebar, "Scroll viewport");
            viewport.AddComponent<RectMask2D>();
            var content = Box(viewport.transform, "Settings", 0, 0, SideWidth, 1101);
            var scroll = sidebar.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;
            developerEditToggle = Button(content, "개발자 모드 OFF · F3", 20, 12, 286, 34,
                app.ToggleDeveloperMode, WorldView.Yellow).GetComponentInChildren<Text>();
            developerEditToggle.fontSize = 14;
            Section(content, "MAP  /  작업 중인 맵", 57);
            NameField = Input(content, 20, 84, 286, value => app.SetMapName(value));
            NameField.characterLimit = 120;
            Section(content, "OBJECT  /  선택한 오브젝트", 138);
            selectionTitle = Label(content, "발판을 선택해 주세요", 20, 166, 286, 25, 15, WorldView.Accent, FontStyle.Bold);
            selectionHint = Label(content, "발판 · 시작점 · 달을 클릭해서 편집", 20, 196, 286, 22, 11, muted);
            Button(content, "+ 발판", 20, 229, 88, 32, app.AddPlatform, WorldView.Accent);
            duplicate = Button(content, "복제", 116, 229, 88, 32, app.DuplicatePlatform);
            delete = Button(content, "삭제", 212, 229, 94, 32, app.DeleteSelection);
            var names = new[] { "위치 X", "위치 Y", "너비", "높이" };
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var column = i % 2;
                var row = i / 2;
                Label(content, names[i], 20 + column * 148, 278 + row * 70, 130, 20, 11, muted);
                Coordinates[i] = Input(content, 20 + column * 148, 301 + row * 70, 138, value => app.SetCoordinate(index, value));
            }
            Section(content, "JUMP  /  손에 맞는 점프", 431);
            Label(content, "개발자 모드로 각도와 강도를 확인해요.", 20, 459, 286, 20, 11, muted);
            var titles = new[] { "점프 감도", "최대 발사 속도", "중력", "좌우 최대 각도 · 수직 기준", "최소 점프 강도" };
            var mins = new[] { .1f, 5, 5, 0, 0 };
            var maxs = new[] { 3f, 40, 60, 85, 100 };
            var order = new[] { 0, 1, 4, 2, 3 };
            for (var row = 0; row < order.Length; row++)
            {
                var index = order[row];
                var y = 494 + row * 71;
                Label(content, titles[index], 20, y, 225, 22, 12, ink);
                settingValues[index] = Label(content, "", 244, y, 62, 22, 12, WorldView.Mint);
                settingValues[index].alignment = TextAnchor.MiddleRight;
                Settings[index] = Slider(content, 24, y + 27, 278, mins[index], maxs[index], value => app.SetJumpSetting(index, value));
                Settings[index].wholeNumbers = index == 4;
                var gesture = Settings[index].GetComponent<StudioSlider>();
                gesture.Begin = app.BeginSettingsGesture;
                gesture.End = app.EndSettingsGesture;
            }
            Button(content, "점프 기본값 복원", 20, 854, 286, 32, app.ResetJump);
            Section(content, "WORKSPACE  /  배치 도구", 913);
            gridText = Button(content, "격자 켜짐", 20, 942, 138, 32, app.ToggleGrid).GetComponentInChildren<Text>();
            snapText = Button(content, "맞춤 꺼짐", 168, 942, 138, 32, app.ToggleSnap).GetComponentInChildren<Text>();
            Label(content, "낙하 한계 Y", 20, 991, 134, 28, 12, muted);
            killField = Input(content, 168, 988, 138, app.SetKillY);
            Button(content, "맵 전체 보기  ·  Home", 20, 1037, 286, 32, app.FrameAll);

            var footer = Panel(editRoot.transform, "Status bar", WorldView.Hex("11192A"));
            Stretch(footer, 0, 0, 1, 0, 0, 0, 0, FooterHeight);
            status = Label(footer, "", 20, 5, 850, 22, 11, muted);
            docTitle = Label(editRoot.transform, "", 23, 88, 700, 23, 12, ink);
            selectTool = Button(editRoot.transform, "선택 · V", 23, 115, 110, 32, () => app.SetEditorTool(EditorTool.Select));
            panTool = Button(editRoot.transform, "화면 드래그 · H", 141, 115, 146, 32, () => app.SetEditorTool(EditorTool.Pan));
            Button(editRoot.transform, "−", 303, 115, 36, 32, () => app.ZoomEditor(-1));
            Button(editRoot.transform, "+", 345, 115, 36, 32, () => app.ZoomEditor(1));
            navigationZoom = Label(editRoot.transform, "", 395, 119, 180, 24, 12, ink);
            var hint = navigationHint = Label(editRoot.transform, "", 23, 0, 1000, 24, 11, muted);
            var hr = hint.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0, 0);
            hr.pivot = Vector2.zero;
            hr.anchoredPosition = new Vector2(23, FooterHeight + 12);
        }

        void BuildTest()
        {
            var guide = Full(playRoot.transform, "Drag gesture guide");
            dragGuide = guide.AddComponent<DragGuideGraphic>();
            dragGuide.raycastTarget = false;
            guide.SetActive(false);
            Label(playRoot.transform, "TEST RUN · v" + StudioApp.Version, 24, 20, 240, 22, 12, WorldView.Mint, FontStyle.Bold);
            testInfo = Label(playRoot.transform, "", 24, 50, 480, 25, 12, ink);
            var back = Button(playRoot.transform, "편집으로 돌아가기  ·  Esc", 0, 18, 220, 38, app.StopTest);
            var rect = back.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-24, -18);
            var developerButton = Button(playRoot.transform, "개발자 모드 OFF · F3", 0, 18, 206, 38, app.ToggleDeveloperMode, WorldView.Yellow);
            var developerRect = developerButton.GetComponent<RectTransform>();
            developerRect.anchorMin = developerRect.anchorMax = new Vector2(1, 1);
            developerRect.pivot = new Vector2(1, 1);
            developerRect.anchoredPosition = new Vector2(-256, -18);
            developerPlayToggle = developerButton.GetComponentInChildren<Text>();
            var hint = Label(playRoot.transform, "아무 곳이나 누르고 아래로 당긴 뒤 놓기   ·   R 재시작   ·   F3 개발자 모드", 0, 0, 950, 30, 14, ink);
            hint.alignment = TextAnchor.MiddleCenter;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(.5f, 0);
            hint.rectTransform.pivot = new Vector2(.5f, 0);
            hint.rectTransform.anchoredPosition = new Vector2(0, 26);
        }

        void BuildDeveloper()
        {
            developerRoot = Full(Canvas.transform, "Developer diagnostics");
            developerPanel = Panel(developerRoot.transform, "Jump telemetry", new Color(panel.r, panel.g, panel.b, .96f));
            developerPanel.GetComponent<Image>().raycastTarget = false;
            Position(developerPanel, 24, 94, 380, 392);
            Label(developerPanel, "DEVELOPER MODE  /  점프 튜닝", 20, 14, 340, 24, 13, WorldView.Yellow, FontStyle.Bold);
            developerState = Label(developerPanel, "", 20, 44, 340, 20, 11, WorldView.Mint);
            developerLive = Label(developerPanel, "", 20, 70, 340, 30, 21, ink, FontStyle.Bold);
            developerDetails = Label(developerPanel, "", 20, 109, 340, 59, 11, muted);
            var track = Panel(developerPanel, "Strength background", fieldColor);
            Position(track, 20, 177, 340, 6);
            track.GetComponent<Image>().raycastTarget = false;
            developerPower = Panel(developerPanel, "Applied strength", WorldView.Yellow);
            Position(developerPower, 20, 177, 0, 6);
            developerPower.GetComponent<Image>().raycastTarget = false;
            Label(developerPanel, "강도 = 현재 최대 발사 속도에 대한 비율", 20, 191, 340, 18, 10, muted);
            developerSettings = Label(developerPanel, "", 20, 219, 340, 35, 11, ink);
            var divider = Panel(developerPanel, "Telemetry divider", fieldColor);
            Position(divider, 20, 264, 340, 1);
            divider.GetComponent<Image>().raycastTarget = false;
            developerLastTitle = Label(developerPanel, "마지막 확정 점프", 20, 278, 340, 20, 11, WorldView.Mint);
            developerLast = Label(developerPanel, "", 20, 303, 340, 27, 17, ink, FontStyle.Bold);
            developerLastDetails = Label(developerPanel, "", 20, 336, 340, 41, 10, muted);
            var drawing = Full(developerRoot.transform, "Click origin and launch angle");
            aimGraphic = drawing.AddComponent<DeveloperAimGraphic>();
            aimGraphic.raycastTarget = false;
        }

        public void UpdateDragGuide()
        {
            var visible = app.IsPlaying && app.IsCharging && !app.DeveloperMode && !HasDialog;
            dragGuide.gameObject.SetActive(visible);
            if (visible) dragGuide.SetDrag(app.ChargeStart, app.PointerPosition);
        }

        public void UpdateDeveloper()
        {
            UpdateDragGuide();
            var enabled = app.DeveloperMode;
            var toggleText = "개발자 모드 " + (enabled ? "ON" : "OFF") + " · F3";
            developerEditToggle.text = developerPlayToggle.text = toggleText;
            developerEditToggle.color = developerPlayToggle.color = panel;
            developerEditToggle.transform.parent.GetComponent<Image>().color = enabled ? WorldView.Mint : WorldView.Yellow;
            developerPlayToggle.transform.parent.GetComponent<Image>().color = enabled ? WorldView.Mint : WorldView.Yellow;
            developerRoot.SetActive(enabled);
            if (!enabled) { aimGraphic.gameObject.SetActive(false); return; }
            Position(developerPanel, 24, app.IsPlaying ? 94 : 174, 380, 392);
            var aim = app.CurrentAim;
            var drawing = app.IsPlaying && app.IsCharging && aim.HasValue;
            aimGraphic.gameObject.SetActive(drawing);
            if (drawing) aimGraphic.SetAim(app.ChargeStart, app.PointerPosition, aim.Value);
            var settings = app.Document.jump;
            developerSettings.text = "현재 설정  감도 " + Number(settings.sensitivity) + "  ·  최대 속도 " + Number(settings.maxSpeed)
                + "\n중력 " + Number(settings.gravity) + "  ·  ±" + Number(settings.maxAngle) + "°  ·  최소 " + Number(settings.minStrength * 100) + "%";
            var strength = 0f;
            if (drawing)
            {
                var value = aim.Value;
                strength = value.Strength;
                developerState.text = !value.IsValid ? "발사 취소 · " + InvalidAimText(value.Status)
                    : "당기는 중" + (value.MinimumApplied ? " · 최소 강도 적용" : "")
                        + (value.AngleLimited ? " · 각도 제한" : "") + (value.Saturated ? " · 최대 강도" : "");
                developerLive.text = value.IsValid ? Summary(value) : "발사 안 함  ·  강도 0%";
                developerDetails.text = "입력 " + AngleText(value.RawAngle) + " → 적용 " + (value.IsValid ? AngleText(value.Angle) : "없음")
                    + "\n당김 " + Number(value.InputDistance) + "px  /  기준 " + Number(value.NormalizedDistance) + "px"
                    + "\n입력 강도 " + Number(value.InputStrength * 100) + "% → 적용 " + Number(value.Strength * 100) + "%"
                    + "\n발사 속도 " + Number(value.Speed) + " / " + Number(value.MaxSpeed) + "  ·  기준 높이 1080px";
            }
            else
            {
                developerState.text = !app.IsPlaying ? "편집 중 · 테스트에서 드래그를 확인하세요."
                    : app.Motor.Grounded ? "클릭하고 아래로 당겨보세요." : "공중 · 착지 후 조준할 수 있어요.";
                developerLive.text = "조준 대기";
                developerDetails.text = "노란 십자: 클릭 기준점 · 점선: 수직 0°"
                    + "\n민트 선: 당긴 거리 · 노란 화살표: 적용 방향"
                    + "\n노란 호: 적용 각도 · 확정 값은 아래에 유지돼요.";
            }
            developerPower.sizeDelta = new Vector2(340 * strength, 6);
            if (app.LastLaunch.HasValue)
            {
                var value = app.LastLaunch.Value;
                developerLastTitle.text = "마지막 확정 점프  #" + app.LastJumpNumber;
                developerLast.text = Summary(value);
                developerLastDetails.text = "속도 " + Number(value.Speed) + "  ·  기준 당김 " + Number(value.NormalizedDistance) + "px  ·  최소 " + Number(value.MinStrength * 100) + "%"
                    + "\n당시 감도 " + Number(value.Sensitivity) + " / 최대 " + Number(value.MaxSpeed)
                    + " / 중력 " + Number(value.Gravity) + " / ±" + Number(value.MaxAngle) + "°";
            }
            else
            {
                developerLastTitle.text = "마지막 확정 점프";
                developerLast.text = "아직 발사한 점프가 없어요.";
                developerLastDetails.text = "마우스를 놓고 실제로 발사된 점프만 기록해요.\n편집으로 돌아와도 마지막 기록을 비교할 수 있어요.";
            }
        }

        static string Summary(LaunchSolution value) => AngleText(value.Angle) + "  ·  강도 "
            + (value.Strength * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        static string AngleText(float angle) => (Mathf.Abs(angle) < .05f ? "수직 " : angle < 0 ? "왼쪽 " : "오른쪽 ")
            + Mathf.Abs(angle).ToString("0.0", CultureInfo.InvariantCulture) + "°";
        static string InvalidAimText(LaunchStatus status) => status == LaunchStatus.TooShort ? "기준 거리 12px 이상 당겨주세요."
            : status == LaunchStatus.WrongDirection ? "아래 방향으로 당겨주세요." : "게임 화면을 확인해 주세요.";

        public bool PointerOverUI(Vector2 position)
        {
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            return hits.Count > 0;
        }

        public void SetPlaying(bool playing)
        {
            editRoot.SetActive(!playing);
            playRoot.SetActive(playing);
            UpdateDeveloper();
            UpdateNavigation();
        }

        public void UpdateNavigation()
        {
            var selecting = app.ActiveEditorTool == EditorTool.Select;
            selectTool.GetComponent<Image>().color = selecting ? WorldView.Mint : fieldColor;
            panTool.GetComponent<Image>().color = selecting ? fieldColor : WorldView.Mint;
            selectTool.GetComponentInChildren<Text>().color = selecting ? panel : ink;
            panTool.GetComponentInChildren<Text>().color = selecting ? ink : panel;
            navigationZoom.text = "확대 " + Mathf.RoundToInt(8 / app.WorldCamera.orthographicSize * 100) + "%";
            navigationHint.text = selecting
                ? "선택: 발판 이동·크기 조절   /   H 화면 드래그   /   Space·가운데 버튼으로 임시 이동   /   휠 한 칸 1.35배"
                : "화면 드래그: 아무 곳이나 눌러 이동   /   V 선택 모드   /   휠 한 칸 1.35배   /   Home 맵 전체 보기";
            selectionHint.text = !selecting ? "화면 이동 모드 · V로 발판 편집"
                : app.SelectedPlatform != null ? "드래그 이동 · 모서리로 크기 조절" : "발판 · 시작점 · 달을 클릭해서 편집";
        }

        public void UpdateTestInfo(bool grounded, bool complete)
        {
            testInfo.text = complete ? "드디어, 달에 닿았어!" : grounded ? "발판에 착지했어요. 다음 점프를 해보세요." : "달을 향해 조금씩, 더 높이.";
        }

        public void Refresh()
        {
            var map = app.Document;
            if (map == null) return;
            SetText(NameField, map.name);
            SetText(killField, Number(map.killY));
            selectionTitle.text = app.SelectionName;
            selectionHint.text = app.SelectedPlatform != null ? "드래그 이동 · 모서리로 크기 조절" : "발판 · 시작점 · 달을 클릭해서 편집";
            var rect = app.SelectionBounds;
            var values = rect.HasValue ? new[] { rect.Value.center.x, rect.Value.center.y, rect.Value.width, rect.Value.height } : new float[4];
            for (var i = 0; i < 4; i++)
            {
                Coordinates[i].interactable = rect.HasValue && (i < 2 || app.SelectedPlatform != null);
                SetText(Coordinates[i], rect.HasValue ? Number(values[i]) : "—");
            }
            var settings = new[] { map.jump.sensitivity, map.jump.maxSpeed, map.jump.gravity, map.jump.maxAngle, map.jump.minStrength * 100 };
            for (var i = 0; i < settings.Length; i++)
            {
                Settings[i].SetValueWithoutNotify(settings[i]);
                settingValues[i].text = i == 3 ? Mathf.RoundToInt(settings[i]) + "°" : i == 4 ? Mathf.RoundToInt(settings[i]) + "%" : Number(settings[i]);
            }
            duplicate.interactable = selectedTest.interactable = app.SelectedPlatform != null;
            delete.interactable = app.SelectedPlatform != null && map.platforms.Count > 1;
            undo.interactable = app.History.CanUndo;
            redo.interactable = app.History.CanRedo;
            gridText.text = app.GridVisible ? "격자 켜짐" : "격자 꺼짐";
            snapText.text = app.SnapEnabled ? "맞춤 0.5" : "맞춤 꺼짐";
            docTitle.text = map.name + (app.Dirty ? "  • 수정됨" : "  • 저장됨");
            UpdateNavigation();
        }

        public void Status(string value) => status.text = value;

        public void Dialog(string title, string message, params (string label, Action action)[] actions)
        {
            CloseDialog();
            Time.timeScale = 0;
            dialog = Full(Canvas.transform, "Dialog");
            dialog.AddComponent<Image>().color = new Color(.02f, .04f, .09f, .8f);
            var body = Panel(dialog.transform, "Dialog body", panel);
            body.anchorMin = body.anchorMax = new Vector2(.5f, .5f);
            body.pivot = new Vector2(.5f, .5f);
            body.sizeDelta = new Vector2(540, 260);
            body.anchoredPosition = Vector2.zero;
            Label(body, title, 28, 24, 484, 37, 23, WorldView.Yellow, FontStyle.Bold);
            var text = Label(body, message, 28, 82, 484, 92, 14, ink);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            var width = (484 - (actions.Length - 1) * 10) / actions.Length;
            for (var i = 0; i < actions.Length; i++)
            {
                var action = actions[i].action;
                Button(body, actions[i].label, 28 + i * (width + 10), 193, width, 38,
                    () => { CloseDialog(); action?.Invoke(); }, i == 0 ? WorldView.Mint : (Color?)null);
            }
        }

        public void CloseDialog()
        {
            if (dialog) { dialog.SetActive(false); UnityEngine.Object.Destroy(dialog); }
            dialog = null;
            Time.timeScale = 1;
        }

        void SetText(InputField field, string value) { if (!field.isFocused) field.SetTextWithoutNotify(value); }
        public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        void Section(Transform parent, string text, float y)
        {
            var line = Panel(parent, "Divider", fieldColor);
            Position(line, 20, y, 286, 1);
            Label(parent, text, 20, y + 9, 286, 20, 10, muted, FontStyle.Bold);
        }

        Button ToolbarButton(Transform parent, string title, ref float x, float width, Action action, Color? color = null)
        {
            var button = Button(parent, title, x, 18, width, 36, action, color);
            x += width + 8;
            return button;
        }

        public Text Label(Transform parent, string text, float x, float y, float width, float height, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var rect = Box(parent, text, x, y, width, height);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        Button Button(Transform parent, string title, float x, float y, float width, float height, Action action, Color? color = null)
        {
            var rect = Panel(parent, title, color ?? fieldColor);
            Position(rect, x, y, width, height);
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.14f, 1.14f, 1.14f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.5f, .5f, .5f, .45f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            var label = Label(rect, title, 0, 0, width, height, 12, color.HasValue ? panel : ink, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        InputField Input(Transform parent, float x, float y, float width, Action<string> action)
        {
            var rect = Panel(parent, "Input", fieldColor);
            Position(rect, x, y, width, 34);
            var field = rect.gameObject.AddComponent<InputField>();
            var label = Label(rect, "", 9, 0, width - 18, 34, 13, ink);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            field.textComponent = label;
            field.targetGraphic = rect.GetComponent<Image>();
            field.characterLimit = 24;
            field.selectionColor = new Color(.6f, .5f, 1, .3f);
            field.caretColor = WorldView.Yellow;
            field.customCaretColor = true;
            field.onEndEdit.AddListener(value => { action(value); app.RequestUIRefresh(); });
            return field;
        }

        Slider Slider(Transform parent, float x, float y, float width, float min, float max, Action<float> action)
        {
            var root = Box(parent, "Slider", x, y, width, 24);
            var background = Panel(root, "Track", fieldColor);
            Position(background, 0, 9, width, 5);
            var fillArea = Box(root, "Fill area", 0, 9, width, 5);
            var fill = Panel(fillArea, "Fill", WorldView.Accent);
            Stretch(fill, 0, 0, 1, 1, 0, 0, 0, 0);
            var handleArea = Box(root, "Handle area", 0, 3, width, 18);
            var handle = Panel(handleArea, "Handle", WorldView.Yellow);
            handle.pivot = Vector2.one * .5f;
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = new Vector2(12, 0);
            var slider = root.gameObject.AddComponent<StudioSlider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(value => action(value));
            return slider;
        }

        static GameObject Full(Transform parent, string name)
        {
            var rect = Box(parent, name, 0, 0, 0, 0);
            Stretch(rect, 0, 0, 1, 1, 0, 0, 0, 0);
            return rect.gameObject;
        }
        static RectTransform Panel(Transform parent, string name, Color color)
        {
            var rect = Box(parent, name, 0, 0, 0, 0);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }
        static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Position(rect, x, y, w, h);
            return rect;
        }
        static void Position(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
        static void Stretch(RectTransform rect, float ax, float ay, float bx, float by, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(ax, ay);
            rect.anchorMax = new Vector2(bx, by);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
    }
}
