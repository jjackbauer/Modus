using Modus.Core.Messaging;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public class CorrelationIdTests
{
    [Fact]
    public void CorrelationId_GivenValidString_ValueEqualsInput()
    {
        var id = new CorrelationId("request-123");
        Assert.Equal("request-123", id.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("")]
    public void CorrelationId_GivenNullOrWhitespace_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => new CorrelationId(input!));
    }

    [Fact]
    public void CorrelationId_GivenEqualStrings_InstancesAreEqual()
    {
        var a = new CorrelationId("same");
        var b = new CorrelationId("same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void CorrelationId_ToString_ReturnsSameStringAsValue()
    {
        var id = new CorrelationId("request-123");
        Assert.Equal(id.Value, id.ToString());
    }
}
