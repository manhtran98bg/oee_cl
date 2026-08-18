using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Runtime.OpcUa;

public interface IOpcUaClientAdapter : IAsyncDisposable
{
    Task<object?> ReadNodeValueAsync(NodeId nodeId, CancellationToken cancellationToken);
}

public interface IOpcUaClientAdapterFactory
{
    Task<IOpcUaClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken);
}

public sealed class OpcUaClientAdapterFactory : IOpcUaClientAdapterFactory
{
    public async Task<IOpcUaClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken)
    {
        var endpointUrl = string.IsNullOrWhiteSpace(connection.EndpointUrl)
            ? throw new InvalidOperationException("OPC UA endpoint URL is required.")
            : connection.EndpointUrl;

        using var timeout = new CancellationTokenSource(Math.Max(1, connection.ConnectTimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var telemetry = DefaultTelemetry.Create(_ => { });
            var applicationConfiguration = await CreateApplicationConfigurationAsync(connection, linked.Token);
            var endpoint = await CoreClientUtils.SelectEndpointAsync(applicationConfiguration, endpointUrl, useSecurity: false, connection.ConnectTimeoutMs, telemetry, linked.Token);
            var endpointConfiguration = EndpointConfiguration.Create(applicationConfiguration);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);
            var session = await new DefaultSessionFactory(telemetry).CreateAsync(
                applicationConfiguration,
                configuredEndpoint,
                updateBeforeConnect: false,
                sessionName: "Rostek Gateway",
                sessionTimeout: (uint)Math.Max(1, connection.ConnectTimeoutMs),
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: null).WaitAsync(linked.Token);

            return new OpcUaClientAdapter(session);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"OPC UA connect timed out after {connection.ConnectTimeoutMs} ms.");
        }
    }

    private static async Task<ApplicationConfiguration> CreateApplicationConfigurationAsync(
        EffectiveConnectionConfiguration connection,
        CancellationToken cancellationToken)
    {
        var pkiRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gateway",
            "pki");

        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "Rostek Gateway",
            ApplicationUri = $"urn:{Utils.GetHostName()}:RostekGateway",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "own"),
                    SubjectName = "CN=Rostek Gateway"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "trusted")
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "issuers")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "rejected")
                },
                AutoAcceptUntrustedCertificates = true
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = Math.Max(1, connection.RequestTimeoutMs)
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = Math.Max(1, connection.ConnectTimeoutMs)
            },
            TraceConfiguration = new TraceConfiguration()
        };

        await configuration.ValidateAsync(ApplicationType.Client, cancellationToken);
        return configuration;
    }
}

public sealed class OpcUaClientAdapter(ISession session) : IOpcUaClientAdapter
{
    public async Task<object?> ReadNodeValueAsync(NodeId nodeId, CancellationToken cancellationToken)
    {
        var nodesToRead = new ReadValueIdCollection
        {
            new()
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value
            }
        };

        var response = await session.ReadAsync(
            requestHeader: null,
            maxAge: 0,
            timestampsToReturn: TimestampsToReturn.Both,
            nodesToRead: nodesToRead,
            ct: cancellationToken);
        var dataValue = response.Results.Count == 0
            ? throw new ServiceResultException(StatusCodes.BadNoData, "OPC UA server returned no data.")
            : response.Results[0];

        if (StatusCode.IsBad(dataValue.StatusCode))
        {
            throw new ServiceResultException(dataValue.StatusCode);
        }

        return dataValue.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await session.CloseAsync(1000, true, CancellationToken.None);
        session.Dispose();
    }
}
