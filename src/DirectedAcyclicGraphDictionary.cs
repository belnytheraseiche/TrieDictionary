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

using System.Text;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Collections;
using System.Buffers;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// A concrete implementation of <see cref="KeyRecordDictionary"/> that uses a Directed Acyclic Word Graph (DAWG).
/// </summary>
/// <remarks>
/// This class stores keys in a DAWG, which is a data structure that compresses a standard Trie
/// by merging common prefixes and suffixes to reduce redundancy.
/// <para>
/// <strong>Important Usage Notes:</strong>
/// <list type="bullet">
/// <item><description><strong>Read-Only Structure:</strong> The dictionary is built once from a collection of keys and is effectively read-only afterward. The <c>Add</c> and <c>Remove</c> operations for keys are not supported and will throw a <see cref="NotSupportedException"/>.</description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class DirectedAcyclicGraphDictionary : KeyRecordDictionary, KeyRecordDictionary.IKeyAccess, KeyRecordDictionary.IAltKeyAccess
{
    const int KeyBit = 0x40000000;
    static readonly ArrayPool<(byte, Node)> edgePool_ = ArrayPool<(byte, Node)>.Shared;
    readonly Node root_;
    readonly Node[] nodes_;
    readonly int maxDepth_;
    readonly Dictionary<int, int> llmap_ = [];

    // 
    // 

    DirectedAcyclicGraphDictionary(Init init, SearchDirectionType searchDirection = SearchDirectionType.LTR)
    : base(searchDirection)
    {
        root_ = init.Root;
        nodes_ = init.Nodes;
        maxDepth_ = init.MaxDepth;
        llmap_ = init.LLMap;
    }

    /// <summary>
    /// Creates a new <see cref="DirectedAcyclicGraphDictionary"/> from a collection of keys.
    /// </summary>
    /// <param name="keys">An enumerable collection of byte arrays to use as the initial keys. The collection does not need to be pre-sorted. Duplicate keys are permitted, but only the first occurrence will be added. The collection must not contain any elements that are null or empty byte array.</param>
    /// <param name="searchDirection">The key search direction for the new dictionary.</param>
    /// <returns>A new, optimized, and read-only <see cref="DirectedAcyclicGraphDictionary"/> instance.</returns>
    /// <remarks>The input keys are sorted and used to build a minimal, compressed graph structure.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    public static DirectedAcyclicGraphDictionary Create(IEnumerable<byte[]> keys, SearchDirectionType searchDirection = SearchDirectionType.LTR)
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
    /// <remarks>The serialization format is a custom binary format specific to this graph implementation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(DirectedAcyclicGraphDictionary dictionary, Stream stream, SerializationOptions? options = null)
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

        // nodes
        var nodecount = dictionary.nodes_.Length;
        var length1 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel);
                // write nodes without edges
                var buffer2 = new byte[Math.Max(10 * dictionary.nodes_.Length, 1280)];
                var offset1 = 0;
                foreach (var node in dictionary.nodes_)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(buffer2.AsSpan(offset1, 2), (ushort)node.Edges.Length);
                    BinaryPrimitives.WriteInt32LittleEndian(buffer2.AsSpan(offset1 + 2, 4), node.EndpointIdentifier);
                    BinaryPrimitives.WriteInt32LittleEndian(buffer2.AsSpan(offset1 + 6, 4), node.AtomicIdentifier);
                    offset1 += 10;
                }
                compressStream.Write(buffer2.AsSpan(0, offset1));
                // write edges
                foreach (var node in dictionary.nodes_)
                {
                    var offset2 = 0;
                    foreach (var (code, next) in node.Edges)
                    {
                        buffer2[offset2] = code;
                        BinaryPrimitives.WriteInt32LittleEndian(buffer2.AsSpan(offset2 + 1, 4), next);
                        offset2 += 5;
                    }
                    compressStream.Write(buffer2.AsSpan(0, offset2));
                }
            }
            var buffer1 = memoryStream.ToArray();
            length1 = buffer1.Length;
            stream.Write(buffer1);
            xxh.Append(buffer1);
        }
        // llmap
        var length2 = 0;
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                var buffer2 = new byte[dictionary.llmap_.Count * 8 + 4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer2, dictionary.llmap_.Count);
                var index = 0;
                foreach (var (key, value) in dictionary.llmap_)
                    BinaryPrimitives.WriteUInt64LittleEndian(buffer2.AsSpan(index++ * 8 + 4, 8), ((ulong)(uint)key << 32) | (uint)value);
                compressStream.Write(buffer2);
            }
            var array = memoryStream.ToArray();
            length2 = array.Length;
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
        //  0: byte * 4, DAG1
        "DAG1"u8.CopyTo(buffer0);// [0x44, 0x41, 0x47, 0x31]
        //  4: uint * 1, xxhash
        xxhc.CopyTo(buffer0.AsSpan(4));
        //  8: int * 1, node count
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(8), nodecount);
        // 12: int * 1, node size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(12), length1);
        // 16: int * 1, max depth
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(16), dictionary.maxDepth_);
        // 20: int * 1, reserved
        // 24: int * 1, direction
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(24), dictionary.SearchDirection == SearchDirectionType.LTR ? 0 : 1);
        // 28: int * 1, additional1 size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(28), dictionary.Additional1.Length);
        // 32: int * 1, additional2 size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(32), dictionary.Additional2.Length);
        // 36: int * 1, record size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(36), bytesRecords.Length);
        // 40: int * 1, llmap bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(40), length2);
        // 44- empty
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
    /// <returns>A new instance of <see cref="DirectedAcyclicGraphDictionary"/>.</returns>
    /// <exception cref="InvalidDataException">The stream data is corrupted or in an unsupported format.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static DirectedAcyclicGraphDictionary Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[4160];
        stream.ReadExactly(buffer0);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("DAG1"u8))// [0x44, 0x41, 0x47, 0x31]
            throw new InvalidDataException("Unsupported format.");

        var xxh = new XxHash32();
        var xxhc = BinaryPrimitives.ReadUInt32LittleEndian(buffer0.AsSpan(4));

        // nodes
        Node? root = null;
        var nodes = new Node[BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(8))];
        {
            var buffer1 = new byte[BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(12))];
            stream.ReadExactly(buffer1);
            xxh.Append(buffer1);

            using var memoryStream = new MemoryStream(buffer1);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            var buffer2 = new byte[10 * nodes.Length + 1280];
            var offset1 = 0;
            decompressStream.ReadExactly(buffer2, 0, 10 * nodes.Length);
            for (var i = 0; i < nodes.Length; i++)
            {
                var edgecount = BinaryPrimitives.ReadUInt16LittleEndian(buffer2.AsSpan(offset1, 2));
                var identifier1 = BinaryPrimitives.ReadInt32LittleEndian(buffer2.AsSpan(offset1 + 2, 4));
                var identifier2 = BinaryPrimitives.ReadInt32LittleEndian(buffer2.AsSpan(offset1 + 6, 4));
                var edges = new (byte, int)[edgecount];
                var offset2 = buffer2.Length - 1280;
                decompressStream.ReadExactly(buffer2, offset2, edgecount * 5);
                for (var j = 0; j < edges.Length; j++)
                {
                    edges[j] = (buffer2[offset2], BinaryPrimitives.ReadInt32LittleEndian(buffer2.AsSpan(offset2 + 1, 4)));
                    offset2 += 5;
                }
                nodes[i] = new(identifier2) { EndpointIdentifier = identifier1, Edges = edges };
                if (identifier2 == 0)
                    root = nodes[i];
                offset1 += 10;
            }
        }
        // llmap
        Dictionary<int, int>? llmap;
        {
            Span<byte> buffer1 = stackalloc byte[4];

            var buffer2 = new byte[BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(40))];
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
        var init = new Init(root!, nodes, BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(16)), llmap);
        DirectedAcyclicGraphDictionary dictionary = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(24)) switch
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

    static Init BulkBuild(IEnumerable<byte[]> keys)
    {
        var root = new Node(0);
        var nodes = new Dictionary<int, Node>(65536);
        nodes.Add(0, root);

        var minimized = new Dictionary<string, Node>(65536);
        var unminimized = new List<Node>(65536);
        var identifier1 = 1;
        var identifier2 = 1;
        var maxDepth = 0;
        byte[] previous = [];
        foreach (var current in keys)
        {
            if (maxDepth < current.Length)
                maxDepth = current.Length;

            __Append(root, nodes, minimized, unminimized, previous, current, identifier1++ | KeyBit, ref identifier2);
            previous = current;
        }
        __Minimize(root, minimized, unminimized, previous, 0);

        return new(root, nodes.OrderBy(n => n.Key).Select(n => n.Value).ToArray(), maxDepth, []);

        #region @@
        static void __Append(Node root, Dictionary<int, Node> nodes, Dictionary<string, Node> minimized, List<Node> unminimized, byte[] keyPrevious, byte[] keyCurrent, int identifier1, ref int identifier2)
        {
            var commonLength = 0;
            for (var i = 0; i < keyCurrent.Length && i < keyPrevious.Length; i++)
                if (keyCurrent[i] != keyPrevious[i])
                    break;
                else
                    commonLength++;
            __Minimize(root, minimized, unminimized, keyPrevious, commonLength);
            var current = unminimized.Count == 0 ? root : unminimized.Last();
            foreach (var code in keyCurrent[commonLength..])
            {
                if (0 != ((1 + identifier2) & KeyBit))
                    throw new ApplicationException("Full.");

                var next = __NewNode(nodes, (int)(((uint)++identifier2 | 0x80000000u) & ~(uint)KeyBit));
                __AddEdge(current, (code, next));
                unminimized.Add(next);
                current = next;
            }
            current.EndpointIdentifier = identifier1;
        }
        static void __Minimize(Node root, Dictionary<string, Node> minimized, List<Node> unminimized, byte[] keyPrevious, int minLength)
        {
            for (var index = unminimized.Count - 1; index >= minLength; index--)
            {
                var node = unminimized[index];
                var signature = node.Signature();
                if (minimized.TryGetValue(signature, out var found))
                    __AddEdge(__Parent(root, unminimized, index), (keyPrevious[index], found));
                else
                    minimized.Add(signature, node);
                unminimized.RemoveAt(index);
            }

            #region @@
            static Node __Parent(Node root, List<Node> nodes, int index)
            => index > 0 ? nodes[index - 1] : root;
            #endregion
        }
        static void __AddEdge(Node node, (byte, Node) value)
        {
            if (!node.TryGetEdge(value.Item1, out var _))
                node.AddEdge(value.Item1, value.Item2.AtomicIdentifier);
        }
        static Node __NewNode(Dictionary<int, Node> nodes, int identifier)
        {
            var node = new Node(identifier);
            nodes.Add(identifier, node);
            return node;
        }
        #endregion
    }

    /// <summary>
    /// Returns a string-specialized wrapper for the dictionary's key access interface.
    /// </summary>
    /// <param name="encoding">The encoding to use for string conversions. Defaults to UTF-8.</param>
    /// <returns>A new <see cref="KeyRecordDictionary.StringSpecialized"/> instance providing string-based access to the dictionary.</returns>
    public StringSpecialized AsStringSpecialized(Encoding? encoding = null)
    => new(this, encoding ?? Encoding.UTF8);

    /// <summary>
    /// Gets an accessor for the list of records associated with a given key identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key whose records are to be accessed. This must be a valid key identifier obtained from a search or add operation.</param>
    /// <param name="isTransient">If true, accesses the transient (in-memory) record store; otherwise, accesses the persistent record store.</param>
    /// <returns>An <see cref="KeyRecordDictionary.IRecordAccess"/> handle for the specified record list.</returns>
    /// <remarks>Note that while new records can be added to a key's record list, new keys cannot be added to the dictionary itself after creation.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identifier"/> is negative.</exception>
    /// <exception cref="ArgumentException">The specified <paramref name="identifier"/> is invalid or does not exist.</exception>
    public IRecordAccess GetRecordAccess(int identifier, bool isTransient = false)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(identifier);
