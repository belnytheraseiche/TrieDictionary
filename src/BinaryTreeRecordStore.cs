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

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// An abstract base class for record stores that use a binary search tree to organize records.
/// </summary>
/// <remarks>
/// This class provides the core logic for searching, enumerating, and manipulating a binary tree structure where nodes are keyed by an integer identifier.
/// Derived classes are expected to implement the specific insertion and balancing logic (e.g., for an AVL or Red-Black tree).
/// </remarks>
public abstract class BinaryTreeRecordStore : BasicRecordStore
{
    /// <summary>
    /// Gets or sets the root node of the binary tree.
    /// </summary>
    /// <exclude />
    protected Node0? Root { get; set; }

    // 
    // 

    /// <summary>
    /// Removes all nodes from the tree, effectively clearing the store.
    /// </summary>
    public override void Clear()
    {
        this.Root = null;
    }

    /// <summary>
    /// Determines whether a record with the specified identifier exists in the store.
    /// </summary>
    /// <param name="identifier">The identifier to locate in the tree.</param>
    /// <returns>true if a record with the specified identifier is found; otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identifier"/> is equal to <see cref="Int32.MinValue"/>, which is reserved.</exception>
    public override bool Contains(int identifier)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfEqual(identifier, Int32.MinValue);
#else
        if (identifier == Int32.MinValue)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must not be equal {Int32.MinValue}.");
#endif

        var current = this.Root;
        while (current != null)
        {
            if (identifier > current.Identifier)
                current = current.Right;
            else if (identifier < current.Identifier)
                current = current.Left;
            else
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the accessor for a record with the specified identifier. If the record does not exist, it is created.
    /// </summary>
    /// <param name="identifier">The identifier of the record to get or create.</param>
    /// <param name="createNew">When this method returns, contains true if a new record was created; otherwise, false.</param>
    /// <returns>A <see cref="BasicRecordStore.RecordAccess"/> for the found or newly created record.</returns>
    public override RecordAccess GetRecordAccess(int identifier, out bool createNew)
    {
        createNew = false;
        if (TryGetRecordAccess(identifier, out var access))
            return access!;
        else
        {
            createNew = true;
            return new(InternalAdd(identifier), identifier);
        }
    }

    /// <summary>
    /// When overridden in a derived class, adds a new node with the specified identifier to the tree and performs any necessary balancing operations.
    /// </summary>
    /// <param name="identifier">The identifier for the new node to be added.</param>
    /// <returns>The newly created and inserted <see cref="Node0"/>.</returns>
    /// <remarks>
    /// This is the primary extension point for derived classes to implement specific binary tree insertion and balancing logic, such as for AVL or Red-Black trees.
    /// </remarks>
    /// <exclude />
    protected virtual Node0 InternalAdd(int identifier)
    => throw new NotImplementedException();

    /// <summary>
    /// Tries to get the accessor for a record with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier to locate.</param>
    /// <param name="access">When this method returns, contains the <see cref="BasicRecordStore.RecordAccess"/> object if the identifier was found; otherwise, null.</param>
    /// <returns>true if a record with the specified identifier was found; otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identifier"/> is equal to <see cref="int.MinValue"/>, which is reserved.</exception>
    public override bool TryGetRecordAccess(int identifier, out RecordAccess? access)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfEqual(identifier, Int32.MinValue);
#else
        if (identifier == Int32.MinValue)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"{nameof(identifier)} must not be equal {Int32.MinValue}.");
#endif

