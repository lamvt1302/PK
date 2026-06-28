namespace PK
{
    public static class PkHudFormatter
    {
        public static string FormatWallet(long gold, int spins, int shield)
        {
            return $"Gold: {gold} | Spins: {spins} | Shield: {shield}";
        }

        public static string FormatIsland(string buildingName, int level, long upgradeCost)
        {
            return $"{buildingName} Lv.{level} | Upgrade: {upgradeCost} gold";
        }

        public static string FormatStatus(string message)
        {
            // Bug #5 (hardcore-r2): Vietnamese default.
            return string.IsNullOrWhiteSpace(message) ? "Sẵn sàng" : message.Trim();
        }
    }
}