#else
        if (identifier <= 0)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must be greater than 0.");
#endif

        if ((identifier & KeyBit) == 0)
            throw new ArgumentOutOfRangeException(nameof(identifier));

        if (isTransient)
            return new BasicRecordAccess(transientRecords_.GetRecordAccess(identifier));
        else
        {
            if (llmap_.TryGetValue(identifier, out var real))
                return new PrimitiveRecordAccess(persistentRecords_.GetRecordAccess(real));
            else
            {
                var access = persistentRecords_.GetRecordAccess();
                llmap_.Add(identifier, access.Identifier);
                return new PrimitiveRecordAccess(access);
            }
        }
    }

    /// <summary>
    /// Determines whether the specified key exists in the graph.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>true if the key is found; otherwise, false.</returns>
    public virtual bool Contains(ReadOnlySpan<byte> key)
    => InternalSearchExactly(key) > 0;

    /// <exclude />
    bool IAltKeyAccess.Contains(ReadOnlySpan<byte> key)
    => InternalSearchExactly(key) > 0;

    /// <summary>
    /// Searches for an exact match of the given sequence and returns its identifier.
    /// </summary>
    /// <param name="sequence">The key to search for.</param>
    /// <returns>The positive identifier for the key if found; otherwise, a non-positive value.</returns>
    public virtual int SearchExactly(ReadOnlySpan<byte> sequence)
    => InternalSearchExactly(sequence);

    /// <exclude />
    int IAltKeyAccess.SearchExactly(ReadOnlySpan<byte> sequence)
    => InternalSearchExactly(sequence);

    int InternalSearchExactly(ReadOnlySpan<byte> sequence)
    {
        if (sequence.Length == 0)
            return -1;

        var current = root_;
        foreach (var code in sequence)
            if (!TryGetEdgeHelper(current, code, out current))
                return -1;
        return current.IsEnd ? current.EndpointIdentifier : -1;
    }

    /// <summary>
    /// Finds all keys in the dictionary that are prefixes of the given sequence.
    /// </summary>
    /// <param name="sequence">The sequence to search within.</param>
    /// <returns>An enumerable collection of (identifier, key) tuples for all matching prefixes.</returns>
    /// <remarks>
    /// This is the implementation of interface method.
    /// For detailed behavior and examples, see <see cref="KeyRecordDictionary.IKeyAccess.SearchCommonPrefix(ReadOnlySpan{byte})"/>.
    /// </remarks>
    public virtual IEnumerable<(int, byte[])> SearchCommonPrefix(ReadOnlySpan<byte> sequence)
    => InternalSearchCommonPrefix(sequence.ToArray());

    /// <exclude />
    IEnumerable<(int, byte[])> IAltKeyAccess.SearchCommonPrefix(ReadOnlySpan<byte> sequence)
    => InternalSearchCommonPrefix(sequence.ToArray());

    IEnumerable<(int, byte[])> InternalSearchCommonPrefix(byte[] sequence)
    {
        if (sequence.Length == 0)
            yield break;

        var current = root_;
        for (var i = 0; i < sequence.Length; i++)
        {
            var code = sequence[i];
            if (!TryGetEdgeHelper(current, code, out current))
                yield break;
            if (current.IsEnd)
                yield return (current.EndpointIdentifier, sequence[..(i + 1)]);
        }
    }

    /// <summary>
    /// Finds the longest key in the dictionary that is a prefix of the given sequence.
    /// </summary>
    /// <param name="sequence">The sequence to search within.</param>
    /// <returns>An enumerable collection of (identifier, key) tuples for all matching prefixes.</returns>
    /// <remarks>
    /// This is the implementation of interface method.
    /// For detailed behavior and examples, see <see cref="KeyRecordDictionary.IKeyAccess.SearchLongestPrefix(ReadOnlySpan{byte})"/>.
    /// </remarks>
    public virtual (int, byte[]) SearchLongestPrefix(ReadOnlySpan<byte> sequence)
    => InternalSearchLongestPrefix(sequence);

    /// <exclude />
    (int, byte[]) IAltKeyAccess.SearchLongestPrefix(ReadOnlySpan<byte> sequence)
    => InternalSearchLongestPrefix(sequence);

    (int, byte[]) InternalSearchLongestPrefix(ReadOnlySpan<byte> sequence)
    {
        var length = 0;
        var last = root_;
        var current = root_;
        for (var i = 0; i < sequence.Length; i++)
        {
            var code = sequence[i];
            if (!TryGetEdgeHelper(current, code, out current))
                break;
            if (current.IsEnd)
                (last, length) = (current, i + 1);
        }

        if (last.IsEnd)
            return (last.EndpointIdentifier, sequence[..length].ToArray());
        else
            return (-1, []);
    }

    /// <summary>
    /// Finds all keys that start with the given prefix.
    /// </summary>
    /// <param name="sequence">The prefix to search for.</param>
    /// <param name="reverse">If true, returns results in reverse lexicographical order.</param>
    /// <returns>An enumerable collection of (identifier, key) tuples for all keys starting with the prefix.</returns>
    /// <remarks>
    /// This is the implementation of interface method.
    /// For detailed behavior and examples, see <see cref="KeyRecordDictionary.IKeyAccess.SearchByPrefix(ReadOnlySpan{byte}, bool)"/>.
    /// </remarks>
    public virtual IEnumerable<(int, byte[])> SearchByPrefix(ReadOnlySpan<byte> sequence, bool reverse = false)
    => InternalSearchByPrefix(sequence, reverse);

    /// <exclude />
    IEnumerable<(int, byte[])> IAltKeyAccess.SearchByPrefix(ReadOnlySpan<byte> sequence, bool reverse)
    => InternalSearchByPrefix(sequence, reverse);

    IEnumerable<(int, byte[])> InternalSearchByPrefix(ReadOnlySpan<byte> sequence, bool reverse)
    => InternalSearchByPrefixNative([.. sequence], reverse);
    // => InternalSearchWildcard([.. sequence, 0], [.. Enumerable.Repeat('.', sequence.Length), '*'], reverse);// too slow

    IEnumerable<(int, byte[])> InternalSearchByPrefixNative(byte[] sequence, bool reverse)
    {
        if (sequence.Length == 0)
            yield break;

        var start = root_;
        foreach (var code in sequence)
            if (!TryGetEdgeHelper(start, code, out start))
                yield break;

        var rent = edgePool_.Rent(256);
        try
        {
            var stack = new Stack<(Node, byte[])>(4096);
            stack.Push((start, sequence));
            while (stack.TryPop(out var item))
            {
                var (current, key) = item;
                if (current.IsEnd)
                    yield return (current.EndpointIdentifier, key);

                foreach (var (code, next) in SortedEdgesHelper(rent.AsSpan(0, 256), current, !reverse))
                    stack.Push((next, [.. key, code]));
            }
#if false
            var stack = new Stack<Node>(4096);
            stack.Push(start);
            while (stack.TryPop(out var current))
            {
                if (current.IsEnd)
                    yield return (current.EndpointIdentifier, InternalGetKey(current.EndpointIdentifier));

                foreach (var (_, next) in SortedEdgesHelper(rent.AsSpan(0, 256), current, !reverse))
                    stack.Push(next);
            }
#endif
        }
        finally
        {
            edgePool_.Return(rent);
        }
    }

    /// <summary>
    /// Enumerates all keys in the dictionary in lexicographical order.
    /// </summary>
    /// <param name="reverse">If true, returns keys in reverse lexicographical order.</param>
    /// <returns>An enumerable collection of all (identifier, key) tuples.</returns>
    public virtual IEnumerable<(int, byte[])> EnumerateAll(bool reverse = false)
    => InternalEnumerateAll(reverse);

    /// <exclude />
    IEnumerable<(int, byte[])> IAltKeyAccess.EnumerateAll(bool reverse)
    => InternalEnumerateAll(reverse);

    IEnumerable<(int, byte[])> InternalEnumerateAll(bool reverse)
    {
        if (reverse)
        {
            if (InternalFindLast(out var identifier, out var key))
            {
                yield return (identifier, key);
                while (InternalFindPrevious(key, out identifier, out key))
                    yield return (identifier, key);
            }
        }
        else
        {
            if (InternalFindFirst(out var identifier, out var key))
            {
                yield return (identifier, key);
                while (InternalFindNext(key, out identifier, out key))
                    yield return (identifier, key);
            }
        }
    }

    /// <summary>
    /// Performs a wildcard search for keys matching a pattern.
    /// </summary>
    /// <param name="sequence">The byte sequence of the pattern.</param>
    /// <param name="cards">A sequence of characters ('?' for single, '*' for multiple wildcards) corresponding to the pattern.</param>
    /// <param name="reverse">If true, returns results in reverse order.</param>
    /// <returns>An enumerable collection of matching (identifier, key) tuples.</returns>
    /// <remarks>
    /// This is the implementation of interface method.
    /// For detailed behavior and examples, see <see cref="KeyRecordDictionary.IKeyAccess.SearchWildcard(ReadOnlySpan{byte}, ReadOnlySpan{char}, bool)"/>.
    /// </remarks>
    public virtual IEnumerable<(int, byte[])> SearchWildcard(ReadOnlySpan<byte> sequence, ReadOnlySpan<char> cards, bool reverse = false)
    => InternalSearchWildcard(sequence.ToArray(), cards.ToArray(), reverse);

    /// <exclude />
    IEnumerable<(int, byte[])> IAltKeyAccess.SearchWildcard(ReadOnlySpan<byte> sequence, ReadOnlySpan<char> cards, bool reverse)
    => InternalSearchWildcard(sequence.ToArray(), cards.ToArray(), reverse);

    IEnumerable<(int, byte[])> InternalSearchWildcard(byte[] sequence, char[] cards, bool reverse)
    {
        if (sequence.Length != cards.Length)
            throw new ArgumentException($"Length of {nameof(cards)} must be equal {nameof(sequence)}.");
        if (sequence.Length == 0)
            yield break;

        var bits0 = new BitArray(sequence.Length + 1);
        bits0[0] = true;
        __Epsilon(bits0);

        var frames = new Stack<FindFrame>();
        frames.Push(new(root_, 0, bits0, false));

        var rent = edgePool_.Rent(256);
        try
        {
            if (!reverse)
            {
                while (frames.TryPop(out var frame))
                {
                    if (__Accepted(frame.Bits))
                    {
                        if (frame.Node.IsEnd)
                            yield return (frame.Node.EndpointIdentifier, InternalGetKey(frame.Node.EndpointIdentifier));
                    }

                    foreach (var (code, next) in SortedEdgesHelper(rent.AsSpan(0, 256), frame.Node, true))
                    {
                        var bits1 = __Step(frame.Bits, code);
                        if (bits1.HasAnySet())
                            frames.Push(new(next, frame.Position + 1, bits1, false));
                    }
                }
            }
            else
            {
                while (frames.TryPop(out var frame))
                {
                    if (frame.PostVisit)
                    {
                        if (__Accepted(frame.Bits))
                        {
                            if (frame.Node.IsEnd)
                                yield return (frame.Node.EndpointIdentifier, InternalGetKey(frame.Node.EndpointIdentifier));
                        }
                        continue;
                    }

                    frames.Push(frame with { PostVisit = true });

                    foreach (var (code, next) in SortedEdgesHelper(rent.AsSpan(0, 256), frame.Node))
                    {
                        var bits1 = __Step(frame.Bits, code);
                        if (bits1.HasAnySet())
                            frames.Push(new(next, frame.Position + 1, bits1, false));
                    }
                }
            }
        }
        finally
        {
            edgePool_.Return(rent);
        }

        #region @@
        static bool __Accepted(BitArray bits)
        => bits[^1];
        BitArray __Epsilon(BitArray bits)
        {
            var loop = false;
            do
            {
                loop = false;
                for (var i = 0; i < bits.Length - 1; i++)
                {
                    if (cards[i] == '*' && bits[i] && !bits[i + 1])
                    {
                        bits[i + 1] = true;
                        loop = true;
                    }
                }
            } while (loop);
            return bits;
        }
        BitArray __Step(BitArray current, byte label)
        {
            var bits = new BitArray(current.Length);
            for (var i = 0; i < bits.Length - 1; i++)
            {
                if (!current[i])
                    continue;

                var card = cards[i];
                if (card == '*')
                    bits[i] = true;
                else if (card == '?')
                    bits[i + 1] = true;
                else if (sequence[i] == label)
                    bits[i + 1] = true;
            }
            return __Epsilon(bits);
        }
        #endregion
    }

    /// <summary>
    /// Finds the first key in the dictionary in lexicographical order.
    /// </summary>
    /// <param name="identifier">When this method returns, the identifier of the first key.</param>
    /// <param name="key">When this method returns, the first key.</param>
    /// <returns>true if the dictionary is not empty; otherwise, false.</returns>
    public virtual bool FindFirst(out int identifier, out byte[] key)
    => InternalFindFirst(out identifier, out key);

    /// <exclude />
    bool IAltKeyAccess.FindFirst(out int identifier, out byte[] key)
    => InternalFindFirst(out identifier, out key);

    bool InternalFindFirst(out int identifier, out byte[] key)
    {
        (identifier, key) = FindMinDescendant(root_, []);
        return identifier != -1;
    }

    /// <summary>
    /// Finds the last key in the dictionary in lexicographical order.
    /// </summary>
    /// <param name="identifier">When this method returns, the identifier of the last key.</param>
    /// <param name="key">When this method returns, the last key.</param>
    /// <returns>true if the dictionary is not empty; otherwise, false.</returns>
    public virtual bool FindLast(out int identifier, out byte[] key)
    => InternalFindLast(out identifier, out key);

    /// <exclude />
    bool IAltKeyAccess.FindLast(out int identifier, out byte[] key)
    => InternalFindLast(out identifier, out key);

    bool InternalFindLast(out int identifier, out byte[] key)
    {
        (identifier, key) = FindMaxDescendant(root_, []);
        return identifier != -1;
    }

    /// <summary>
    /// Finds the next key in lexicographical order after the specified identifier.
    /// </summary>
    /// <param name="currentIdentifier">The identifier to start the search from.</param>
    /// <param name="foundIdentifier">When this method returns, the identifier of the next key.</param>
    /// <param name="foundKey">When this method returns, the next key.</param>
    /// <returns>true if a next key was found; otherwise, false.</returns>
    public virtual bool FindNext(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
    => InternalFindNext(currentIdentifier, out foundIdentifier, out foundKey);

    /// <exclude />
    bool IAltKeyAccess.FindNext(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
    => InternalFindNext(currentIdentifier, out foundIdentifier, out foundKey);

    bool InternalFindNext(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
    {
        (foundIdentifier, foundKey) = (-1, []);
        if (InternalTryGetKey(currentIdentifier, out var currentKey))
            return InternalFindNext(currentKey, out foundIdentifier, out foundKey);
        else
            return false;
    }

    /// <summary>
    /// Finds the next key in lexicographical order after the specified key.
    /// </summary>
    /// <param name="currentKey">The key to start the search from.</param>
    /// <param name="foundIdentifier">When this method returns, the identifier of the next key.</param>
    /// <param name="foundKey">When this method returns, the next key.</param>
    /// <returns>true if a next key was found; otherwise, false.</returns>
    public virtual bool FindNext(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
    => InternalFindNext(currentKey, out foundIdentifier, out foundKey);

    /// <exclude />
    bool IAltKeyAccess.FindNext(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
    => InternalFindNext(currentKey, out foundIdentifier, out foundKey);

    bool InternalFindNext(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
    {
        (foundIdentifier, foundKey) = (-1, []);

        if (currentKey.Length == 0)
            throw new InvalidOperationException($"Not found {nameof(currentKey)}.");

        var path = new Stack<(byte, Node)>(currentKey.Length);
        var current = root_;
        foreach (var code in currentKey)
        {
            if (!TryGetEdgeHelper(current, code, out var next))
                throw new InvalidOperationException($"Not found {nameof(currentKey)}.");
            path.Push((code, current));
            current = next;
        }

        if (current.Edges.Length > 0)
        {
            var (code, next) = MinEdgeHelper(current);
            var result = FindMinDescendant(next, [.. currentKey, code]);
            if (result.Item1 != -1)
            {
                (foundIdentifier, foundKey) = result;
                return true;
            }
        }

        var rent = edgePool_.Rent(256);
        try
        {
            Span<(byte, Node)> buffer = rent.AsSpan(0, 256);
            var tmpKey = currentKey;
            while (path.Count != 0)
            {
                var (code1, parent) = path.Pop();
                tmpKey = tmpKey[..^1];

                foreach (var (code2, next) in SortedEdgesHelper(buffer, parent))
                {
                    if (code2 <= code1)
                        continue;
                    var nextResult = FindMinDescendant(next, [.. tmpKey, code2]);
                    if (nextResult.Item1 != -1)
                    {
                        (foundIdentifier, foundKey) = nextResult;
                        return true;
                    }
                }
            }
        }
        finally
        {
            edgePool_.Return(rent);
        }

        return false;
    }

    /// <summary>
    /// Finds the previous key in lexicographical order before the specified identifier.
    /// </summary>
    /// <param name="currentIdentifier">The identifier to start the search from.</param>
    /// <param name="foundIdentifier">When this method returns, the identifier of the previous key.</param>
    /// <param name="foundKey">When this method returns, the previous key.</param>
    /// <returns>true if a previous key was found; otherwise, false.</returns>
    public virtual bool FindPrevious(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
    => InternalFindPrevious(currentIdentifier, out foundIdentifier, out foundKey);

    /// <exclude />
    bool IAltKeyAccess.FindPrevious(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
    => InternalFindPrevious(currentIdentifier, out foundIdentifier, out foundKey);

    bool InternalFindPrevious(int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
    {
        (foundIdentifier, foundKey) = (-1, []);
        if (InternalTryGetKey(currentIdentifier, out var currentKey))
            return InternalFindPrevious(currentKey, out foundIdentifier, out foundKey);
        else
            return false;
    }

    /// <summary>
    /// Finds the previous key in lexicographical order before the specified key.
    /// </summary>
    /// <param name="currentKey">The key to start the search from.</param>
    /// <param name="foundIdentifier">When this method returns, the identifier of the previous key.</param>
    /// <param name="foundKey">When this method returns, the previous key.</param>
    /// <returns>true if a previous key was found; otherwise, false.</returns>
    public virtual bool FindPrevious(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
    => InternalFindPrevious(currentKey, out foundIdentifier, out foundKey);

    /// <exclude />
    bool IAltKeyAccess.FindPrevious(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
    => InternalFindPrevious(currentKey, out foundIdentifier, out foundKey);

    bool InternalFindPrevious(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
    {
        (foundIdentifier, foundKey) = (-1, []);

        if (currentKey.Length == 0)
            throw new InvalidOperationException($"Not found {nameof(currentKey)}.");

        var path = new Stack<(byte, Node)>(currentKey.Length);
        var current = root_;
        foreach (var code in currentKey)
        {
            if (!TryGetEdgeHelper(current, code, out var next))
                throw new InvalidOperationException($"Not found {nameof(currentKey)}.");
            path.Push((code, current));
            current = next;
        }

        var rent = edgePool_.Rent(256);
        try
        {
            Span<(byte, Node)> buffer = rent.AsSpan(0, 256);
            var tmpKey = currentKey.ToArray();
            while (path.Count != 0)
            {
                var (code1, parent) = path.Pop();
                tmpKey = tmpKey[..^1];
                foreach (var (code2, previous) in SortedEdgesHelper(buffer, parent, true))
                {
                    if (code2 >= code1)
                        continue;
                    var result = FindMaxDescendant(previous, [.. tmpKey, code2]);
                    if (result.Item1 != -1)
                    {
                        (foundIdentifier, foundKey) = result;
                        return true;
                    }
                }
                if (parent.IsEnd && parent != root_)
                {
                    (foundIdentifier, foundKey) = (parent.EndpointIdentifier, tmpKey);
                    return true;
                }
            }
        }
        finally
        {
            edgePool_.Return(rent);
        }

        return false;
    }

    (int, byte[]) FindMinDescendant(Node node, byte[] path)
    {
        if (node.IsEnd)
            return (node.EndpointIdentifier, path);
        if (node.Edges.Length != 0)
        {
            var (code, next) = MinEdgeHelper(node);
            return FindMinDescendant(next, [.. path, code]);
        }
        return (-1, []);
    }

    (int, byte[]) FindMaxDescendant(Node node, byte[] path)
    {
        if (node.Edges.Length != 0)
        {
            var (code, next) = MaxEdgeHelper(node);
            var result = FindMaxDescendant(next, [.. path, code]);
            if (result.Item1 != -1)
                return result;
        }
        if (node.IsEnd)
            return (node.EndpointIdentifier, path);
        return (-1, []);
    }

    /// <summary>
    /// Tries to get the key associated with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key to get.</param>
    /// <param name="key">When this method returns, contains the key if found; otherwise, an empty array.</param>
    /// <returns>true if the key was found; otherwise, false.</returns>
    public virtual bool TryGetKey(int identifier, out byte[] key)
    => InternalTryGetKey(identifier, out key);

    /// <exclude />
    bool IAltKeyAccess.TryGetKey(int identifier, out byte[] key)
    => InternalTryGetKey(identifier, out key);

    bool InternalTryGetKey(int identifier, out byte[] key)
    {
        key = [];
        if (identifier < 0 || (identifier & KeyBit) == 0)
            return false;

        key = InternalGetKeyUnsafe(identifier);
        return key is not [];
    }

    /// <summary>
    /// Gets the key associated with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key to get.</param>
    /// <returns>The key as a byte array.</returns>
    /// <exception cref="ArgumentException">The specified identifier does not exist.</exception>
    public virtual byte[] GetKey(int identifier)
    => InternalGetKey(identifier);

    /// <exclude />
    byte[] IAltKeyAccess.GetKey(int identifier)
    => InternalGetKey(identifier);

    byte[] InternalGetKey(int identifier)
    {
        if (identifier < 0 || (identifier & KeyBit) == 0)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"Invalid {nameof(identifier)}.");

        var key = InternalGetKeyUnsafe(identifier);
        if (key is not [])
            return key;

        throw new ArgumentException($"Invalid {nameof(identifier)}.");
    }

    byte[] InternalGetKeyUnsafe(int identifier)
    {
        var rent = edgePool_.Rent(256);
        try
        {
            var buffer = rent.AsSpan(0, 256);
            var stack = new Stack<(Node, byte[])>(maxDepth_);
            stack.Push((root_, []));
            while (stack.TryPop(out var current))
            {
                var (node, path) = current;
                if (node.IsEnd && node.EndpointIdentifier == identifier)
                    return [.. path];

                foreach (var (code, next) in SortedEdgesHelper(buffer, node, true))
                    stack.Push((next, [.. path, code]));
            }
        }
        finally
        {
            edgePool_.Return(rent);
        }

        return [];
    }

    /// <summary>
    /// This operation is not supported. The dictionary is read-only after creation.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public int Add(ReadOnlySpan<byte> key)
    => throw new NotSupportedException();

    /// <summary>
    /// This operation is not supported. The dictionary is read-only after creation.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public bool TryAdd(ReadOnlySpan<byte> key, out int identifier)
    => throw new NotSupportedException();

    /// <summary>
    /// This operation is not supported. The dictionary is read-only after creation.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public bool Remove(int identifier)
    => throw new NotSupportedException();

    /// <summary>
    /// This operation is not supported. The dictionary is read-only after creation.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public bool Remove(ReadOnlySpan<byte> key)
    => throw new NotSupportedException();

    bool TryGetEdgeHelper(Node target, byte code, out Node node)
    {
        node = null!;
        if (target.TryGetEdge(code, out var identifier))
        {
            node = NodeAt(nodes_, identifier);
            return true;
        }
        return false;
    }

    (byte, Node) MinEdgeHelper(Node target)
    {
        var value = target.MinEdge();
        return (value.Item1, NodeAt(nodes_, value.Item2));
    }

    (byte, Node) MaxEdgeHelper(Node target)
    {
        var value = target.MaxEdge();
        return (value.Item1, NodeAt(nodes_, value.Item2));
    }

    // IEnumerable<(byte, Node)> SortedEdgesHelper(Node target, bool descending = false)
    // => target.SortedEdges(descending).Select(n => (n.Item1, NodeAt(nodes_, n.Item2)));

    Span<(byte, Node)> SortedEdgesHelper(Span<(byte, Node)> buffer, Node target, bool descending = false)
    {
        var index = 0;
        foreach (var edge in target.SortedEdges(descending))
            buffer[index++] = (edge.Item1, NodeAt(nodes_, edge.Item2));
        return buffer[..index];
    }

    static Node NodeAt(Node[] nodes, int identifier)
    {
        var (l, r) = (0, nodes.Length - 1);
        while (l <= r)
        {
            var m = (l + r) / 2;
            if (nodes[m].AtomicIdentifier < identifier)
                l = m + 1;
            else if (nodes[m].AtomicIdentifier > identifier)
                r = m - 1;
            else
                return nodes[m];
        }

        throw new InvalidOperationException($"{identifier} is not registered.");
    }

    // 
    // 

    class LtrDictionary(Init init) : DirectedAcyclicGraphDictionary(init, SearchDirectionType.LTR)
    {
    }

    class RtlDictionary(Init init) : DirectedAcyclicGraphDictionary(init, SearchDirectionType.RTL)
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

    class Node(int identifier)
    {
        const int SortThreshold = 12;

        public int AtomicIdentifier { get; set; } = identifier;
        public int EndpointIdentifier { get; set; } = identifier;
        public (byte, int)[] Edges { get; set; } = [];
        public bool IsEnd { get => 0 != (this.EndpointIdentifier & KeyBit); }

        public void AddEdge(byte code, int node)
        {
            if (this.TryGetEdge(code, out var _))
                return;

            if (this.Edges.Length + 1 < SortThreshold)
                this.Edges = [.. this.Edges, (code, node)];
            else
            {
                var copy = new (byte, int)[this.Edges.Length + 1];
                Array.Copy(this.Edges, copy, this.Edges.Length);
                copy[^1] = (code, node);
                this.Edges = copy;
                // this.Edges = [.. this.Edges.Append((code, node)).OrderBy(n => n.Item1)];
            }
        }

        public bool TryGetEdge(byte code, out int node)
        {
            node = 0;
            var found = -1;
            if (this.Edges.Length < SortThreshold)
            {
                for (var i = 0; i < this.Edges.Length; i++)
                {
                    if (this.Edges[i].Item1 == code)
                    {
                        found = i;
                        break;
                    }
                }
            }
            else
            {
                var (l, r) = (0, this.Edges.Length - 1);
                while (l <= r)
                {
                    var m = (l + r) / 2;
                    if (this.Edges[m].Item1 < code)
                        l = m + 1;
                    else if (this.Edges[m].Item1 > code)
                        r = m - 1;
                    else
                    {
                        found = m;
                        break;
                    }
                }
            }

            if (found < 0)
                return false;
            else
            {
                node = this.Edges[found].Item2;
                return true;
            }
        }

        public (byte, int) MinEdge()
        {
            if (this.Edges.Length == 0)
                throw new InvalidOperationException("No edges.");

            var found = this.Edges[0];
            if (this.Edges.Length < SortThreshold)
                for (var i = 1; i < this.Edges.Length; i++)
                    if (found.Item1 > this.Edges[i].Item1)
                        found = this.Edges[i];
            return found;
            // return this.Edges.MinBy(n => n.Item1);
        }

        public (byte, int) MaxEdge()
        {
            if (this.Edges.Length == 0)
                throw new InvalidOperationException("No edges.");

            var found = this.Edges[^1];
            if (this.Edges.Length < SortThreshold)
                for (var i = this.Edges.Length - 2; i >= 0; i--)
                    if (found.Item1 < this.Edges[i].Item1)
                        found = this.Edges[i];
            return found;
            // return this.Edges.MaxBy(n => n.Item1);
        }

        public IEnumerable<(byte, int)> SortedEdges(bool descending = false)
        {
            if (this.Edges.Length >= SortThreshold)
                return descending ? Enumerable.Reverse(this.Edges) : this.Edges;
            else
            {
                (byte, int)[] copy = [.. this.Edges];
                if (!descending)
                    Array.Sort(copy);
                else
                    Array.Sort(copy, (a, b) => b.Item1.CompareTo(a.Item1));
                return copy;
            }

            // if (this.Edges.Length < SortThreshold)
            //     return descending ? this.Edges.OrderByDescending(n => n.Item1) : this.Edges.OrderBy(n => n.Item1);
            // else
            //     return descending ? this.Edges.Reverse() : this.Edges;
        }

        public string Signature()
        {
            var builder = new StringBuilder(16);
            builder.Append(this.IsEnd ? '1' : '0');
            foreach (var (code, node) in this.SortedEdges())
                builder.Append($":{code:X02},{node:X08}");
            return builder.ToString();
        }
    }

    record FindFrame(Node Node, int Position, BitArray Bits, bool PostVisit);
    record Init(Node Root, Node[] Nodes, int MaxDepth, Dictionary<int, int> LLMap);
}
