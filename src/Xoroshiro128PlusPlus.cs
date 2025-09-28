// MIT License
// 
// Copyright (c) 2025 belnytheraseiche
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// Represents a 64-bit pseudo-random number generator based on the xoroshiro128++ algorithm.
/// </summary>
/// <remarks>
/// This generator is not cryptographically secure.
/// </remarks>
public class Xoroshiro128PlusPlus : ICloneable
{
    static readonly ulong[] Jmp = [0xDF900294D8F554A5ul, 0x170865DF4B3201FCul];
    ulong s0_;
    ulong s1_;

    // 
    // 

    /// <summary>
    /// Initializes a new instance of the <see cref="Xoroshiro128PlusPlus"/> class, using a unique seed value derived from a new GUID.
    /// </summary>
    public Xoroshiro128PlusPlus()
    : this(MemoryMarshal.Read<ulong>(Guid.NewGuid().ToByteArray()))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Xoroshiro128PlusPlus"/> class, using the specified seed value.
    /// </summary>
    /// <param name="seed">A number used to calculate a starting value for the pseudo-random number sequence.</param>
    public Xoroshiro128PlusPlus(ulong seed)
    {
        var s = seed;
        s0_ = __SplitMix64(ref s);
        s1_ = __SplitMix64(ref s);

        #region @@
        static ulong __SplitMix64(ref ulong s)
        {
            var z = s += 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
        #endregion
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Xoroshiro128PlusPlus"/> class by copying the state from another instance.
    /// </summary>
    /// <param name="obj">The <see cref="Xoroshiro128PlusPlus"/> instance to copy the state from.</param>
    /// <remarks>
    /// This constructor is primarily intended for replicating the state of the generator for parallel processing scenarios.
    /// After creating a copy, call the <see cref="Jump"/> method on one of the instances to ensure that each generator 
    /// produces a non-overlapping sequence of random numbers.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
    public Xoroshiro128PlusPlus(Xoroshiro128PlusPlus obj)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(obj);
#else
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));
#endif

        (s0_, s1_) = (obj.s0_, obj.s1_);
    }

    /// <summary>
    /// Creates a new object that is a shallow copy of the current instance.
    /// </summary>
    /// <returns>A new object that is a shallow copy of this instance.</returns>
    /// <remarks>
    /// This constructor is primarily intended for replicating the state of the generator for parallel processing scenarios.
    /// After creating a copy, call the <see cref="Jump"/> method on one of the instances to ensure that each generator 
    /// produces a non-overlapping sequence of random numbers.
    /// </remarks>
    public object Clone()
    => this.MemberwiseClone();

    /// <summary>
    /// Advances the state of the generator by 2^64 steps.
    /// </summary>
    /// <remarks>
    /// This is equivalent to generating 2^64 random numbers and discarding them, but it is executed much faster.
    /// It is useful for creating non-overlapping subsequences for parallel random number generation.
    /// </remarks>
    public void Jump()
    {
        var (s0, s1) = (0ul, 0ul);
        for (var i = 0; i < Jmp.Length; i++)
        {
            for (var bit = 0; bit < 64; bit++)
            {
                if ((Jmp[i] & (1ul << bit)) != 0)
                    (s0, s1) = (s0 ^ s0_, s1 ^ s1_);
                NextUInt64();
            }
        }
        (s0_, s1_) = (s0, s1);
    }

    /// <summary>
    /// Generates the next pseudo-random number.
    /// </summary>
    /// <returns>A 64-bit unsigned integer containing the generated random value.</returns>
    /// <exclude />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(s0_ + s1_, 17) + s0_;
        var (s0, s1) = (s0_, s1_);
        s1 ^= s0;
        s0_ = BitOperations.RotateLeft(s0, 49) ^ s1 ^ (s1 << 21);
        s1_ = BitOperations.RotateLeft(s1, 28);
        return result;
    }

    /// <summary>
    /// Returns a non-negative random integer.
    /// </summary>
    /// <returns>A 32-bit signed integer that is greater than or equal to 0 and less than <see cref="Int32.MaxValue"/>.</returns>
    public int Next()
    {
        var value = 0;
        while (Int32.MaxValue == (value = (int)(NextUInt64() >> 33)))
            ;
        return value;
    }

    /// <summary>
    /// Returns a non-negative random integer that is less than the specified maximum.
    /// </summary>
    /// <param name="maxValue">The exclusive upper bound of the random number to be generated. <paramref name="maxValue"/> must be greater than or equal to 0.</param>
    /// <returns>A 32-bit signed integer that is greater than or equal to 0, and less than <paramref name="maxValue"/>.</returns>
    /// <remarks>If <paramref name="maxValue"/> is 0, this method will always return 0.</remarks>
    public int Next(int maxValue)
    => Next(0, maxValue);

    /// <summary>
    /// Returns a random integer that is within a specified range.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
    /// <param name="maxValue">The exclusive upper bound of the random number returned. <paramref name="maxValue"/> must be greater than or equal to <paramref name="minValue"/>.</param>
    /// <returns>A 32-bit signed integer greater than or equal to <paramref name="minValue"/> and less than <paramref name="maxValue"/>.</returns>
    /// <remarks>
    /// If <paramref name="minValue"/> equals <paramref name="maxValue"/>, this method returns <paramref name="minValue"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minValue"/> is greater than <paramref name="maxValue"/>.</exception>
    public int Next(int minValue, int maxValue)
    => (int)NextInt64(minValue, maxValue);

    /// <summary>
    /// Returns a non-negative random 64-bit integer.
    /// </summary>
    /// <returns>A 64-bit signed integer that is greater than or equal to 0 and less than <see cref="Int64.MaxValue"/>.</returns>
    public long NextInt64()
    {
        var value = 0L;
        while (Int64.MaxValue == (value = (long)(NextUInt64() >> 1)))
            ;
        return value;
    }

    /// <summary>
    /// Returns a non-negative random 64-bit integer that is less than the specified maximum.
    /// </summary>
    /// <param name="maxValue">The exclusive upper bound of the random number to be generated. <paramref name="maxValue"/> must be greater than or equal to 0.</param>
    /// <returns>A 64-bit signed integer that is greater than or equal to 0, and less than <paramref name="maxValue"/>.</returns>
    /// <remarks>If <paramref name="maxValue"/> is 0, this method will always return 0.</remarks>
    public long NextInt64(long maxValue)
    => NextInt64(0, maxValue);

    /// <summary>
    /// Returns a random 64-bit integer that is within a specified range.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
    /// <param name="maxValue">The exclusive upper bound of the random number returned. <paramref name="maxValue"/> must be greater than or equal to <paramref name="minValue"/>.</param>
    /// <returns>A 64-bit signed integer greater than or equal to <paramref name="minValue"/> and less than <paramref name="maxValue"/>.</returns>
    /// <remarks>
    /// If <paramref name="minValue"/> equals <paramref name="maxValue"/>, this method returns <paramref name="minValue"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minValue"/> is greater than <paramref name="maxValue"/>.</exception>
    public long NextInt64(long minValue, long maxValue)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
