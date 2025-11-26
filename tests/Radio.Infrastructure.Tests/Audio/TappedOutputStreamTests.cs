using System.Reflection;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Unit tests for the TappedOutputStream class.
/// Tests the internal Stream-based ring buffer implementation.
/// </summary>
public class TappedOutputStreamTests
{
  private static Stream CreateTappedOutputStream(int sampleRate = 48000, int channels = 2, int bufferSizeSeconds = 1)
  {
    // Use reflection to access the internal TappedOutputStream class
    var assembly = typeof(Radio.Infrastructure.DependencyInjection.AudioServiceExtensions).Assembly;
    var type = assembly.GetType("Radio.Infrastructure.Audio.SoundFlow.TappedOutputStream")!;
    return (Stream)Activator.CreateInstance(type, sampleRate, channels, bufferSizeSeconds)!;
  }

  private static void InvokeWriteFromEngine(Stream stream, float[] samples)
  {
    var type = stream.GetType();
    var method = type.GetMethod("WriteFromEngine", [typeof(float[])])!;
    method.Invoke(stream, [samples]);
  }

  private static void InvokeClear(Stream stream)
  {
    var type = stream.GetType();
    var method = type.GetMethod("Clear")!;
    method.Invoke(stream, null);
  }

  private static int GetAvailable(Stream stream)
  {
    var type = stream.GetType();
    var property = type.GetProperty("Available")!;
    return (int)property.GetValue(stream)!;
  }

  private static int GetSampleRate(Stream stream)
  {
    var type = stream.GetType();
    var property = type.GetProperty("SampleRate")!;
    return (int)property.GetValue(stream)!;
  }

  private static int GetChannels(Stream stream)
  {
    var type = stream.GetType();
    var property = type.GetProperty("Channels")!;
    return (int)property.GetValue(stream)!;
  }

  [Fact]
  public void Constructor_InitializesWithCorrectParameters()
  {
    // Arrange & Act
    var stream = CreateTappedOutputStream(48000, 2, 5);

    // Assert - check that the stream is created with correct properties
    Assert.Equal(48000, GetSampleRate(stream));
    Assert.Equal(2, GetChannels(stream));
    Assert.True(stream.CanRead);
    Assert.False(stream.CanSeek);
    Assert.False(stream.CanWrite);
  }

  [Fact]
  public void Available_ReturnsZeroWhenEmpty()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act
    var available = GetAvailable(stream);

    // Assert
    Assert.Equal(0, available);
  }

  [Fact]
  public void WriteFromEngine_IncreasesAvailableBytes()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

    // Act
    InvokeWriteFromEngine(stream, samples);
    var available = GetAvailable(stream);

    // Assert - each float sample becomes 2 bytes (16-bit PCM)
    Assert.Equal(samples.Length * 2, available);
  }

  [Fact]
  public void Read_ReturnsWrittenData()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var samples = new float[] { 0.5f, -0.5f };
    InvokeWriteFromEngine(stream, samples);
    var buffer = new byte[4];

    // Act
    var bytesRead = stream.Read(buffer, 0, buffer.Length);

    // Assert
    Assert.Equal(4, bytesRead);
    Assert.Equal(0, GetAvailable(stream));
  }

  [Fact]
  public void Read_ReturnsZeroWhenEmpty()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var buffer = new byte[10];

    // Act
    var bytesRead = stream.Read(buffer, 0, buffer.Length);

    // Assert
    Assert.Equal(0, bytesRead);
  }

  [Fact]
  public void Read_ReturnsPartialDataWhenNotEnoughAvailable()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var samples = new float[] { 0.5f }; // 2 bytes
    InvokeWriteFromEngine(stream, samples);
    var buffer = new byte[10];

    // Act
    var bytesRead = stream.Read(buffer, 0, buffer.Length);

    // Assert
    Assert.Equal(2, bytesRead);
  }

  [Fact]
  public void Clear_ResetsBuffer()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };
    InvokeWriteFromEngine(stream, samples);

    // Act
    InvokeClear(stream);

    // Assert
    Assert.Equal(0, GetAvailable(stream));
  }

  [Fact]
  public void WriteFromEngine_HandlesClipping()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    // Values outside -1 to 1 should be clamped
    var samples = new float[] { 2.0f, -2.0f };

    // Act - should not throw
    InvokeWriteFromEngine(stream, samples);

    // Assert
    Assert.Equal(4, GetAvailable(stream));
  }

  [Fact]
  public void RingBuffer_WrapsAround()
  {
    // Arrange - small buffer for testing wraparound
    // Buffer size = 1000 * 1 * 2 * 1 = 2000 bytes for 1 second
    var stream = CreateTappedOutputStream(1000, 1, 1);

    // Generate samples that will fill most of the buffer
    var samples = new float[500]; // 500 * 2 = 1000 bytes
    for (var i = 0; i < samples.Length; i++)
    {
      samples[i] = 0.5f;
    }

    // Act - Write and read multiple times to force wraparound
    for (var iteration = 0; iteration < 5; iteration++)
    {
      InvokeWriteFromEngine(stream, samples);
      var buffer = new byte[1000];
      var bytesRead = stream.Read(buffer, 0, buffer.Length);
      Assert.Equal(1000, bytesRead);
    }

    // Assert - buffer should still work after wraparound
    InvokeWriteFromEngine(stream, samples);
    Assert.Equal(1000, GetAvailable(stream));
  }

  [Fact]
  public void Length_ThrowsNotSupportedException()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act & Assert
    Assert.Throws<NotSupportedException>(() => _ = stream.Length);
  }

  [Fact]
  public void Position_ThrowsNotSupportedException()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act & Assert
    Assert.Throws<NotSupportedException>(() => _ = stream.Position);
    Assert.Throws<NotSupportedException>(() => stream.Position = 0);
  }

  [Fact]
  public void Seek_ThrowsNotSupportedException()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act & Assert
    Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
  }

  [Fact]
  public void SetLength_ThrowsNotSupportedException()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act & Assert
    Assert.Throws<NotSupportedException>(() => stream.SetLength(100));
  }

  [Fact]
  public void Write_ThrowsNotSupportedException()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var buffer = new byte[10];

    // Act & Assert
    Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length));
  }

  [Fact]
  public void Flush_DoesNotThrow()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act & Assert - should not throw
    stream.Flush();
  }
}
