using EDC.Views;
using Xunit;

namespace EDC.Tests;

/// <summary>
/// The notice of a step copied sits bottom right, like a notification: away from the warning, from the
/// ring round the star that scooping must not cross, and from the HUD in the middle.
/// </summary>
public sealed class CopyNoticeTests
{
    private static readonly GameScreen Screen = new(IntPtr.Zero, 0, 100, 1920, 1000, 1);

    [Fact]
    public void It_sits_bottom_right_a_little_off_the_edges() =>
        Assert.Equal((1920 - 300 - 30, 100 + 1000 - 40 - 30), CopyNotice.PlaceOn(Screen, 300, 40));
}
