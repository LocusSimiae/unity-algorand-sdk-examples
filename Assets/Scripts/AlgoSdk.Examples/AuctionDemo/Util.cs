using AlgoSdk;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using AlgoSdk.LowLevel;

namespace AlgoSdk.Examples.AuctionDemo
{
    public static class Util
    {
        public static async UniTask<AlgoApiResponse<PendingTransaction>> WaitForTransaction(IAlgodClient client, TransactionId txid, ulong timeout = 10)
        {
            AlgoApiResponse<PendingTransaction> response = await client.GetPendingTransaction(txid);

            var (lastStatusError, lastStatus) = await client.GetCurrentStatus();
            ulong lastRound = lastStatus.LastRound;
            ulong startRound = lastRound;

            while (lastRound < (startRound + timeout) && response.Payload.ConfirmedRound == 0)
            {
                response = await client.GetPendingTransaction(txid);
                if (response.Error.IsError)
                {
                    Debug.LogError($"[WaitForTransaction] Algod GetPendingTransaction failed: {response.Error.Message}");
                    return response;
                }
                if (!response.Payload.PoolError.IsEmpty)
                {
                    Debug.LogError($"[WaitForTransaction] Algod GetPendingTransaction error: {response.Payload.PoolError}");
                    return response;
                }

                ++lastRound;
                (lastStatusError, lastStatus) = await client.GetStatusAfterWaitingForRound(lastRound);
            }

            if (response.Payload.ConfirmedRound == 0)
            {
                Debug.LogError($"[WaitForTransaction] Transaction {txid} not confirmed after {timeout} rounds!");
            }

            return response;
        }

        public static async UniTask<byte[]> FullyCompileContract(IAlgodClient client, string tealPrecompiledSourcePath)
        {
            //https://stackoverflow.com/questions/3259583/how-to-get-files-in-a-relative-path-in-c-sharp
            if (!System.IO.File.Exists(tealPrecompiledSourcePath))
            {
                Debug.LogError($"[FullyCompileContract] {tealPrecompiledSourcePath} doesn't exist!");
                return null;
            }

            string contract = System.IO.File.ReadAllText(tealPrecompiledSourcePath);

            var (error, compilationResult) = await client.TealCompile(contract);
            if (error.IsError)
            {
                Debug.LogError($"[FullyCompileContract] Algod TealCompile failed: {error.Message}");
                return null;
            }

            return Convert.FromBase64String(compilationResult.CompiledBytesBase64);
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

        public static async UniTask<Dictionary<string, TealValue>> GetAppGlobalState(IAlgodClient client, ulong appId)
        {
            var (error, application) = await client.GetApplication(appId);
            if (error.IsError)
            {
                Debug.LogError($"[GetAppGlobalState] Algod GetApplication failed: {error.Message}");
                return null;
            }

            //uses decodeState func in Python (see above)
            return application.Params.GlobalState.ToDictionary(
                x =>
                {
                    FixedString128Bytes encodedKey = x.Key;
                    FixedString128Bytes decodedKey = default;
                    encodedKey.Base64ToUtf8(ref decodedKey);
                    return decodedKey.Value;
                }, x => x.Value);
        }

        public static async UniTask<Dictionary<ulong, ulong>> GetBalances(IAlgodClient client, Address account)
        {
            var balances = new Dictionary<ulong, ulong>();

            var (error, accountInfo) = await client.GetAccountInformation(account);
            if (error.IsError)
            {
                Debug.LogError($"[GetBalances] Algod GetAccountInformation failed: {error.Message}");
                return balances;
            }

            balances.Add(0, accountInfo.Amount);
            Array.ForEach(accountInfo.Assets, x => balances.Add(x.AssetId, x.Amount));

            return balances;
        }

        public static async UniTask<(ulong, ulong)> GetLastBlockTimestamp(IAlgodClient client)
        {
            var (statusError, status) = await client.GetCurrentStatus();
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
