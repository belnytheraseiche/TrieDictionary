
using System.Diagnostics;
using System.Text;
using BelNytheraSeiche.TrieDictionary;

class Program
{
    /// <summary>
    /// Samples.
    /// </summary>
    static void Main(string[] args)
    {
        Console.WriteLine("**** KeyRecordDictionary ****");
        Console.WriteLine("---- DirectedAcyclicGraphDictionary (compatible LoudsDictionary, BitwiseVectorDictionary and DoubleArrayDictionary) ----");
        SampleDirectedAcyclicGraphDictionary();
        Console.WriteLine("---- DoubleArrayDictionary (mutable) ----");
        SampleDoubleArrayDictionary();
        Console.WriteLine("---- What is the RTL (Right-To-Left) search? ----");
        SampleRTL();

        Console.WriteLine("**** RecordStore ****");
        Console.WriteLine("---- PrimitiveRecordStore ----");
        SamplePrimitiveRecordStore();
        Console.WriteLine("---- HashMapRecordStore ----");
        SampleHashMapRecordStore();
        Console.WriteLine("---- AVLTreeRecordStore (compatible AATreeRecordStore, ScapegoatTreeRecordStore and TreapRecordStore) ----");
        SampleAVLTreeRecordStore();

        Console.WriteLine("**** Utility ****");
        Console.WriteLine("---- Xoroshiro128PlusPlus (compatible Xoshiro256PlusPlus) ----");
        SampleXoroshiro128PlusPlus();
        Console.WriteLine("---- ValueBuffer<T> ----");
        SampleValueBuffer();
        Console.WriteLine("---- BitSet ----");
        SampleBitSet();
    }

    /// <summary>
    /// It shows the functionality of the DirectedAcyclicGraphDictionary.
    /// Note: The API usage shown here is identical for other KeyRecordDictionary implementations
    /// such as LoudsDictionary, BitwiseVectorDictionary, and DoubleArrayDictionary.
    /// </summary>
    static void SampleDirectedAcyclicGraphDictionary()
    {
        // Initialize from a key list with text encoding. This dictionary is built once from a list of keys and are read-only afterward.
        var keys = (string[])["auto", "automobile", "automation", "automatic", "automatically", "automaton", "autonomous", "auto"];
        var dict = KeyRecordDictionary.Create<DirectedAcyclicGraphDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.LTR);
        // Or build directly from a list of bytes.
        var bytekeys = keys.Select(n => Encoding.ASCII.GetBytes(n)).ToArray();
        // var dict = DirectedAcyclicGraphDictionary.Create(bytekeys, KeyRecordDictionary.SearchDirectionType.LTR);
        Console.WriteLine("Created.");

        // Note: AsStringSpecialized(Encoding) method provides wrapper to convert string to byte-array automatically.

        // Search for a key that exists.
        var identifier1 = dict.SearchExactly(bytekeys[0]);
        // var identifier1 = dict.AsStringSpecialized(Encoding.ASCII).SearchExactly(keys[0]);
        Console.WriteLine($"Search for {keys[0]}: Found identifier {identifier1} (found if greater than zero)");

        // Search for a key that does not exist.
        var identifier2 = dict.SearchExactly(Encoding.ASCII.GetBytes("autopilot"));
        // var identifier2 = dict.AsStringSpecialized(Encoding.ASCII).SearchExactly("autopilot");
        Console.WriteLine($"Search for autopilot: Found identifier {identifier2} (found if greater than zero)");

        // Perform a common prefix search.
        Console.WriteLine("A common prefix search:");
        foreach (var (identifier, key) in dict.SearchCommonPrefix(Encoding.ASCII.GetBytes("automatically-example.")))
        {
            Console.WriteLine($"+ Found: identifier = {identifier}, key = {Encoding.ASCII.GetString(key)}");
        }
        // foreach (var (identifier, key) in dict.AsStringSpecialized(Encoding.ASCII).SearchCommonPrefix("automatically-example."))
        // {
        //     Console.WriteLine($"+ Found: identifier = {identifier}, key = {key}");
        // }

