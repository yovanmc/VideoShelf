using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

public sealed class FakeVideoFilePicker : IVideoFilePicker
{
    public string? NextResult { get; set; }
    public string? PickVideo(string? initialFolder = null) => NextResult;
}
