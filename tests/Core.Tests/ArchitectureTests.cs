// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Reflection;
using Core.UI.Hosting;
using Core.Concurrency;
using Core.IO.Mapped;
using Core.Mvp.Services;
using Core.Primitives;
using Core.Serialization.Documents;

namespace Core.Tests;

/// <summary>
/// Guards the assembly dependency direction: the graph must stay a DAG with no
/// sibling or downward reference, so a lower layer never depends on a higher one.
/// </summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Core_is_a_leaf()
    {
        Assert.Empty(CoreReferences(typeof(ByteSize).Assembly));
    }

    [Fact]
    public void IO_depends_on_Core_and_Platform()
    {
        var references = CoreReferences(typeof(MappedFile).Assembly);

        Assert.Contains("Core", references);
        Assert.Contains("Core.Platform", references);
        Assert.DoesNotContain("Core.Mvp", references);
        Assert.DoesNotContain("Core.UI", references);
        Assert.DoesNotContain("Core.Serialization", references);
    }

    [Fact]
    public void Concurrency_is_a_leaf()
    {
        // The library needs no other Core assembly: its failures are typed
        // results of its own (ConcurrencyResult), not CoreError, because an abort is
        // a normal return. A leaf keeps the dependency graph a DAG with no edge
        // to justify or guard.
        var references = CoreReferences(typeof(ConcurrencyContext).Assembly);

        Assert.Empty(references);
    }

    [Fact]
    public void Mvp_depends_on_Core_and_Platform()
    {
        var references = CoreReferences(typeof(IService).Assembly);

        Assert.Contains("Core", references);
        Assert.Contains("Core.Platform", references);
        Assert.DoesNotContain("Core.IO", references);
        Assert.DoesNotContain("Core.UI", references);
        Assert.DoesNotContain("Core.Serialization", references);
    }

    [Fact]
    public void Serialization_depends_on_Core_IO_and_Platform()
    {
        var references = CoreReferences(typeof(DataDocument).Assembly);

        Assert.Contains("Core", references);
        Assert.Contains("Core.IO", references);
        Assert.Contains("Core.Platform", references);
        Assert.DoesNotContain("Core.Mvp", references);
        Assert.DoesNotContain("Core.UI", references);
    }

    [Fact]
    public void Avalonia_depends_on_Mvp_and_Platform_not_Serialization()
    {
        // The shell receives its platform services through CorePlatform, so no
        // Linux type reaches the UI assembly. Serialization stays out of the UI
        // layer: a product composes its data stores itself.
        var references = CoreReferences(typeof(CoreAppBuilder).Assembly);

        Assert.Contains("Core.Mvp", references);
        Assert.Contains("Core.Platform", references);
        Assert.DoesNotContain("Core.Platform.Linux", references);
        Assert.DoesNotContain("Core.IO", references);
        Assert.DoesNotContain("Core.Serialization", references);
        Assert.DoesNotContain("Core.Concurrency", references);
    }

    private static IReadOnlySet<string> CoreReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name == "Core" || name.StartsWith("Core.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
}
