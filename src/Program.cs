using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.CloudHealth.PreviewMigration.Models.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Microsoft.CloudHealth.PreviewMigration;

public class Program
{
    public static readonly string[] SupportedV2Locations = ["canadacentral", "uksouth"];

    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.AddBranch<CommandSettings>("convert", add =>
            {
                add.AddCommand<ConvertFromFileCommand>("file");
                add.AddCommand<ConvertFromAzureResourceCommand>("azure");
            });
        });

        return await app.RunAsync(args);
    }

    public class ConvertSettings : CommandSettings
    {
        [CommandOption("-o|--outputfolder <outputFolderPath>")]
        public string OutputFolder { get; init; }

        [CommandOption("--armtemplate")]
        [DefaultValue(false)]
        public bool? CompileArmTemplate { get; init; }
    }

    public class ConvertFromFileSettings : ConvertSettings
    {
        [CommandOption("-i|--inputfile <inputFilePath>")]
        public string InputFilePath { get; set; }
    }

    public class ConvertFromAzureResourceSettings : ConvertSettings
    {
        [CommandOption("-r|--resourceId <resourceId>")]
        public required string ResourceId { get; set; }
    }

    public class ConvertFromFileCommand : AsyncCommand<ConvertFromFileSettings>
    {
        public override ValidationResult Validate(CommandContext context, ConvertFromFileSettings settings)
        {
            if (!File.Exists(settings.InputFilePath))
            {
                return ValidationResult.Error($"File not found - {settings.InputFilePath}");
            }

            if (string.IsNullOrEmpty(settings.OutputFolder))
            {
                return ValidationResult.Error("Output file path is required");
            }

            if (File.Exists(settings.OutputFolder))
            {
                return ValidationResult.Error($"Output folder is a file. Please specify a folder instead - {settings.OutputFolder}");
            }

            return base.Validate(context, settings);
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConvertFromFileSettings settings)
        {
            var logger = CreateLogger();

            logger.LogInformation("Reading input file {inputFile}...", settings.InputFilePath);
            var v1Json = await File.ReadAllTextAsync(settings.InputFilePath);
            var v1HealthModel = JsonSerializer.Deserialize<HealthModel>(v1Json);

            if (v1HealthModel == null)
            {
                logger.LogError("Failed to deserialize v1 health model");
                return 1;
            }

            await BicepFileCreator.CompileAndWriteOutputFile(v1HealthModel, settings.OutputFolder, logger,
                settings.CompileArmTemplate ?? false);

            logger.LogInformation("Health model {healthModelName} converted and written to output folder {outputFolder}", v1HealthModel.name, settings.OutputFolder);

            return 0;
        }
    }

    public class ConvertFromAzureResourceCommand : AsyncCommand<ConvertFromAzureResourceSettings>
    {
        public override ValidationResult Validate(CommandContext context, ConvertFromAzureResourceSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ResourceId))
            {
                return ValidationResult.Error("Resource Id is required");
            }

            if (!ResourceIdentifier.TryParse(settings.ResourceId, out var resourceId))
            {
                return ValidationResult.Error("Invalid resource Id");
            }

            if (string.IsNullOrEmpty(settings.OutputFolder))
            {
                return ValidationResult.Error("Output file path is required");
            }

            if (File.Exists(settings.OutputFolder))
            {
                return ValidationResult.Error($"Output folder is a file. Please specify a folder instead - {settings.OutputFolder}");
            }

            return base.Validate(context, settings);
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConvertFromAzureResourceSettings settings)
        {
            var logger = CreateLogger();
            logger.LogInformation("Getting access token for ARM...");
            var tokenCredential = new DefaultAzureCredential(true);
            var token = (await tokenCredential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]))).Token;
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri("https://management.azure.com" + settings.ResourceId + "?api-version=2022-11-01-preview"),
                Method = HttpMethod.Get,
                Headers =
                {
                    Authorization = new AuthenticationHeaderValue("Bearer", token)
                }
            };
            logger.LogInformation("Getting resource {resourceId}...", settings.ResourceId);
            var responseMessage = await httpClient.SendAsync(request);
            if (!responseMessage.IsSuccessStatusCode)
            {
                logger.LogError("Failed to get resource {resourceId}", settings.ResourceId);
                logger.LogError(await responseMessage.Content.ReadAsStringAsync());
                return 1;
            }

            var v1HealthModel = await responseMessage.Content.ReadFromJsonAsync<HealthModel>();
            if (v1HealthModel == null)
            {
                logger.LogError("Failed to deserialize v1 health model");
                return 1;
            }

            logger.LogInformation("Health model {healthModelName} retrieved from ARM", v1HealthModel.name);

            await BicepFileCreator.CompileAndWriteOutputFile(v1HealthModel, settings.OutputFolder, logger,
                settings.CompileArmTemplate ?? false);

            logger.LogInformation("Health model {healthModelName} converted and written to output folder {outputFolder}", v1HealthModel.name, settings.OutputFolder);

            return 0;
        }
    }

    private static ILogger CreateLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Program", LogLevel.Debug)
                .AddSimpleConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.SingleLine = true;
                    options.UseUtcTimestamp = true;
                    options.ColorBehavior = LoggerColorBehavior.Disabled;
                    //options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffff ";
                });
        });
        ILogger logger = loggerFactory.CreateLogger("HealthModelConverter");
        return logger;
    }
}