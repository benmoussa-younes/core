// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Core.Concurrency.Tests;

/// <summary>
/// Guards <see cref="ConcurrencyResult"/>, the outcome value every call returns.
/// </summary>
public sealed class ConcurrencyResultTests
{
    [Fact]
    public void Completed_has_no_failure()
    {
        var result = ConcurrencyResult.Completed();

        Assert.Equal(ConcurrencyStatus.Completed, result.Status);
        Assert.True(result.IsCompleted);
        Assert.Empty(result.Failures);
        Assert.Null(result.FirstFailure);
    }

    [Fact]
    public void Aborted_carries_the_first_failure()
    {
        var failure = new InvalidOperationException("boom");

        var result = ConcurrencyResult.Aborted(failure);

        Assert.Equal(ConcurrencyStatus.Aborted, result.Status);
        Assert.False(result.IsCompleted);
        Assert.Same(failure, result.FirstFailure);
        Assert.Same(failure, result.Failures[0]);
    }

    [Fact]
    public void Collected_carries_every_failure_in_order()
    {
        Exception[] failures = [new InvalidOperationException("a"), new InvalidOperationException("b")];

        var result = ConcurrencyResult.Collected(failures);

        Assert.Equal(ConcurrencyStatus.Faulted, result.Status);
        Assert.Equal(failures, result.Failures);
        Assert.Same(failures[0], result.FirstFailure);
    }

    [Fact]
    public void ThrowIfFailed_rethrows_the_first_failure()
    {
        var failure = new InvalidOperationException("boom");
        var result = ConcurrencyResult.Aborted(failure);

        var thrown = Assert.Throws<InvalidOperationException>(result.ThrowIfFailed);

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void ThrowIfFailed_is_a_no_op_when_nothing_failed()
    {
        ConcurrencyResult.Completed().ThrowIfFailed();
    }

    [Fact]
    public void A_null_failure_is_a_programmer_error()
    {
        Assert.Throws<ArgumentNullException>(() => ConcurrencyResult.Aborted(null!));
        Assert.Throws<ArgumentNullException>(() => ConcurrencyResult.Collected(null!));
    }

    [Fact]
    public void A_result_with_no_failures_is_completed_without_needing_the_factory()
    {
        var result = new ConcurrencyResult(ConcurrencyStatus.Completed);

        Assert.True(result.IsCompleted);
        Assert.Empty(result.Failures);
    }
}
