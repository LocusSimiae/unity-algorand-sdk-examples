using AlgoSdk.Examples.AuctionDemo;
using AlgoSdk;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using Account = AlgoSdk.Examples.AuctionDemo.Account;
using Resources = AlgoSdk.Examples.AuctionDemo.Resources;

namespace AlgoSdk.Examples.StatefulContract
{
    public class StatefulContractExample : MonoBehaviour
    {
        private void Start() => Run().Forget();

        private static async UniTaskVoid Run()
        {
            Debug.Log("Started stateful contract example!");
            
            Setup setup = new Setup();
            IAlgodClient client = setup.AlgodClient;

            Debug.Log("Generating temporary accounts...");
            //used to create and sign the smart contract
            AuctionDemo.Account admin = AuctionDemo.Account.FromMnemonic(Mnemonic.FromString("place blouse sad pigeon wing warrior wild script problem team blouse camp soldier breeze twist mother vanish public glass code arrow execute convince ability there"));

            // create two account to create and user the stateful contract
            AuctionDemo.Account creator = AuctionDemo.Account.FromMnemonic(Mnemonic.FromString("benefit once mutual legal marble hurdle dress toe fuel country prepare canvas barrel divide major square name captain calm flock crumble receive economy abandon power"));
            AuctionDemo.Account user = AuctionDemo.Account.FromMnemonic(Mnemonic.FromString("pledge become mouse fantasy matrix bunker ask tissue prepare vocal unit patient cliff index train network intact company across stage faculty master mom abstract above"));

            await Resources.FundAccount(client, admin.Address);
            await Resources.FundAccount(client, creator.Address);
            await Resources.FundAccount(client, user.Address);

            Debug.Log($"admin account: {admin.Address}");
            Debug.Log($"creator account: {creator.Address}");
            Debug.Log($"user account: {user.Address}");

            // declare application state storage (immutable)
            StateSchema globalSchema = new StateSchema() { NumUints = 1, NumByteSlices = 0 };
            StateSchema localSchema = new StateSchema() { NumUints = 1, NumByteSlices = 1 };

            string projectPath = Directory.GetCurrentDirectory();
            string approvalPath = Path.Combine(projectPath, @"Assets\AlgoSdk.Examples\DotNetSDKStatefulContract\stateful_approval_init.teal");
            string approvalRefactPath = Path.Combine(projectPath, @"Assets\AlgoSdk.Examples\DotNetSDKStatefulContract\stateful_approval_refact.teal");
            string clearPath = Path.Combine(projectPath, @"Assets\AlgoSdk.Examples\DotNetSDKStatefulContract\stateful_clear.teal");

            // user declared approval program (initial)
            byte[] approvalProgram = await Util.FullyCompileContract(client, approvalPath);

            // user declared approval program (refactored)
            byte[] approvalProgramRefactored = await Util.FullyCompileContract(client, approvalRefactPath);
            // creator 53GNUYJSTKGEHAVYE5ZS65YTVJSYZSJ7KJBWNQT3MJESCOKNOWEBYTLVA4
            // user GG7UDCTXNHADKSJ22GG64BZNKXXLXMSYWVZDD2UGHBZ6RLVXWGRLMW52DU
            // declare clear state program source
            byte[] clearProgram = await Util.FullyCompileContract(client, clearPath);

            // create new application
            ulong appId = await CreateApp(client, creator, approvalProgram, clearProgram, globalSchema, localSchema);

            // opt-in to application
            await OptIn(client, user, appId);

            // call application without arguments
            //await CallApp(client, user, appId, null); //throws error because args are null???

            // read local state of application from user account
            await ReadLocalState(client, user, appId);

            // read global state of application
            await ReadGlobalState(client, creator, appId);

            // update application
            await UpdateApp(client, creator, appId,
                approvalProgramRefactored,
                clearProgram);

            // call application with arguments
            var date = DateTime.Now;
            Debug.Log(date.ToString("yyyy-MM-dd 'at' HH:mm:ss"));
            List<byte[]> appArgs = new List<byte[]>
            {
                System.Text.Encoding.UTF8.GetBytes(date.ToString("yyyy-MM-dd 'at' HH:mm:ss"))
            };

            await CallApp(client, user, appId, appArgs);

            // read local state of application from user account
            await ReadLocalState(client, user, appId);

            // close-out from application
            await CloseOutApp(client, user, appId);

            // opt-in again to application
            await OptIn(client, user, appId);

            // call application with arguments
            await CallApp(client, user, appId, appArgs);

            // read local state of application from user account
            await ReadLocalState(client, user, appId);

            // delete application
            await DeleteApp(client, creator, appId);

            // clear application from user account
            await ClearApp(client, user, appId);

            Debug.Log("Finished stateful contract example!");
        }

