﻿using SharePointPnP.PowerShell.CmdletHelpAttributes;
using SharePointPnP.PowerShell.Commands.Base.PipeBinds;
using System;
using System.IO;
using System.Management.Automation;
using System.Security;
using System.Linq;
using Microsoft.SharePoint.Client;
using OfficeDevPnP.Core;
using SharePointPnP.PowerShell.Commands.Provider;
using File = System.IO.File;
using System.Net;
#if !ONPREMISES
using Microsoft.SharePoint.Client.CompliancePolicy;
#endif

namespace SharePointPnP.PowerShell.Commands.Base
{
    [Cmdlet(VerbsCommunications.Connect, "PnPOnline", SupportsShouldProcess = false)]
    [CmdletHelp("Connect to a SharePoint site",
        "Connects to a SharePoint site and creates a context that is required for the other PnP Cmdlets",
        DetailedDescription = "If no credentials have been specified, and the CurrentCredentials parameter has not been specified, you will be prompted for credentials.",
        Category = CmdletHelpCategory.Base)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://contoso.sharepoint.com",
        Remarks = @"This will prompt for username and password and creates a context for the other PowerShell commands to use. When a generic credential is added to the Windows Credential Manager with https://contoso.sharepoint.com, PowerShell will not prompt for username and password.",
        SortOrder = 1)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://contoso.sharepoint.com -Credentials (Get-Credential)",
        Remarks = @"This will prompt for username and password and creates a context for the other PowerShell commands to use. ",
        SortOrder = 2)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url http://yourlocalserver -CurrentCredentials",
        Remarks = @"This will use the current user credentials and connects to the server specified by the Url parameter.",
        SortOrder = 3)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url http://yourlocalserver -Credentials 'O365Creds'",
        Remarks = @"This will use credentials from the Windows Credential Manager, as defined by the label 'O365Creds'.",
        SortOrder = 4)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url http://yourlocalserver -Credentials (Get-Credential) -UseAdfs",
        Remarks = @"This will prompt for username and password and creates a context using ADFS to authenticate.",
        SortOrder = 5)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://yourserver -Credentials (Get-Credential) -CreateDrive
cd SPO:\\
dir",
        Remarks = @"This will prompt you for credentials and creates a context for the other PowerShell commands to use. It will also create a SPO:\\ drive you can use to navigate around the site",
        SortOrder = 6)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://yourserver -Credentials (Get-Credential) -AuthenticationMode FormsAuthentication",
        Remarks = @"This will prompt you for credentials and creates a context for the other PowerShell commands to use. It assumes your server is configured for Forms Based Authentication (FBA)",
        SortOrder = 7)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://contoso.sharepoint.de -AppId 344b8aab-389c-4e4a-8fa1-4c1ae2c0a60d -AppSecret a3f3faf33f3awf3a3sfs3f3ss3f4f4a3fawfas3ffsrrffssfd -AzureEnvironment Germany",
        Remarks = @"This will authenticate you to the German Azure environment using the German Azure endpoints for authentication",
        SortOrder = 8)]
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://contoso.sharepoint.com -SPOManagementShell",
        Remarks = @"This will authenticate you using the SharePoint Online Management Shell application",
        SortOrder = 9)]
#if ONPREMISES
    [CmdletExample(
        Code = @"PS:> Connect-PnPOnline -Url https://yourserver -ClientId 763d5e60-b57e-426e-8e87-b7258f7f8188 -HighTrustCertificatePath c:\HighTrust.pfx -HighTrustCertificatePassword 'password' -HighTrustCertificateIssuerId 6b9534d8-c2c1-49d6-9f4b-cd415620bca8",
        Remarks = @"Connect to an on-premises SharePoint environment using a high trust certificate",
        SortOrder = 10)]
