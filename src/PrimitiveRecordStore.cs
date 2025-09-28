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

using System.Collections;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// Provides a low-level, memory-efficient data store for variable-length byte records.
/// </summary>
/// <remarks>
/// This store uses a series of fixed-size byte arrays ("chunks") to avoid allocations on the Large Object Heap.
/// It internally manages a system of linked lists, where each list is a sequence of records.
/// The entire store can be serialized to and deserialized from a compressed, checksummed binary format.
/// <para>
/// <strong>Record Size Limit:</strong> Please note that the <c>Content</c> for any single record
/// cannot exceed 4,096 bytes. This limit is enforced during operations such as adding or updating records.
/// </para>
/// <para>
/// <strong>Important Usage Note:</strong> This is an append-only style store. When records are removed
/// (i.e., unlinked from their respective lists), the underlying space they occupied is <strong>not</strong> reclaimed or reused.
/// As a result, memory usage will only grow as new records are added. To compact the store and reclaim
/// the space from deleted records, the store must be rebuilt. The correct procedure is to create a new, empty store instance
/// and transfer all valid records from the old store to the new one, for instance by iterating through a known set of root identifiers.
/// </para>
/// </remarks>
public sealed class PrimitiveRecordStore
{
    readonly List<byte[]> chunks_ = [new byte[262144]];
    int biLast_ = 1;

    // 
    // 

    /// <summary>
    /// Serializes the specified store into a byte array.
    /// </summary>
    /// <param name="store">The store to serialize.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <returns>A byte array containing the serialized data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public static byte[] Serialize(PrimitiveRecordStore store, SerializationOptions? options = null)
    {
        using var memoryStream = new MemoryStream();
        Serialize(store, memoryStream, options);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Serializes the specified store to a file.
    /// </summary>
    /// <param name="store">The store to serialize.</param>
    /// <param name="file">The path of the file to create.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="file"/> is empty or whitespace.</exception>
    public static void Serialize(PrimitiveRecordStore store, string file, SerializationOptions? options = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(file);
#else
        if (file == null)
            throw new ArgumentNullException(nameof(file));
#endif
        if (String.IsNullOrWhiteSpace(file))
            throw new ArgumentException($"{nameof(file)} is empty.");

        using var fileStream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
        Serialize(store, fileStream, options);
    }

    /// <summary>
    /// Serializes the specified store into a stream.
    /// </summary>
    /// <param name="store">The store to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(PrimitiveRecordStore store, Stream stream, SerializationOptions? options = null)
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

        var buffer2 = new byte[4];
        foreach (var array in store.chunks_)
        {
            using var memoryStream = new MemoryStream();
            {
                using var compressStream = new BrotliStream(memoryStream, compressionLevel, true);
                compressStream.Write(array);
            }
            var buffer1 = memoryStream.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(buffer2, buffer1.Length);
            stream.Write(buffer2);
            stream.Write(buffer1);
            xxh.Append(buffer2);
            xxh.Append(buffer1);
        }

        Span<byte> xxhc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(xxhc, xxh.GetCurrentHashAsUInt32());

        var lastPosition = stream.Position;
        //  0: byte * 4, LLL1
        "LLL1"u8.CopyTo(buffer0);// [0x4C, 0x4C, 0x4C, 0x31]
        //  4: uint * 1, xxhash
        xxhc.CopyTo(buffer0.AsSpan(4));
        //  8: int * 1, array count
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(8), store.chunks_.Count);
        // 12: int * 1, last block index
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(12), store.biLast_);
        // 16- empty
        stream.Seek(firstPosition, SeekOrigin.Begin);
        stream.Write(buffer0);

        stream.Seek(lastPosition, SeekOrigin.Begin);
    }

    /// <summary>
    /// Deserializes a store from a byte array.
    /// </summary>
    /// <param name="data">The byte array containing the serialized data.</param>
    /// <returns>A new instance of <see cref="PrimitiveRecordStore"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="InvalidDataException">The data is in an unsupported format or is corrupted.</exception>
    public static PrimitiveRecordStore Deserialize(byte[] data)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(data);
#else
        if (data == null)
            throw new ArgumentNullException(nameof(data));
#endif

        using var memoryStream = new MemoryStream(data);
        return Deserialize(memoryStream);
    }

    /// <summary>
    /// Deserializes a store from a file.
    /// </summary>
    /// <param name="file">The path of the file to read from.</param>
    /// <returns>A new instance of <see cref="PrimitiveRecordStore"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="file"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidDataException">The data is in an unsupported format or is corrupted.</exception>
    public static PrimitiveRecordStore Deserialize(string file)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(file);
