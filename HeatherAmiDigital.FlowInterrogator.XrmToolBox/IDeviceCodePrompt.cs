using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// Displays an MSAL device-code prompt to the user. Implemented by the UI layer so the
/// authentication service (in Core) does not depend on WinForms.
/// </summary>
public interface IDeviceCodePrompt
{
    /// <summary>
    /// Shows the verification URL and user code so the user can complete sign-in in a browser.
    /// The implementation must marshal to the UI thread and return without blocking MSAL's
    /// background polling.
    /// </summary>
    /// <param name="info">The device-code details supplied by MSAL.</param>
    Task ShowDeviceCodeAsync(DeviceCodeResult info);
}
