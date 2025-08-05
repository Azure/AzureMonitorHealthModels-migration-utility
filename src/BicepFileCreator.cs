using System.IO.Abstractions;
using System.Text;
using Bicep.Core;
using Bicep.Core.Analyzers.Interfaces;
using Bicep.Core.Analyzers.Linter;
using Bicep.Core.Features;
using Bicep.Core.FileSystem;
using Bicep.Core.Registry;
using Bicep.Core.Registry.Auth;
using Bicep.Core.Semantics.Namespaces;
using Bicep.Core.TypeSystem.Providers;
using Bicep.Core.Utils;
using Microsoft.CloudHealth.PreviewMigration.Models.V2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HealthModel = Microsoft.CloudHealth.PreviewMigration.Models.V1.HealthModel;

namespace Microsoft.CloudHealth.PreviewMigration;

public class BicepFileCreator
{
    public static async Task CompileAndWriteOutputFile(HealthModel v1HealthModel,
        string outputFolder,
        ILogger logger, bool compileArmTemplate = false)
    {
        var bicep = ConvertToV2HealthModel(v1HealthModel, logger);

        if (string.IsNullOrEmpty(bicep))
        {
            logger.LogWarning("No bicep output generated for health model {healthModelName}. Skipping writing output file",
                v1HealthModel.name);
            return;
        }

        var output = bicep;

        var extension = ".bicep";

        if (compileArmTemplate)
        {
            output = await CompileBicepToArmTemplate(bicep);
            extension = ".json";
        }

        var outFilePath = Path.Combine(outputFolder, v1HealthModel.name + extension);

        logger.LogInformation("Writing result to output file {outputFile}...", outFilePath);
        Directory.CreateDirectory(outputFolder);
        await File.WriteAllTextAsync(outFilePath, output);
    }

