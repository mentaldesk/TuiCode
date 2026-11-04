using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class CSharpNamespaceTests
{
    [Theory]
    [InlineData("using System;\n\nnamespace TuiCode.Shapes;\n\nclass A { }", "TuiCode.Shapes")]
    [InlineData("namespace TuiCode.Shapes\n{\n    namespace Inner { }\n}", "TuiCode.Shapes")]
    [InlineData("  namespace Indented {\n}", "Indented")]
    [InlineData("// namespace Commented;\nclass A { }", null)]
    [InlineData("class A { }", null)]
    public void Of_gives_the_first_namespace_the_file_declares(string text, string? expected) =>
        Assert.Equal(expected, CSharpNamespace.Of(text.Split('\n')));
}
