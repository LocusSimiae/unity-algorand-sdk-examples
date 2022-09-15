using AlgoSdk.Algod;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Util
    {
        public static async UniTask<AlgoApiResponse<PendingTransactionResponse>> WaitForTransaction(AlgodClient client, TransactionId txid, ulong timeout = 10)
        {
            AlgoApiResponse<PendingTransactionResponse> response = await client.PendingTransactionInformation(txid);

            var (lastStatusError, lastStatus) = await client.GetStatus();
            ulong lastRound = lastStatus.LastRound;
            ulong startRound = lastRound;

            while (lastRound < (startRound + timeout) && response.Payload.ConfirmedRound == 0)
            {
                response = await client.PendingTransactionInformation(txid);
                if (response.Error.IsError)
                {
                    Debug.LogError($"[WaitForTransaction] Algod GetPendingTransaction failed: {response.Error.Message}");
                    return response;
                }
                if (!string.IsNullOrEmpty(response.Payload.PoolError))
                {
                    Debug.LogError($"[WaitForTransaction] Algod GetPendingTransaction error: {response.Payload.PoolError}");
                    return response;
                }

                ++lastRound;
                (lastStatusError, lastStatus) = await client.WaitForBlock(lastRound);
            }

            if (response.Payload.ConfirmedRound == 0)
            {
                Debug.LogError($"[WaitForTransaction] Transaction {txid} not confirmed after {timeout} rounds!");
            }

            return response;
        }

        public static async UniTask<byte[]> FullyCompileContract(AlgodClient client, string tealPrecompiledSourcePath)
        {
            if (!System.IO.File.Exists(tealPrecompiledSourcePath))
            {
                Debug.LogError($"[FullyCompileContract] {tealPrecompiledSourcePath} doesn't exist!");
                return null;
            }

            byte[] contract = System.IO.File.ReadAllBytes(tealPrecompiledSourcePath);

            var (error, compilationResult) = await client.TealCompile(contract);
            if (error.IsError)
            {
                Debug.LogError($"[FullyCompileContract] Algod TealCompile failed: {error.Message}");
                return null;
            }

            return Convert.FromBase64String(compilationResult.Result);
        }

        /*
        def decodeState(stateArray: List[Any]) -> Dict[bytes, Union[int, bytes]]:
            state: Dict[bytes, Union[int, bytes]] = dict()

            for pair in stateArray:
                key = b64decode(pair["key"])

                value = pair["value"]
                valueType = value["type"]

                if valueType == 2:
                    # value is uint64
                    value = value.get("uint", 0)
                elif valueType == 1:
                    # value is byte array
                    value = b64decode(value.get("bytes", ""))
                else:
                    raise Exception(f"Unexpected state type: {valueType}")

                state[key] = value

            return state
        */

        public static async UniTask<Dictionary<string, Algod.TealValue>> GetAppGlobalState(AlgodClient client, ulong appId)
        {
            var (error, application) = await client.GetApplicationByID(appId);
            if (error.IsError)
            {
                Debug.LogError($"[GetAppGlobalState] Algod GetApplication failed: {error.Message}");
                return null;
            }

            //uses decodeState func in Python (see above)
            return application.WrappedValue.Params.GlobalState.WrappedValue.ToDictionary(
                x =>
                {
                    FixedString128Bytes encodedKey = x.Key;
                    FixedString128Bytes decodedKey = default;
                    encodedKey.Base64ToUtf8(ref decodedKey);
                    return decodedKey.Value;
                }, x => x.Value
            );
        }

        public static async UniTask<Dictionary<ulong, ulong>> GetBalances(AlgodClient client, Address account)
        {
            var balances = new Dictionary<ulong, ulong>();

            var (error, accountInfo) = await client.AccountInformation(account);
            if (error.IsError)
            {
                Debug.LogError($"[GetBalances] Algod GetAccountInformation failed: {error.Message}");
                return balances;
            }

            balances.Add(0, accountInfo.WrappedValue.Amount);
            Array.ForEach(accountInfo.WrappedValue.Assets, x => balances.Add(x.AssetId, x.Amount));

            return balances;
        }

        public static async UniTask<(ulong, ulong)> GetLastBlockTimestamp(AlgodClient client)
        {
            var (statusError, status) = await client.GetStatus();
            if (statusError.IsError)
            {
                Debug.LogError($"[GetLastBlockTimestamp] Algod GetCurrentStatus failed: {statusError.Message}");
                return (0, 0);
            }
            ulong lastRound = status.LastRound;
            var blockResponse = await client.GetBlock(lastRound);
            if (blockResponse.Error.IsError)
            {
                Debug.LogError($"[GetLastBlockTimestamp] Algod GetBlock failed: {blockResponse.Error.Message}");
                return (lastRound, 0);
            }
            ulong timestamp = blockResponse.Payload.BlockHeader.Timestamp;
            return (lastRound, timestamp);
        }
    }
}
