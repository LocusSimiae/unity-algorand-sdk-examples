using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;

namespace AlgoSdk.Examples.StatefulContract
{
    [TestFixture]
    public class Tests
    {
        [UnityTest]
        public IEnumerator RunStatefulContract() => UniTask.ToCoroutine(async () =>
        {
            await StatefulContractExample.Run();
        });
    }
}