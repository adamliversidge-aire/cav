namespace CAV.BillingView.Extensions;

internal static class StringExtensions
{
    /// <summary>
    /// Format of account name {SERVICE-ENV}
    /// </summary>
    /// <param name="accountName"></param>
    /// <returns></returns>
    internal static string GetServiceFromAccountName(this string accountName)
    {
        return accountName.Split("-").First();
    }
}