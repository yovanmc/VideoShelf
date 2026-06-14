using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>Always returns <see cref="NextResult"/> without showing a dialog.</summary>
public sealed class FakeConfirmService : IConfirmService
{
    /// <summary>Set to <c>true</c> to simulate the user clicking "Yes".</summary>
    public bool NextResult { get; set; } = true;

    public bool Confirm(string title, string message) => NextResult;
}
