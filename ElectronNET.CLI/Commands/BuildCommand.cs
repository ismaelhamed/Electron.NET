using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ElectronNET.CLI.Commands.Actions;

namespace ElectronNET.CLI.Commands
{
    public class BuildCommand : ICommand
    {
        public const string COMMAND_NAME = "build";
        public const string COMMAND_DESCRIPTION = "Build your Electron Application.";
        public static string COMMAND_ARGUMENTS = "Needed: '/target' with params 'win/osx/linux' to build for a typical app or use 'custom' and specify .NET Core build config & electron build config" + Environment.NewLine +
                                                 " for custom target, check .NET Core RID Catalog and Electron build target/" + Environment.NewLine +
                                                 " e.g. '/target win' or '/target custom \"win7-x86;win32\"'" + Environment.NewLine +
                                                 "Optional: '/dotnet-configuration' with the desired .NET Core build config e.g. release or debug. Default = Release" + Environment.NewLine + 
                                                 "Optional: '/electron-arch' to specify the resulting electron processor architecture (e.g. ia86 for x86 builds). Be aware to use the '/target custom' param as well!" + Environment.NewLine +
                                                 "Optional: '/electron-params' specify any other valid parameter, which will be routed to the electron-packager." + Environment.NewLine +
                                                 "Full example for a 32bit debug build with electron prune: build /target custom win7-x86;win32 /dotnet-configuration Debug /electron-arch ia32  /electron-params \"--prune=true \"";

        public static IList<CommandOption> CommandOptions { get; set; } = new List<CommandOption>();

        private string[] _args;

        public BuildCommand(string[] args)
        {
            _args = args;
        }

        private string _paramTarget = "target";
        private string _paramDotNetConfig = "dotnet-configuration";
        private string _paramElectronArch = "electron-arch";
        private string _paramElectronParams = "electron-params";

        public Task<bool> ExecuteAsync()
        {
            return Task.Run(() =>
            {
                Console.WriteLine("Build Electron Application...");

                var parser = new SimpleCommandLineParser();
                parser.Parse(_args);

                // Desired and specified platform
                var desiredPlatform = parser.Arguments[_paramTarget][0];
                var specifiedFromCustom = string.Empty;
                if (desiredPlatform == "custom" && parser.Arguments[_paramTarget].Length > 1)
                {
                    specifiedFromCustom = parser.Arguments[_paramTarget][1];
                }

                var platformInfo = GetTargetPlatformInformation.Do(desiredPlatform, specifiedFromCustom);

                // Desired configuration arg
                var configuration = "Release";
                if (parser.Arguments.ContainsKey(_paramDotNetConfig))
                {
                    configuration = parser.Arguments[_paramDotNetConfig][0];
                }

                // Set TempPath and execute dotnet build in this directory
                var tempPath = Path.Combine(Directory.GetCurrentDirectory(), "obj", "desktop", desiredPlatform);
                if (Directory.Exists(tempPath) == false)
                {
                    Directory.CreateDirectory(tempPath);
                }

                // TODO: Log.Debug
                Console.WriteLine("Executing dotnet publish in this directory: " + tempPath);

                Console.WriteLine($"Build ASP.NET Core App for {platformInfo.NetCorePublishRid} under {configuration}-Configuration...");
                var resultCode = ProcessHelper.CmdExecute($"dotnet publish -r {platformInfo.NetCorePublishRid} -c {configuration} --output \"{Path.Combine(tempPath, "bin")}\"", Directory.GetCurrentDirectory());
                if (resultCode != 0)
                {
                    Console.WriteLine("Error occurred during dotnet publish: " + resultCode);
                    return false;
                }

                // Embed electron files
                DeployEmbeddedElectronFiles.Do(tempPath);

                var checkForNodeModulesDirPath = Path.Combine(tempPath, "node_modules");

                if (Directory.Exists(checkForNodeModulesDirPath) == false)
                {
                    // TODO: Log.Debug
                    Console.WriteLine("node_modules missing in: " + checkForNodeModulesDirPath);

                    Console.WriteLine("Start npm install...");
                    ProcessHelper.CmdExecute("npm install", tempPath);

                    Console.WriteLine("Start npm install electron-packager...");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Works proper on Windows... 
                        ProcessHelper.CmdExecute("npm install electron-packager --global", tempPath);
                    }
                    else
                    {
                        // ToDo: find another solution or document it proper
                        // GH Issue https://github.com/electron-userland/electron-prebuilt/issues/48
                        Console.WriteLine("Electron Packager - make sure you invoke 'sudo npm install electron-packager --global' at " + tempPath + " manually. Sry.");
                    }
                }
                else
                {
                    // TODO: Log.Warning
                    Console.WriteLine("Skip npm install, because node_modules directory exists in: " + checkForNodeModulesDirPath);
                }

                Console.WriteLine("Build Electron Desktop Application...");

                // TODO: Need a solution for --asar support

                var electronArch = "x64";
                if (parser.Arguments.ContainsKey(_paramElectronArch))
                {
                    electronArch = parser.Arguments[_paramElectronArch][0];
                }

                var electronParams = "";
                if (parser.Arguments.ContainsKey(_paramElectronParams))
                {
                    electronParams = parser.Arguments[_paramElectronParams][0];
                }

                Console.WriteLine($"Packaging Electron App for Platform {platformInfo.ElectronPackerPlatform}...");
                ProcessHelper.CmdExecute($"electron-packager . --platform={platformInfo.ElectronPackerPlatform} --arch={electronArch} {electronParams} --out=\"{Path.Combine(Directory.GetCurrentDirectory(), "bin", "desktop")}\" --overwrite", tempPath);
                Console.WriteLine("Packaging done.");

                Console.WriteLine("");
                Console.WriteLine("Build succeeded. Congrats!");
                return true;
            });
        }
    }
}
