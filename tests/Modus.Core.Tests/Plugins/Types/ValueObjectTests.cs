using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins.Types;

public class ValueObjectTests
{
    // PluginId
    [Fact]
    public void PluginId_GivenValidString_ValueEqualsInput()
    {
        var id = new PluginId("test-value");
        Assert.Equal("test-value", id.Value);
    }

    [Fact]
    public void PluginId_GivenNullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new PluginId(null!));
    }

    [Fact]
    public void PluginId_GivenWhitespaceString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new PluginId("   "));
    }

    [Fact]
    public void PluginId_GivenEqualStrings_InstancesAreEqual()
    {
        var a = new PluginId("same");
        var b = new PluginId("same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void PluginId_GivenDifferentStrings_InstancesAreNotEqual()
    {
        var a = new PluginId("alpha");
        var b = new PluginId("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PluginId_ToString_ReturnsSameStringAsValue()
    {
        var id = new PluginId("test-value");
        Assert.Equal(id.Value, id.ToString());
    }

    // OperationName
    [Fact]
    public void OperationName_GivenValidString_ValueEqualsInput()
    {
        var op = new OperationName("test-value");
        Assert.Equal("test-value", op.Value);
    }

    [Fact]
    public void OperationName_GivenNullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OperationName(null!));
    }

    [Fact]
    public void OperationName_GivenWhitespaceString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OperationName("   "));
    }

    [Fact]
    public void OperationName_GivenEqualStrings_InstancesAreEqual()
    {
        var a = new OperationName("same");
        var b = new OperationName("same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void OperationName_GivenDifferentStrings_InstancesAreNotEqual()
    {
        var a = new OperationName("alpha");
        var b = new OperationName("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void OperationName_ToString_ReturnsSameStringAsValue()
    {
        var op = new OperationName("test-value");
        Assert.Equal(op.Value, op.ToString());
    }

    // ContractName
    [Fact]
    public void ContractName_GivenValidString_ValueEqualsInput()
    {
        var cn = new ContractName("test-value");
        Assert.Equal("test-value", cn.Value);
    }

    [Fact]
    public void ContractName_GivenNullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ContractName(null!));
    }

    [Fact]
    public void ContractName_GivenWhitespaceString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ContractName("   "));
    }

    [Fact]
    public void ContractName_GivenEqualStrings_InstancesAreEqual()
    {
        var a = new ContractName("same");
        var b = new ContractName("same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ContractName_GivenDifferentStrings_InstancesAreNotEqual()
    {
        var a = new ContractName("alpha");
        var b = new ContractName("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ContractName_ToString_ReturnsSameStringAsValue()
    {
        var cn = new ContractName("test-value");
        Assert.Equal(cn.Value, cn.ToString());
    }

    // JobName
    [Fact]
    public void JobName_GivenValidString_ValueEqualsInput()
    {
        var jn = new JobName("test-value");
        Assert.Equal("test-value", jn.Value);
    }

    [Fact]
    public void JobName_GivenNullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JobName(null!));
    }

    [Fact]
    public void JobName_GivenWhitespaceString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JobName("   "));
    }

    [Fact]
    public void JobName_GivenEqualStrings_InstancesAreEqual()
    {
        var a = new JobName("same");
        var b = new JobName("same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void JobName_GivenDifferentStrings_InstancesAreNotEqual()
    {
        var a = new JobName("alpha");
        var b = new JobName("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void JobName_ToString_ReturnsSameStringAsValue()
    {
        var jn = new JobName("test-value");
        Assert.Equal(jn.Value, jn.ToString());
    }

    // CapabilityName
    [Fact]
    public void CapabilityName_GivenValidString_ValueEqualsInput()
    {
        var cap = new CapabilityName("test-value");
        Assert.Equal("test-value", cap.Value);
    }

    [Fact]
    public void CapabilityName_GivenNullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityName(null!));
    }

    [Fact]
    public void CapabilityName_GivenWhitespaceString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityName("   "));
    }

    [Fact]
    public void CapabilityName_GivenEqualStrings_InstancesAreEqual()
    {
        var a = new CapabilityName("same");
        var b = new CapabilityName("same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void CapabilityName_GivenDifferentStrings_InstancesAreNotEqual()
    {
        var a = new CapabilityName("alpha");
        var b = new CapabilityName("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CapabilityName_ToString_ReturnsSameStringAsValue()
    {
        var cap = new CapabilityName("test-value");
        Assert.Equal(cap.Value, cap.ToString());
    }
}