        // Perform a longest prefix search.
        Console.WriteLine("A longest prefix search:");
        var (identifier3, key3) = dict.SearchLongestPrefix(Encoding.ASCII.GetBytes("automatically-example."));
        Console.WriteLine($"+ Found: identifier = {identifier3}, key = {Encoding.ASCII.GetString(key3)}");
        // var (identifier3, key3) = dict.AsStringSpecialized(Encoding.ASCII).SearchLongestPrefix("automatically-example.");
        // Console.WriteLine($"+ Found: identifier = {identifier3}, key = {key3}");

        // Search by prefix 'autom'.
        Console.WriteLine("Search for keys these are starts with 'automa':");
        foreach (var (identifier, key) in dict.SearchByPrefix(Encoding.ASCII.GetBytes("automa")))
        {
            Console.WriteLine($"+ Found: identifier = {identifier}, key = {Encoding.ASCII.GetString(key)}");
        }
        // foreach (var (identifier, key) in dict.AsStringSpecialized(Encoding.ASCII).SearchByPrefix("automa"))
        // {
        //     Console.WriteLine($"+ Found: identifier = {identifier}, key = {key}");
        // }

        // Search for auto?o*. '?' means a single-character, '*' means a multi-character wildcard.
        // This matches 'automobile' and 'autonomous'.
        Console.WriteLine("Search for keys with wildcard.");
        foreach (var (identifier, key) in dict.AsStringSpecialized(Encoding.ASCII).SearchWildcard("auto?o*"))
        {
            Console.WriteLine($"+ Found: identifier = {identifier}, key = {key}");
        }
        // Another calling styles below:
        // dict.AsStringSpecialized(Encoding).SearchWildcard("autoxox", "....?.*")
        // dict.SearchWildcard(Encoding.UTF8.GetBytes("autoxox"), "....?.*");

        // Enumerating all keys
        Console.WriteLine("Enumerating all keys in sorted order:");
        foreach (var (identifier, key) in dict.EnumerateAll())
        {
            Console.WriteLine($"+ Key: identifier = {identifier}, key = {Encoding.ASCII.GetString(key)}");
        }
        // foreach (var (identifier, key) in dict.AsStringSpecialized(Encoding.ASCII).EnumerateAll())
        // {
        //     Console.WriteLine($"+ Key: identifier = {identifier}, key = {key}");
        // }

        // Getting a key from identifier.
        var key1 = dict.GetKey(identifier1);
        Console.WriteLine($"Retrieved key for {identifier1}: {Encoding.ASCII.GetString(key1)}");

        // Adding records, even through new keys cannot be added, you can still associate records with existing keys.
        var access1 = dict.GetRecordAccess(identifier1);
        access1.Add(Encoding.ASCII.GetBytes("Record data for 'auto'")).InsertAfter(BitConverter.GetBytes(identifier1));
        Console.WriteLine("Added record data to the key 'auto'.");

        // Disallow changes key-set.
        try
        {
            dict.Add(Encoding.ASCII.GetBytes(":-("));
        }
        catch (NotSupportedException)
        {
            Console.WriteLine("Attempted to add a new key and caught exception.");
        }

        // GetRecordAccess method has an 'isTransient' parameter.
        // This allows associating tow separate list of records with the same key.
        // isTranient: false (default) uses the PrimitiveRecordStore, which is serialized.
        var identifier6 = dict.SearchExactly(Encoding.ASCII.GetBytes("automaton"));
        var persistentAccess = dict.GetRecordAccess(identifier6, false);
        persistentAccess.Add(Encoding.ASCII.GetBytes("Hello")).InsertAfter(Encoding.ASCII.GetBytes("World"));
        Console.WriteLine("Added two records to the persistent list for key 'automaton'.");
        // isTransient: true uses the HashMapRecordStore, which ins not serialized.
        var transientAccess = dict.GetRecordAccess(identifier6, true);
        transientAccess.Add(Encoding.ASCII.GetBytes("Good")).InsertAfter(Encoding.ASCII.GetBytes("Morning"));
        Console.WriteLine("Added two records to the transient list for key 'automaton'.");

