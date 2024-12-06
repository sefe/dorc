using System;

namespace Org.OpenAPITools.Client.Auth
{
    /// <summary>
    /// Factory class to get a particular Token Generator
    /// Benefit to this is you can easily swap out Auth Token flows without having to change all your logic/integrated code each ti
    /// </summary>
    public class AuthTokenGeneratorFactory
    {
        public static IAuthTokenGenerator GetAuthTokenGenerator(AadConnectionSettings aadConnectionSettings)
        {
            // Kept it simple here to just check if ClientSecret is present.
            // In other cases we do further checks, but this should cover it for now.
            if (!String.IsNullOrEmpty(aadConnectionSettings.ClientSecret))
            {
                return new AppFlowAuthTokenGenerator(aadConnectionSettings);
            }
            else
            {
                // Here you would usually put in your alternative token generator flow if required
                throw new NotSupportedException();
            }
        }
    }   
}