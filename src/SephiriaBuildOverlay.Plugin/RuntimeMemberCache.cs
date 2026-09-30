using System.Reflection;

namespace SephiriaBuildOverlay.Plugin;

// Store metadata only, never a Unity object or mutable game state. Missing
// members are cached too; probing fallback names must not enumerate a type again.
internal sealed class RuntimeMemberCache
{
    private readonly Dictionary<Type, Dictionary<string, MemberInfo>> _types = new();
    public int TypesInspected => _types.Count;

    public MemberInfo? Find(Type type, params string[] names)
    {
        if (!_types.TryGetValue(type, out var members))
        {
            members = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var current = type; current is not null; current = current.BaseType)
                foreach (var member in current.GetMembers(flags))
                    if ((member is FieldInfo || member is PropertyInfo property && property.GetIndexParameters().Length == 0) && !members.ContainsKey(member.Name))
                        members.Add(member.Name, member);
            _types.Add(type, members);
        }
        foreach (var name in names)
            if (members.TryGetValue(name, out var member)) return member;
        return null;
    }
}
