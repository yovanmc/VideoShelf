using System;
using VideoShelf.App.Diagnostics;
using Xunit;

namespace VideoShelf.App.Tests;

public class CrashHandlerTests
{
    [Fact]
    public void FormatReport_IncludesExceptionTypeAndMessage()
    {
        var ex = new InvalidOperationException("boom");
        string report = CrashReporter.FormatReport("UI thread", ex);
        Assert.Contains("UI thread", report);
        Assert.Contains("InvalidOperationException", report);
        Assert.Contains("boom", report);
    }

    [Fact]
    public void FormatReport_NullException_DoesNotThrow()
    {
        string report = CrashReporter.FormatReport("AppDomain", null);
        Assert.Contains("AppDomain", report);
        Assert.Contains("Unknown error", report);
    }
}
