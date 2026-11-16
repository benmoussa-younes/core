// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Results;

namespace Core.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_exposes_value_and_state()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_exposes_error_and_state()
    {
        var error = new CoreError("usage", "invalid arguments");

        var result = Result<int>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Value_on_failure_throws()
    {
        var result = Result<int>.Failure(new CoreError("code", "message"));

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Error_on_success_throws()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void Failure_with_null_error_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }

    [Fact]
    public void TryGetValue_returns_the_value_on_success()
    {
        var result = Result<string>.Success("value");

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void TryGetValue_returns_false_on_failure()
    {
        var result = Result<string>.Failure(new CoreError("code", "message"));

        Assert.False(result.TryGetValue(out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetError_returns_the_error_on_failure()
    {
        var error = new CoreError("code", "message");

        var result = Result<string>.Failure(error);

        Assert.True(result.TryGetError(out var actual));
        Assert.Same(error, actual);
    }

    [Fact]
    public void TryGetError_returns_false_on_success()
    {
        var result = Result<string>.Success("value");

        Assert.False(result.TryGetError(out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Map_projects_the_successful_value()
    {
        var result = Result<int>.Success(21);

        var projected = result.Map(value => value * 2);

        Assert.True(projected.IsSuccess);
        Assert.Equal(42, projected.Value);
    }

    [Fact]
    public void Map_propagates_the_failure()
    {
        var error = new CoreError("code", "message");
        var result = Result<int>.Failure(error);

        var projected = result.Map(value => value * 2);

        Assert.True(projected.IsFailure);
        Assert.Same(error, projected.Error);
    }

    [Fact]
    public void Map_rejects_a_null_projection()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).Map<int>(null!));
    }

    [Fact]
    public void Bind_projects_the_successful_value()
    {
        var result = Result<int>.Success(21);

        var bound = result.Bind(value => Result<string>.Success(value.ToString()));

        Assert.True(bound.IsSuccess);
        Assert.Equal("21", bound.Value);
    }

    [Fact]
    public void Bind_propagates_the_failure()
    {
        var error = new CoreError("code", "message");
        var result = Result<int>.Failure(error);

        var bound = result.Bind(value => Result<string>.Success(value.ToString()));

        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Bind_rejects_a_null_projection()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).Bind<int>(null!));
    }

    [Fact]
    public void Match_collapses_a_success()
    {
        var result = Result<int>.Success(42);

        var text = result.Match(value => $"ok:{value}", error => $"ko:{error.Code}");

        Assert.Equal("ok:42", text);
    }

    [Fact]
    public void Match_collapses_a_failure()
    {
        var result = Result<int>.Failure(new CoreError("code", "message"));

        var text = result.Match(value => $"ok:{value}", error => $"ko:{error.Code}");

        Assert.Equal("ko:code", text);
    }

    [Fact]
    public void Match_rejects_null_projections()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<ArgumentNullException>(() => result.Match<int>(null!, _ => 0));
        Assert.Throws<ArgumentNullException>(() => result.Match<int>(_ => 0, null!));
    }

    [Fact]
    public void ValueOr_returns_the_value_or_fallback()
    {
        Assert.Equal(1, Result<int>.Success(1).ValueOr(2));
        Assert.Equal(2, Result<int>.Failure(new CoreError("code", "message")).ValueOr(2));
    }

    [Fact]
    public void ValueOr_factory_returns_the_value_or_projection()
    {
        Assert.Equal(1, Result<int>.Success(1).ValueOr(_ => 2));
        Assert.Equal(2, Result<int>.Failure(new CoreError("code", "message")).ValueOr(_ => 2));
    }

    [Fact]
    public void ValueOr_factory_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).ValueOr(null!));
    }

    [Fact]
    public void Default_is_a_success_with_the_default_value()
    {
        var result = default(Result<int>);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(0, result.Value);
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(0, value);
        Assert.False(result.TryGetError(out _));
    }

    [Fact]
    public void Default_flows_through_combinators_without_a_null_error()
    {
        var result = default(Result<int>);

        Assert.Equal(1, result.Map(value => value + 1).Value);
        Assert.Equal(0, result.Bind(value => Result<int>.Success(value)).Value);
        Assert.Equal(0, result.Match(value => value, _ => -1));
        Assert.Equal(0, result.ValueOr(5));
    }
}
