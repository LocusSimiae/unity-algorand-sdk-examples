using AlgoSdk;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgoSdk.Examples.AuctionDemo
{
    public class Setup
    {
        static readonly string ALGOD_ADDRESS = "http://localhost:4001";
        static readonly string ALGOD_TOKEN = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        static readonly string KMD_ADDRESS = "http://localhost:4002";
        static readonly string KMD_TOKEN = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        static readonly string KMD_WALLET_NAME = "unencrypted-default-wallet";
        static readonly string KMD_WALLET_PASSWORD = "";

        public AlgodClient AlgodClient = new AlgodClient(ALGOD_ADDRESS, ALGOD_TOKEN);
        public KmdClient KmdClient = new KmdClient(KMD_ADDRESS, KMD_TOKEN);

        static List<Account> kmdAccounts = new List<Account>();

        public async UniTask<List<Account>> GetGenesisAccounts()
        {
            if (kmdAccounts.Count != 0)
            {
                return kmdAccounts;
            }

            var walletsResponse = await KmdClient.ListWallets();
            if (walletsResponse.Error.IsError)
            {
                Debug.LogError($"[GetGenesisAccounts] KMD ListWallets failed with: {walletsResponse.Error}");
                return kmdAccounts;
            }

            Wallet[] wallets = walletsResponse.Payload.Wallets;
            if (wallets == null || wallets.Length == 0)
            {
                Debug.LogError($"[GetGenesisAccounts] KMD ListWallets couldn't find any wallets!");
                return kmdAccounts;
            }

            int foundIndex = Array.FindIndex(wallets, x => x.Name == KMD_WALLET_NAME);
            if (foundIndex < 0)
            {
                Debug.LogError($"[GetGenesisAccounts] KMD ListWallets didn't find a wallet with name: {KMD_WALLET_NAME}");
                return kmdAccounts;
            }

            var walletId = walletsResponse.Payload.Wallets[foundIndex].Id;
            var walletHandleTokenResponse = await KmdClient.InitWalletHandleToken(walletId, KMD_WALLET_PASSWORD);
            if (walletHandleTokenResponse.Error.IsError)
            {
                Debug.LogError($"[GetGenesisAccounts] KMD InitWalletHandleToken failed with: {walletHandleTokenResponse.Error}");
                return kmdAccounts;
            }

            var walletHandleToken = walletHandleTokenResponse.Payload.WalletHandleToken;
            var keysResponse = await KmdClient.ListKeys(walletHandleToken);
            if (keysResponse.Error.IsError)
            {
                Debug.LogError($"[GetGenesisAccounts] KMD ListKeys failed with: {keysResponse.Error}");
                return kmdAccounts;
            }

            foreach (var address in keysResponse.Payload.Addresses)
            {
                var exportKeyResponse = await KmdClient.ExportKey(address, walletHandleToken, KMD_WALLET_PASSWORD);
                if (exportKeyResponse.Error.IsError)
                {
                    Debug.LogError($"[GetGenesisAccounts] KMD ExportKey failed with: {exportKeyResponse.Error}");
                    return kmdAccounts;
                }

                kmdAccounts.Add(exportKeyResponse.Payload.PrivateKey);
            }

            await KmdClient.ReleaseWalletHandleToken(walletHandleToken);

            return kmdAccounts;
        }
    }
}
