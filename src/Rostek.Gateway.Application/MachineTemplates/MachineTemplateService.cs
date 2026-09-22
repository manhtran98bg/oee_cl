using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.MachineTemplates;

public sealed class MachineTemplateService(
    IConfigRepository repository,
    IOptions<RuntimeOptions> runtimeOptions,
    ILogger<MachineTemplateService> logger) : IMachineTemplateService
{
    public async Task<IReadOnlyList<MachineTemplateListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var templates = await repository.ListTemplatesAsync(includeSignals: true, cancellationToken);
        return templates
            .OrderBy(template => template.Code)
            .Select(template => new MachineTemplateListItem(template.Id, template.Code, template.Name, template.Protocol, template.Signals.Count, template.Enabled))
            .ToList();
    }

    public async Task<MachineTemplateInput?> GetInputAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateAsync(id, includeSignals: false, cancellationToken);
        return template is null
            ? null
            : new MachineTemplateInput
            {
                Id = template.Id,
                Code = template.Code,
                Name = template.Name,
                Protocol = template.Protocol,
                Manufacturer = template.Manufacturer,
                Model = template.Model,
                DefaultPollingIntervalMs = template.DefaultPollingIntervalMs,
                Net100ServerHost = template.Net100ServerHost,
                Net100ServerPort = template.Net100ServerPort,
                Net100BasePath = template.Net100BasePath,
                Net100AuthenticationMode = template.Net100AuthenticationMode,
                Net100CredentialReference = template.Net100CredentialReference,
                Description = template.Description,
                Enabled = template.Enabled
            };
    }

    public async Task<IReadOnlyList<TemplateSignalListItem>> ListSignalsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateAsync(templateId, includeSignals: true, cancellationToken);
        return template?.Signals
            .OrderBy(signal => signal.DisplayOrder)
            .ThenBy(signal => signal.SignalCode)
            .Select(signal => new TemplateSignalListItem(signal.Id, signal.SignalCode, signal.DisplayName, signal.SourceAddress, signal.DataType, signal.Required, signal.Enabled))
            .ToList() ?? [];
    }

    public async Task<TemplateSignalInput?> GetSignalInputAsync(Guid templateId, Guid signalId, CancellationToken cancellationToken)
    {
        var signal = await repository.GetTemplateSignalAsync(signalId, cancellationToken);
        if (signal is null || signal.TemplateId != templateId)
        {
            return null;
        }

        return new TemplateSignalInput
        {
            Id = signal.Id,
            TemplateId = signal.TemplateId,
            SignalCode = signal.SignalCode,
            DisplayName = signal.DisplayName,
            SourceAddress = signal.SourceAddress,
            DataType = signal.DataType,
            AccessMode = signal.AccessMode,
            SamplingIntervalMs = signal.SamplingIntervalMs,
            ScalingFactor = signal.ScalingFactor,
            ScalingOffset = signal.ScalingOffset,
            Required = signal.Required,
            Enabled = signal.Enabled,
            ValueMappingJson = signal.ValueMappingJson,
            OptionsJson = signal.OptionsJson,
            DisplayOrder = signal.DisplayOrder
        };
    }

    public async Task<GatewayResult<Guid>> SaveAsync(MachineTemplateInput input, string? userName, CancellationToken cancellationToken)
    {
        var code = NormalizeCode(input.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return GatewayResult<Guid>.Fail("Template code is required.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return GatewayResult<Guid>.Fail("Template name is required.");
        }

        if (input.DefaultPollingIntervalMs < runtimeOptions.Value.MinimumPollingIntervalMs)
        {
            return GatewayResult<Guid>.Fail($"Default polling interval must be at least {runtimeOptions.Value.MinimumPollingIntervalMs} ms.");
        }

        if (input.Protocol == GatewayProtocol.Net100Http)
        {
            if (string.IsNullOrWhiteSpace(input.Net100ServerHost))
            {
                return GatewayResult<Guid>.Fail("NET100 server IP or host is required.");
            }

            if (input.Net100ServerPort is < 1 or > 65535)
            {
                return GatewayResult<Guid>.Fail("NET100 server port must be between 1 and 65535.");
            }

            var authenticationMode = NormalizeAuthenticationMode(input.Net100AuthenticationMode);
            if (authenticationMode is not ("NONE" or "BASIC"))
            {
                return GatewayResult<Guid>.Fail("NET100 authentication mode must be NONE or BASIC.");
            }

            if (authenticationMode == "BASIC" && string.IsNullOrWhiteSpace(input.Net100CredentialReference))
            {
                return GatewayResult<Guid>.Fail("NET100 credential reference is required for BASIC authentication.");
            }
        }

        if (await repository.TemplateCodeExistsAsync(code, input.Id, cancellationToken))
        {
            return GatewayResult<Guid>.Fail($"Template code '{code}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        MachineTemplate template;
        var isNew = input.Id is null;
        if (input.Id is Guid id)
        {
            template = await repository.GetTemplateAsync(id, includeSignals: false, cancellationToken) ?? throw new InvalidOperationException("Template not found.");
            template.UpdatedAtUtc = now;
        }
        else
        {
            template = new MachineTemplate { CreatedAtUtc = now, UpdatedAtUtc = now };
            await repository.AddTemplateAsync(template, cancellationToken);
        }

        template.Code = code;
        template.Name = input.Name.Trim();
        template.Protocol = input.Protocol;
        template.Manufacturer = NullIfWhiteSpace(input.Manufacturer);
        template.Model = NullIfWhiteSpace(input.Model);
        template.DefaultPollingIntervalMs = input.DefaultPollingIntervalMs;
        template.Net100ServerHost = input.Protocol == GatewayProtocol.Net100Http
            ? NullIfWhiteSpace(input.Net100ServerHost)
            : null;
        template.Net100ServerPort = input.Protocol == GatewayProtocol.Net100Http
            ? input.Net100ServerPort
            : 80;
        template.Net100BasePath = input.Protocol == GatewayProtocol.Net100Http
            ? Net100Configuration.NormalizeBasePath(input.Net100BasePath)
            : Net100Configuration.DefaultBasePath;
        template.Net100AuthenticationMode = input.Protocol == GatewayProtocol.Net100Http
            ? NormalizeAuthenticationMode(input.Net100AuthenticationMode)
            : "NONE";
        template.Net100CredentialReference = input.Protocol == GatewayProtocol.Net100Http &&
                                             template.Net100AuthenticationMode == "BASIC"
            ? NullIfWhiteSpace(input.Net100CredentialReference)
            : null;
        template.Description = NullIfWhiteSpace(input.Description);
        template.Enabled = input.Enabled;

        if (isNew && input.Protocol == GatewayProtocol.Net100Http)
        {
            AddDefaultNet100Signals(template, now);
        }

        await AddAuditAsync(input.Id is null ? "Create machine template" : "Update machine template", nameof(MachineTemplate), template.Id, userName, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Saved machine template {TemplateCode} ({TemplateId}). Protocol={Protocol}, Action={Action}",
            template.Code,
            template.Id,
            template.Protocol,
            input.Id is null ? "Create" : "Update");
        return GatewayResult<Guid>.Ok(template.Id);
    }

    public async Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken)
    {
        var source = await repository.GetTemplateAsync(id, includeSignals: true, cancellationToken);
        if (source is null)
        {
            return GatewayResult<Guid>.Fail("Template not found.");
        }

        var code = NormalizeCode(newCode);
        if (await repository.TemplateCodeExistsAsync(code, null, cancellationToken))
        {
            return GatewayResult<Guid>.Fail($"Template code '{code}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var clone = new MachineTemplate
        {
            Code = code,
            Name = $"{source.Name} Copy",
            Protocol = source.Protocol,
            Manufacturer = source.Manufacturer,
            Model = source.Model,
            DefaultPollingIntervalMs = source.DefaultPollingIntervalMs,
            Net100ServerHost = source.Net100ServerHost,
            Net100ServerPort = source.Net100ServerPort,
            Net100BasePath = source.Net100BasePath,
            Net100AuthenticationMode = source.Net100AuthenticationMode,
            Net100CredentialReference = source.Net100CredentialReference,
            Description = source.Description,
            Enabled = source.Enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Signals = source.Signals.Select(signal => new TemplateSignal
            {
                SignalCode = signal.SignalCode,
                DisplayName = signal.DisplayName,
                SourceAddress = signal.SourceAddress,
                DataType = signal.DataType,
                AccessMode = signal.AccessMode,
                SamplingIntervalMs = signal.SamplingIntervalMs,
                ScalingFactor = signal.ScalingFactor,
                ScalingOffset = signal.ScalingOffset,
                Required = signal.Required,
                Enabled = signal.Enabled,
                ValueMappingJson = signal.ValueMappingJson,
                OptionsJson = signal.OptionsJson,
                DisplayOrder = signal.DisplayOrder,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList()
        };

        await repository.AddTemplateAsync(clone, cancellationToken);
        await AddAuditAsync("Clone machine template", nameof(MachineTemplate), clone.Id, userName, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Cloned machine template {SourceTemplateCode} to {TemplateCode} ({TemplateId})", source.Code, clone.Code, clone.Id);
        return GatewayResult<Guid>.Ok(clone.Id);
    }

    public async Task<GatewayResult<Guid>> SaveSignalAsync(TemplateSignalInput input, string? userName, CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateAsync(input.TemplateId, includeSignals: true, cancellationToken);
        if (template is null)
        {
            return GatewayResult<Guid>.Fail("Template not found.");
        }

        var code = NormalizeCode(input.SignalCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            return GatewayResult<Guid>.Fail("Signal code is required.");
        }

        if (input.Required && !input.Enabled)
        {
            return GatewayResult<Guid>.Fail("Required signal cannot be disabled.");
        }

        if (string.IsNullOrWhiteSpace(input.SourceAddress))
        {
            return GatewayResult<Guid>.Fail("Source address is required.");
        }

        if (template.Signals.Any(signal => signal.SignalCode.Equals(code, StringComparison.OrdinalIgnoreCase) && signal.Id != input.Id))
        {
            return GatewayResult<Guid>.Fail($"Signal code '{code}' already exists in this template.");
        }

        var now = DateTimeOffset.UtcNow;
        TemplateSignal signal;
        if (input.Id is Guid id)
        {
            signal = template.Signals.Single(item => item.Id == id);
            signal.UpdatedAtUtc = now;
        }
        else
        {
            signal = new TemplateSignal { TemplateId = template.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
            await repository.AddTemplateSignalAsync(signal, cancellationToken);
        }

        signal.SignalCode = code;
        signal.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? code : input.DisplayName.Trim();
        signal.SourceAddress = input.SourceAddress.Trim();
        signal.DataType = input.DataType;
        signal.AccessMode = input.AccessMode;
        signal.SamplingIntervalMs = input.SamplingIntervalMs;
        signal.ScalingFactor = input.ScalingFactor;
        signal.ScalingOffset = input.ScalingOffset;
        signal.Required = input.Required;
        signal.Enabled = input.Enabled;
        signal.ValueMappingJson = NullIfWhiteSpace(input.ValueMappingJson);
        signal.OptionsJson = NullIfWhiteSpace(input.OptionsJson);
        signal.DisplayOrder = input.DisplayOrder;

        await AddAuditAsync(input.Id is null ? "Create template signal" : "Update template signal", nameof(TemplateSignal), signal.Id, userName, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Saved template signal {SignalCode} ({SignalId}) for template {TemplateCode}. Action={Action}",
            signal.SignalCode,
            signal.Id,
            template.Code,
            input.Id is null ? "Create" : "Update");
        return GatewayResult<Guid>.Ok(signal.Id);
    }

    public async Task<GatewayResult> DeleteSignalAsync(Guid id, string? userName, CancellationToken cancellationToken)
    {
        var signal = await repository.GetTemplateSignalAsync(id, cancellationToken);
        if (signal is null)
        {
            return GatewayResult.Fail("Template signal not found.");
        }

        repository.RemoveTemplateSignal(signal);
        await AddAuditAsync("Delete template signal", nameof(TemplateSignal), id, userName, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Deleted template signal {SignalCode} ({SignalId})", signal.SignalCode, signal.Id);
        return GatewayResult.Ok();
    }

    private Task AddAuditAsync(string action, string entityType, Guid entityId, string? userName, CancellationToken cancellationToken) =>
        repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

    private static void AddDefaultNet100Signals(MachineTemplate template, DateTimeOffset now)
    {
        template.Signals =
        [
            CreateSignal(template, "MACHINE_STATE", "Machine State", Net100Configuration.MachineStateSource, SignalDataType.String, true, 0, now),
            CreateSignal(template, "SHOT_OK_COUNT", "Shot OK Count", Net100Configuration.ShotNumberSource, SignalDataType.Int64, true, 1, now),
            CreateSignal(template, "CYCLE_TIME_MS", "Cycle Time", Net100Configuration.CycleTimeMsSource, SignalDataType.Int32, false, 2, now),
            CreateSignal(template, "NET100_ALARM", "NET100 Alarm", Net100Configuration.AlarmSource, SignalDataType.Boolean, false, 3, now),
            CreateSignal(template, "NET100_QUALITY_CODE", "NET100 Quality Code", Net100Configuration.QualityCodeSource, SignalDataType.Int32, false, 4, now)
        ];
    }

    private static TemplateSignal CreateSignal(
        MachineTemplate template,
        string code,
        string name,
        string sourceAddress,
        SignalDataType dataType,
        bool required,
        int displayOrder,
        DateTimeOffset now) =>
        new()
        {
            Template = template,
            TemplateId = template.Id,
            SignalCode = code,
            DisplayName = name,
            SourceAddress = sourceAddress,
            DataType = dataType,
            Required = required,
            Enabled = true,
            DisplayOrder = displayOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string NormalizeAuthenticationMode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "NONE" : value.Trim().ToUpperInvariant();
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
