using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

public sealed class ImageSizeExtensionTests
{
    [Fact]
    public void Typical_photo_is_not_risky_on_a_typical_machine()
    {
        var photo = new ImageSize(4000, 6000); // the doc's own 24 MP target
        var available = 8L * 1024 * 1024 * 1024; // 8 GB

        Assert.False(photo.IsRiskyToOpen(available));
    }

    [Fact]
    public void A_huge_image_is_risky_on_the_same_machine()
    {
        var huge = new ImageSize(30_000, 30_000); // 900 MP
        var available = 8L * 1024 * 1024 * 1024;

        Assert.True(huge.IsRiskyToOpen(available));
    }

    [Fact]
    public void The_same_image_can_be_risky_on_a_smaller_machine_and_not_on_a_bigger_one()
    {
        var size = new ImageSize(10_000, 10_000); // 100 MP

        Assert.True(size.IsRiskyToOpen(1L * 1024 * 1024 * 1024));   // 1 GB: risky
        Assert.False(size.IsRiskyToOpen(64L * 1024 * 1024 * 1024)); // 64 GB: not risky
    }

    [Fact]
    public void Unknown_available_memory_never_warns()
    {
        var huge = new ImageSize(50_000, 50_000);

        Assert.False(huge.IsRiskyToOpen(0));
        Assert.False(huge.IsRiskyToOpen(-1));
    }

    [Fact]
    public void Real_available_memory_does_not_throw()
    {
        // Exercises the GC.GetGCMemoryInfo() path (no explicit availableBytes) rather than asserting a value,
        // since that depends on the machine running the test.
        var size = new ImageSize(4000, 6000);
        _ = size.IsRiskyToOpen();
    }
}
