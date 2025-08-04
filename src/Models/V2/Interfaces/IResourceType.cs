namespace Microsoft.CloudHealth.PreviewMigration.Models.V2.Interfaces;

public interface IResourceType
{
    public string Name { get; set; }
    public string Type { get; }
    public string ApiVersion { get; }
    
    //public IResourceProperties Properties { get; set; }

    public string ToBicepString(
        string symbolicName, 
        string? overwriteNameParameter = null,
        string? parent = null,
        IEnumerable<string>? dependsOn = null);
}