#else
        if (file == null)
            throw new ArgumentNullException(nameof(file));
#endif
        if (String.IsNullOrWhiteSpace(file))
            throw new ArgumentException($"{nameof(file)} is empty.");

        using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Deserialize(fileStream);
    }

    /// <summary>
    /// Deserializes a store from a stream.
    /// </summary>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <see cref="PrimitiveRecordStore"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The data is in an unsupported format or is corrupted.</exception>
    public static PrimitiveRecordStore Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[64];
        stream.ReadExactly(buffer0, 0, buffer0.Length);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("LLL1"u8))// "[0x4C, 0x4C, 0x4C, 0x31]*/
            throw new InvalidDataException("Unsupported format.");

        var xxh = new XxHash32();
        var xxhc = BinaryPrimitives.ReadUInt32LittleEndian(buffer0.AsSpan(4));

        var store = new PrimitiveRecordStore() { biLast_ = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(12)) };
        store.chunks_.Clear();

        var count = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(8));
        var buffer2 = new byte[4];
        while (count-- != 0)
        {
            stream.ReadExactly(buffer2, 0, 4);
            var buffer1 = new byte[BinaryPrimitives.ReadInt32LittleEndian(buffer2)];
            stream.ReadExactly(buffer1);
            xxh.Append(buffer2);
            xxh.Append(buffer1);

            using var memoryStream = new MemoryStream(buffer1);
            using var decompressStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
            var buffer3 = new byte[262144];
            decompressStream.ReadExactly(buffer3);
            store.chunks_.Add(buffer3);
        }

        if (xxhc != xxh.GetCurrentHashAsUInt32())
            throw new InvalidDataException("Broken.");

        return store;
    }

    /// <summary>
    /// Clears all data from the store and resets it to its initial state.
    /// </summary>
    public void Clear()
    {
        chunks_.Clear();
        chunks_.Add(new byte[262144]);
        biLast_ = 2;
    }

    /// <summary>
    /// Gets a <see cref="RecordAccess"/> handle for a linked list of records.
    /// </summary>
    /// <param name="identifier">The identifier of the record list. Specify 0 to create a new list and get its identifier.</param>
    /// <returns>A <see cref="RecordAccess"/> object to manage the specified record list.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identifier"/> is negative.</exception>
    /// <exception cref="ArgumentException">The specified <paramref name="identifier"/> is invalid or does not exist.</exception>
    public RecordAccess GetRecordAccess(int identifier = 0)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(identifier);
#else
        if (identifier < 0)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must be greater than or equal 0.");
