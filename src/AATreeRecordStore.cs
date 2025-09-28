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
/// A concrete implementation of <see cref="BinaryTreeRecordStore"/> that uses a self-balancing AA tree.
/// </summary>
/// <remarks>
/// This class uses an AA tree, a variation of a Red-Black tree, to maintain balance.
/// It simplifies the rebalancing logic by using two core operations, Skew and Split, to handle insertions and deletions efficiently.
/// <para>
/// <strong>Serialization Limits:</strong> The binary serialization format for this class imposes certain constraints on the data.
/// Exceeding these limits will result in an <see cref="InvalidDataException"/> during serialization.
/// <list type="bullet">
/// <item><description><strong>Max Records per List:</strong> A single identifier can have a maximum of 16,777,215 records in its linked list.</description></item>
/// <item><description><strong>Max Record Content Size:</strong> The <c>Content</c> byte array of any single record cannot exceed 65,535 bytes.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class AATreeRecordStore : BinaryTreeRecordStore, ICloneable
{
    /// <summary>
    /// Serializes the entire state of the tree store into a stream.
    /// </summary>
    /// <param name="store">The <see cref="AATreeRecordStore"/> instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="BasicRecordStore.SerializationOptions.Default"/> will be used.</param>
    /// <remarks>
    /// The serialization format is specific to this tree implementation, using Brotli compression and an XxHash32 checksum for data integrity.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(AATreeRecordStore store, Stream stream, SerializationOptions? options = null)
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
            var buffer1 = new byte[16];
            foreach (var node0 in store.Traverse())
            {
                count++;

                var node1 = (Node1)node0;
                var records = __Records(node1.Data);
                if (records.Length > 0x00FFFFFF)
                    throw new InvalidDataException("Too many records.");
                if (records.Any(n => n.Content.Length > 0x0000FFFF))
                    throw new InvalidDataException("Too large record data.");

                //  0: int * 1, current identifier
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, node1.Identifier);
                //  4: int * 1, left identiifier
                BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(4), node1.Left?.Identifier ?? Int32.MinValue);
                //  8: int * 1, right identifier
                BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(8), node1.Right?.Identifier ?? Int32.MinValue);
                // 12: uint * 1, level << 24 | count of data
                BinaryPrimitives.WriteUInt32LittleEndian(buffer1.AsSpan(12), (uint)(node1.Level << 24) | (uint)records.Length);
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
        //  0: byte * 4, AAT1
        "AAT1"u8.CopyTo(buffer0);// [0x41, 0x41, 0x54, 0x31]
        //  4: uint * 1, xxhash
        xxhc.CopyTo(buffer0.AsSpan(4));
        //  8: int * 1, key total bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(8), array1.Length);
        // 12: int * 1, record total bytes
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(12), array2.Length);
        // 16: int * 1, root identifier
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(16), store.Root?.Identifier ?? Int32.MinValue);
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
    /// Deserializes an <see cref="AATreeRecordStore"/> from a stream.
    /// </summary>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <see cref="AATreeRecordStore"/> reconstructed from the stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream data is corrupted, in an unsupported format, or contains invalid values.</exception>
    public static AATreeRecordStore Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[64];
        stream.ReadExactly(buffer0, 0, buffer0.Length);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("AAT1"u8))// [0x41, 0x41, 0x54, 0x31]
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

        var dictionary = new Dictionary<int, (Node1, int, int)>();
        using var memoryStream1 = new MemoryStream(array1);
        using var memoryStream2 = new MemoryStream(array2);
        using var decompressStream1 = new BrotliStream(memoryStream1, CompressionMode.Decompress);
        using var decompressStream2 = new BrotliStream(memoryStream2, CompressionMode.Decompress);

        var buffer1 = new byte[16];
        var buffer2 = new byte[65538];
        while (count-- != 0)
        {
            decompressStream1.ReadExactly(buffer1);

            var currentIdentifier = BinaryPrimitives.ReadInt32LittleEndian(buffer1);
            var leftIdentifier = BinaryPrimitives.ReadInt32LittleEndian(buffer1.AsSpan(4));
            var rightIdentifier = BinaryPrimitives.ReadInt32LittleEndian(buffer1.AsSpan(8));
            var tmp = BinaryPrimitives.ReadUInt32LittleEndian(buffer1.AsSpan(12));
            var level = (int)((tmp & 0xFF000000u) >> 24);
            var loop = (int)(tmp & 0x00FFFFFFu);
            var node = new Node1(currentIdentifier) { Level = level };
            Record? record1 = null;
            while (loop-- != 0)
            {
                decompressStream2.ReadExactly(buffer2.AsSpan(0, 2));
                var contentSize = (int)BinaryPrimitives.ReadUInt16LittleEndian(buffer2);
                decompressStream2.ReadExactly(buffer2.AsSpan(2, contentSize));
                var record2 = new Record(null!) { Content = buffer2[2..(2 + contentSize)] };
                if (node.Data == null)
                    node.Data = record1 = record2;
                else
                    record1 = record1!.Next = record2;
            }
            dictionary.Add(currentIdentifier, (node, leftIdentifier, rightIdentifier));
        }

        var store = new AATreeRecordStore();
        var rootIdentifier = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(16));
        if (rootIdentifier != Int32.MinValue)
        {
            foreach (var entry in dictionary)
            {
                var (node, left, right) = entry.Value;
                if (left != Int32.MinValue)
                    node.Left = dictionary[left].Item1;
                if (right != Int32.MinValue)
                    node.Right = dictionary[right].Item1;
            }
            store.Root = dictionary[rootIdentifier].Item1;
        }
        return store;
    }

    /// <summary>
    /// Creates a deep copy of the <see cref="AATreeRecordStore"/>.
    /// </summary>
    /// <returns>A new <see cref="AATreeRecordStore"/> instance with the same structure and record data as the original.</returns>
    /// <remarks>The method creates a new tree and copies all records, ensuring that the new store is independent of the original.</remarks>
    public object Clone()
    {
        var clone = new AATreeRecordStore();
        foreach (var (identifier, access1) in Enumerate())
        {
            var access2 = clone.GetRecordAccess(identifier);
            foreach (var record1 in access1)
                access2.Add([.. record1.Content]);
        }
        return clone;
    }

    /// <summary>
    /// Adds a new node with the specified identifier to the tree, performing Skew and Split operations to maintain the tree's balance.
    /// </summary>
    /// <param name="identifier">The identifier for the new node.</param>
    /// <returns>The found or newly created node.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identifier"/> is <see cref="Int32.MinValue"/>.</exception>
    /// <exclude />
    protected override Node0 InternalAdd(int identifier)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfEqual(identifier, Int32.MinValue);
