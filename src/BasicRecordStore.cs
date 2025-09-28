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
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// An abstract base class for key-value record stores.
/// </summary>
/// <remarks>
/// This class defines a common interface for record manipulation (add, remove, enumerate, etc.)
/// and provides a generic, reflection-based serialization mechanism that dispatches to the static methods of the concrete derived class.
/// </remarks>
public abstract class BasicRecordStore
{
    /// <summary>
    /// Serializes the specified store instance into a byte array.
    /// </summary>
    /// <typeparam name="T">The concrete type of the record store, which must have a public static `Serialize(T, Stream)` method.</typeparam>
    /// <param name="obj">The record store instance to serialize.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <returns>A byte array containing the serialized data.</returns>
    /// <remarks>This static method uses reflection to invoke the static `Serialize(T, Stream)` method on the actual type of <paramref name="obj"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
    public static byte[] Serialize<
#if NET5_0_OR_GREATER
   [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(T obj, SerializationOptions? options = null) where T : BasicRecordStore
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(obj);
#else
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));
#endif

        using var memoryStream = new MemoryStream();
        Serialize(obj, memoryStream, options);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Serializes the specified store instance to a file.
    /// </summary>
    /// <typeparam name="T">The concrete type of the record store, which must have a public static `Serialize(T, Stream)` method.</typeparam>
    /// <param name="obj">The record store instance to serialize.</param>
    /// <param name="file">The path of the file to create.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <remarks>This static method uses reflection to invoke the static `Serialize(T, Stream)` method on the actual type of <paramref name="obj"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="file"/> is empty or whitespace.</exception>
    public static void Serialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(T obj, string file, SerializationOptions? options = null) where T : BasicRecordStore
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
        Serialize(obj, fileStream, options);
    }

    /// <summary>
    /// Serializes the specified store instance into a stream using reflection.
    /// </summary>
    /// <typeparam name="T">The concrete type of the record store, which must have a public static `Serialize(T, Stream)` method.</typeparam>
    /// <param name="obj">The record store instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="stream"/> is null.</exception>
    /// <exception cref="NotSupportedException">The type <typeparamref name="T"/> does not have a public static `Serialize(T, Stream)` method.</exception>
    public static void Serialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(T obj, Stream stream, SerializationOptions? options = null) where T : BasicRecordStore
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var type = typeof(T);
        var m = type.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static, null, [type, typeof(Stream), typeof(SerializationOptions)], null) ?? throw new NotSupportedException();
        m.Invoke(null, [obj, stream, options]);
    }

    /// <summary>
    /// Deserializes a store from a byte array.
    /// </summary>
    /// <typeparam name="T">The concrete type of the record store to deserialize, which must have a public static `Deserialize(Stream)` method.</typeparam>
    /// <param name="data">The byte array containing the serialized data.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    /// <remarks>This static method uses reflection to invoke the static `Deserialize(Stream)` method on the specified type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    public static T Deserialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(byte[] data) where T : BasicRecordStore
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(data);
#else
        if (data == null)
            throw new ArgumentNullException(nameof(data));
