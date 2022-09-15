using AlgoSdk.Algod;
using AlgoSdk.Examples.AuctionDemo;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using UnityEngine;

namespace AlgoSdk.Examples.RockPaperScissors
{
    public class RockPaperScissorsExample : MonoBehaviour
    {
        void Start() => Run().Forget();

        public static async UniTask Run()
        {
            Debug.Log("Rock Paper Scissors demo started!");

            Setup setup = new Setup();
            AlgodClient client = setup.AlgodClient;

            Debug.Log("Generating temporary accounts...");
            Account challenger = await AuctionDemo.Resources.GetTemporaryAccount(client);
            Account opponent = await AuctionDemo.Resources.GetTemporaryAccount(client);
            Debug.Log($"Alice (challenger account): {challenger.Address}");
            Debug.Log($"Bob (opponent account): {opponent.Address}");

            Debug.Log($"Alice is creating the rock paper scissors app");
            AppIndex appId = await CreateApp(
                client: client,
                sender: challenger
            );

            Address appAddress = appId.GetAppAddress();
            Debug.Log($"Done. The app ID is {appId.Index} and the escrow account is {appAddress}");

            Debug.Log("Both Alice and Bob are opting in to the app.");
            await Optin(client, challenger, appId);
            await Optin(client, opponent, appId);

            Func<UniTask> logBalances = async () =>
            {
                var bobBalances = await Util.GetBalances(client, opponent.Address);
                Debug.Log($"Bob's balances: {bobBalances.ToDebugString()}");

                var aliceBalances = await Util.GetBalances(client, challenger.Address);
                Debug.Log($"Alice's balances: {aliceBalances.ToDebugString()}");

                var appBalances = await Util.GetBalances(client, appAddress);
                Debug.Log($"App/Escrow's balances: {appBalances.ToDebugString()}");
            };

            Debug.Log("All participants balances before the game:");
            await logBalances();

            Debug.Log("Alice is creating and setting up the challenge between her and Bob!");
            ulong challengeWager = 123456;
            await SetupChallenge(client, challenger, opponent, appId, appAddress, challengeWager);

            Debug.Log("Bob accepts the challenge and sends his play!");
            await AcceptChallenge(client, challenger, opponent, appId, appAddress, challengeWager);

            Debug.Log("Alice reveals her play!");
            await RevealChallengerPlay(client, challenger, opponent, appId);

            Debug.Log("Bob wins the game!");

            Debug.Log("All participants balances after the game:");
            await logBalances();

            Debug.Log("Alices cleans up the challenge!");
            await Cleanup(client, appId, challenger);

            Debug.Log("Rock Paper Scissors demo finished!");
        }

        private static async UniTask<AppIndex> CreateApp(AlgodClient client, Account sender)
        {
            var (approval, clear) = await GetContracts(client);

            StateSchema globalSchema = new StateSchema() { NumUints = 0, NumByteSlices = 0 };
            StateSchema localSchema = new StateSchema() { NumUints = 1, NumByteSlices = 3 };

            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"Algod GetSuggestedParams error: {txnParamsError.Message}");
                return default;
            }

            AppCallTxn txn = Transaction.AppCreate(
                sender: sender.Address,
                txnParams: txnParams,
                approvalProgram: approval,
                clearStateProgram: clear,
                globalStateSchema: globalSchema,
                localStateSchema: localSchema
            );

