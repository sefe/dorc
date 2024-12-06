using System;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Lamar;
using log4net;

namespace Tools.PostRestoreEndurCLI
{
    public class CliRegistry : ServiceRegistry
    {
        public CliRegistry()
        {
            try
            {
                For<ILog>().Use(LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType));
                For<IRequestsManager>().Use<RequestsManager>();
                For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
            }
            catch (Exception e)
            {
                var log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

                log.Error(e);
                throw;
            }
        }
    }
}