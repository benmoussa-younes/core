// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.RegularExpressions;
using Core.Mvp.Composition;
using Core.Mvp.Services;

namespace Core.Tests;

/// <summary>
/// Guards the source <em>placement</em> rules: the assembly count, folder equals
/// namespace, one public type per file and filename equals the type name. The
/// assembly <em>graph</em> is guarded separately by <see cref="ArchitectureTests"/>.
/// </summary>
public sealed class PlacementTests
{
    /// <summary>The target number of assemblies in <c>src/</c> (the seam rule).</summary>
    private const int ExpectedAssemblyCount = 8;

    /// <summary>
    /// Generic types whose plain filename is already owned by their non-generic
    /// counterpart use the conventional flattened name instead. Keep this list
    /// empty unless a collision makes the plain filename impossible.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FilenameExceptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The non-generic IPresenter owns IPresenter.cs, so the generic
            // IPresenter<TView> uses the flattened IPresenterOfT.cs.
            ["IPresenterOfT.cs"] = "IPresenter",
        };

    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SourceRoot = Path.Combine(RepositoryRoot, "src");

    private static readonly Regex NamespaceDeclaration = new(
        @"^namespace[ \t]+(?<ns>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // The type declarations are anchored at column 0, so a nested type is not
    // mistaken for a top-level one. This assumes every source file uses a
    // file-scoped namespace (it does); a block-scoped namespace would indent its
    // top-level type and hide it from these guards, so extend them if one appears.
    private static readonly Regex NamedTypeDeclaration = new(
        @"^(?<access>public|internal)[ \t]+(?:(?:sealed|static|abstract|readonly|partial|unsafe)[ \t]+)*(?:class|record(?:[ \t]+(?:class|struct))?|struct|interface|enum)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex DelegateDeclaration = new(
        @"^(?<access>public|internal)[ \t]+(?:(?:sealed|static|abstract|readonly|partial|unsafe)[ \t]+)*delegate[ \t]+[^\s]+[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// The shell composition points (the application host, the view host and
    /// navigation) are chrome, not views, so they live in <c>Mvp/Composition</c>.
    /// </summary>
    [Fact]
    public void Hosts_live_in_Mvp_Composition()
    {
        Assert.Equal("Core.Mvp.Composition", typeof(IApplicationHost).Namespace);
        Assert.Equal("Core.Mvp.Composition", typeof(IViewHost).Namespace);
        Assert.Equal("Core.Mvp.Composition", typeof(INavigationHost).Namespace);
    }

    /// <summary>The service contracts a presenter consumes live in <c>Mvp/Services</c>.</summary>
    [Fact]
    public void Service_contracts_live_in_Mvp_Services()
    {
        Assert.Equal("Core.Mvp.Services", typeof(IService).Namespace);
        Assert.Equal("Core.Mvp.Services", typeof(IDialogService).Namespace);
        Assert.Equal("Core.Mvp.Services", typeof(INotificationService).Namespace);
        Assert.Equal("Core.Mvp.Services", typeof(ITrayService).Namespace);
    }

    [Fact]
    public void Source_holds_exactly_the_target_assemblies()
    {
        var assemblies = Directory.GetDirectories(SourceRoot)
            .Where(directory => Directory.EnumerateFiles(directory, "*.csproj").Any())
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedAssemblyCount, assemblies.Length);
    }

    [Fact]
    public void Every_source_file_namespace_matches_its_folder()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            var match = NamespaceDeclaration.Match(File.ReadAllText(file));
            if (!match.Success)
            {
                violations.Add($"{Relative(file)}: no namespace declaration");
                continue;
            }

            var expected = ExpectedNamespace(file);
            var actual = match.Groups["ns"].Value;
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                violations.Add($"{Relative(file)}: namespace {actual} (expected {expected})");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_source_file_declares_at_most_one_public_type()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            var publicTypes = TypeDeclarations(file)
                .Where(declaration => declaration.IsPublic)
                .Select(declaration => declaration.Name)
                .ToArray();
            if (publicTypes.Length > 1)
            {
                violations.Add($"{Relative(file)}: {publicTypes.Length} public types ({string.Join(", ", publicTypes)})");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_source_file_is_named_after_a_declared_type()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            var declarations = TypeDeclarations(file);
            var publicTypes = declarations
                .Where(declaration => declaration.IsPublic)
                .Select(declaration => declaration.Name)
                .ToArray();
            if (publicTypes.Length > 1)
            {
                continue; // Reported by Every_source_file_declares_at_most_one_public_type.
            }

            // The file is named after its public type; an internal-only helper
            // file is named after the helper type it declares.
            var expectedNames = publicTypes.Length == 1
                ? publicTypes
                : declarations.Select(declaration => declaration.Name).ToArray();

            var actualName = Path.GetFileNameWithoutExtension(file);
            if (expectedNames.Contains(actualName, StringComparer.Ordinal))
            {
                continue;
            }

            if (expectedNames.Length > 0 &&
                FilenameExceptions.TryGetValue(Path.GetFileName(file), out var allowed) &&
                expectedNames.Contains(allowed, StringComparer.Ordinal))
            {
                continue;
            }

            violations.Add($"{Relative(file)}: file name {actualName} (declared {string.Join(", ", expectedNames)})");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> EnumerateSourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "obj" or "bin");

    private static IReadOnlyList<TypeDeclaration> TypeDeclarations(string file)
    {
        var text = File.ReadAllText(file);
        var declarations = new List<TypeDeclaration>();
        foreach (Match match in NamedTypeDeclaration.Matches(text))
        {
            declarations.Add(ToDeclaration(match));
        }

        foreach (Match match in DelegateDeclaration.Matches(text))
        {
            declarations.Add(ToDeclaration(match));
        }

        return declarations;
    }

    private static TypeDeclaration ToDeclaration(Match match) =>
        new(match.Groups["name"].Value,
            string.Equals(match.Groups["access"].Value, "public", StringComparison.Ordinal));

    private readonly record struct TypeDeclaration(string Name, bool IsPublic);

    private static string ExpectedNamespace(string file)
    {
        var segments = Path.GetRelativePath(SourceRoot, file)
            .Split(Path.DirectorySeparatorChar);
        var assembly = segments[0];
        var folders = segments[1..^1];

        return folders.Length == 0
            ? assembly
            : assembly + "." + string.Join('.', folders);
    }

    private static string Relative(string file) =>
        Path.GetRelativePath(RepositoryRoot, file);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Core.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Core.slnx was not found above the test output directory.");
    }
}
