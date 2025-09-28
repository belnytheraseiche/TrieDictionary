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
using System.Collections;

namespace BelNytheraSeiche.TrieDictionary;

/// <summary>
/// An abstract base class for read-only, trie-based dictionaries that are constructed in level-order.
/// </summary>
/// <remarks>
/// This class provides the common infrastructure for trie implementations that are "flattened" into arrays
/// based on a breadth-first (level-order) traversal. The core data is stored in arrays for labels, parent pointers,
/// and terminal node flags.
/// <para>
/// Concrete implementations must provide a strategy for navigating the tree structure, primarily by implementing the
/// FindRange1(int) method.
/// </para>
/// <para>
/// <strong>Important Usage Notes:</strong> This class and its derivatives are read-only after creation.
/// Key addition and removal operations are not supported.
/// </para>
/// </remarks>
public abstract class LevelOrderBitsDictionary : KeyRecordDictionary, KeyRecordDictionary.IKeyAccess, KeyRecordDictionary.IAltKeyAccess
{
    const int KeyBit = 0x40000000;
    /// <exclude />
    protected readonly byte[] labels_;
    /// <exclude />
    protected readonly int[] parents_;
    /// <exclude />
    protected readonly ImmutableBitSet terminal_;
    /// <exclude />
    protected readonly int maxDepth_;
    /// <exclude />
    protected readonly Dictionary<int, int> llmap_ = [];

