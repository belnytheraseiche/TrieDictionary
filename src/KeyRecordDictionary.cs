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
using System.Collections;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// An abstract base class for advanced key-record dictionaries.
/// </summary>
/// <remarks>
/// This class provides a framework for mapping byte array keys to linked lists of data records.
/// It features a dual-storage system (transient and persistent) for records, supports different key search directions (LTR/RTL),
/// and allows for custom metadata storage. Concrete implementations are expected to provide the specific key management structure (e.g., a trie).
/// </remarks>
/// <param name="searchDirection">The key search direction for the new dictionary.</param>
public abstract class KeyRecordDictionary(KeyRecordDictionary.SearchDirectionType searchDirection)
{
    /// <summary>
    /// Gets the search direction (Left-To-Right or Right-To-Left) for key operations.
    /// </summary>
    public SearchDirectionType SearchDirection { get; } = searchDirection;
    /// <summary>
    /// Gets or sets a small, general-purpose byte array for additional persistent data.
    /// </summary>
    /// <remarks>The size of the byte array cannot exceed 4096 bytes.</remarks>
    /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The length of the assigned byte array is greater than 4096.</exception>
    public byte[] Additional1
    {
        get => additional1_;
        set
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(value);
#else
            if (value == null)
                throw new ArgumentNullException(nameof(value));
#endif
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, 4096);
#else
            if (value.Length > 4096)
                throw new ArgumentOutOfRangeException(nameof(value), $"Length of {nameof(value)} must be less than or equal 4096.");
#endif
            additional1_ = value;
            // additional1_ = value?.Length switch
            // {
            //     <= 4096 => value,
            //     > 4096 => throw new ArgumentOutOfRangeException(nameof(value)),
            //     _ => throw new ArgumentNullException(nameof(value)),
            // };
        }
    }
    /// <summary>
    /// Gets or sets a large, general-purpose byte array for additional persistent data.
    /// </summary>
    /// <remarks>The size of the byte array cannot exceed 1,073,741,824 bytes (1 GB).</remarks>
    /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The length of the assigned byte array is greater than 1 GB.</exception>
    public byte[] Additional2
    {
        get => additional2_;
        set
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(value);
#else
            if (value == null)
                throw new ArgumentNullException(nameof(value));
#endif
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, 1073741824);
#else
            if (value.Length > 1073741824)
                throw new ArgumentOutOfRangeException(nameof(value), $"Length of {nameof(value)} must be less than or equal 1073741824.");
#endif
            additional2_ = value;
            // additional2_ = value?.Length switch
            // {
            //     <= 1073741824 => value,
            //     > 1073741824 => throw new ArgumentOutOfRangeException(nameof(value)),
            //     _ => throw new ArgumentNullException(nameof(value)),
            // };
        }
    }

    // small, persistent
    byte[] additional1_ = [];
    // large, persistent
    byte[] additional2_ = [];
    /// <exclude />
    // protected BasicRecordStore btRecords_ = new TreapRecordStore();
    protected BasicRecordStore transientRecords_ = new HashMapRecordStore();// faster
    /// <exclude />
    protected PrimitiveRecordStore persistentRecords_ = new();

    /// <summary>
    /// Creates a new instance of a derived dictionary type with an empty set of keys.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary to create.</typeparam>
    /// <param name="searchDirection">The key search direction for the new dictionary.</param>
    /// <returns>A new instance of the specified dictionary type.</returns>
    public static T Create<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(SearchDirectionType searchDirection = SearchDirectionType.LTR) where T : KeyRecordDictionary
    {
        return Create<T>([], searchDirection);
    }

    /// <summary>
    /// Creates a new instance of a derived dictionary type, populated with an initial set of keys.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary to create.</typeparam>
    /// <param name="keys">An enumerable collection of byte arrays to use as the initial keys. The collection does not need to be pre-sorted. Duplicate keys are permitted, but only the first occurrence will be added. The collection must not contain any elements that are null or empty byte array.</param>
    /// <param name="searchDirection">The key search direction for the new dictionary.</param>
    /// <returns>A new instance of the specified dictionary type.</returns>
    /// <remarks>This method uses reflection to invoke the static `Create` method on the concrete derived type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    public static T Create<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(IEnumerable<byte[]> keys, SearchDirectionType searchDirection = SearchDirectionType.LTR) where T : KeyRecordDictionary
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(keys);
#else
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));
#endif

        var type = typeof(T);
        var m = type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public, null, [typeof(IEnumerable<byte[]>), typeof(SearchDirectionType)], null) ?? throw new NotSupportedException();
        return m.Invoke(null, [keys, searchDirection]) as T ?? throw new NotSupportedException();
    }

    /// <summary>
    /// Creates a new instance of a derived dictionary type, populated with an initial set of string keys.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary to create.</typeparam>
    /// <param name="keys">An enumerable collection of strings to use as the initial keys. The collection does not need to be pre-sorted. Duplicate keys are permitted, but only the first occurrence will be added. The collection must not contain any elements that are null or empty strings.</param>
    /// <param name="encoding">The encoding to use for converting strings to bytes. Defaults to UTF-8.</param>
    /// <param name="searchDirection">The key search direction for the new dictionary.</param>
    /// <returns>A new instance of the specified dictionary type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    public static T Create<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(IEnumerable<string> keys, Encoding? encoding = null, SearchDirectionType searchDirection = SearchDirectionType.LTR) where T : KeyRecordDictionary
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(keys);
#else
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));
#endif

        encoding ??= Encoding.UTF8;
        return Create<T>(keys.Select(n => encoding.GetBytes(n)), searchDirection);
    }

    /// <summary>
    /// Serializes the specified dictionary instance into a byte array.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary.</typeparam>
    /// <param name="obj">The dictionary instance to serialize.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <returns>A byte array containing the serialized data.</returns>
    /// <remarks>This static method uses reflection to invoke the static `Serialize` method on the concrete derived type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
    public static byte[] Serialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(T obj, SerializationOptions? options = null) where T : KeyRecordDictionary
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
    /// Serializes the specified dictionary instance to a file.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary.</typeparam>
    /// <param name="obj">The dictionary instance to serialize.</param>
    /// <param name="file">The path of the file to create.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <remarks>This static method uses reflection to invoke the static `Serialize` method on the concrete derived type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="file"/> is empty or whitespace.</exception>
    public static void Serialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(T obj, string file, SerializationOptions? options = null) where T : KeyRecordDictionary
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
    /// Serializes the specified dictionary instance into a stream.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary.</typeparam>
    /// <param name="obj">The dictionary instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="SerializationOptions.Default"/> will be used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="stream"/> is null.</exception>
    /// <exception cref="NotSupportedException">The type <typeparamref name="T"/> does not have a public static `Serialize(T, Stream)` method.</exception>
    public static void Serialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(T obj, Stream stream, SerializationOptions? options = null) where T : KeyRecordDictionary
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
    /// Deserializes a dictionary from a byte array.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary to deserialize.</typeparam>
    /// <param name="data">The byte array containing the serialized data.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    /// <remarks>This static method uses reflection to invoke the static `Deserialize` method on the specified type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    public static T Deserialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(byte[] data) where T : KeyRecordDictionary
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
    /// Deserializes a dictionary from a file.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary to deserialize.</typeparam>
    /// <param name="file">The path of the file to read from.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    /// <remarks>This static method uses reflection to invoke the static `Deserialize` method on the specified type <typeparamref name="T"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="file"/> is empty or whitespace.</exception>
    public static T Deserialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(string file) where T : KeyRecordDictionary
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
    /// Deserializes a dictionary from a stream.
    /// </summary>
    /// <typeparam name="T">The concrete type of the dictionary to deserialize.</typeparam>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="NotSupportedException">The type <typeparamref name="T"/> does not have a public static `Deserialize(Stream)` method.</exception>
    public static T Deserialize<
