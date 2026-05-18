namespace Radio.Metrics.Tests;

using Radio.Metrics;
using Xunit;

/// <summary>
/// Tests for <see cref="MetricDescriptorRegistry"/>. Covers register/lookup
/// behavior, insertion-order stability, and re-registration overwrite (PR D
/// #11 of the Arc follow-up backlog).
/// </summary>
public class MetricDescriptorRegistryTests
{
  [Fact]
  public void Register_StoresDescriptor_LookupReturnsIt()
  {
    var registry = new MetricDescriptorRegistry();
    var descriptor = new MetricDescriptor
    {
      Key = "system.memory_usage_mb",
      Unit = MetricUnit.Megabytes,
      Category = "System",
      DisplayName = "Memory Usage",
    };

    registry.Register(descriptor);

    var result = registry.GetByKey("system.memory_usage_mb");
    Assert.NotNull(result);
    Assert.Equal(MetricUnit.Megabytes, result!.Unit);
    Assert.Equal("System", result.Category);
  }

  [Fact]
  public void GetByKey_UnknownKey_ReturnsNull()
  {
    var registry = new MetricDescriptorRegistry();

    var result = registry.GetByKey("does.not.exist");

    Assert.Null(result);
  }

  [Fact]
  public void GetByKey_EmptyOrNullKey_ReturnsNull()
  {
    var registry = new MetricDescriptorRegistry();

    Assert.Null(registry.GetByKey(""));
    Assert.Null(registry.GetByKey(null!));
  }

  [Fact]
  public void All_PreservesInsertionOrder()
  {
    var registry = new MetricDescriptorRegistry();
    registry.Register(new MetricDescriptor { Key = "a", Unit = MetricUnit.Bare });
    registry.Register(new MetricDescriptor { Key = "b", Unit = MetricUnit.Percent });
    registry.Register(new MetricDescriptor { Key = "c", Unit = MetricUnit.Count });

    var keys = registry.All.Select(d => d.Key).ToArray();

    Assert.Equal(new[] { "a", "b", "c" }, keys);
  }

  [Fact]
  public void Register_DuplicateKey_OverwritesAndPreservesOrder()
  {
    var registry = new MetricDescriptorRegistry();
    registry.Register(new MetricDescriptor { Key = "a", Unit = MetricUnit.Bare });
    registry.Register(new MetricDescriptor { Key = "b", Unit = MetricUnit.Percent });
    registry.Register(new MetricDescriptor { Key = "c", Unit = MetricUnit.Count });

    // Replace "b" with a different unit; "b" should remain in position 2.
    registry.Register(new MetricDescriptor { Key = "b", Unit = MetricUnit.Megabytes });

    var keys = registry.All.Select(d => d.Key).ToArray();
    Assert.Equal(new[] { "a", "b", "c" }, keys);
    Assert.Equal(MetricUnit.Megabytes, registry.GetByKey("b")!.Unit);
  }

  [Fact]
  public void Register_NullDescriptor_Throws()
  {
    var registry = new MetricDescriptorRegistry();

    Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
  }

  [Fact]
  public void Register_EmptyKey_Throws()
  {
    var registry = new MetricDescriptorRegistry();

    Assert.Throws<ArgumentException>(() =>
      registry.Register(new MetricDescriptor { Key = "", Unit = MetricUnit.Bare }));
  }

  [Fact]
  public void All_EmptyRegistry_ReturnsEmpty()
  {
    var registry = new MetricDescriptorRegistry();

    Assert.Empty(registry.All);
  }
}
