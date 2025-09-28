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
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Collections;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// A concrete implementation of <see cref="KeyRecordDictionary"/> that uses a mutable Double-Array trie structure.
/// </summary>
/// <remarks>
/// This class provides very fast key lookups by representing a trie data structure in two compact arrays ('BASE' and 'CHECK').
/// <para>
/// <strong>Important Usage Notes:</strong>
/// <list type="bullet">
/// <item><description><strong>Mutable Structure:</strong> Unlike the DAWG or LOUDS implementations, this dictionary is mutable. Keys can be added and removed after the initial creation.</description></item>
/// <item><description><strong>Fragmentation:</strong> Repeated additions and removals can lead to fragmentation of the internal arrays, which may degrade performance and increase memory usage over time. To eliminate fragmentation and optimize the structure, it is recommended to periodically rebuild the dictionary using the static <see cref="Compact(DoubleArrayDictionary)"/> method.</description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class DoubleArrayDictionary : KeyRecordDictionary, KeyRecordDictionary.IKeyAccess, KeyRecordDictionary.IAltKeyAccess
{
    const uint KeyBit = 0x80000000u;
    const int ForwardingPointer = -2;
    const int ArrayAlignment = 524288;
    static readonly ArrayPool<int> intPool_ = ArrayPool<int>.Shared;
    static readonly ArrayPool<byte> bytePool_ = ArrayPool<byte>.Shared;
    readonly ValueBuffer<BC> bc_;
    int freeRoot_ = 1;
    int maxDepth_ = 0;

    // 
    // 

    DoubleArrayDictionary(SearchDirectionType searchDirection = SearchDirectionType.LTR)
    : base(searchDirection)
    {
        bc_ = new(ArrayAlignment);
        bc_.ExtendCapacity(1);
        ResetBCs();
    }

    DoubleArrayDictionary(ValueBuffer<BC> bc, SearchDirectionType searchDirection = SearchDirectionType.LTR)
    : base(searchDirection)
    {
        bc_ = bc;
    }

    /// <summary>
    /// Creates a new <see cref="DoubleArrayDictionary"/> from a collection of keys.
    /// </summary>
    /// <param name="keys">An enumerable collection of byte arrays to use as the initial keys. The collection does not need to be pre-sorted. Duplicate keys are permitted, but only the first occurrence will be added. The collection must not contain any elements that are null or empty byte array.</param>
    /// <param name="searchDirection">The key search direction for the new dictionary.</param>
    /// <returns>A new, mutable <see cref="DoubleArrayDictionary"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    public static DoubleArrayDictionary Create(IEnumerable<byte[]> keys, SearchDirectionType searchDirection = SearchDirectionType.LTR)
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

        DoubleArrayDictionary dictionary = searchDirection switch
        {
            SearchDirectionType.LTR => new LtrDictionary(),
            SearchDirectionType.RTL => new RtlDictionary(),
            _ => throw new ArgumentException($"Search direction is invalid."),
        };
        dictionary.BulkAppend(enumerator);
        return dictionary;
    }

    /// <summary>
    /// Serializes the state of the dictionary into a stream.
    /// </summary>
    /// <param name="dictionary">The dictionary instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="KeyRecordDictionary.SerializationOptions.Default"/> will be used.</param>
    /// <remarks>The serialization format is a custom binary format specific to this Double-Array trie implementation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(DoubleArrayDictionary dictionary, Stream stream, SerializationOptions? options = null)
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

        // key chunks
        var buffer1 = new byte[8 * ArrayAlignment];
        var chunkSizeMax = 0;
        foreach (var array in dictionary.bc_.EnumerateChunks())
        {
            for (var i = 0; i < ArrayAlignment; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(buffer1.AsSpan(i * 8, 8), array[i].ToUInt64());

            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                compressStream.Write(buffer1);
            }
            var buffer2 = new byte[4 + memoryStream.Length];
            BinaryPrimitives.WriteInt32LittleEndian(buffer2, (int)memoryStream.Length);
            memoryStream.ToArray().CopyTo(buffer2.AsSpan(4));
            stream.Write(buffer2);
            xxh.Append(buffer2);
            if (chunkSizeMax < buffer2.Length)
                chunkSizeMax = buffer2.Length;
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
        //  0: byte * 4, DAA1
        "DAA1"u8.CopyTo(buffer0);// [0x44, 0x41, 0x41, 0x31]
        //  4: uint * 1, xxhash
        xxhc.CopyTo(buffer0.AsSpan(4));
        //  8: int * 1, array alignment
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(8), ArrayAlignment);
        // 12: int * 1, array count
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(12), dictionary.bc_.NumberOfChunks);
        // 16: int * 1, array used
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(16), dictionary.bc_.Used);
        // 20: int * 1, freeRoot_;
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(20), dictionary.freeRoot_);
        // 24: int * 1, direction
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(24), dictionary.SearchDirection == SearchDirectionType.LTR ? 0 : 1);
        // 28: int * 1, additional1 size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(28), dictionary.Additional1.Length);
        // 32: int * 1, additional2 size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(32), dictionary.Additional2.Length);
        // 36: int * 1, record size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(36), bytesRecords.Length);
        // 40: int * 1, max chunk size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(40), chunkSizeMax);
        // 44: int * 1, max depth
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(44), dictionary.maxDepth_);
        // 48- empty
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
    /// <returns>A new instance of <see cref="DoubleArrayDictionary"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream data is corrupted or in an unsupported format.</exception>
    public static DoubleArrayDictionary Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[4160];
        stream.ReadExactly(buffer0);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("DAA1"u8))// [0x44, 0x41, 0x41, 0x31]
            throw new InvalidDataException("Unsupported format.");

        var xxh = new XxHash32();
        var xxhc = BinaryPrimitives.ReadUInt32LittleEndian(buffer0.AsSpan(4));

        var aa = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(8));
        if (aa != ArrayAlignment)
            throw new InvalidDataException("Unsupported format.");

        var ab = new ValueBuffer<BC>(aa);
        var ac = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(12));
        var chunkSizeMax = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(40));

        // key chunks
        var buffer1 = new byte[chunkSizeMax];
        var buffer2 = new byte[8 * aa];
        for (var i = 0; i < ac; i++)
        {
            stream.ReadExactly(buffer1.AsSpan(0, 4));
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            stream.ReadExactly(buffer1.AsSpan(4, chunkSize));
            xxh.Append(buffer1.AsSpan(0, 4 + chunkSize));

            using var memoryStream = new MemoryStream(buffer1, 4, chunkSize);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            decompressStream.ReadExactly(buffer2);
            var chunk = new BC[aa];
            for (var j = 0; j < chunk.Length; j++)
                chunk[j] = BC.FromUInt64(BinaryPrimitives.ReadUInt64LittleEndian(buffer2.AsSpan(j * 8, 8)));
            ab.AppendChunkDirectly(chunk);
        }
        ab.SetUsedDirectly(BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(16)));

        var additional1 = buffer0.AsSpan(64, BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(28)));
        var freeRoot = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(20));
        var maxDepth = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(44));
        DoubleArrayDictionary dictionary = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(24)) switch
        {
            0 => new LtrDictionary(ab) { freeRoot_ = freeRoot, maxDepth_ = maxDepth, Additional1 = [.. additional1] },
            1 => new RtlDictionary(ab) { freeRoot_ = freeRoot, maxDepth_ = maxDepth, Additional1 = [.. additional1] },
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

    void BulkAppend(IEnumerable<byte[]> keys)
    {
        var array = keys.ToArray();
        if (array.Length != 0)
            maxDepth_ = array.Max(n => n.Length);

        var siblings = __Fetch(array, new() { Right = array.Length });
        __AppendRecursive(array, siblings);

        #region @@
        void __AppendRecursive(IReadOnlyList<byte[]> keys, BulkNode[] siblings)
        {
            if (siblings.Length == 0)
                return;

            var codes = new List<int>(siblings.Select(n => n.Code));
            codes.Sort();
            var begin = FindBegin([.. codes]);
            var parentPosition = 0;
            if (siblings[0].Depth > 1)
            {
                var key = keys[siblings[0].Left];
                parentPosition = __Traverse(key.AsSpan(0, siblings[0].Depth - 1));
            }
            bc_[parentPosition].B = begin;

            foreach (var tmpNode in siblings)
            {
                var nextPosition = begin + tmpNode.Code;
                RemoveFromFreeList(nextPosition);
                bc_[nextPosition] = new(0, parentPosition);
            }

            foreach (var tmpNode in siblings)
            {
                var currentPosition = begin + tmpNode.Code;
                if (tmpNode.Code == 0)
                    bc_[currentPosition].B = -1;
                else
                    __AppendRecursive(keys, __Fetch(keys, tmpNode));
            }
        }
        int __Traverse(ReadOnlySpan<byte> key)
        {
            var currentPosition = 0;
            foreach (var k in key)
            {
                var code = k + 1;
                if (bc_[currentPosition].B <= 0)
                    return currentPosition;
                var nextPosition = bc_[currentPosition].B + code;
                if (nextPosition >= bc_.Used || bc_[nextPosition].C != currentPosition)
                    return currentPosition;
                currentPosition = nextPosition;
            }
            return currentPosition;
        }
        static BulkNode[] __Fetch(IReadOnlyList<byte[]> keys, BulkNode tmpParent)
        {
            var siblings = new List<BulkNode>();
            var tmpCode = -1;
            for (var i = tmpParent.Left; i < tmpParent.Right; i++)
            {
                var key = keys[i];
                if (key.Length < tmpParent.Depth)
                    continue;

                var currentCode = key.Length == tmpParent.Depth ? 0 : key[tmpParent.Depth] + 1;
                if (currentCode > tmpCode)
                {
                    if (tmpCode != -1)
                        siblings.Last().Right = i;
                    siblings.Add(new() { Code = currentCode, Depth = tmpParent.Depth + 1, Left = i });
                }
                tmpCode = currentCode;
            }
            if (siblings.Count > 0)
                siblings.Last().Right = tmpParent.Right;
            return [.. siblings];
        }
        #endregion
    }

    /// <summary>
    /// Rebuilds the dictionary to eliminate fragmentation and optimize its internal structure.
    /// </summary>
    /// <param name="original">The original, potentially fragmented dictionary to compact.</param>
    /// <returns>A new, compacted <see cref="DoubleArrayDictionary"/> instance containing the same keys and records.</returns>
    /// <remarks>
    /// This method should be used when performance or memory usage degrades after a large number of additions and removals.
    /// It works by creating a new dictionary from the keys of the original, effectively rebuilding the internal arrays.
    /// <para>
    /// <strong>Important:</strong> The integer identifiers assigned to keys in the new, compacted dictionary will be different
    /// from those in the original. Any external references to the old identifiers will be invalid after compaction.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="original"/> is null.</exception>
    public static DoubleArrayDictionary Compact(DoubleArrayDictionary original)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(original);
#else
        if (original == null)
            throw new ArgumentNullException(nameof(original));
#endif

        var copy = Create(original.EnumerateAll().Select(n => n.Item2), original.SearchDirection);
        copy.Additional1 = [.. original.Additional1];
        copy.Additional2 = [.. original.Additional2];
        foreach (var (identifier1, key) in original.EnumerateAll())
        {
            var identifier2 = copy.SearchExactly(key);
            {
                var access1 = original.GetRecordAccess(identifier1);
                var access2 = copy.GetRecordAccess(identifier2);
                foreach (var r in access1)
                    access2.Add(r.Content).ExByte = r.ExByte;
            }
            {
                var access1 = original.GetRecordAccess(identifier1, true);
                var access2 = copy.GetRecordAccess(identifier2, true);
                foreach (var r in access1)
                    access2.Add(r.Content);
            }
        }
        return copy;
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
    /// <exception cref="ArgumentException">The specified <paramref name="identifier"/> is invalid or does not exist.</exception>
    public IRecordAccess GetRecordAccess(int identifier, bool isTransient = false)
    {
        identifier = ResolveIdentifier(identifier);

        if (identifier <= 0 || identifier >= bc_.Used || bc_[identifier].C == -1 || bc_[identifier].B >= 0)
            throw new ArgumentException($"Invalid {nameof(identifier)}.");

        if (isTransient)
            return new BasicRecordAccess(transientRecords_.GetRecordAccess(identifier));
        else
        {
            var b = bc_[identifier].B;
            if (b != -1)
                return new PrimitiveRecordAccess(persistentRecords_.GetRecordAccess(b & 0x7FFFFFFF));
            else
            {
                var access = persistentRecords_.GetRecordAccess();
                bc_[identifier].B = (int)((uint)access.Identifier | KeyBit);
                return new PrimitiveRecordAccess(access);
            }
        }
    }

    /// <summary>
    /// Removes all keys and records from the dictionary and resets its internal state.
    /// </summary>
    public void Clear()
    {
        bc_.Clear();
        bc_.ExtendCapacity(1);
        ResetBCs();
        freeRoot_ = 1;
        base.InternalClear();
    }

    /// <summary>
    /// Adds a key to the dictionary. If the key already exists, its existing identifier is returned.
    /// </summary>
    /// <param name="key">The key to add.</param>
    /// <returns>The identifier for the added or existing key.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    public virtual int Add(ReadOnlySpan<byte> key)
    => InternalTryAdd(key, out var identifier) ? identifier : identifier;

    /// <exclude />
    int IAltKeyAccess.Add(ReadOnlySpan<byte> key)
    => InternalTryAdd(key, out var identifier) ? identifier : identifier;

    /// <summary>
    /// Tries to add a key to the dictionary.
    /// </summary>
    /// <param name="key">The key to add.</param>
    /// <param name="identifier">When this method returns, contains the identifier for the new key. If the key already existed, this will be the existing identifier.</param>
    /// <returns>true if the key was newly added; false if the key already existed.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    public virtual bool TryAdd(ReadOnlySpan<byte> key, out int identifier)
    => InternalTryAdd(key, out identifier);

    /// <exclude />
    bool IAltKeyAccess.TryAdd(ReadOnlySpan<byte> key, out int identifier)
    => InternalTryAdd(key, out identifier);

    bool InternalTryAdd(ReadOnlySpan<byte> key, out int identifier)
    {
        if (key.Length == 0)
            throw new ArgumentException($"{nameof(key)} is empty.");

        if (maxDepth_ < key.Length)
            maxDepth_ = key.Length;

        var currentPosition = 0;
        foreach (var k in key)
        {
            var code = k + 1;
            if (bc_[currentPosition].B <= 0)
                bc_[currentPosition].B = FindBegin([code]);
            var nextPosition = bc_[currentPosition].B + code;
            if (nextPosition >= bc_.Used || (bc_[nextPosition].C != -1 && bc_[nextPosition].C != currentPosition))
            {
                Relocate(currentPosition, code);
                nextPosition = bc_[currentPosition].B + code;
            }
            if (bc_[nextPosition].C == -1)
            {
                RemoveFromFreeList(nextPosition);
                bc_[nextPosition] = new(0, currentPosition);
            }
            currentPosition = nextPosition;
        }

        if (bc_[currentPosition].B <= 0)
            bc_[currentPosition].B = FindBegin([0]);
        else
        {
            var tmpPosition = bc_[currentPosition].B;
            if (tmpPosition >= bc_.Used || (bc_[tmpPosition].C != -1 && bc_[tmpPosition].C != currentPosition))
                Relocate(currentPosition, 0);
        }

        var isNewKey = false;
        var finalPosition = bc_[currentPosition].B;
        if (bc_[finalPosition].C == -1)
        {
            RemoveFromFreeList(finalPosition);
            bc_[finalPosition].B = -1;
            bc_[finalPosition].C = currentPosition;
            isNewKey = true;
        }
        identifier = finalPosition;
        return isNewKey;
    }

    /// <summary>
    /// Determines whether the specified key exists in the dictionary.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>true if the key is found; otherwise, false.</returns>
    public virtual bool Contains(ReadOnlySpan<byte> key)
    => InternalSearchExactly(key) > 0;

    /// <exclude />
    bool IAltKeyAccess.Contains(ReadOnlySpan<byte> key)
    => InternalSearchExactly(key) > 0;

    /// <summary>
    /// Removes a key and its associated records from the dictionary using its identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key to remove.</param>
    /// <returns>true if the key was found and removed; otherwise, false.</returns>
    public virtual bool Remove(int identifier)
    {
        identifier = ResolveIdentifier(identifier);
        var key = InternalGetKey(identifier);
        return key.Length != 0 && InternalRemove(key);
    }

    /// <summary>
    /// Removes a key and its associated records from the dictionary.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>true if the key was found and removed; otherwise, false.</returns>
    public virtual bool Remove(ReadOnlySpan<byte> key)
    => InternalRemove(key);

    /// <exclude />
    bool IAltKeyAccess.Remove(ReadOnlySpan<byte> key)
    => InternalRemove(key);

    bool InternalRemove(ReadOnlySpan<byte> key)
    {
        if (key.Length == 0)
            return false;

        var traced = __Trace(bc_, key);
        if (traced.Length == 0)
            return false;

        var finalPosition = traced[^1];
        if (bc_[finalPosition].B != -1)
            persistentRecords_.GetRecordAccess(bc_[finalPosition].B & 0x7FFFFFFF).Clear();
        transientRecords_.Remove(finalPosition);

        AddToFreeList(finalPosition);
        for (var i = traced.Length - 2; i >= 0; i--)
        {
            var currentPosition = traced[i];
            if (__CountChildren(currentPosition) == 0)
                AddToFreeList(currentPosition);
            else
                break;
        }
        return true;

        #region @@
        int __CountChildren(int index)
        {
            if (bc_[index].B <= 0)
                return 0;

            var (count, offset) = (0, bc_[index].B);
            for (var code = 0; code <= 256; code++)
            {
                var childPosition = offset + code;
                if (childPosition < bc_.Used && bc_[childPosition].C == index)
                    count++;
            }
            return count;
        }
        static int[] __Trace(ValueBuffer<BC> bc, ReadOnlySpan<byte> key)
        {
            var result = new List<int>();

            var currentPosition = 0;
            result.Add(currentPosition);
            foreach (var k in key)
            {
                var code = k + 1;
                var nextPosition = bc[currentPosition].B + code;
                if (nextPosition >= bc.Used || bc[nextPosition].C != currentPosition)
                    return [];

                result.Add(currentPosition = nextPosition);
            }
            var finalPosition = bc[currentPosition].B;
            if (finalPosition >= bc.Used || bc[finalPosition].C != currentPosition || bc[finalPosition].B >= 0)
                return [];
            result.Add(finalPosition);
            return [.. result];
        }
        #endregion
    }

    /// <summary>
    /// Tries to get the key associated with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key to get.</param>
    /// <param name="key">When this method returns, contains the key if found; otherwise, an empty array.</param>
    /// <returns>true if the key was found; otherwise, false.</returns>
    public virtual bool TryGetKey(int identifier, out byte[] key)
    => _TryGetKey(identifier, out key);

    /// <exclude />
    bool IAltKeyAccess.TryGetKey(int identifier, out byte[] key)
    => _TryGetKey(identifier, out key);

    bool _TryGetKey(int identifier, out byte[] key)
    {
        key = [];
        identifier = ResolveIdentifier(identifier);
        if (identifier <= 0 || identifier >= bc_.Used || bc_[identifier].C == -1 || bc_[identifier].B >= 0)
            return false;

        key = InternalGetKeyUnsafe(identifier);
        return true;
    }

    /// <summary>
    /// Gets the key associated with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key to get.</param>
    /// <returns>The key as a byte array.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The specified identifier is out of the valid range.</exception>
    /// <exception cref="ArgumentException">The specified identifier is invalid.</exception>
    public virtual byte[] GetKey(int identifier)
    => InternalGetKey(identifier);

    /// <exclude />
    byte[] IAltKeyAccess.GetKey(int identifier)
    => InternalGetKey(identifier);

    byte[] InternalGetKey(int identifier)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(identifier);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(identifier, bc_.Used);
#else
        if (identifier <= 0)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must be greater than 0.");
        if (identifier >= bc_.Used)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must be less than {bc_.Used}.");
#endif

        var original = identifier;
        identifier = ResolveIdentifier(identifier);
        if (bc_[identifier].C == -1)
            throw new ArgumentException($"Invalid {nameof(original)}.");

        return InternalGetKeyUnsafe(identifier);
    }

    byte[] InternalGetKeyUnsafe(int identifier)
    {
        var currentPosition = identifier;
        var key = new Stack<byte>(maxDepth_);
        while (currentPosition > 0)
        {
            var parentPosition = bc_[currentPosition].C;
            var code = currentPosition - bc_[parentPosition].B;
            if (code != 0)
                key.Push((byte)(code - 1));
            currentPosition = parentPosition;
        }
        return [.. key];
    }

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

        var currentPosition = 0;
        foreach (var k in sequence)
        {
            var code = k + 1;
            var nextPosition = bc_[currentPosition].B + code;
            if (nextPosition >= bc_.Used || bc_[nextPosition].C != currentPosition)
                return -1;
            currentPosition = nextPosition;
        }
        var finalPosition = bc_[currentPosition].B;
        return finalPosition < bc_.Used && bc_[finalPosition].C == currentPosition && bc_[finalPosition].B < 0 ? finalPosition : -1;
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

        var currentPosition = 0;
        for (var i = 0; i < sequence.Length; i++)
        {
            var code = sequence[i] + 1;
            var nextPosition = bc_[currentPosition].B + code;
            if (nextPosition >= bc_.Used || bc_[nextPosition].C != currentPosition)
                yield break;

            currentPosition = nextPosition;
            var finalPosition = bc_[currentPosition].B;
            if (finalPosition < bc_.Used && bc_[finalPosition].C == currentPosition && bc_[finalPosition].B < 0)
                yield return (finalPosition, sequence[..(i + 1)]);
        }
    }

    /// <summary>
    /// Finds the longest key in the dictionary that is a prefix of the given sequence.
    /// </summary>
    /// <param name="sequence">The sequence to search within.</param>
    /// <returns>A tuple containing the identifier and key of the longest matching prefix, or (-1, []) if no match is found.</returns>
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
        var lastPosition = -1;
        var currentPosition = 0;
        for (var i = 0; i < sequence.Length; i++)
        {
            var code = sequence[i] + 1;
            var nextPosition = bc_[currentPosition].B + code;
            if (nextPosition >= bc_.Used || bc_[nextPosition].C != currentPosition)
                break;

            currentPosition = nextPosition;
            var finalPosition = bc_[currentPosition].B;
            if (finalPosition < bc_.Used && bc_[finalPosition].C == currentPosition && bc_[finalPosition].B < 0)
                (lastPosition, length) = (finalPosition, i + 1);
        }

        return (lastPosition, lastPosition != -1 ? sequence[.. length].ToArray() : []);
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
    // => InternalSearchWildcard([.. sequence, 0], [.. Enumerable.Repeat('.', sequence.Length), '*'], reverse);

    IEnumerable<(int, byte[])> InternalSearchByPrefixNative(byte[] sequence, bool reverse)
    {
        if (sequence.Length == 0)
            yield break;

        var startPosition = 0;
        for (var i = 0; i < sequence.Length; i++)
        {
            var code = sequence[i] + 1;
            var nextPosition = bc_[startPosition].B + code;
            if (nextPosition >= bc_.Used || bc_[nextPosition].C != startPosition)
                yield break;
            startPosition = nextPosition;
        }

        var rent = intPool_.Rent(257);
        try
        {
            var stack = new Stack<int>(4096);
            stack.Push(startPosition);
            while (stack.TryPop(out var currentPosition))
            {
                var finalPosition = bc_[currentPosition].B;
                if (finalPosition < bc_.Used && bc_[finalPosition].C == currentPosition && bc_[finalPosition].B < 0)
                    yield return (finalPosition, InternalGetKeyUnsafe(finalPosition));

                var children = GetChildren(rent.AsSpan(0, 257), bc_, currentPosition, reverse);
                for (var i = children.Length - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (child == 0)
                        continue;

                    var nextPosition = bc_[currentPosition].B + child;
                    stack.Push(nextPosition);
                }
            }
#if false
            var stack = new Stack<(int, byte[])>(4096);
            stack.Push((startPosition, sequence.ToArray()));
            while (stack.TryPop(out var item))
            {
                var (currentPosition, currentKey) = item;
                var finalPosition = bc_[currentPosition].B;
                if (finalPosition < bc_.Used && bc_[finalPosition].C == currentPosition && bc_[finalPosition].B < 0)
                    yield return (finalPosition, currentKey);

                var children = GetChildren(rent.AsSpan(0, 257), bc_, currentPosition, reverse);
                for (var i = children.Length - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (child == 0)
                        continue;

                    var nextPosition = bc_[currentPosition].B + child;
                    stack.Push((nextPosition, [.. currentKey, (byte)(child - 1)]));
                }
            }
#endif
        }
        finally
        {
            intPool_.Return(rent);
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
        frames.Push(new(0, bits0, false));

        var rent = intPool_.Rent(257);
        try
        {
            if (!reverse)
            {
                while (frames.TryPop(out var frame))
                {
                    if (__Accepted(frame.Bits))
                    {
                        var finalPosition = bc_[frame.Position].B;
                        if (finalPosition > 0 && finalPosition < bc_.Used && bc_[finalPosition].C == frame.Position && bc_[finalPosition].B < 0)
                            yield return (finalPosition, InternalGetKeyUnsafe(finalPosition));
                    }

                    if (bc_[frame.Position].B <= 0)
                        continue;

                    var children = GetChildren(rent.AsSpan(0, 257), bc_, frame.Position, true);
                    foreach (var code in children)
                    {
                        if (code == 0)
                            continue;

                        var nextPosition = bc_[frame.Position].B + code;
                        var bits1 = __Step(frame.Bits, (byte)(code - 1));
                        if (bits1.HasAnySet())
                            frames.Push(new(nextPosition, bits1, false));
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
                            var finalPosition = bc_[frame.Position].B;
                            if (finalPosition > 0 && finalPosition < bc_.Used && bc_[finalPosition].C == frame.Position && bc_[finalPosition].B < 0)
                                yield return (finalPosition, InternalGetKeyUnsafe(finalPosition));
                        }
                        continue;
                    }

                    frames.Push(frame with { PostVisit = true });

                    if (bc_[frame.Position].B <= 0)
                        continue;

                    var children = GetChildren(rent.AsSpan(0, 257), bc_, frame.Position);
                    foreach (var code in children)
                    {
                        if (code == 0)
                            continue;

                        var nextPosition = bc_[frame.Position].B + code;
                        var bits1 = __Step(frame.Bits, (byte)(code - 1));
                        if (bits1.HasAnySet())
                            frames.Push(new(nextPosition, bits1, false));
                    }
                }
            }
        }
        finally
        {
            intPool_.Return(rent);
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
        var rent = bytePool_.Rent(maxDepth_);
        try
        {
            if (TraverseNext(0, true, rent.AsSpan(0, maxDepth_), out identifier, out var keyLength))
            {
                key = rent[..keyLength];
                return true;
            }

            (identifier, key) = (-1, []);
            return false;
        }
        finally
        {
            bytePool_.Return(rent);
        }

        // (identifier, key) = TraverseNext(0, true);
        // return identifier != -1;
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
        var rent = bytePool_.Rent(maxDepth_);
        try
        {
            if (TraverseNext(0, false, rent.AsSpan(0, maxDepth_), out identifier, out var keyLength))
            {
                key = rent[..keyLength];
                return true;
            }

            (identifier, key) = (-1, []);
            return false;
        }
        finally
        {
            bytePool_.Return(rent);
        }

        // (identifier, key) = TraverseNext(0, false);
        // return identifier != -1;
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
    => InternalFindNext(InternalGetKey(currentIdentifier), out foundIdentifier, out foundKey);

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

        var currentPath = new Stack<int>(currentKey.Length);
        var currentPosition = 0;
        currentPath.Push(currentPosition);
        foreach (var k in currentKey)
        {
            var code = k + 1;
            var nextPosition = bc_[currentPosition].B + code;
            if (nextPosition >= bc_.Used || bc_[nextPosition].C != currentPosition)
                throw new InvalidOperationException($"Not found {nameof(currentKey)}.");
            currentPath.Push(currentPosition = nextPosition);
        }

        var rent1 = intPool_.Rent(257);
        var rent2 = bytePool_.Rent(maxDepth_);
        try
        {
            var buffer1 = rent1.AsSpan(0, 257);
            var buffer2 = rent2.AsSpan(0, maxDepth_);
            var children = GetChildren(buffer1, bc_, currentPosition);
            foreach (var code in children)
            {
                if (code == 0)
                    continue;

                var childPosition = bc_[currentPosition].B + code;
                if (TraverseNext(childPosition, true, buffer2, out var suffixIdentifier, out var suffixLength))
                {
                    foundIdentifier = suffixIdentifier;
                    foundKey = [.. currentKey, (byte)(code - 1), .. buffer2[..suffixLength]];
                    return true;
                }
            }

            while (currentPath.Count >= 2)
            {
                currentPosition = currentPath.Pop();
                var parentPosition = currentPath.Peek();
                var code1 = currentPosition - bc_[parentPosition].B;
                var code2 = __FindFirstChildCode(buffer1, bc_, parentPosition, code1);
                if (code2 != -1)
                {
                    var siblingPosition = bc_[parentPosition].B + code2;
                    if (TraverseNext(siblingPosition, true, buffer2, out var suffixIdentifier, out var suffixLength))
                    {
                        foundIdentifier = suffixIdentifier;
                        foundKey = [.. currentKey[..(currentPath.Count - 1)], (byte)(code2 - 1), .. buffer2[..suffixLength]];
                        return true;
                    }
                }
            }
        }
        finally
        {
            intPool_.Return(rent1);
            bytePool_.Return(rent2);
        }

        return false;

        #region @@
        static int __FindFirstChildCode(Span<int> buffer, ValueBuffer<BC> bc, int index, int code)
        {
            var children = GetChildren(buffer, bc, index);
            for (var i = 0; i < children.Length; i++)
                if (children[i] > code)
                    return children[i];
            return -1;
        }
        #endregion
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
    => InternalFindPrevious(InternalGetKey(currentIdentifier), out foundIdentifier, out foundKey);

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

        var currentPath = new Stack<int>(currentKey.Length);
        var currentPosition = 0;
        currentPath.Push(currentPosition);
        foreach (var k in currentKey)
        {
            var code = k + 1;
            var nextPosition = bc_[currentPosition].B + code;
            if (nextPosition >= bc_.Used || bc_[nextPosition].C != currentPosition)
                throw new InvalidOperationException($"Not found {nameof(currentKey)}.");
            currentPath.Push(currentPosition = nextPosition);
        }

        var rent1 = intPool_.Rent(257);
        var rent2 = bytePool_.Rent(maxDepth_);
        try
        {
            var buffer1 = rent1.AsSpan(0, 257);
            var buffer2 = rent2.AsSpan(0, maxDepth_);
            while (currentPath.Count >= 2)
            {
                currentPosition = currentPath.Pop();
                var parentPosition = currentPath.Peek();
                var code1 = currentPosition - bc_[parentPosition].B;
                var code2 = __FindLastChildCode(buffer1, bc_, parentPosition, code1);
                if (code2 != -1)
                {
                    var siblingPosition = bc_[parentPosition].B + code2;
                    if (TraverseNext(siblingPosition, false, buffer2, out var suffixIdentifier, out var suffixLength))
                    {
                        foundIdentifier = suffixIdentifier;
                        foundKey = code2 != 0 ? [.. currentKey[..(currentPath.Count - 1)], (byte)(code2 - 1), .. buffer2[..suffixLength]] : [.. currentKey[..(currentPath.Count - 1)], .. buffer2[..suffixLength]];
                        return true;
                    }
                }

                var finalPosition = bc_[parentPosition].B;
                if (finalPosition < bc_.Used && bc_[finalPosition].C == parentPosition && bc_[finalPosition].B < 0)
                {
                    (foundIdentifier, foundKey) = (finalPosition, InternalGetKeyUnsafe(finalPosition));
                    return true;
                }
            }
        }
        finally
        {
            intPool_.Return(rent1);
            bytePool_.Return(rent2);
        }

        return false;

        #region @@
        static int __FindLastChildCode(Span<int> buffer, ValueBuffer<BC> bc, int index, int code)
        {
            var children = GetChildren(buffer, bc, index, true);
            for (var i = 0; i < children.Length; i++)
                if (children[i] < code)
                    return children[i];
            return -1;
        }
        #endregion
    }

    bool TraverseNext(int startPosition, bool isPreOrder, Span<byte> keyBuffer, out int returnPosition, out int keyLength)
    {
        (returnPosition, keyLength) = (-1, 0);

        var rent = intPool_.Rent(257);
        try
        {
            var buffer = rent.AsSpan(0, 257);

            var currentPosition = startPosition;
            while (true)
            {
                var finalPosition = bc_[currentPosition].B;
                if (finalPosition > 0 && finalPosition < bc_.Used && bc_[finalPosition].C == currentPosition && bc_[finalPosition].B < 0)
                {
                    returnPosition = finalPosition;
                    if (isPreOrder || !HasChildren(currentPosition))
                        return returnPosition != -1;
                }

                var children = GetChildren(buffer, bc_, currentPosition);
                if (children.Length == 0)
                    break;

                var code = isPreOrder ? children[0] : children[^1];
                if (code == 0)
                    break;

                keyBuffer[keyLength++] = (byte)(code - 1);
                currentPosition = bc_[currentPosition].B + code;
            }

            if (!isPreOrder && returnPosition != -1)
                return true;
        }
        finally
        {
            intPool_.Return(rent);
        }

        return false;
    }

    void ResetBCs()
    {
        for (var i = 1; i < bc_.Capacity; i++)
            bc_[i] = new(i + 1, -1);
        bc_[bc_.Capacity - 1] = new(-1, -1);
    }

    void Resize(int newSize)
    {
        var tmpSize1 = bc_.Capacity;
        var tmpSize2 = bc_.ExtendCapacity(newSize);
        for (var i = tmpSize2 - 1; i >= tmpSize1; i--)
            AddToFreeList(i);
    }

    void RemoveFromFreeList(int index)
    {
        if (index == freeRoot_)
            freeRoot_ = bc_[index].B;
        else
        {
            var currentPosition = freeRoot_;
            while (currentPosition != -1)
            {
                var nextPosition = bc_[currentPosition].B;
                if (nextPosition == index)
                {
                    bc_[currentPosition].B = bc_[nextPosition].B;
                    break;
                }

                if (nextPosition != currentPosition)
                    currentPosition = nextPosition;
                else
                    break;
            }
        }
    }

    int FindBegin(ReadOnlySpan<int> sorted)
    {
        if (sorted.Length == 0)
            return 1;

        var free = freeRoot_;
        while (free is not -1 and not 0)
        {
            var begin = free - sorted[0];
            if (begin <= 0)
            {
                free = bc_[free].B;
                continue;
            }

            var maxPosition = begin + sorted[^1];
            if (maxPosition >= bc_.Capacity)
            {
                Resize(1 + maxPosition);
                free = freeRoot_;
                continue;
            }

            var placeable = true;
            for (var i = 1; i < sorted.Length; i++)
            {
                var codePosition = begin + sorted[i];
                if (bc_[codePosition].C != -1)
                {
                    placeable = false;
                    break;
                }
            }
            if (placeable)
                return begin;

            free = bc_[free].B;
        }

        for (var begin = bc_.Used; ; begin++)
        {
            var maxPosition = begin + sorted[^1];
            if (maxPosition >= bc_.Capacity)
                Resize(1 + maxPosition);

            var placeable = true;
            foreach (var code in sorted)
            {
                var codePosition = begin + code;
                if (bc_[codePosition].C != -1)
                {
                    placeable = false;
                    break;
                }
            }
            if (placeable)
                return begin;
        }
    }

    void Relocate(int index, int code)
    {
        var rent = intPool_.Rent(514);
        try
        {
            var buffer1 = rent.AsSpan(0, 257);
            var buffer2 = rent.AsSpan(257, 257);

            var children = __SeedChildren(buffer1, bc_, index, code);

            var begin1 = bc_[index].B;
            var begin2 = FindBegin(children);
            bc_[index].B = begin2;
            foreach (var child in children)
            {
                if (child == code)
                    continue;

                var (oldPosition, newPosition) = (begin1 + child, begin2 + child);
                RemoveFromFreeList(newPosition);
                bc_[newPosition] = new(bc_[oldPosition].B, index);
                if (bc_[oldPosition].B > 0)
                {
                    foreach (var grand in GetChildren(buffer2, bc_, oldPosition))
                    {
                        var grandPosition = bc_[oldPosition].B + grand;
                        if (grandPosition < bc_.Used && bc_[grandPosition].C == oldPosition)
                            bc_[grandPosition].C = newPosition;
                    }
                }
                if (bc_[oldPosition].B < 0)
                    bc_[oldPosition] = new(newPosition, ForwardingPointer);
                else
                    AddToFreeList(oldPosition);
            }
        }
        finally
        {
            intPool_.Return(rent);
        }

        #region @@
        static Span<int> __SeedChildren(Span<int> buffer, ValueBuffer<BC> bc, int index, int code)
        {
            var children = GetChildren(buffer, bc, index);
            buffer[children.Length] = code;
            return __Sort(buffer[..(children.Length + 1)]);
        }
        static Span<int> __Sort(Span<int> span)
        {
#if NET5_0_OR_GREATER
            span.Sort();
            return span;
#else
            var array = span.ToArray();
            Array.Sort(array);
            array.CopyTo(span);
            return span;
#endif
        }
        #endregion
    }

    int ResolveIdentifier(int identifier)
    {
        while (identifier > 0 && identifier < bc_.Used && bc_[identifier].C == ForwardingPointer)
            identifier = bc_[identifier].B;
        return identifier;
    }

    void AddToFreeList(int index)
    {
        bc_[index] = new(freeRoot_, -1);
        freeRoot_ = index;
    }

    bool HasChildren(int index)
    {
        if (bc_[index].B <= 0)
            return false;

        var offset = bc_[index].B;
        for (var code = 0; code <= 256; code++)
        {
            var childPosition = offset + code;
            if (childPosition < bc_.Used && bc_[childPosition].C == index)
                return true;
        }
        return false;
    }

    static Span<int> GetChildren(Span<int> buffer, ValueBuffer<BC> bc, int index, bool reverse = false)
    {
        if (bc[index].B <= 0)
            return buffer[..0];

        var offset = bc[index].B;
        var count = 0;
        if (!reverse)
        {
            for (var code = 0; code <= 256; code++)
            {
                var childPosition = offset + code;
                if (childPosition < bc.Used && bc[childPosition].C == index)
                    buffer[count++] = code;
            }
        }
        else
        {
            for (var code = 256; code >= 0; code--)
            {
                var childPosition = offset + code;
                if (childPosition < bc.Used && bc[childPosition].C == index)
                    buffer[count++] = code;
            }
        }
        return buffer[..count];
    }

    // 
    // 

    class BulkNode
    {
        public int Code { get; set; }
        public int Depth { get; set; }
        public int Left { get; set; }
        public int Right { get; set; }
    }

    struct BC(int b = 0, int c = 0)
    {
        public int B { get; set; } = b;
        public int C { get; set; } = c;

        public readonly void Deconstruct(out int b, out int c)
        => (b, c) = (this.B, this.C);
        public readonly ulong ToUInt64() => (uint)this.B | ((ulong)(uint)this.C << 32);
        public static BC FromUInt64(ulong value) => new((int)(uint)(value & 0x00000000FFFFFFFFul), (int)(uint)((value & 0xFFFFFFFF00000000ul) >> 32));
    }

    class LtrDictionary : DoubleArrayDictionary
    {
        public LtrDictionary()
        : base(SearchDirectionType.LTR)
        {
        }

        public LtrDictionary(ValueBuffer<BC> bc)
        : base(bc, SearchDirectionType.LTR)
        {
        }
    }

    class RtlDictionary : DoubleArrayDictionary
    {
        public RtlDictionary()
        : base(SearchDirectionType.RTL)
        {
        }

        public RtlDictionary(ValueBuffer<BC> bc)
        : base(bc, SearchDirectionType.RTL)
        {
        }

        public override int Add(ReadOnlySpan<byte> key)
        => Rtl.ImplAdd(this, key);

        public override bool TryAdd(ReadOnlySpan<byte> key, out int identifier)
        => Rtl.ImplTryAdd(this, key, out identifier);

        public override bool Remove(ReadOnlySpan<byte> key)
        => Rtl.ImplRemove(this, key);

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

    record FindFrame(int Position, BitArray Bits, bool PostVisit);
}
