﻿using Grpc.Core;
using PnP.Scanning.Core;
using PnP.Scanning.Core.Services;
using PnP.Scanning.Process.Services;
using System.CommandLine;

namespace PnP.Scanning.Process.Commands
{
    internal class StartCommandHandler
    {
        private readonly ScannerManager processManager;

        private Command cmd;
        private Option<Mode> modeOption;
        private Option<string> tenantOption;
        private Option<Microsoft365Environment> environmentOption;
        private Option<List<string>> sitesListOption;
        private Option<FileInfo> sitesFileOption;
        private Option<AuthenticationMode> authenticationModeOption;
        private Option<Guid> applicationIdOption;
        private Option<string> certPathOption;
        private Option<FileInfo> certPfxFileInfoOption;
        private Option<string> certPfxFilePasswordOption;
#if DEBUG
        // Specific options for the test handler
        private Option<int> testNumberOfSitesOption;
#endif

        public StartCommandHandler(ScannerManager processManagerInstance)
        {
            processManager = processManagerInstance;

            cmd = new Command("start", "starts a scan");

            // Configure the options for the start command

            #region Scan scope

            // Scanner mode
            modeOption = new(
                name: $"--{Constants.StartMode}",
                getDefaultValue: () => Mode.Test,
                description: "Scanner mode"
                )
            {
                IsRequired = true,
            };
            cmd.AddOption(modeOption);

            tenantOption = new(
                name: $"--{Constants.StartTenant}",
                description: "Name of the tenant that will be scanned (e.g. contoso.sharepoint.com)")
            {
                IsRequired = false
            };
            cmd.AddOption(tenantOption);

            environmentOption = new(
                name: $"--{Constants.StartEnvironment}",
                getDefaultValue: () => Microsoft365Environment.Production,
                description: "The cloud environment you're scanning")
            {
                IsRequired = false
            };
            cmd.AddOption(environmentOption);

            sitesListOption = new(
                name: $"--{Constants.StartSitesList}",
                parseArgument: (result) =>
                {
                    // https://github.com/dotnet/command-line-api/issues/1287
#pragma warning disable CS8604 // Possible null reference argument.
                    var siteFile = result.FindResultFor(sitesFileOption);
#pragma warning restore CS8604 // Possible null reference argument.
                    if (siteFile != null)
                    {
                        result.ErrorMessage = $"the --{Constants.StartSitesList} option is mutually exclusive with the --{Constants.StartSitesFile} option";
#pragma warning disable CS8603 // Possible null reference return.
                        return null;
#pragma warning restore CS8603 // Possible null reference return.
                    }

                    return result.Tokens.Select(t => t.Value).ToList();
                },
                description: "List with site collections to scan")
            {
                IsRequired = false
            };
            cmd.AddOption(sitesListOption);

            sitesFileOption = new(
                name: $"--{Constants.StartSitesFile}",
                parseArgument: (result) =>
                {
                    var siteList = result.FindResultFor(sitesListOption);
                    if (siteList != null)
                    {
                        result.ErrorMessage = $"the --{Constants.StartSitesFile} option is mutually exclusive with the --{Constants.StartSitesList} option";
#pragma warning disable CS8603 // Possible null reference return.
                        return null;
#pragma warning restore CS8603 // Possible null reference return.
                    }

                    return new FileInfo(result.Tokens[0].Value);
                },
                description: "File containing a list of site collections to scan")
            {
                IsRequired = false
            };

            sitesFileOption.ExistingOnly();

            cmd.AddOption(sitesFileOption);
            #endregion

            #region Scan authentication

            // Authentication mode
            authenticationModeOption = new(
                    name: $"--{Constants.StartAuthMode}",
                    getDefaultValue: () => AuthenticationMode.Interactive,
                    description: "Authentication mode used for the scan")
            {
                IsRequired = true
            };

            cmd.AddOption(authenticationModeOption);

            // Application id
            applicationIdOption = new(
                name: $"--{Constants.StartApplicationId}",
                // Default application to use is the PnP Management shell application
                getDefaultValue: () => Guid.Parse("31359c7f-bd7e-475c-86db-fdb8c937548e"),
                description: "Azure AD application id to use for authenticating the scan")
            {
                IsRequired = true
            };
            cmd.AddOption(applicationIdOption);

            // Certificate path
            certPathOption = new(
                name: $"--{Constants.StartCertPath}",
                parseArgument: (result) =>
                {
                    // https://github.com/dotnet/command-line-api/issues/1287
                    var authenticationMode = result.FindResultFor(authenticationModeOption);
                    if (authenticationMode != null && authenticationMode.GetValueOrDefault<AuthenticationMode>() != AuthenticationMode.Application)
                    {
                        result.ErrorMessage = $"--{Constants.StartCertPath} can only be used with --{Constants.StartAuthMode} application";
                        return "";
                    }

                    return result.Tokens[0].Value;
                },
                description: "Path to stored certificate in the form of StoreName|StoreLocation|Thumbprint. E.g. My|LocalMachine|3FG496B468BE3828E2359A8A6F092FB701C8CDB1")
            {
                IsRequired = false,
            };

            certPathOption.AddValidator(val =>
            {
                // Custom validation of the provided option input 
                string? input = val.GetValueOrDefault<string>();
                if (input != null && input.Split("|", StringSplitOptions.RemoveEmptyEntries).Length == 3)
                {
                    return "";
                }
                else
                {
                    return $"Invalid --{Constants.StartCertPath} value";
                }
            });
            cmd.AddOption(certPathOption);

            // Certificate PFX file
            certPfxFileInfoOption = new(
                name: $"--{Constants.StartCertFile}",
                parseArgument: (result) =>
                {
                    var authenticationMode = result.FindResultFor(authenticationModeOption);
                    if (authenticationMode != null && authenticationMode.GetValueOrDefault<AuthenticationMode>() != AuthenticationMode.Application)
                    {
                        result.ErrorMessage = $"--{Constants.StartCertPath} can only be used with --{Constants.StartAuthMode} application";
#pragma warning disable CS8603 // Possible null reference return.
                        return null;
#pragma warning restore CS8603 // Possible null reference return.
                    }

#pragma warning disable CS8604 // Possible null reference argument.
                    if (result.FindResultFor(certPfxFilePasswordOption) is { })
                    {
                        result.ErrorMessage = $"using --{Constants.StartCertFile} also requires using --{Constants.StartCertPassword}";
#pragma warning disable CS8603 // Possible null reference return.
                        return null;
#pragma warning restore CS8603 // Possible null reference return.
                    }
#pragma warning restore CS8604 // Possible null reference argument.

                    return new FileInfo(result.Tokens[0].Value);
                },
                description: "Path to certificate PFX file"
                )
            {
                IsRequired = false,
            };

            certPfxFileInfoOption.ExistingOnly();
            cmd.AddOption(certPfxFileInfoOption);

            // Certificate PFX file password
            certPfxFilePasswordOption = new(
                name: $"--{Constants.StartCertPassword}",
                parseArgument: (result) =>
                {
                    var authenticationMode = result.FindResultFor(authenticationModeOption);
                    if (authenticationMode != null && authenticationMode.GetValueOrDefault<AuthenticationMode>() != AuthenticationMode.Application)
                    {
                        result.ErrorMessage = $"--{Constants.StartCertPassword} can only be used with --{Constants.StartAuthMode} application";
                        return "";
                    }

                    if (result.FindResultFor(certPfxFileInfoOption) is { })
                    {
                        result.ErrorMessage = $"using --{Constants.StartCertPassword} also requires using --{Constants.StartCertFile}";
                        return "";
                    }

                    return result.Tokens[0].Value;
                },
                description: "Password for the certificate PFX file")
            {
                IsRequired = false
            };
            cmd.AddOption(certPfxFilePasswordOption);

            #endregion

            #region Scan component specific handlers

#if DEBUG
            testNumberOfSitesOption = new(
                name: $"--{Constants.StartTestNumberOfSites}",
                parseArgument: (result) =>
                {
                    var mode = result.FindResultFor(modeOption);
                    if (mode != null && mode.GetValueOrDefault<Mode>() != Mode.Test)
                    {
                        result.ErrorMessage = $"--{Constants.StartTestNumberOfSites} can only be used with --{Constants.StartMode} test";
                        return 10;
                    }

                    int numberOfSites = int.Parse(result.Tokens[0].Value);

                    // Set default value if needed
                    if (numberOfSites <= 0)
                    {
                        numberOfSites = 10;
                    }

                    return numberOfSites;
                },
                description: "Number of site collections to emulate for dummy scanning")
            {
                IsRequired = false
            };
            testNumberOfSitesOption.SetDefaultValue(10);
            cmd.AddOption(testNumberOfSitesOption);
#endif

            #endregion

        }