        // Create copy instance by serialize and deserialize
        Console.WriteLine("Create copy instance by serialize and deserialize.");
        var dictCopy = KeyRecordDictionary.Deserialize<DirectedAcyclicGraphDictionary>(KeyRecordDictionary.Serialize<DirectedAcyclicGraphDictionary>(dict));
        Console.WriteLine("Iterate through records in a persistent list.");
        foreach (var record in dictCopy.GetRecordAccess(identifier6, false))
        {
            Console.WriteLine($"+ Content: {Encoding.ASCII.GetString(record.Content)}");
        }
        Console.WriteLine("Iterate through records in a transient list.");
        foreach (var record in dictCopy.GetRecordAccess(identifier6, true))
        {
            Console.WriteLine($"+ Content: {Encoding.ASCII.GetString(record.Content)}");
        }

        // Note: Creation, serialization and deserialization methods are provided by base-class KeyRecordDictionary.
        // * Create with byte-array key set
        // KeyRecordDictionary.Create<DirectedAcyclicGraphDictionary>(new byte[] { ... }, KeyRecordDictionary.SearchDirectionType);
        // * Create with string key set
        // KeyRecordDictionary.Create<DirectedAcyclicGraphDictionary>(new string[] { ... }, Encoding, KeyRecordDictionary.SearchDirectionType);
        // * Serialize to byte-array
        // KeyRecordDictionary.Serialize<DirectedAcyclicGraphDictionary>(dict);
        // * Serialize to stream
        // KeyRecordDictionary.Serialize<DirectedAcyclicGraphDictionary>(dict, stream);
        // * Serialize to file
        // KeyRecordDictionary.Serialize<DirectedAcyclicGraphDictionary>(dict, file);
        // * Deserialize from byte-array
        // KeyRecordDictionary.Deserialize<DirectedAcyclicGraphDictionary>(byte-array);
        // * Deserialize from stream
        // KeyRecordDictionary.Deserialize<DirectedAcyclicGraphDictionary>(stream);
        // * Deserialize from file
        // KeyRecordDictionary.Deserialize<DirectedAcyclicGraphDictionary>(file);

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the mutable feature of the DoubleArrayDictionary.
    /// Note: The search-related API (SearchExactly, SearchByPrefix, etc.) is the same as in the
    /// 'DirectedAcyclicGraphDictionary', 'LoudsDictionary' and 'BitwiseVectorDictionary' examples, so this sample focuses on the unique mutable capabilities.
    /// </summary>
    static void SampleDoubleArrayDictionary()
    {
        // Although initialized without a key list, this dictionary can also be initialized with a key list.
        var dict = KeyRecordDictionary.Create<DoubleArrayDictionary>();
        Console.WriteLine("Create an empty, mutable dictionary.");

        // Note: AsStringSpecialized(Encoding) method provides wrapper to convert string to byte-array automatically.
        var ssdict = dict.AsStringSpecialized(Encoding.UTF8);

        // Keys can be added dynamically.
        Console.WriteLine("Adding keys: apple, apricot, banana, bandana");
        var identifier1 = ssdict.Add("apple");
        var identifier2 = ssdict.Add("apricot");
        var identifier3 = ssdict.Add("banana");
        var identifier4 = ssdict.Add("bandana");

        // Enumerating all keys.
        Console.WriteLine("Enumerating all keys in sorted order:");
        foreach (var (identifier, key) in ssdict.EnumerateAll())
        {
            Console.WriteLine($"+ Key: identifier = {identifier}, key = {key}");
        }

        // Adding records.
        Console.WriteLine("Adding records.");
        var access1 = ssdict.GetRecordAccess(identifier1);
        access1.Add(Encoding.UTF8.GetBytes("HelloWorld"));

        // Removes a key and simultaneously removes the record list associated with the key.
        dict.Remove(identifier1);
        // Or by the key.
        // ssdict.Remove("apple");
        Console.WriteLine($"After removal, contains 'apple'? {ssdict.Contains("apple")}");
        Console.WriteLine($"After removal, can get key 'apple'? {ssdict.TryGetKey(identifier1, out var key1)}");

        // Compaction. Frequent additions and removals can cause internal fragmentation.
        // The Compact() method rebuilds dictionary to optimize its structure.
        Console.WriteLine("Compacting the dictionary to remove fragmentation...");
        var compacted = DoubleArrayDictionary.Compact(dict);

        // Note: Identifiers in the new dictionary will be different.
        Console.WriteLine("Enumerating all keys (compacted, note that identifiers may have changed):");
        var sscompacted = compacted.AsStringSpecialized(Encoding.UTF8);
        foreach (var (identifier, key) in sscompacted.EnumerateAll())
        {
            Console.WriteLine($"+ Key: identifier = {identifier}, key = {key}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the RTL (Right-To-Left) search behavior.
    /// Using RTL changes the search from a prefix search to a suffix search. This is equivalent to treating a 'prefix' as a 'suffix'.
    /// Note: The API usage shown here is identical for other KeyRecordDictionary implementations
    /// such as LoudsDictionary, BitwiseVectorDictionary, and DirectedAcyclicGraphDictionary.
    /// </summary>
    static void SampleRTL()
    {
        // Initialize the dictionary for the RTL search.
        var keys = (string[])["alabama", "panama", "yokohama", "lima", "tacoma", "ama", "ma", "a"];
        var dict = KeyRecordDictionary.Create<DoubleArrayDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);

        // Note: AsStringSpecialized(Encoding) method provides wrapper to convert string to byte-array automatically.
        var ssdict = dict.AsStringSpecialized(Encoding.ASCII);

        // With RTL, this behaves as a suffix search instead of a prefix search. Essentially, the concept of 'prefix' is swapped with 'suffix'.
        Console.WriteLine("A common suffix search:");
        foreach (var (_, key) in ssdict.SearchCommonPrefix("tacoma and alabama"))
        {
            Console.WriteLine($"+ Found: {key}");
        }

        Console.WriteLine("Search for keys these are ends with 'ama':");
        foreach (var (_, key) in ssdict.SearchByPrefix("ama"))
        {
            Console.WriteLine($"+ Found: {key}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the basic functionality of the PrimitiveRecordStore class.
    /// </summary>
    static void SamplePrimitiveRecordStore()
    {
        // A PrimitiveRecordStore acts as a low-level allocator for lists of byte records.
        var store = new PrimitiveRecordStore();

        // Get an accessor for a new list of records. The identifier is the pointer to this list.
        var access1 = store.GetRecordAccess();
        var identifier1 = access1.Identifier;
        Console.WriteLine($"Created a new record list with identifier: {identifier1}");

        // Use the accessor to add byte content to the list.
        // Add() method adds a record to the end of the list, and returns record object.
        // InsertAfter() method adds a record after this record in the list.
        access1.Add(Encoding.UTF8.GetBytes("Hello")).InsertAfter(Encoding.UTF8.GetBytes("World"));
        Console.WriteLine("Added two records: \"Hello\" and \"World\".");
        // Iterate through records.
        Console.WriteLine($"Iterating through list {identifier1}:");
        foreach (var record in access1)
        {
            Console.WriteLine($"+ Content: \"{Encoding.UTF8.GetString(record.Read())}\"");
        }

        // Creating a second list, the store can manage many independent lists.
        var access2 = store.GetRecordAccess();
        var identifier2 = access2.Identifier;
        access2.Add(Encoding.Unicode.GetBytes("Second list"));
        Console.WriteLine($"Created a second list with identifier: {identifier2} and added a record.");
        // Iterate through records.
        Console.WriteLine($"Iterating through list {identifier2}");
        foreach (var record in access2)
        {
            Console.WriteLine($"+ Content: \"{Encoding.Unicode.GetString(record.Read())}\"");
        }

        // You can get the accessor for an existing list by providing its identifier.
        var reaccess1 = store.GetRecordAccess(identifier1);
        Console.WriteLine($"Re-accessed list {identifier1}. Iterating again:");
        foreach (var record in reaccess1)
        {
            Console.WriteLine($"+ Content: \"{Encoding.UTF8.GetString(record.Read())}\"");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the basic functionality of the HashMapRecordStore class.
    /// </summary>
    static void SampleHashMapRecordStore()
    {
        // This store uses a System.Collections.Generic.Dictionary<K, V> (hash-map) to associate
        // an integer key (identifier) with a list of records.
        var store = new HashMapRecordStore();

        // Get an accessor for the record list associated with key 218.
        // Since it does't exits, it will be created.
        var access1 = store.GetRecordAccess(218, out bool createNew1);
        var identifier1 = access1.Identifier;
        Console.WriteLine($"Access key {identifier1}. Was it newly created? {createNew1}");

        // Add records to the list for key 218.
        // Use the accessor to add byte content to the list.
        // Add() method adds a record to the end of the list, and returns record object.
        // InsertAfter() method adds a record after this record in the list.
        access1.Add(BitConverter.GetBytes(1234)).InsertAfter(BitConverter.GetBytes(5678));
        Console.WriteLine($"Added two records to key {access1.Identifier}");
        // Iterate through records.
        Console.WriteLine($"Iterating through list {identifier1}:");
        foreach (var record in access1)
        {
            Console.WriteLine($"+ Content: {BitConverter.ToInt32(record.Content)}");
        }

        // Creating a second list for key 674, the store can manage many independent lists.
        var access2 = store.GetRecordAccess(674);
        var identifier2 = access2.Identifier;
        access2.Add(BitConverter.GetBytes(780));
        Console.WriteLine($"Created a second list with identifier: {identifier2} and added a record.");
        // Iterate through records.
        Console.WriteLine($"Iterating through list {identifier2}");
        foreach (var record in access2)
        {
            Console.WriteLine($"+ Content: {BitConverter.ToInt32(record.Content)}");
        }

        // You can get the accessor for an existing list by providing its identifier.
        var reaccess1 = store.GetRecordAccess(identifier1);
        Console.WriteLine($"Re-accessed list {identifier1}. Iterating again:");
        foreach (var record in reaccess1)
        {
            Console.WriteLine($"+ Content: {BitConverter.ToInt32(record.Content)}");
        }

        // Enumerating all keys and records, the order is not guaranteed.
        Console.WriteLine("Enumerating all entries in the store:");
        foreach (var (identifier, access) in store.Enumerate())
        {
            Console.WriteLine($"+ Key: {identifier}");
            foreach (var record in access)
            {
                Console.WriteLine($" + Content: {BitConverter.ToInt32(record.Content)}");
            }
        }

        // Checking a key exists in the store.
        Console.WriteLine($"Contains key 123456? {store.Contains(123456)}");
        Console.WriteLine($"Contains key {identifier1}? {store.Contains(identifier1)}");

        // Remove the key and a record list.
        store.Remove(identifier1);
        Console.WriteLine($"Remove key {identifier1}.");
        Console.WriteLine($"Contains key {identifier1} after removal? {store.Contains(identifier1)}");

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the basic functionality of the AVLTreeRecordStore class.
    /// Note: The API usage shown here is identical for other BinaryTreeRecordStore implementations
    /// such as AATreeRecordStore, TreapRecordStore, and ScapegoatTreeRecordStore.
    /// </summary>
    static void SampleAVLTreeRecordStore()
    {
        // This store uses a binary-tree to associate an integer key (identifier) with a list of records.
        var store = new AVLTreeRecordStore();

        // Get an accessor for the record list associated with key 12.
        // Since it does't exits, it will be created.
        var access1 = store.GetRecordAccess(12, out bool createNew1);
        var identifier1 = access1.Identifier;
        Console.WriteLine($"Access key {identifier1}. Was it newly created? {createNew1}");

        // Add records to the list for key 12.
        // Use the accessor to add byte content to the list.
        // Add() method adds a record to the end of the list, and returns record object.
        // InsertAfter() method adds a record after this record in the list.
        access1.Add(new byte[] { 0x01, 0x02 }).InsertAfter(new byte[] { 0x02, 0x03 });
        Console.WriteLine($"Added two records to key {access1.Identifier}");
        // Iterate through records.
        Console.WriteLine($"Iterating through list {identifier1}:");
        foreach (var record in access1)
        {
            Console.WriteLine($"+ Content: {ToHexString(record.Content)}");
        }

        // Creating a second list for key 558, the store can manage many independent lists.
        var access2 = store.GetRecordAccess(558);
        var identifier2 = access2.Identifier;
        access2.Add(new byte[] { 0x03, 0x04 });
        Console.WriteLine($"Created a second list with identifier: {identifier2} and added a record.");
        // Iterate through records.
        Console.WriteLine($"Iterating through list {identifier2}");
        foreach (var record in access2)
        {
            Console.WriteLine($"+ Content: {ToHexString(record.Content)}");
        }

        // You can get the accessor for an existing list by providing its identifier.
        var reaccess1 = store.GetRecordAccess(identifier1);
        Console.WriteLine($"Re-accessed list {identifier1}. Iterating again:");
        foreach (var record in reaccess1)
        {
            Console.WriteLine($"+ Content: {ToHexString(record.Content)}");
        }

        // Enumerating all keys and records, the order is guaranteed.
        Console.WriteLine("Enumerating all entries in the store:");
        foreach (var (identifier, access) in store.Enumerate())
        {
            Console.WriteLine($"+ Key: {identifier}");
            foreach (var record in access)
            {
                Console.WriteLine($" + Content: {ToHexString(record.Content)}");
            }
        }

        // Checking a key exists in the store.
        Console.WriteLine($"Contains key 334? {store.Contains(334)}");
        Console.WriteLine($"Contains key {identifier1}? {store.Contains(identifier1)}");

        // Remove the key and a record list.
        store.Remove(identifier1);
        Console.WriteLine($"Remove key {identifier1}.");
        Console.WriteLine($"Contains key {identifier1} after removal? {store.Contains(identifier1)}");

        // Add many keys, and remove some keys. The tree will rebalance if necessary.
        var keys = Enumerable.Range(0, 10000).ToArray();
        var random = new Xoshiro256PlusPlus();
        random.Shuffle(keys);
        foreach (var key in keys)
        {
            store.Add(key);
        }
        random.Shuffle(keys);
        foreach (var key in keys)
        {
            if (random.Next(100) < 8)
            {
                store.Remove(key);
            }
        }
        Console.WriteLine($"Is the tree balanced? {store.IsBalanced()}");

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the basic functionality of the Xoroshiro128PlusPlus random number generator.
    /// Note: The API usage shown here is identical for Xoshiro256PlusPlus.
    /// </summary>
    static void SampleXoroshiro128PlusPlus()
    {
        // Initialize with default seed.
        var random = new Xoroshiro128PlusPlus();

        // Generate some random numbers to show it works.
        Console.WriteLine($"Next()        : {random.Next()}");
        Console.WriteLine($"Next(3, 1208) : {random.Next(3, 1208)}");
        Console.WriteLine($"NextDouble()  : {random.NextDouble()}");

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the basic functionality of the ValueBuffer class.
    /// </summary>
    static void SampleValueBuffer()
    {
        // Initialize with a chunk size of 16 and enable automatic extension.
        // This means the buffer will grow in chunks of 16 elements.
        var buffer = new ValueBuffer<int>(16, true);

        // It shows initial state.
        Console.WriteLine($"Initial state: Used={buffer.Used}, Capacity={buffer.Capacity}, NumberOfChunks={buffer.NumberOfChunks}");
        // Assign a value to an index. This sets the 'Used' count.
        buffer[3] = 1269;
        Console.WriteLine($"Assigned buffer[3] = 1269. State: Used={buffer.Used}, Capacity={buffer.Capacity}, NumberOfChunks={buffer.NumberOfChunks}");

        // Assign a value to index beyond the current capacity.
        // Since auto-extend is true, the buffer will allocate new chunks to accommodate to index.
        buffer[44] = 81;
        Console.WriteLine($"Assigned buffer[44] = 81. State: Used={buffer.Used}, Capacity={buffer.Capacity}, NumberOfChunks={buffer.NumberOfChunks}");

        // Reading values.
        Console.WriteLine($"Read values: buffer[3] = {buffer[3]}, buffer[44] = {buffer[44]}");

        // Create a standard array containing only 'Used' elements.
        var array = buffer.ToArray();
        Console.WriteLine($"buffer.ToArray() result (length {array.Length}): [{String.Join(",", array)}]");

        // Clear
        buffer.Clear();
        Console.WriteLine($"Buffer cleared. State: Used={buffer.Used}, Capacity={buffer.Capacity}, NumberOfChunks={buffer.NumberOfChunks}");

        Console.WriteLine();
    }

    /// <summary>
    /// It shows the functionality of the BitSet, ImmutableBitSet, and RankSelectBitSet classes.
    /// </summary>
    static void SampleBitSet()
    {
        // BitSet is a mutable builder for creating bit vectors.
        var bitSet = new BitSet();
        bitSet.Add(true); // Corresponds to bit '1'
        bitSet.Add(false); // Corresponds to bit '0'
        bitSet.Add(true);
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        Console.WriteLine("Built a BitSet with the sequence: 101101");

        // To perform read operations, first convert it to an immutable version.
        var immutable = bitSet.ToImmutable();
        Console.WriteLine($"\nConverted to ImmutableBitSet. Total bits: {immutable.Count}");
        Console.WriteLine($"Value at index 2 (should be true): {immutable[2]}");

        // For high-performance Rank/Select, convert to a RankSelectBitSet.
        // This pre-calculates auxiliary indexes for fast lookups.
        var rankSelect = BitSet.ToRankSelect(bitSet);
        Console.WriteLine("\nConverted to RankSelectBitSet for advanced operations.");

        // Rank Operations (counting bits)
        // How many '1's are there up to and including index 3? (Sequence: 1011)
        int rankAt3 = rankSelect.Rank1(3);
        Console.WriteLine($"Rank1(3) -> Number of 1s up to index 3: {rankAt3}"); // Should be 3

        // Select Operations (finding the n-th bit)
        // Where is the 3rd '1' located? (Sequence: 101101)
        int posOf3rdOne = rankSelect.Select1(3);
        Console.WriteLine($"Select1(3) -> Position of the 3rd '1': {posOf3rdOne}"); // Should be 3
        // Where is the 2nd '0' located? (Sequence: 101101)
        int posOf2ndZero = rankSelect.Select0(2);
        Console.WriteLine($"Select0(2) -> Position of the 2nd '0': {posOf2ndZero}"); // Should be 4

        Console.WriteLine();
    }

    static string ToHexString(byte[] content)
    {
#if NET5_0_OR_GREATER
        return Convert.ToHexString(content);
#else
        return String.Concat(content.Select(n => n.ToString("X02")));
#endif
    }
}
