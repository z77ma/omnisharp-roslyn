using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Framework.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Models;
using OmniSharp.Options;
using OmniSharp.Services;

namespace OmniSharp.AspNet5
{
    public class AspNet5Paths
    {
        private readonly IOmnisharpEnvironment _env;
        private readonly OmniSharpOptions _options;
        private readonly ILogger _logger;
        public string Dnx { get; private set; }
        public string Dnu { get; private set; }
        public string Klr { get; private set; }
        public string Kpm { get; private set; }
        public string K   { get; private set; }

        public AspNet5Paths(IOmnisharpEnvironment env,
                            OmniSharpOptions options,
                            ILoggerFactory loggerFactory)
        {
            _env = env;
            _options = options;
            _logger = loggerFactory.Create<AspNet5Paths>();

            var runtimePath = GetRuntimePath().Value;
            Dnx = FirstPath(runtimePath, "dnx", "dnx.exe");
            Dnu = FirstPath(runtimePath, "dnu", "dnu.cmd");
            Klr = FirstPath(runtimePath, "klr", "klr.exe");
            Kpm = FirstPath(runtimePath, "kpm", "kpm.cmd");
            K   = FirstPath(runtimePath, "k", "k.cmd");
        }

        public AspNet5RuntimePathResult GetRuntimePath()
        {
            var root = ResolveRootDirectory(_env.Path);
            var globalJson = Path.Combine(root, "global.json");
            var versionOrAliasToken = GetRuntimeVersionOrAlias(globalJson);
            var versionOrAlias = versionOrAliasToken?.Value<string>() ?? _options.AspNet5.Alias ?? "default";
            var seachedLocations = new List<string>();

            foreach (var location in GetRuntimeLocations())
            {
                var paths = GetRuntimePathsFromVersionOrAlias(versionOrAlias, location);

                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    if (Directory.Exists(path))
                    {
                        _logger.WriteInformation(string.Format("Using runtime '{0}'.", path));
                        return new AspNet5RuntimePathResult()
                        {
                            Value = path
                        };
                    }

                    seachedLocations.Add(path);
                }
            }

            var message = new ErrorMessage()
            {
                Text = string.Format("The specified runtime path '{0}' does not exist. Searched locations {1}.\nVisit https://github.com/aspnet/Home/tree/glennc/readmelove for an installation guide.", versionOrAlias, string.Join("\n", seachedLocations))
            };
            if (versionOrAliasToken != null)
            {
                message.FileName = globalJson;
                message.Line = ((IJsonLineInfo)versionOrAliasToken).LineNumber;
                message.Column = ((IJsonLineInfo)versionOrAliasToken).LinePosition;
            }
            _logger.WriteError(message.Text);
            return new AspNet5RuntimePathResult()
            {
                Error = message
            };
        }

        private JToken GetRuntimeVersionOrAlias(string globalJson)
        {
            if (File.Exists(globalJson))
            {
                _logger.WriteInformation("Looking for sdk version in '{0}'.", globalJson);

                using (var stream = File.OpenRead(globalJson))
                {
                    var obj = JObject.Load(new JsonTextReader(new StreamReader(stream)));
                    return obj["sdk"]?["version"];
                }
            }

            return null;
        }

        private static string ResolveRootDirectory(string projectPath)
        {
            var di = new DirectoryInfo(projectPath);
            while (di.Parent != null)
            {
                if (di.EnumerateFiles("global.json").Any())
                {
                    return di.FullName;
                }

                di = di.Parent;
            }
            // If we don't find any files then make the project folder the root
            return projectPath;
        }

        private IEnumerable<string> GetRuntimeLocations()
        {
            yield return Environment.GetEnvironmentVariable("DNX_HOME");
            yield return Environment.GetEnvironmentVariable("KRE_HOME");

            var home = Environment.GetEnvironmentVariable("HOME") ??
                       Environment.GetEnvironmentVariable("USERPROFILE");

            // Newer path
            yield return Path.Combine(home, ".dnx");

            // New path

            yield return Path.Combine(home, ".k");

            // Old path
            yield return Path.Combine(home, ".kre");
        }

        private IEnumerable<string> GetRuntimePathsFromVersionOrAlias(string versionOrAlias, string runtimePath)
        {
            // Newer format
            yield return GetRuntimePathFromVersionOrAlias(versionOrAlias, runtimePath, ".dnx", "dnx-mono.{0}", "dnx-clr-win-x86.{0}", "runtimes");

            // New format

            yield return GetRuntimePathFromVersionOrAlias(versionOrAlias, runtimePath, ".k", "kre-mono.{0}", "kre-clr-win-x86.{0}", "runtimes");

            // Old format
            yield return GetRuntimePathFromVersionOrAlias(versionOrAlias, runtimePath, ".kre", "KRE-Mono.{0}", "KRE-CLR-x86.{0}", "packages");
        }

        private string GetRuntimePathFromVersionOrAlias(string versionOrAlias,
                                                        string runtimeHome,
                                                        string sdkFolder,
                                                        string monoFormat,
                                                        string windowsFormat,
                                                        string runtimeFolder)
        {
            if (string.IsNullOrEmpty(runtimeHome))
            {
                return null;
            }

            var aliasDirectory = Path.Combine(runtimeHome, "alias");

            var aliasFiles = new[] { "{0}.alias", "{0}.txt" };

            // Check alias first
            foreach (var shortAliasFile in aliasFiles)
            {
                var aliasFile = Path.Combine(aliasDirectory, string.Format(shortAliasFile, versionOrAlias));

                if (File.Exists(aliasFile))
                {
                    var fullName = File.ReadAllText(aliasFile).Trim();

                    return Path.Combine(runtimeHome, runtimeFolder, fullName);
                }
            }

            // There was no alias, look for the input as a version
            var version = versionOrAlias;

            if (PlatformHelper.IsMono)
            {
                return Path.Combine(runtimeHome, runtimeFolder, string.Format(monoFormat, versionOrAlias));
            }
            else
            {
                return Path.Combine(runtimeHome, runtimeFolder, string.Format(windowsFormat, versionOrAlias));
            }
        }

        private static string FirstPath(string runtimePath, params string[] candidates)
        {
            if (runtimePath == null)
            {
                return null;
            }
            return candidates
                .Select(candidate => Path.Combine(runtimePath, "bin", candidate))
                .FirstOrDefault(File.Exists);
        }
    }
}
