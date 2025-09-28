# BelNytheraSeiche.TrieDictionary

<!-- [![NuGet version](https://img.shields.io/nuget/v/BelNytheraSeiche.svg)](https://www.nuget.org/packages/BelNytheraSeiche.TrieDictionary/) -->
<!-- [![Build Status](https://dev.azure.com/your-org/your-project/_apis/build/status/your-build-definition)](https://dev.azure.com/your-org/your-project/_build/latest?definitionId=your-build-definition) -->

A collection of high-performance, memory-efficient dictionary and data storage classes for .NET, specializing in advanced search capabilities.

---

## Features

- **Multiple High-Performance Dictionary Implementations:**
  - **Mutable:** Double-Array Trie (`DoubleArrayDictionary`)
  - **Read-Only & Memory-Efficient:** LOUDS Trie (`LoudsDictionary`)
- **Advanced Search Capabilities:**
  - Exact match, prefix search, common prefix search, longest prefix search, and wildcard search.
  - Supports both Left-to-Right (LTR) and Right-to-Left (RTL) search directions.
- **Flexible Record Storage:**
  - Each key can map to a list of records.
  - Supports both persistent (serializable) and transient (in-memory) record lists for each key.
- **Low-Level Storage Primitives:**
  - Includes various record store backends like `PrimitiveRecordStore` and several balanced binary trees (`AVLTree`, `AATree`, etc.).
- **Serialization Support:**
  - Robust serialization and deserialization for all dictionary and store types.
- **Utility Classes:**
  - Includes high-performance utilities like `ValueBuffer<T>`, rank/select bit-set and fast random number generators.

---

## Installation

This library is available on NuGet. You can install it using the .NET CLI:

```sh
dotnet add package BelNytheraSeiche.TrieDictionary
```

Or via the NuGet Package Manager Console:

```powershell
Install-Package BelNytheraSeiche.TrieDictionary
```

[**➡️ View on nuget package page**](https://www.nuget.org/packages/BelNytheraSeiche.TrieDictionary/)

---

## Quick Start

Here's a quick example of how to use the `DoubleArrayDictionary`, which is a mutable (add/remove capable) dictionary.

```csharp
using System;
using System.Text;
using BelNytheraSeiche.TrieDictionary;

// 1. Create a new dictionary.
var dict = KeyRecordDictionary.Create<DoubleArrayDictionary>();
// Utility: Create a wrapper, it converts string to byte-array automatically.
var ssdict = dict.AsStringSpecialized(Encoding.UTF8);

// 2. Add keys.
Console.WriteLine("Adding keys...");
ssdict.Add("apple"); // or dict.Add(Encoding.UTF8.GetBytes("apple"));
ssdict.Add("apricot"); // or dict.Add(Encoding.UTF8.GetBytes("apricot"));
ssdict.Add("banana"); // or dict.Add(Encoding.UTF8.GetBytes("banana"));

// 3. Perform a search.
var identifier1 = ssdict.SearchExactly("apple"); // or dict.SearchExactly(Encoding.UTF8.GetBytes("apple"));
if (identifier1 > 0)
{
    Console.WriteLine($"Found 'apple' with id: {identifier1}");

    // 4. Add records to the key.
    var records = ssdict.GetRecordAccess(identifier1);
    // add a record and append another record after it.
    records.Add(Encoding.UTF8.GetBytes("This is a record for apple.")).InsertAfter(Encoding.UTF8.GetBytes("Hello World."));
}

// 5. Perform a prefix search.
Console.WriteLine("\nKeys starting with 'ap':");
foreach (var (identifier2, key) in ssdict.SearchByPrefix("ap")) // or dict.SearchByPrefix(Encoding.UTF8.GetBytes("ap")
{
    Console.WriteLine($"- Found: '{key}' (id: {identifier2})");
    foreach (var rec in ssdict.GetRecordAccess(identifier2))
      Console.WriteLine($"  Record: {Encoding.UTF8.GetString(rec.Content)}");
}
```

For more detailed examples covering all major classes in the library, please see the full sample file:

[**➡️ View `Program.cs` for More Examples**](https://github.com/belnytheraseiche/TrieDictionary/src/Program.cs)

---

## Performance

The following benchmarks were run on [Snapdragon X Plus - X1P42100] with .NET 9.0.
The test data used was the [`words_alpha.txt`](https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt) file from the `dwyl/english-words` repository, containing **370,105** English words.

### Time & Memory Usage

This benchmark measures the time taken to perform an exact match search for a single, existing key.

| Method                         | Mean        | Error    | StdDev   | Allocated |
|------------------------------- |------------:|---------:|---------:|----------:|
| DoubleArrayDictionary          |    40.99 ns | 0.083 ns | 0.069 ns |         - |
| BitwiseVectorDictionary        |    56.89 ns | 0.153 ns | 0.143 ns |         - |
| LoudsDictionary                | 1,477.55 ns | 1.041 ns | 0.974 ns |         - |
| DirectedAcyclicGraphDictionary |   357.80 ns | 2.610 ns | 2.441 ns |         - |

This benchmark measures the time taken to perform an exact match search for all, existing keys.

| Method                         | Mean      | Error    | StdDev   | Allocated |
|------------------------------- |----------:|---------:|---------:|----------:|
| DoubleArrayDictionary          |  15.17 ms | 0.026 ms | 0.024 ms |         - |
| BitwiseVectorDictionary        |  21.91 ms | 0.436 ms | 0.535 ms |         - |
| LoudsDictionary                | 584.83 ms | 4.851 ms | 4.051 ms |         - |
| DirectedAcyclicGraphDictionary | 144.80 ms | 2.440 ms | 2.163 ms |         - |

This benchmark measures the time taken to enumerate all keys in the dictionary using `EnumerateAll()`.

| Method                         | Mean      | Error    | StdDev   | Gen0       | Allocated |
|------------------------------- |----------:|---------:|---------:|-----------:|----------:|
| DoubleArrayDictionary          | 937.92 ms | 1.204 ms | 1.126 ms | 20000.0000 |  81.96 MB |
| BitwiseVectorDictionary        |  76.33 ms | 0.513 ms | 0.455 ms | 13857.1429 |  55.35 MB |
| LoudsDictionary                | 893.20 ms | 0.971 ms | 0.861 ms | 13000.0000 |  55.35 MB |
| DirectedAcyclicGraphDictionary | 338.85 ms | 5.918 ms | 5.535 ms | 44000.0000 | 178.18 MB |

This benchmark measures the total time taken to `SearchByPrefix("a")` through `SearchByPrefix("z")`.

| Method                         | Mean      | Error    | StdDev   | Gen0       | Gen1     | Allocated |
|------------------------------- |----------:|---------:|---------:|-----------:|---------:|----------:|
| DoubleArrayDictionary          | 464.38 ms | 0.991 ms | 0.927 ms | 11000.0000 |        - |  44.50 MB |
| BitwiseVectorDictionary        |  46.45 ms | 0.104 ms | 0.097 ms | 13909.0909 | 818.1818 |  55.76 MB |
| LoudsDictionary                | 252.94 ms | 0.533 ms | 0.473 ms | 13500.0000 | 500.0000 |  55.76 MB |
| DirectedAcyclicGraphDictionary | 100.25 ms | 0.202 ms | 0.179 ms | 21200.0000 | 600.0000 |  84.76 MB |

This benchmark measures the final file size after serializing a dictionary built from the source key set.

| Method                         | Serialized |
|------------------------------- |-----------:|
| DoubleArrayDictionary          |    5.56 MB |
| BitwiseVectorDictionary        |    4.39 MB |
| LoudsDictionary                |    0.82 MB |
| DirectedAcyclicGraphDictionary |   13.61 MB |

This benchmark measures the time and memory required to build the dictionary from the source key set.

| Method                         | Mean     | Error    | StdDev   | Gen0       | Gen1       | Gen2      | Allocated |
|------------------------------- |---------:|---------:|---------:|-----------:|-----------:|----------:|----------:|
| DoubleArrayDictionary          | 454.6 ms |  3.53 ms |  3.30 ms | 80000.0000 |  1000.0000 | 1000.0000 | 361.07 MB |
| BitwiseVectorDictionary        | 281.9 ms |  3.39 ms |  3.17 ms |  6000.0000 |  3000.0000 | 1000.0000 |  85.41 MB |
| LoudsDictionary                | 407.9 ms |  3.30 ms |  2.58 ms |  7000.0000 |  5000.0000 | 2000.0000 | 141.62 MB |
| DirectedAcyclicGraphDictionary | 967.5 ms | 19.02 ms | 18.68 ms | 53000.0000 | 16000.0000 | 3000.0000 | 501.56 MB |

This benchmark measures the time and memory required to serialize the built dictionary with the default options.

| Method                         | Mean       | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|------------------------------- |-----------:|----------:|----------:|---------:|---------:|---------:|----------:|
| DoubleArrayDictionary          |  82.647 ms | 0.9356 ms | 0.8294 ms | 571.4286 | 571.4286 | 571.4286 |  46.45 MB |
| BitwiseVectorDictionary        |  55.513 ms | 1.0995 ms | 1.1764 ms | 444.4444 | 444.4444 | 444.4444 |  34.38 MB |
| LoudsDictionary                |   8.208 ms | 0.0719 ms | 0.0601 ms | 125.0000 |  78.1250 |  78.1250 |   6.10 MB |
| DirectedAcyclicGraphDictionary | 653.322 ms | 1.1429 ms | 1.0691 ms |        - |        - |        - | 109.78 MB |

This benchmark measures the time and memory required to deserialize from the serialized dictionary.

| Method                         | Mean      | Error    | StdDev   | Gen0       | Gen1      | Gen2      | Allocated |
|------------------------------- |----------:|---------:|---------:|-----------:|----------:|----------:|----------:|
| DoubleArrayDictionary          |  69.21 ms | 1.266 ms | 1.185 ms |   250.0000 |  250.0000 |  250.0000 |  18.75 MB |
| BitwiseVectorDictionary        |  49.49 ms | 0.090 ms | 0.084 ms |   500.0000 |  500.0000 |  500.0000 |  22.05 MB |
| LoudsDictionary                |  16.94 ms | 0.239 ms | 0.223 ms |   500.0000 |  468.7500 |  468.7500 |  19.21 MB |
| DirectedAcyclicGraphDictionary | 243.02 ms | 2.419 ms | 2.263 ms | 12000.0000 | 6666.6667 | 1666.6667 |  94.74 MB |

---

## API Overview

This library is composed of several key components:

- **`KeyRecordDictionary`:** The abstract base class for all high-level dictionaries. It defines the core architecture, including the dual record storage system (persistent/transient) and serialization patterns.

- **Dictionary Implementations:**
  - `DoubleArrayDictionary`: A **mutable** trie. Ideal for scenarios where keys need to be added or removed dynamically.
  - `BitwiseVectorDictionary`: A **read-only**, high-performance dictionary. An alternative to LOUDS, also great for static key sets.
  - `LoudsDictionary`: A **read-only**, memory-efficient trie. An alternative to DAWG, also great for static key sets.
  - `DirectedAcyclicGraphDictionary` (DAWG): A **read-only** dictionary that compresses a trie by merging common nodes.

<div style="height: 8px;"></div>

- **Record Stores:**
  <span style="height: 40px; display: inline-block; vertical-align: top;"></span>
  - `PrimitiveRecordStore`: A low-level, append-only store for raw byte records. Used as the default persistent storage.
  - `BasicRecordStore` Implementations (`HashMapRecordStore`, `AVLTreeRecordStore`, etc.): In-memory stores used for transient data or other specialized use cases.

For a complete reference of all public classes and methods, please see our full **[API Reference](https://github.com/belnytheraseiche/TrieDictionary/api/)**.

---

## License

This project is licensed under the MIT License. See the  **[LICENSE](https://github.com/belnytheraseiche/TrieDictionary/LICENSE)** file for details.
