using System.Reflection;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;

namespace TuiCode.Tests;

// A text box is the library's PlaceholderField or MultiLineField, which paint their own caret (#668).
public class TextBoxTypesTests
{
#pragma warning disable CS0618 // TextView is obsolete, which is the point.
    private static readonly Type[] Forbidden = [typeof(TextField), typeof(TextView)];
#pragma warning restore CS0618

    private static readonly Assembly[] Ours =
    [
        typeof(WorkbenchHost).Assembly, typeof(TuiCode.Search.SearchView).Assembly, typeof(PaneTabs).Assembly,
        typeof(EditorTab).Assembly, typeof(FileExplorerView).Assembly,
    ];

    [Fact]
    public void No_view_creates_a_plain_TextField_or_a_TextView()
    {
        var offenders = Ours
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(Constructs)
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"Use PlaceholderField or MultiLineField in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void No_view_of_ours_is_a_TextField_or_a_TextView()
    {
        var offenders = Ours
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => Forbidden.Any(forbidden => forbidden.IsAssignableFrom(type)))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, $"Build on PlaceholderField or MultiLineField instead: {string.Join(", ", offenders)}");
    }

    private static bool Constructs(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) return false;
        const byte newobj = 0x73;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != newobj) continue;
            try
            {
                var target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1),
                    method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (Forbidden.Contains(target?.DeclaringType)) return true;
            }
            catch (ArgumentException)
            {
            }
        }
        return false;
    }
}
