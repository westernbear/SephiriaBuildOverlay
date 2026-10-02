using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    internal NativeBuildWindow? CreateBuildWindow(Action<string> changed, Action<string> load, Action settings, Action close, float panelScale = 1, Action<float>? resized = null)
    {
        var type = GameType("UI_MessageBox_InputYesNo");
        if (type is null) return null;
        var template = Resources.FindObjectsOfTypeAll(type).OfType<Component>()
            .FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
        return template is null ? null : new NativeBuildWindow(this, template, changed, load, settings, close, panelScale, resized);
    }

    // New UI objects, using the game's actual TMP font, sliced panel/input
    // sprites and UI_HorayButton primitive. No native menu callbacks are copied.
    internal sealed class NativeBuildWindow : IDisposable
    {
        private readonly UnityGameGateway _gateway;
        private readonly GameObject _root;
        private readonly Component _system;
        private readonly Component _module;
        private readonly Component _input;
        private readonly Component _title;
        private readonly Component _activeTitle;
        private readonly Component _load;
        private readonly Component _loadText;
        private readonly Component _settings;
        private readonly Component _close;
        private readonly Component _resizeGrip;
        private readonly PanelResizeState _resize;
        private readonly Action<float>? _resized;
        private float _drawUiScale = 1, _drawFit = 1;
        private readonly RectTransform _panel;
        private readonly HashSet<object> _actions = new();
        private readonly OwnedUiInputScope _inputScope;
        private readonly Action<string> _loadAction;
        private readonly Action _settingsAction;
        private readonly Action _closeAction;
        private readonly Type _imageType;
        private readonly Type _textType;
        private readonly object _font;
        private Component? _previousSystem;
        private bool _previousSystemEnabled;
        private object? _ownedAsset;
        private readonly List<UnityEngine.Object> _ownedReferences = new();
        private bool _busy;
        private bool _gamepad;
        public bool Visible => _root != null && _root.activeSelf;

        public NativeBuildWindow(UnityGameGateway gateway, Component template, Action<string> changed,
            Action<string> load, Action settings, Action close, float panelScale, Action<float>? resized)
        {
            _gateway = gateway; _loadAction = load; _settingsAction = settings; _closeAction = close;
            _resize = new PanelResizeState(panelScale); _resized = resized;
            _imageType = NeedType("UnityEngine.UI.Image"); _textType = NeedType("TMPro.TextMeshProUGUI");
            var textTemplate = ReadNamedObject(template, "text") as Component ?? throw new InvalidOperationException("게임 메뉴 폰트가 없습니다.");
            _font = ReadNamedObject(textTemplate, "font") ?? throw new InvalidOperationException("게임 메뉴 폰트가 없습니다.");
            // The game's menu has its own canvas/world scaling. This canvas
            // uses screen pixels: keep the font asset, not the source scale or
            // a source material carrying another menu's clipping/stencil state.
            _root = new GameObject("SephiriaBuildOverlay.BuildWindow", typeof(RectTransform), typeof(Canvas));
            _root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_root); _root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var canvas = _root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = OverlayUiTokens.OverlaySortingOrder; canvas.pixelPerfect = true;
                canvas.referencePixelsPerUnit = ScreenSpaceUiMetrics.ReferencePixelsPerUnit;
                _root.AddComponent(NeedType("UnityEngine.UI.GraphicRaycaster"));
                var shield = Image("Input shield", (RectTransform)_root.transform, null, new Color(0, 0, 0, .4f), true);
                var shieldRect = (RectTransform)shield.transform;
                shieldRect.anchorMin = Vector2.zero; shieldRect.anchorMax = Vector2.one; shieldRect.offsetMin = Vector2.zero; shieldRect.offsetMax = Vector2.zero;
                var images = template.GetComponentsInChildren(_imageType, true).OfType<Component>();
                var panelImage = images.Where(x => ReadNamedObject(x, "sprite") is Sprite sprite && sprite.border != Vector4.zero)
                    .OrderByDescending(x => x.transform is RectTransform rect ? rect.rect.width * rect.rect.height : 0).FirstOrDefault()
                    ?? throw new InvalidOperationException("게임 패널 리소스가 없습니다.");
                gateway._notificationPanelSprite = ReadNamedObject(panelImage, "sprite") as Sprite;
                gateway._notificationPanelColor = ReadColor(panelImage);
                gateway._notificationFont = _font as UnityEngine.Object;
                _panel = (RectTransform)Image("Build panel", (RectTransform)_root.transform,
                    ReadNamedObject(panelImage, "sprite") as Sprite, ReadColor(panelImage), true).transform;
                _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(.5f, .5f);
                _title = Text("Heading", _panel, "빌드", OverlayTheme.Accent, pixels: OverlayUiTokens.HeadingFontSize);
                _activeTitle = Text("Active build", _panel, "", OverlayTheme.Muted, pixels: OverlayUiTokens.SmallFontSize);
                _load = Button("Load", "불러오기", () => { if (!_busy) _loadAction(_gamepad ? GUIUtility.systemCopyBuffer.Trim() : InputText); }, out _loadText);
                _settings = Button("Settings", "설정", _settingsAction, out _);
                _close = Button("Close", "닫기", _closeAction, out _);
                _resizeGrip = Image("Resize panel", _panel, null, OverlayTheme.Surface, true);
                // Pixel marks use the native Image primitive, not a font glyph
                // that may be absent from the game's localized font atlas.
                for (var i = 0; i < 3; i++)
                {
                    var mark = Image("Resize mark", (RectTransform)_resizeGrip.transform, null, OverlayTheme.Muted, false);
                    Place(mark, new ControlBounds(8 + i * 5, 18 - i * 5, 3, 3));
                }
                var drag = _resizeGrip.gameObject.AddComponent(NeedType("UnityEngine.EventSystems.EventTrigger"));
                AddDragEvent(drag, "PointerDown", nameof(BeginResize));
                AddDragEvent(drag, "BeginDrag", nameof(BeginResize));
                AddDragEvent(drag, "Drag", nameof(DragResize)); AddDragEvent(drag, "EndDrag", nameof(EndResize));
                AddDragEvent(drag, "PointerUp", nameof(EndResize));
                var inputRoot = ControlSurface("Build link", _panel, OverlayTheme.Inset);
                var viewport = Node("Text viewport", (RectTransform)inputRoot.transform);
                viewport.gameObject.AddComponent(NeedType("UnityEngine.UI.RectMask2D"));
                viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = new Vector2(10, 4); viewport.offsetMax = new Vector2(-10, -4);
                var value = Text("Value", viewport, "", OverlayTheme.Text, "Left"); Stretch((RectTransform)value.transform);
                var placeholder = Text("Placeholder", viewport, "Wiki 빌드 링크", OverlayTheme.Muted, "Left"); Stretch((RectTransform)placeholder.transform);
                _input = inputRoot.gameObject.AddComponent(NeedType("TMPro.TMP_InputField"));
                Set(_input, "textViewport", viewport); Set(_input, "textComponent", value); Set(_input, "placeholder", placeholder);
                Set(_input, "targetGraphic", inputRoot); Set(_input, "characterLimit", 256); Set(_input, "caretWidth", 2);
                StyleControl(_input);
                Set(_input, "customCaretColor", true); Set(_input, "caretColor", OverlayTheme.Text);
                SetEnum(_input, "lineType", "SingleLine");
                Listen(_input, "onValueChanged", new UnityAction<string>(changed));
                Listen(_input, "onSubmit", new UnityAction<string>(text => { if (!_busy) _loadAction(text); }));
                _system = _root.AddComponent(NeedType("UnityEngine.EventSystems.EventSystem"));
                Set(_system, "sendNavigationEvents", false); // Paired-pad navigation is owned by ControllerMenu, not all connected pads.
                _module = _root.AddComponent(NeedType("UnityEngine.InputSystem.UI.InputSystemUIInputModule"));
                // AssignDefaultActions uses a process-wide static asset in the
                // installed Input System. Build a private map instead: never
                // disable or destroy input actions belonging to the game.
                var defaults = Activator.CreateInstance(NeedType("UnityEngine.InputSystem.DefaultInputActions"))!;
                _ownedAsset = ReadNamedObject(defaults, "asset");
                Set(_module, "actionsAsset", _ownedAsset);
                var original = gateway.ReadStatic("UnityEngine.EventSystems.EventSystem", "current");
                var originalModule = original is null ? null : ReadNamedObject(original, "currentInputModule");
                if (_ownedAsset is null || ReferenceEquals(_ownedAsset, originalModule is null ? null : ReadNamedObject(originalModule, "actionsAsset")))
                    throw new InvalidOperationException("독립 UI 입력 맵을 만들 수 없습니다.");
                foreach (var (name, actionName) in new[] { ("point", "Point"), ("leftClick", "Click"), ("rightClick", "RightClick"),
                    ("middleClick", "MiddleClick"), ("scrollWheel", "ScrollWheel"), ("move", "Navigate"), ("submit", "Submit"), ("cancel", "Cancel") })
                {
                    var action = _ownedAsset.GetType().GetMethod("FindAction", new[] { typeof(string), typeof(bool) })!
                        .Invoke(_ownedAsset, new object[] { "UI/" + actionName, true })!;
                    var reference = NeedType("UnityEngine.InputSystem.InputActionReference").GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
                        .Invoke(null, new[] { action })!;
                    _ownedReferences.Add((UnityEngine.Object)reference);
                    Set(_module, name, reference); _actions.Add(action);
                }
                _inputScope = new OwnedUiInputScope(_module, _actions);
            }
            catch
            {
                UnityEngine.Object.Destroy(_root);
                foreach (var reference in _ownedReferences) UnityEngine.Object.Destroy(reference);
                if (_ownedAsset is UnityEngine.Object asset) UnityEngine.Object.Destroy(asset);
                throw;
            }
        }

        public string InputText => ReadNamedString(_input, "text") ?? "";
        public bool OwnsModule(object module) => _inputScope.AllowsModule(module, Visible);
        public bool OwnsAction(object action) => _inputScope.AllowsAction(action, Visible);

        public void Show(string locator, bool gamepad)
        {
            if (Visible) return;
            _gamepad = gamepad;
            _previousSystem = _gateway.ReadStatic("UnityEngine.EventSystems.EventSystem", "current") as Component;
            _previousSystemEnabled = _previousSystem is Behaviour behaviour && behaviour.enabled;
            if (_previousSystem is Behaviour previous) previous.enabled = false;
            try
            {
                Set(_input, "text", locator);
                _root.SetActive(true);
                Focus(gamepad ? _load : _input);
                if (!gamepad) Call(_input, "ActivateInputField");
            }
            catch { Hide(); throw; }
        }

        public void Hide()
        {
            _resize.Cancel();
            if (_root != null) _root.SetActive(false);
            if (_previousSystem is Behaviour previous) previous.enabled = _previousSystemEnabled;
            _previousSystem = null;
        }
        public void CancelResize() => _resize.Cancel();

        public void Render(bool gamepad, bool busy, string? activeTitle, float scale, ControllerMenu menu)
        {
            _busy = busy;
            if (_gamepad != gamepad)
            {
                if (gamepad) { _resize.Cancel(); Call(_input, "DeactivateInputField", false); Focus(_load); }
                else { Focus(_input); Call(_input, "ActivateInputField"); }
            }
            _gamepad = gamepad;
            var availableWidth = Math.Max(1, Screen.width - OverlayUiTokens.EdgeMargin * 2);
            var availableHeight = Math.Max(1, Screen.height - 120); // Leave the passive notification corner outside this modal.
            _drawUiScale = scale;
            _drawFit = Math.Min(availableWidth / OverlayUiTokens.CompactWidth, availableHeight / OverlayUiTokens.CompactHeight);
            scale = Math.Min(scale * _resize.Scale, _drawFit);
            _panel.sizeDelta = new Vector2(OverlayUiTokens.CompactWidth, OverlayUiTokens.CompactHeight);
            _panel.localScale = new Vector3(scale, scale, 1);
            Place(_title, CompactBuildLayout.Heading);
            Place(_settings, CompactBuildLayout.Settings); Place(_close, CompactBuildLayout.Close);
            Place(_input, CompactBuildLayout.Input(gamepad));
            Place(_load, CompactBuildLayout.Load(gamepad));
            Place(_activeTitle, CompactBuildLayout.ActiveTitle);
            Place(_resizeGrip, CompactBuildLayout.Resize); _resizeGrip.gameObject.SetActive(!gamepad);
            Set(_activeTitle, "text", activeTitle ?? "");
            Set(_loadText, "text", busy ? "불러오는 중" : gamepad ? "클립보드 불러오기" : "불러오기");
            Set(_load, "interactable", !busy); Set(_settings, "interactable", !busy);
            Set(_input, "interactable", !busy && !gamepad);
            if (gamepad)
            {
                menu.BeginFrame();
                if (!busy) { menu.Add("native-load", () => _loadAction(GUIUtility.systemCopyBuffer.Trim())); menu.Add("native-settings", _settingsAction); }
                menu.Add("native-close", _closeAction); menu.EndFrame();
                Focus(menu.IsSelected("native-settings") ? _settings : menu.IsSelected("native-close") ? _close : _load);
            }
        }

        private void Focus(Component component) => Call(_system, "SetSelectedGameObject", component.gameObject);
        private void AddDragEvent(Component trigger, string eventName, string handlerName)
        {
            var entry = Activator.CreateInstance(trigger.GetType().GetNestedType("Entry")!)!;
            SetEnum(entry, "eventID", eventName);
            var listenerType = typeof(UnityAction<>).MakeGenericType(NeedType("UnityEngine.EventSystems.BaseEventData"));
            var handler = GetType().GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            Listen(entry, "callback", Delegate.CreateDelegate(listenerType, this, handler));
            ((IList)ReadNamedObject(trigger, "triggers")!).Add(entry);
        }
        private bool ResizePointer(object data, out int pointer, out Vector2 position)
        {
            pointer = Convert.ToInt32(ReadNamedObject(data, "pointerId"));
            position = ReadNamedObject(data, "position") is Vector2 point ? point : Vector2.zero;
            return Visible && Application.isFocused && !_gamepad && ReadNamedObject(data, "button")?.ToString() == "Left";
        }
        private void BeginResize(object data)
        {
            if (ResizePointer(data, out var pointer, out var point))
            {
                // BeginDrag is dispatched at the FIRST moved position, not at
                // mouse-down. A fast one-frame drag can already be at its final
                // position: start from pressPosition so that movement is kept.
                var pressed = ReadNamedObject(data, "pressPosition") is Vector2 origin ? origin : point;
                if (_resize.Begin(pointer, pressed.x, pressed.y, _panel.localScale.x, _drawUiScale, _drawFit))
                    _gateway._log.LogInfo($"Panel resize started: pointer={pointer}, origin={pressed}, position={point}");
            }
        }
        private void DragResize(object data)
        { if (ResizePointer(data, out var pointer, out var point)) _resize.Drag(pointer, point.x, point.y); }
        private void EndResize(object data)
        {
            if (ResizePointer(data, out var pointer, out var point))
            { _resize.Drag(pointer, point.x, point.y); FinishResize(pointer); }
        }
        public void HandleMouseResizeEvent(Event input)
        {
            if (!Visible || !Application.isFocused || _gamepad) { _resize.Cancel(); return; }
            const int mousePointer = -1;
            var point = new Vector2(input.mousePosition.x, Screen.height - input.mousePosition.y);
            if (input.type == EventType.MouseDown && input.button == 0 &&
                _gateway.ScreenRect((RectTransform)_resizeGrip.transform).Contains(input.mousePosition))
            {
                // uGUI can coalesce fast drag moves or send a different pointer
                // ID through its module. Own the legacy mouse stream ONLY after
                // a left press inside our visible grip. No polling/other input
                // can start a drag, and no game action is sent.
                _resize.Cancel();
                _resize.Begin(mousePointer, point.x, point.y, _panel.localScale.x, _drawUiScale, _drawFit);
                input.Use();
            }
            else if (_resize.ActivePointer == mousePointer && input.button == 0 &&
                input.type is EventType.MouseDrag or EventType.MouseUp)
            {
                _resize.Drag(mousePointer, point.x, point.y);
                if (input.type == EventType.MouseUp) FinishResize(mousePointer);
                input.Use();
            }
        }
        private void FinishResize(int pointer)
        {
            if (!_resize.End(pointer)) return;
            _gateway._log.LogInfo($"Panel resize finished: scale={_resize.Scale}");
            _resized?.Invoke(_resize.Scale);
        }
        internal object Diagnostics() => new
        {
            visible = Visible, font = (_font as UnityEngine.Object)?.name,
            headingPixels = ReadNamedObject(_title, "fontSize"), bodyPixels = ReadNamedObject(_loadText, "fontSize"),
            panelSprite = ReadNamedObject(_panel.GetComponent(_imageType), "sprite") is Sprite sprite ? sprite.name : null,
            button = _load.GetType().FullName, input = _input.GetType().FullName,
            privateInputActions = _actions.Count, navigationEvents = ReadNamedObject(_system, "sendNavigationEvents"),
            resizeEvents = (ReadNamedObject(_resizeGrip.GetComponent(NeedType("UnityEngine.EventSystems.EventTrigger")), "triggers") as IList)?.Count
        };
        private Type NeedType(string name) => _gateway.GameType(name) ?? throw new TypeLoadException(name);
        private Component Image(string name, RectTransform parent, Sprite? sprite, Color color, bool raycast)
        {
            var rect = Node(name, parent); var image = rect.gameObject.AddComponent(_imageType);
            Set(image, "sprite", sprite); Set(image, "color", color); Set(image, "raycastTarget", raycast);
            if (sprite != null)
            {
                SetEnum(image, "type", "Sliced");
                var border = sprite.border;
                Set(image, "pixelsPerUnitMultiplier", ScreenSpaceUiMetrics.SliceMultiplier(Math.Max(Math.Max(border.x, border.y), Math.Max(border.z, border.w)), sprite.pixelsPerUnit));
            }
            return image;
        }
        private Component Text(string name, RectTransform parent, string value, Color color, string alignment = "Left", float pixels = OverlayUiTokens.BodyFontSize)
        {
            var text = Node(name, parent).gameObject.AddComponent(_textType);
            Set(text, "font", _font); Set(text, "fontSize", pixels);
            Set(text, "enableAutoSizing", false); SetEnum(text, "fontStyle", "Normal");
            Set(text, "text", value); Set(text, "color", color);
            Set(text, "raycastTarget", false); Set(text, "richText", false); Set(text, "enableWordWrapping", false);
            SetEnum(text, "alignment", alignment); SetEnum(text, "overflowMode", "Ellipsis");
            return text;
        }
        private Component Button(string name, string label, Action click, out Component text)
        {
            // Native targetGraphic is a selection-only ornament, not a button
            // background. Reuse UI_HorayButton, but own its visible surface and
            // color states so the button remains legible when not selected.
            var image = ControlSurface(name, _panel, OverlayTheme.Button);
            text = Text(name + " label", (RectTransform)image.transform, label, OverlayTheme.Text, "Center"); Stretch((RectTransform)text.transform);
            var button = image.gameObject.AddComponent(NeedType("UI_HorayButton"));
            Set(button, "targetGraphic", image); Set(button, "text", text);
            StyleControl(button); Set(button, "disabledColor", OverlayTheme.Muted);
            // All navigation stays in this modal; no native menu focus targets are copied.
            var navigation = ReadNamedObject(button, "navigation");
            if (navigation is not null) { SetEnum(navigation, "mode", "None"); Set(button, "navigation", navigation); }
            Listen(button, "onClick", new UnityAction(click));
            return button;
        }
        private Component ControlSurface(string name, RectTransform parent, Color color)
        {
            var image = Image(name, parent, null, color, true);
            var rect = (RectTransform)image.transform;
            foreach (var edge in new[] { "Top", "Bottom", "Left", "Right" })
            {
                var trim = (RectTransform)Image(name + " " + edge, rect, null, OverlayTheme.Border, false).transform;
                trim.anchorMin = edge == "Top" ? new Vector2(0, 1) : Vector2.zero;
                trim.anchorMax = edge == "Bottom" ? new Vector2(1, 0) : edge == "Left" ? new Vector2(0, 1) : Vector2.one;
                if (edge == "Right") trim.anchorMin = new Vector2(1, 0);
                trim.pivot = new Vector2(.5f, .5f);
                trim.offsetMin = Vector2.zero; trim.offsetMax = Vector2.zero;
                if (edge is "Top" or "Bottom") { trim.sizeDelta = new Vector2(0, 1); trim.anchoredPosition = new Vector2(0, edge == "Top" ? -.5f : .5f); }
                else { trim.sizeDelta = new Vector2(1, 0); trim.anchoredPosition = new Vector2(edge == "Right" ? -.5f : .5f, 0); }
            }
            return image;
        }
        private static void StyleControl(object selectable)
        {
            SetEnum(selectable, "transition", "ColorTint");
            var colors = ReadNamedObject(selectable, "colors")!;
            Set(colors, "normalColor", Color.white); Set(colors, "highlightedColor", new Color(1.2f, 1.2f, 1.2f));
            Set(colors, "selectedColor", new Color(1.2f, 1.2f, 1.2f)); Set(colors, "pressedColor", new Color(.8f, .8f, .8f));
            Set(colors, "disabledColor", new Color(.65f, .65f, .65f));
            Set(colors, "colorMultiplier", 1f); Set(colors, "fadeDuration", .1f); Set(selectable, "colors", colors);
        }
        private static RectTransform Node(string name, RectTransform parent)
        {
            var node = new GameObject(name, typeof(RectTransform)); node.transform.SetParent(parent, false);
            var rect = node.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            return rect;
        }
        private static void Place(Component component, ControlBounds bounds)
        { var rect = (RectTransform)component.transform; rect.anchoredPosition = new Vector2(bounds.X, -bounds.Y); rect.sizeDelta = new Vector2(bounds.Width, bounds.Height); }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(4, 2); rect.offsetMax = new Vector2(-4, -2); }
        private static Color ReadColor(object graphic) => ReadNamedObject(graphic, "color") is Color color ? color : Color.white;
        private static void Listen(object target, string name, Delegate listener)
        {
            var evt = ReadNamedObject(target, name) ?? throw new MissingMemberException(name);
            evt.GetType().GetMethod("AddListener")!.Invoke(evt, new object[] { listener });
        }
        private static void Set(object target, string name, object? value)
        {
            var member = Members.Find(target.GetType(), name);
            if (member is PropertyInfo property && property.CanWrite) property.SetValue(target, value);
            else if (member is FieldInfo field) field.SetValue(target, value);
            else throw new MissingMemberException(target.GetType().FullName, name);
        }
        private static void SetEnum(object target, string name, string value)
        {
            var member = Members.Find(target.GetType(), name);
            var type = member is PropertyInfo property ? property.PropertyType : (member as FieldInfo)?.FieldType;
            if (type?.IsEnum == true) Set(target, name, Enum.Parse(type, value));
        }
        private static void Call(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .First(x => x.Name == name && x.GetParameters().Length == arguments.Length);
            method.Invoke(target, arguments);
        }
        public void Dispose() => Dispose(true);
        public void Dispose(bool destroyUnityObjects)
        {
            Hide();
            if (destroyUnityObjects && _root != null) UnityEngine.Object.Destroy(_root);
            if (destroyUnityObjects && _ownedAsset is UnityEngine.Object asset) UnityEngine.Object.Destroy(asset);
            if (destroyUnityObjects) foreach (var reference in _ownedReferences) UnityEngine.Object.Destroy(reference);
            _ownedReferences.Clear(); _ownedAsset = null; _actions.Clear();
        }
    }
}