#else
        if (identifier == Int32.MinValue)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must not be equal {Int32.MinValue}.");
#endif

        Node0? found = null;
        this.Root = __Add(this.Root, identifier, ref found);
        return found!;

        #region @@
        Node0 __Add(Node0? current, int identifier, ref Node0? found)
        {
            if (current == null)
                return found = new Node1(identifier);

            if (identifier == current.Identifier)
                return found = current;
            else if (identifier < current.Identifier)
                current.Left = __Add(current.Left!, identifier, ref found);
            else if (identifier > current.Identifier)
                current.Right = __Add(current.Right!, identifier, ref found);

            return Split(Skew(current))!;
        }
        #endregion
    }

    /// <summary>
    /// Removes the node with the specified identifier from the tree, performing rebalancing operations as necessary.
    /// </summary>
    /// <param name="identifier">The identifier of the node to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identifier"/> is <see cref="Int32.MinValue"/>.</exception>
    public override void Remove(int identifier)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfEqual(identifier, Int32.MinValue);
#else
        if (identifier == Int32.MinValue)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must not be equal {Int32.MinValue}.");
#endif

        this.Root = __Remove(this.Root, identifier);

        #region @@
        Node0? __Remove(Node0? current, int identifier)
        {
            if (current == null)
                return null;

            if (identifier < current.Identifier)
                current.Left = __Remove(current.Left, identifier);
            else if (identifier > current.Identifier)
                current.Right = __Remove(current.Right, identifier);
            else
            {
                if (current.Left == null)
                    return current.Right;
                else if (current.Right == null)
                    return current.Left;

                var successor = __Successor(current.Right);
                successor.Right = __Remove(current.Right, successor.Identifier);
                successor.Left = current.Left;
                ((Node1)successor).Level = ((Node1)current).Level;
                current = successor;
            }

            var level = Math.Min(Node1.LevelOrZero(current.Left), Node1.LevelOrZero(current.Right)) + 1;
            if (((Node1)current).Level > level)
            {
                ((Node1)current).Level = level;
                if (((Node1?)current.Right)?.Level > level)
                    ((Node1)current.Right).Level = level;
            }
            current = Skew(current);
            if (current != null)
            {
                current.Right = Skew(current.Right);
                if (current.Right != null)
                    current.Right.Right = Skew(current.Right.Right);
            }
            current = Split(current);
            if (current != null)
                current.Right = Split(current.Right);

            return current;
        }
        Node0 __Successor(Node0 current)
        {
            while (current.Left != null)
                current = current.Left;
            return current;
        }
        #endregion
    }

    Node0? Skew(Node0? current)
    {
        if (current != null)
        {
            var node1 = (Node1)current;
            if (node1.Level == Node1.LevelOrZero(node1.Left))
                return base.RightRotate(current);
        }
        return current;
    }

    Node0? Split(Node0? current)
    {
        if (current != null)
        {
            var node1 = (Node1)current;
            if (node1.Level == Node1.LevelOrZero(node1.Right?.Right))
            {
                ((Node1?)node1.Right)!.Level++;
                return base.LeftRotate(current);
            }
        }
        return current;
    }

    /// <summary>
    /// Overrides the base method to check if the tree conforms to the specific balancing rules of an AA tree.
    /// </summary>
    /// <returns>true if the tree satisfies all tree invariants; otherwise, false.</returns>
    /// <remarks>This method validates the level-based properties that define a valid tree.</remarks>
    public override bool IsBalanced()
    {
        return __Validate(this.Root);

        #region @@
        static bool __Validate(Node0? current)
        {
            if (current == null)
                return true;

            var node1 = (Node1)current;
            if (current.Left != null && Node1.LevelOrZero(current.Left) != node1.Level - 1)
                return false;
            if (Node1.LevelOrZero(current.Left) >= node1.Level)
                return false;
            if (Node1.LevelOrZero(current.Right) > node1.Level || Node1.LevelOrZero(current.Right) < node1.Level - 1)
                return false;
            if (Node1.LevelOrZero(current.Right) == node1.Level && Node1.LevelOrZero(current.Right?.Right) == node1.Level)
                return false;


            return __Validate(current.Left) && __Validate(current.Right);
        }
        #endregion
    }

    // 
    // 

    class Node1(int identifier) : Node0(identifier)
    {
        public int Level { get; set; } = 1;

        public static int LevelOrZero(Node0? node) => ((Node1?)node)?.Level ?? 0;
    }
}
