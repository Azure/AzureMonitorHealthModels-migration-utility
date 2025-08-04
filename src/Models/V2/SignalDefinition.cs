using Microsoft.CloudHealth.PreviewMigration.Models.V2.Interfaces;

namespace Microsoft.CloudHealth.PreviewMigration.Models.V2;

public class SignalDefinition : IResourceType
{
    public required string Name { get; set; }
    public string Type => $"{Constants.ProviderNamespace}/{Constants.HealthModelsResourceType}/signalDefinitions";
    public string ApiVersion => "2025-05-01-preview";

    public SignalDefinitionProperties Properties { get; set; }

    public string ToBicepString(string symbolicName,
        string? overwriteNameParameter = null, string? parent = null,
        IEnumerable<string>? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var dependsOnString = dependsOn == null ? "[]" : "[\n    " + string.Join("\n    ", dependsOn) + "\n  ]";

        var template = $$"""
                         resource {{symbolicName}} '{{Type}}@{{ApiVersion}}' = {
                           parent: {{parent}}
                           name: {{overwriteNameParameter ?? $"'{Name}'"}}
                           properties: {{Properties.ToBicepString()}}
                           dependsOn: {{dependsOnString}}
                         }
                         """;

        return template;
    }
}

public abstract class SignalDefinitionProperties : IResourceProperties
{
    public abstract string SignalKind { get; }
    public string? DataUnit { get; set; }
    public string? TimeGrain { get; set; }
    public string RefreshInterval { get; set; } = "PT1M";
    public required EvaluationRules EvaluationRules { get; set; }

    public abstract string ToBicepString();
}

public class EvaluationRules
{
    public required StaticThreshold UnhealthyRule { get; set; }
    public required StaticThreshold DegradedRule { get; set; }
}

public class StaticThreshold
{
    public required string Operator { get; set; }
    public required string Threshold { get; set; }
}

public class AzureResourceSignalDefinitionProperties : SignalDefinitionProperties
{
    public override string SignalKind => "AzureResourceMetric";
    public required string metricNamespace { get; set; }
    public required string metricName { get; set; }
    public required string aggregationType { get; set; }
    public string? dimension { get; set; }
    public string? dimensionFilter { get; set; }

    public override string ToBicepString()
    {
        var template = $$"""
                         {
                               displayName: '{{metricName}}'
                               signalKind: '{{SignalKind}}'
                               dataUnit: {{(DataUnit == null ? "null" : "'" + DataUnit + "'")}}
                               metricNamespace: '{{metricNamespace}}'
                               metricName: '{{metricName}}'
                               timeGrain: '{{TimeGrain}}'
                               refreshInterval: '{{RefreshInterval}}'
                               aggregationType: '{{aggregationType}}'
                               dimension: {{(dimension == null ? "null" : "'" + dimension + "'")}}
                               dimensionFilter: {{(dimensionFilter == null ? "null" : "'" + dimensionFilter + "'")}}
                               evaluationRules: {
                                 unhealthyRule: {
                                   operator: '{{EvaluationRules.UnhealthyRule.Operator}}'
                                   threshold: '{{EvaluationRules.UnhealthyRule.Threshold}}'
                                 }
                                 degradedRule: {
                                   operator: '{{EvaluationRules.DegradedRule.Operator}}'
                                   threshold: '{{EvaluationRules.DegradedRule.Threshold}}'
                                 }
                               }
                             }
                         """;

        return template;
    }
}

public class LogAnalyticsSignalDefinitionProperties : SignalDefinitionProperties
{
    public required string Name { get; set; }
    public required string QueryText { get; set; }
    public string? ValueColumnName { get; set; }
    public override string SignalKind => "LogAnalyticsQuery";

    public override string ToBicepString()
    {
        var template = $$"""
                         {
                               displayName: '{{Name}}'
                               signalKind: '{{SignalKind}}'
                               dataUnit: {{(DataUnit == null ? "null" : "'" + DataUnit + "'")}}
                               queryText: '{{QueryText.Replace("\n", "\\n")}}'
                               valueColumnName: {{(ValueColumnName == null ? "null" : $"'{ValueColumnName}'")}}
                               timeGrain: {{(TimeGrain == null ? "null" : $"'{TimeGrain}'")}}
                               refreshInterval: '{{RefreshInterval}}'
                               evaluationRules: {
                                 unhealthyRule: {
                                   operator: '{{EvaluationRules.UnhealthyRule.Operator}}'
                                   threshold: '{{EvaluationRules.UnhealthyRule.Threshold}}'
                                 }
                                 degradedRule: {
                                   operator: '{{EvaluationRules.DegradedRule.Operator}}'
                                   threshold: '{{EvaluationRules.DegradedRule.Threshold}}'
                                 }
                               }
                             }
                         """;

        return template;
    }
}

public class PrometheusSignalDefinitionProperties : SignalDefinitionProperties
{
    public required string Name { get; set; }
    public required string QueryText { get; set; }
    public override string SignalKind => "PrometheusMetricsQuery";

    public override string ToBicepString()
    {
        var template = $$"""
                         {
                               displayName: '{{Name}}'
                               signalKind: '{{SignalKind}}'
                               dataUnit: {{(DataUnit == null ? "null" : "'" + DataUnit + "'")}}
                               queryText: '{{QueryText.Replace("\n", "\\n")}}'
                               timeGrain: {{(TimeGrain == null ? "null" : $"'{TimeGrain}'")}}
                               refreshInterval: '{{RefreshInterval}}'
                               evaluationRules: {
                                 unhealthyRule: {
                                   operator: '{{EvaluationRules.UnhealthyRule.Operator}}'
                                   threshold: '{{EvaluationRules.UnhealthyRule.Threshold}}'
                                 }
                                 degradedRule: {
                                   operator: '{{EvaluationRules.DegradedRule.Operator}}'
                                   threshold: '{{EvaluationRules.DegradedRule.Threshold}}'
                                 }
                               }
                             }
                         """;

        return template;
    }
}