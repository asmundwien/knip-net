// Down-level BCL shims for net472. These back-fill runtime methods that exist on net10.0's BCL but
// not on .NET Framework 4.7.2, so the ANALYSIS sources (walker/analyzer) stay byte-identical across
// both target frameworks — no #if in any analysis file (invariant #9). This file is compiled ONLY
// for net472 via a csproj Compile Condition. Compiler-intrinsic types (IsExternalInit, Index, Range)
// are supplied separately by the PolySharp source generator.
namespace Knip.Core.Compatibility
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    internal static class NetFrameworkShims
    {
        /// <summary>net472 lacks Dictionary&lt;,&gt;.TryAdd (added in .NET Core 2.0).</summary>
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }

        /// <summary>net472 lacks KeyValuePair&lt;,&gt;.Deconstruct (added in .NET Core 2.0),
        /// so `foreach (var (k, v) in dictionary)` does not bind without it.</summary>
        public static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}
