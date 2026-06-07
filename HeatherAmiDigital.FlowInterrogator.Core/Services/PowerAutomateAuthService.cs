using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace HeatherAmiDigital.FlowInterrogator.Core.Services;

/// <summary>
/// Handles interactive authentication against the Power Automate Management API 
/// using the MSAL Device Code flow. Required because Service Principals cannot 
/// retrieve run history for flows owned by other users.
/// </summary>
public sealed class PowerAutomateAuthService
{
    // Well-known multi-tenant client ID for native desktop applications.
    // Replace with a dedicated App Registration ID for formal production deployments.
    private const string DefaultClientId = "1950a258-227b-4e31-a9cf-717495945fc2";

    private const string Authority = "https://login.microsoftonline.com/organizations";

    // Scope for the Power Automate Flow Resource Provider API.
    private static readonly string[] Scopes = { "https://service.flow.microsoft.com/.default" };

    private readonly IPublicClientApplication _msalClient;
    private readonly Func<DeviceCodeResult, Task> _deviceCodeCallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerAutomateAuthService"/> class.
    /// </summary>
    /// <param name="deviceCodeCallback">
    /// A delegate invoked by MSAL when the device code is generated. 
    /// The UI layer must implement this to display the code and verification URL to the user.
    /// </param>
    /// <param name="clientId">The Azure AD Application (client) ID. Defaults to the standard PowerShell/native app ID.</param>
    public PowerAutomateAuthService(Func<DeviceCodeResult, Task> deviceCodeCallback, string clientId = DefaultClientId)
    {
        _deviceCodeCallback = deviceCodeCallback ?? throw new ArgumentNullException(nameof(deviceCodeCallback));

        _msalClient = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(Authority)
            .WithDefaultRedirectUri()
            .Build();
    }

    /// <summary>
    /// Acquires an access token for the Power Automate Management API.
    /// Attempts silent acquisition from the MSAL cache first, falling back to the Device Code flow.
    /// </summary>
    /// <returns>A valid JWT access token string.</returns>
    /// <exception cref="MsalException">Thrown if the user cancels the flow or authentication fails.</exception>
    public async Task<string> GetAccessTokenAsync()
    {
        var accounts = await _msalClient.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account != null)
        {
            try
            {
                var silentResult = await _msalClient.AcquireTokenSilent(Scopes, account).ExecuteAsync();
                return silentResult.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // Silent auth failed (e.g., token expired and no refresh token available), fall through to device code
            }
        }

        var deviceCodeResult = await _msalClient.AcquireTokenWithDeviceCode(Scopes, _deviceCodeCallback).ExecuteAsync();
        return deviceCodeResult.AccessToken;
    }

    /// <summary>
    /// Clears all cached accounts and tokens from the MSAL in-memory cache.
    /// Useful when the user needs to switch accounts or force a re-authentication.
    /// </summary>
    public async Task ClearCacheAsync()
    {
        var accounts = await _msalClient.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await _msalClient.RemoveAsync(account);
        }
    }
}