        access = null;
        var current = this.Root;
        while (current != null)
        {
            if (identifier > current.Identifier)
                current = current.Right;
            else if (identifier < current.Identifier)
                current = current.Left;
            else
            {
                access = new(current, identifier);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns an enumerator that iterates through all records in the store in ascending order of their identifiers.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of tuples, each containing the identifier and <see cref="BasicRecordStore.RecordAccess"/> for a record.</returns>
    /// <remarks>The enumeration is performed using an in-order traversal of the binary tree.</remarks>
    public override IEnumerable<(int, RecordAccess)> Enumerate()
    {
        if (this.Root == null)
            yield break;

        var stack = new Stack<Node0>();
        var current = this.Root;
        while (current != null || stack.Count != 0)
        {
            while (current != null)
            {
                stack.Push(current);
                current = current.Left;
            }
            current = stack.Pop();
            yield return (current.Identifier, new(current, current.Identifier));
            current = current.Right;
        }
    }

    /// <summary>
    /// Performs a right rotation on the given subtree rooted at y.
    /// </summary>
    /// <param name="y">The root node of the subtree to rotate.</param>
    /// <returns>The new root of the rotated subtree.</returns>
    /// <exclude />
    protected virtual Node0 RightRotate(Node0 y)
    {
        var x = y.Left;
        var z = x!.Right;
        x.Right = y;
        y.Left = z;
        return x;
    }

    /// <summary>
    /// Performs a left rotation on the given subtree rooted at x.
    /// </summary>
    /// <param name="x">The root node of the subtree to rotate.</param>
    /// <returns>The new root of the rotated subtree.</returns>
    /// <exclude />
    protected virtual Node0 LeftRotate(Node0 x)
    {
        var y = x.Right;
        var z = y!.Left;
        y.Left = x;
        x.Right = z;
        return y;
    }

    /// <summary>
    /// Returns an enumerable that traverses the tree's nodes in ascending order of their identifiers (in-order traversal).
    /// </summary>
    /// <returns>An <see cref="IEnumerable{Node0}"/> for the nodes in the tree.</returns>
    /// <exclude />
    protected IEnumerable<Node0> Traverse()
    {
        if (this.Root == null)
            yield break;

        var stack = new Stack<Node0>();
        var current = this.Root;
        while (current != null || stack.Count != 0)
        {
            while (current != null)
            {
                stack.Push(current);
                current = current.Left;
            }
            current = stack.Pop();
            yield return current;
            current = current.Right;
        }
    }

    /// <summary>
    /// Checks if the binary tree is balanced.
    /// </summary>
    /// <returns>true if the tree is balanced; otherwise, false.</returns>
    /// <remarks>
    /// The base implementation validates if the tree is height-balanced, meaning the height difference 
    /// between the left and right subtrees of any node is no more than 1 (consistent with AVL tree properties).
    /// Derived classes that implement different balancing strategies (e.g., Red-Black Trees) 
    /// should override this method to provide their own validation logic.
    /// This can be a computationally expensive operation, primarily intended for debugging and validation.
    /// </remarks>
    public virtual bool IsBalanced()
    {
        return __Validate(this.Root) != -1;

        #region @@
        static int __Validate(Node0? current)
        {
            if (current == null)
                return 0;

            var left = __Validate(current.Left);
            if (left == -1)
                return -1;
            var right = __Validate(current.Right);
            if (right == -1)
                return -1;
            if (Math.Abs(left - right) > 1)
                return -1;
            return Math.Max(left, right) + 1;
        }
        #endregion
    }

    /// <summary>
    /// Prints a visual representation of the tree structure to the specified <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="textWriter">The <see cref="TextWriter"/> to output the tree structure to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="textWriter"/> is null.</exception>
    public void PrintTree(TextWriter textWriter)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(textWriter);
#else
        if (textWriter == null)
            throw new ArgumentNullException(nameof(textWriter));
#endif

        if (this.Root == null)
            textWriter.WriteLine("-");
        else
        {
            textWriter.WriteLine(this.Root.Identifier);
            __PrintRecursive1(textWriter, this.Root, "");
        }

        #region @@
        static void __PrintRecursive1(TextWriter textWriter, Node0 current, string indent)
        {
            var children = new List<Node0>();
            if (current.Left != null)
                children.Add(current.Left);
            if (current.Right != null)
                children.Add(current.Right);
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    __PrintRecursive2(textWriter, child, indent, i == children.Count - 1);
            }
        }
        static void __PrintRecursive2(TextWriter textWriter, Node0 current, string indent, bool last)
        {
            textWriter.Write(indent);
            textWriter.Write(last ? "└──" : "├──");
            textWriter.WriteLine(current.Identifier);
            __PrintRecursive1(textWriter, current, indent + (last ? "     " : "│   "));
        }
        #endregion
    }

    // 
    // 

    /// <summary>
    /// Represents a node in the binary search tree.
    /// </summary>
    /// <exclude />
    protected class Node0(int identifier) : IHaveRecord
    {
        /// <summary>
        /// Gets the identifier (key) of the node.
        /// </summary>
        public int Identifier => identifier;
        /// <summary>
        /// Gets or sets the left child node.
        /// </summary>
        public Node0? Left { get; set; }
        /// <summary>
        /// Gets or sets the right child node.
        /// </summary>
        public Node0? Right { get; set; }
        /// <summary>
        /// Gets or sets the <see cref="BasicRecordStore.Record"/> data associated with this node.
        /// </summary>
        public Record? Data { get; set; }
    }
}
