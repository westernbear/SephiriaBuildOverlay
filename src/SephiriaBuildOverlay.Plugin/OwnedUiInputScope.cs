namespace SephiriaBuildOverlay.Plugin;

// Compare identities, not names: equally named native actions stay blocked.
internal sealed class OwnedUiInputScope
{
    private readonly object _module;
    private readonly object[] _actions;
    public OwnedUiInputScope(object module, IEnumerable<object> actions)
    { _module = module ?? throw new ArgumentNullException(nameof(module)); _actions = actions.ToArray(); }
    public bool AllowsModule(object module, bool visible) => visible && ReferenceEquals(module, _module);
    public bool AllowsAction(object action, bool visible) => visible && _actions.Any(x => ReferenceEquals(x, action));
}