#endif

        if (identifier == 0)
            return new(this, CreateNewList());
        else
        {
            var (ai, bi) = ((int)(((uint)identifier & 0x1FFF0000u) >> 16), identifier & 0x0000FFFF);
            if (ai >= chunks_.Count)
                throw new ArgumentException($"Invalid {nameof(identifier)}.");
            if (ai + 1 == chunks_.Count && bi >= biLast_)
                throw new ArgumentException($"Invalid {nameof(identifier)}.");
            return new(this, identifier);
        }

        #region @@
        int CreateNewList()
        {
            if (biLast_ + 1 >= 65536)
            {
                if (chunks_.Count == 0x1FFF)
                    throw new ApplicationException($"Full.");

                chunks_.Add(new byte[262144]);
                biLast_ = 0;
            }

            var (ai, bi) = (chunks_.Count - 1, biLast_++);
            return (ai << 16) | bi | 0x20000000;
        }
        #endregion
    }

    static (int, int, int) FromExtraValue(uint value)
    => ((int)((value & 0xFFE00000u) >> 21), (int)((value & 0x001FFF00u) >> 8), (int)(value & 0x000000FFu));

    static uint ToExtraValue(int capacity, int actual, int exbyte)
    => (((uint)capacity & 0x000007FFu) << 21) | (((uint)actual & 0x1FFF) << 8) | ((uint)exbyte & 0x000000FF);

    static uint ToExtraValue(int? capacity, int? actual, int? exbyte, uint initial)
    {
        var (a, b, _) = FromExtraValue(initial);
        return ToExtraValue(capacity ?? a, actual ?? b, exbyte ?? b);
    }

    Span<byte> GetBuffer(int identifier, bool all = false)
    {
        var (ai, bi) = ((int)(((uint)identifier & 0x1FFF0000u) >> 16), identifier & 0x0000FFFF);
        var array = chunks_[ai];
        if (!all || (identifier & 0x20000000) != 0)
            return array.AsSpan(bi * 4, 4);
        else
        {
            var (capacity, _, _) = FromExtraValue(BinaryPrimitives.ReadUInt32LittleEndian(array.AsSpan(bi * 4 + 4)));
            return array.AsSpan(bi * 4, 8 + capacity * 4);
        }
    }

    Span<byte> Allocate(int contentSize, out int identifier)
    {
        var contentCapacity = (contentSize + 4 - (contentSize % 4)) / 4;

        if (2 + contentCapacity + biLast_ >= 65536)
        {
            if (chunks_.Count == 0x1FFF)
                throw new ApplicationException($"Full.");

            chunks_.Add(new byte[262144]);
            biLast_ = 0;
        }

        var (ai, bi) = (chunks_.Count - 1, biLast_);
        biLast_ += 2 + contentCapacity;
        identifier = (ai << 16) | bi;
        var array = chunks_[ai];
        BinaryPrimitives.WriteUInt32LittleEndian(array.AsSpan(bi * 4 + 4), ToExtraValue(contentCapacity, contentSize, 0));
        return array.AsSpan(bi * 4, 8 + contentCapacity * 4);
    }

    // 
    // 

    /// <summary>
    /// Provides options to control the serialization process for data store.
    /// </summary>
    public class SerializationOptions
    {
        /// <summary>
        /// Gets or sets the compression level to use for serialization.
        /// </summary>
        /// <remarks>
        /// Higher compression levels can result in smaller file sizes but may take longer to process.
        /// Defaults to <see cref="CompressionLevel.Fastest"/> for a balance of speed and size.
        /// </remarks>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;

        /// <summary>
        /// Gets a singleton instance of <see cref="SerializationOptions"/> with default values.
        /// </summary>
        /// <remarks>
        /// Use this property to avoid creating a new options object when default settings are sufficient.
        /// </remarks>
        public static SerializationOptions Default { get; } = new();
    }

    /// <summary>
    /// Represents a handle to a single record within the store.
    /// </summary>
    /// <remarks>
    /// This object is a proxy that provides methods to read, update, and navigate the record's data and relationships.
    /// Operations performed on this object modify the underlying data in the parent <see cref="PrimitiveRecordStore"/>.
    /// </remarks>
    public sealed class Record
    {
        readonly RecordAccess access_;

        /// <summary>
        /// Gets the unique, persistent identifier for this record within the store.
        /// </summary>
        public int Identifier { get; }
        /// <summary>
        /// Gets or sets an extra, user-definable byte of metadata associated with this record. The value must be in the range of 0 to 255.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The assigned value is less than 0 or greater than 255.</exception>
        public int ExByte { get => access_.GetExByte(this); set => access_.SetExByte(this, value); }
        /// <summary>
        /// Reads the content of the record.
        /// </summary>
        /// <returns>A read-only span of bytes representing the record's content.</returns>
        public ReadOnlySpan<byte> Read() => access_.ReadContent(this);
        /// <summary>
        /// Updates the content of the record.
        /// </summary>
        /// <param name="content">The new content for the record.</param>
        public void Update(ReadOnlySpan<byte> content) => access_.UpdateContent(this, content);
        /// <summary>
        /// Clears the content of the record, setting its length to zero.
        /// </summary>
        public void Clear() => access_.UpdateContent(this, []);
        /// <summary>
        /// Gets the total allocated storage capacity for the record's content in bytes.
        /// </summary>
        public int Capacity => access_.GetContentCapacitySize(this);
        /// <summary>
        /// Gets the actual size of the record's content in bytes.
        /// </summary>
        public int Actual => access_.GetContentActualSize(this);
        /// <summary>
        /// Gets the next record in the linked list, or null if this is the last record.
        /// </summary>
        public Record? Next => access_.GetNext(this);
        /// <summary>
        /// Removes this record from the linked list it belongs to.
        /// </summary>
        public void Remove() => access_.Remove(this);
        /// <summary>
        /// Inserts a new record with the specified content immediately before this record in the list.
        /// </summary>
        /// <param name="content">The content of the new record to insert.</param>
        /// <returns>A <see cref="Record"/> handle for the newly inserted record.</returns>
        public Record InsertBefore(ReadOnlySpan<byte> content) => access_.InsertBefore(this, content);
        /// <summary>
        /// Inserts a new record with the specified content immediately after this record in the list.
        /// </summary>
        /// <param name="content">The content of the new record to insert.</param>
        /// <returns>A <see cref="Record"/> handle for the newly inserted record.</returns>
        public Record InsertAfter(ReadOnlySpan<byte> content) => access_.InsertAfter(this, content);

        internal Record(RecordAccess access, int identifier)
        {
            access_ = access;
            this.Identifier = identifier;
        }
    }

    /// <summary>
    /// Provides access to a linked list of records within a <see cref="PrimitiveRecordStore"/>.
    /// </summary>
    /// <remarks>
    /// This class implements <see cref="IEnumerable{Record}"/> to allow iterating over the records in the list.
    /// The collection will throw an <see cref="InvalidOperationException"/> if modified during enumeration.
    /// </remarks>
    public sealed class RecordAccess : IEnumerable<Record>
    {
        /// <summary>
        /// Gets the parent <see cref="PrimitiveRecordStore"/> that this accessor belongs to.
        /// </summary>
        public PrimitiveRecordStore Store { get; }
        /// <summary>
        /// Gets the identifier for the head of the linked list that this object accesses.
        /// </summary>
        public int Identifier { get; }
        internal bool Modified { get; set; }

        internal RecordAccess(PrimitiveRecordStore store, int identifier)
        {
            this.Store = store;
            this.Identifier = identifier;
        }

        /// <summary>
        /// Removes all records from this list.
        /// </summary>
        /// <exception cref="InvalidOperationException">The operation is not supported for this accessor instance.</exception>
        public void Clear()
        {
            if ((this.Identifier & 0x20000000) == 0)
                throw new InvalidOperationException("No root.");

            Store.GetBuffer(this.Identifier).Clear();
            Modified = true;
        }

        /// <summary>
        /// Adds a new record with the specified content to the end of the list.
        /// </summary>
        /// <param name="content">The content of the new record.</param>
        /// <returns>A <see cref="Record"/> handle for the newly added record.</returns>
        /// <exception cref="InvalidOperationException">The operation is not supported for this accessor instance.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="content"/> exceeds the maximum allowed size (4096 bytes).</exception>
        public Record Add(ReadOnlySpan<byte> content)
        {
            if ((this.Identifier & 0x20000000) == 0)
                throw new InvalidOperationException("No root.");

#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, 4096);
#else
            if (content.Length > 4096)
                throw new ArgumentOutOfRangeException(nameof(content), $"Length of {nameof(content)} must be less than or equal 4096.");
#endif

            var span = Store.GetBuffer(this.Identifier);
            var current = BinaryPrimitives.ReadInt32LittleEndian(span);
            while (current != 0)
                current = BinaryPrimitives.ReadInt32LittleEndian(span = Store.GetBuffer(current));
            var allocated = Store.Allocate(content.Length, out var identifier);
            BinaryPrimitives.WriteInt32LittleEndian(span, identifier);
            content.CopyTo(allocated[8..]);
            Modified = true;
            return new(this, identifier);
        }

        internal void Remove(Record record)
        {
            if ((this.Identifier & 0x20000000) == 0)
                throw new InvalidOperationException("No root.");

            var span1 = Store.GetBuffer(this.Identifier);
            var current = BinaryPrimitives.ReadInt32LittleEndian(span1);
            while (current != 0)
            {
                if (current == record.Identifier)
                {
                    var span2 = Store.GetBuffer(record.Identifier);
                    BinaryPrimitives.WriteInt32LittleEndian(span1, BinaryPrimitives.ReadInt32LittleEndian(span2));
                    BinaryPrimitives.WriteInt32LittleEndian(span2, 0);
                    Modified = true;
                    return;
                }

                current = BinaryPrimitives.ReadInt32LittleEndian(span1 = Store.GetBuffer(current));
            }

            throw new InvalidOperationException($"Record missing.");
        }

        internal Record InsertBefore(Record record1, ReadOnlySpan<byte> content)
        {
            if ((this.Identifier & 0x20000000) == 0)
                throw new InvalidOperationException("No root.");

#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, 4096);
#else
            if (content.Length > 4096)
                throw new ArgumentOutOfRangeException(nameof(content), $"Length of {nameof(content)} must be less than or equal 4096.");
#endif

            var allocated = Store.Allocate(content.Length, out var identifier);
            content.CopyTo(allocated[8..]);
            var record2 = new Record(this, identifier);

            var span1 = Store.GetBuffer(this.Identifier);
            var current = BinaryPrimitives.ReadInt32LittleEndian(span1);
            while (current != 0)
            {
                if (current == record1.Identifier)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(allocated, current);
                    BinaryPrimitives.WriteInt32LittleEndian(span1, identifier);
                    Modified = true;
                    return record2;
                }

                current = BinaryPrimitives.ReadInt32LittleEndian(span1 = Store.GetBuffer(current));
            }

            throw new InvalidOperationException($"Record missing.");
        }

        internal Record InsertAfter(Record record1, ReadOnlySpan<byte> content)
        {
            if ((this.Identifier & 0x20000000) == 0)
                throw new InvalidOperationException("No root.");

#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, 4096);
#else
            if (content.Length > 4096)
                throw new ArgumentOutOfRangeException(nameof(content), $"Length of {nameof(content)} must be less than or equal 4096.");
#endif

            var allocated = Store.Allocate(content.Length, out var identifier);
            content.CopyTo(allocated[8..]);
            var record2 = new Record(this, identifier);

            var span1 = Store.GetBuffer(this.Identifier);
            var current = BinaryPrimitives.ReadInt32LittleEndian(span1);
            while (current != 0)
            {
                if (current == record1.Identifier)
                {
                    var span2 = Store.GetBuffer(record1.Identifier);
                    BinaryPrimitives.WriteInt32LittleEndian(allocated, BinaryPrimitives.ReadInt32LittleEndian(span2));
                    BinaryPrimitives.WriteInt32LittleEndian(span2, identifier);
                    Modified = true;
                    return record2;
                }

                current = BinaryPrimitives.ReadInt32LittleEndian(span1 = Store.GetBuffer(current));
            }

            throw new InvalidOperationException($"Record missing.");
        }

        internal ReadOnlySpan<byte> ReadContent(Record record)
        {
            var span = Store.GetBuffer(record.Identifier, true);
            var (_, actual, _) = FromExtraValue(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
            return span[8..(8 + actual)];
        }

        internal void UpdateContent(Record record, ReadOnlySpan<byte> content)
        {
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, 4096);
#else
            if (content.Length > 4096)
                throw new ArgumentOutOfRangeException(nameof(content), $"Length of {nameof(content)} must be less than or equal 4096.");
#endif

            var span = Store.GetBuffer(record.Identifier, true);
            var (capacity, _, exbyte) = FromExtraValue(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, capacity * 4);
#else
            if (content.Length > capacity * 4)
                throw new ArgumentOutOfRangeException(nameof(content), $"Length of {nameof(content)} must be less than or equal {capacity * 4}.");
#endif

            content.CopyTo(span[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ToExtraValue(capacity, content.Length, exbyte));
        }

        internal int GetContentCapacitySize(Record record)
        {
            var span = Store.GetBuffer(record.Identifier, true);
            var (capacity, _, _) = FromExtraValue(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
            return capacity * 4;
        }

        internal int GetContentActualSize(Record record)
        {
            var span = Store.GetBuffer(record.Identifier, true);
            var (_, actual, _) = FromExtraValue(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
            return actual;
        }

        internal int GetExByte(Record record)
        {
            var span = Store.GetBuffer(record.Identifier, true);
            var (_, _, exbyte) = FromExtraValue(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
            return exbyte;
        }

        internal void SetExByte(Record record, int exbyte)
        {
            if ((exbyte & 0xFFFFFF00) != 0)
                throw new ArgumentOutOfRangeException(nameof(exbyte));

            var span = Store.GetBuffer(record.Identifier, true);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ToExtraValue(null, null, exbyte, BinaryPrimitives.ReadUInt32LittleEndian(span[4..])));
        }

        internal Record? GetNext(Record record)
        {
            var span = Store.GetBuffer(record.Identifier);
            var current = BinaryPrimitives.ReadInt32LittleEndian(span);
            return current != 0 ? new(this, current) : null;
        }

        internal Record? GetFirst()
        {
            if ((this.Identifier & 0x20000000) == 0)
                throw new InvalidOperationException("No root.");

            var span = Store.GetBuffer(this.Identifier);
            var current = BinaryPrimitives.ReadInt32LittleEndian(span); ;
            return current != 0 ? new(this, current) : null;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the records in the list.
        /// </summary>
        /// <returns>An enumerator for this record list.</returns>
        public IEnumerator<Record> GetEnumerator()
        {
            this.Modified = false;
            return new Enumerator(this);
        }


        IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

        // 
        // 

        class Enumerator(RecordAccess access) : IEnumerator<Record>
        {
            public Record Current { get; private set; } = null!;
            object IEnumerator.Current { get => Current; }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (access.Modified)
                    throw new InvalidOperationException("Sequence modified.");

                if (this.Current == null)
                    return (this.Current = access.GetFirst()!) != null;
                else
                {
                    var next = access.GetNext(this.Current);
                    if (next == null)
                        return false;
                    else
                    {
                        this.Current = next;
                        return true;
                    }
                }
            }

            public void Reset()
            {
                this.Current = null!;
                access.Modified = false;
            }
        }
    }
}