    private static string? ConvertToV2HealthModel(HealthModel v1HealthModel, ILogger logger)
    {
        var location = v1HealthModel.location.ToLower();
        if (!Program.SupportedV2Locations.Contains(location))
        {
            logger.LogWarning("Location {location} is not supported in V2. Falling back to {fallbackLocation}",
                location, Program.SupportedV2Locations.First());
            location = Program.SupportedV2Locations.First();
        }

        var resourceNameParameterName = "resourceName";

        var bicepBuilder = new StringBuilder();
        bicepBuilder.AppendLine($"param {resourceNameParameterName} string = '{v1HealthModel.name}'");
        bicepBuilder.AppendLine($"param location string = '{location}'");
        bicepBuilder.AppendLine();

        var v2HealthModel = new Models.V2.HealthModel
        {
            Name = v1HealthModel.name,
            Identity = v1HealthModel.identity != null
                ? new Models.V2.Identity
                {
                    Type = v1HealthModel.identity.type,
                    UserAssignedIdentities = v1HealthModel.identity.userAssignedIdentities?.ToDictionary(kvp => kvp.Key,
                        _ => new object())
                }
                : null,
            Tags = v1HealthModel.tags,
        };

        var modelSymbolicName = "healthModel";

        bicepBuilder.AppendLine(v2HealthModel.ToBicepString(modelSymbolicName, resourceNameParameterName));
        bicepBuilder.AppendLine();

        if (v1HealthModel.properties.nodes == null)
        {
            logger.LogWarning("No nodes found in v1 health model");
            logger.LogInformation(bicepBuilder.ToString());
            return null;
        }

        var allQueryIds = v1HealthModel.properties.nodes.SelectMany(n => n.queries?.Select(q => q.queryId) ?? []).ToList();
        // check if no duplicates, else return. Should not happen, though.
        if (allQueryIds.Count != allQueryIds.Distinct().Count())
        {
            logger.LogWarning("Duplicate queryIds found in v1 health model. Please ensure all queryIds are unique within the health model");
            return null;
        }

        // Key: symbolic name, Value: object
        var signalDefinitions = new Dictionary<string, SignalDefinition>();
        var authenticationSettings = new Dictionary<string, AuthenticationSetting>();
        var entities = new Dictionary<string, Entity>();
        var relationships = new Dictionary<string, Relationship>();

        if (v1HealthModel.identity != null)
        {
            if (v1HealthModel.identity.type.Contains("SystemAssigned", StringComparison.InvariantCultureIgnoreCase))
            {
                var authenticationSetting = new AuthenticationSetting
                {
                    Name = "SystemAssigned",
                    Properties = new ManagedIdentityAuthenticationSettingProperties
                    {
                        DisplayName = "SystemAssigned",
                        ManagedIdentityName = "SystemAssigned"
                    }
                };
                var symbolicName = "authenticationSettingSystemAssigned";
                authenticationSettings.Add(symbolicName, authenticationSetting);
                bicepBuilder.AppendLine();
                bicepBuilder.AppendLine(authenticationSetting.ToBicepString(symbolicName, parent: modelSymbolicName));
            }

            if (v1HealthModel.identity.userAssignedIdentities != null)
            {
                foreach (var userMi in v1HealthModel.identity.userAssignedIdentities)
                {
                    var userMiName = userMi.Key.Split('/').Last();
                    var authenticationSetting = new AuthenticationSetting
                    {
                        Name = userMi.Key.GenerateDeterministicGuid().ToString(),
                        Properties = new ManagedIdentityAuthenticationSettingProperties
                        {
                            DisplayName = userMiName,
                            ManagedIdentityName = userMi.Key
                        }
                    };
                    var symbolicName = "authenticationSettingUserMi" + authenticationSettings.Count;
                    authenticationSettings.Add(symbolicName, authenticationSetting);
                    bicepBuilder.AppendLine();
                    bicepBuilder.AppendLine(authenticationSetting.ToBicepString(symbolicName, parent: modelSymbolicName));
                }
            }
        }

        foreach (var node in v1HealthModel.properties.nodes)
        {
            var queries = node.queries;
            if (queries == null)
                continue;

            foreach (var query in queries)
            {
                if (query.dataType == "Text")
                {
                    logger.LogWarning(
                        "Query {queryId} has unsupported data type '{dataType}'. Not migrating this query!",
                        query.queryId,
                        query.dataType);
                    continue;
                }

                var signalDefinition = new SignalDefinition()
                {
                    Name = query.queryId
                };
                if (query.queryType == "ResourceMetricsQuery")
                {
                    if (query.metricNamespace.Equals("microsoft.healthmodel/healthmodels", StringComparison.InvariantCultureIgnoreCase))
                    {
                        logger.LogWarning(
                            "Query {queryId} for '{metricNamespace}' will not be migrated. Nested health models are handled differently!",
                            query.queryId,
                            query.metricNamespace);
                        continue;
                    }

                    var properties = new AzureResourceSignalDefinitionProperties
                    {
                        metricName = query.metricName,
                        metricNamespace = query.metricNamespace,
                        TimeGrain = query.timeGrain,
                        aggregationType = query.aggregationType,
                        DataUnit = query.dataUnit,
                        dimensionFilter = query.dimensionFilter,
                        dimension = query.dimension,
                        EvaluationRules = new EvaluationRules
                        {
                            UnhealthyRule = new StaticThreshold
                            {
                                Operator = query.unhealthyOperator,
                                Threshold = query.unhealthyThreshold
                            },
                            DegradedRule = new StaticThreshold
                            {
                                Operator = query.degradedOperator,
                                Threshold = query.degradedThreshold
                            }
                        }
                    };
                    signalDefinition.Properties = properties;
                }
                else if (query.queryType == "LogQuery")
                {
                    var properties = new LogAnalyticsSignalDefinitionProperties
                    {
                        Name = query.name,
                        QueryText = query.queryText,
                        DataUnit = query.dataUnit,
                        TimeGrain = query.timeGrain,
                        ValueColumnName = query.valueColumnName,
                        EvaluationRules = new EvaluationRules
                        {
                            UnhealthyRule = new StaticThreshold
                            {
                                Operator = query.unhealthyOperator,
                                Threshold = query.unhealthyThreshold
                            },
                            DegradedRule = new StaticThreshold
                            {
                                Operator = query.degradedOperator,
                                Threshold = query.degradedThreshold
                            }
                        }
                    };
                    signalDefinition.Properties = properties;
                }
                else if (query.queryType == "PrometheusMetricsQuery")
                {
                    var properties = new PrometheusSignalDefinitionProperties
                    {
                        Name = query.name,
                        QueryText = query.queryText,
                        DataUnit = query.dataUnit,
                        TimeGrain = query.timeGrain,
                        EvaluationRules = new EvaluationRules
                        {
                            UnhealthyRule = new StaticThreshold
                            {
                                Operator = query.unhealthyOperator,
                                Threshold = query.unhealthyThreshold
                            },
                            DegradedRule = new StaticThreshold
                            {
                                Operator = query.degradedOperator,
                                Threshold = query.degradedThreshold
                            }
                        }
                    };
                    signalDefinition.Properties = properties;
                }

                var symbolicName = $"sigDef{signalDefinitions.Count}";
                signalDefinitions.Add(symbolicName, signalDefinition);

                bicepBuilder.AppendLine();
                bicepBuilder.AppendLine(signalDefinition.ToBicepString(symbolicName, parent: modelSymbolicName));
            }
        }

        foreach (var node in v1HealthModel.properties.nodes)
        {
            // Special handling for root node
            var isRoot = node.nodeId == "0";
            var nodeName = isRoot ? v2HealthModel.Name : node.nodeId;

            var entity = new Entity
            {
                Name = nodeName,
                Properties = new EntityProperties
                {
                    DisplayName = node.name,
                    Impact = node.impact,
                    CanvasPosition = node.visual == null
                        ? null
                        : new CanvasPosition
                        {
                            X = node.visual.x,
                            Y = node.visual.y
                        }
                }
            };

            var dependsOn = new List<string>();

            KeyValuePair<string, AuthenticationSetting>? authenticationSetting = null;
            var queries = node.queries?.Where(q => q.enabledState == "Enabled").ToList(); // Filter out any disabled queries, those will not be assigned
            if (queries?.Count > 0)
            {
                authenticationSetting = authenticationSettings.FirstOrDefault(a =>
                    a.Value.Properties.ManagedIdentityName.EndsWith(node.credentialId, StringComparison.InvariantCultureIgnoreCase));

                var signalGroup = new SignalGroup();

                var resourceMetricsQueries = queries
                    .Where(q => q.queryType == "ResourceMetricsQuery")
                    .ToList();
                if (resourceMetricsQueries.Count != 0)
                {
                    signalGroup.AzureResource = new AzureResourceSignalGroup
                    {
                        AuthenticationSetting = authenticationSetting.Value.Value.Name,
                        AzureResourceId = node.azureResourceId,
                        SignalAssignments = new SignalAssignment
                        {
                            SignalDefinitions = resourceMetricsQueries
                                .Where(q => !q.metricNamespace.Equals("microsoft.healthmodel/healthmodels",
                                    StringComparison.InvariantCultureIgnoreCase))
                                .Select(q => q.queryId).ToArray()
                        }
                    };

                    // Special handling for nested health models
                    if (node.azureResourceId.Contains("microsoft.healthmodel/healthmodels", StringComparison.InvariantCultureIgnoreCase))
                    {
                        signalGroup.AzureResource.AzureResourceId = node.azureResourceId.Replace(
                            "microsoft.healthmodel/healthmodels", "Microsoft.CloudHealth/healthmodels", StringComparison.InvariantCultureIgnoreCase);
                        logger.LogInformation(
                            "Replacing 'microsoft.healthmodel/healthmodels' with 'Microsoft.Cloudhealth/healthmodels' in AzureResourceId for nested health model {nodeName}. Ensure that the nested health model is also being converted and resides in the same resource group as before!",
                            nodeName);
                    }

                    var dependentQueries = resourceMetricsQueries
                        .Where(q => !q.metricNamespace.Equals("microsoft.healthmodel/healthmodels",
                            StringComparison.InvariantCultureIgnoreCase))
                        .Select(q => signalDefinitions.First(kvp => kvp.Value.Name == q.queryId).Key);
                    dependsOn.AddRange(dependentQueries);
                }

                // Filter out any queries that are of type LogQuery and have a data type of Text
                var logAnalyticsQueries = queries.Where(q => q.queryType == "LogQuery").Where(q => q.dataType != "Text")
                    .ToList();
                if (logAnalyticsQueries.Count != 0)
                {
                    signalGroup.AzureLogAnalytics = new LogAnalyticsSignalGroup
                    {
                        AuthenticationSetting = authenticationSetting.Value.Value.Name,
                        LogAnalyticsWorkspaceResourceId = node.logAnalyticsResourceId,
                        SignalAssignments = new SignalAssignment
                        {
                            SignalDefinitions = logAnalyticsQueries.Select(q => q.queryId).ToArray()
                        }
                    };

                    dependsOn.AddRange(logAnalyticsQueries.Select(q =>
                        signalDefinitions.First(kvp => kvp.Value.Name == q.queryId).Key));
                }

                var prometheusQueries = queries.Where(q => q.queryType == "PrometheusMetricsQuery").ToList();
                if (prometheusQueries.Count != 0)
                {
                    signalGroup.AzureMonitorWorkspace = new AzureMonitorWorkspaceSignalGroup
                    {
                        AuthenticationSetting = authenticationSetting.Value.Value.Name,
                        AzureMonitorWorkspaceResourceId = node.azureMonitorWorkspaceResourceId,
                        SignalAssignments = new SignalAssignment
                        {
                            SignalDefinitions = prometheusQueries.Select(q => q.queryId).ToArray()
                        }
                    };

                    dependsOn.AddRange(prometheusQueries.Select(q => signalDefinitions.First(kvp => kvp.Value.Name == q.queryId).Key));
                }

                entity.Properties.SignalGroup = signalGroup;
            }

            var symbolicName = $"entity{entities.Count}";
            entities.Add(symbolicName, entity);

            if (authenticationSetting != null)
            {
                dependsOn.Add(authenticationSetting.Value.Key);
            }

            bicepBuilder.AppendLine();
            if (isRoot)
            {
                bicepBuilder.AppendLine(entity.ToBicepString(symbolicName, overwriteNameParameter: resourceNameParameterName, parent: modelSymbolicName, dependsOn: dependsOn));
            }
            else
            {
                bicepBuilder.AppendLine(entity.ToBicepString(symbolicName, parent: modelSymbolicName, dependsOn: dependsOn));
            }
        }

        foreach (var node in v1HealthModel.properties.nodes)
        {
            var nodeName = node.nodeId == "0" ? v2HealthModel.Name : node.nodeId;
            var childNodeIds = node.childNodeIds;
            if (childNodeIds == null || childNodeIds.Length == 0)
                continue;
            foreach (var childNodeId in childNodeIds)
            {
                var relationship = new Relationship
                {
                    Name = $"{nodeName}-{childNodeId}".GenerateDeterministicGuid().ToString(),
                    Properties = new RelationshipProperties
                    {
                        ParentEntityName = nodeName,
                        ChildEntityName = childNodeId
                    }
                };
                var symbolicName = $"relationship{relationships.Count}";

                relationships.Add(symbolicName, relationship);

                var parentEntitySymbolicName = entities.First(kvp => kvp.Value.Name == nodeName).Key;
                var childEntitySymbolicName = entities.First(kvp => kvp.Value.Name == childNodeId).Key;

                bicepBuilder.AppendLine();
                bicepBuilder.AppendLine(relationship.ToBicepString(symbolicName, parent: modelSymbolicName,
                    dependsOn: [parentEntitySymbolicName, childEntitySymbolicName]));
            }
        }

        return bicepBuilder.ToString();
    }