#endif
    public class ConnectOnline : PSCmdlet
    {
        private const string ParameterSet_MAIN = "Main";
        private const string ParameterSet_TOKEN = "Token";
        private const string ParameterSet_WEBLOGIN = "WebLogin";
#if !ONPREMISES
        private const string ParameterSet_NATIVEAAD = "Azure Active Directory";
        private const string ParameterSet_APPONLYAAD = "App-Only with Azure Active Directory";
        private const string ParameterSet_SPOManagement = "SPO Management Shell Credentials";
        private const string SPOManagementClientId = "9bc3ab49-b65d-410a-85ad-de819febfddc";
        private const string SPOManagementRedirectUri = "https://oauth.spops.microsoft.com/";
#endif
#if ONPREMISES
        private const string ParameterSet_HIGHTRUST = "HighTrust";
#endif
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterAttribute.AllParameterSets, ValueFromPipeline = true, HelpMessage = "The Url of the site collection to connect to.")]
        public string Url;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_MAIN, HelpMessage = "Credentials of the user to connect with. Either specify a PSCredential object or a string. In case of a string value a lookup will be done to the Windows Credential Manager for the correct credentials.")]
        public CredentialPipeBind Credentials;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_MAIN, HelpMessage = "If you want to connect with the current user credentials")]
        public SwitchParameter CurrentCredentials;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_MAIN, HelpMessage = "If you want to connect to your on-premises SharePoint farm using ADFS")]
        public SwitchParameter UseAdfs;

        [Parameter(Mandatory = false, ParameterSetName = ParameterAttribute.AllParameterSets, HelpMessage = "Specifies a minimal server healthscore before any requests are executed.")]
        public int MinimalHealthScore = -1;

        [Parameter(Mandatory = false, ParameterSetName = ParameterAttribute.AllParameterSets, HelpMessage = "Defines how often a retry should be executed if the server healthscore is not sufficient. Default is 10 times.")]
        public int RetryCount = 10;

        [Parameter(Mandatory = false, ParameterSetName = ParameterAttribute.AllParameterSets, HelpMessage = "Defines how many seconds to wait before each retry. Default is 1 second.")]
        public int RetryWait = 1;

        [Parameter(Mandatory = false, ParameterSetName = ParameterAttribute.AllParameterSets, HelpMessage = "The request timeout. Default is 180000")]
        public int RequestTimeout = 1800000;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_TOKEN, HelpMessage = "Authentication realm. If not specified will be resolved from the url specified.")]
        public string Realm;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_TOKEN, HelpMessage = "The Application Client ID to use.")]
        public string AppId;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_TOKEN, HelpMessage = "The Application Client Secret to use.")]
        public string AppSecret;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_WEBLOGIN, HelpMessage = "If you want to connect to SharePoint with browser based login")]
        public SwitchParameter UseWebLogin;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_MAIN, HelpMessage = "Specify to use for instance use forms based authentication (FBA)")]
        public ClientAuthenticationMode AuthenticationMode = ClientAuthenticationMode.Default;

        [Parameter(Mandatory = false, HelpMessage = "If you want to create a PSDrive connected to the URL")]
        public SwitchParameter CreateDrive;

        [Parameter(Mandatory = false, HelpMessage = "Name of the PSDrive to create (default: SPO)")]
        public string DriveName = "SPO";

#if !ONPREMISES
        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_SPOManagement, HelpMessage = "Log in using the SharePoint Online Management Shell application")]
        public SwitchParameter SPOManagementShell;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_NATIVEAAD, HelpMessage = "The Client ID of the Azure AD Application")]
        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_APPONLYAAD, HelpMessage = "The Client ID of the Azure AD Application")]
#endif
#if ONPREMISES
        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_HIGHTRUST, HelpMessage = "The Client ID of the Add-In Registration in SharePoint")]
#endif
        public string ClientId;

