using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Resources;
using Microsoft.CloudHealth.PreviewMigration.Models.V1;
using Microsoft.Extensions.Logging;

namespace Microsoft.CloudHealth.PreviewMigration;

public class ArmCreator
{
    /// <summary>
    /// Experimental method to create a V2 HealthModel from a V1 HealthModel using Azure Resource Manager.
    /// </summary>
    /// <param name="subscriptionId"></param>
    /// <param name="resourceGroupName"></param>
    /// <param name="v1HealthModel"></param>
    /// <param name="logger"></param>
    public static async Task CreateV2HealthModelWithArm(string subscriptionId, string resourceGroupName,
        HealthModel v1HealthModel, ILogger logger)
    {
        var armClient = new ArmClient(new DefaultAzureCredential()); // CodeQL [SM05137] This is a helper tool which is not used in production environments, so it is safe to use DefaultAzureCredential with all options enabled.

        var resourceGroupResourceId = ResourceGroupResource.CreateResourceIdentifier(subscriptionId, resourceGroupName);
        var resourceGroupResource = armClient.GetResourceGroupResource(resourceGroupResourceId);

        HealthModelCollection collection = resourceGroupResource.GetHealthModels();

        var location = v1HealthModel.location.ToLower();
        if (!Utils.SupportedV2Locations.Contains(location))
        {
            logger.LogWarning("Location {location} is not supported in V2. Falling back to {fallbackLocation}",
                location, Utils.SupportedV2Locations.First());
            location = Utils.SupportedV2Locations.First();
        }

        HealthModelData data = new HealthModelData(new AzureLocation(location));

        if (v1HealthModel.tags?.Count > 0)
        {
            foreach (var tag in v1HealthModel.tags)
            {
                data.Tags.Add(tag.Key, tag.Value);
            }
        }

        if (v1HealthModel.identity != null)
        {
            data.Identity = new ManagedServiceIdentity(v1HealthModel.identity.type);
            if (v1HealthModel.identity.userAssignedIdentities != null)
            {
                foreach (var userMi in v1HealthModel.identity.userAssignedIdentities)
                {
                    data.Identity.UserAssignedIdentities.Add(new ResourceIdentifier(userMi.Key),
                        new Azure.ResourceManager.Models.UserAssignedIdentity());
                }
            }
        }

        HealthModelResource v2HealthModel;
        try
        {
            ArmOperation<HealthModelResource> lro =
                await collection.CreateOrUpdateAsync(WaitUntil.Completed, v1HealthModel.name, data);
            v2HealthModel = lro.Value;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create HealthModel {healthModelId}", v1HealthModel.name);
            return;
        }

        HealthModelData resourceData = v2HealthModel.Data;
        logger.LogInformation("HealthModel {healthModelId} created", v2HealthModel.Id);

        if (v1HealthModel.identity != null)
        {
            var identities = new List<string>();
            if (!string.IsNullOrEmpty(v1HealthModel.identity.principalId))
            {
                identities.Add("SystemAssigned");
            }

            if (v1HealthModel.identity.userAssignedIdentities != null)
            {
                identities.AddRange(v1HealthModel.identity.userAssignedIdentities.Select(u => u.Key.Split('/').Last()));
            }

            foreach (var identity in identities)
            {
                var authenticationSettingData = new HealthModelAuthenticationSettingData()
                {
                    Properties =
                    {
                        DisplayName = identity
                    }
                };
                var authSetting = await v2HealthModel.GetHealthModelAuthenticationSettings().CreateOrUpdateAsync(
                    WaitUntil.Completed,
                    identity,
                    authenticationSettingData);

                logger.LogInformation("AuthenticationSetting created: {authenticationSetting}",
                    authSetting.Value.Data.Properties.DisplayName);
            }
        }

        var nodes = v1HealthModel.properties.nodes;
        if (nodes == null)
        {
            logger.LogInformation("No nodes found in v1 health model. Exiting...");
            return;
        }

        var signalDefinitions = v2HealthModel.GetHealthModelSignalDefinitions().GetAllAsync();
        await foreach (var page in signalDefinitions.AsPages())
        {
            foreach (var sigDef in page.Values)
            {
                logger.LogInformation("SignalDefinition: {signalDefinition}", sigDef.Data.Name);
            }
        }

        var allQueries = nodes.Where(n => n.queries != null).SelectMany(n => n.queries!);

        foreach (var query in allQueries)
        {
            var signalDefinitionData = new HealthModelSignalDefinitionData()
            {
                Properties = { DisplayName = query.name }
            };
        }

        foreach (var node in nodes)
        {
            var entityData = new HealthModelEntityData()
            {
                Properties =
                {
                    DisplayName = node.name,
                    Impact = node.impact,
                }
            };

            var entityResource = await v2HealthModel.GetHealthModelEntities().CreateOrUpdateAsync(
                WaitUntil.Completed,
                node.nodeId,
                entityData);

            logger.LogInformation("Entity created: {entity}", entityResource.Value.Data.Properties.DisplayName);
        }
    }
}