using RTLSDRCore.Enums;

namespace RTLSDRCore.DSP
{
    /// <summary>
    /// Factory for creating demodulator instances
    /// </summary>
    public static class DemodulatorFactory
    {
        /// <summary>
        /// Creates a demodulator for the specified modulation type
        /// </summary>
        /// <param name="modulation">Modulation type</param>
        /// <returns>Demodulator instance</returns>
        public static IDemodulator Create(ModulationType modulation)
        {
            return modulation switch
            {
                ModulationType.AM => new AmDemodulator(),
                ModulationType.NFM => new NfmDemodulator(),
                ModulationType.WFM => new WfmDemodulator(),
                ModulationType.USB => new UsbDemodulator(),
                ModulationType.LSB => new LsbDemodulator(),
                ModulationType.CW => new AmDemodulator(), // CW uses AM demod with narrow filter
                ModulationType.RAW => new RawDemodulator(),
                _ => throw new ArgumentException($"Unsupported modulation type: {modulation}", nameof(modulation))
            };
        }

        /// <summary>
        /// Gets the recommended bandwidth for a modulation type
        /// </summary>
        /// <param name="modulation">Modulation type</param>
        /// <returns>Bandwidth in Hz</returns>
        public static int GetRecommendedBandwidth(ModulationType modulation)
        {
            return modulation switch
            {
                ModulationType.AM => 10_000,
                ModulationType.NFM => 12_500,
                ModulationType.WFM => 200_000,
                ModulationType.USB => 3_000,
                ModulationType.LSB => 3_000,
                ModulationType.CW => 500,
                ModulationType.RAW => 0,
                _ => 10_000
            };
        }
    }

    /// <summary>
    /// Raw IQ passthrough (no demodulation)
    /// </summary>
    public class RawDemodulator : IDemodulator
    {
        /// <inheritdoc/>
        public string Name => "RAW";

        /// <inheritdoc/>
        public int SampleRate { get; set; } = 2_400_000;

        /// <inheritdoc/>
        public int Bandwidth { get; set; } = 0;

        /// <inheritdoc/>
        public int Demodulate(ReadOnlySpan<Models.IqSample> input, Span<float> output)
        {
            var count = Math.Min(input.Length, output.Length);
            for (var i = 0; i < count; i++)
            {
                output[i] = input[i].I; // Just output I component
            }
            return count;
        }

        /// <inheritdoc/>
        public void Reset() { }
    }
}
