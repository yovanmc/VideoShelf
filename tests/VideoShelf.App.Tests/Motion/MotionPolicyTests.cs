// tests/VideoShelf.App.Tests/Motion/MotionPolicyTests.cs
using VideoShelf.App.Motion;
using Xunit;
using Shouldly;

public class MotionPolicyTests
{
    [Theory]
    [InlineData(true,  true,  true)]   // OS allows + app enabled -> animate
    [InlineData(false, true,  false)]  // OS minimize-animations -> no
    [InlineData(true,  false, false)]  // app disabled -> no
    [InlineData(false, false, false)]
    public void ShouldAnimate_respects_os_and_app(bool osAnim, bool appEnabled, bool expected)
        => MotionPolicy.ShouldAnimate(osAnim, appEnabled).ShouldBe(expected);
}
