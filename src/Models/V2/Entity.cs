using System.Text;
using Microsoft.CloudHealth.PreviewMigration.Models.V2.Interfaces;

namespace Microsoft.CloudHealth.PreviewMigration.Models.V2;

public class Entity : IResourceType
{
    public required string Name { get; set; }
    public string Type => $"{Constants.ProviderNamespace}/{Constants.HealthModelsResourceType}/entities";
    public string ApiVersion => "2025-05-01-preview";

    public EntityProperties Properties { get; set; }

    public string ToBicepString(string symbolicName,
        string? overwriteNameParameter = null, string? parent = null,
        IEnumerable<string>? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var dependsOnString = dependsOn == null || !dependsOn.Any()
            ? "[]"
            : "[\n    " + string.Join("\n    ", dependsOn) + "\n  ]";

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

public class EntityProperties : IResourceProperties
{
    public string? DisplayName { get; set; }
    public string Impact { get; set; } = "Standard";
    public CanvasPosition? CanvasPosition { get; set; }

    public SignalGroup? SignalGroup { get; set; }

    public string ToBicepString()
    {
        var canvasPositionString = CanvasPosition == null
            ? "null"
            : $$"""
                {
                        x: {{CanvasPosition.X}}
                        y: {{CanvasPosition.Y}}
                      }
                """;

        var template = $$"""
                         {
                               displayName: '{{DisplayName}}'
                               impact: '{{Impact}}'
                               canvasPosition: {{canvasPositionString}}
                               signals: {{(SignalGroup == null ? "null" : SignalGroup.ToBicepString())}}
                           }
                         """;

        return template;
    }
}

public class SignalGroup
{
    public AzureResourceSignalGroup? AzureResource { get; set; }
    public LogAnalyticsSignalGroup? AzureLogAnalytics { get; set; }
    public AzureMonitorWorkspaceSignalGroup? AzureMonitorWorkspace { get; set; }

    public string ToBicepString()
    {
        var azureResourceString = AzureResource == null
            ? "null"
            : $$"""
                {
                          azureResourceId: '{{AzureResource.AzureResourceId}}'
                          authenticationSetting: '{{AzureResource.AuthenticationSetting}}'
                          signalAssignments: {{AzureResource.SignalAssignmentsToBicepString()}}
                        }
                """;

        var logAnalyticsString = AzureLogAnalytics == null
            ? "null"
            : $$"""
                {
                          logAnalyticsWorkspaceResourceId: '{{AzureLogAnalytics.LogAnalyticsWorkspaceResourceId}}'
                          authenticationSetting: '{{AzureLogAnalytics.AuthenticationSetting}}'
                          signalAssignments: {{AzureLogAnalytics.SignalAssignmentsToBicepString()}}
                        }
                """;

        var amwString = AzureMonitorWorkspace == null
            ? "null"
            : $$"""
                {
                          azureMonitorWorkspaceResourceId: '{{AzureMonitorWorkspace.AzureMonitorWorkspaceResourceId}}'
                          authenticationSetting: '{{AzureMonitorWorkspace.AuthenticationSetting}}'
                          signalAssignments: {{AzureMonitorWorkspace.SignalAssignmentsToBicepString()}}
                        }
                """;

        var template = $$"""
                         {
                                 azureResource: {{azureResourceString}}
                                 azureLogAnalytics: {{logAnalyticsString}}
                                 azureMonitorWorkspace: {{amwString}}
                             }
                         """;

        return template;
    }
}

public abstract class SignalAssignmentGroup
{
    public required string AuthenticationSetting { get; set; }
    public SignalAssignment? SignalAssignments { get; set; }

    public string SignalAssignmentsToBicepString()
    {
        string signalAssignmentsString;
        if (SignalAssignments == null || SignalAssignments.SignalDefinitions == null ||
            SignalAssignments.SignalDefinitions.Length == 0)
        {
            signalAssignmentsString = "null";
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            foreach (var sigDefName in SignalAssignments.SignalDefinitions)
            {
                sb.Append($$"""
                                        {
                                          signalDefinitions: ['{{sigDefName}}']
                                        }
                            """);
                sb.Append('\n');
            }

            sb.Append("          ]");
            signalAssignmentsString = sb.ToString();
        }

        return signalAssignmentsString;
    }
}

public class SignalAssignment
{
    public string[]? SignalDefinitions { get; set; }
}

public class AzureMonitorWorkspaceSignalGroup : SignalAssignmentGroup
{
    public required string AzureMonitorWorkspaceResourceId { get; set; }
}

public class LogAnalyticsSignalGroup : SignalAssignmentGroup
{
    public required string LogAnalyticsWorkspaceResourceId { get; set; }
}

public class AzureResourceSignalGroup : SignalAssignmentGroup
{
    public required string AzureResourceId { get; set; }
}

public class CanvasPosition
{
    public int X { get; set; }
    public int Y { get; set; }
}