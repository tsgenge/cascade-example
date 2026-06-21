using FluentAssertions;
using Xunit;

namespace Cascade.Example.UnitTests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        true.Should().BeTrue();
    }
}