        /// <summary>
        /// https://github.com/dotnet/command-line-api/blob/main/docs/model-binding.md#more-complex-types
        /// </summary>
        /// <returns></returns>
        public Command Create()
        {
            // Custom validation of provided command input, use to validate option combinations
            //cmd.AddValidator(commandResult =>
            //{
            //    //https://github.com/dotnet/command-line-api/issues/1119
            //    if (authenticationModeOption != null)
            //    {
            //        AuthenticationMode mode = commandResult.FindResultFor(authenticationModeOption).GetValueOrDefault<AuthenticationMode>();                    

            //    }

            //    return null;
            //});

            // Binder approach as that one can handle an unlimited number of command line arguments
            var startBinder = new StartBinder(modeOption, tenantOption, environmentOption, sitesListOption, sitesFileOption,
                                              authenticationModeOption, applicationIdOption, certPathOption, certPfxFileInfoOption, certPfxFilePasswordOption
#if DEBUG                                              
                                              , testNumberOfSitesOption
#endif
                                              );
            cmd.SetHandler(async (StartOptions arguments) =>
            {
                await HandleStartAsync(arguments);

            }, startBinder);

            return cmd;
        }

        private async Task HandleStartAsync(StartOptions arguments)
        {
            // Setup client to talk to scanner
            var client = await processManager.GetScannerClientAsync();

            // Kick off a scan
            var start = new StartRequest
            {
                Mode = arguments.Mode.ToString(),
                Tenant = arguments.Tenant != null ? arguments.Tenant.ToString() : "",
                Environment = arguments.Environment.ToString(),
                SitesList = arguments.SitesList != null ? string.Join(",", arguments.SitesList) : "",
                SitesFile = arguments.SitesFile != null ? arguments.SitesFile.FullName.ToString() : "",
                AuthMode = arguments.AuthMode.ToString(),
                ApplicationId = arguments.ApplicationId.ToString(),
            };

#if DEBUG
            if (arguments.Mode == Mode.Test)
            {
                start.Properties.Add(new PropertyRequest
                {
                    Property = testNumberOfSitesOption.Name.TrimStart('-'),
                    Type = "int",
                    Value = arguments.TestNumberOfSites.ToString(),
                });
            }
#endif

            var call = client.Start(start);
            await foreach (var message in call.ResponseStream.ReadAllAsync())
            {
                ColorConsole.WriteInfo($"Status: {message.Status}");
            }
        }
    }
}
