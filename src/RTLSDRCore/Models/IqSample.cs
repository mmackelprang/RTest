namespace RTLSDRCore.Models
{
    /// <summary>
    /// Represents a complex IQ (In-phase/Quadrature) sample
    /// </summary>
    public readonly struct IqSample
    {
        /// <summary>
        /// Gets the in-phase component
        /// </summary>
        public float I { get; }

        /// <summary>
        /// Gets the quadrature component
        /// </summary>
        public float Q { get; }

        /// <summary>
        /// Creates a new IQ sample
        /// </summary>
        /// <param name="i">In-phase component</param>
        /// <param name="q">Quadrature component</param>
        public IqSample(float i, float q)
        {
            I = i;
            Q = q;
        }

        /// <summary>
        /// Gets the magnitude of the complex sample
        /// </summary>
        public float Magnitude => MathF.Sqrt(I * I + Q * Q);

        /// <summary>
        /// Gets the phase angle in radians
        /// </summary>
        public float Phase => MathF.Atan2(Q, I);

        /// <summary>
        /// Gets the squared magnitude (more efficient than Magnitude)
        /// </summary>
        public float MagnitudeSquared => I * I + Q * Q;

        /// <summary>
        /// Adds two IQ samples
        /// </summary>
        public static IqSample operator +(IqSample a, IqSample b) =>
            new(a.I + b.I, a.Q + b.Q);

        /// <summary>
        /// Subtracts two IQ samples
        /// </summary>
        public static IqSample operator -(IqSample a, IqSample b) =>
            new(a.I - b.I, a.Q - b.Q);

        /// <summary>
        /// Multiplies two IQ samples (complex multiplication)
        /// </summary>
        public static IqSample operator *(IqSample a, IqSample b) =>
            new(a.I * b.I - a.Q * b.Q, a.I * b.Q + a.Q * b.I);

        /// <summary>
        /// Scales an IQ sample by a scalar value
        /// </summary>
        public static IqSample operator *(IqSample a, float scalar) =>
            new(a.I * scalar, a.Q * scalar);

        /// <summary>
        /// Gets the complex conjugate
        /// </summary>
        public IqSample Conjugate => new(I, -Q);

        /// <summary>
        /// Normalizes the sample to unit magnitude
        /// </summary>
        public IqSample Normalize()
        {
            var mag = Magnitude;
            return mag > 0 ? new IqSample(I / mag, Q / mag) : new IqSample(0, 0);
        }

        /// <summary>
        /// Creates an IQ sample from polar coordinates
        /// </summary>
        /// <param name="magnitude">The magnitude</param>
        /// <param name="phase">The phase in radians</param>
        /// <returns>A new IQ sample</returns>
        public static IqSample FromPolar(float magnitude, float phase) =>
            new(magnitude * MathF.Cos(phase), magnitude * MathF.Sin(phase));

        /// <inheritdoc/>
        public override string ToString() => $"({I:F4}, {Q:F4}j)";
    }
}