#if !ONPREMISES
        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_NATIVEAAD, HelpMessage = "The Redirect URI of the Azure AD Application")]
        public string RedirectUri;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_APPONLYAAD, HelpMessage = "The Azure AD Tenant name,e.g. mycompany.onmicrosoft.com")]
        public string Tenant;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_APPONLYAAD, HelpMessage = "Path to the certificate (*.pfx)")]
        public string CertificatePath;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_APPONLYAAD, HelpMessage = "Password to the certificate (*.pfx)")]
        public SecureString CertificatePassword;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_NATIVEAAD, HelpMessage = "Clears the token cache.")]
        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_SPOManagement, HelpMessage = "Clears the token cache.")]
        public SwitchParameter ClearTokenCache;

        [Parameter(Mandatory = false, ParameterSetName = ParameterSet_NATIVEAAD, HelpMessage = "The Azure environment to use for authentication, the defaults to 'Production' which is the main Azure environment.")]
        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_APPONLYAAD, HelpMessage = "The Azure environment to use for authentication, the defaults to 'Production' which is the main Azure environment.")]
        public AzureEnvironment AzureEnvironment = AzureEnvironment.Production;
#endif
        [Parameter(Mandatory = false, HelpMessage = "The url to the Tenant Admin site. If not specified, the cmdlets will assume to connect automatically to https://<tenantname>-admin.sharepoint.com where appropriate.")]
        public string TenantAdminUrl;

        [Parameter(Mandatory = false, ParameterSetName = ParameterAttribute.AllParameterSets, HelpMessage = "Should we skip the check if this site is the Tenant admin site. Default is false")]
        public SwitchParameter SkipTenantAdminCheck;

        [Parameter(Mandatory = false, HelpMessage = "Ignores any SSL errors. To be used i.e. when connecting to a SharePoint farm using self signed certificates or using a certificate authority not trusted by this machine.")]
        public SwitchParameter IgnoreSslErrors;

#if ONPREMISES
        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_HIGHTRUST, HelpMessage = "The path to the private key certificate (.pfx) to use for the High Trust connection")]
        public string HighTrustCertificatePath;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_HIGHTRUST, HelpMessage = "The password of the private key certificate (.pfx) to use for the High Trust connection")]
        public string HighTrustCertificatePassword;

        [Parameter(Mandatory = true, ParameterSetName = ParameterSet_HIGHTRUST, HelpMessage = "The IssuerID under which the CER counterpart of the PFX has been registered in SharePoint as a Trusted Security Token issuer to use for the High Trust connection")]
        public string HighTrustCertificateIssuerId;
