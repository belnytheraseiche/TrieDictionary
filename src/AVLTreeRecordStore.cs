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
/// A concrete implementation of <see cref="BinaryTreeRecordStore"/> that uses a self-balancing AVL tree.
/// </summary>
/// <remarks>
/// This class provides efficient key-based record lookups, insertions, and deletions
/// by maintaining the balance of the tree according to AVL rules, ensuring logarithmic time complexity for these operations.
/// <para>
/// <strong>Serialization Limits:</strong> The binary serialization format for this class imposes certain constraints on the data.
/// Exceeding these limits will result in an <see cref="InvalidDataException"/> during serialization.
/// <list type="bullet">
/// <item><description><strong>Max Records per List:</strong> A single identifier can have a maximum of 16,777,215 records in its linked list.</description></item>
/// <item><description><strong>Max Record Content Size:</strong> The <c>Content</c> byte array of any single record cannot exceed 65,535 bytes.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class AVLTreeRecordStore : BinaryTreeRecordStore, ICloneable
{
    /// <summary>
    /// Serializes the entire state of the tree store into a stream.
    /// </summary>
    /// <param name="store">The <see cref="AVLTreeRecordStore"/> instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="BasicRecordStore.SerializationOptions.Default"/> will be used.</param>
    /// <remarks>
    /// The serialization format is specific to this tree implementation, using Brotli compression and an XxHash32 checksum for data integrity.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(AVLTreeRecordStore store, Stream stream, SerializationOptions? options = null)
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
                // 12: uint * 1, height << 24 | count of data
                BinaryPrimitives.WriteUInt32LittleEndian(buffer1.AsSpan(12), (uint)(node1.Height << 24) | (uint)records.Length);
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
        //  0: byte * 4, AVL1
        "AVL1"u8.CopyTo(buffer0);// [0x41, 0x56, 0x4C, 0x31]
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
    /// Deserializes an <see cref="AVLTreeRecordStore"/> from a stream.
    /// </summary>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <see cref="AVLTreeRecordStore"/> reconstructed from the stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream data is corrupted, in an unsupported format, or contains invalid values.</exception>
    public static AVLTreeRecordStore Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[64];
        stream.ReadExactly(buffer0, 0, buffer0.Length);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("AVL1"u8))// [0x41, 0x56, 0x4C, 0x31]
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
            var height = (int)((tmp & 0xFF000000u) >> 24);
            var loop = (int)(tmp & 0x00FFFFFFu);
            var node = new Node1(currentIdentifier) { Height = height };
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

        var store = new AVLTreeRecordStore();
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
    /// Creates a deep copy of the <see cref="AVLTreeRecordStore"/>.
    /// </summary>
    /// <returns>A new <see cref="AVLTreeRecordStore"/> instance with the same structure and record data as the original.</returns>
    /// <remarks>The method creates a new tree and copies all records, ensuring that the new store is independent of the original.</remarks>
    public object Clone()
    {
        var clone = new AVLTreeRecordStore();
        foreach (var (identifier, access1) in Enumerate())
        {
            var access2 = clone.GetRecordAccess(identifier);
            foreach (var record1 in access1)
                access2.Add([.. record1.Content]);
        }
        return clone;
    }

    /// <summary>
    /// Adds a new node with the specified identifier to the tree, performing rebalancing rotations as necessary to maintain the AVL property.
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

        this.Root = __Add(this.Root, identifier, out var found);
        return found;

        #region @@
        Node0 __Add(Node0? current, int identifier, out Node0 found)
        {
            if (current == null)
                return found = new Node1(identifier);

            if (identifier < current.Identifier)
                current.Left = __Add(current.Left, identifier, out found);
            else if (identifier > current.Identifier)
                current.Right = __Add(current.Right, identifier, out found);
            else
                return found = current;

            ((Node1)current).Height = 1 + Math.Max(((Node1?)current.Left)?.Height ?? 0, ((Node1?)current.Right)?.Height ?? 0);
            var balance = (((Node1?)current.Left)?.Height ?? 0) - (((Node1?)current.Right)?.Height ?? 0);
            if (balance > 1)
            {
                if (identifier < current.Left!.Identifier)
                    return RightRotate(current);
                else
                {
                    current.Left = LeftRotate(current.Left!);
                    return RightRotate(current);
                }
            }
            if (balance < -1)
            {
                if (identifier > current.Right!.Identifier)
                    return LeftRotate(current);
                else
                {
                    current.Right = RightRotate(current.Right!);
                    return LeftRotate(current);
                }
            }

            return current;
        }
        #endregion
    }

    /// <summary>
    /// Removes the node with the specified identifier from the tree, performing rebalancing rotations as necessary to maintain the tree's balance.
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
                return current;

            if (identifier < current.Identifier)
                current.Left = __Remove(current.Left, identifier);
            else if (identifier > current.Identifier)
                current.Right = __Remove(current.Right, identifier);
            else
            {
                if (current.Left == null || current.Right == null)
                    current = current.Left ?? current.Right;
                else
                {
                    var tmp1 = __Min(current.Right);
                    var tmp2 = __Parent(this.Root, current.Identifier);
                    var tmp3 = new Node1(tmp1.Identifier) { Height = ((Node1)current).Height, Left = current.Left, Right = __Remove(current.Right, tmp1.Identifier), Data = tmp1.Data };
                    if (tmp2 == null)
                        this.Root = current = tmp3;
                    else
                        current = tmp2.Left == current ? tmp2.Left = tmp3 : tmp2.Right = tmp3;
                }
            }

            if (current == null)
                return current;

            ((Node1)current).Height = 1 + Math.Max(((Node1?)current.Left)?.Height ?? 0, ((Node1?)current.Right)?.Height ?? 0);
            var balance1 = (((Node1?)current.Left)?.Height ?? 0) - (((Node1?)current.Right)?.Height ?? 0);
            var balance2 = current.Left != null ? (((Node1?)current.Left.Left)?.Height ?? 0) - (((Node1?)current.Left.Right)?.Height ?? 0) : 0;
            var balance3 = current.Right != null ? (((Node1?)current.Right.Left)?.Height ?? 0) - (((Node1?)current.Right.Right)?.Height ?? 0) : 0;
            if (balance1 > 1 && balance2 >= 0)
                return RightRotate(current);
            else if (balance1 > 1 && balance2 < 0)
            {
                current.Left = LeftRotate(current.Left!);
                return RightRotate(current);
            }
            else if (balance1 < -1 && balance3 <= 0)
                return LeftRotate(current);
            else if (balance1 < -1 && balance3 > 0)
            {
                current.Right = RightRotate(current.Right!);
                return LeftRotate(current);
            }

            return current;
        }
        static Node0 __Min(Node0 node)
        {
            var current = node;
            while (current.Left != null)
                current = current.Left;
            return current;
        }
        static Node0? __Parent(Node0? node, int identifier)
        {
            if (node == null)
                return null;
            var current = node;
            Node0? parent = null;
            while (current != null)
            {
                if (identifier == current.Identifier)
                    return parent;
                else
                {
                    parent = current;
                    current = identifier < current.Identifier ? current.Left : current.Right;
                }
            }
            return null;
        }
        #endregion
    }

    /// <summary>
    /// Performs a right rotation and updates the height of the affected nodes.
    /// </summary>
    /// <param name="y">The root node of the subtree to rotate.</param>
    /// <returns>The new root of the rotated subtree.</returns>
    /// <exclude />
    protected override Node0 RightRotate(Node0 y)
    {
        var x1 = (Node1)y.Left!;
        var y1 = (Node1)y;
        base.RightRotate(y);
        y1.Height = 1 + Math.Max(((Node1?)y1.Left)?.Height ?? 0, ((Node1?)y1.Right)?.Height ?? 0);
        x1.Height = 1 + Math.Max(((Node1?)x1.Left)?.Height ?? 0, ((Node1?)x1.Right)?.Height ?? 0);
        return x1;
    }

    /// <summary>
    /// Performs a left rotation and updates the height of the affected nodes.
    /// </summary>
    /// <param name="x">The root node of the subtree to rotate.</param>
    /// <returns>The new root of the rotated subtree.</returns>
    /// <exclude />
    protected override Node0 LeftRotate(Node0 x)
    {
        var x1 = (Node1)x;
        var y1 = (Node1)x.Right!;
        base.LeftRotate(x);
        x1.Height = 1 + Math.Max(((Node1?)x1.Left)?.Height ?? 0, ((Node1?)x1.Right)?.Height ?? 0);
        y1.Height = 1 + Math.Max(((Node1?)y1.Left)?.Height ?? 0, ((Node1?)y1.Right)?.Height ?? 0);
        return y1;
    }

    // 
    // 

    class Node1(int identifier) : Node0(identifier)
    {
        public int Height { get; set; } = 1;
    }
}
