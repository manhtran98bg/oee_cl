using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Machines;
using Rostek.Gateway.Runtime.Net100;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class Net100RuntimeTests
{
    private const string LiveXml = """
        <live xmlns="http://www.jsw.co.jp/net100/2.0">
          <address>172.20.20.11</address>
          <condname>81143-K2T-V300</condname>
          <status>down</status>
          <alarm>false</alarm>
          <message />
          <shotno>4490</shotno>
          <lastshotinfo type="text/csv">4490,"2026-09-20","06:17:11",1,42.93,60.00,3.90</lastshotinfo>
          <updated>2026-09-22T09:49:51</updated>
          <updatingdatabase>false</updatingdatabase>
        </live>
        """;

    [Fact]
    public void Live_parser_reads_namespaced_xml_and_embedded_csv()
    {
        var parser = new Net100LiveParser(new Net100LastShotInfoParser());

        var result = parser.Parse(LiveXml);

        Assert.Equal("172.20.20.11", result.Address);
        Assert.Equal("down", result.Status);
        Assert.False(result.Alarm);
        Assert.Equal(4490, result.ShotNumber);
        Assert.NotNull(result.LastShotInfo);
        Assert.Equal(1, result.LastShotInfo!.QualityCode);
        Assert.Equal(42.93, result.LastShotInfo.CycleTimeSeconds, 2);
    }

    [Theory]
    [InlineData("production", false, "run")]
    [InlineData("down", false, "stop")]
    [InlineData("arrange", false, "stop")]
    [InlineData("production", true, "error")]
    [InlineData("unknown", false, "disconnect")]
    public void Machine_state_mapping_matches_jsw_rules(string status, bool alarm, string expected)
    {
        Assert.Equal(expected, Net100ProfileRuntime.NormalizeMachineState(status, alarm));
    }

    [Fact]
    public async Task Client_adapter_sends_basic_authorization_header()
    {
        var handler = new CapturingHttpMessageHandler(LiveXml);
        await using var adapter = new Net100ClientAdapter(
            new Uri("http://172.20.20.5/net100"),
            "test-user",
            "test-password",
            handler);

        await adapter.ReadLiveAsync("172.20.20.11", 1000, CancellationToken.None);

        Assert.Equal("Basic", handler.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("test-user:test-password")),
            handler.Authorization?.Parameter);
    }

    [Fact]
    public async Task Machines_in_same_template_share_one_profile_adapter()
    {
        var templateId = Guid.NewGuid();
        var adapterFactory = new FakeNet100AdapterFactory(LiveXml, expectedReads: 2);
        var valueStore = new MachineValueStore();
        var options = Options.Create(new RuntimeOptions
        {
            MinimumPollingIntervalMs = 200,
            MaxNet100ConcurrentRequests = 2
        });
        var profileFactory = new Net100ProfileRuntimeFactory(
            adapterFactory,
            new Net100LiveParser(new Net100LastShotInfoParser()),
            valueStore,
            options,
            NullLogger<Net100ProfileRuntime>.Instance);
        var coordinator = new Net100RuntimeCoordinator(profileFactory, NullLogger<Net100RuntimeCoordinator>.Instance);
        var first = CreateMachine(templateId, "JSW-01", "172.20.20.11");
        var second = CreateMachine(templateId, "JSW-02", "172.20.20.12");
        var configuration = new RuntimeConfiguration(
            1,
            DateTimeOffset.UtcNow,
            new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                [first.MachineCode] = first,
                [second.MachineCode] = second
            });

        try
        {
            await coordinator.ApplyConfigurationAsync(configuration, CancellationToken.None);
            await adapterFactory.ReadsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(1, adapterFactory.CreateCount);
            Assert.Contains("172.20.20.11", adapterFactory.Addresses);
            Assert.Contains("172.20.20.12", adapterFactory.Addresses);
            Assert.Equal(4490L, valueStore.GetSnapshot("JSW-01")!.Values.Single(value => value.SignalCode == "SHOT_OK_COUNT").Value);
            Assert.Equal(42930, valueStore.GetSnapshot("JSW-02")!.Values.Single(value => value.SignalCode == "CYCLE_TIME_MS").Value);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    private static EffectiveMachineConfiguration CreateMachine(Guid templateId, string machineCode, string address) =>
        new(
            Guid.NewGuid(),
            machineCode,
            machineCode,
            Net100Configuration.Protocol,
            true,
            new EffectiveConnectionConfiguration(
                Net100Configuration.Protocol,
                address,
                80,
                "http://172.20.20.5:80/net100",
                null,
                null,
                null,
                null,
                null,
                1000,
                1000,
                0,
                200,
                new Dictionary<string, object> { [OeeTimeSources.OptionName] = OeeTimeSources.GatewayState }),
            [
                new EffectiveSignalConfiguration("MACHINE_STATE", Net100Configuration.MachineStateSource, "STRING", null, 1, 0, true, true, null, null),
                new EffectiveSignalConfiguration("SHOT_OK_COUNT", Net100Configuration.ShotNumberSource, "INT64", null, 1, 0, true, true, null, null),
                new EffectiveSignalConfiguration("CYCLE_TIME_MS", Net100Configuration.CycleTimeMsSource, "INT32", null, 1, 0, false, true, null, null)
            ])
        {
            TemplateId = templateId
        };

    private sealed class FakeNet100AdapterFactory(string responseXml, int expectedReads) : INet100ClientAdapterFactory
    {
        private int _readCount;

        public int CreateCount { get; private set; }
        public ConcurrentBag<string> Addresses { get; } = [];
        public TaskCompletionSource ReadsCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public INet100ClientAdapter Create(Uri baseAddress, string? authenticationMode, string? credentialReference)
        {
            CreateCount++;
            return new FakeNet100Adapter(this, responseXml, expectedReads);
        }

        private sealed class FakeNet100Adapter(FakeNet100AdapterFactory owner, string xml, int expectedReads) : INet100ClientAdapter
        {
            public Task<string> ReadLiveAsync(string machineAddress, int timeoutMs, CancellationToken cancellationToken)
            {
                owner.Addresses.Add(machineAddress);
                if (Interlocked.Increment(ref owner._readCount) >= expectedReads)
                {
                    owner.ReadsCompleted.TrySetResult();
                }

                return Task.FromResult(xml.Replace("172.20.20.11", machineAddress, StringComparison.Ordinal));
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingHttpMessageHandler(string responseXml) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml)
            });
        }
    }
}
