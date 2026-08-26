using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using SR = System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Profiling;

namespace Antigravity.Editor
{
    public interface IGenerator
    {
        bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles);
        void Sync();
        string SolutionFile();
        string ProjectDirectory { get; }
        IAssemblyNameProvider AssemblyNameProvider { get; }
        void GenerateAll(bool generateAll);
        bool SolutionExists();
    }

    public class ProjectGeneration : IGenerator
    {
        private enum ScriptingLanguage
        {
            None,
            CSharp
        }

        public static readonly string MSBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

        private const string _windowsNewline = "\r\n";

        private const string _settingsJson = @"{
    ""files.exclude"":
    {
        ""**/.DS_Store"":true,
        ""**/.git"":true,
        ""**/.gitmodules"":true,
        ""**/*.booproj"":true,
        ""**/*.pidb"":true,
        ""**/*.suo"":true,
        ""**/*.user"":true,
        ""**/*.userprefs"":true,
        ""**/*.unityproj"":true,
        ""**/*.dll"":true,
        ""**/*.exe"":true,
        ""**/*.pdf"":true,
        ""**/*.mid"":true,
        ""**/*.midi"":true,
        ""**/*.wav"":true,
        ""**/*.gif"":true,
        ""**/*.ico"":true,
        ""**/*.jpg"":true,
        ""**/*.jpeg"":true,
        ""**/*.png"":true,
        ""**/*.psd"":true,
        ""**/*.tga"":true,
        ""**/*.tif"":true,
        ""**/*.tiff"":true,
        ""**/*.3ds"":true,
        ""**/*.3DS"":true,
        ""**/*.fbx"":true,
        ""**/*.FBX"":true,
        ""**/*.lxo"":true,
        ""**/*.LXO"":true,
        ""**/*.ma"":true,
        ""**/*.MA"":true,
        ""**/*.obj"":true,
        ""**/*.OBJ"":true,
        ""**/*.asset"":true,
        ""**/*.cubemap"":true,
        ""**/*.flare"":true,
        ""**/*.mat"":true,
        ""**/*.meta"":true,
        ""**/*.prefab"":true,
        ""**/*.unity"":true,
        ""build/"":true,
        ""Build/"":true,
        ""Library/"":true,
        ""library/"":true,
        ""obj/"":true,
        ""Obj/"":true,
        ""ProjectSettings/"":true,
        ""temp/"":true,
        ""Temp/"":true
    }
}";

        private static readonly Dictionary<string, ScriptingLanguage> _builtinSupportedExtensions = new Dictionary<string, ScriptingLanguage>
        {
            { "cs", ScriptingLanguage.CSharp },
            { "uxml", ScriptingLanguage.None },
            { "uss", ScriptingLanguage.None },
            { "shader", ScriptingLanguage.None },
            { "compute", ScriptingLanguage.None },
            { "cginc", ScriptingLanguage.None },
            { "hlsl", ScriptingLanguage.None },
            { "glslinc", ScriptingLanguage.None },
            { "template", ScriptingLanguage.None },
            { "raytrace", ScriptingLanguage.None }
        };

        private readonly string _solutionProjectEntryTemplate = string.Join("\r\n", @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""", @"EndProject").Replace("    ", "\t");

        private readonly string _solutionProjectConfigurationTemplate = string.Join("\r\n", @"        {{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU", @"        {{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU").Replace("    ", "\t");

        private static readonly string[] _reimportSyncExtensions = { ".dll", ".asmdef" };

        private string[] _projectSupportedExtensions = Array.Empty<string>();
        private const string _targetLanguageVersion = "latest";

        public string ProjectDirectory { get; }
        public IAssemblyNameProvider AssemblyNameProvider => _assemblyNameProvider;

        public void GenerateAll(bool generateAll)
        {
            _assemblyNameProvider.ToggleProjectGeneration(
                ProjectGenerationFlag.BuiltIn
                | ProjectGenerationFlag.Embedded
                | ProjectGenerationFlag.Git
                | ProjectGenerationFlag.Local
#if UNITY_2019_3_OR_NEWER
                | ProjectGenerationFlag.LocalTarBall
#endif
                | ProjectGenerationFlag.PlayerAssemblies
                | ProjectGenerationFlag.Registry
                | ProjectGenerationFlag.Unknown);
        }

        private readonly string _projectName;
        private readonly IAssemblyNameProvider _assemblyNameProvider;
        private readonly IFileIO _fileIOProvider;
        private readonly IGUIDGenerator _guidProvider;

        private const string _toolsVersion = "4.0";
        private const string _productVersion = "10.0.20506";
        private const string _baseDirectory = ".";
        private const string _targetFrameworkVersion = "v4.7.1";

        public ProjectGeneration(string tempDirectory)
            : this(tempDirectory, new AssemblyNameProvider(), new FileIOProvider(), new GUIDProvider())
        {
        }

        public ProjectGeneration(string tempDirectory, IAssemblyNameProvider assemblyNameProvider, IFileIO fileIO, IGUIDGenerator guidGenerator)
        {
            ProjectDirectory = tempDirectory.NormalizePath();
            _projectName = Path.GetFileName(ProjectDirectory);
            _assemblyNameProvider = assemblyNameProvider;
            _fileIOProvider = fileIO;
            _guidProvider = guidGenerator;
        }

        public bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles)
        {
            Profiler.BeginSample("SolutionSynchronizerSync");
            SetupProjectSupportedExtensions();

            if (!HasFilesBeenModified(affectedFiles, reimportedFiles))
            {
                Profiler.EndSample();
                return false;
            }

            var assemblies = _assemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution);
            var allProjectAssemblies = RelevantAssembliesForMode(assemblies).ToList();
            SyncSolution(allProjectAssemblies);

            var allAssetProjectParts = GenerateAllAssetProjectParts();

            var affectedNames = affectedFiles.Select(asset => _assemblyNameProvider.GetAssemblyNameFromScriptPath(asset)).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Split(new [] {".dll"}, StringSplitOptions.RemoveEmptyEntries)[0]);
            var reimportedNames = reimportedFiles.Select(asset => _assemblyNameProvider.GetAssemblyNameFromScriptPath(asset)).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Split(new [] {".dll"}, StringSplitOptions.RemoveEmptyEntries)[0]);
            var affectedAndReimported = new HashSet<string>(affectedNames.Concat(reimportedNames));

            foreach (var assembly in allProjectAssemblies)
            {
                if (!affectedAndReimported.Contains(assembly.name))
                {
                    continue;
                }

                SyncProject(assembly, allAssetProjectParts, ParseResponseFileData(assembly));
            }

            Profiler.EndSample();
            return true;
        }

        private bool HasFilesBeenModified(List<string> affectedFiles, string[] reimportedFiles)
        {
            return affectedFiles.Any(ShouldFileBePartOfSolution) || reimportedFiles.Any(ShouldSyncOnReimportedAsset);
        }

        private static bool ShouldSyncOnReimportedAsset(string asset)
        {
            return _reimportSyncExtensions.Contains(new FileInfo(asset).Extension);
        }

        private static IEnumerable<SR.MethodInfo> GetPostProcessorCallbacks(string name)
        {
            return TypeCache
                .GetTypesDerivedFrom<AssetPostprocessor>()
                .Select(t => t.GetMethod(name, SR.BindingFlags.Public | SR.BindingFlags.NonPublic | SR.BindingFlags.Static))
                .Where(m => m != null);
        }

        private static void OnGeneratedCSProjectFiles()
        {
            foreach (var method in GetPostProcessorCallbacks(nameof(OnGeneratedCSProjectFiles)))
            {
                method.Invoke(null, Array.Empty<object>());
            }
        }

        private static string InvokeAssetPostProcessorGenerationCallbacks(string name, string path, string content)
        {
            foreach (var method in GetPostProcessorCallbacks(name))
            {
                var args = new[] { path, content };
                var returnValue = method.Invoke(null, args);
                if (method.ReturnType == typeof(string))
                {
                    content = (string)returnValue;
                }
            }

            return content;
        }

        private static string OnGeneratedCSProject(string path, string content)
        {
            return InvokeAssetPostProcessorGenerationCallbacks(nameof(OnGeneratedCSProject), path, content);
        }

        private static string OnGeneratedSlnSolution(string path, string content)
        {
            return InvokeAssetPostProcessorGenerationCallbacks(nameof(OnGeneratedSlnSolution), path, content);
        }

        public void Sync()
        {
            SetupProjectSupportedExtensions();
            GenerateAndWriteSolutionAndProjects();

            OnGeneratedCSProjectFiles();
        }

        public bool SolutionExists()
        {
            return _fileIOProvider.Exists(SolutionFile());
        }

        private void SetupProjectSupportedExtensions()
        {
            _projectSupportedExtensions = _assemblyNameProvider.ProjectSupportedExtensions;
        }

        private bool ShouldFileBePartOfSolution(string file)
        {
            if (_assemblyNameProvider.IsInternalizedPackagePath(file))
            {
                return false;
            }

            return HasValidExtension(file);
        }

        private bool HasValidExtension(string file)
        {
            var extension = Path.GetExtension(file);

            if (extension == ".dll")
            {
                return true;
            }

            if (file.ToLower().EndsWith(".asmdef"))
            {
                return true;
            }

            return IsSupportedExtension(extension);
        }

        private bool IsSupportedExtension(string extension)
        {
            extension = extension.TrimStart('.');
            if (_builtinSupportedExtensions.ContainsKey(extension))
            {
                return true;
            }
            if (_projectSupportedExtensions.Contains(extension))
            {
                return true;
            }
            return false;
        }

        private static ScriptingLanguage ScriptingLanguageFor(Assembly assembly)
        {
            return ScriptingLanguageFor(GetExtensionOfSourceFiles(assembly.sourceFiles));
        }

        private static string GetExtensionOfSourceFiles(string[] files)
        {
            return files.Length > 0 ? GetExtensionOfSourceFile(files[0]) : "NA";
        }

        private static string GetExtensionOfSourceFile(string file)
        {
            var ext = Path.GetExtension(file).ToLower();
            ext = ext.Substring(1);
            return ext;
        }

        private static ScriptingLanguage ScriptingLanguageFor(string extension)
        {
            return _builtinSupportedExtensions.TryGetValue(extension.TrimStart('.'), out var result)
                ? result
                : ScriptingLanguage.None;
        }

        public void GenerateAndWriteSolutionAndProjects()
        {
            var assemblies = _assemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution).ToArray();

            var allAssetProjectParts = GenerateAllAssetProjectParts();

            SyncSolution(assemblies);
            var allProjectAssemblies = RelevantAssembliesForMode(assemblies).ToList();
            foreach (var assembly in allProjectAssemblies)
            {
                var responseFileData = ParseResponseFileData(assembly);
                SyncProject(assembly, allAssetProjectParts, responseFileData);
            }

            WriteVSCodeSettingsFiles();
        }

        private List<ResponseFileData> ParseResponseFileData(Assembly assembly)
        {
            var systemReferenceDirectories = CompilationPipeline.GetSystemAssemblyDirectories(assembly.compilerOptions.ApiCompatibilityLevel);

            var responseFilesData = assembly.compilerOptions.ResponseFiles.ToDictionary(x => x, x => _assemblyNameProvider.ParseResponseFile(
                x,
                ProjectDirectory,
                systemReferenceDirectories
            ));

            var responseFilesWithErrors = responseFilesData.Where(x => x.Value.Errors.Any())
                .ToDictionary(x => x.Key, x => x.Value);

            if (responseFilesWithErrors.Any())
            {
                foreach (var error in responseFilesWithErrors)
                {
                    foreach (var valueError in error.Value.Errors)
                    {
                        Debug.LogError($"{error.Key} Parse Error : {valueError}");
                    }
                }
            }

            return responseFilesData.Select(x => x.Value).ToList();
        }

        private Dictionary<string, string> GenerateAllAssetProjectParts()
        {
            var stringBuilders = new Dictionary<string, StringBuilder>();

            foreach (var asset in _assemblyNameProvider.GetAllAssetPaths())
            {
                if (_assemblyNameProvider.IsInternalizedPackagePath(asset))
                {
                    continue;
                }

                var extension = Path.GetExtension(asset);
                if (IsSupportedExtension(extension) && ScriptingLanguage.None == ScriptingLanguageFor(extension))
                {
                    var assemblyName = _assemblyNameProvider.GetAssemblyNameFromScriptPath(asset);

                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        continue;
                    }

                    assemblyName = Path.GetFileNameWithoutExtension(assemblyName);

                    if (!stringBuilders.TryGetValue(assemblyName, out var projectBuilder))
                    {
                        projectBuilder = new StringBuilder();
                        stringBuilders[assemblyName] = projectBuilder;
                    }

                    projectBuilder.Append("     <None Include=\"").Append(_fileIOProvider.EscapedRelativePathFor(asset, ProjectDirectory)).Append("\" />").Append(_windowsNewline);
                }
            }

            var result = new Dictionary<string, string>();

            foreach (var entry in stringBuilders)
            {
                result[entry.Key] = entry.Value.ToString();
            }

            return result;
        }

        private void SyncProject(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            List<ResponseFileData> responseFilesData)
        {
            SyncProjectFileIfNotChanged(ProjectFile(assembly), ProjectText(assembly, allAssetsProjectParts, responseFilesData));
        }

        private void SyncProjectFileIfNotChanged(string path, string newContents)
        {
            if (Path.GetExtension(path) == ".csproj")
            {
                newContents = OnGeneratedCSProject(path, newContents);
            }

            SyncFileIfNotChanged(path, newContents);
        }

        private void SyncSolutionFileIfNotChanged(string path, string newContents)
        {
            newContents = OnGeneratedSlnSolution(path, newContents);

            SyncFileIfNotChanged(path, newContents);
        }

        private void SyncFileIfNotChanged(string filename, string newContents)
        {
            try
            {
                if (_fileIOProvider.Exists(filename) && newContents == _fileIOProvider.ReadAllText(filename))
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            _fileIOProvider.WriteAllText(filename, newContents);
        }

        private string ProjectText(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            List<ResponseFileData> responseFilesData)
        {
            var projectBuilder = new StringBuilder();
            ProjectHeader(assembly, responseFilesData, projectBuilder);
            var references = new List<string>();

            foreach (var file in assembly.sourceFiles)
            {
                var fullFile = _fileIOProvider.EscapedRelativePathFor(file, ProjectDirectory);
                projectBuilder.Append("     <Compile Include=\"").Append(fullFile).Append("\" />").Append(_windowsNewline);
            }

            if (allAssetsProjectParts.TryGetValue(assembly.name, out var additionalAssetsForProject))
            {
                projectBuilder.Append(additionalAssetsForProject);
            }

            var responseRefs = responseFilesData.SelectMany(x => x.FullPathReferences.Select(r => r));
            var internalAssemblyReferences = assembly.assemblyReferences
              .Where(i => !i.sourceFiles.Any(ShouldFileBePartOfSolution)).Select(i => i.outputPath);
            var allReferences =
              assembly.compiledAssemblyReferences
                .Union(responseRefs)
                .Union(references)
                .Union(internalAssemblyReferences);

            foreach (var reference in allReferences)
            {
                var fullReference = Path.IsPathRooted(reference) ? reference : Path.Combine(ProjectDirectory, reference);
                AppendReference(fullReference, projectBuilder);
            }

            if (0 < assembly.assemblyReferences.Length)
            {
                projectBuilder.Append("  </ItemGroup>").Append(_windowsNewline);
                projectBuilder.Append("  <ItemGroup>").Append(_windowsNewline);
                foreach (var reference in assembly.assemblyReferences.Where(i => i.sourceFiles.Any(ShouldFileBePartOfSolution)))
                {
                    projectBuilder.Append("    <ProjectReference Include=\"").Append(reference.name).Append(GetProjectExtension()).Append("\">").Append(_windowsNewline);
                    projectBuilder.Append("      <Project>{").Append(ProjectGuid(reference.name)).Append("}</Project>").Append(_windowsNewline);
                    projectBuilder.Append("      <Name>").Append(reference.name).Append("</Name>").Append(_windowsNewline);
                    projectBuilder.Append("    </ProjectReference>").Append(_windowsNewline);
                }
            }

            projectBuilder.Append(ProjectFooter());
            return projectBuilder.ToString();
        }

        private static void AppendReference(string fullReference, StringBuilder projectBuilder)
        {
            var escapedFullPath = SecurityElement.Escape(fullReference);
            escapedFullPath = escapedFullPath.NormalizePath();
            projectBuilder.Append("    <Reference Include=\"").Append(Path.GetFileNameWithoutExtension(escapedFullPath)).Append("\">").Append(_windowsNewline);
            projectBuilder.Append("        <HintPath>").Append(escapedFullPath).Append("</HintPath>").Append(_windowsNewline);
            projectBuilder.Append("    </Reference>").Append(_windowsNewline);
        }

        public string ProjectFile(Assembly assembly)
        {
            var fileBuilder = new StringBuilder(assembly.name);
            fileBuilder.Append(".csproj");
            return Path.Combine(ProjectDirectory, fileBuilder.ToString());
        }

        public string SolutionFile()
        {
            return Path.Combine(ProjectDirectory, $"{_projectName}.sln");
        }

        private void ProjectHeader(
            Assembly assembly,
            List<ResponseFileData> responseFilesData,
            StringBuilder builder
        )
        {
            var otherArguments = GetOtherArgumentsFromResponseFilesData(responseFilesData);
            GetProjectHeaderTemplate(
                builder,
                ProjectGuid(assembly.name),
                assembly.name,
                string.Join(";", new[] { "DEBUG", "TRACE" }.Concat(assembly.defines).Concat(responseFilesData.SelectMany(x => x.Defines)).Concat(EditorUserBuildSettings.activeScriptCompilationDefines).Distinct().ToArray()),
                GenerateLangVersion(otherArguments["langversion"], assembly),
                assembly.compilerOptions.AllowUnsafeCode | responseFilesData.Any(x => x.Unsafe),
                GenerateAnalyserItemGroup(RetrieveRoslynAnalyzers(assembly, otherArguments)),
                GenerateRoslynAnalyzerRulesetPath(assembly, otherArguments)
            );
        }

        private static string GenerateLangVersion(IEnumerable<string> langVersionList, Assembly assembly)
        {
            var langVersion = langVersionList.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(langVersion))
            {
                return langVersion;
            }
#if UNITY_2020_2_OR_NEWER
            return assembly.compilerOptions.LanguageVersion;
#else
            return _targetLanguageVersion;
#endif
        }

        private static string GenerateRoslynAnalyzerRulesetPath(Assembly assembly, ILookup<string, string> otherResponseFilesData)
        {
#if UNITY_2020_2_OR_NEWER
            return GenerateAnalyserRuleSet(otherResponseFilesData["ruleset"].Append(assembly.compilerOptions.RoslynAnalyzerRulesetPath).Where(a => !string.IsNullOrEmpty(a)).Distinct().Select(x => MakeAbsolutePath(x).NormalizePath()).ToArray());
#else
            return GenerateAnalyserRuleSet(otherResponseFilesData["ruleset"].Distinct().Select(x => MakeAbsolutePath(x).NormalizePath()).ToArray());
#endif
        }

        private static string GenerateAnalyserRuleSet(string[] paths)
        {
            return paths.Length == 0
                ? string.Empty
                : $"{Environment.NewLine}{string.Join(Environment.NewLine, paths.Select(a => $"    <CodeAnalysisRuleSet>{a}</CodeAnalysisRuleSet>"))}";
        }

        private static string MakeAbsolutePath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
        }

        private static ILookup<string, string> GetOtherArgumentsFromResponseFilesData(List<ResponseFileData> responseFilesData)
        {
            var paths = responseFilesData.SelectMany(x =>
                {
                    return x.OtherArguments.Where(a => a.StartsWith("/") || a.StartsWith("-"))
                                           .Select(b =>
                    {
                        var index = b.IndexOf(":", StringComparison.Ordinal);
                        if (index > 0 && b.Length > index)
                        {
                            var key = b.Substring(1, index - 1);
                            return new KeyValuePair<string, string>(key, b.Substring(index + 1));
                        }

                        const string warnaserror = "warnaserror";
                        return b.Substring(1).StartsWith(warnaserror)
                            ? new KeyValuePair<string, string>(warnaserror, b.Substring(warnaserror.Length + 1))
                            : default;
                    });
                })
              .Distinct()
              .ToLookup(o => o.Key, pair => pair.Value);
            return paths;
        }

        private string[] RetrieveRoslynAnalyzers(Assembly assembly, ILookup<string, string> otherArguments)
        {
#if UNITY_2020_2_OR_NEWER
            return otherArguments["analyzer"].Concat(otherArguments["a"])
                .SelectMany(x => x.Split(';'))
#if !ROSLYN_ANALYZER_FIX
                .Concat(_assemblyNameProvider.GetRoslynAnalyzerPaths())
#else
                .Concat(assembly.compilerOptions.RoslynAnalyzerDllPaths)
#endif
                .Select(MakeAbsolutePath)
                .Distinct()
                .ToArray();
#else
            return otherArguments["analyzer"].Concat(otherArguments["a"])
                .SelectMany(x => x.Split(';'))
                .Distinct()
                .Select(MakeAbsolutePath)
                .ToArray();
#endif
        }

        private static string GenerateAnalyserItemGroup(string[] paths)
        {
            if (paths.Length == 0)
            {
                return string.Empty;
            }

            var analyserBuilder = new StringBuilder();
            analyserBuilder.Append("  <ItemGroup>").Append(_windowsNewline);
            foreach (var path in paths)
            {
                analyserBuilder.Append($"    <Analyzer Include=\"{path.NormalizePath()}\" />").Append(_windowsNewline);
            }

            analyserBuilder.Append("  </ItemGroup>").Append(_windowsNewline);
            return analyserBuilder.ToString();
        }

        private static string GetSolutionText()
        {
            return string.Join("\r\n", @"", @"Microsoft Visual Studio Solution File, Format Version {0}", @"# Visual Studio {1}", @"{2}", @"Global", @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution", @"        Debug|Any CPU = Debug|Any CPU", @"    EndGlobalSection", @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution", @"{3}", @"    EndGlobalSection", @"    GlobalSection(SolutionProperties) = preSolution", @"        HideSolutionNode = FALSE", @"    EndGlobalSection", @"EndGlobal", @"").Replace("    ", "\t");
        }

        private static string GetProjectFooterTemplate()
        {
            return string.Join("\r\n", @"  </ItemGroup>", @"  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />", @"  <Target Name=""BeforeBuild"">", @"  </Target>", @"  <Target Name=""AfterBuild"">", @"  </Target>", @"</Project>", @"");
        }

        private static void GetProjectHeaderTemplate(
            StringBuilder builder,
            string assemblyGUID,
            string assemblyName,
            string defines,
            string langVersion,
            bool allowUnsafe,
            string analyzerBlock,
            string rulesetBlock
        )
        {
            builder.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>").Append(_windowsNewline);
            builder.Append(@"<Project ToolsVersion=""").Append(_toolsVersion).Append(@""" DefaultTargets=""Build"" xmlns=""").Append(MSBuildNamespaceUri).Append(@""">").Append(_windowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(_windowsNewline);
            builder.Append(@"    <LangVersion>").Append(langVersion).Append("</LangVersion>").Append(_windowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(_windowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(_windowsNewline);
            builder.Append(@"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>").Append(_windowsNewline);
            builder.Append(@"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>").Append(_windowsNewline);
            builder.Append(@"    <ProductVersion>").Append(_productVersion).Append("</ProductVersion>").Append(_windowsNewline);
            builder.Append(@"    <SchemaVersion>2.0</SchemaVersion>").Append(_windowsNewline);
            builder.Append(@"    <RootNamespace>").Append(EditorSettings.projectGenerationRootNamespace).Append("</RootNamespace>").Append(_windowsNewline);
            builder.Append(@"    <ProjectGuid>{").Append(assemblyGUID).Append("}</ProjectGuid>").Append(_windowsNewline);
            builder.Append(@"    <OutputType>Library</OutputType>").Append(_windowsNewline);
            builder.Append(@"    <AppDesignerFolder>Properties</AppDesignerFolder>").Append(_windowsNewline);
            builder.Append(@"    <AssemblyName>").Append(assemblyName).Append("</AssemblyName>").Append(_windowsNewline);
            builder.Append(@"    <TargetFrameworkVersion>").Append(_targetFrameworkVersion).Append("</TargetFrameworkVersion>").Append(_windowsNewline);
            builder.Append(@"    <FileAlignment>512</FileAlignment>").Append(_windowsNewline);
            builder.Append(@"    <BaseDirectory>").Append(_baseDirectory).Append("</BaseDirectory>").Append(_windowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(_windowsNewline);
            builder.Append(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">").Append(_windowsNewline);
            builder.Append(@"    <DebugSymbols>true</DebugSymbols>").Append(_windowsNewline);
            builder.Append(@"    <DebugType>full</DebugType>").Append(_windowsNewline);
            builder.Append(@"    <Optimize>false</Optimize>").Append(_windowsNewline);
            builder.Append(@"    <OutputPath>Temp\bin\Debug\</OutputPath>").Append(_windowsNewline);
            builder.Append(@"    <DefineConstants>").Append(defines).Append("</DefineConstants>").Append(_windowsNewline);
            builder.Append(@"    <ErrorReport>prompt</ErrorReport>").Append(_windowsNewline);
            builder.Append(@"    <WarningLevel>4</WarningLevel>").Append(_windowsNewline);
            builder.Append(@"    <NoWarn>0169</NoWarn>").Append(_windowsNewline);
            builder.Append(@"    <AllowUnsafeBlocks>").Append(allowUnsafe).Append("</AllowUnsafeBlocks>").Append(_windowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(_windowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(_windowsNewline);
            builder.Append(@"    <NoConfig>true</NoConfig>").Append(_windowsNewline);
            builder.Append(@"    <NoStdLib>true</NoStdLib>").Append(_windowsNewline);
            builder.Append(@"    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>").Append(_windowsNewline);
            builder.Append(@"    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>").Append(_windowsNewline);
            builder.Append(@"    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>").Append(_windowsNewline);
            builder.Append(rulesetBlock);
            builder.Append(@"  </PropertyGroup>").Append(_windowsNewline);
            builder.Append(analyzerBlock);
            builder.Append(@"  <ItemGroup>").Append(_windowsNewline);
        }

        private void SyncSolution(IEnumerable<Assembly> assemblies)
        {
            SyncSolutionFileIfNotChanged(SolutionFile(), SolutionText(assemblies));
        }

        private string SolutionText(IEnumerable<Assembly> assemblies)
        {
            var fileversion = "11.00";
            var vsversion = "2010";

            var relevantAssemblies = RelevantAssembliesForMode(assemblies);
            var projectEntries = GetProjectEntries(relevantAssemblies);
            var projectConfigurations = string.Join(_windowsNewline, relevantAssemblies.Select(i => GetProjectActiveConfigurations(ProjectGuid(i.name))).ToArray());
            return string.Format(GetSolutionText(), fileversion, vsversion, projectEntries, projectConfigurations);
        }

        private static IEnumerable<Assembly> RelevantAssembliesForMode(IEnumerable<Assembly> assemblies)
        {
            return assemblies.Where(i => ScriptingLanguage.CSharp == ScriptingLanguageFor(i));
        }

        private string GetProjectEntries(IEnumerable<Assembly> assemblies)
        {
            var projectEntries = assemblies.Select(i => string.Format(
                _solutionProjectEntryTemplate,
                SolutionGuid(i),
                i.name,
                Path.GetFileName(ProjectFile(i)),
                ProjectGuid(i.name)
            ));

            return string.Join(_windowsNewline, projectEntries.ToArray());
        }

        private string GetProjectActiveConfigurations(string projectGuid)
        {
            return string.Format(
                _solutionProjectConfigurationTemplate,
                projectGuid);
        }

        private string ProjectGuid(string assembly)
        {
            return _guidProvider.ProjectGuid(_projectName, assembly);
        }

        private string SolutionGuid(Assembly assembly)
        {
            return _guidProvider.SolutionGuid(_projectName, GetExtensionOfSourceFiles(assembly.sourceFiles));
        }

        private static string ProjectFooter()
        {
            return GetProjectFooterTemplate();
        }

        private static string GetProjectExtension()
        {
            return ".csproj";
        }

        private void WriteVSCodeSettingsFiles()
        {
            var vsCodeDirectory = Path.Combine(ProjectDirectory, ".vscode");

            if (!_fileIOProvider.Exists(vsCodeDirectory))
            {
                _fileIOProvider.CreateDirectory(vsCodeDirectory);
            }

            var vsCodeSettingsJson = Path.Combine(vsCodeDirectory, "settings.json");

            if (!_fileIOProvider.Exists(vsCodeSettingsJson))
            {
                _fileIOProvider.WriteAllText(vsCodeSettingsJson, _settingsJson);
            }
        }
    }

    public static class SolutionGuidGenerator
    {
        private static readonly MD5 _md5 = MD5CryptoServiceProvider.Create();

        public static string GuidForProject(string projectName)
        {
            return ComputeGuidHashFor($"{projectName}salt");
        }

        public static string GuidForSolution(string projectName, string sourceFileExtension)
        {
            return "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
        }

        private static string ComputeGuidHashFor(string input)
        {
            var hash = _md5.ComputeHash(Encoding.Default.GetBytes(input));
            return new Guid(hash).ToString();
        }
    }
}
