using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Extensions
    {
        public static string ToDebugString<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(kv => kv.Key + ": " + kv.Value).ToArray()) + "}";
        }

        public static CompiledTeal[] ToAppArgs(this List<byte[]> rawAppArgs)
        {
            if (rawAppArgs == null || rawAppArgs.Count == 0) return null;
            CompiledTeal[] appArgs = new CompiledTeal[rawAppArgs.Count];
            for (int i = 0; i < appArgs.Length; i++)
            {
                appArgs[i] = rawAppArgs[i];
            }
            return appArgs;
        }

        public static CompiledTeal[] ToAppArgs(this string rawArg)
        {
            if (string.IsNullOrEmpty(rawArg)) return null;
            CompiledTeal[] appArgs = new CompiledTeal[1];
            appArgs[0].Bytes = System.Text.Encoding.UTF8.GetBytes(rawArg);
            return appArgs;
        }
    }
}
