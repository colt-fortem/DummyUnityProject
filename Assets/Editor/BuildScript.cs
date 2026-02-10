using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DevToolbox.Editor
{
    public static class BuildScript
    {
        private static readonly string Eol = Environment.NewLine;
        private static string buildHistoryJsonPath;
        private static string repoUrl;

        [MenuItem("Build/Windows")]
        public static void Build()
        {
            // Gather values from args
            Dictionary<string, string> options = GetValidatedOptions();

            // Set version for this build
            PlayerSettings.bundleVersion = options["buildVersion"];

            // Set path for build data
            buildHistoryJsonPath = options["buildHistoryPath"];

            // Set prefix for release notes link
            repoUrl = options["repoUrl"];
            if (!repoUrl.EndsWith("/"))
            {
                repoUrl += "/";
            }

            // Apply build target
            var buildTarget = (BuildTarget) Enum.Parse(typeof(BuildTarget), options["buildTarget"]);

            // Custom build
            Build(buildTarget, options["customBuildPath"]);
        }

        private static Dictionary<string, string> GetValidatedOptions()
        {
            ParseCommandLineArguments(out Dictionary<string, string> validatedOptions);

            if (!validatedOptions.TryGetValue("projectPath", out string _))
            {
                Console.WriteLine("Missing argument -projectPath");
                EditorApplication.Exit(110);
            }

            if (!validatedOptions.TryGetValue("buildTarget", out string buildTarget))
            {
                Console.WriteLine("Missing argument -buildTarget");
                EditorApplication.Exit(120);
            }

            if (!Enum.IsDefined(typeof(BuildTarget), buildTarget ?? string.Empty))
            {
                Console.WriteLine($"{buildTarget} is not a defined {nameof(BuildTarget)}");
                EditorApplication.Exit(121);
            }

            if (!validatedOptions.TryGetValue("customBuildPath", out string _))
            {
                Console.WriteLine("Missing argument -customBuildPath");
                //string path = Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'));
                //validatedOptions.Add("customBuildPath", path + "/build/MyGame.exe");
                //Console.WriteLine("customBuildPath: " + validatedOptions["customBuildPath"]);
                EditorApplication.Exit(130);
            }

            if (!validatedOptions.TryGetValue("repoUrl", out string _))
            {
                Console.WriteLine("Missing argument -repoUrl");
                EditorApplication.Exit(1);
            }

            const string defaultCustomBuildName = "TestBuild";
            if (!validatedOptions.TryGetValue("customBuildName", out string customBuildName))
            {
                Console.WriteLine($"Missing argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }
            else if (customBuildName == "")
            {
                Console.WriteLine($"Invalid argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }

            if (!validatedOptions.TryGetValue("buildHistoryPath", out string _))
            {
                Console.WriteLine($"Missing argument -buildHistoryPath, defaulting to 'BuildHistory/data.json'.");
                validatedOptions.Add("buildHistoryPath", "BuildHistory/data.json");
            }

            return validatedOptions;
        }

        private static void ParseCommandLineArguments(out Dictionary<string, string> providedArguments)
        {
            providedArguments = new Dictionary<string, string>();
            string[] args = Environment.GetCommandLineArgs();

            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#    Parsing settings     #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}"
            );

            // Extract flags with optional values
            for (int current = 0, next = 1; current < args.Length; current++, next++)
            {
                // Parse flag
                bool isFlag = args[current].StartsWith("-");
                if (!isFlag) continue;
                string flag = args[current].TrimStart('-');

                // Parse optional value
                bool flagHasValue = next < args.Length && !args[next].StartsWith("-");
                string value = flagHasValue ? args[next].TrimStart('-') : "";
                string displayValue = "\"" + value + "\"";

                // Assign
                Console.WriteLine($"Found flag \"{flag}\" with value {displayValue}.");
                providedArguments.Add(flag, value);
            }
        }

        private static void Build(BuildTarget buildTarget, string filePath)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                target = buildTarget,
                locationPathName = filePath,
                //options = BuildOptions.CleanBuildCache,
            };

            var version = PlayerSettings.bundleVersion;
            if (!version.StartsWith('v'))
            {
                version = "v" + version;
            }

            var buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary buildSummary = buildReport.summary;
            PrintBuildSummary(buildSummary);
            SaveBuildSummary(buildReport, version);
            ExitWithResult(buildSummary.result);
        }

        private static void PrintBuildSummary(BuildSummary summary)
        {
            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#      Build results      #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}" +
                $"Duration: {summary.totalTime}{Eol}" +
                $"Warnings: {summary.totalWarnings}{Eol}" +
                $"Errors: {summary.totalErrors}{Eol}" +
                $"Size: {summary.totalSize} bytes{Eol}" +
                $"{Eol}"
            );
        }

        private static void SaveBuildSummary(BuildReport buildReport, string version)
        {
            var summary = buildReport.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                return;
            }

            var assetTypeSizes = GetAssetTypeSizes(buildReport);
            List<BuildData> dataList = LoadJsonData();
            var entry = new BuildData
            {
                Version = version,
                Timestamp = summary.buildEndedAt.ToString(),
                BuildSize = summary.totalSize,
                BuildTime = (int)Math.Round(summary.totalTime.TotalSeconds),
                ReleaseNotes = repoUrl + version,
                AssetTypeSizes = assetTypeSizes.ToList(),
            };

            dataList.Add(entry);
            string jsonContent = JsonConvert.SerializeObject(dataList, Formatting.Indented);
            File.WriteAllText(buildHistoryJsonPath, jsonContent);
        }

        private static List<BuildData> LoadJsonData()
        {
            var dataList = new List<BuildData>();

            if (File.Exists(buildHistoryJsonPath))
            {
                string jsonContent = File.ReadAllText(buildHistoryJsonPath);
                dataList = JsonConvert.DeserializeObject<List<BuildData>>(jsonContent);
            }

            return dataList;
        }

        private static IEnumerable<AssetTypeSize> GetAssetTypeSizes(BuildReport buildReport)
        {
            //Console.WriteLine($"OUTPUT PATH: {buildReport.summary.outputPath}");
            var dataPath = buildReport.summary.outputPath.Replace(".exe", "_Data");
            var levelsSize = Directory
                .EnumerateFiles(dataPath, "level*", SearchOption.TopDirectoryOnly)
                .Sum(x => new FileInfo(x).Length);

            var assetTypes = new Dictionary<string, int>()
            {
                { "Textures", 0 },
                { "Meshes", 0 },
                { "Animations", 0 },
                { "Sounds", 0 },
                { "Shaders", 0 },
                { "Other Assets", 0 },
                { "Scripts", 0 },
                { "File headers", 0 },
                { "Levels", (int)levelsSize },
            };

            foreach (var packedAsset in buildReport.packedAssets)
            {
                assetTypes["File headers"] += (int)packedAsset.overhead;

                Console.WriteLine($"PACKED ASSET: {packedAsset.shortPath} - {packedAsset.overhead}");
                foreach (var entry in packedAsset.contents)
                {
                    var type = entry.type.Name;
                    type = type switch
                    {
                        "Texture2D" or "Cubemap" => "Textures",
                        "Mesh" => "Meshes",
                        "AudioClip" => "Sounds",
                        "Shader" => "Shaders",
                        "MonoScript" => "Scripts",
                        "AnimationClip" => "Animations",
                        _ => "Other Assets",
                    };

                    assetTypes.TryAdd(type, 0);
                    //if (entry.sourceAssetPath.StartsWith("Packages"))
                    //{
                    //    var endIndex = entry.sourceAssetPath.IndexOf('/', 9);
                    //    var package = entry.sourceAssetPath.Substring(9, endIndex - 9);
                    //    assetTypes.TryAdd(package, 0);
                    //    assetTypes[package] += (int)entry.packedSize;
                    //}
                    //else if (entry.sourceAssetPath.StartsWith("Assets"))
                    //{
                    //    assetTypes.TryAdd("MyAssets", 0);
                    //    assetTypes["MyAssets"] += (int)entry.packedSize;
                    //}
                    var sizeProp = entry.packedSize;
                    assetTypes[type] += (int)sizeProp;
                }
            }

            var result = assetTypes.Select(x => new AssetTypeSize { Name = x.Key, Size = x.Value });

            return result;
        }

        private static void ExitWithResult(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Succeeded:
                    Console.WriteLine("Build succeeded!");
                    EditorApplication.Exit(0);
                    break;
                case BuildResult.Failed:
                    Console.WriteLine("Build failed!");
                    EditorApplication.Exit(101);
                    break;
                case BuildResult.Cancelled:
                    Console.WriteLine("Build cancelled!");
                    EditorApplication.Exit(102);
                    break;
                case BuildResult.Unknown:
                default:
                    Console.WriteLine("Build result is unknown!");
                    EditorApplication.Exit(103);
                    break;
            }
        }

        [Serializable]
        private sealed class BuildData
        {
            public string Version;
            public string Timestamp;
            public int BuildTime;
            public ulong BuildSize;
            public string ReleaseNotes;
            public List<AssetTypeSize> AssetTypeSizes;
        }

        [Serializable]
        private sealed class AssetTypeSize
        {
            public string Name;
            public int Size;
        }
    }
}
