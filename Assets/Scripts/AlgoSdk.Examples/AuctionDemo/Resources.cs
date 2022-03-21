using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Resources
    {
        const ulong FUNDING_AMOUNT = 100_000_000;
        const int TEMPORARY_ACCOUNTS = 16;
        static Stack<Account> accountList = new Stack<Account>(TEMPORARY_ACCOUNTS);

        public static async UniTask<AlgoApiResponse<PendingTransaction>> PayAccount(IAlgodClient client, Account sender, Address to, ulong amount)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[PayAccount] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return default;
            }

            var paymentTxn = Transaction.Payment(
                sender: sender.Address,
                txnParams: txnParams,
                receiver: to,
                amount: amount
            );

            var signedTxn = paymentTxn.Sign(sender.SecretKey);

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[PayAccount] Algod SendTransaction error: {sendTxnError.Message}");
                return default;
            }

            return await Util.WaitForTransaction(client, txid);
        }

        public static async UniTask<AlgoApiResponse<PendingTransaction>> FundAccount(IAlgodClient client, Address address, ulong amount = FUNDING_AMOUNT)
        {
            Setup setup = new Setup();
            List<Account> genesisAccounts = await setup.GetGenesisAccounts();
            Account fundingAccount = genesisAccounts[UnityEngine.Random.Range(0, genesisAccounts.Count)];
            return await PayAccount(client, fundingAccount, address, amount);
        }

        public static async UniTask<Account> GetTemporaryAccount(IAlgodClient client)
        {
            if (accountList.Count > 0)
            {
                return accountList.Pop();
            }

            List<Account> accounts = new List<Account>();
            for (int i = 0; i < TEMPORARY_ACCOUNTS; ++i)
            {
                accountList.Push(AlgoSdk.Account.GenerateAccount().Item1);
            }

            Setup setup = new Setup();
            List<Account> genesisAccounts = await setup.GetGenesisAccounts();
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[GetTemporaryAccount] Algod GetSuggestedParams error: {txnParamsError.Message}");
                accountList.Clear();
                return default;
            }

            List<PaymentTxn> txns = new List<PaymentTxn>();
            for (int i = 0; i < accountList.Count; ++i)
            {
                Account fundingAccount = genesisAccounts[i % genesisAccounts.Count];
                txns.Add(Transaction.Payment(
                    sender: fundingAccount.Address,
                    txnParams: txnParams,
                    receiver: accountList.ToArray()[i].Address,
                    amount: FUNDING_AMOUNT
                ));
            }

            var groupId = Transaction.GetGroupId(txns.Select(x => x.GetId()).ToArray());

            List<Signed<PaymentTxn>> signedTxns = new List<Signed<PaymentTxn>>();
            for (int i = 0; i < txns.Count; ++i)
            {
                Account fundingAccount = genesisAccounts[i % genesisAccounts.Count];
                PaymentTxn txn = txns[i];
                txn.Group = groupId;
                signedTxns.Add(txn.Sign(fundingAccount.SecretKey));
            }

            var signedTxnsArray = signedTxns.Select(x => x.ToUntyped()).ToArray();
            var (txnErr, txid) = await client.SendTransactions(signedTxnsArray);
            if (txnErr.IsError)
            {
                Debug.LogError($"[GetTemporaryAccount] Algod SendTransactions failed: {txnErr.Message}");
                accountList.Clear();
                return default;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[GetTemporaryAccount] Algod WaitForTransaction failed: {pendingErr.Message}");
                accountList.Clear();
                return default;
            }

            return accountList.Pop();
        }

        public static async UniTask<AlgoApiResponse<PendingTransaction>> EnsureOptedIn(IAlgodClient client, ulong assetId, PrivateKey account)
        {
            var (err, accountInfo) = await client.GetAccountInformation(account.ToAddress());
            if (err.IsError)
            {
                Debug.LogError($"[EnsureOptedIn] Algod GetAccountInformation failed: {err.Message}");
                return default;
            }

            if (accountInfo.Assets?.Any(a => a.AssetId == assetId) ?? false)
            {
                Debug.Log($"[EnsureOptedIn] Account already opted in to assetId {assetId}");
                return default;
            }

            using var kp = account.ToKeyPair();
            var (_, txnParams) = await client.GetSuggestedParams();
            var assetOptInTxn = Transaction.AssetAccept(
                kp.PublicKey,
                txnParams,
                assetId
            );

            var signedTxn = assetOptInTxn.Sign(kp.SecretKey);
            var (txnErr, txid) = await client.SendTransaction(signedTxn);
            if (txnErr.IsError)
            {
                Debug.LogError($"[EnsureOptedIn] Algod SendTransaction failed: {txnErr.Message}");
                return default;
            }

            return await Util.WaitForTransaction(client, txid);
        }

        public static async UniTask<ulong> CreateDummyAsset(IAlgodClient client, ulong total, Account account = default)
        {
            if (Equals(account, default(Account)))
            {
                account = await GetTemporaryAccount(client);
            }

            int randomNumber = UnityEngine.Random.Range(0, 999);
            //this random note reduces the likelihood of this transaction looking like a duplicate
            System.Random rnd = new System.Random();
            byte[] randomNote = new byte[20];
            rnd.NextBytes(randomNote);

            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CreateDummyAsset] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return 0;
            }

            AssetParams assetParams = new AssetParams()
            {
                Creator = account.Address,
                Total = total,
                Decimals = 0,
                DefaultFrozen = false,
                Manager = account.Address,
                Reserve = account.Address,
                Freeze = account.Address,
                Clawback = account.Address,
                UnitName = $"D{randomNumber}",
                Name = $"Dummy {randomNumber}",
                Url = string.Format("{0}{1}", "https://dummy.asset/", randomNumber)
            };

            AssetConfigTxn txn = Transaction.AssetCreate(account.Address, txnParams, assetParams);
            txn.Note = randomNote;

            var signedTxn = txn.Sign(account.SecretKey);
            var (txnErr, txid) = await client.SendTransaction(signedTxn);
            if (txnErr.IsError)
            {
                Debug.LogError($"[CreateDummyAsset] Algod SendTransaction failed: {txnErr.Message}");
                return 0;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CreateDummyAsset] Algod WaitForTransaction failed: {pendingErr.Message}");
                return 0;
            }

            if (pendingTxn.AssetIndex == 0)
            {
                Debug.LogError("[CreateDummyAsset] Asset index is 0");
            }

            return pendingTxn.AssetIndex;
        }
    }
}
