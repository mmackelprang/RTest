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
    return (Stream)Activator.CreateInstance(type, sampleRate, channels, bufferSizeSeconds, null)!;
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

  private static int GetAvailableForReader(Stream stream, string readerId)
  {
    var type = stream.GetType();
    var method = type.GetMethod("GetAvailableForReader")!;
    return (int)method.Invoke(stream, [readerId])!;
  }

  private static Stream CreateReader(Stream stream, string readerId, int lagBytes = 0)
  {
    var type = stream.GetType();
    var method = type.GetMethod("CreateReader")!;
    return (Stream)method.Invoke(stream, [readerId, lagBytes])!;
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
  public void Available_ReturnsZeroWithNoReaders()
  {
    // Arrange
    var stream = CreateTappedOutputStream();

    // Act
    var available = GetAvailable(stream);

    // Assert - legacy Available always returns 0
    Assert.Equal(0, available);
  }

  [Fact]
  public void WriteFromEngine_IncreasesAvailableBytesForReader()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "test-reader");
    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

    // Act
    InvokeWriteFromEngine(stream, samples);
    var available = GetAvailableForReader(stream, "test-reader");

    // Assert - each float sample becomes 2 bytes (16-bit PCM)
    Assert.Equal(samples.Length * 2, available);
  }

  [Fact]
  public void Reader_ReturnsWrittenData()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "test-reader");
    var samples = new float[] { 0.5f, -0.5f };
    InvokeWriteFromEngine(stream, samples);
    var buffer = new byte[4];

    // Act
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert
    Assert.Equal(4, bytesRead);
    Assert.Equal(0, GetAvailableForReader(stream, "test-reader"));
  }

  [Fact]
  public void Reader_ReturnsZeroWhenEmpty()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "test-reader");
    var buffer = new byte[10];

    // Act
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert
    Assert.Equal(0, bytesRead);
  }

  [Fact]
  public void Reader_ReturnsPartialDataWhenNotEnoughAvailable()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "test-reader");
    var samples = new float[] { 0.5f }; // 2 bytes
    InvokeWriteFromEngine(stream, samples);
    var buffer = new byte[10];

    // Act
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert
    Assert.Equal(2, bytesRead);
  }

  [Fact]
  public void Clear_ResetsBufferForAllReaders()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "test-reader");
    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };
    InvokeWriteFromEngine(stream, samples);

    // Act
    InvokeClear(stream);

    // Assert
    Assert.Equal(0, GetAvailableForReader(stream, "test-reader"));
  }

  [Fact]
  public void WriteFromEngine_HandlesClipping()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "test-reader");
    // Values outside -1 to 1 should be clamped
    var samples = new float[] { 2.0f, -2.0f };

    // Act - should not throw
    InvokeWriteFromEngine(stream, samples);

    // Assert
    Assert.Equal(4, GetAvailableForReader(stream, "test-reader"));
  }

  [Fact]
  public void RingBuffer_WrapsAround()
  {
    // Arrange - small buffer for testing wraparound
    // Buffer size = 1000 * 1 * 2 * 1 = 2000 bytes for 1 second
    var stream = CreateTappedOutputStream(1000, 1, 1);
    var reader = CreateReader(stream, "test-reader");

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
      var bytesRead = reader.Read(buffer, 0, buffer.Length);
      Assert.Equal(1000, bytesRead);
    }

    // Assert - buffer should still work after wraparound
    InvokeWriteFromEngine(stream, samples);
    Assert.Equal(1000, GetAvailableForReader(stream, "test-reader"));
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

  [Fact]
  public void MultipleReaders_ReceiveAllWrittenData()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader1 = CreateReader(stream, "reader-1");
    var reader2 = CreateReader(stream, "reader-2");

    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };
    InvokeWriteFromEngine(stream, samples);

    // Act
    var buffer1 = new byte[8];
    var buffer2 = new byte[8];
    var bytesRead1 = reader1.Read(buffer1, 0, buffer1.Length);
    var bytesRead2 = reader2.Read(buffer2, 0, buffer2.Length);

    // Assert - both readers get the same data
    Assert.Equal(8, bytesRead1);
    Assert.Equal(8, bytesRead2);
    Assert.Equal(buffer1, buffer2);
  }

  [Fact]
  public void MultipleReaders_IndependentPositions()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader1 = CreateReader(stream, "reader-1");
    var reader2 = CreateReader(stream, "reader-2");

    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };
    InvokeWriteFromEngine(stream, samples);

    // Act - reader1 reads half, reader2 reads all
    var buffer1 = new byte[4];
    var buffer2 = new byte[8];
    var bytesRead1 = reader1.Read(buffer1, 0, buffer1.Length);
    var bytesRead2 = reader2.Read(buffer2, 0, buffer2.Length);

    // Assert
    Assert.Equal(4, bytesRead1);
    Assert.Equal(8, bytesRead2);

    // reader1 still has 4 bytes available, reader2 has 0
    Assert.Equal(4, GetAvailableForReader(stream, "reader-1"));
    Assert.Equal(0, GetAvailableForReader(stream, "reader-2"));
  }

  [Fact]
  public void Reader_DisposeRemovesTracking()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var reader = CreateReader(stream, "disposable-reader");
    var samples = new float[] { 0.5f };
    InvokeWriteFromEngine(stream, samples);

    // Act
    reader.Dispose();

    // Assert - reader position should be removed
    Assert.Equal(0, GetAvailableForReader(stream, "disposable-reader"));
  }

  [Fact]
  public void Reader_OnlyReceivesDataWrittenAfterCreation()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var samplesBeforeReader = new float[] { 1.0f, -1.0f };
    InvokeWriteFromEngine(stream, samplesBeforeReader);

    // Create reader AFTER data was written (no lag)
    var reader = CreateReader(stream, "late-reader");

    // Write new data
    var samplesAfterReader = new float[] { 0.5f };
    InvokeWriteFromEngine(stream, samplesAfterReader);

    // Act
    var buffer = new byte[10];
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert - only gets data written after reader was created
    Assert.Equal(2, bytesRead); // 1 sample * 2 bytes
  }

  [Fact]
  public void Reader_WithLag_GetsHistoricalData()
  {
    // Arrange
    var stream = CreateTappedOutputStream(1000, 1, 1); // 1kHz, mono, 1s buffer = 2000 bytes
    var samples = new float[100]; // 200 bytes of PCM
    for (var i = 0; i < samples.Length; i++)
      samples[i] = 0.5f;
    InvokeWriteFromEngine(stream, samples);

    // Create reader with 100-byte lag — should start behind write position
    var reader = CreateReader(stream, "lag-reader", lagBytes: 100);

    // Act
    var buffer = new byte[200];
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert — reader should get 100 bytes of historical data immediately
    Assert.Equal(100, bytesRead);
  }

  [Fact]
  public void Reader_WithLag_ClampedToTotalBytesWritten()
  {
    // Arrange
    var stream = CreateTappedOutputStream(1000, 1, 1);
    var samples = new float[10]; // 20 bytes of PCM
    for (var i = 0; i < samples.Length; i++)
      samples[i] = 0.5f;
    InvokeWriteFromEngine(stream, samples);

    // Create reader requesting more lag than what's been written
    var reader = CreateReader(stream, "lag-reader", lagBytes: 1000);

    // Act
    var buffer = new byte[200];
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert — lag clamped to 20 bytes (total written)
    Assert.Equal(20, bytesRead);
  }

  [Fact]
  public void Reader_WithZeroLag_BehavesLikeDefault()
  {
    // Arrange
    var stream = CreateTappedOutputStream();
    var samplesBeforeReader = new float[] { 1.0f, -1.0f };
    InvokeWriteFromEngine(stream, samplesBeforeReader);

    // Create reader with explicit zero lag
    var reader = CreateReader(stream, "no-lag-reader", lagBytes: 0);

    // Act
    var buffer = new byte[10];
    var bytesRead = reader.Read(buffer, 0, buffer.Length);

    // Assert — no historical data, same as default
    Assert.Equal(0, bytesRead);
  }
}
