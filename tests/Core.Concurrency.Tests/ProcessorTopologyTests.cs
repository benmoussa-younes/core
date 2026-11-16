// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Concurrency.Platform.Linux;

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards the CPU-topology read against a temporary <c>sysfs</c> fixture, so every
/// branch — a full tree, a missing tree, a restricted affinity set, an unreadable
/// file — is driven deterministically instead of depending on the host.
/// </summary>
public sealed class ProcessorTopologyTests
{
    [Fact]
    public void The_live_physical_core_count_is_positive_and_bounded()
    {
        Assert.InRange(
            ProcessorTopology.PhysicalCoreCount(),
            1,
            ProcessorTopology.AllowedProcessorCount());
    }

    [Fact]
    public void The_live_allowed_processor_count_is_positive()
    {
        Assert.True(ProcessorTopology.AllowedProcessorCount() >= 1);
    }

    [Fact]
    public void A_six_core_three_package_tree_counts_six_cores()
    {
        using var fixture = TopologyFixture.Create(packages: 3, coresPerPackage: 2, logicalPerCore: 2);

        // 3 packages x 2 cores x 2 logical = 12 logical CPUs, 6 distinct cores.
        Assert.Equal(6, ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 12));
    }

    [Fact]
    public void A_missing_tree_falls_back_to_the_allowed_count()
    {
        using var fixture = TopologyFixture.Create(packages: 1, coresPerPackage: 1, logicalPerCore: 1);
        fixture.RemoveRoot();

        Assert.Equal(7, ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 7));
    }

    [Fact]
    public void A_restricted_affinity_set_counts_only_the_allowed_cpus()
    {
        using var fixture = TopologyFixture.Create(packages: 1, coresPerPackage: 8, logicalPerCore: 1);

        // Only the first four CPUs are allowed, so four cores are counted.
        Assert.Equal(4, ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 4));
    }

    [Fact]
    public void An_unreadable_topology_file_is_treated_as_unknown()
    {
        using var fixture = TopologyFixture.Create(packages: 1, coresPerPackage: 4, logicalPerCore: 1);

        // cpu0 keeps its files (the readability guard checks cpu0); cpu1's core_id
        // is removed so its read fails and is caught as unknown.
        fixture.RemoveTopologyFile(1, "core_id");

        var count = ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 4);

        // cpu0 contributes its real pair; cpu1 contributes "?:?"; the distinct set
        // is what the core count reports.
        Assert.InRange(count, 1, 4);
    }

    [Fact]
    public void An_odd_affinity_mask_is_counted_bit_by_bit()
    {
        // 0b1010_0001 has three set bits.
        Assert.Equal(3, ProcessorTopology.CountBits([0b1010_0001]));

        // An all-zero mask still reports one processor, never zero.
        Assert.Equal(1, ProcessorTopology.CountBits([0]));
    }

    [Fact]
    public void A_sparse_affinity_set_reads_the_allowed_cpus_not_a_contiguous_prefix()
    {
        // MINOR 8 regression: the count used to iterate cpu0..cpuN (N = the count of
        // allowed cpus), so a sparse cpuset read the wrong entries or stopped at the
        // hole. Every cpu here is a distinct core; the mask allows cpu0..cpu3 and
        // cpu8..cpu11 (8 cpus), so the count is 8, not a prefix of 8 cpus.
        using var fixture = TopologyFixture.Create(packages: 1, coresPerPackage: 16, logicalPerCore: 1);

        var mask = new byte[ProcessorTopology.MaximumCpuCount / 8];
        foreach (var cpu in new[] { 0, 1, 2, 3, 8, 9, 10, 11 })
        {
            mask[cpu >> 3] |= (byte)(1 << (cpu & 7));
        }

        Assert.Equal(8, ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 8, affinity: mask));
    }

    [Fact]
    public void A_sparse_affinity_set_skips_a_hole_before_an_allowed_cpu()
    {
        // The fixture has cpu0..cpu5; the mask allows cpu0 and cpu5, with a hole in
        // between. The lower count (2) must not stop the walk at the hole.
        using var fixture = TopologyFixture.Create(packages: 1, coresPerPackage: 6, logicalPerCore: 1);

        var mask = new byte[ProcessorTopology.MaximumCpuCount / 8];
        mask[0] |= 1 << 0;
        mask[0] |= 1 << 5;

        Assert.Equal(2, ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 2, affinity: mask));
    }

    [Fact]
    public void A_hole_in_a_contiguous_tree_ends_the_walk_without_a_mask()
    {
        // No affinity mask: the tree is contiguous and cpu0..cpu(allowed-1) is read.
        // A missing cpu ends the walk rather than skipping to the next index.
        using var fixture = TopologyFixture.Create(packages: 1, coresPerPackage: 6, logicalPerCore: 1);
        fixture.RemoveCpu(3);

        // cpu0..cpu2 are read, cpu3 is the hole that ends the walk: three cores.
        Assert.Equal(3, ProcessorTopology.CountPhysicalCores(fixture.Root, allowed: 6));
    }

    [Fact]
    public void Pinning_an_all_zero_mask_is_a_best_effort_failure()
    {
        // The defensive tail: an all-zero mask has no bit to pin to, so the request
        // reports false instead of pinning to processor 0 or dividing by zero.
        var mask = new byte[ProcessorTopology.MaximumCpuCount / 8];

        Assert.False(ProcessorTopology.PinCurrentThread(ordinal: 0, mask, allowed: 1));
    }

    [Fact]
    public void Pinning_a_thread_is_best_effort_and_never_throws()
    {
        // MINOR 7 regression: PinToCpu was a silent no-op. On Linux the worker now
        // calls sched_setaffinity; the request is advisory, so a host that refuses
        // it (a restrictive cpuset) is not an error. When it succeeds, the mask
        // holds exactly one allowed processor.
        using var context = ConcurrencyContext.Create(new ConcurrencyConfiguration { WorkerCount = 2, PinToCpu = true });

        var pinned = new bool[2];
        var maskBits = new int[2];

        context.For(
            0,
            2,
            1,
            index =>
            {
                pinned[index] = ProcessorTopology.PinCurrentThread((int)index);
                maskBits[index] = ProcessorTopology.CountBits(ProcessorTopology.CurrentThreadMask() ?? [0]);
            },
            new ConcurrencyOptions { Partition = WorkPartition.StaticChunks });

        // The call either pinned (mask = 1) or was refused (mask > 1); it never
        // crashed. Where the kernel accepted it the mask is exactly one cpu.
        for (var index = 0; index < 2; index++)
        {
            if (pinned[index])
            {
                Assert.Equal(1, maskBits[index]);
            }
        }
    }

    [Fact]
    public void The_affinity_fallback_is_never_zero()
    {
        Assert.True(ProcessorTopology.FallbackProcessorCount() >= 1);
    }

    [Fact]
    public void A_cgroup_v2_quota_is_rounded_up_to_whole_processors()
    {
        using var fixture = CgroupFixture.Create();
        fixture.WriteV2("250000 100000");

        // 250000 us of 100000 us period = 2.5 processors, rounded up to 3.
        Assert.Equal(3, ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    [Fact]
    public void A_cgroup_v2_max_line_reports_no_quota()
    {
        using var fixture = CgroupFixture.Create();
        fixture.WriteV2("max 100000");

        Assert.Null(ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("50000")]          // one field only
    [InlineData("0 100000")]       // zero quota
    [InlineData("50000 0")]        // zero period
    [InlineData("x 100000")]       // unparsable quota
    [InlineData("50000 y")]        // unparsable period
    public void A_malformed_cgroup_v2_line_reports_no_quota(string contents)
    {
        using var fixture = CgroupFixture.Create();
        fixture.WriteV2(contents);

        Assert.Null(ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    [Fact]
    public void A_cgroup_v1_quota_is_rounded_up_to_whole_processors()
    {
        using var fixture = CgroupFixture.Create();
        fixture.WriteV1(quota: "150000", period: "100000");

        // 150000 us of 100000 us = 1.5 processors, rounded up to 2.
        Assert.Equal(2, ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    [Theory]
    [InlineData("0", "100000")]     // a zero quota means "no limit" in v1
    [InlineData("150000", "0")]     // a zero period is invalid
    [InlineData("x", "100000")]     // unparsable quota
    [InlineData("150000", "y")]     // unparsable period
    public void A_cgroup_v1_line_without_a_positive_quota_reports_no_quota(string quota, string period)
    {
        using var fixture = CgroupFixture.Create();
        fixture.WriteV1(quota, period);

        Assert.Null(ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    [Fact]
    public void A_missing_cgroup_tree_reports_no_quota()
    {
        using var fixture = CgroupFixture.Create();

        // Neither cpu.max (v2) nor cpu/ (v1) exists: the affinity count stands.
        Assert.Null(ProcessorTopology.QuotaProcessorCount(fixture.Root));
        Assert.Null(ProcessorTopology.QuotaProcessorCount());
    }

    [Fact]
    public void An_unreadable_cgroup_file_reports_no_quota()
    {
        using var fixture = CgroupFixture.Create();

        // A file that exists (so File.Exists is true) but whose mode is 000 cannot be
        // read: ReadText's catch turns the failure into "no quota". The suite runs
        // unprivileged, so the mode bits are enforced for this process.
        fixture.WriteUnreadableV2();

        Assert.Null(ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    [Fact]
    public void A_cgroup_quota_beyond_int_max_is_clamped()
    {
        using var fixture = CgroupFixture.Create();

        // A quota far beyond int.MaxValue processors must clamp, never wrap.
        fixture.WriteV2($"{long.MaxValue / 2} 1");

        Assert.Equal(int.MaxValue, ProcessorTopology.QuotaProcessorCount(fixture.Root));
    }

    /// <summary>
    /// A temporary cgroup mount root: <c>cpu.max</c> for v2 and
    /// <c>cpu/cpu.cfs_quota_us</c> + <c>cpu/cpu.cfs_period_us</c> for v1.
    /// </summary>
    private sealed class CgroupFixture : IDisposable
    {
        private CgroupFixture(string root)
        {
            Root = root;
        }

        internal string Root { get; }

        internal static CgroupFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"core-cgroup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new CgroupFixture(root);
        }

        internal void WriteV2(string contents) =>
            File.WriteAllText(Path.Combine(Root, "cpu.max"), contents);

        internal void WriteUnreadableV2()
        {
            var path = Path.Combine(Root, "cpu.max");
            File.WriteAllText(path, "100000 100000");
            File.SetUnixFileMode(path, UnixFileMode.None);
        }

        internal void WriteV1(string quota, string period)
        {
            var v1 = Path.Combine(Root, "cpu");
            Directory.CreateDirectory(v1);
            File.WriteAllText(Path.Combine(v1, "cpu.cfs_quota_us"), quota);
            File.WriteAllText(Path.Combine(v1, "cpu.cfs_period_us"), period);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    /// <summary>
    /// A temporary <c>sysfs</c> <c>cpu</c> tree: one directory per logical CPU,
    /// each with a <c>topology</c> holding a package id and a core id.
    /// </summary>
    private sealed class TopologyFixture : IDisposable
    {
        private TopologyFixture(string root)
        {
            Root = root;
        }

        internal string Root { get; }

        internal static TopologyFixture Create(int packages, int coresPerPackage, int logicalPerCore)
        {
            var root = Path.Combine(Path.GetTempPath(), $"core-topology-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var cpu = 0;
            for (var package = 0; package < packages; package++)
            {
                for (var core = 0; core < coresPerPackage; core++)
                {
                    for (var logical = 0; logical < logicalPerCore; logical++)
                    {
                        var topology = Path.Combine(root, $"cpu{cpu}", "topology");
                        Directory.CreateDirectory(topology);
                        File.WriteAllText(Path.Combine(topology, "physical_package_id"), package.ToString());
                        File.WriteAllText(Path.Combine(topology, "core_id"), core.ToString());
                        cpu++;
                    }
                }
            }

            return new TopologyFixture(root);
        }

        internal void RemoveRoot() => Directory.Delete(Root, recursive: true);

        internal void RemoveCpu(int cpu) =>
            Directory.Delete(Path.Combine(Root, $"cpu{cpu}"), recursive: true);

        internal void RemoveTopologyFile(int cpu, string file) =>
            File.Delete(Path.Combine(Root, $"cpu{cpu}", "topology", file));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
