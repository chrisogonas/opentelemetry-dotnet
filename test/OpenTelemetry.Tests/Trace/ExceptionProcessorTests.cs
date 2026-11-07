// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using OpenTelemetry.Tests;

namespace OpenTelemetry.Trace.Tests;

public class ExceptionProcessorTests
{
    [Fact]
    public void ActivityStatusSetToErrorWhenExceptionProcessorEnabled()
    {
        var activitySourceName = Utils.GetCurrentMethodName();
        using var activitySource = new ActivitySource(activitySourceName);
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(activitySourceName)
            .SetSampler(new AlwaysOnSampler())
#pragma warning disable CA2000 // Dispose objects before losing scope
            .AddProcessor(new ExceptionProcessor())
#pragma warning restore CA2000 // Dispose objects before losing scope
            .Build();

        Activity? activity1 = null;
        Activity? activity2 = null;
        Activity? activity3 = null;
        Activity? activity4 = null;
        Activity? activity5 = null;

        try
        {
            using (activity1 = activitySource.StartActivity("Activity1"))
            {
                using (activity2 = activitySource.StartActivity("Activity2"))
                {
                    throw new InvalidOperationException("Oops!");
                }
            }
        }
        catch (Exception)
        {
            using (activity3 = activitySource.StartActivity("Activity3"))
            {
            }
        }
        finally
        {
            using (activity4 = activitySource.StartActivity("Activity4"))
            {
            }
        }

        try
        {
            throw new InvalidOperationException("Oops!");
        }
        catch (Exception)
        {
            /*
               Note: Behavior here is different depending on the processor
               architecture.

               x86: Exception is cleared BEFORE the catch runs.
               Marshal.GetExceptionPointers returns zero.

               non-x86: Exception is cleared AFTER the catch runs.
               Marshal.GetExceptionPointers returns non-zero.
            */
            activity5 = activitySource.StartActivity("Activity5");
        }
        finally
        {
            activity5?.Dispose();
        }

        Assert.NotNull(activity1);
        Assert.Equal(StatusCode.Error, activity1.GetStatus().StatusCode);
        Assert.Null(GetTagValue(activity1, "otel.exception_pointers"));

        Assert.NotNull(activity2);
        Assert.Equal(StatusCode.Error, activity2.GetStatus().StatusCode);
        Assert.Null(GetTagValue(activity2, "otel.exception_pointers"));

        Assert.NotNull(activity3);
        Assert.Equal(StatusCode.Unset, activity3.GetStatus().StatusCode);
        Assert.Null(GetTagValue(activity3, "otel.exception_pointers"));

        Assert.NotNull(activity4);
        Assert.Equal(StatusCode.Unset, activity4.GetStatus().StatusCode);

        Assert.Null(GetTagValue(activity4, "otel.exception_pointers"));

        Assert.NotNull(activity5);
        Assert.Equal(StatusCode.Unset, activity5.GetStatus().StatusCode);
        Assert.Null(GetTagValue(activity5, "otel.exception_pointers"));
    }

    [Fact]
    public void ActivityStatusNotSetWhenExceptionProcessorNotEnabled()
    {
        var activitySourceName = Utils.GetCurrentMethodName();
        using var activitySource = new ActivitySource(activitySourceName);
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(activitySourceName)
            .SetSampler(new AlwaysOnSampler())
            .Build();

        Activity? activity = null;

        try
        {
            using (activity = activitySource.StartActivity("Activity"))
            {
                throw new InvalidOperationException("Oops!");
            }
        }
        catch (Exception)
        {
        }

        Assert.Equal(StatusCode.Unset, activity.GetStatus().StatusCode);
    }

    [Fact]
    public async Task ActivityStatusNotSetOnOuterActivityWhenAsyncExceptionIsCaught()
    {
        // Regression test for https://github.com/open-telemetry/opentelemetry-dotnet/issues/2753
        var activitySourceName = Utils.GetCurrentMethodName();
        using var activitySource = new ActivitySource(activitySourceName);
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(activitySourceName)
            .SetSampler(new AlwaysOnSampler())
            .SetErrorStatusOnException()
            .Build();

        Activity? outer = null;
        Activity? inner = null;

        using (outer = activitySource.StartActivity("Outer"))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            try
            {
                using (inner = activitySource.StartActivity("Inner"))
                {
                    await ThrowAfterDelayAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // Caught: the outer activity should not be marked as an error.
            }
        }

        Assert.NotNull(inner);
        Assert.Equal(StatusCode.Error, inner.GetStatus().StatusCode);

        Assert.NotNull(outer);
        Assert.Equal(StatusCode.Unset, outer.GetStatus().StatusCode);
    }

    private static async Task ThrowAfterDelayAsync()
    {
        await Task.Delay(10, TestContext.Current.CancellationToken);
        throw new InvalidOperationException("Oops!");
    }

    private static object? GetTagValue(Activity activity, string tagName)
    {
        foreach (ref readonly var tag in activity.EnumerateTagObjects())
        {
            if (tag.Key == tagName)
            {
                return tag.Value;
            }
        }

        return null;
    }
}
