using MouseConfig.Core.Devices;

namespace MouseConfig.Tests;

public sealed class MouseFamiliesTests
{
    [Theory]
    [InlineData(0x046D, "Logitech")]
    [InlineData(0x1532, "Razer")]
    [InlineData(0x1BCF, "Sunplus chip (Redgear, Philips and others)")]
    [InlineData(0x258A, "SinoWealth chip (Glorious, Redragon and many budget brands)")]
    public void Known_makers_are_found(int vendorId, string maker)
    {
        Assert.Equal(maker, MouseFamilies.Find((ushort)vendorId)!.Maker);
    }

    [Fact]
    public void Unknown_or_missing_ids_give_no_family()
    {
        Assert.Null(MouseFamilies.Find(0x1234));
        Assert.Null(MouseFamilies.Find(null));
        Assert.NotEmpty(MouseFamilies.UnknownMakerText);
    }

    [Fact]
    public void Each_vendor_id_appears_once_and_only_sunplus_has_a_plugin()
    {
        Assert.Equal(MouseFamilies.All.Count, MouseFamilies.All.Select(f => f.VendorId).Distinct().Count());
        Assert.Equal([0x1BCF], MouseFamilies.All.Where(f => f.HasPluginHere).Select(f => (int)f.VendorId));
        Assert.All(MouseFamilies.All, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.HowItWorks));
            Assert.False(string.IsNullOrWhiteSpace(f.MakerTool));
        });
    }
}
