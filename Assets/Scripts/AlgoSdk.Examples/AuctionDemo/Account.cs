using AlgoSdk;

namespace AlgoSdk.Examples.AuctionDemo
{
    public struct Account
    {
        public PrivateKey PrivateKey;
        public Address Address => PrivateKey.ToAddress();
        public Mnemonic Mnemonic => PrivateKey.ToMnemonic();

        public static implicit operator Account(PrivateKey privateKey)
        {
            return new Account() { PrivateKey = privateKey };
        }

        public static Account FromMnemonic(string mnemonic) => FromMnemonic(mnemonic);
        public static Account FromMnemonic(Mnemonic mnemonic)
        {
            var privateKey = mnemonic.ToPrivateKey();
            return new Account()
            {
                PrivateKey = privateKey
            };
        }
    }
}
