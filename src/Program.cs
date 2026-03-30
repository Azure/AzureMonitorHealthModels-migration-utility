using System.ComponentModel;
using Spectre.Console.Cli;

namespace Microsoft.CloudHealth.PreviewMigration;

public static class Program
{
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
        public required string OutputFolder { get; init; }

        [CommandOption("--armtemplate")]
        [DefaultValue(false)]
        public bool? CompileArmTemplate { get; init; }
    }
}