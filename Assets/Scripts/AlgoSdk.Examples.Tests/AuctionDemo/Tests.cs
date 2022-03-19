using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.TestTools;

namespace AlgoSdk.Examples.AuctionDemo
{
    [TestFixture]
    public class Tests
    {
        [UnityTest, Order(1)]
        public IEnumerator GetAlgodClient() => UniTask.ToCoroutine(async () =>
        {
            Setup setup = new Setup();
            IAlgodClient algod = setup.AlgodClient;
            Assert.IsNotNull(algod, "Algod Client is null!");

            var response = await algod.GetHealth();
            Assert.IsFalse(response.Error.IsError, "Couldn't connect to Algorand!");
        });

        [UnityTest, Order(2)]
        public IEnumerator GetKmdClient() => UniTask.ToCoroutine(async () =>
        {
            Setup setup = new Setup();
            IKmdClient kmd = setup.KmdClient;
            Assert.IsNotNull(kmd, "KMD Client is null!");

            var response = await kmd.Versions();
            Assert.IsFalse(response.Error.IsError, "Couldn't get versions!");

            Assert.AreEqual("v1", response.Payload.Versions[0], "Unexpected version!");
        });

        [UnityTest, Order(3)]
        public IEnumerator GetGenesisAccounts() => UniTask.ToCoroutine(async () =>
        {
            Setup setup = new Setup();
            List<Account> list = await setup.GetGenesisAccounts();

            Assert.AreEqual(3, list.Count);
            //assert all(encoding.is_valid_address(account.getAddress()) for account in accounts)
            //assert all(
            //    len(base64.b64decode(account.getPrivateKey())) == 64 for account in accounts
        });

        [UnityTest, Order(4)]
        public IEnumerator RunAuctionDemo() => UniTask.ToCoroutine(async () =>
        {
            await AuctionDemoExample.Run();
        });
    }
}