#if NET5_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
    T>(Stream stream) where T : KeyRecordDictionary
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
    /// When called by a derived class, clears all data from the record stores and additional data fields.
    /// </summary>
    /// <exclude />
    protected void InternalClear()
    {
        additional1_ = [];
        additional2_ = [];
        transientRecords_.Clear();
        persistentRecords_.Clear();
    }

    // 
    // 

    /// <summary>
    /// Specifies the direction for key matching and sorting operations.
    /// </summary>
    public enum SearchDirectionType
    {
        /// <summary>
        /// Left-To-Right search, representing standard lexicographical order.
        /// </summary>
        LTR,
        /// <summary>
        /// Right-To-Left search, useful for suffix matching and reverse lexicographical order.
        /// </summary>
        RTL
    };

    /// <summary>
    /// Provides options to control the serialization process for dictionary data structures.
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
        /// [LOUDS-specific] Gets or sets a value indicating whether to include the parent pointer array in the serialization output.
        /// Defaults to <c>false</c>.
        /// </summary>
        /// <remarks>
        /// Setting this to <c>false</c> will reduce the final file size, but it may significantly increase
        /// the time required for deserialization as the parent structure needs to be rebuilt.
        /// </remarks>
        public bool IncludeLoudsParentPointers { get; set; } = false;

        /// <summary>
        /// Gets a singleton instance of <see cref="SerializationOptions"/> with default values.
        /// </summary>
        /// <remarks>
        /// Use this property to avoid creating a new options object when default settings are sufficient.
        /// </remarks>
        public static SerializationOptions Default { get; } = new();
    }

    /// <summary>
    /// Defines a public contract for a single data record.
    /// </summary>
    public interface IRecord
    {
        /// <summary>
        /// Gets or sets an extra, user-definable byte of metadata associated with this record (0-255).
        /// Not supported by all underlying record stores, only supported by <see cref="PrimitiveRecordStore"/>.
        /// </summary>
        int ExByte { get; set; }
        /// <summary>
        /// Gets or sets the byte array content of this record.
        /// </summary>
        byte[] Content { get; set; }
        /// <summary>
        /// Gets the next record in the linked list, or null if this is the last record.
        /// </summary>
        IRecord? Next { get; }
        /// <summary>
        /// Removes this record from the list it belongs to.
        /// </summary>
        void Remove();
        /// <summary>
        /// Inserts a new record immediately before this record in the list.
        /// </summary>
        /// <param name="content">The content of the new record to insert.</param>
        /// <returns>An <see cref="IRecord"/> handle for the newly inserted record.</returns>
        IRecord InsertBefore(ReadOnlySpan<byte> content);
        /// <summary>
        /// Inserts a new record immediately after this record in the list.
        /// </summary>
        /// <param name="content">The content of the new record to insert.</param>
        /// <returns>An <see cref="IRecord"/> handle for the newly inserted record.</returns>
        IRecord InsertAfter(ReadOnlySpan<byte> content);
    }

    sealed class BasicRecord(BasicRecordStore.Record record) : IRecord
    {
        public int ExByte { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public byte[] Content { get => record.Content; set => record.Content = value; }
        public IRecord? Next
        {
            get
            {
                var next = record.Next;
                return next != null ? new BasicRecord(next) : null;
            }
        }

        public IRecord InsertBefore(ReadOnlySpan<byte> content) => new BasicRecord(record.InsertBefore(content));
        public IRecord InsertAfter(ReadOnlySpan<byte> content) => new BasicRecord(record.InsertAfter(content));
        public void Remove() => record.Remove();
    }

    class PrimitiveRecord(PrimitiveRecordStore.Record record) : IRecord
    {
        public int ExByte { get => record.ExByte; set => record.ExByte = value; }
        public byte[] Content { get => record.Read().ToArray(); set => record.Update(value); }
        public IRecord? Next
        {
            get
            {
                var next = record.Next;
                return next != null ? new PrimitiveRecord(next) : null;
            }
        }

        public IRecord InsertBefore(ReadOnlySpan<byte> content) => new PrimitiveRecord(record.InsertBefore(content));
        public IRecord InsertAfter(ReadOnlySpan<byte> content) => new PrimitiveRecord(record.InsertAfter(content));
        public void Remove() => record.Remove();
    }

    /// <summary>
    /// Defines a public contract for accessing and managing a list of records associated with a single key.
    /// </summary>
    public interface IRecordAccess : IEnumerable<IRecord>
    {
        /// <summary>
        /// Adds a new record with the specified content to the end of the list.
        /// </summary>
        /// <param name="content">The content of the new record.</param>
        /// <returns>An <see cref="IRecord"/> handle for the newly added record.</returns>
        IRecord Add(ReadOnlySpan<byte> content);
        /// <summary>
        /// Removes all records from this list.
        /// </summary>
        void Clear();
    }

    internal class BasicRecordAccess(BasicRecordStore.RecordAccess access) : IRecordAccess
    {
        public IRecord Add(ReadOnlySpan<byte> content) => new BasicRecord(access.Add(content));
        public void Clear() => access.Clear();
        public IEnumerator<IRecord> GetEnumerator() => new Enumerator(access.GetEnumerator());
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        class Enumerator(IEnumerator<BasicRecordStore.Record> enumerator) : IEnumerator<IRecord>
        {
            public IRecord Current { get => new BasicRecord(enumerator.Current); }
            object IEnumerator.Current { get => Current; }

            public void Dispose() => enumerator.Dispose();
            public bool MoveNext() => enumerator.MoveNext();
            public void Reset() => enumerator.Reset();
        }
    }

    internal class PrimitiveRecordAccess(PrimitiveRecordStore.RecordAccess access) : IRecordAccess
    {
        public IRecord Add(ReadOnlySpan<byte> content) => new PrimitiveRecord(access.Add(content));
        public void Clear() => access.Clear();
        public IEnumerator<IRecord> GetEnumerator() => new Enumerator(access.GetEnumerator());
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        class Enumerator(IEnumerator<PrimitiveRecordStore.Record> enumerator) : IEnumerator<IRecord>
        {
            public IRecord Current => new PrimitiveRecord(enumerator.Current);
            object IEnumerator.Current => Current;

            public void Dispose() => enumerator.Dispose();
            public bool MoveNext() => enumerator.MoveNext();
            public void Reset() => enumerator.Reset();
        }
    }

    /// <summary>
    /// Defines the primary public contract for all key-based operations within the dictionary.
    /// </summary>
    public interface IKeyAccess
    {
        /// <summary>
        /// Tries to get the key associated with the specified identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key to get.</param>
        /// <param name="key">When this method returns, contains the key associated with the identifier, if found; otherwise, an empty array.</param>
        /// <returns>true if the key was found; otherwise, false.</returns>
        bool TryGetKey(int identifier, out byte[] key);

        /// <summary>
        /// Gets the key associated with the specified identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key to get.</param>
        /// <returns>The key as a byte array.</returns>
        /// <exception cref="KeyNotFoundException">The specified identifier does not exist.</exception>
        byte[] GetKey(int identifier);

        /// <summary>
        /// Determines whether the dictionary contains the specified key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns>true if the key is found; otherwise, false.</returns>
        bool Contains(ReadOnlySpan<byte> key);

        /// <summary>
        /// Searches for an exact match of the given sequence and returns its identifier.
        /// </summary>
        /// <param name="sequence">The key to search for.</param>
        /// <returns>The identifier for the key if found; otherwise, a value indicating not found (typically 0 or -1).</returns>
        /// <example>
        /// If the dictionary contains the keys "a", "app", and "apple", and the input sequence is "apple",
        /// this method will return a key: "apple".
        /// </example>
        int SearchExactly(ReadOnlySpan<byte> sequence);

        /// <summary>
        /// Finds all keys in the dictionary that are prefixes of the given sequence.
        /// </summary>
        /// <param name="sequence">The sequence to search within.</param>
        /// <returns>An enumerable collection of (identifier, key) tuples for all matching prefixes.</returns>
        /// <example>
        /// If the dictionary contains the keys "a", "app", and "apple", and the input sequence is "applepie",
        /// this method will return all three keys: "a", "app", and "apple".
        /// </example>
        IEnumerable<(int, byte[])> SearchCommonPrefix(ReadOnlySpan<byte> sequence);

        /// <summary>
        /// Finds the longest key in the dictionary that is a prefix of the given sequence.
        /// </summary>
        /// <param name="sequence">The sequence to search within.</param>
        /// <returns>A tuple containing the identifier and key of the longest matching prefix, or a default value if no match is found.</returns>
        /// <example>
        /// If the dictionary contains the keys "a", "app", and "apple", and the input sequence is "applepie",
        /// this method will return a key: "apple".
        /// </example>
        (int, byte[]) SearchLongestPrefix(ReadOnlySpan<byte> sequence);

        /// <summary>
        /// Finds all keys that start with the given prefix.
        /// </summary>
        /// <param name="sequence">The prefix to search for.</param>
        /// <param name="reverse">If true, returns results in reverse lexicographical order.</param>
        /// <returns>An enumerable collection of (identifier, key) tuples for all keys starting with the prefix.</returns>
        /// <example>
        /// If the dictionary contains the keys "a", "app", and "apple", and the input sequence is "ap",
        /// this method will return two keys: "app" and "apple".
        /// </example>
        IEnumerable<(int, byte[])> SearchByPrefix(ReadOnlySpan<byte> sequence, bool reverse = false);

        /// <summary>
        /// Enumerates all keys in the dictionary.
        /// </summary>
        /// <param name="reverse">If true, returns keys in reverse lexicographical order.</param>
        /// <returns>An enumerable collection of all (identifier, key) tuples.</returns>
        IEnumerable<(int, byte[])> EnumerateAll(bool reverse = false);

        /// <summary>
        /// Performs a wildcard search for keys matching a pattern.
        /// </summary>
        /// <param name="sequence">The byte sequence of the pattern.</param>
        /// <param name="cards">A sequence of characters ('?' for single, '*' for multiple wildcards) corresponding to the pattern.</param>
        /// <param name="reverse">If true, returns results in reverse order.</param>
        /// <returns>An enumerable collection of matching (identifier, key) tuples.</returns>
        /// <example>
        /// If the dictionary contains the keys "a", "app", and "apple", and the input sequence is "a?p*",
        /// this method will return two keys: "app" and "apple".
        /// The search pattern is provided as two separate arguments (`sequence` and `cards`) to unambiguously support searching for any possible byte value (0-255).
        /// The `sequence` span contains the literal byte values for the pattern, while the `cards` span defines the role of each corresponding position.
        /// This design allows a search to include literal byte values that might otherwise be interpreted as wildcard characters (e.g., the byte value 63, which is the ASCII code for '?').
        /// </example>
        IEnumerable<(int, byte[])> SearchWildcard(ReadOnlySpan<byte> sequence, ReadOnlySpan<char> cards, bool reverse = false);

        /// <summary>
        /// Finds the first key in the dictionary according to the current sort order.
        /// </summary>
        /// <param name="identifier">When this method returns, the identifier of the first key.</param>
        /// <param name="key">When this method returns, the first key.</param>
        /// <returns>true if the dictionary is not empty; otherwise, false.</returns>
        bool FindFirst(out int identifier, out byte[] key);

        /// <summary>
        /// Finds the last key in the dictionary according to the current sort order.
        /// </summary>
        /// <param name="identifier">When this method returns, the identifier of the last key.</param>
        /// <param name="key">When this method returns, the last key.</param>
        /// <returns>true if the dictionary is not empty; otherwise, false.</returns>
        bool FindLast(out int identifier, out byte[] key);

        /// <summary>
        /// Finds the next key in sequence after the specified identifier.
        /// </summary>
        /// <param name="currentIdentifier">The identifier to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the next key.</param>
        /// <param name="foundKey">When this method returns, the next key.</param>
        /// <returns>true if a next key was found; otherwise, false.</returns>
        bool FindNext(int currentIdentifier, out int foundIdentifier, out byte[] foundKey);

        /// <summary>
        /// Finds the next key in sequence after the specified key.
        /// </summary>
        /// <param name="currentKey">The key to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the next key.</param>
        /// <param name="foundKey">When this method returns, the next key.</param>
        /// <returns>true if a next key was found; otherwise, false.</returns>
        bool FindNext(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey);

        /// <summary>
        /// Finds the previous key in sequence before the specified identifier.
        /// </summary>
        /// <param name="currentIdentifier">The identifier to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the previous key.</param>
        /// <param name="foundKey">When this method returns, the previous key.</param>
        /// <returns>true if a previous key was found; otherwise, false.</returns>
        bool FindPrevious(int currentIdentifier, out int foundIdentifier, out byte[] foundKey);

        /// <summary>
        /// Finds the previous key in sequence before the specified key.
        /// </summary>
        /// <param name="currentKey">The key to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the previous key.</param>
        /// <param name="foundKey">When this method returns, the previous key.</param>
        /// <returns>true if a previous key was found; otherwise, false.</returns>
        bool FindPrevious(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey);

        /// <summary>
        /// Adds a key to the dictionary. If the key already exists, its existing identifier is returned.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <returns>The identifier for the added or existing key.</returns>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        int Add(ReadOnlySpan<byte> key);

        /// <summary>
        /// Tries to add a key to the dictionary.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="identifier">When this method returns, contains the identifier for the new key. If the key already existed, this will be the existing identifier.</param>
        /// <returns>true if the key was newly added; false if the key already existed.</returns>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        bool TryAdd(ReadOnlySpan<byte> key, out int identifier);

        /// <summary>
        /// Removes a key from the dictionary using its identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key to remove.</param>
        /// <returns>true if the key was found and removed; otherwise, false.</returns>
        bool Remove(int identifier);

        /// <summary>
        /// Removes a key from the dictionary.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>true if the key was found and removed; otherwise, false.</returns>
        bool Remove(ReadOnlySpan<byte> key);

        /// <summary>
        /// Returns a string-specialized wrapper for this key access interface.
        /// </summary>
        /// <param name="encoding">The encoding to use for string operations. Defaults to UTF-8.</param>
        /// <returns>A new <see cref="StringSpecialized"/> instance.</returns>
        StringSpecialized AsStringSpecialized(Encoding? encoding = null);

        /// <summary>
        /// Gets an accessor for the list of records associated with a given key identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key whose records are to be accessed.</param>
        /// <param name="isTransient">If true, accesses the transient record store; otherwise, accesses the persistent record store.</param>
        /// <returns>An <see cref="IRecordAccess"/> handle for the specified record list.</returns>
        IRecordAccess GetRecordAccess(int identifier, bool isTransient = false);
    }

    /// <exclude />
    public interface IAltKeyAccess
    {
        bool TryGetKey(int identifier, out byte[] key);
        byte[] GetKey(int identifier);
        bool Contains(ReadOnlySpan<byte> key);
        int SearchExactly(ReadOnlySpan<byte> sequence);
        IEnumerable<(int, byte[])> SearchCommonPrefix(ReadOnlySpan<byte> sequence);
        (int, byte[]) SearchLongestPrefix(ReadOnlySpan<byte> sequence);
        IEnumerable<(int, byte[])> SearchByPrefix(ReadOnlySpan<byte> sequence, bool reverse = false);
        IEnumerable<(int, byte[])> EnumerateAll(bool reverse = false);
        IEnumerable<(int, byte[])> SearchWildcard(ReadOnlySpan<byte> sequence, ReadOnlySpan<char> cards, bool reverse = false);
        bool FindFirst(out int identifier, out byte[] key);
        bool FindLast(out int identifier, out byte[] key);
        bool FindNext(int currentIdentifier, out int foundIdentifier, out byte[] foundKey);
        bool FindNext(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey);
        bool FindPrevious(int currentIdentifier, out int foundIdentifier, out byte[] foundKey);
        bool FindPrevious(ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey);
        int Add(ReadOnlySpan<byte> key);
        bool TryAdd(ReadOnlySpan<byte> key, out int identifier);
        bool Remove(ReadOnlySpan<byte> key);
    }

    /// <summary>
    /// Provides helper methods for Left-To-Right (standard) key comparisons.
    /// </summary>
    /// <exclude />
    protected static class Ltr
    {
        /// <summary>
        /// Gets a comparer for standard lexicographical sorting of byte arrays.
        /// </summary>
        public static readonly KeySortComparer SortComparer = new();

        /// <summary>
        /// Compares two byte arrays using standard lexicographical order.
        /// </summary>
        public class KeySortComparer : IComparer<byte[]>
        {
            public int Compare(byte[]? x, byte[]? y)
            {
                if (x == null && y == null)
                    return 0;
                else if (x == null && y != null)
                    return -1;
                else if (x != null && y == null)
                    return 1;
                else
                {
                    var length = Math.Min(x!.Length, y!.Length);
                    for (var i = 0; i < length; i++)
                        if (x[i] != y[i])
                            return x[i] - y[i];
                    return x.Length == y.Length ? 0 : (x.Length < y.Length ? -1 : 1);
                }
            }
        }
    }

    /// <summary>
    /// Provides helper methods for Right-To-Left (reverse) key comparisons and manipulations.
    /// </summary>
    /// <exclude />
    protected static class Rtl
    {
        static readonly ArrayPool<byte> bytePool_ = ArrayPool<byte>.Shared;

        public static T[] Reverse<T>(IEnumerable<T> key)
        => [.. key.Reverse()];

        public static Span<T> Reverse<T>(Span<T> buffer, ReadOnlySpan<T> key)
        {
            for (var i = 0; i < key.Length; i++)
                buffer[i] = key[^(i + 1)];
            return buffer[..key.Length];
        }

        public static int ImplAdd(IAltKeyAccess access, ReadOnlySpan<byte> key)
        {
            var rent = bytePool_.Rent(key.Length);
            try
            {
                return access.Add(Reverse(rent.AsSpan(0, key.Length), key));
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static bool ImplTryAdd(IAltKeyAccess access, ReadOnlySpan<byte> key, out int identifier)
        {
            var rent = bytePool_.Rent(key.Length);
            try
            {
                return access.TryAdd(Reverse(rent.AsSpan(0, key.Length), key), out identifier);
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static bool ImplRemove(IAltKeyAccess access, ReadOnlySpan<byte> key)
        {
            var rent = bytePool_.Rent(key.Length);
            try
            {
                return access.Remove(Reverse(rent.AsSpan(0, key.Length), key));
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static bool ImplTryGetKey(IAltKeyAccess access, int identifier, out byte[] key)
        {
            if (access.TryGetKey(identifier, out key))
            {
                key = Reverse(key);
                return true;
            }
            return false;
        }

        public static byte[] ImplGetKey(IAltKeyAccess access, int identifier)
        => Reverse(access.GetKey(identifier));

        public static bool ImplContains(IAltKeyAccess access, ReadOnlySpan<byte> key)
        {
            var rent = bytePool_.Rent(key.Length);
            try
            {
                return access.Contains(Reverse(rent.AsSpan(0, key.Length), key));
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static int ImplSearchExactly(IAltKeyAccess access, ReadOnlySpan<byte> sequence)
        {
            var rent = bytePool_.Rent(sequence.Length);
            try
            {
                return access.SearchExactly(Reverse(rent.AsSpan(0, sequence.Length), sequence));
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static IEnumerable<(int, byte[])> ImplSearchCommonPrefix(IAltKeyAccess access, ReadOnlySpan<byte> sequence)
        {
            return __Internal(sequence.ToArray());

            #region @@
            IEnumerable<(int, byte[])> __Internal(byte[] sequence)
            {
                var rent = bytePool_.Rent(sequence.Length);
                try
                {
                    foreach (var (identifier, key) in access.SearchCommonPrefix(Reverse(rent.AsSpan(0, sequence.Length), sequence)))
                        yield return (identifier, Reverse(key));
                }
                finally
                {
                    bytePool_.Return(rent);
                }
            }
            #endregion
        }

        public static (int, byte[]) ImplSearchLongestPrefix(IAltKeyAccess access, ReadOnlySpan<byte> sequence)
        {
            var rent = bytePool_.Rent(sequence.Length);
            try
            {
                var (identifier, key) = access.SearchLongestPrefix(Reverse(rent.AsSpan(0, sequence.Length), sequence));
                return (identifier, Reverse(key));
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static IEnumerable<(int, byte[])> ImplSearchByPrefix(IAltKeyAccess access, ReadOnlySpan<byte> sequence, bool reverse = false)
        {
            return __Internal(sequence.ToArray(), reverse);

            #region @@
            IEnumerable<(int, byte[])> __Internal(byte[] sequence, bool reverse)
            {
                var rent = bytePool_.Rent(sequence.Length);
                try
                {
                    foreach (var (identifier, key) in access.SearchByPrefix(Reverse(rent.AsSpan(0, sequence.Length), sequence), reverse))
                        yield return (identifier, Reverse(key));
                }
                finally
                {
                    bytePool_.Return(rent);
                }
            }
            #endregion
        }

        public static IEnumerable<(int, byte[])> ImplEnumerateAll(IAltKeyAccess access, bool reverse = false)
        {
            foreach (var (identifier, key) in access.EnumerateAll(reverse))
                yield return (identifier, Reverse(key));
        }

        public static IEnumerable<(int, byte[])> ImplSearchWildcard(IAltKeyAccess access, ReadOnlySpan<byte> sequence, ReadOnlySpan<char> cards, bool reverse = false)
        {
            return __Internal(sequence.ToArray(), cards.ToArray(), reverse);

            #region @@
            IEnumerable<(int, byte[])> __Internal(byte[] sequence, char[] cards, bool reverse)
            {
                var rent = bytePool_.Rent(sequence.Length);
                try
                {
                    foreach (var (identifier, key) in access.SearchWildcard(Reverse(rent.AsSpan(0, sequence.Length), sequence), Reverse(cards), reverse))
                        yield return (identifier, Reverse(key));
                }
                finally
                {
                    bytePool_.Return(rent);
                }
            }
            #endregion
        }

        public static bool ImplFindFirst(IAltKeyAccess access, out int identifier, out byte[] key)
        {
            if (access.FindFirst(out identifier, out key))
            {
                key = Reverse(key);
                return true;
            }
            return false;
        }

        public static bool ImplFindLast(IAltKeyAccess access, out int identifier, out byte[] key)
        {
            if (access.FindLast(out identifier, out key))
            {
                key = Reverse(key);
                return true;
            }
            return false;
        }

        public static bool ImplFindNext(IAltKeyAccess access, int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
        {
            if (access.FindNext(currentIdentifier, out foundIdentifier, out foundKey))
            {
                foundKey = Reverse(foundKey);
                return true;
            }
            return false;
        }

        public static bool ImplFindNext(IAltKeyAccess access, ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
        {
            var rent = bytePool_.Rent(currentKey.Length);
            try
            {
                if (access.FindNext(Reverse(rent.AsSpan(0, currentKey.Length), currentKey), out foundIdentifier, out foundKey))
                {
                    foundKey = Reverse(foundKey);
                    return true;
                }
                return false;
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        public static bool ImplFindPrevious(IAltKeyAccess access, int currentIdentifier, out int foundIdentifier, out byte[] foundKey)
        {
            if (access.FindPrevious(currentIdentifier, out foundIdentifier, out foundKey))
            {
                foundKey = Reverse(foundKey);
                return true;
            }
            return false;
        }

        public static bool ImplFindPrevious(IAltKeyAccess access, ReadOnlySpan<byte> currentKey, out int foundIdentifier, out byte[] foundKey)
        {
            var rent = bytePool_.Rent(currentKey.Length);
            try
            {
                if (access.FindPrevious(Reverse(rent.AsSpan(0, currentKey.Length), currentKey), out foundIdentifier, out foundKey))
                {
                    foundKey = Reverse(foundKey);
                    return true;
                }
                return false;
            }
            finally
            {
                bytePool_.Return(rent);
            }
        }

        // 
        // 

        /// <summary>
        /// Compares two byte arrays using reverse lexicographical order.
        /// </summary>
        public class KeySortComparer : IComparer<byte[]>
        {
            public int Compare(byte[]? x, byte[]? y)
            {
                if (x == null && y == null)
                    return 0;
                else if (x == null && y != null)
                    return -1;
                else if (x != null && y == null)
                    return 1;
                else
                {
                    var length = Math.Min(x!.Length, y!.Length);
                    for (var i = 0; i < length; i++)
                        if (x[^(i + 1)] != y[^(i + 1)])
                            return x[^(i + 1)] - y[^(i + 1)];
                    return x.Length == y.Length ? 0 : (x.Length < y.Length ? -1 : 1);
                }
            }
        }
    }

    /// <summary>
    /// Provides a string-specialized wrapper around an <see cref="IKeyAccess"/> instance, simplifying operations by handling string-to-byte encoding and decoding.
    /// </summary>
    public sealed class StringSpecialized
    {
        static readonly ArrayPool<byte> bytePool_ = ArrayPool<byte>.Shared;
        readonly int charByteLength_;

        /// <summary>
        /// Gets the underlying <see cref="IKeyAccess"/> dictionary.
        /// </summary>
        public IKeyAccess Dictionary { get; }
        /// <summary>
        /// Gets the <see cref="System.Text.Encoding"/> used for string conversions.
        /// </summary>
        public Encoding Encoding { get; }

        // 
        // 

        internal StringSpecialized(IKeyAccess dictionary, Encoding encoding)
        {
            this.Dictionary = dictionary;
            this.Encoding = encoding;
            charByteLength_ = encoding.CodePage switch { 1200 => 2, 1201 => 2, 1252 => 1, 12000 => 4, 20127 => 1, 28591 => 1, _ => 0 };
        }

        /// <summary>
        /// Adds a key to the dictionary. If the key already exists, its existing identifier is returned.
        /// </summary>
        /// <param name="key">The string key to add.</param>
        /// <returns>The identifier for the added or existing key.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public int Add(string key)
        => this.Dictionary.Add(ToBytes(key));

        /// <summary>
        /// Tries to add a key to the dictionary.
        /// </summary>
        /// <param name="key">The string key to add.</param>
        /// <param name="identifier">When this method returns, contains the identifier for the new key. If the key already existed, this will be the existing identifier.</param>
        /// <returns>true if the key was newly added; false if the key already existed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public bool TryAdd(string key, out int identifier)
        => this.Dictionary.TryAdd(ToBytes(key), out identifier);

        /// <summary>
        /// Determines whether the dictionary contains the specified key.
        /// </summary>
        /// <param name="key">The string key to locate.</param>
        /// <returns>true if the key is found; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool Contains(string key)
        => this.Dictionary.Contains(ToBytes(key));

        /// <summary>
        /// Removes a key from the dictionary.
        /// </summary>
        /// <param name="key">The string key to remove.</param>
        /// <returns>true if the key was found and removed; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool Remove(string key)
        => this.Dictionary.Remove(ToBytes(key));

        /// <summary>
        /// Tries to get the key associated with the specified identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key to get.</param>
        /// <param name="key">When this method returns, contains the key as a string, if found; otherwise, an empty string.</param>
        /// <returns>true if the key was found; otherwise, false.</returns>
        public bool TryGetKey(int identifier, out string key)
        {
            key = "";
            if (this.Dictionary.TryGetKey(identifier, out var bytes))
            {
                key = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the key associated with the specified identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key to get.</param>
        /// <returns>The key as a string.</returns>
        public string GetKey(int identifier)
        => this.Encoding.GetString(this.Dictionary.GetKey(identifier));

        /// <summary>
        /// Searches for an exact match of the given text and returns its identifier.
        /// </summary>
        /// <param name="text">The string key to search for.</param>
        /// <returns>The identifier for the key if found; otherwise, a value indicating not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public int SearchExactly(string text)
        => this.Dictionary.SearchExactly(ToBytes(text));

        /// <summary>
        /// Finds all keys in the dictionary that are prefixes of the given text.
        /// </summary>
        /// <param name="text">The text to search within.</param>
        /// <returns>An enumerable collection of (identifier, key) string tuples for all matching prefixes.</returns>
        /// <remarks>
        /// This is the string-specialized version of this method.
        /// For detailed behavior and examples, see <see cref="IKeyAccess.SearchCommonPrefix(ReadOnlySpan{byte})"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public IEnumerable<(int, string)> SearchCommonPrefix(string text)
        {
            foreach (var (identifier, key) in this.Dictionary.SearchCommonPrefix(ToBytes(text)))
                yield return (identifier, this.Encoding.GetString(key));
        }

        /// <summary>
        /// Finds the longest key in the dictionary that is a prefix of the given text.
        /// </summary>
        /// <param name="text">The text to search within.</param>
        /// <returns>A tuple containing the identifier and string key of the longest matching prefix, or (-1, "") if no match is found.</returns>
        /// <remarks>
        /// This is the string-specialized version of this method.
        /// For detailed behavior and examples, see <see cref="IKeyAccess.SearchLongestPrefix(ReadOnlySpan{byte})"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public (int, string) SearchLongestPrefix(string text)
        {
            var (identifier, key) = this.Dictionary.SearchLongestPrefix(ToBytes(text));
            return (identifier, this.Encoding.GetString(key));
        }

        /// <summary>
        /// Finds all keys that start with the given prefix.
        /// </summary>
        /// <param name="text">The prefix to search for.</param>
        /// <param name="reverse">If true, returns results in reverse order.</param>
        /// <returns>An enumerable collection of (identifier, key) string tuples.</returns>
        /// <remarks>
        /// This is the string-specialized version of this method.
        /// For detailed behavior and examples, see <see cref="IKeyAccess.SearchByPrefix(ReadOnlySpan{byte}, bool)"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public IEnumerable<(int, string)> SearchByPrefix(string text, bool reverse = false)
        {
            foreach (var (identifier, key) in this.Dictionary.SearchByPrefix(ToBytes(text), reverse))
                yield return (identifier, this.Encoding.GetString(key));
        }

        /// <summary>
        /// Enumerates all keys in the dictionary as strings.
        /// </summary>
        /// <param name="reverse">If true, returns keys in reverse order.</param>
        /// <returns>An enumerable collection of all (identifier, key) string tuples.</returns>
        public IEnumerable<(int, string)> EnumerateAll(bool reverse = false)
        {
            foreach (var (identifier, key) in this.Dictionary.EnumerateAll(reverse))
                yield return (identifier, this.Encoding.GetString(key));
        }

        /// <summary>
        /// Performs a wildcard search using user-defined wildcard characters.
        /// </summary>
        /// <param name="pattern">The search pattern.</param>
        /// <param name="cardQ">The character to be treated as a single-character wildcard ('?'), which matches any single character.</param>
        /// <param name="cardA">The character to be treated as a multi-character wildcard ('*'), which matches any sequence of zero or more characters.</param>
        /// <param name="reverse">If true, returns results in reverse order.</param>
        /// <returns>An enumerable collection of matching (identifier, key) string tuples.</returns>
        /// <remarks>
        /// This is a convenience method that provides a simple way to perform wildcard searches.
        /// It internally translates the pattern for use by the more advanced <see cref="SearchWildcard(string, string, bool)"/> overload.
        /// <example>
        /// A call like <code>SearchWildcard("Hell?*World")</code> is internally converted to an equivalent call:
        /// <code>SearchWildcard("Hell?*World", "....?*.....")</code>.
        /// The second argument, "cards", specifies that '?' and '*' should be treated as wildcards, while '.' indicates a literal character match.
        /// </example>
        /// <para>
        /// <strong>Encoding Limitation:</strong> The multi-character wildcard ('*') is only fully supported for single-byte encodings (codepage 1252, 20127 and 28591).
        /// The single-character wildcard ('?') is supported for some encodings (codepage 1200, 1201, 1252, 12000, 20127 and 28591).
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null.</exception>
        /// <exception cref="NotSupportedException">The current encoding is not supported for wildcard searches.</exception>
        public IEnumerable<(int, string)> SearchWildcard(string pattern, char cardQ = '?', char cardA = '*', bool reverse = false)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(pattern);
#else
            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));
#endif
            if (cardQ == cardA)
                throw new ArgumentException($"2 cards are equal.");

            var cards = pattern.ToCharArray();
            for (var i = 0; i < cards.Length; i++)
                if (cards[i] == cardQ)
                    cards[i] = '?';
                else if (cards[i] == cardA)
                    cards[i] = '*';
                else
                    cards[i] = '.';
            return SearchWildcard(pattern, new string(cards), reverse);
        }

        /// <summary>
        /// Performs a wildcard search using a standardized wildcard pattern.
        /// </summary>
        /// <param name="text">The search pattern as a string.</param>
        /// <param name="cards">A string of the same length as <paramref name="text"/>'s byte representation, where '?' is a single wildcard, '*' is a multiple wildcard, and '.' is a literal match.</param>
        /// <param name="reverse">If true, returns results in reverse order.</param>
        /// <returns>An enumerable collection of matching (identifier, key) string tuples.</returns>
        /// <remarks>
        /// This is the string-specialized version of this method.
        /// For detailed behavior and examples, see <see cref="IKeyAccess.SearchWildcard(ReadOnlySpan{byte}, ReadOnlySpan{char}, bool)"/>.
        /// <para>
        /// <strong>Encoding Limitation:</strong> The multi-character wildcard ('*') is only fully supported for single-byte encodings (codepage 1252, 20127 and 28591).
        /// The single-character wildcard ('?') is supported for some encodings (codepage 1200, 1201, 1252, 12000, 20127 and 28591).
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="cards"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="cards"/> and <paramref name="cards"/> are different length.</exception>
        /// <exception cref="NotSupportedException">The current encoding is not supported for wildcard searches.</exception>
        public IEnumerable<(int, string)> SearchWildcard(string text, string cards, bool reverse = false)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(cards);
#else
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));
#endif
            if (text.Length != cards.Length)
                throw new ArgumentException($"Length of {nameof(text)} must be equal {nameof(cards)}.");

            var bytes = ToBytes(text);
            switch (charByteLength_)
            {
                case 1:
                    foreach (var (identifier, key) in this.Dictionary.SearchWildcard(bytes, cards, reverse))
                        yield return (identifier, this.Encoding.GetString(key));
                    break;
                case 2:
                case 4:
                    {
                        if (cards.Contains('*'))
                            throw new ArgumentException($"Current encoding does not supported '*'.");

                        var cardsNew = new char[bytes.Length];
                        Array.Fill(cardsNew, '.');
                        for (var i = 0; i < text.Length; i++)
                            if (text[i] == '?')
                                Array.Fill(cardsNew, '?', i * charByteLength_, charByteLength_);
                        foreach (var (identifier, key) in this.Dictionary.SearchWildcard(bytes, cardsNew, reverse))
                            yield return (identifier, this.Encoding.GetString(key));
                    }
                    break;
                default:
                    throw new NotSupportedException($"Current encoding does not supported.");
            }
        }

        /// <summary>
        /// Finds the first key in the dictionary according to the current sort order.
        /// </summary>
        /// <param name="identifier">When this method returns, the identifier of the first key.</param>
        /// <param name="key">When this method returns, the first key as a string.</param>
        /// <returns>true if the dictionary is not empty; otherwise, false.</returns>
        public bool FindFirst(out int identifier, out string key)
        {
            key = "";
            if (this.Dictionary.FindFirst(out identifier, out var bytes))
            {
                key = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the last key in the dictionary according to the current sort order.
        /// </summary>
        /// <param name="identifier">When this method returns, the identifier of the last key.</param>
        /// <param name="key">When this method returns, the last key as a string.</param>
        /// <returns>true if the dictionary is not empty; otherwise, false.</returns>
        public bool FindLast(out int identifier, out string key)
        {
            key = "";
            if (this.Dictionary.FindLast(out identifier, out var bytes))
            {
                key = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the next key in sequence after the specified identifier.
        /// </summary>
        /// <param name="currentIdentifier">The identifier to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the next key.</param>
        /// <param name="foundKey">When this method returns, the next key as a string.</param>
        /// <returns>true if a next key was found; otherwise, false.</returns>
        public bool FindNext(int currentIdentifier, out int foundIdentifier, out string foundKey)
        {
            foundKey = "";
            if (this.Dictionary.FindNext(currentIdentifier, out foundIdentifier, out var bytes))
            {
                foundKey = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the next key in sequence after the specified key.
        /// </summary>
        /// <param name="currentKey">The string key to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the next key.</param>
        /// <param name="foundKey">When this method returns, the next key as a string.</param>
        /// <returns>true if a next key was found; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="currentKey"/> is null.</exception>
        public bool FindNext(string currentKey, out int foundIdentifier, out string foundKey)
        {
            foundKey = "";
            if (this.Dictionary.FindNext(ToBytes(currentKey), out foundIdentifier, out var bytes))
            {
                foundKey = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the previous key in sequence before the specified identifier.
        /// </summary>
        /// <param name="currentIdentifier">The identifier to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the previous key.</param>
        /// <param name="foundKey">When this method returns, the previous key as a string.</param>
        /// <returns>true if a previous key was found; otherwise, false.</returns>
        public bool FindPrevious(int currentIdentifier, out int foundIdentifier, out string foundKey)
        {
            foundKey = "";
            if (this.Dictionary.FindPrevious(currentIdentifier, out foundIdentifier, out var bytes))
            {
                foundKey = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the previous key in sequence before the specified key.
        /// </summary>
        /// <param name="currentKey">The string key to start the search from.</param>
        /// <param name="foundIdentifier">When this method returns, the identifier of the previous key.</param>
        /// <param name="foundKey">When this method returns, the previous key as a string.</param>
        /// <returns>true if a previous key was found; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="currentKey"/> is null.</exception>
        public bool FindPrevious(string currentKey, out int foundIdentifier, out string foundKey)
        {
            foundKey = "";
            if (this.Dictionary.FindPrevious(ToBytes(currentKey), out foundIdentifier, out var bytes))
            {
                foundKey = this.Encoding.GetString(bytes);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets an accessor for the list of records associated with a given key identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the key whose records are to be accessed.</param>
        /// <param name="isTransient">If true, accesses the transient record store; otherwise, accesses the persistent record store.</param>
        /// <returns>An <see cref="IRecordAccess"/> handle for the specified record list.</returns>
        public IRecordAccess GetRecordAccess(int identifier, bool isTransient = false)
        => this.Dictionary.GetRecordAccess(identifier, isTransient);

        // public static implicit operator KeyRecordDictionary(StringSpecialized obj) => obj.Dictionary as KeyRecordDictionary ?? throw new InvalidCastException("Bad cast.");

        byte[] ToBytes(string text)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(text);
#else
            if (text == null)
                throw new ArgumentNullException(nameof(text));
#endif

            if (text.Length == 0)
                return [];

            var count = this.Encoding.GetMaxByteCount(text.Length);
            var buffer = bytePool_.Rent(count);
            try
            {
                var length = this.Encoding.GetBytes(text, buffer);
                return buffer[..length];
            }
            finally
            {
                bytePool_.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Provides an implementation of <see cref="IEqualityComparer{T}"/> for byte arrays.
    /// </summary>
    /// <exclude />
    protected class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        /// <summary>
        /// Gets a singleton instance of the <see cref="ByteArrayComparer"/>.
        /// </summary>
        public static ByteArrayComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y)
        => (x, y) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            _ => x.SequenceEqual(y),
        };

#if !NETSTANDARD2_0
        public int GetHashCode([DisallowNull] byte[] obj)
#else
        public int GetHashCode(byte[] obj)
#endif
        {
            var code = 17;
            foreach (var value in obj)
                code = unchecked(code * 32 + value);
            return code;
        }
    }
}