    /// <summary>
    /// Compiles the Bicep input to an ARM template JSON string.
    /// </summary>
    /// <param name="bicepInput"></param>
    /// <returns></returns>
    private static async Task<string?> CompileBicepToArmTemplate(string bicepInput)
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, bicepInput);

        var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<INamespaceProvider, DefaultNamespaceProvider>();
            services.AddSingleton<IResourceTypeProviderFactory, ResourceTypeProviderFactory>();
            services.AddSingleton<IContainerRegistryClientFactory, ContainerRegistryClientFactory>();
            services.AddSingleton<ITemplateSpecRepositoryFactory, TemplateSpecRepositoryFactory>();
            services.AddSingleton<IModuleDispatcher, ModuleDispatcher>();
            services.AddSingleton<IArtifactRegistryProvider, DefaultArtifactRegistryProvider>();
            services.AddSingleton<ITokenCredentialFactory, TokenCredentialFactory>();
            services.AddSingleton<IFileResolver, FileResolver>();
            services.AddSingleton<IEnvironment, Bicep.Core.Utils.Environment>();
            services
                .AddSingleton<Bicep.Core.Configuration.IConfigurationManager,
                    Bicep.Core.Configuration.ConfigurationManager>();
            services.AddSingleton<IBicepAnalyzer, LinterAnalyzer>();
            services.AddSingleton<IFeatureProviderFactory, FeatureProviderFactory>();
            services.AddSingleton<ILinterRulesProvider, LinterRulesProvider>();
            services.AddSingleton<BicepCompiler>();
        }).Build();

        var bicepCompiler = host.Services.GetRequiredService<BicepCompiler>();
        var compilation = bicepCompiler.CreateCompilationWithoutRestore(new Uri(tempFile, UriKind.Absolute));
        var armTemplate = compilation.Emitter.Template().Template;
        return armTemplate;
    }
}