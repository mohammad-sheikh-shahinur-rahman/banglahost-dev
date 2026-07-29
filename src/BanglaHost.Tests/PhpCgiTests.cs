using Xunit;
using BanglaHost.Core;

namespace BanglaHost.Tests;

public class PhpCgiTests
{
    [Theory]
    [InlineData("8.1", 9181)]
    [InlineData("8.2", 9182)]
    [InlineData("8.3", 9183)]
    [InlineData("8.4", 9184)]
    [InlineData("default", 9100)]
    [InlineData("", 9100)]
    [InlineData("invalid", 9100)]
    public void PortFor_ReturnsCorrectPort(string version, int expectedPort)
    {
        Assert.Equal(expectedPort, PhpCgi.PortFor(version));
    }
}
