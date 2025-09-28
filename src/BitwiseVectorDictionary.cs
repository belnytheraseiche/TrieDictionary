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

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// A concrete implementation of <see cref="KeyRecordDictionary"/> that uses a high-performance, array-based trie structure.
/// </summary>
/// <remarks>
/// This class represents a trie data structure that is flattened into a series of arrays using a level-order (breadth-first) build process.
/// The tree's topology is stored using integer arrays for parent/child relationships, while a bit vector is used to mark terminal nodes.
/// This approach prioritizes lookup performance and a relatively straightforward implementation over the extreme memory compactness of structures like LOUDS.
/// <para>
/// <strong>Important Usage Notes:</strong>
/// <list type="bullet">
/// <item><description><strong>Read-Only Structure:</strong> The dictionary is built once from a collection of keys and is effectively read-only afterward. Key addition and removal operations are not supported and will throw a <see cref="NotSupportedException"/>.</description></item>
/// <item><description><strong>Key Reconstruction:</strong> Getting a key by its identifier requires traversing the structure from a node up to the root.</description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class BitwiseVectorDictionary : LevelOrderBitsDictionary
{
    readonly int[] edges_;

    // 
    // 

    BitwiseVectorDictionary(Init1 init, SearchDirectionType searchDirection = SearchDirectionType.LTR)
    : base(init, searchDirection)
    {
        edges_ = init.Edges;
    }

    /// <summary>
    /// Creates a new <see cref="BitwiseVectorDictionary"/> from a collection of keys.
    /// </summary>
    /// <param name="keys">An enumerable collection of byte arrays to use as the initial keys. The collection does not need to be pre-sorted. Duplicate keys are permitted, but only the first occurrence will be added. The collection must not contain any elements that are null or empty byte array.</param>
    /// <param name="searchDirection">The key search direction for the new dictionary.</param>
    /// <returns>A new, optimized, and read-only <see cref="BitwiseVectorDictionary"/> instance.</returns>
    /// <remarks>The input keys are sorted and used to construct the Bitwise-Vector trie representation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    public static BitwiseVectorDictionary Create(IEnumerable<byte[]> keys, SearchDirectionType searchDirection = SearchDirectionType.LTR)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(keys);
#else
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));
#endif

        var enumerator = new HashSet<byte[]>(keys.Where(n => n is not null and not { Length: 0 }), ByteArrayComparer.Instance).Select(n => searchDirection switch
        {
            SearchDirectionType.LTR => n,
            SearchDirectionType.RTL => Rtl.Reverse(n),
            _ => throw new ArgumentException($"Search direction is invalid."),
        }).Order(Ltr.SortComparer);

        var init = BulkBuild(enumerator);
        return searchDirection switch
        {
            SearchDirectionType.LTR => new LtrDictionary(init),
            SearchDirectionType.RTL => new RtlDictionary(init),
            _ => throw new ArgumentException($"Search direction is invalid."),
        };
    }

    /// <summary>
    /// Serializes the state of the dictionary into a stream.
    /// </summary>
    /// <param name="dictionary">The dictionary instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="KeyRecordDictionary.SerializationOptions.Default"/> will be used.</param>
    /// <remarks>The serialization format is a custom binary format specific to this Bitwise-Vector implementation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(BitwiseVectorDictionary dictionary, Stream stream, SerializationOptions? options = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (dictionary == null)
            throw new ArgumentNullException(nameof(dictionary));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var compressionLevel = (options ?? SerializationOptions.Default).CompressionLevel;

        var firstPosition = stream.Position;
        var xxh = new XxHash32();
        var buffer0 = new byte[4160];
        stream.Write(buffer0);

        // labels
        var length1 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                Span<byte> buffer1 = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, dictionary.labels_.Length);
                compressStream.Write(buffer1);
                compressStream.Write(dictionary.labels_);
            }
            var array = memoryStream.ToArray();
            length1 = array.Length;
            stream.Write(array);
            xxh.Append(array);
        }
        // edges
        var length2 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                var buffer1 = new byte[dictionary.edges_.Length * 4 + 4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, dictionary.edges_.Length);
                for (var i = 0; i < dictionary.edges_.Length; i++)
                    BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(i * 4 + 4, 4), dictionary.edges_[i]);
                compressStream.Write(buffer1);
            }
            var array = memoryStream.ToArray();
            length2 = array.Length;
            stream.Write(array);
            xxh.Append(array);
        }
        // parents
        var length3 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                var buffer1 = new byte[dictionary.parents_.Length * 4 + 4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, dictionary.parents_.Length);
                for (var i = 0; i < dictionary.parents_.Length; i++)
                    BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(i * 4 + 4, 4), dictionary.parents_[i]);
                compressStream.Write(buffer1);
            }
            var array = memoryStream.ToArray();
            length3 = array.Length;
            stream.Write(array);
            xxh.Append(array);
        }
        // terminal
        var length4 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                var buffer1 = new byte[dictionary.terminal_.Buffer.Length * 8 + 4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, dictionary.terminal_.Buffer.Length);
                for (var i = 0; i < dictionary.terminal_.Buffer.Length; i++)
                    BinaryPrimitives.WriteUInt64LittleEndian(buffer1.AsSpan(i * 8 + 4, 8), dictionary.terminal_.Buffer[i]);
                compressStream.Write(buffer1);
            }
            var array = memoryStream.ToArray();
            length4 = array.Length;
            stream.Write(array);
            xxh.Append(array);
        }
        // llmap
        var length5 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                var buffer1 = new byte[dictionary.llmap_.Count * 8 + 4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, dictionary.llmap_.Count);
                var index = 0;
                foreach (var (key, value) in dictionary.llmap_)
                    BinaryPrimitives.WriteUInt64LittleEndian(buffer1.AsSpan(index++ * 8 + 4, 8), ((ulong)(uint)key << 32) | (uint)value);
                compressStream.Write(buffer1);
            }
            var array = memoryStream.ToArray();
            length5 = array.Length;
            stream.Write(array);
            xxh.Append(array);
        }
        // records
        var bytesRecords = PrimitiveRecordStore.Serialize(dictionary.persistentRecords_, new() { CompressionLevel = compressionLevel });
        stream.Write(bytesRecords);
        // additional2
        stream.Write(dictionary.Additional2);
        xxh.Append(dictionary.Additional2);

        Span<byte> xxhc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(xxhc, xxh.GetCurrentHashAsUInt32());

        var lastPosition = stream.Position;
        //  0: byte * 4, BWV1
        "BWV1"u8.CopyTo(buffer0);// [0x42, 0x57, 0x56,x 0x31]
        //  4: uint * 1, xxhash
        xxhc.CopyTo(buffer0.AsSpan(4));
        //  8: int * 1, labels bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(8), length1);
        // 12: int * 1, edges bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(12), length2);
        // 16: int * 1, parents bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(16), length3);
        // 20: int * 1, terminal bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(20), length4);
        // 24: int * 1, direction
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(24), dictionary.SearchDirection == SearchDirectionType.LTR ? 0 : 1);
        // 28: int * 1, additional1 size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(28), dictionary.Additional1.Length);
        // 32: int * 1, additional2 size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(32), dictionary.Additional2.Length);
        // 36: int * 1, record size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(36), bytesRecords.Length);
        // 40: int * 1, llmap bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(40), length5);
        // 44: int * 1, max depth
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(44), dictionary.maxDepth_);
        // 48: int * 1, terminal count
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(48), dictionary.terminal_.Count);
        // 52- empty
        // 64: byte * 4096, additional1
        dictionary.Additional1.CopyTo(buffer0.AsSpan(64, dictionary.Additional1.Length));
        stream.Seek(firstPosition, SeekOrigin.Begin);
        stream.Write(buffer0);

        stream.Seek(lastPosition, SeekOrigin.Begin);
    }

    /// <summary>
    /// Deserializes a dictionary from a stream.
    /// </summary>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <see cref="BitwiseVectorDictionary"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream data is corrupted or in an unsupported format.</exception>
    public static BitwiseVectorDictionary Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[4160];
        stream.ReadExactly(buffer0);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("BWV1"u8))// [0x42, 0x57, 0x56,x 0x31]
            throw new InvalidDataException("Unsupported format.");

        var xxh = new XxHash32();
        var xxhc = BinaryPrimitives.ReadUInt32LittleEndian(buffer0.AsSpan(4));

        Span<byte> buffer1 = stackalloc byte[4];

        // labels
        var length1 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(8));
        byte[]? labels;
        {
            var buffer2 = new byte[length1];
            stream.ReadExactly(buffer2);
            xxh.Append(buffer2);

            using var memoryStream = new MemoryStream(buffer2);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            decompressStream.ReadExactly(buffer1);
            var count = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            labels = new byte[count];
            decompressStream.ReadExactly(labels);
        }
        // edges
        var length2 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(12));
        int[]? edges;
        {
            var buffer2 = new byte[length2];
            stream.ReadExactly(buffer2);
            xxh.Append(buffer2);

            using var memoryStream = new MemoryStream(buffer2);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            decompressStream.ReadExactly(buffer1);
            var count = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            var buffer3 = new byte[count * 4];
            decompressStream.ReadExactly(buffer3);
            edges = new int[count];
            for (var i = 0; i < count; i++)
                edges[i] = BinaryPrimitives.ReadInt32LittleEndian(buffer3.AsSpan(i * 4, 4));
        }
        // parents
        var length3 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(16));
        int[]? parents;
        {
            var buffer2 = new byte[length3];
            stream.ReadExactly(buffer2);
            xxh.Append(buffer2);

            using var memoryStream = new MemoryStream(buffer2);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            decompressStream.ReadExactly(buffer1);
            var count = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            var buffer3 = new byte[count * 4];
            decompressStream.ReadExactly(buffer3);
            parents = new int[count];
            for (var i = 0; i < count; i++)
                parents[i] = BinaryPrimitives.ReadInt32LittleEndian(buffer3.AsSpan(i * 4, 4));
        }
        // terminal
        var length4 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(20));
        ulong[]? terminal;
        {
            var buffer2 = new byte[length4];
            stream.ReadExactly(buffer2);
            xxh.Append(buffer2);

            using var memoryStream = new MemoryStream(buffer2);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            decompressStream.ReadExactly(buffer1);
            var count = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            var buffer3 = new byte[count * 8];
            decompressStream.ReadExactly(buffer3);
            terminal = new ulong[count];
            for (var i = 0; i < count; i++)
                terminal[i] = BinaryPrimitives.ReadUInt64LittleEndian(buffer3.AsSpan(i * 8, 8));
        }
        // llmap
        var length5 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(40));
        Dictionary<int, int>? llmap;
        {
            var buffer2 = new byte[length5];
            stream.ReadExactly(buffer2);
            xxh.Append(buffer2);

            using var memoryStream = new MemoryStream(buffer2);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            decompressStream.ReadExactly(buffer1);
            var count = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            var buffer3 = new byte[count * 8];
            decompressStream.ReadExactly(buffer3);
            var array = new ulong[count];
            for (var i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReadUInt64LittleEndian(buffer3.AsSpan(i * 8, 8));
            llmap = array.ToDictionary(n => (int)(n >> 32), n => (int)(uint)n);
        }

        var additional1 = buffer0.AsSpan(64, BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(28)));
        var init = new Init1(labels, edges, parents, new ImmutableBitSet(terminal, BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(48))), BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(44)), llmap);
        BitwiseVectorDictionary dictionary = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(24)) switch
        {
            0 => new LtrDictionary(init) { Additional1 = [.. additional1] },
            1 => new RtlDictionary(init) { Additional1 = [.. additional1] },
            _ => throw new InvalidDataException("Broken."),
        };

        // records
        var bytesRecords = new byte[BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(36))];
        stream.ReadExactly(bytesRecords);
        dictionary.persistentRecords_ = PrimitiveRecordStore.Deserialize(bytesRecords);
        // additional2
        var additional2 = new byte[BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(32))];
        stream.ReadExactly(additional2);
        dictionary.Additional2 = additional2;
        xxh.Append(additional2);

        if (xxhc != BinaryPrimitives.ReadUInt32LittleEndian(buffer0.AsSpan(4)))
            throw new InvalidDataException("Broken.");

        return dictionary;
    }

    static Init1 BulkBuild(IEnumerable<byte[]> keys)
    {
        var array = keys.ToArray();
        var labels = new ValueBuffer<byte>(65536, true);
        var parents = new ValueBuffer<int>(65536, true);
        var edges = new ValueBuffer<int>(65536, true);
        var terminal = new BitSet();
        var queue = new Queue<BulkPending>(65536);
        var maxDepth = array.MaxBy(n => n.Length)?.Length ?? 0;

        if (maxDepth == 0)
            throw new ArgumentException($"{nameof(keys)} are empty.");

        edges.Add(0);
        {
            var start = 0;
            while (start < array.Length)
            {
                var key = array[start];
                var code = key[0];
                var end = start + 1;
                while (end < array.Length && array[end][0] == code)
                    end++;
                labels.Add(code);
                parents.Add(-1);
                terminal.Add(key.Length == 1);
                queue.Enqueue(new(start, end, labels.Count - 1, 1));
                start = end;
            }
        }
        while (queue.TryDequeue(out var pending))
        {
            edges.Add(labels.Count);
            var start = pending.Start;
            if (start < pending.End && array[start].Length == pending.Depth)
                start++;
            while (start < pending.End)
            {
                var key = array[start];
                var code = key[pending.Depth];
                var end = start + 1;
                while (end < pending.End)
                {
                    if (array[end].Length <= pending.Depth || array[end][pending.Depth] != code)
                        break;
                    end++;
                }
                labels.Add(code);
                parents.Add(pending.Node);
                terminal.Add(key.Length == pending.Depth + 1);
                queue.Enqueue(new(start, end, labels.Count - 1, pending.Depth + 1));
                start = end;
            }
        }

        edges.Add(labels.Count);
        return new(labels.ToArray(), edges.ToArray(), parents.ToArray(), terminal.ToImmutable(), maxDepth, []);
    }

    /// <exclude />
    protected override (int, int) FindRange1(int parent)
    => (edges_[parent], edges_[parent + 1]);

    // 
    // 

    class LtrDictionary(Init1 init) : BitwiseVectorDictionary(init, SearchDirectionType.LTR)
    {
    }

    class RtlDictionary(Init1 init) : BitwiseVectorDictionary(init, SearchDirectionType.RTL)
    {
        public override bool TryGetKey(int identifier, out byte[] key)
        => Rtl.ImplTryGetKey(this, identifier, out key);

        public override byte[] GetKey(int identifier)
        => Rtl.ImplGetKey(this, identifier);

        public override bool Contains(ReadOnlySpan<byte> key)
        => Rtl.ImplContains(this, key);

        public override int SearchExactly(ReadOnlySpan<byte> sequence)
        => Rtl.ImplSearchExactly(this, sequence);

        public override IEnumerable<(int, byte[])> SearchCommonPrefix(ReadOnlySpan<byte> sequence)
        => Rtl.ImplSearchCommonPrefix(this, sequence);

        public override (int, byte[]) SearchLongestPrefix(ReadOnlySpan<byte> sequence)
        => Rtl.ImplSearchLongestPrefix(this, sequence);

        public override IEnumerable<(int, byte[])> SearchByPrefix(ReadOnlySpan<byte> sequence, bool reverse = false)
        => Rtl.ImplSearchByPrefix(this, sequence, reverse);

        public override IEnumerable<(int, byte[])> EnumerateAll(bool reverse = false)
        => Rtl.ImplEnumerateAll(this, reverse);

        public override IEnumerable<(int, byte[])> SearchWildcard(ReadOnlySpan<byte> sequence, ReadOnlySpan<char> cards, bool reverse = false)
        => Rtl.ImplSearchWildcard(this, sequence, cards, reverse);

        public override bool FindFirst(out int identifier, out byte[] key)
        => Rtl.ImplFindFirst(this, out identifier, out key);

        public override bool FindLast(out int identifier, out byte[] key)
        => Rtl.ImplFindLast(this, out identifier, out key);

        public override bool FindNext(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
        => Rtl.ImplFindNext(this, currentIdentifier, out foundIdentifier, out foundKey);

        public override bool FindNext(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
        => Rtl.ImplFindNext(this, currentKey, out foundIdentifier, out foundKey);

        public override bool FindPrevious(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
        => Rtl.ImplFindPrevious(this, currentIdentifier, out foundIdentifier, out foundKey);

        public override bool FindPrevious(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
        => Rtl.ImplFindPrevious(this, currentKey, out foundIdentifier, out foundKey);
    }

    record BulkPending(int Start, int End, int Node, int Depth);
    record Init1(byte[] Labels, int[] Edges, int[] Parents, ImmutableBitSet Terminal, int MaxDepth, Dictionary<int, int> LLMap) : Init0(Labels, Parents, Terminal, MaxDepth, LLMap);
}