        static async UniTask<ulong> CreateApp(IAlgodClient client, AuctionDemo.Account creator, byte[] approvalProgram, byte[] clearProgram, StateSchema globalSchema, StateSchema localSchema)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CreateApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return default;
            }

            AppCallTxn txn = Transaction.AppCreate(creator.Address, txnParams, approvalProgram, clearProgram, globalSchema, localSchema);

            var signedTxn = txn.Sign(creator.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[CreateApp] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CreateApp] Algod SendTransaction error: {sendTxnError.Message}");
                return 0;
            }
            Debug.Log("[CreateApp] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CreateApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return 0;
            }

            if (pendingTxn.ApplicationIndex == 0)
            {
                Debug.LogError($"[CreateApp] Application index is 0!");
            }
            Debug.Log("[CreateApp] App id is: " + pendingTxn.ApplicationIndex);

            return pendingTxn.ApplicationIndex;
        }

        static async UniTask OptIn(IAlgodClient client, AuctionDemo.Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
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

            var signedTxn = optInTnx.Sign(sender.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[OptIn] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[OptIn] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[OptIn] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[OptIn] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[OptIn] Address { sender.Address } optin to Application with ID { appId }");
        }

        static async UniTask CloseOutApp(IAlgodClient client, AuctionDemo.Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
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

            var signedTxn = txn.Sign(sender.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[CloseOutApp] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CloseOutApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[CloseOutApp] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CloseOutApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[CloseOutApp] Application (ID: { appId }) close out confirmed with round: { pendingTxn.ConfirmedRound }");
        }

        static async UniTask UpdateApp(IAlgodClient client, AuctionDemo.Account creator, ulong appId, byte[] approvalProgram, byte[] clearProgram)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
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

            var signedTxn = txn.Sign(creator.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[UpdateApp] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[UpdateApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[UpdateApp] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[UpdateApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[UpdateApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound }");
        }

        static async UniTask DeleteApp(IAlgodClient client, AuctionDemo.Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
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

            var signedTxn = txn.Sign(sender.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[DeleteApp] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[DeleteApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[DeleteApp] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[DeleteApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[DeleteApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound }");
        }

        static async UniTask ClearApp(IAlgodClient client, AuctionDemo.Account sender, ulong appId)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
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

            var signedTxn = txn.Sign(sender.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[ClearApp] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[ClearApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[ClearApp] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[ClearApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }

            Debug.Log($"[ClearApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound }");
        }

        static async UniTask CallApp(IAlgodClient client, AuctionDemo.Account sender, ulong appId, List<byte[]> args)
        {
            var (txnParamsError, txnParams) = await client.GetSuggestedParams();
            if (txnParamsError.IsError)
            {
                Debug.LogError($"[CallApp] Algod GetSuggestedParams error: {txnParamsError.Message}");
                return;
            }

            var txn = Transaction.AppCall(
                sender: sender.Address,
                applicationId: appId,
                appArguments: args?.SelectMany(x => x).ToArray(),
                txnParams: txnParams
            );

            var signedTxn = txn.Sign(sender.PrivateKey.ToKeyPair().SecretKey);
            Debug.Log("[CallApp] Signed transaction");

            var (sendTxnError, txid) = await client.SendTransaction(signedTxn);
            if (sendTxnError.IsError)
            {
                Debug.LogError($"[CallApp] Algod SendTransaction error: {sendTxnError.Message}");
                return;
            }
            Debug.Log("[CallApp] Sent transaction");

            var (pendingErr, pendingTxn) = await Util.WaitForTransaction(client, txid);
            if (pendingErr.IsError)
            {
                Debug.LogError($"[CallApp] Algod WaitForTransaction failed: {pendingErr.Message}");
                return;
            }
            Debug.Log($"[CallApp] Application (ID: { appId } confirmed with round: { pendingTxn.ConfirmedRound }");

            Debug.Log($"[CallApp] Global state delta: { GetDeltaStateValues(pendingTxn.GlobalStateDelta) }");
            foreach (var accountDelta in pendingTxn.LocalStateDelta?.Where(x => x.Address == sender.Address))
            {
                Debug.Log($"[CallApp] Local state delta: { GetDeltaStateValues(accountDelta.Delta) }");
            }
        }

        static async UniTask ReadLocalState(IAlgodClient client, AuctionDemo.Account account, ulong appId)
        {
            var (error, accountInfo) = await client.GetAccountInformation(account.Address);
            if (error.IsError)
            {
                Debug.LogError($"[ReadLocalState] Algod GetAccountInformation failed: {error.Message}");
                return;
            }

            foreach (var state in accountInfo.ApplicationsLocalState.Where(x => x.Id == appId))
            {
                Debug.Log($"[ReadLocalState] User's application local state: { GetAppStateValues(state.KeyValues) }");
            }
        }

        static async UniTask ReadGlobalState(IAlgodClient client, AuctionDemo.Account account, ulong appId)
        {
            var (error, accountInfo) = await client.GetAccountInformation(account.Address);
            if (error.IsError)
            {
                Debug.LogError($"[ReadGlobalState] Algod GetAccountInformation failed: {error.Message}");
                return;
            }

            foreach (var app in accountInfo.CreatedApplications.Where(x => x.Id == appId))
            {
                Debug.Log($"[ReadGlobalState] Application global state: { GetAppStateValues(app.Params.GlobalState) }");
            }

            //var (error, application) = await client.GetApplication(appId);
            //if (error.IsError)
            //{
            //    Debug.LogError($"Algod GetApplication failed: {error.Message}");
            //    return;
            //}

            //return application.Params.GlobalState.ToDictionary(
            //    x =>
            //    {
            //        FixedString128Bytes keyDecoded = default;
            //        x.Key.Base64ToUtf8(ref keyDecoded);
            //        return keyDecoded.ToString();
            //    }, x => x.Value);
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
                outStr += "key: " + decodedKey.ToString() + "; value: ";

                TealValue value = v.Value;
                if (value.Type == TealValueType.Uint)
                {
                    outStr += "(uint) " + value.UintValue;
                }
                else if (value.Type == TealValueType.Bytes)
                {
                    FixedString128Bytes encodedValue = value.Bytes.ToString();
                    FixedString128Bytes decodedValue = default;
                    encodedValue.Base64ToUtf8(ref decodedValue);
                    outStr += "(bytes) " + decodedValue.ToString();
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
                outStr += "key: " + decodedKey.ToString() + "; value: ";

                EvalDelta value = v.Value;
                if (value.Action == EvalDeltaAction.SetUInt)
                {
                    outStr += "(set uint) " + value.UInt;
                }
                else if (value.Action == EvalDeltaAction.SetBytes)
                {
                    FixedString128Bytes encodedValue = value.Bytes.ToString();
                    FixedString128Bytes decodedValue = default;
                    encodedValue.Base64ToUtf8(ref decodedValue);
                    outStr += "(set bytes) " + decodedValue.ToString();
                }
                else if(value.Action == EvalDeltaAction.Delete)
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
