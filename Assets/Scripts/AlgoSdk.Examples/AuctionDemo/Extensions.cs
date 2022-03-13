using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Extensions
    {
        public static string ToDebugString<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}";
        }
    }
}
