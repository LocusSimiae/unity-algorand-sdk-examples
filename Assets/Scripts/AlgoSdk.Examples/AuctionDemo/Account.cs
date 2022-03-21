namespace AlgoSdk.Examples.AuctionDemo
{
    public struct Account
    {
        public PrivateKey PrivateKey;
        public Address Address => PrivateKey.ToAddress();
        public Mnemonic Mnemonic => PrivateKey.ToMnemonic();
        public Crypto.Ed25519.SecretKeyHandle SecretKey => PrivateKey.ToKeyPair().SecretKey;

        public static implicit operator Account(PrivateKey privateKey) => new Account() { PrivateKey = privateKey };

        public static Account FromMnemonic(string mnemonic) => FromMnemonic(Mnemonic.FromString(mnemonic));
        public static Account FromMnemonic(Mnemonic mnemonic) => mnemonic.ToPrivateKey();
    }
}
