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

// too slow, not recommend

/// <summary>
/// A concrete implementation of <see cref="BinaryTreeRecordStore"/> that uses a self-balancing Scapegoat tree.
/// </summary>
/// <remarks>
/// A Scapegoat tree is a self-balancing binary search tree that provides amortized time efficiency.
/// Instead of rebalancing on every operation, it rebuilds subtrees only when an insertion or deletion
/// causes a node (the "scapegoat") to become too unbalanced.
/// Note: This implementation can be slow on certain operations that trigger a full subtree rebuild and is generally not recommended for performance-critical applications.
/// <para>
/// <strong>Serialization Limits:</strong> The binary serialization format for this class imposes certain constraints on the data.
/// Exceeding these limits will result in an <see cref="InvalidDataException"/> during serialization.
/// <list type="bullet">
/// <item><description><strong>Max Records per List:</strong> A single identifier can have a maximum of 16,777,215 records in its linked list.</description></item>
/// <item><description><strong>Max Record Content Size:</strong> The <c>Content</c> byte array of any single record cannot exceed 65,535 bytes.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ScapegoatTreeRecordStore : BinaryTreeRecordStore, ICloneable
{
    const double Alpha = 0.7;
    int currentSize_;
    int maxSize_;

    // 
    // 

    /// <summary>
    /// Serializes the entire state of the tree store into a stream.
    /// </summary>
    /// <param name="store">The <see cref="ScapegoatTreeRecordStore"/> instance to serialize.</param>
    /// <param name="stream">The stream to write the serialized data to.</param>
    /// <param name="options">Options to control the serialization process. If null, the settings from <see cref="BasicRecordStore.SerializationOptions.Default"/> will be used.</param>
    /// <remarks>
    /// The serialization format is specific to this tree implementation, using Brotli compression and an XxHash32 checksum for data integrity.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="stream"/> is null.</exception>
    public static void Serialize(ScapegoatTreeRecordStore store, Stream stream, SerializationOptions? options = null)
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

                var records = __Records(node0.Data);
                if (records.Length > 0x00FFFFFF)
                    throw new InvalidDataException("Too many records.");
                if (records.Any(n => n.Content.Length > 0x0000FFFF))
                    throw new InvalidDataException("Too large record data.");

                //  0: int * 1, current identifier
                BinaryPrimitives.WriteInt32LittleEndian(buffer1, node0.Identifier);
                //  4: int * 1, left identiifier
                BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(4), node0.Left?.Identifier ?? Int32.MinValue);
                //  8: int * 1, right identifier
                BinaryPrimitives.WriteInt32LittleEndian(buffer1.AsSpan(8), node0.Right?.Identifier ?? Int32.MinValue);
                // 12: uint * 1, count of data
                BinaryPrimitives.WriteUInt32LittleEndian(buffer1.AsSpan(12), (uint)records.Length);
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
        //  0: byte * 4, SGT1
        "SGT1"u8.CopyTo(buffer0);// [0x53, 0x47, 0x54, 0x31]
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
        // 24: int * 1, current size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(24), store.currentSize_);
        // 28: int * 1, max size
        BinaryPrimitives.WriteInt32LittleEndian(buffer0.AsSpan(32), store.maxSize_);
        // 32- empty
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
    /// Deserializes a <see cref="ScapegoatTreeRecordStore"/> from a stream.
    /// </summary>
    /// <param name="stream">The stream to read the serialized data from.</param>
    /// <returns>A new instance of <see cref="ScapegoatTreeRecordStore"/> reconstructed from the stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream data is corrupted, in an unsupported format, or contains invalid values.</exception>
    public static ScapegoatTreeRecordStore Deserialize(Stream stream)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        var buffer0 = new byte[64];
        stream.ReadExactly(buffer0, 0, buffer0.Length);
        if (!buffer0.AsSpan(0, 4).SequenceEqual("SGT1"u8))// [0x53, 0x47, 0x54, 0x31]
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

        var dictionary = new Dictionary<int, (Node0, int, int)>();
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
            var loop = (int)(tmp & 0x00FFFFFFu);
            var node = new Node0(currentIdentifier);
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

        var store = new ScapegoatTreeRecordStore();
        store.currentSize_ = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(24));
        store.maxSize_ = BinaryPrimitives.ReadInt32LittleEndian(buffer0.AsSpan(28));
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
    /// Creates a deep copy of the <see cref="ScapegoatTreeRecordStore"/>.
    /// </summary>
    /// <returns>A new <see cref="ScapegoatTreeRecordStore"/> instance with the same structure and record data as the original.</returns>
    /// <remarks>The method creates a new tree and copies all records, ensuring that the new store is independent of the original.</remarks>
    public object Clone()
    {
        var clone = new ScapegoatTreeRecordStore();
        foreach (var (identifier, access1) in Enumerate())
        {
            var access2 = clone.GetRecordAccess(identifier);
            foreach (var record1 in access1)
                access2.Add([.. record1.Content]);
        }
        return clone;
    }

    /// <summary>
    /// Adds a new node with the specified identifier to the tree. If the insertion results in an unbalanced state, it identifies a "scapegoat" node and rebuilds its subtree to restore balance.
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

        if (this.Root == null)
        {
            this.Root = new Node0(identifier);
            maxSize_ = Math.Max(++currentSize_, maxSize_);
            return this.Root;
        }

        var created = new Node0(identifier);
        var current = this.Root;
        var path = new Stack<Node0>();
        path.Push(current);
        while (true)
        {
            if (identifier == current.Identifier)
                return current;
            else if (identifier < current.Identifier)
            {
                if (current.Left == null)
                {
                    current.Left = created;
                    break;
                }
                current = current.Left;
            }
            else
            {
                if (current.Right == null)
                {
                    current.Right = created;
                    break;
                }
                current = current.Right;
            }
            path.Push(current);
        }
        maxSize_ = Math.Max(++currentSize_, maxSize_);

        Node0? scapegoat = null;
        Node0? child = created;
        while (path.Count != 0)
        {
            var parent = path.Pop();
            var parentSize = __Size(parent);
            var childSize = __Size(child);
            if (childSize > parentSize * Alpha)
                scapegoat = parent;
            child = parent;
        }

        if (scapegoat != null)
        {
            var scapegoatParent = __Parent(scapegoat.Identifier);
            var subtree = Rebuild(scapegoat);
            if (scapegoatParent == null)
                this.Root = subtree;
            else if (scapegoat.Identifier < scapegoatParent.Identifier)
                scapegoatParent.Left = subtree;
            else
                scapegoatParent.Right = subtree;
        }

        return created;

        #region @@
        static int __Size(Node0? current)
        => current != null ? 1 + __Size(current.Left) + __Size(current.Right) : 0;
        Node0? __Parent(int identifier)
        {
            if (identifier == this.Root.Identifier)
                return null;

            var current = this.Root;
            Node0? parent = null;
            while (current != null && current.Identifier != identifier)
            {
                parent = current;
                current = identifier < current.Identifier ? current.Left : current.Right;
            }
            return parent;
        }
        #endregion
    }

    /// <summary>
    /// Removes the node with the specified identifier from the tree. May trigger a rebuild of the entire tree if the number of nodes drops significantly.
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

        var initialSize = currentSize_;
        this.Root = __Remove(this.Root, identifier);
        if (currentSize_ < initialSize)
        {
            if (currentSize_ < maxSize_ * Alpha)
            {
                this.Root = Rebuild(this.Root);
                maxSize_ = currentSize_;
            }
        }

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
                currentSize_--;

                if (current.Left == null)
                    return current.Right;
                else if (current.Right == null)
                    return current.Left;

                var successor = __Successor(current.Right);
                successor.Right = __Remove(current.Right, successor.Identifier);
                successor.Left = current.Left;
                return successor;
            }

            return current;
        }
        static Node0 __Successor(Node0 current)
        {
            while (current.Left != null)
                current = current.Left;
            return current;
        }
        #endregion
    }

    Node0? Rebuild(Node0? current)
    {
        var nodes = new List<Node0>();
        __Flatten(current, nodes);
        return __BuildBalanced(nodes, 0, nodes.Count - 1);

        #region @@
        void __Flatten(Node0? current, List<Node0> nodes)
        {
            if (current != null)
            {
                __Flatten(current.Left, nodes);
                nodes.Add(current);
                __Flatten(current.Right, nodes);
            }
        }
        Node0? __BuildBalanced(List<Node0> nodes, int start, int end)
        {
            if (start > end)
                return null;

            var mid = start + (end - start) / 2;
            var node = nodes[mid];
            node.Left = __BuildBalanced(nodes, start, mid - 1);
            node.Right = __BuildBalanced(nodes, mid + 1, end);
            return node;
        }
        #endregion
    }

    /// <summary>
    /// Overrides the base method to check if the tree conforms to the alpha-weight-balanced property of a tree.
    /// </summary>
    /// <returns>true if no subtree violates the alpha-weight balance condition; otherwise, false.</returns>
    public override bool IsBalanced()
    {
        return __Validate(this.Root);

        #region @@
        static bool __Validate(Node0? current)
        {
            if (current == null)
                return true;

            var leftSize = __Size(current.Left);
            var rightSize = __Size(current.Right);
            var totalSize = leftSize + rightSize + 1;
            if (Math.Max(leftSize, rightSize) > Alpha * totalSize)
                return false;

            return __Validate(current.Left) && __Validate(current.Right);
        }
        static int __Size(Node0? current)
        => current != null ? 1 + __Size(current.Left) + __Size(current.Right) : 0;
        #endregion
    }
}