            var signedTxn = sender.SignTxn(txn);

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"Algod SendTransaction error: {sendTxnError.Message}");
                return 0;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {pendingErr.Message}");
                return 0;
            }

            if (pendingTxn.ApplicationIndex == 0)
            {
                Debug.LogError($"Application index is 0!");
            }

            return (AppIndex)pendingTxn.ApplicationIndex.Value;
        }

        public static async UniTask<(byte[], byte[])> GetContracts(AlgodClient client)
        {
            string projectPath = Directory.GetCurrentDirectory();
            var approval = await Util.FullyCompileContract(client, Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\RockPaperScissors\python\compiled\approval.teal"));
            var clear = await Util.FullyCompileContract(client, Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\RockPaperScissors\python\compiled\clear.teal"));
            return (approval, clear);
        }

        static async UniTask Optin(AlgodClient client, Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppOptIn(
                sender: sender.Address,
                txnParams: txnParams,
                applicationId: appId
            );

            var signedTxn = sender.SignTxn(txn);

            var (txnErr, txnResponse) = await client.SendTransaction(signedTxn);
            if (txnErr.IsError)
            {
                Debug.LogError($"[EnsureOptedIn] Algod SendTransaction failed: {txnErr.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txnResponse.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
        }

        private static async UniTask SetupChallenge(AlgodClient client, Account challenger, Account opponent, ulong appId, Address appAddress, ulong wager)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            //todo
            //this should be part of the challenge setup like in the auction demo, but it wasn't part of the pyteal class!
            //this is needed because the app has to hold a min balance
            PaymentTxn fundAppTxn = Transaction.Payment(
                sender: challenger.Address,
                receiver: appAddress,
                amount: 100_000, //min account balance
                txnParams: txnParams
            );

            var signedFundAppTxn = challenger.SignTxn(fundAppTxn);

            var (sendTxnError, txnResponse) = await client.SendTransaction(signedFundAppTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txnResponse.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            var appArgs = new CompiledTeal[]
            {
                "challenge",
                Convert.FromBase64String("1fK8biazjYeoGhCPrUE5MXRpOMp2fvRn8wnfvoTtM5A=") //challenge id hash b64
            };

            AppCallTxn callTxn = Transaction.AppCall(
                sender: challenger.Address,
                txnParams: txnParams,
                applicationId: appId,
                appArguments: appArgs,
                accounts: new Address[] { opponent.Address }
            );

            PaymentTxn challengeWagerTxn = Transaction.Payment(
                sender: challenger.Address,
                receiver: appAddress,
                amount: wager,
                txnParams: txnParams
            );

            var atomic = Transaction.Atomic();
            atomic.AddTxn(callTxn);
            atomic.AddTxn(challengeWagerTxn);
            var signedTxns = atomic.Build().SignWith(challenger);
            try
            {
                var response = await signedTxns.Submit(client);
                await response.Confirm(10);
            }
            catch(AlgoApiException error)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {error.Message}");
            }
        }

        private static async UniTask AcceptChallenge(AlgodClient client, Account challenger, Account opponent, ulong appId, Address appAddress, ulong wager)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var appArgs = new CompiledTeal[]
            {
                "accept",
                "r" //opponent play is rock
            };

            AppCallTxn callTxn = Transaction.AppCall(
                sender: opponent.Address,
                txnParams: txnParams,
                applicationId: appId,
                appArguments: appArgs,
                accounts: new Address[] { challenger.Address }
            );
            PaymentTxn fundAppTxn = Transaction.Payment(
                sender: opponent.Address,
                receiver: appAddress,
                amount: wager,
                txnParams: txnParams
            );


            var atomic = Transaction.Atomic();
            atomic.AddTxn(callTxn);
            atomic.AddTxn(fundAppTxn);
            var signedTxns = atomic.Build().SignWith(opponent);
            try
            {
                var response = await signedTxns.Submit(client);
                await response.Confirm(10);
            }
            catch(AlgoApiException error)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {error.Message}");
            }
        }

        private static async UniTask RevealChallengerPlay(AlgodClient client, Account challenger, Account opponent, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var appArgs = new CompiledTeal[]
            {
                "reveal",
                "s-143298479749479749" //challenger play is scissors hashed with the challenge id hash in b64
            };

            txnParams.Fee = 3000; //app sends out transactions

            AppCallTxn callTxn = Transaction.AppCall(
                sender: challenger.Address,
                txnParams: txnParams,
                applicationId: appId,
                appArguments: appArgs,
                accounts: new Address[] { opponent.Address }
            );

            var signedCallTxn = challenger.SignTxn(callTxn);

            var (sendTxnError, txnResponse) = await client.SendTransaction(signedCallTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txnResponse.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
        }

        public static async UniTask Cleanup(AlgodClient client, ulong appId, Account closer)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            txnParams.Fee = 2 * txnParams.MinFee; //get min account balance from app back

            var deleteTxn = Transaction.AppDelete(
                sender: closer.Address,
                applicationId: appId,
                txnParams: txnParams
            );

            var signedDeleteTxn = closer.SignTxn(deleteTxn);

            var (sendTxnError, txnResponse) = await client.SendTransaction(signedDeleteTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txnResponse.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
        }
    }
}
