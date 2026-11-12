using BlueMeal.Api.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BlueMeal.UnitTests.Common;

/// <summary>Regression: requests aborted by the browser (navigation mid-request) were logged as Error-level 500s (found in the E2E run).</summary>
public class ClientAbortTests
{
    private static DefaultHttpContext Ctx(bool aborted)
    {
        var cts = new CancellationTokenSource();
        if (aborted) cts.Cancel();
        return new DefaultHttpContext { RequestAborted = cts.Token };
    }

    [Fact]
    public async Task Cancellation_after_the_client_left_becomes_499_without_an_exception()
    {
        var ctx = Ctx(aborted: true);
        var mw = new ExpectedExceptionMiddleware(c => throw new OperationCanceledException(c.RequestAborted));

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(ExpectedExceptionMiddleware.ClientClosedRequest);
    }

    [Fact]
    public async Task Cancellation_while_the_client_is_still_connected_is_rethrown()
    {
        var ctx = Ctx(aborted: false);
        var mw = new ExpectedExceptionMiddleware(_ => throw new OperationCanceledException());

        var act = () => mw.InvokeAsync(ctx);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
