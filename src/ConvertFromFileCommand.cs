using System.Text.Json;
using Microsoft.CloudHealth.PreviewMigration.Models.V1;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Microsoft.CloudHealth.PreviewMigration;

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
        var logger = Utils.CreateLogger();

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

public class ConvertFromFileSettings : Program.ConvertSettings
{
    [CommandOption("-i|--inputfile <inputFilePath>")]
    public string InputFilePath { get; set; }
}