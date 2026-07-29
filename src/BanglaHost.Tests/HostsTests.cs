using Xunit;
using BanglaHost.Core;

namespace BanglaHost.Tests;

public class HostsTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("sub.domain.local")]
    [InlineData("my-site.test")]
    [InlineData("a.b.c.d.e.f.g.h")]
    public void IsValidDomain_ValidDomains_ReturnsTrue(string domain)
    {
        Assert.True(Hosts.IsValidDomain(domain));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("example.com ")]
    [InlineData(" example.com")]
    [InlineData("example.com\n127.0.0.1 malicious.com")]
    [InlineData("example.com\r\n")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("example.com.")]
    [InlineData(".example.com")]
    [InlineData("exam_ple.com")]
    public void IsValidDomain_InvalidDomains_ReturnsFalse(string domain)
    {
        Assert.False(Hosts.IsValidDomain(domain));
    }
}
