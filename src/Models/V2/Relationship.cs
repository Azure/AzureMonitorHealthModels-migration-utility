using Microsoft.CloudHealth.PreviewMigration.Models.V2.Interfaces;

namespace Microsoft.CloudHealth.PreviewMigration.Models.V2;

public class Relationship : IResourceType
{
    public required string Name { get; set; }
    public string Type => $"{Constants.ProviderNamespace}/{Constants.HealthModelsResourceType}/relationships";
    public string ApiVersion => "2025-05-01-preview";
    public RelationshipProperties Properties { get; set; }

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
                           properties: {
                             parentEntityName: '{{Properties.ParentEntityName}}'
                             childEntityName: '{{Properties.ChildEntityName}}'
                           }
                           dependsOn: {{dependsOnString}}
                         }
                         """;

        return template;
    }
}

public class RelationshipProperties : IResourceProperties
{
    public required string ParentEntityName { get; set; }
    public required string ChildEntityName { get; set; }
}