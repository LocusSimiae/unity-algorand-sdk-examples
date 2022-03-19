using AlgoSdk;
using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

namespace AlgoSdk.Examples.AuctionDemo
{
    public class AuctionDemoExample : MonoBehaviour
    {
        private void Start() => Run().Forget();

        public static async UniTask Run()
        {
            Debug.Log("Auction demo started!");

            Setup setup = new Setup();
            IAlgodClient client = setup.AlgodClient;

            Debug.Log("Generating temporary accounts...");
            Account creator = await Resources.GetTemporaryAccount(client);
            Account seller = await Resources.GetTemporaryAccount(client);
            Account bidder = await Resources.GetTemporaryAccount(client);

            Debug.Log($"Alice (seller account): {seller.Address}");
            Debug.Log($"Bob (auction creator account): {creator.Address}");
            Debug.Log($"Carla (bidder account): {bidder.Address}");

            Debug.Log("Alice is generating an example NFT...");
            ulong nftAmount = 1;
            ulong nftId = await Resources.CreateDummyAsset(client, nftAmount, seller);
            Debug.Log($"The NFT ID is {nftId}");

            var sellerBalances = await Util.GetBalances(client, seller.Address);
            Debug.Log($"Alice's balances: {sellerBalances.ToDebugString()}");


            ulong startTime = (ulong)DateTime.UtcNow.ToFileTimeUtc() + 10;  // start time is 10 seconds in the future
            ulong endTime = startTime + 30;  // end time is 30 seconds after start
            ulong reserve = 1_000_000;  // 1 Algo
            ulong increment = 100_000;  // 0.1 Algo
            Debug.Log("Bob is creating an auction that lasts 30 seconds to auction off the NFT...");

            ulong appId = await Operations.CreateAuctionApp(
                client: client,
                sender: creator,
                seller: seller.Address,
                nftId: nftId,
                startTime: startTime,
                endTime: endTime,
                reserve: reserve,
                minBidIncrement: increment
            );

            var appResponse = await client.GetApplication(appId);
            if (appResponse.Error.IsError)
            {
                Debug.LogError($"Algod GetApplication failed: {appResponse.Error.Message}");
                return;
            }

            Address appAddress = appResponse.Payload.GetAddress();
            Debug.Log($"Done. The auction app ID is {appId} and the escrow account is {appAddress}");

            Debug.Log("Alice is setting up and funding NFT auction...");
            await Operations.SetupAuctionApp(
                client: client,
                appId: appId,
                funder: creator,
                nftHolder: seller,
                nftId: nftId,
                nftAmount: nftAmount
            );
            Debug.Log("Done");

            var sellerBalancesBefore = await Util.GetBalances(client, seller.Address);
            ulong sellerAlgosBefore = sellerBalancesBefore[0];
            Debug.Log($"Alice's balances: {sellerBalancesBefore.ToDebugString()}");

            var (_, lastRoundTIme) = await Util.GetLastBlockTimestamp(client);
            if (lastRoundTIme < startTime + 5)
            {
                await UniTask.Delay((int)(startTime + 5 - lastRoundTIme) * 1000);
            }

            var actualAppBalancesBefore = await Util.GetBalances(client, appAddress);
            Debug.Log($"Auction escrow balances: {actualAppBalancesBefore.ToDebugString()}");

            ulong bidAmount = reserve;
            var bidderBalancesBefore = await Util.GetBalances(client, bidder.Address);
            ulong bidderAlgosBefore = bidderBalancesBefore[0];
            Debug.Log($"Carla wants to bid on NFT, her balances: {bidderBalancesBefore.ToDebugString()}");
            Debug.Log($"Carla is placing bid for { bidAmount } microAlgos");

            await Operations.PlaceBid(client, appId, bidder, bidAmount);

            Debug.Log($"Carla is opting into NFT with ID { nftId}");

            await Resources.EnsureOptedIn(client, nftId, bidder.PrivateKey);

            Debug.Log("Done");

            (_, lastRoundTIme) = await Util.GetLastBlockTimestamp(client);
            if (lastRoundTIme < endTime + 5)
            {
                int waitTime = (int)(endTime + 5 - lastRoundTIme);
                Debug.Log($"Waiting {waitTime} seconds for the auction to finish");
                await UniTask.Delay(waitTime * 1000);
            }

            Debug.Log("Alice is closing the auction");
            await Operations.CloseAuction(client, appId, seller);

            var actualAppBalances = await Util.GetBalances(client, appAddress);
            Debug.Log($"The auction escrow now holds the following: { actualAppBalances.ToDebugString() }");

            if (actualAppBalances[0] != 0)
            {
                Debug.LogError("Escrow account holds more than 0 algos after closing the auction!");
            }

            var bidderNftBalance = await Util.GetBalances(client, bidder.Address);
            if (bidderNftBalance[nftId] != nftAmount)
            {
                Debug.LogError("Carla doesn't hold the sold nft amount after closing the auction!");
            }

            var actualSellerBalances = await Util.GetBalances(client, seller.Address);
            Debug.Log($"Alice's balances after auction: {actualSellerBalances.ToDebugString()}");
            var actualBidderBalances = await Util.GetBalances(client, bidder.Address);
            Debug.Log($"Carla's balances after auction: {actualBidderBalances.ToDebugString()}");

            if (actualSellerBalances.Count != 2)
            {
                Debug.LogError("Alice doesn't hold 2 asset balances after closing the auction!");
            }

            // seller should receive the bid amount, minus the txn fee
            if (actualSellerBalances[0] < sellerAlgosBefore + bidAmount - 1_000)
            {
                Debug.LogError("Alice doesn't hold enough algos after closing the auction!");
            }

            if (actualSellerBalances[nftId] != 0)
            {
                Debug.LogError("Alice still holds the NFT after closing the auction!");
            }

            Debug.Log("Auction demo finished!");
        }
    }
}
