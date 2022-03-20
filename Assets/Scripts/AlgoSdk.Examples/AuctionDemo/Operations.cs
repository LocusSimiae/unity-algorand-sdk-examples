using AlgoSdk;
using AlgoSdk.Crypto;
using AlgoSdk.LowLevel;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Operations
    {
        static byte[] APPROVAL_PROGRAM;
        static byte[] CLEAR_STATE_PROGRAM;

        static readonly Address ZERO_ADDRESS = Address.FromString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFKQ");

        /// <summary>
        /// Get the compiled TEAL contracts for the auction.
        /// </summary>
        /// <param name="client">An algod client that has the ability to compile TEAL programs.</param>
        /// <returns>A tuple of 2 byte strings. The first is the approval program, and the second is the clear state program.</returns>
        public static async UniTask<(byte[], byte[])> GetContracts(IAlgodClient client)
        {
            if (APPROVAL_PROGRAM == null || APPROVAL_PROGRAM.Length == 0)
            {
                string projectPath = Directory.GetCurrentDirectory();
                APPROVAL_PROGRAM = await Util.FullyCompileContract(client, Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\AuctionDemo\auction_approval.teal"));
                CLEAR_STATE_PROGRAM = await Util.FullyCompileContract(client, Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\AuctionDemo\auction_clear_state.teal"));
            }
            return (APPROVAL_PROGRAM, CLEAR_STATE_PROGRAM);
        }

        /// <summary>
        /// Create a new auction.
        /// </summary>
        /// <param name="client">An algod client.</param>
        /// <param name="sender">The account that will create the auction application.</param>
        /// <param name="seller">The address of the seller that currently holds the NFT being auctioned.</param>
        /// <param name="nftId">The ID of the NFT being auctioned.</param>
        /// <param name="startTime">A UNIX timestamp representing the start time of the auction. This must be greater than the current UNIX timestamp.</param>
        /// <param name="endTime">A UNIX timestamp representing the end time of the auction. This must be greater than startTime.</param>
        /// <param name="reserve">
        /// The reserve amount of the auction. If the auction ends without a bid that is equal to or greater than this amount, 
        /// the auction will fail, meaning the bid amount will be refunded to the lead bidder and the NFT will return to the seller.
        /// </param>
        /// <param name="minBidIncrement">The minimum different required between a new bid and the current leading bid.</param>
        /// <returns>The ID of the newly created auction app.</returns>
        public static async UniTask<ulong> CreateAuctionApp(IAlgodClient client, Account sender, Address seller, ulong nftId, ulong startTime, ulong endTime, ulong reserve, ulong minBidIncrement)
        {
            var (approval, clear) = await GetContracts(client);

            StateSchema globalSchema = new StateSchema() { NumUints = 7, NumByteSlices = 2 };
            StateSchema localSchema = new StateSchema() { NumUints = 0, NumByteSlices = 0 };

            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CreateAuctionApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return default;
            }

            AppCallTxn txn = Transaction.AppCreate(sender.Address, txnParams, approval, clear, globalSchema, localSchema);
            txn.OnComplete = OnCompletion.NoOp;

            List<byte[]> appArgs = new List<byte[]>();
            appArgs.Add(seller.ToPublicKey().ToArray());
            appArgs.Add(nftId.ToBytesBigEndian(Allocator.Temp).ToArray());
            appArgs.Add(startTime.ToBytesBigEndian(Allocator.Temp).ToArray());
            appArgs.Add(endTime.ToBytesBigEndian(Allocator.Temp).ToArray());
            appArgs.Add(reserve.ToBytesBigEndian(Allocator.Temp).ToArray());
            appArgs.Add(minBidIncrement.ToBytesBigEndian(Allocator.Temp).ToArray());

            /*
                app_args = [
                encoding.decode_address(seller),
                nftID.to_bytes(8, "big"),
                startTime.to_bytes(8, "big"),
                endTime.to_bytes(8, "big"),
                reserve.to_bytes(8, "big"),
                minBidIncrement.to_bytes(8, "big"),
            ]
            */

            CompiledTeal[] compiledTeals = new CompiledTeal[appArgs.Count];
            for(int i = 0; i < compiledTeals.Length; i++)
            {
                compiledTeals[i] = appArgs[i];
            }

            txn.AppArguments = compiledTeals;

            var signedTxn = txn.Sign(sender.PrivateKey.ToKeyPair().SecretKey);

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CreateAuctionApp] Algod SendTransaction error: {sendTxnError.Message}");
                return 0;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CreateAuctionApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return 0;
            }

            if (pendingTxn.ApplicationIndex == 0)
            {
                Debug.LogError($"[CreateAuctionApp] Application index is 0!");
            }

            return pendingTxn.ApplicationIndex;
        }

        /// <summary>
        /// Finish setting up an auction.
        /// This operation funds the app auction escrow account, opts that account into
        /// the NFT, and sends the NFT to the escrow account, all in one atomic
        /// transaction group.The auction must not have started yet.
        /// 
        /// The escrow account requires a total of 0.203 Algos for funding.See the code
        /// below for a breakdown of this amount.
        /// </summary>
        /// <param name="client">An algod client.</param>
        /// <param name="appId">The app ID of the auction.</param>
        /// <param name="funder">The account providing the funding for the escrow account.</param>
        /// <param name="nftHolder">The account holding the NFT.</param>
        /// <param name="nftId">The NFT ID.</param>
        /// <param name="nftAmount">The NFT amount being auctioned.Some NFTs has a total supply
        /// of 1, while others are fractional NFTs with a greater total supply,
        /// so use a value that makes sense for the NFT being auctioned.
        /// </param>
        /// <returns></returns>
        public static async UniTask SetupAuctionApp(IAlgodClient client, ulong appId, Account funder, Account nftHolder, ulong nftId, ulong nftAmount)
        {
            var appResponse = await client.GetApplication(appId);
            if (appResponse.Error.IsError)
            {
                Debug.LogError($"[SetupAuctionApp] Algod GetApplication failed: {appResponse.Error.Message}");
                return;
            }

            Address appAddress = appResponse.Payload.GetAddress();

            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[SetupAuctionApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            ulong fundingAmount =
                //min account balance
                100_000
                //additional min balance to opt into NFT
                + 100_000
                //3 * min txn fee
                + 3 * 1_000;

            PaymentTxn fundAppTxn = Transaction.Payment(
                sender: funder.Address,
                receiver: appAddress,
                amount: fundingAmount,
                txnParams: txnParams
            );

            CompiledTeal[] appArgs = new CompiledTeal[1];
            appArgs[0].Bytes = System.Text.Encoding.UTF8.GetBytes("setup");

            AppCallTxn setupTxn = Transaction.AppCall(
                sender: funder.Address,
                applicationId: appId,
                txnParams: txnParams,
                onComplete: OnCompletion.NoOp,
                appArguments: appArgs,
                foreignAssets: new ulong[] { nftId }
            );

            AssetTransferTxn fundNftTxn = Transaction.AssetTransfer(
                sender: nftHolder.Address,
                assetReceiver: appAddress,
                xferAsset: nftId,
                assetAmount: nftAmount,
                txnParams: txnParams
            );

            var groupId = Transaction.GetGroupId(fundAppTxn.GetId(), setupTxn.GetId(), fundNftTxn.GetId());
            fundAppTxn.Group = groupId;
            setupTxn.Group = groupId;
            fundNftTxn.Group = groupId;

            var signedFundAppTxn = fundAppTxn.Sign(funder.PrivateKey.ToKeyPair().SecretKey);
            var signedSetupTxnn = setupTxn.Sign(funder.PrivateKey.ToKeyPair().SecretKey);
            var signedFundNftTxn = fundNftTxn.Sign(nftHolder.PrivateKey.ToKeyPair().SecretKey);

            var (sendTxnError, txid) = await client.SendTransactions(signedFundAppTxn, signedSetupTxnn, signedFundNftTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[SetupAuctionApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[SetupAuctionApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
        }

        /// <summary>
        /// Place a bid on an active auction.
        /// </summary>
        /// <param name="client">An Algod client.</param>
        /// <param name="appId">The app ID of the auction.</param>
        /// <param name="bidder">The account providing the bid.</param>
        /// <param name="bidAmount">The amount of the bid.</param>
        /// <returns></returns>
        public static async UniTask PlaceBid(IAlgodClient client, ulong appId, Account bidder, ulong bidAmount)
        {
            var appResponse = await client.GetApplication(appId);
            if (appResponse.Error.IsError)
            {
                Debug.LogError($"[PlaceBid] Algod GetApplication failed: {appResponse.Error.Message}");
                return;
            }

            Address appAddress = appResponse.Payload.GetAddress();
            var appGlobalState = await Util.GetAppGlobalState(client, appId);

            TealValue value = default;
            if (!appGlobalState.TryGetValue("nft_id", out value))
            {
                Debug.LogError($"[PlaceBid] Unable to get nft_id from app global state");
                return;
            }
            ulong nftId = value.UintValue;

            Address? prevBidLeader = GetAddressFromAppState(appGlobalState, "bid_account");
            if(!prevBidLeader.HasValue || prevBidLeader.Value == ZERO_ADDRESS)
            {
                Debug.Log("No previous bidder found. Placing first bid!");
            }
            else
            {
                Debug.Log($"Previous bidder found: { prevBidLeader.Value }");
            }

            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[PlaceBid] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var payTxn = Transaction.Payment(
                sender: bidder.Address,
                receiver: appAddress,
                amount: bidAmount,
                txnParams: txnParams
            );

            CompiledTeal[] appArgs = new CompiledTeal[1];
            appArgs[0].Bytes = System.Text.Encoding.UTF8.GetBytes("bid");

            AppCallTxn appCallTxn = Transaction.AppCall(
                sender: bidder.Address,
                applicationId: appId,
                onComplete: OnCompletion.NoOp,
                appArguments: appArgs,
                foreignAssets: new ulong[] { nftId },
                accounts: prevBidLeader.HasValue ? new Address[] { prevBidLeader.Value } : null,
                txnParams: txnParams
            );

            var groupId = Transaction.GetGroupId(payTxn.GetId(), appCallTxn.GetId());
            payTxn.Group = groupId;
            appCallTxn.Group = groupId;

            var signedPayTxn = payTxn.Sign(bidder.PrivateKey.ToKeyPair().SecretKey);
            var signedAppCallTxn = appCallTxn.Sign(bidder.PrivateKey.ToKeyPair().SecretKey);

            var (sendTxnError, txid) = await client.SendTransactions(signedPayTxn, signedAppCallTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[PlaceBid] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[PlaceBid] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
        }

        /// <summary>
        /// Close an auction.
        /// 
        /// This action can only happen before an auction has begun, in which case it is
        /// cancelled, or after an auction has ended.
        /// 
        /// If called after the auction has ended and the auction was successful, the
        /// NFT is transferred to the winning bidder and the auction proceeds are
        /// transferred to the seller. If the auction was not successful, the NFT and
        /// all funds are transferred to the seller.
        /// </summary>
        /// <param name="client">An Algod client.</param>
        /// <param name="appId">The app ID of the auction.</param>
        /// <param name="closer">The account initiating the close transaction. This must be
        /// either the seller or auction creator if you wish to close the
        /// auction before it starts. Otherwise, this can be any account.
        /// </param>
        public static async UniTask CloseAuction(IAlgodClient client, ulong appId, Account closer)
        {
            var appResponse = await client.GetApplication(appId);
            if (appResponse.Error.IsError)
            {
                Debug.LogError($"[CloseAuction] Algod GetApplication failed: {appResponse.Error.Message}");
                return;
            }

            Address appAddress = appResponse.Payload.GetAddress();
            var appGlobalState = await Util.GetAppGlobalState(client, appId);

            TealValue value = default;
            if (!appGlobalState.TryGetValue("nft_id", out value))
            {
                Debug.LogError($"[CloseAuction] Unable to get nft_id from app global state");
                return;
            }
            ulong nftId = value.UintValue;

            Address? seller = GetAddressFromAppState(appGlobalState, "seller");
            Address? bidder = GetAddressFromAppState(appGlobalState, "bid_account");

            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CloseAuction] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var deleteTxn = Transaction.AppDelete(
                sender: closer.Address,
                applicationId: appId,
                accounts: new Address[] { seller.Value, bidder.Value },
                foreignAssets: new ulong[] { nftId },
                txnParams: txnParams
            );

            var signedDeleteTxn = deleteTxn.Sign(closer.PrivateKey.ToKeyPair().SecretKey);

            var (sendTxnError, txid) = await client.SendTransaction(signedDeleteTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CloseAuction] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CloseAuction] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
        }

        private static Address? GetAddressFromAppState(Dictionary<string, TealValue> globalAppState, string key)
        {
            Address? address = null;
            if (globalAppState.ContainsKey(key))
            {
                byte[] publicKeyBytes = globalAppState[key].Bytes.ToArray();
                Ed25519.PublicKey publicKey = default;
                publicKey.CopyFrom(publicKeyBytes, 0);
                address = Address.FromPublicKey(publicKey);
            }
            return address;
        }
    }
}
