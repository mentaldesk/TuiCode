using System.Reflection;
using System.Runtime.CompilerServices;
using TuiCode.Editor;

namespace TuiCode.Tests;

// UnsafeAccessor and reflection only fail when they're used, so a TG upgrade that renames a private member breaks an edit at runtime.
public class TerminalGuiInternalsTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static TheoryData<string> Accessors() =>
        [.. AccessorMethods().Select(m => $"{m.DeclaringType!.Name}.{m.Name}")];

    [Theory]
    [MemberData(nameof(Accessors))]
    public void Every_unsafe_accessor_finds_its_member(string accessor)
    {
        var method = AccessorMethods().Single(m => $"{m.DeclaringType!.Name}.{m.Name}" == accessor);
        var attribute = method.GetCustomAttribute<UnsafeAccessorAttribute>()!;
        var parameters = method.GetParameters();
        var target = TypeOf(parameters[0]);
        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
            target = typeof(List<>);

        if (attribute.Kind is UnsafeAccessorKind.Field)
        {
            Assert.NotNull(target.GetField(attribute.Name!, Members));
            return;
        }

        var arguments = parameters.Skip(1).Select(TypeOf).Select(t => t.IsByRef ? t.GetElementType()! : t).ToArray();
        var found = target.GetMethods(Members).Where(m => m.Name == attribute.Name)
            .Any(m => m.GetParameters().Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType).SequenceEqual(arguments));
        Assert.True(found, $"{target.Name} has no {attribute.Name}({string.Join(", ", arguments.Select(a => a.Name))})");
    }

    [Fact]
    public void Every_reflected_field_is_found()
    {
        var reflected = typeof(EditorTab).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(FieldInfo))
            .ToList();

        Assert.NotEmpty(reflected);
        Assert.All(reflected, f => Assert.NotNull(f.GetValue(null)));
    }

    private static IEnumerable<MethodInfo> AccessorMethods() =>
        typeof(EditorTab).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<UnsafeAccessorAttribute>() is not null);

    private static Type TypeOf(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<UnsafeAccessorTypeAttribute>() is { } named
            ? Type.GetType(named.TypeName, throwOnError: true)!
            : parameter.ParameterType;
}
