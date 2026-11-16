// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Reflection;
using System.Text.RegularExpressions;
using Core.Results;

namespace Core.Tests;

/// <summary>
/// Guards the error-code catalogue: every <c>new CoreError("...")</c> literal in
/// the shipped source must be a value of a <see cref="CoreErrorCodes"/> constant,
/// and the constants must be unique. A new code added with a literal and no
/// constant fails here.
/// </summary>
public sealed partial class CoreErrorCodesTests
{
    [Fact]
    public void Every_emitted_code_is_in_the_catalogue()
    {
        var known = KnownCodes();
        var found = EmittedCodes();

        Assert.NotEmpty(found);

        var unknown = found.Where(code => !known.Contains(code)).ToList();

        Assert.True(
            unknown.Count == 0,
            $"Error codes emitted in src/ but missing from CoreErrorCodes: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void The_catalogue_has_no_duplicate_codes()
    {
        var duplicates = typeof(CoreErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true })
            .GroupBy(field => (string)field.GetRawConstantValue()!)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate error codes: {string.Join(", ", duplicates)}");
    }

    private static HashSet<string> KnownCodes() =>
        typeof(CoreErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    private static List<string> EmittedCodes()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in CoreErrorLiteral().Matches(File.ReadAllText(file)))
            {
                found.Add(match.Groups[1].Value);
            }
        }

        return [.. found];
    }

    [GeneratedRegex("CoreError\\(\\s*\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex CoreErrorLiteral();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Core.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Core.slnx was not found above the test output directory.");
    }
}