#else
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), $"{nameof(minValue)} must be less than or equal {nameof(maxValue)}.");
#endif

        if (minValue == maxValue)
            return minValue;

        var range = (ulong)(maxValue - minValue);
        var threshold = UInt64.MaxValue - (UInt64.MaxValue % range) - 1;
        var value = 0ul;
        while (threshold < (value = NextUInt64()))
            ;
        return unchecked(minValue + (long)(value % range));
    }

    /// <summary>
    /// Returns a random single-precision floating-point number that is greater than or equal to 0.0, and less than 1.0.
    /// </summary>
    /// <returns>A single-precision floating-point number that is greater than or equal to 0.0f and less than 1.0f.</returns>
    public float NextSingle()
    => (NextUInt64() >> 40) * (1.0f / (1u << 24));

    /// <summary>
    /// Returns a random double-precision floating-point number that is greater than or equal to 0.0, and less than 1.0.
    /// </summary>
    /// <returns>A double-precision floating-point number that is greater than or equal to 0.0 and less than 1.0.</returns>
    public double NextDouble()
    => (NextUInt64() >> 11) * (1.0 / (1ul << 53));

    /// <summary>
    /// Fills the elements of a specified span of bytes with random numbers.
    /// </summary>
    /// <param name="buffer">The span of bytes to fill with random numbers.</param>
    public void NextBytes(Span<byte> buffer)
    {
        var span = buffer;
        while (span.Length >= 8)
        {
            var value = NextUInt64();
#if NETSTANDARD2_1
            MemoryMarshal.Write(span, ref value);
#else
            MemoryMarshal.Write(span, in value);
#endif
            span = span[8..];
        }
        if (!span.IsEmpty)
            MemoryMarshal.AsBytes([NextUInt64()])[..span.Length].CopyTo(span);
    }

    /// <summary>
    /// Randomizes the order of the elements in a span.
    /// </summary>
    /// <typeparam name="T">The type of the elements of the span.</typeparam>
    /// <param name="values">The span whose elements to shuffle.</param>
    /// <remarks>This method uses the Fisher-Yates shuffle algorithm.</remarks>
    public void Shuffle<T>(Span<T> values)
    {
        // fisher-yates shuffle
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (values[j], values[i]) = (values[i], values[j]);
        }
    }

    /// <summary>
    /// Randomizes the order of the elements in a list.
    /// </summary>
    /// <typeparam name="T">The type of the elements of the list.</typeparam>
    /// <param name="values">The list whose elements to shuffle.</param>
    /// <remarks>This method uses the Fisher-Yates shuffle algorithm.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    public void Shuffle<T>(IList<T> values)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(values);
#else
        if (values == null)
            throw new ArgumentNullException(nameof(values));
#endif

        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (values[j], values[i]) = (values[i], values[j]);
        }
    }
}
