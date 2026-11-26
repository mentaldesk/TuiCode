using TuiCode.Syntax;
using TuiCode.Workbench.Usages;

namespace TuiCode.Tests;

public class FileDefinitionsTests
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private static readonly string[] Targets =
    [
        "<Project>",                                                        // 0
        "  <ItemGroup>",                                                    // 1
        "    <SentryNativeBuildInputs Include=\"CMakeLists.txt\" />",       // 2
        "    <Compile Include=\"a.cs\">",                                   // 3
        "      <Link>a.cs</Link>",                                          // 4
        "    </Compile>",                                                   // 5
        "  </ItemGroup>",                                                   // 6
        "  <!-- <Target Name=\"Commented\"> -->",                           // 7
        "  <Target Name=\"_BuildSentryNativeSDK\"",                         // 8
        "          Inputs=\"@(SentryNativeBuildInputs)\">",                 // 9
        "    <PropertyGroup>",                                              // 10
        "      <NativeFlags>$(Flags);-O2</NativeFlags>",                    // 11
        "    </PropertyGroup>",                                             // 12
        "    <Exec Command=\"cmake $(NativeFlags)\" />",                    // 13
        "  </Target>",                                                      // 14
        "  <Target Name=\"_SetupSentryNative\" DependsOnTargets=\"A;_BuildSentryNativeSDK\" />", // 15
        "</Project>",                                                       // 16
    ];

    [Fact]
    public void MsBuild_defines_targets_and_the_properties_and_items_its_groups_hold()
    {
        var definitions = FileDefinitions.MsBuild(Targets);

        Assert.Equal(
        [
            new("SentryNativeBuildInputs", 2, 5, 2),
            new("Compile", 3, 5, 5),
            new("_BuildSentryNativeSDK", 8, 16, 14),
            new("NativeFlags", 11, 7, 11),
            new("_SetupSentryNative", 15, 16, 15),
        ], definitions.Declarations);
    }

    [Theory]
    [InlineData(9, "_BuildSentryNativeSDK")]
    [InlineData(13, "_BuildSentryNativeSDK")]
    [InlineData(11, "NativeFlags")]
    [InlineData(4, "Compile")]
    [InlineData(15, "_SetupSentryNative")]
    [InlineData(0, null)]
    [InlineData(7, null)]
    [InlineData(16, null)]
    public void Around_is_the_innermost_target_property_or_item_a_line_is_in(int line, string? name) =>
        Assert.Equal(name, FileDefinitions.MsBuild(Targets).Around(line)?.Name);

    [Fact]
    public void Defines_is_true_only_on_the_line_that_names_the_definition()
    {
        var definitions = FileDefinitions.MsBuild(Targets);

        Assert.True(definitions.Defines("SentryNativeBuildInputs", 2));
        Assert.False(definitions.Defines("SentryNativeBuildInputs", 9));
        Assert.True(definitions.Defines("_BuildSentryNativeSDK", 8));
        Assert.False(definitions.Defines("_BuildSentryNativeSDK", 15));
    }

    [Theory]
    [InlineData("Sentry.Native.targets", true)]
    [InlineData("Directory.Build.props", true)]
    [InlineData("Sentry.csproj", true)]
    [InlineData("build.proj", true)]
    [InlineData("Program.cs", false)]
    [InlineData("pom.xml", false)]
    public void IsMsBuild_takes_targets_props_and_every_proj(string path, bool msBuild) =>
        Assert.Equal(msBuild, FileDefinitions.IsMsBuild(path));

    [Fact]
    public void FromSymbols_runs_each_definition_as_far_as_sticky_lines_pins_it_and_a_one_liner_to_its_own_line()
    {
        string[] lines =
        [
            "class Diff",                       // 0
            "{",                                // 1
            "    int Hunks() => 0;",            // 2
            "    void Print()",                 // 3
            "    {",                            // 4
            "        Hunks();",                 // 5
            "    }",                            // 6
            "}",                                // 7
            "var top = 1;",                     // 8
        ];
        var scan = Syntax.CreateSymbolScan(Syntax.LanguageById("csharp"), lines)!;
        scan.LineTimeLimit = TimeSpan.FromMinutes(1);
        Assert.True(scan.Advance(TimeSpan.MaxValue));

        var definitions = FileDefinitions.FromSymbols(scan.Symbols, scan.Lines);

        Assert.Equal("Print", definitions.Around(5)?.Name);
        Assert.Equal("Hunks", definitions.Around(2)?.Name);
        Assert.Equal("Diff", definitions.Around(1)?.Name);
        Assert.Equal(new Declaration("Print", 3, 9, 6), definitions.Around(4));
        Assert.True(definitions.Defines("Hunks", 2));
        Assert.False(definitions.Defines("Hunks", 5));
    }
}
