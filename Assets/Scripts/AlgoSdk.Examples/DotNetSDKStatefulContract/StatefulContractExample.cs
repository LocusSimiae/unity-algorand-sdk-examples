using AlgoSdk.Algod;
using AlgoSdk.Examples.AuctionDemo;
using AlgoSdk.LowLevel;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace AlgoSdk.Examples.StatefulContract
{
    //this example's origin is https://github.com/RileyGe/dotnet-algorand-sdk/blob/master/sdk-examples/V2/contract/StatefulContract.cs
    public class StatefulContractExample : MonoBehaviour
    {
        private void Start() => Run().Forget();

        public static async UniTask Run()
        {
            Debug.Log("Started stateful contract example!");

            AuctionDemo.Setup setup = new AuctionDemo.Setup();
            AlgodClient client = setup.AlgodClient;

            Debug.Log("Generating temporary accounts...");

            //used to create and sign the smart contract
            Account admin = new Account(Mnemonic.FromString("place blouse sad pigeon wing warrior wild script problem team blouse camp soldier breeze twist mother vanish public glass code arrow execute convince ability there").ToPrivateKey());

            // create two account to create and user the stateful contract
            Account creator = new Account(Mnemonic.FromString("benefit once mutual legal marble hurdle dress toe fuel country prepare canvas barrel divide major square name captain calm flock crumble receive economy abandon power").ToPrivateKey());
            Account user = new Account(Mnemonic.FromString("pledge become mouse fantasy matrix bunker ask tissue prepare vocal unit patient cliff index train network intact company across stage faculty master mom abstract above").ToPrivateKey());

            await AuctionDemo.Resources.FundAccount(client, admin.Address);
            await AuctionDemo.Resources.FundAccount(client, creator.Address);
            await AuctionDemo.Resources.FundAccount(client, user.Address);

            Debug.Log($"admin account: {admin.Address}");
            Debug.Log($"creator account: {creator.Address}");
            Debug.Log($"user account: {user.Address}");

            // declare application state storage (immutable)
            StateSchema globalSchema = new StateSchema() { NumUints = 1, NumByteSlices = 0 };
            StateSchema localSchema = new StateSchema() { NumUints = 1, NumByteSlices = 1 };

            string projectPath = Directory.GetCurrentDirectory();
            string approvalPath = Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\DotNetSDKStatefulContract\stateful_approval_init.teal");
            string approvalRefactPath = Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\DotNetSDKStatefulContract\stateful_approval_refact.teal");
            string clearPath = Path.Combine(projectPath, @"Assets\Scripts\AlgoSdk.Examples\DotNetSDKStatefulContract\stateful_clear.teal");

            // user declared approval program (initial)
            byte[] approvalProgram = await AuctionDemo.Util.FullyCompileContract(client, approvalPath);

            // user declared approval program (refactored)
            byte[] approvalProgramRefactored = await AuctionDemo.Util.FullyCompileContract(client, approvalRefactPath);
            // creator 53GNUYJSTKGEHAVYE5ZS65YTVJSYZSJ7KJBWNQT3MJESCOKNOWEBYTLVA4
            // user GG7UDCTXNHADKSJ22GG64BZNKXXLXMSYWVZDD2UGHBZ6RLVXWGRLMW52DU
            // declare clear state program source
            byte[] clearProgram = await AuctionDemo.Util.FullyCompileContract(client, clearPath);

            // create new application
            ulong appId = await CreateApp(client, creator, approvalProgram, clearProgram, globalSchema, localSchema);

            // opt-in to application
            await OptIn(client, user, appId);

            // call application without arguments
            await CallApp(client, user, appId, null);

            // read local state of application from user account
            await ReadLocalState(client, user, appId);

            // read global state of application
            await ReadGlobalState(client, creator, appId);

            // update application
            await UpdateApp(client, creator, appId, approvalProgramRefactored, clearProgram);

            // call application with arguments
            string date = DateTime.Now.ToString("yyyy-MM-dd 'at' HH:mm:ss");
            Debug.Log($"Sending app argument: {date}");
            CompiledTeal[] appArgs = new CompiledTeal[] { System.Text.Encoding.UTF8.GetBytes(date) };
            await CallApp(client, user, appId, appArgs);

            // read local state of application from user account
            await ReadLocalState(client, user, appId);

            // read global state of application
            await ReadGlobalState(client, creator, appId);

            // close-out from application
            await CloseOutApp(client, user, appId);

            // opt-in again to application
            await OptIn(client, user, appId);

            // call application with arguments
            await CallApp(client, user, appId, appArgs);

            // read local state of application from user account
            await ReadLocalState(client, user, appId);

            // read global state of application
            await ReadGlobalState(client, creator, appId);

            // delete application
            await DeleteApp(client, creator, appId);

            // clear application from user account
            await ClearApp(client, user, appId);

            Debug.Log("Finished stateful contract example!");
        }

        static async UniTask<ulong> CreateApp(AlgodClient client, Account creator, byte[] approvalProgram, byte[] clearProgram, StateSchema globalSchema, StateSchema localSchema)
        {
            
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CreateApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return default;
            }

            AppCallTxn txn = Transaction.AppCreate(creator.Address, txnParams, approvalProgram, clearProgram, globalSchema, localSchema);

            var signedTxn = creator.SignTxn(txn);
            Debug.Log("[CreateApp] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CreateApp] Algod SendTransaction error: {sendTxnError.Message}");
                return 0;
            }
            Debug.Log("[CreateApp] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CreateApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return 0;
            }

            if (pendingTxn.ApplicationIndex == 0)
            {
                Debug.LogError($"[CreateApp] Application index is 0!");
            }
            Debug.Log("[CreateApp] App id is: " + pendingTxn.ApplicationIndex.Value);

            return pendingTxn.ApplicationIndex;
        }

        static async UniTask OptIn(AlgodClient client, Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[OptIn] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            AppCallTxn optInTnx = Transaction.AppOptIn(
                sender: sender.Address,
                applicationId: appId,
                txnParams: txnParams
            );

            var signedTxn = sender.SignTxn(optInTnx);
            Debug.Log("[OptIn] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[OptIn] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[OptIn] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[OptIn] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[OptIn] Address { sender.Address } optin to Application with ID { appId }");
        }

        static async UniTask CloseOutApp(AlgodClient client, Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CloseOutApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppCloseOut(
                sender: sender.Address,
                applicationId: appId,
                txnParams: txnParams
            );

            var signedTxn = sender.SignTxn(txn);
            Debug.Log("[CloseOutApp] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CloseOutApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[CloseOutApp] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CloseOutApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[CloseOutApp] Application (ID: { appId }) close out confirmed with round: { pendingTxn.ConfirmedRound.Value }");
        }

        static async UniTask UpdateApp(AlgodClient client, Account creator, ulong appId, byte[] approvalProgram, byte[] clearProgram)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[UpdateApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppUpdateTxn(
                sender: creator.Address,
                applicationId: appId,
                approvalProgram: approvalProgram,
                clearStateProgram: clearProgram,
                txnParams: txnParams
            );

            var signedTxn = creator.SignTxn(txn);
            Debug.Log("[UpdateApp] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[UpdateApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[UpdateApp] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[UpdateApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[UpdateApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound.Value }");
        }

        static async UniTask DeleteApp(AlgodClient client, Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[DeleteApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppDelete(
                sender: sender.Address,
                applicationId: appId,
                txnParams: txnParams
            );

            var signedTxn = sender.SignTxn(txn);
            Debug.Log("[DeleteApp] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[DeleteApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[DeleteApp] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[DeleteApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[DeleteApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound.Value }");
        }

        static async UniTask ClearApp(AlgodClient client, Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[ClearApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppClearState(
                sender: sender.Address,
                applicationId: appId,
                txnParams: txnParams
            );

            var signedTxn = sender.SignTxn(txn);
            Debug.Log("[ClearApp] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[ClearApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[ClearApp] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[ClearApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[ClearApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound.Value }");
        }

        static async UniTask CallApp(AlgodClient client, Account sender, ulong appId, CompiledTeal[] args)
        {
            var (txnParamsError, txnParams) = await client.TransactionParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CallApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppCall(
                sender: sender.Address,
                applicationId: appId,
                appArguments: args,
                txnParams: txnParams
            );

            var signedTxn = sender.SignTxn(txn);
            Debug.Log("[CallApp] Signed transaction");

            var (sendTxnError, response) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CallApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[CallApp] Sent transaction");

            var (pendingErr, pendingTxn) = await AuctionDemo.Util.WaitForTransaction(client, response.TxId);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CallApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
            Debug.Log($"[CallApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound.Value }");

            Debug.Log($"[CallApp] Global state delta: { GetDeltaStateValues(pendingTxn.GlobalStateDelta.WrappedValue) }");
            foreach (var accountDelta in pendingTxn.LocalStateDelta?.Where(x => x.Address == sender.Address))
            {
                Debug.Log($"[CallApp] Local state delta: { GetDeltaStateValues(accountDelta.Delta.WrappedValue) }");
            }
        }

        static async UniTask ReadLocalState(AlgodClient client, Account account, ulong appId)
        {
            var (error, accountInfo) = await client.AccountInformation(account.Address);
            if (error.IsError)
            {
                Debug.LogError($"[ReadLocalState] Algod GetAccountInformation failed: {error.Message}");
                return;
            }

            foreach (var state in accountInfo.Account.AppsLocalState.Where(x => x.Id == appId))
            {
                Debug.Log($"[ReadLocalState] User's application local state: { GetAppStateValues(state.KeyValue.WrappedValue) }");
            }
        }

        static async UniTask ReadGlobalState(AlgodClient client, Account account, ulong appId)
        {
            var (error, accountInfo) = await client.AccountInformation(account.Address);
            if (error.IsError)
            {
                Debug.LogError($"[ReadGlobalState] Algod GetAccountInformation failed: {error.Message}");
                return;
            }

            foreach (var app in accountInfo.Account.CreatedApps.Where(x => x.Id == appId))
            {
                Debug.Log($"[ReadGlobalState] Application global state: { GetAppStateValues(app.Params.GlobalState) }");
            }
        }

        static string GetAppStateValues(TealKeyValue[] keyValues)
        {
            if (keyValues == null) return "";
            string outStr = "";
            foreach (var v in keyValues)
            {
                FixedString128Bytes encodedKey = v.Key;
                FixedString128Bytes decodedKey = default;
                encodedKey.Base64ToUtf8(ref decodedKey);
                outStr += "key: " + decodedKey.Value + "; value: ";

                Algod.TealValue value = v.Value;
                if (value.Type == (ulong)TealValueType.Uint)
                {
                    outStr += "(uint) " + value.Uint;
                }
                else if (value.Type == (ulong)TealValueType.Bytes)
                {
                    //this only works because UTF8 bytes were sent
                    FixedString128Bytes bytes = value.Bytes;
                    FixedString128Bytes decodedBytes = default;
                    bytes.Base64ToUtf8(ref decodedBytes);
                    outStr += "(bytes) " + decodedBytes.Value;
                }
                else
                {
                    outStr += "(none)";
                }
                outStr += "; ";
            }

            return outStr;
        }

        static string GetDeltaStateValues(EvalDeltaKeyValue[] keyValues)
        {
            if (keyValues == null) return "";
            string outStr = "";
            foreach (var v in keyValues)
            {
                FixedString64Bytes encodedKey = v.Key;
                FixedString64Bytes decodedKey = default;
                encodedKey.Base64ToUtf8(ref decodedKey);
                outStr += "key: " + decodedKey.Value + "; value: ";

                EvalDelta value = v.Value;
                if (value.Action == (ulong)DeltaAction.SetUInt)
                {
                    outStr += "(set uint) " + value.Uint.Value;
                }
                else if (value.Action == (ulong)DeltaAction.SetBytes && !string.IsNullOrEmpty(value.Bytes))
                {
                    //this only works because UTF8 bytes were sent
                    FixedString128Bytes bytes = value.Bytes;
                    FixedString128Bytes decodedBytes = default;
                    bytes.Base64ToUtf8(ref decodedBytes);
                    outStr += "(set bytes) " + decodedBytes.Value;
                }
                else if (value.Action == (ulong)DeltaAction.Delete)
                {
                    outStr += "(delete)";
                }
                else
                {
                    outStr += "(none)";
                }
                outStr += "; ";
            }

            return outStr;
        }
    }
}
