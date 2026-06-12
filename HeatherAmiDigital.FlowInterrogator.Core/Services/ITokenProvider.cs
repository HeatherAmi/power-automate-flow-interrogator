using System.Threading.Tasks;

namespace HeatherAmiDigital.FlowInterrogator.Core.Services;

/// <summary>
/// Supplies access tokens for the Power Automate Management API. Abstracted from the
/// concrete MSAL implementation so HTTP-facing services can be unit-tested without a
/// live interactive sign-in.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Acquires a valid bearer access token for the Flow Management API.
    /// </summary>
    /// <returns>A JWT access token string.</returns>
    Task<string> GetAccessTokenAsync();
}
