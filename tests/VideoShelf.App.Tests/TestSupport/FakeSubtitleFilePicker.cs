using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

public sealed class FakeSubtitleFilePicker : ISubtitleFilePicker
{
    public string? NextResult { get; set; }
    public string? PickSubtitle(string? initialFolder = null) => NextResult;
}
