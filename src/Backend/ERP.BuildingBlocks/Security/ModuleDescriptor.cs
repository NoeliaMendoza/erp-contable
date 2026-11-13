namespace ERP.BuildingBlocks.Security;

/// <summary>
/// Describe un módulo funcional que puede activarse por tenant (feature flag).
/// El código coincide con <c>platform.tenant_features.feature_code</c>.
/// </summary>
public sealed record ModuleDescriptor(string Code, string DisplayName, string Owner);