#endif

        using var memoryStream = new MemoryStream(data);
        return Deserialize<T>(memoryStream);
    }

    /// <summary>
    /// Deserializes a store from a file.
    /// </summary>
    /// <typeparam name="T">The concrete type of the record store to deserialize, which must have a public static `Deserialize(Stream)` method.</typeparam>
    /// <param name="file">The path of the file to read from.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    /// <remarks>This static method uses reflection to invoke the static `Deserialize(Stream)` method on the specified type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="file"/> is empty or whitespace.</exception>
    public static T Deserialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(string file) where T : BasicRecordStore
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
        return Deserialize<T>(fileStream);
    }

    /// <summary>
    /// Deserializes a store from a stream using reflection.
    /// </summary>
    /// <typeparam name="T">The concrete type of the record store to deserialize, which must have a public static `Deserialize(Stream)` method.</typeparam>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="NotSupportedException">The type <typeparamref name="T"/> does not have a public static `Deserialize(Stream)` method.</exception>
    public static T Deserialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(Stream stream) where T : BasicRecordStore
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var type = typeof(T);
        var m = type.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static, null, [typeof(Stream)], null) ?? throw new NotSupportedException();
        return m.Invoke(null, [stream]) as T ?? throw new NotSupportedException();
    }

    /// <summary>
    /// When overridden in a derived class, removes all records from the store.
    /// </summary>
    public virtual void Clear()
    => throw new NotImplementedException();

    /// <summary>
    /// When overridden in a derived class, determines whether a record with the specified identifier exists.
    /// </summary>
    /// <param name="identifier">The identifier to locate.</param>
    /// <returns>true if a record with the specified identifier is found; otherwise, false.</returns>
    public virtual bool Contains(int identifier)
    => throw new NotImplementedException();

    /// <summary>
    /// When overridden in a derived class, removes a record with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the record to remove.</param>
    public virtual void Remove(int identifier)
    => throw new NotImplementedException();

    /// <summary>
    /// Adds a record with the specified identifier, or returns false if it already exists.
    /// </summary>
    /// <param name="identifier">The identifier of the record to add.</param>
    /// <returns>true if a new record was added; false if a record with that identifier already existed.</returns>
    public virtual bool Add(int identifier)
    {
        GetRecordAccess(identifier, out var createNew);
        return createNew;
    }

    /// <summary>
    /// Gets the accessor for a record with the specified identifier. If the record does not exist, it is created.
    /// </summary>
    /// <param name="identifier">The identifier of the record to get or create.</param>
    /// <returns>A <see cref="RecordAccess"/> for the found or newly created record.</returns>
    public virtual RecordAccess GetRecordAccess(int identifier)
    => GetRecordAccess(identifier, out var _);

    /// <summary>
    /// When overridden in a derived class, gets the accessor for a record with the specified identifier, indicating if it was newly created.
    /// </summary>
    /// <param name="identifier">The identifier of the record to get or create.</param>
    /// <param name="createNew">When this method returns, contains true if a new record was created; otherwise, false.</param>
    /// <returns>A <see cref="RecordAccess"/> for the found or newly created record.</returns>
    public virtual RecordAccess GetRecordAccess(int identifier, out bool createNew)
    => throw new NotImplementedException();

    /// <summary>
    /// When overridden in a derived class, tries to get the accessor for a record with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier to locate.</param>
    /// <param name="access">When this method returns, contains the <see cref="RecordAccess"/> object if the identifier was found; otherwise, null.</param>
    /// <returns>true if a record with the specified identifier was found; otherwise, false.</returns>
    public virtual bool TryGetRecordAccess(int identifier, out RecordAccess? access)
    => throw new NotImplementedException();

    /// <summary>
    /// When overridden in a derived class, returns an enumerator that iterates through all records in the store.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of tuples, each containing an identifier and its corresponding <see cref="RecordAccess"/>.</returns>
    public virtual IEnumerable<(int, RecordAccess)> Enumerate()
    => throw new NotImplementedException();

    // 
    // 

    /// <summary>
    /// Provides options to control the serialization process for key-value record stores.
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
    /// Defines a contract for objects that can own a linked list of <see cref="Record"/> objects.
    /// </summary>
    /// <exclude />
    public interface IHaveRecord
    {
        /// <summary>
        /// Gets or sets the first record (the head) of the linked list.
        /// </summary>
        Record? Data { get; set; }
    }

    /// <summary>
    /// Represents a single node in a linked list of records.
    /// </summary>
    public sealed class Record
    {
        readonly RecordAccess access_;
        byte[] content_ = [];

        /// <summary>
        /// Gets or sets the byte array content of this record.
        /// </summary>
        /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
        public byte[] Content { get => content_; set { content_ = value ?? throw new ArgumentNullException(nameof(value)); } }
        /// <summary>
        /// Gets the next record in the list, or null if this is the last record.
        /// </summary>
        public Record? Next { get; internal set; }
        /// <summary>
        /// Removes this record from the list it belongs to.
        /// </summary>
        public void Remove() => access_.Remove(this);
        /// <summary>
        /// Inserts a new record with the specified content immediately before this record in the list.
        /// </summary>
        /// <param name="content">The content of the new record to insert.</param>
        /// <returns>The newly inserted <see cref="Record"/>.</returns>
        public Record InsertBefore(ReadOnlySpan<byte> content) => access_.InsertBefore(this, new(access_) { Content = [.. content] });
        /// <summary>
        /// Inserts a new record with the specified content immediately after this record in the list.
        /// </summary>
        /// <param name="content">The content of the new record to insert.</param>
        /// <returns>The newly inserted <see cref="Record"/>.</returns>
        public Record InsertAfter(ReadOnlySpan<byte> content) => access_.InsertAfter(this, new(access_) { Content = [.. content] });

        internal Record(RecordAccess access)
        {
            access_ = access;
        }
    }

    /// <summary>
    /// Provides access to and manages a linked list of <see cref="Record"/> objects.
    /// </summary>
    /// <remarks>
    /// This class implements <see cref="IEnumerable{Record}"/> to allow easy iteration.
    /// The collection will throw an <see cref="InvalidOperationException"/> if modified during enumeration.
    /// </remarks>
    public sealed class RecordAccess : IEnumerable<Record>
    {
        internal IHaveRecord HaveRecord { get; }
        internal bool Modified { get; private set; }
        /// <summary>
        /// Gets the identifier for the head of the linked list that this object accesses.
        /// </summary>
        public int Identifier { get; }

        internal RecordAccess(IHaveRecord haveRecord, int identifier)
        {
            this.HaveRecord = haveRecord;
            this.Identifier = identifier;
        }

        /// <summary>
        /// Removes all records from the list.
        /// </summary>
        public void Clear()
        {
            this.HaveRecord.Data = null;
            Modified = true;
        }

        internal void Remove(Record record)
        {
            var current = this.HaveRecord.Data ?? throw new InvalidOperationException($"Record missing.");

            if (current == record)
            {
                this.HaveRecord.Data = current.Next;
                record.Next = null;
                Modified = true;
                return;
            }
            else
            {
                Record previous = current;
                current = current.Next;
                while (current != null)
                {
                    if (current == record)
                    {
                        previous.Next = current.Next;
                        current.Next = null;
                        Modified = true;
                        return;
                    }
                    previous = current;
                    current = current.Next;
                }
            }

            throw new InvalidOperationException($"Record missing.");
        }

        /// <summary>
        /// Adds a new record with the specified content to the end of the list.
        /// </summary>
        /// <param name="content">The content of the new record.</param>
        /// <returns>The newly added <see cref="Record"/>.</returns>
        public Record Add(ReadOnlySpan<byte> content)
        {
            var current = this.HaveRecord.Data;
            if (current == null)
            {
                Modified = true;
                return this.HaveRecord.Data = new(this) { Content = content.ToArray() };
            }
            else
            {
                while (current != null)
                {
                    if (current.Next == null)
                    {
                        Modified = true;
                        return current.Next = new(this) { Content = content.ToArray() };
                    }
                    current = current.Next;
                }
            }

            throw new InvalidDataException("Broken.");
        }

        internal Record InsertBefore(Record record1, Record record2)
        {
            var current = this.HaveRecord.Data ?? throw new InvalidOperationException($"Record missing.");
            if (current == record1)
            {
                (this.HaveRecord.Data = record2).Next = record1;
                Modified = true;
                return record2;
            }
            else
            {
                Record previous = current;
                current = current.Next;
                while (current != null)
                {
                    if (current == record1)
                    {
                        (previous.Next = record2).Next = record1;
                        Modified = true;
                        return record2;
                    }
                    previous = current;
                    current = current.Next;
                }
            }

            throw new InvalidOperationException($"Record missing.");
        }

        internal Record InsertAfter(Record record1, Record record2)
        {
            var current = this.HaveRecord.Data ?? throw new InvalidOperationException($"Record missing.");
            while (current != null)
            {
                if (current == record1)
                {
                    record2.Next = current.Next;
                    current.Next = record2;
                    Modified = true;
                    return record2;
                }
                current = current.Next;
            }

            throw new InvalidOperationException($"Record missing.");
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
            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (access.Modified)
                    throw new InvalidOperationException("Sequence modified.");

                if (this.Current == null)
                    return (this.Current = access.HaveRecord.Data!) != null;
                else
                {
                    if (this.Current.Next == null)
                        return false;
                    else
                    {
                        this.Current = this.Current.Next;
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
