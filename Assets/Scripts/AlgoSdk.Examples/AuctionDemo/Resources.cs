using AlgoSdk.Algod;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Resources
    {
        const ulong FUNDING_AMOUNT = 100_000_000;
        const int TEMPORARY_ACCOUNTS = 10;
        static Stack<Account> accountList = new Stack<Account>(TEMPORARY_ACCOUNTS);

        public static async UniTask<AlgoApiResponse<PendingTransactionResponse>> PayAccount(AlgodClient client, Account sender, Address to, ulong amount)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
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

            var signedTxn = sender.SignTxn(paymentTxn);

            var (sendTxnError, txResponse) = await client.RawTransaction(AlgoApiSerializer.SerializeMessagePack(signedTxn));
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[PayAccount] Algod SendTransaction error: {sendTxnError.Message}");
                return default;
            }

            return await Util.WaitForTransaction(client, txResponse.TxId);
        }

        public static async UniTask<AlgoApiResponse<PendingTransactionResponse>> FundAccount(AlgodClient client, Address address, ulong amount = FUNDING_AMOUNT)
        {
            Setup setup = new Setup();
            List<Account> genesisAccounts = await setup.GetGenesisAccounts();
            Account fundingAccount = genesisAccounts[UnityEngine.Random.Range(0, genesisAccounts.Count)];
            return await PayAccount(client, fundingAccount, address, amount);
        }

        public static async UniTask<Account> GetTemporaryAccount(AlgodClient client)
        {
            if (accountList.Count > 0)
            {
                return accountList.Pop();
            }

            for (int i = 0; i < TEMPORARY_ACCOUNTS; ++i)
            {
                accountList.Push(Account.GenerateAccount());
            }

            Setup setup = new Setup();
            List<Account> genesisAccounts = await setup.GetGenesisAccounts();
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[GetTemporaryAccount] Algod GetSuggestedParams error: {txnParamsError.Message}");
                accountList.Clear();
                return default;
            }

            var atomic = Transaction.Atomic();
            for (int i = 0; i < accountList.Count; ++i)
            {
                Account fundingAccount = genesisAccounts[i % genesisAccounts.Count];
                atomic.AddTxn(Transaction.Payment(
                    sender: fundingAccount.Address,
                    txnParams: txnParams,
                    receiver: accountList.ToArray()[i].Address,
                    amount: FUNDING_AMOUNT
                ));
            }
            var signer = atomic.Build();
            foreach (var account in genesisAccounts)
                signer = signer.SignWith(account);

            try
            {
                var response = await signer.Submit(client);
                await response.Confirm(10);
            }
            catch (AlgoApiException error)
            {
                Debug.LogError($"[GetTemporaryAccount] Algod WaitForTransaction failed: {error.Message}");
                accountList.Clear();
                return default;
            }

            return accountList.Pop();
        }

        public static async UniTask<AlgoApiResponse<PendingTransactionResponse>> EnsureOptedIn(AlgodClient client, ulong assetId, Account account)
        {
            var (err, accountInfo) = await client.AccountInformation(account.Address);
            if (err.IsError)
            {
                Debug.LogError($"[EnsureOptedIn] Algod GetAccountInformation failed: {err.Message}");
                return default;
            }

            if (accountInfo.WrappedValue.Assets?.Any(a => a.AssetId == assetId) ?? false)
            {
                Debug.Log($"[EnsureOptedIn] Account already opted in to assetId {assetId}");
                return default;
            }

            var (_, txnParams) = await client.TransactionParams();
            var assetOptInTxn = Transaction.AssetAccept(
                account.Address,
                txnParams,
                assetId
            );

            var signedTxn = account.SignTxn(assetOptInTxn);
            var (txnErr, pendingTx) = await client.RawTransaction(AlgoApiSerializer.SerializeMessagePack(signedTxn));
            if (txnErr.IsError)
            {
                Debug.LogError($"[EnsureOptedIn] Algod SendTransaction failed: {txnErr.Message}");
                return default;
            }

            return await Util.WaitForTransaction(client, pendingTx.TxId);
        }

        public static async UniTask<ulong> CreateDummyAsset(AlgodClient client, ulong total, Account account = default)
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

            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CreateDummyAsset] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return 0;
            }

            AssetParams assetParams = new AssetParams()
            {
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

            var signedTxn = account.SignTxn(txn);
            var (txnErr, txid) = await client.RawTransaction(AlgoApiSerializer.SerializeMessagePack(signedTxn));
            if (txnErr.IsError)
            {
                Debug.LogError($"[CreateDummyAsset] Algod SendTransaction failed: {txnErr.Message}");
                return 0;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid.TxId);
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
