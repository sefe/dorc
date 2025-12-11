using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.OAuth2;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 6)
        {
            PrintUsage();
            return;
        }

        // Validate and parse arguments
        string host = args[0];
        
        if (!int.TryParse(args[1], out int port))
        {
            Console.WriteLine("[ERROR] ✗ Invalid port number: '{0}'", args[1]);
            Console.WriteLine("        Port must be a valid integer (e.g., 5671, 5672)");
            Console.WriteLine();
            PrintUsage();
            return;
        }

        string tokenEndpoint = args[2];
        string clientId = args[3];
        string clientSecret = args[4];
        string scope = args[5];
        string principal = args.Length > 6 ? args[6] : "guest";

        // Validate required arguments are not empty
        if (string.IsNullOrWhiteSpace(host))
        {
            Console.WriteLine("[ERROR] ✗ Host cannot be empty");
            PrintUsage();
            return;
        }

        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            Console.WriteLine("[ERROR] ✗ Token endpoint cannot be empty");
            PrintUsage();
            return;
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            Console.WriteLine("[ERROR] ✗ Client ID cannot be empty");
            PrintUsage();
            return;
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            Console.WriteLine("[ERROR] ✗ Client secret cannot be empty");
            PrintUsage();
            return;
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            Console.WriteLine("[ERROR] ✗ Scope cannot be empty");
            PrintUsage();
            return;
        }

        PrintBanner(host, port, tokenEndpoint, clientId, scope, principal);

        try
        {
            await TestRabbitMqOAuthConnection(host, port, tokenEndpoint, clientId, clientSecret, scope, principal);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] ✗ Connection failed: {ex.GetType().Name}");
            Console.WriteLine($"        Message: {ex.Message}");
            Console.WriteLine($"\nFull Exception:");
            Console.WriteLine(ex);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage: Tools.RabbitMqOAuthTest <host> <port> <tokenEndpoint> <clientId> <clientSecret> <scope> [principal]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <host>              - RabbitMQ server hostname or IP address");
        Console.WriteLine("  <port>              - RabbitMQ server port (typically 5671 for AMQPS or 5672 for AMQP)");
        Console.WriteLine("  <tokenEndpoint>     - OAuth 2.0 token endpoint URL (e.g., https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token)");
        Console.WriteLine("  <clientId>          - OAuth 2.0 client ID (application ID in Azure AD)");
        Console.WriteLine("  <clientSecret>      - OAuth 2.0 client secret (application password)");
        Console.WriteLine("  <scope>             - OAuth 2.0 scope (e.g., api://resource-id/.default)");
        Console.WriteLine("  [principal]         - Optional RabbitMQ principal name (default: guest)");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  Tools.RabbitMqOAuthTest backboneut 5671 \\");
        Console.WriteLine("    https://login.microsoftonline.com/213c2807-792f-48e1-924a-eac984ef3354/oauth2/v2.0/token \\");
        Console.WriteLine("    764857fa-a568-4a30-8b74-517ba0468414 \\");
        Console.WriteLine("    your-client-secret \\");
        Console.WriteLine("    api://d4adfc67-f3b4-4965-bde9-26033e84f1e4/.default \\");
        Console.WriteLine("    rabbitmq-oauth2-dorc-client");
        Console.WriteLine();
    }

    static void PrintBanner(string host, int port, string tokenEndpoint, string clientId, string scope, string principal)
    {
        Console.WriteLine("==========================================================================================");
        Console.WriteLine("RabbitMQ OAuth Connection Test (v7)");
        Console.WriteLine("==========================================================================================");
        Console.WriteLine($"Host: {host}");
        Console.WriteLine($"Port: {port}");
        Console.WriteLine($"Principal: {principal}");
        Console.WriteLine($"Token Endpoint: {tokenEndpoint}");
        Console.WriteLine($"Client ID: {clientId}");
        Console.WriteLine($"Scope: {scope}");
        Console.WriteLine();
    }

    static async Task TestRabbitMqOAuthConnection(string host, int port, string tokenEndpoint, string clientId, string clientSecret, string scope, string principal)
    {
        Console.WriteLine($"[DEBUG] OAuth2 scope configured: {scope}");
        Console.WriteLine($"[DEBUG] OAuth2 principal configured: {principal}");
        Console.WriteLine("[INFO] Attempting AMQPS connection with OAuth token...\n");

        var tokenEndpointUri = new Uri(tokenEndpoint);

        // Create OAuth2 client builder (official RabbitMQ.Client.OAuth2 package)
        var oAuth2ClientBuilder = new OAuth2ClientBuilder(
            clientId,
            clientSecret,
            tokenEndpointUri);

        // Set scope if provided
        if (!string.IsNullOrWhiteSpace(scope))
        {
            oAuth2ClientBuilder.SetScope(scope);
            Console.WriteLine($"[DEBUG] OAuth 2.0 scope set: {scope}");
        }

        // Build the OAuth2 client
        var oAuth2Client = await oAuth2ClientBuilder.BuildAsync(CancellationToken.None);
        Console.WriteLine("[DEBUG] OAuth2 client built successfully");

        // Create credentials provider using the OAuth2 client
        var credentialsProvider = new OAuth2ClientCredentialsProvider("DOrc-Test", oAuth2Client);
        Console.WriteLine("[DEBUG] Credentials provider created");

        // Get the credentials that will be sent to RabbitMQ for debugging
        var credentials = await credentialsProvider.GetCredentialsAsync(CancellationToken.None);
        Console.WriteLine("[DEBUG] Credentials retrieved from provider");
        Console.WriteLine("[DEBUG] Credentials being sent to RabbitMQ:");
        Console.WriteLine($"  UserName: '{credentials.UserName}'");
        Console.WriteLine($"  Password (Token) Length: {credentials.Password?.Length ?? 0} characters");
        Console.WriteLine($"  Password (Token) Preview: {(credentials.Password?.Length > 50 ? credentials.Password?.Substring(0, 50) + "..." : credentials.Password)}");
        Console.WriteLine($"  Token Valid Until: {credentials.ValidUntil}");
        Console.WriteLine();

        // Create connection factory
        // NOTE: Per Dorc.Monitor implementation:
        // - UserName/Password are set but can be empty
        // - CredentialsProvider handles the actual OAuth token
        // - The CredentialsProvider returns both username and token as password
        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            VirtualHost = "/",
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            ClientProvidedName = $"DOrc-Test-{Environment.MachineName}-{Guid.NewGuid()}",
            UserName = "",         // Empty - CredentialsProvider will provide actual username
            Password = "",         // Empty - CredentialsProvider will provide OAuth token
            Ssl = new SslOption
            {
                Enabled = true,
                ServerName = host,
                Version = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                CertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            },
            CredentialsProvider = credentialsProvider
        };

        // Test token request first
        Console.WriteLine("[TRACE] Testing OAuth Token Request...");
        using (var client = new System.Net.Http.HttpClient())
        using (var tokenRequest = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "scope", scope },
            { "grant_type", "client_credentials" }
        }))
        {
            try
            {
                var response = await client.PostAsync(tokenEndpoint, tokenRequest);
                Console.WriteLine($"  Response Status: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"  Error: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Token request failed: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("[INFO] Creating RabbitMQ connection...");

        // Create and test connection
        await using (var connection = await factory.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            Console.WriteLine("[SUCCESS] ✓ Successfully connected to RabbitMQ with OAuth!");
            Console.WriteLine($"[SUCCESS] ✓ Connection State: {connection.IsOpen}");
            Console.WriteLine($"[SUCCESS] ✓ Channel State: {channel.IsOpen}");

            await channel.CloseAsync();
            await connection.CloseAsync();
        }
}