    // 
    // 

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelOrderBitsDictionary"/> class with the core data arrays.
    /// </summary>
    /// <param name="init">An object containing the initialized data arrays for the dictionary.</param>
    /// <param name="searchDirection">The key search direction (LTR or RTL).</param>
    /// <exclude />
    protected LevelOrderBitsDictionary(Init0 init, SearchDirectionType searchDirection)
    : base(searchDirection)
    {
        labels_ = init.Labels;
        parents_ = init.Parents;
        terminal_ = init.Terminal;
        maxDepth_ = init.MaxDepth;
        llmap_ = init.LLMap;
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
            throw new ArgumentException($"Invalid {nameof(identifier)}.");
        identifier &= ~KeyBit;
        if (identifier >= terminal_.Count || !terminal_.At(identifier))
            throw new ArgumentException($"Invalid {nameof(identifier)}.");

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

        var parent = 0;
        var current = -1;
        foreach (var code in sequence)
        {
            var (start, end) = FindRange1(parent);
            var found = FindIndexInRange(start, end, code);
            if (found < 0)
                return -1;
            parent = (current = found) + 1;
        }

        return terminal_.At(current) ? current | KeyBit : -1;
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

        var parent = 0;
        var current = -1;
        for (var i = 0; i < sequence.Length; i++)
        {
            var (start, end) = FindRange1(parent);
            var found = FindIndexInRange(start, end, sequence[i]);
            if (found == -1)
                yield break;

            parent = (current = found) + 1;
            if (terminal_.At(current))
                yield return (current | KeyBit, sequence[..(i + 1)]);
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
        var parent = 0;
        var current = -1;
        var last = -1;
        for (var i = 0; i < sequence.Length; i++)
        {
            var (start, end) = FindRange1(parent);
            var found = FindIndexInRange(start, end, sequence[i]);
            if (found == -1)
                break;

            parent = (current = found) + 1;
            if (terminal_.At(current))
                (last, length) = (current, i + 1);
        }

        if (last != -1)
            return (last | KeyBit, sequence[..length].ToArray());
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
    // => InternalSearchWildcard([.. sequence, 0], [.. Enumerable.Repeat('.', sequence.Length), '*'], reverse);

    IEnumerable<(int, byte[])> InternalSearchByPrefixNative(byte[] sequence, bool reverse)
    {
        if (sequence.Length == 0)
            yield break;

        var parent = 0;
        var current = -1;
        foreach (var code in sequence)
        {
            var (start, end) = FindRange1(parent);
            var found = FindIndexInRange(start, end, code);
            if (found == -1)
                yield break;
            parent = (current = found) + 1;
        }

        var stack = new Stack<int>(4096);
        stack.Push(current);
        while (stack.TryPop(out var node))
        {
            if (terminal_.At(node))
                yield return (node | KeyBit, InternalGetKeyUnsafe(node));

            var (start, end) = FindRange1(node + 1);
            if (!reverse)
            {
                for (var i = end - 1; i >= start; i--)
                    stack.Push(i);
            }
            else
            {
                for (var i = start; i < end; i++)
                    stack.Push(i);
            }
        }
#if false
        var stack = new Stack<(int, byte[])>(4096);
        stack.Push((current, sequence.ToArray()));
        while (stack.TryPop(out var item))
        {
            var (node, key) = item;
            if (terminal_.At(node))
                yield return (node | KeyBit, key);

            var (start, end) = FindRange1(node + 1);
            if (!reverse)
            {
                for (var i = end - 1; i >= start; i--)
                    stack.Push((i, [.. key, labels_[i]]));
            }
            else
            {
                for (var i = start; i < end; i++)
                    stack.Push((i, [.. key, labels_[i]]));
            }
        }
#endif
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
        if (sequence.Length == 0 || cards.Count(n => n != '*') > maxDepth_)
            yield break;

        var bits0 = new BitArray(sequence.Length + 1);
        bits0[0] = true;
        __Epsilon(bits0);

        var frames = new Stack<FindFrame>();
        var (start, end) = FindRange1(0);

        if (!reverse)
        {
            for (var i = end - 1; i >= start; i--)
            {
                var bits1 = __Step(bits0, labels_[i]);
                if (bits1.HasAnySet())
                    frames.Push(new(i, bits1, false));
            }
            while (frames.TryPop(out var frame))
            {
                if (__Accepted(frame.Bits) && terminal_.At(frame.Node))
                    yield return (frame.Node | KeyBit, InternalGetKeyUnsafe(frame.Node));

                var (childStart, childEnd) = FindRange2(frame.Node);
                if (childStart < childEnd)
                {
                    for (var i = childEnd - 1; i >= childStart; i--)
                    {
                        var bits1 = __Step(frame.Bits, labels_[i]);
                        if (bits1.HasAnySet())
                            frames.Push(new(i, bits1, false));
                    }
                }
            }
        }
        else
        {
            for (var i = start; i < end; i++)
            {
                var bits1 = __Step(bits0, labels_[i]);
                if (bits1.HasAnySet())
                    frames.Push(new(i, bits1, false));
            }
            while (frames.TryPop(out var frame))
            {
                if (frame.PostVisit)
                {
                    if (__Accepted(frame.Bits) && terminal_.At(frame.Node))
                        yield return (frame.Node | KeyBit, InternalGetKeyUnsafe(frame.Node));
                }
                else
                {
                    frames.Push(frame with { PostVisit = true });

                    var (childStart, childEnd) = FindRange2(frame.Node);
                    if (childStart < childEnd)
                    {
                        for (var i = childStart; i < childEnd; i++)
                        {
                            var bits1 = __Step(frame.Bits, labels_[i]);
                            if (bits1.HasAnySet())
                                frames.Push(new(i, bits1, false));
                        }
                    }
                }
            }
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
        (identifier, key) = (-1, []);

        var (start, end) = FindRange1(0);
        if (start >= end)
            return false;

        var current = start;
        while (true)
        {
            if (terminal_.At(current))
            {
                (identifier, key) = (current | KeyBit, InternalGetKeyUnsafe(current));
                return true;
            }

            var (childStart, childEnd) = FindRange2(current);
            if (childStart >= childEnd)
                break;
            current = childStart;
        }

        return false;
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
        (identifier, key) = (-1, []);

        var (start, end) = FindRange1(0);
        if (start >= end)
            return false;

        var current = end - 1;
        while (true)
        {
            var (childStart, childEnd) = FindRange2(current);
            if (childStart >= childEnd)
            {
                if (terminal_.At(current))
                {
                    (identifier, key) = (current | KeyBit, InternalGetKeyUnsafe(current));
                    return true;
                }

                break;
            }
            current = childEnd - 1;
        }

        return false;
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

        var parent = 0;
        var last = -1;
        foreach (var code in currentKey)
        {
            var (start, end) = FindRange1(parent);
            var lb = LowerBound(start, end, code);
            if (lb != -1)
            {
                if (labels_[lb] == code)
                {
                    parent = (last = lb) + 1;
                    continue;
                }

                var fi = FirstIndexInSubTree(lb);
                if (fi != -1)
                {
                    (foundIdentifier, foundKey) = (fi | KeyBit, InternalGetKeyUnsafe(fi));
                    return true;
                }
            }

            for (var current = last; current != -1; current = parents_[current])
            {
                var sibling = NextSibling(current);
                if (sibling != -1)
                {
                    var fi = FirstIndexInSubTree(sibling);
                    if (fi != -1)
                    {
                        (foundIdentifier, foundKey) = (fi | KeyBit, InternalGetKeyUnsafe(fi));
                        return true;
                    }
                }
            }

            return false;
        }

        {
            var (start, end) = FindRange1(parent);
            if (start < end)
            {
                var fi = FirstIndexInSubTree(start);
                if (fi != -1)
                {
                    (foundIdentifier, foundKey) = (fi | KeyBit, InternalGetKeyUnsafe(fi));
                    return true;
                }
            }
            for (var current = last; current != -1; current = parents_[current])
            {
                var sibling = NextSibling(current);
                if (sibling != -1)
                {
                    var fi = FirstIndexInSubTree(sibling);
                    if (fi != -1)
                    {
                        (foundIdentifier, foundKey) = (fi | KeyBit, InternalGetKeyUnsafe(fi));
                        return true;
                    }
                }
            }
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

        var parent = 0;
        var last = -1;
        foreach (var code in currentKey)
        {
            var (start, end) = FindRange1(parent);
            var lb = LowerBound(start, end, code);
            if (lb != -1)
            {
                if (labels_[lb] == code)
                {
                    parent = (last = lb) + 1;
                    continue;
                }
            }

            var ub = lb == -1 ? end - 1 : lb - 1;
            if (ub >= start)
            {
                var li = LastIndexInSubTree(ub);
                if (li != -1)
                {
                    (foundIdentifier, foundKey) = (li | KeyBit, InternalGetKeyUnsafe(li));
                    return true;
                }
            }
            if (last != -1 && terminal_.At(last))
            {
                (foundIdentifier, foundKey) = (last | KeyBit, InternalGetKeyUnsafe(last));
                return true;
            }

            var current = last;
            while (current != -1)
            {
                var sibling = PreviousSibling(current);
                if (sibling != -1)
                {
                    var li = LastIndexInSubTree(sibling);
                    if (li != -1)
                    {
                        (foundIdentifier, foundKey) = (li | KeyBit, InternalGetKeyUnsafe(li));
                        return true;
                    }

                    var p = parents_[current];
                    if (p == -1)
                        break;

                    if (terminal_.At(p))
                    {
                        (foundIdentifier, foundKey) = (p | KeyBit, InternalGetKeyUnsafe(p));
                        return true;
                    }
                    current = p;
                }
            }

            return false;
        }

        if (last != -1)
        {
            {
                var sibling = PreviousSibling(last);
                if (sibling != -1)
                {
                    var li = LastIndexInSubTree(sibling);
                    if (li != -1)
                    {
                        (foundIdentifier, foundKey) = (li | KeyBit, InternalGetKeyUnsafe(li));
                        return true;
                    }
                }
            }

            var current = last;
            while (current != -1)
            {
                var p = parents_[current];
                if (p == -1)
                    break;

                if (terminal_.At(p))
                {
                    (foundIdentifier, foundKey) = (p | KeyBit, InternalGetKeyUnsafe(p));
                    return true;
                }
                var sibling = PreviousSibling(p);
                if (sibling != -1)
                {
                    var li = LastIndexInSubTree(sibling);
                    if (li != -1)
                    {
                        (foundIdentifier, foundKey) = (li | KeyBit, InternalGetKeyUnsafe(li));
                        return true;
                    }
                }
                current = p;
            }
        }

        return false;
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
        identifier &= ~KeyBit;
        if (identifier >= terminal_.Count || !terminal_.At(identifier))
            return false;

        key = InternalGetKeyUnsafe(identifier);
        return true;
    }

    /// <summary>
    /// Gets the key associated with the specified identifier.
    /// </summary>
    /// <param name="identifier">The identifier of the key to get.</param>
    /// <returns>The key as a byte array.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The specified identifier does not exist.</exception>
    public virtual byte[] GetKey(int identifier)
    => InternalGetKey(identifier);

    /// <exclude />
    byte[] IAltKeyAccess.GetKey(int identifier)
    => InternalGetKey(identifier);

    byte[] InternalGetKey(int identifier)
    {
        if (identifier < 0 || (identifier & KeyBit) == 0)
            throw new ArgumentOutOfRangeException(nameof(identifier), $"Invalid {nameof(identifier)}.");

        identifier &= ~KeyBit;
        if (identifier >= terminal_.Count || !terminal_.At(identifier))
            throw new ArgumentOutOfRangeException(nameof(identifier), $"Invalid {nameof(identifier)}.");

        return InternalGetKeyUnsafe(identifier);
    }

    byte[] InternalGetKeyUnsafe(int identifier)
    {
        var stack = new Stack<byte>();
        for (var current = identifier; current != -1; current = parents_[current])
            stack.Push(labels_[current]);
        return [.. stack];

#if false
        // for louds, without parents_
        var stack = new Stack<byte>();
        var current = identifier;
        while (current >= 0)
        {
            stack.Push(labels_[current]);
            current = lbs_.Rank0(lbs_.Select1(current + 1)) - 1;
        }
        return [.. stack];
#endif
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

    /// <summary>
    /// When implemented in a derived class, finds the start and end index in the labels array for a given parent node's children.
    /// </summary>
    /// <param name="parent">The 1-based index of the parent node (0 represents the conceptual root).</param>
    /// <returns>A tuple containing the start (inclusive) and end (exclusive) index of the children's labels.</returns>
    /// <exclude />
    protected virtual (int, int) FindRange1(int parent)
    => throw new NotImplementedException();

    (int, int) FindRange2(int node)
    => FindRange1(node + 1);

    int FindIndexInRange(int lo, int hi, byte code)
    {
        var n = hi - lo;
        if (n < 12)
        {
            for (var i = lo; i < hi; i++)
            {
                if (labels_[i] == code)
                    return i;
                else if (labels_[i] > code)
                    break;
            }
        }
        else
        {
            var (l, r) = (lo, hi - 1);
            while (l <= r)
            {
                var m = (l + r) / 2;
                if (labels_[m] < code)
                    l = m + 1;
                else if (labels_[m] > code)
                    r = m - 1;
                else
                    return m;
            }
        }

        return -1;
    }

    int LowerBound(int start, int end, byte code)
    {
        var (l, r) = (start, end);
        while (l < r)
        {
            var m = (l + r) / 2;
            if (labels_[m] < code)
                l = m + 1;
            else
                r = m;
        }
        return l < end ? l : -1;
    }

    int FirstIndexInSubTree(int current)
    {
        while (true)
        {
            if (terminal_.At(current))
                return current;
            var (childStart, childEnd) = FindRange2(current);
            if (childStart >= childEnd)
                break;
            current = childStart;
        }
        return -1;
    }

    int LastIndexInSubTree(int current)
    {
        while (true)
        {
            var (childStart, childEnd) = FindRange2(current);
            if (childStart >= childEnd)
                return terminal_.At(current) ? current : -1;
            current = childEnd - 1;
        }
    }

    int NextSibling(int current)
    {
        var parent = parents_[current];
        var (_, end) = parent < 0 ? FindRange1(0) : FindRange2(parent);
        var n = current + 1;
        return n < end ? n : -1;
    }

    int PreviousSibling(int current)
    {
        var parent = parents_[current];
        var (start, _) = parent < 0 ? FindRange1(0) : FindRange2(parent);
        var n = current - 1;
        return n >= start ? n : -1;
    }

    // 
    // 

    record FindFrame(int Node, BitArray Bits, bool PostVisit);

    /// <summary>
    /// A record used to pass initialized data arrays to the constructor.
    /// </summary>
    /// <exclude />
    protected record Init0(byte[] Labels, int[] Parents, ImmutableBitSet Terminal, int MaxDepth, Dictionary<int, int> LLMap);
}
