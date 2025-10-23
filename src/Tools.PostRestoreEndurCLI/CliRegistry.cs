using System;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Lamar;
using Microsoft.Extensions.Logging;

namespace Tools.PostRestoreEndurCLI
{
    public class CliRegistry : ServiceRegistry
    {
        public CliRegistry()
        {
            try
            {
                // TODO: Configure ILogger from DI container
                For<IRequestsManager>().Use<RequestsManager>();
                For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
                For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
            }
            catch (Exception e)
            {
                // TODO: Add proper logging using ILogger from DI container
                Console.Error.WriteLine($"Error in CliRegistry: {e}");
                throw;
            }
        }
    }
}