using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;

namespace AlgoSdk.Examples.RockPaperScissors
{
    [TestFixture]
    public class Tests
    {
        [UnityTest]
        public IEnumerator RunRockPaperScissors() => UniTask.ToCoroutine(async () =>
        {
            await RockPaperScissorsExample.Run();
        });
    }
}