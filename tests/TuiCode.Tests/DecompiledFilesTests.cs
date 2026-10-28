using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class DecompiledFilesTests
{
    private const string ConsoleUri = "csharp:/work/App.csproj/decompiled/System.Console.cs";

    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Csharp_ls_source_is_written_to_the_temp_folder_and_titled_after_its_type()
    {
        var files = new DecompiledFiles(_fs);

        var file = files.Write(ConsoleUri, new DecompiledSource("System.Console", "System.Console", "public static class Console { }"));

        Assert.StartsWith(_fs.Path.Combine(_fs.Path.GetTempPath(), "TuiCode", "decompiled"), file.Path, StringComparison.Ordinal);
        Assert.Equal("System.Console.cs", _fs.Path.GetFileName(file.Path));
        Assert.Equal("public static class Console { }", _fs.File.ReadAllText(file.Path));
        Assert.Equal("Console.cs [decompiled]", file.Title);
        Assert.Equal("System.Console [decompiled]", file.Place);
        Assert.Equal("Decompiled from System.Console, read-only", file.Origin);
        Assert.Equal(ConsoleUri, file.Uri);
        Assert.Same(file, files.At(file.Path));
    }

    [Fact]
    public void Another_projects_copy_of_a_type_gets_a_file_of_its_own()
    {
        var files = new DecompiledFiles(_fs);
        var source = new DecompiledSource("System.Console", "System.Console", "class Console { }");

        var first = files.Write(ConsoleUri, source);
        var second = files.Write("csharp:/work/Tests.csproj/decompiled/System.Console.cs", source);

        Assert.NotEqual(first.Path, second.Path);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List`1", "List.cs [decompiled]")]
    [InlineData("System.Environment+SpecialFolder", "SpecialFolder.cs [decompiled]")]
    public void A_generic_or_nested_type_is_titled_by_its_plain_name(string name, string title) =>
        Assert.Equal(title, new DecompiledFile("/tmp/x.cs", name, "System.Runtime").Title);

    [Fact]
    public void A_file_the_roslyn_server_wrote_under_MetadataAsSource_is_decompiled_from_the_assembly_its_header_names()
    {
        var path = _fs.Path.Combine(_fs.Path.GetTempPath(), "MetadataAsSource", "a28", "DecompilationMetadataAsSourceFileProvider", "69e", "Console.cs");
        _fs.AddFile(path, new MockFileData(
            "﻿#region Assembly System.Console, Version=10.0.0.0, Culture=neutral\n// Decompiled with ICSharpCode.Decompiler\n#endregion\n"));

        var file = new DecompiledFiles(_fs).At(path);

        Assert.Equal("Console.cs [decompiled]", file?.Title);
        Assert.Equal("Decompiled from System.Console, read-only", file?.Origin);
        Assert.Null(file?.Uri);
    }

    [Theory]
    [InlineData("void Console.WriteLine(string? value)", "WriteLine(string? value)")]
    [InlineData("void Console.WriteLine()\nmore", "WriteLine()")]
    [InlineData("class System.Console", null)]
    public void Quick_info_names_the_overload_called(string code, string? called) =>
        Assert.Equal(called, DecompiledFiles.Called([new HoverPart("csharp", code), new HoverPart(null, "Writes a line.")]));

    [Fact]
    public void Any_other_file_in_the_temp_folder_is_an_ordinary_file()
    {
        var path = _fs.Path.Combine(_fs.Path.GetTempPath(), "notes", "Console.cs");
        _fs.AddFile(path, new MockFileData("class Console { }"));

        Assert.Null(new DecompiledFiles(_fs).At(path));
    }
}
