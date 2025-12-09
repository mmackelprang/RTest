namespace RTLSDRCore.Models
{
    /// <summary>
    /// Represents the audio output format configuration
    /// </summary>
    public class AudioFormat
    {
        /// <summary>
        /// Gets or sets the sample rate in Hz (e.g., 48000, 44100)
        /// </summary>
        public int SampleRate { get; set; } = 48000;

        /// <summary>
        /// Gets or sets the number of audio channels (1 = mono, 2 = stereo)
        /// </summary>
        public int Channels { get; set; } = 1;

        /// <summary>
        /// Gets or sets the bits per sample (typically 16 or 32)
        /// </summary>
        public int BitsPerSample { get; set; } = 16;

        /// <summary>
        /// Creates a default mono audio format at 48kHz
        /// </summary>
        public static AudioFormat Default => new()
        {
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 16
        };

        /// <summary>
        /// Creates a stereo audio format at 48kHz
        /// </summary>
        public static AudioFormat Stereo => new()
        {
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16
        };

        /// <summary>
        /// Creates a high-quality audio format at 96kHz
        /// </summary>
        public static AudioFormat HighQuality => new()
        {
            SampleRate = 96000,
            Channels = 2,
            BitsPerSample = 32
        };

        /// <summary>
        /// Calculates the byte rate for this audio format
        /// </summary>
        public int ByteRate => SampleRate * Channels * (BitsPerSample / 8);

        /// <summary>
        /// Calculates the block align for this audio format
        /// </summary>
        public int BlockAlign => Channels * (BitsPerSample / 8);

        /// <inheritdoc/>
        public override string ToString() =>
            $"{SampleRate}Hz, {Channels}ch, {BitsPerSample}bit";
    }
}