#endif

        protected override void ProcessRecord()
        {
            if (IgnoreSslErrors)
            {
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            }

            PSCredential creds = null;
            if (Credentials != null)
            {
                creds = Credentials.Credential;
            }

            if (ParameterSetName == ParameterSet_TOKEN)
            {
                SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InstantiateSPOnlineConnection(new Uri(Url), Realm, AppId, AppSecret, Host, MinimalHealthScore, RetryCount, RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck);
            }
            else if (UseWebLogin)
            {
                SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InstantiateWebloginConnection(new Uri(Url), MinimalHealthScore, RetryCount, RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck);
            }
            else if (UseAdfs)
            {
                if (creds == null)
                {
                    if ((creds = GetCredentials()) == null)
                    {
                        creds = Host.UI.PromptForCredential(Properties.Resources.EnterYourCredentials, "", "", "");
                    }
                }

                SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InstantiateAdfsConnection(new Uri(Url), creds, Host, MinimalHealthScore, RetryCount, RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck);
            }
#if !ONPREMISES
            else if (ParameterSetName == ParameterSet_SPOManagement)
            {
                ConnectNativAAD(SPOManagementClientId, SPOManagementRedirectUri);
            }
            else if (ParameterSetName == ParameterSet_NATIVEAAD)
            {
                ConnectNativAAD(ClientId, RedirectUri);
            }
            else if (ParameterSetName == ParameterSet_APPONLYAAD)
            {
                SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InitiateAzureADAppOnlyConnection(new Uri(Url), ClientId, Tenant, CertificatePath, CertificatePassword, MinimalHealthScore, RetryCount, RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck, AzureEnvironment);
            }
#endif
#if ONPREMISES
            else if (ParameterSetName == ParameterSet_HIGHTRUST)
            {
                SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InstantiateHighTrustConnection(Url, ClientId, HighTrustCertificatePath, HighTrustCertificatePassword, HighTrustCertificateIssuerId, MinimalHealthScore, RetryCount, RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck);
            }
#endif
            else
            {
                if (!CurrentCredentials && creds == null)
                {
                    creds = GetCredentials();
                    if (creds == null)
                    {
                        creds = Host.UI.PromptForCredential(Properties.Resources.EnterYourCredentials, "", "", "");
                    }
                }
                SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InstantiateSPOnlineConnection(new Uri(Url), creds, Host, CurrentCredentials, MinimalHealthScore, RetryCount, RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck, AuthenticationMode);
            }
            WriteVerbose($"PnP PowerShell Cmdlets ({System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}): Connected to {Url}");

            if (CreateDrive && SPOnlineConnection.CurrentConnection.Context != null)
            {
                var provider = SessionState.Provider.GetAll().FirstOrDefault(p => p.Name.Equals(SPOProvider.PSProviderName, StringComparison.InvariantCultureIgnoreCase));
                if (provider != null)
                {
                    if (provider.Drives.Any(d => d.Name.Equals(DriveName, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        SessionState.Drive.Remove(DriveName, true, "Global");
                    }

                    var drive = new PSDriveInfo(DriveName, provider, string.Empty, Url, null);
                    SessionState.Drive.New(drive, "Global");
                }
            }
        }

#if !ONPREMISES
        private void ConnectNativAAD(string clientId, string redirectUrl)
        {
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configFolder = Path.Combine(appDataFolder, "SharePointPnP.PowerShell");
            Directory.CreateDirectory(configFolder); // Ensure folder exists
            if (ClearTokenCache)
            {
                string configFile = Path.Combine(configFolder, "tokencache.dat");

                if (File.Exists(configFile))
                {
                    File.Delete(configFile);
                }
            }
            SPOnlineConnection.CurrentConnection = SPOnlineConnectionHelper.InitiateAzureADNativeApplicationConnection(
                new Uri(Url), clientId, new Uri(redirectUrl), MinimalHealthScore, RetryCount,
                RetryWait, RequestTimeout, TenantAdminUrl, SkipTenantAdminCheck, AzureEnvironment);
        }
#endif

        private PSCredential GetCredentials()
        {
            PSCredential creds;

            var connectionUri = new Uri(Url);

            // Try to get the credentials by full url

            creds = Utilities.CredentialManager.GetCredential(Url);
            if (creds == null)
            {
                // Try to get the credentials by splitting up the path
                var pathString = $"{connectionUri.Scheme}://{(connectionUri.IsDefaultPort ? connectionUri.Host : $"{connectionUri.Host}:{connectionUri.Port}")}";
                var path = connectionUri.AbsolutePath;
                while (path.IndexOf('/') != -1)
                {
                    path = path.Substring(0, path.LastIndexOf('/'));
                    if (!string.IsNullOrEmpty(path))
                    {
                        var pathUrl = $"{pathString}{path}";
                        creds = Utilities.CredentialManager.GetCredential(pathUrl);
                        if (creds != null)
                        {
                            break;
                        }
                    }
                }

                if (creds == null)
                {
                    // Try to find the credentials by schema and hostname
                    creds = Utilities.CredentialManager.GetCredential(connectionUri.Scheme + "://" + connectionUri.Host);

                    if (creds == null)
                    {
                        // try to find the credentials by hostname
                        creds = Utilities.CredentialManager.GetCredential(connectionUri.Host);
                    }
                }

            }

            return creds;
        }
    }
}
