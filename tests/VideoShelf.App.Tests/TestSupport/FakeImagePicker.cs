using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

public sealed class FakeImagePicker(string? result) : IImagePicker
{
    public string? PickImage(string? initialFolder = null) => result;
}
