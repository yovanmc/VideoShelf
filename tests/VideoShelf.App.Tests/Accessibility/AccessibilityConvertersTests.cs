using System.Globalization;
using VideoShelf.App.Converters;
using Xunit;

namespace VideoShelf.App.Tests.Accessibility;

public class AccessibilityConvertersTests
{
    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.5, "50%")]
    [InlineData(0.756, "76%")]
    [InlineData(1.0, "100%")]
    [InlineData(-0.2, "0%")]
    [InlineData(1.5, "100%")]
    public void FractionToPercentText_formats_and_clamps(double f, string expected)
    {
        var c = new FractionToPercentText();
        var r = c.Convert(f, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, r);
    }
}
