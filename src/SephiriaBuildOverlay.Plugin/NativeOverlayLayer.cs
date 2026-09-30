using System.Reflection;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private NativeOverlayLayer? _nativeLayer;
    private UnityEngine.Object? _nativeFont;
    private float _nativeFontPixels = 14;

    public void BeginNativeOverlay(bool visible)
    {
        if (!visible) { _nativeLayer?.SetVisible(false); return; }
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as System.Collections.IDictionary;
        var status = registry?["UI_CharacterStatusPanel"];
        var icon = status is null ? null : (ReadNamedObject(status, "itemIcons") as System.Collections.IEnumerable)?.OfType<Component>().FirstOrDefault();
        var template = icon is null ? null : ReadNamedObject(icon, "powerText") as Component;
        var font = template is null ? null : ReadNamedObject(template, "font") as UnityEngine.Object;
        if (font == null) { _nativeLayer?.SetVisible(false); return; }
        if (_nativeLayer is null)
        {
            var textType = GameType("TMPro.TextMeshProUGUI");
            var imageType = GameType("UnityEngine.UI.Image");
            if (textType is null || imageType is null) return;
            _nativeLayer = new NativeOverlayLayer(textType, imageType);
        }
        _nativeFont = font;
        if (template!.transform is RectTransform templateRect && templateRect.rect.height > 0)
        {
            var localSize = Convert.ToSingle(ReadNamedObject(template, "fontSize") ?? 5f);
            _nativeFontPixels = Math.Max(14f, localSize * ScreenRect(templateRect).height / templateRect.rect.height);
        }
        _nativeLayer.Begin(font);
    }

    public void EndNativeOverlay() => _nativeLayer?.End();

    private sealed class NativeOverlayLayer : IDisposable
    {
        private readonly GameObject _root;
        private readonly Type _textType;
        private readonly Type _imageType;
        private readonly MethodInfo? _preferredValues;
        private readonly Dictionary<string, Element> _elements = new(StringComparer.Ordinal);
        private UnityEngine.Object _font = null!;
        private int _generation;
        private sealed class Element
        {
            public GameObject Object = null!;
            public RectTransform Rect = null!;
            public Component Component = null!;
            public int Generation;
            public string? Text;
            public UnityEngine.Object? Font;
            public float FontSize;
            public Color Color;
            public Sprite? Sprite;
            public string? MeasuredText;
            public UnityEngine.Object? MeasuredFont;
            public float MeasuredPixels;
            public Vector2 MeasuredSize;
        }
        public NativeOverlayLayer(Type textType, Type imageType)
        {
            _textType = textType; _imageType = imageType;
            _preferredValues = textType.GetMethod("GetPreferredValues", new[] { typeof(string) });
            _root = new GameObject("SephiriaBuildOverlay.Native", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000; canvas.pixelPerfect = true;
            // No GraphicRaycaster: these passive labels cannot intercept native
            // inventory dragging, mouse clicks, or the game's UI navigation.
        }
        public void SetVisible(bool visible) { if (_root != null && _root.activeSelf != visible) _root.SetActive(visible); }
        public void Begin(UnityEngine.Object font) { _font = font; _generation++; SetVisible(true); }
        public object Diagnostics() => new
        {
            visible = _root != null && _root.activeSelf,
            font = _font != null ? _font.name : null,
            passive = _root != null && _root.GetComponent("GraphicRaycaster") == null,
            labels = _elements.Where(x => x.Value.Component.GetType() == _textType).Select(x => new { key = x.Key, text = x.Value.Text }).ToArray(),
            elements = _elements.Count
        };

        private Element Get(string key, Rect rectangle, Type type)
        {
            if (!_elements.TryGetValue(key, out var element))
            {
                var obj = new GameObject("Overlay " + key, typeof(RectTransform));
                obj.transform.SetParent(_root.transform, false);
                element = new Element { Object = obj, Rect = obj.GetComponent<RectTransform>(), Component = obj.AddComponent(type) };
                element.Rect.anchorMin = Vector2.zero; element.Rect.anchorMax = Vector2.zero; element.Rect.pivot = new Vector2(0, 1);
                Set(element.Component, "raycastTarget", false);
                if (type == _textType)
                {
                    Set(element.Component, "enableAutoSizing", false);
                    Set(element.Component, "richText", false);
                    SetEnum(element.Component, "alignment", "Center");
                    SetEnum(element.Component, "fontStyle", "Normal");
                }
                _elements.Add(key, element);
            }
            element.Generation = _generation;
            var position = new Vector2(Mathf.Round(rectangle.x), Mathf.Round(Screen.height - rectangle.y));
            var size = new Vector2(Mathf.Round(rectangle.width), Mathf.Round(rectangle.height));
            if (element.Rect.anchoredPosition != position) element.Rect.anchoredPosition = position;
            if (element.Rect.sizeDelta != size) element.Rect.sizeDelta = size;
            return element;
        }
        public void Label(string key, Rect rectangle, string text, Color color, float pixels)
        {
            var element = Get(key, rectangle, _textType);
            if (element.Font != _font) { Set(element.Component, "font", _font); element.Font = _font; }
            if (element.Text != text) { Set(element.Component, "text", text); element.Text = text; }
            if (element.FontSize != pixels) { Set(element.Component, "fontSize", pixels); element.FontSize = pixels; }
            if (element.Color != color) { Set(element.Component, "color", color); element.Color = color; }
        }
        public Vector2 MeasureLabel(string key, string text, float pixels)
        {
            var measurementKey = "measure:" + key;
            if (!_elements.TryGetValue(measurementKey, out var element))
            {
                element = Get(measurementKey, Rect.zero, _textType);
                Set(element.Component, "text", string.Empty); element.Text = string.Empty;
                element.Object.SetActive(false);
            }
            else element.Generation = _generation;
            if (element.MeasuredText == text && element.MeasuredFont == _font && element.MeasuredPixels == pixels)
                return element.MeasuredSize;
            if (element.Font != _font) { Set(element.Component, "font", _font); element.Font = _font; }
            if (element.FontSize != pixels) { Set(element.Component, "fontSize", pixels); element.FontSize = pixels; }
            // Measure with the actual game TMP asset, not the IMGUI fallback
            // font. Cache per label so this does not recalculate every frame.
            var size = new Vector2(text.Length * pixels, pixels * 1.5f);
            try
            {
                if (_preferredValues?.Invoke(element.Component, new object[] { text }) is Vector2 preferred &&
                    preferred.x > 0 && preferred.y > 0 && !float.IsInfinity(preferred.x) && !float.IsInfinity(preferred.y))
                    size = preferred;
            }
            catch { /* Conservative visible fallback on TMP schema changes. */ }
            element.MeasuredText = text; element.MeasuredFont = _font; element.MeasuredPixels = pixels; element.MeasuredSize = size;
            return size;
        }
        public void Box(string key, Rect rectangle, Color color, Sprite? sprite = null)
        {
            var element = Get(key, rectangle, _imageType);
            if (element.Color != color) { Set(element.Component, "color", color); element.Color = color; }
            if (element.Sprite != sprite)
            {
                Set(element.Component, "sprite", sprite); element.Sprite = sprite;
                Set(element.Component, "preserveAspect", sprite != null);
            }
        }
        public void Border(string key, Rect rect, Color color, float width = 2)
        {
            Box(key + "t", new Rect(rect.x, rect.y, rect.width, width), color);
            Box(key + "b", new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Box(key + "l", new Rect(rect.x, rect.y, width, rect.height), color);
            Box(key + "r", new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }
        public void Corners(string key, Rect rect, Color color, float length, float width)
        {
            Box(key + "tlh", new Rect(rect.x, rect.y, length, width), color);
            Box(key + "tlv", new Rect(rect.x, rect.y, width, length), color);
            Box(key + "trh", new Rect(rect.xMax - length, rect.y, length, width), color);
            Box(key + "trv", new Rect(rect.xMax - width, rect.y, width, length), color);
            Box(key + "blh", new Rect(rect.x, rect.yMax - width, length, width), color);
            Box(key + "blv", new Rect(rect.x, rect.yMax - length, width, length), color);
            Box(key + "brh", new Rect(rect.xMax - length, rect.yMax - width, length, width), color);
            Box(key + "brv", new Rect(rect.xMax - width, rect.yMax - length, width, length), color);
        }
        public void End()
        {
            foreach (var key in _elements.Where(x => x.Value.Generation != _generation).Select(x => x.Key).ToArray())
            {
                UnityEngine.Object.Destroy(_elements[key].Object); _elements.Remove(key);
            }
        }
        private static void Set(object target, string name, object? value)
        {
            var member = Members.Find(target.GetType(), name);
            if (member is PropertyInfo property && property.CanWrite) property.SetValue(target, value);
            else if (member is FieldInfo field) field.SetValue(target, value);
        }
        private static void SetEnum(object target, string name, string value)
        {
            if (Members.Find(target.GetType(), name) is PropertyInfo property && property.PropertyType.IsEnum)
                property.SetValue(target, Enum.Parse(property.PropertyType, value));
        }
        public void Dispose() { _elements.Clear(); if (_root != null) UnityEngine.Object.Destroy(_root); }
    }
}
