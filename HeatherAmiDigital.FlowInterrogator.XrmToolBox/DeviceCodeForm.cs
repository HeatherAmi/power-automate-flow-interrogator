using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Identity.Client;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// A modeless dialog that presents the MSAL device-code verification URL and user code.
/// Shown with <see cref="Control.Show()"/> (not <c>ShowDialog</c>) so the rest of the tool
/// stays interactive while MSAL polls the sign-in endpoint in the background.
/// </summary>
public sealed class DeviceCodeForm : Form
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceCodeForm"/> class.
    /// </summary>
    /// <param name="info">The device-code details to display.</param>
    public DeviceCodeForm(DeviceCodeResult info)
    {
        if (info == null) throw new ArgumentNullException(nameof(info));

        Text = "Power Automate sign-in";
        ShowIcon = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 230);

        var lblIntro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(12, 12, 12, 0),
            Text = "To authorise access to Power Automate run history, open the URL below and enter the code."
        };

        var lblUrl = new Label { AutoSize = true, Left = 12, Top = 64, Text = "Verification URL:" };
        var txtUrl = new TextBox
        {
            ReadOnly = true,
            Left = 12,
            Top = 84,
            Width = 416,
            Text = info.VerificationUrl
        };
        txtUrl.GotFocus += (s, e) => txtUrl.SelectAll();

        var lblCode = new Label { AutoSize = true, Left = 12, Top = 116, Text = "Code:" };
        var lblCodeValue = new Label
        {
            AutoSize = false,
            Left = 12,
            Top = 134,
            Width = 416,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 18, FontStyle.Bold),
            Text = info.UserCode
        };

        var btnSignedIn = new Button
        {
            Text = "I've signed in",
            DialogResult = DialogResult.OK,
            Left = 200,
            Top = 180,
            Width = 110
        };
        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Left = 318,
            Top = 180,
            Width = 110
        };

        var btnOpen = new Button
        {
            Text = "Open",
            Left = 12,
            Top = 180,
            Width = 80
        };
        btnOpen.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(info.VerificationUrl) { UseShellExecute = true });
            }
            catch
            {
                // Opening the browser is a convenience; the URL is also copyable from the textbox.
            }
        };

        Controls.AddRange(new Control[] { lblIntro, lblUrl, txtUrl, lblCode, lblCodeValue, btnOpen, btnSignedIn, btnCancel });

        AcceptButton = btnSignedIn;
        CancelButton = btnCancel;

        // Closing the form (either button) is purely a UI dismissal; MSAL keeps polling
        // regardless, so do not dispose anything that would interrupt the token acquisition.
        FormClosed += (s, e) => { };
    }
}
