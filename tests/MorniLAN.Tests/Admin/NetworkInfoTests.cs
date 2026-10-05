using System.Net;
using MorniLAN.Admin.Server;

namespace MorniLAN.Tests.Admin;

public class NetworkInfoTests
{
    [Theory]
    [InlineData("192.168.1.20", 24, "192.168.1.255")]
    [InlineData("10.0.5.7", 16, "10.0.255.255")]
    [InlineData("172.16.3.9", 20, "172.16.15.255")]
    public void BroadcastOf_ComputesDirectedBroadcast(string ip, int prefix, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), NetworkInfo.BroadcastOf(IPAddress.Parse(ip), prefix));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(32)]
    public void BroadcastOf_ReturnsNull_ForPointToPoint(int prefix)
    {
        Assert.Null(NetworkInfo.BroadcastOf(IPAddress.Parse("10.0.0.1"), prefix));
    }

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.101.102.103", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("100.63.0.1", false)]
    [InlineData("192.168.1.20", false)]
    public void IsTailscale_RecognizesCgnatRange(string ip, bool expected)
    {
        Assert.Equal(expected, NetworkInfo.IsTailscale(IPAddress.Parse(ip)));
    }

    [Fact]
    public void AdminEndpoints_EndsWithMachineName()
    {
        Assert.Equal(Environment.MachineName, NetworkInfo.AdminEndpoints()[^1]);
    }
}
