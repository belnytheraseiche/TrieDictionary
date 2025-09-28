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

using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// A concrete implementation of <see cref="BasicRecordStore"/> that uses a hash map as its underlying data structure.
/// </summary>
/// <remarks>
/// This class uses a <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> to store records.
/// It provides fast, average-case O(1) time complexity for record lookups, additions, and removals.
/// Unlike the tree-based stores, this implementation does not store or enumerate records in a sorted order.
/// <para>
/// <strong>Serialization Limits:</strong> The binary serialization format for this class imposes certain constraints on the data.
/// Exceeding these limits will result in an <see cref="InvalidDataException"/> during serialization.
/// <list type="bullet">
/// <item><description><strong>Max Records per List:</strong> A single identifier can have a maximum of 16,777,215 records in its linked list.</description></item>
/// <item><description><strong>Max Record Content Size:</strong> The <c>Content</c> byte array of any single record cannot exceed 65,535 bytes.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class HashMapRecordStore : BasicRecordStore, ICloneable
{
    readonly Dictionary<int, Top> data_;

    // 
    // 

    /// <summary>
    /// Initializes a new, empty instance of the <see cref="HashMapRecordStore"/> class.
    /// </summary>
    public HashMapRecordStore()
    {
        data_ = [];
    }

    HashMapRecordStore(Dictionary<int, Top> data)
    {
        data_ = data;
    }

    /// <summary>
    /// Serializes the entire state of the hash map store into a stream.
    /// </summary>
    /// <param name="store">The <see cref="HashMapRecordStore"/> instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="BasicRecordStore.SerializationOptions.Default"/> will be used.</param>
    /// <remarks>
    /// The serialization format is specific to this hash map implementation, using Brotli compression and an XxHash32 checksum for data integrity.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(HashMapRecordStore store, Stream stream, SerializationOptions? options = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (store == null)
            throw new ArgumentNullException(nameof(store));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var compressionLevel = (options ?? SerializationOptions.Default).CompressionLevel;

        var firstPosition = stream.Position;
        var xxh = new XxHash32();
        var buffer0 = new byte[64];
        stream.Write(buffer0);

        var count = 0;
        using var memoryStream1 = new MemoryStream();
        using var memoryStream2 = new MemoryStream();
        {
            using var compressStream1 = new BrotliStream(memoryStream1, compressionLevel, true);
            using var compressStream2 = new BrotliStream(memoryStream2, compressionLevel, true);
            var buffer1 = new byte[8];
            foreach (var (identifier, top) in store.data_)
            {
                count++;

                var records = __Records(top.Data);
                if (records.Length > 0x00FFFFFF)
                    throw new InvalidDataException("Too many records.");
                if (records.Any(n => n.Content.Length > 0x0000FFFF))
                    throw new InvalidDataException("Too large record data.");

                BinaryPrimitives.WriteInt32LittleEndian(buffer1, identifier);
                BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(4), records.Length);
                compressStream1.Write(buffer1);

                foreach (var record in records)
                {
                    var buffer2 = new byte[2 + record.Content.Length];
                    BinaryPrimitives.WriteUInt16LittleEndian(buffer2, (ushort)record.Content.Length);
                    record.Content.CopyTo(buffer2.AsSpan(2));
                    compressStream2.Write(buffer2);
                }
            }
        }

        var array1 = memoryStream1.ToArray();
        var array2 = memoryStream2.ToArray();
        xxh.Append(array1);
        xxh.Append(array2);
        stream.Write(array1);
        stream.Write(array2);

        Span<byte> xxhc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(xxhc, xxh.GetCurrentHashAsUInt32());

        var lastPosition = stream.Position;
        //  0: byte * 4, SKV1
        "SKV1"u8.CopyTo(buffer0);// [0x53, 0x4B, 0x56, 0x31]
        //  4: uint * 1, xxhash
        xxhc.CopyTo(buffer0.AsSpan(4));
        //  8: int * 1, key total bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(8), array1.Length);
        // 12: int * 1, record total bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(12), array2.Length);
        // 16: int * 1, reserved
        // 20: int * 1, key count
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(20), count);
        // 24- empty
        stream.Seek(firstPosition, SeekOrigin.Begin);
        stream.Write(buffer0);

        stream.Seek(lastPosition, SeekOrigin.Begin);

        #region @@
        static Record[] __Records(Record? data)
        {
            var result = new List<Record>();
            var current = data;
            while (current != null)
            {
                result.Add(current);
                current = current.Next;
            }
            return [.. result];
        }
        #endregion
    }

    /// <summary>
    /// Deserializes a <see cref="HashMapRecordStore"/> from a stream.
    /// </summary>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <see cref="HashMapRecordStore"/> reconstructed from the stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream data is corrupted, in an unsupported format, or contains invalid values.</exception>
    public static HashMapRecordStore Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[64];
        stream.ReadExactly(buffer0, 0, buffer0.Length);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("SKV1"u8))// [0x53, 0x4B, 0x56, 0x31]
            throw new InvalidDataException("Unsupported format.");

        var xxh = new XxHash32();
        var xxhc = BinaryPrimitives.ReadUInt32LittleEndian(buffer0.AsSpan(4));
        var count = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(20));
        var arrayLength1 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(8));
        var arrayLength2 = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(12));
        var array1 = new byte[arrayLength1];
        var array2 = new byte[arrayLength2];
        stream.ReadExactly(array1);
        stream.ReadExactly(array2);
        xxh.Append(array1);
        xxh.Append(array2);
        if (xxhc != xxh.GetCurrentHashAsUInt32())
            throw new InvalidDataException("Broken.");

        var dictionary = new Dictionary<int, Top>();
        using var memoryStream1 = new MemoryStream(array1);
        using var memoryStream2 = new MemoryStream(array2);
        using var decompressStream1 = new BrotliStream(memoryStream1, CompressionMode.Decompress);
        using var decompressStream2 = new BrotliStream(memoryStream2, CompressionMode.Decompress);

        var buffer1 = new byte[8];
        var buffer2 = new byte[65538];
        while (count-- != 0)
        {
            decompressStream1.ReadExactly(buffer1);

            var identifier = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            var loop = BinaryPrimitives.ReadInt32LittleEndian(buffer1.AsSpan(4));
            var top = new Top();
            Record? record1 = null;
            while (loop-- != 0)
            {
                decompressStream2.ReadExactly(buffer2.AsSpan(0, 2));
                var contentSize = (int)BinaryPrimitives.ReadUInt16LittleEndian(buffer2);
                decompressStream2.ReadExactly(buffer2.AsSpan(2, contentSize));
                var record2 = new Record(null!) { Content = buffer2[2..(2 + contentSize)] };
                if (top.Data == null)
                    top.Data = record1 = record2;
                else
                    record1 = record1!.Next = record2;
            }
            dictionary.Add(identifier, top);
        }

        return new(dictionary);
    }

    /// <summary>
    /// Creates a deep copy of the <see cref="HashMapRecordStore"/>.
    /// </summary>
    /// <returns>A new <see cref="HashMapRecordStore"/> instance with the same keys and record data as the original.</returns>
    /// <remarks>The method creates a new dictionary and copies all key-value pairs and their associated record lists, ensuring that the new store is independent of the original.</remarks>
    public object Clone()
    {
        var clone = new HashMapRecordStore();
        foreach (var (identifier, access1) in Enumerate())
        {
            var access2 = clone.GetRecordAccess(identifier, out var _);
            foreach (var record1 in access1)
                access2.Add([.. record1.Content]);
        }
        return clone;
    }

    /// <summary>
    /// Determines whether a record with the specified identifier exists in the hash map.
    /// </summary>
    /// <param name="identifier">The identifier to locate.</param>
    /// <returns>true if a record with the specified identifier is found; otherwise, false.</returns>
    public override bool Contains(int identifier)
    => data_.ContainsKey(identifier);

    /// <summary>
    /// Removes the record (and its associated list of data) with the specified identifier from the hash map.
    /// </summary>
    /// <param name="identifier">The identifier of the record to remove.</param>
    public override void Remove(int identifier)
    => data_.Remove(identifier);

    /// <summary>
    /// Gets the accessor for a record's list of data. If an entry for the identifier does not exist, it is created.
    /// </summary>
    /// <param name="identifier">The identifier of the record list.</param>
    /// <param name="createNew">When this method returns, contains true if a new entry was created; otherwise, false.</param>
    /// <returns>A <see cref="BasicRecordStore.RecordAccess"/> for the found or newly created record list.</returns>
    public override RecordAccess GetRecordAccess(int identifier, out bool createNew)
    {
        createNew = false;
        if (data_.TryGetValue(identifier, out var top))
            return new(top, identifier);
        else
        {
            createNew = true;
            data_.Add(identifier, top = new());
            return new(top, identifier);
        }
    }

    /// <summary>
    /// Tries to get the accessor for a record's list of data.
    /// </summary>
    /// <param name="identifier">The identifier to locate.</param>
    /// <param name="access">When this method returns, contains the <see cref="BasicRecordStore.RecordAccess"/> object if the identifier was found; otherwise, null.</param>
    /// <returns>true if an entry with the specified identifier was found; otherwise, false.</returns>
    public override bool TryGetRecordAccess(int identifier, out RecordAccess? access)
    {
        access = null;
        if (data_.TryGetValue(identifier, out var top))
        {
            access = new(top, identifier);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns an enumerator that iterates through all records in the store.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of tuples, each containing an identifier and its corresponding <see cref="BasicRecordStore.RecordAccess"/>.</returns>
    /// <remarks>The order of enumeration is not guaranteed to be sorted, as it depends on the internal implementation of the hash map.</remarks>
    public override IEnumerable<(int, RecordAccess)> Enumerate()
    {
        foreach (var (key, top) in data_)
            yield return (key, new(top, key));
    }

    // 
    // 

    class Top : IHaveRecord
    {
        public Record? Data { get; set; }
    }
}
