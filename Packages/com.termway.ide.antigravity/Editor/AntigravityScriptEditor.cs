using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;

namespace Antigravity.Editor
{
    [InitializeOnLoad]
    public class AntigravityScriptEditor : IExternalCodeEditor
    {
        private const string _antigravityArgument = "antigravity_arguments";
        private const string _antigravityExtension = "antigravity_userExtensions";

        private static readonly GUIContent _resetArguments = EditorGUIUtility.TrTextContent("Reset argument");
        private string _arguments;

        private IDiscovery _discoverability;
        private IGenerator _projectGeneration;

        private static readonly string[] _supportedFileNames = {
            "antigravityide.exe",
            "antigravityide.app",
            "antigravityide",
            "antigravity-ide.exe",
            "antigravity-ide.app",
            "antigravity-ide",
            "antigravity.exe",
            "antigravity.app",
            "antigravity",
            "agy.exe",
            "agy.app",
            "agy"
        };

        private static bool IsOSX => Application.platform == RuntimePlatform.OSXEditor;
        private static string DefaultApp => EditorPrefs.GetString("kScriptsDefaultApp");
        private static string DefaultArgument { get; } = "\"$(ProjectPath)\" -g \"$(File)\":$(Line):$(Column)";

        private string Arguments
        {
            get
            {
                return _arguments ?? (_arguments = EditorPrefs.GetString(_antigravityArgument, DefaultArgument));
            }
            set
            {
                _arguments = value;
                EditorPrefs.SetString(_antigravityArgument, value);
            }
        }

        private static string[] DefaultExtensions
        {
            get
            {
                var customExtensions = new[] { "json", "asmdef", "log" };
                return EditorSettings.projectGenerationBuiltinExtensions
                    .Concat(EditorSettings.projectGenerationUserExtensions)
                    .Concat(customExtensions)
                    .Distinct()
                    .ToArray();
            }
        }

        private static string[] HandledExtensions
        {
            get
            {
                return HandledExtensionsString
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.TrimStart('.', '*'))
                    .ToArray();
            }
        }

        private static string HandledExtensionsString
        {
            get
            {
                return EditorPrefs.GetString(_antigravityExtension, string.Join(";", DefaultExtensions));
            }
            set
            {
                EditorPrefs.SetString(_antigravityExtension, value);
            }
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            if (!IsAntigravityInstallation(editorPath))
            {
                installation = default;
                return false;
            }

            installation = new CodeEditor.Installation
            {
                Name = "Antigravity",
                Path = editorPath
            };

            return true;
        }

        public void OnGUI()
        {
            Arguments = EditorGUILayout.TextField("External Script Editor Args", Arguments);
            if (GUILayout.Button(_resetArguments, GUILayout.Width(120)))
            {
                Arguments = DefaultArgument;
            }

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            SettingsButton(ProjectGenerationFlag.Embedded, "Embedded packages", "");
            SettingsButton(ProjectGenerationFlag.Local, "Local packages", "");
            SettingsButton(ProjectGenerationFlag.Registry, "Registry packages", "");
            SettingsButton(ProjectGenerationFlag.Git, "Git packages", "");
            SettingsButton(ProjectGenerationFlag.BuiltIn, "Built-in packages", "");
            SettingsButton(ProjectGenerationFlag.Unknown, "Packages from unknown sources", "");
            RegenerateProjectFiles();
            EditorGUI.indentLevel--;
        }

        private void RegenerateProjectFiles()
        {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(new GUILayoutOption[] { }));
            rect.width = 252;
            if (GUI.Button(rect, "Regenerate project files"))
            {
                _projectGeneration.Sync();
            }
        }

        private void SettingsButton(ProjectGenerationFlag preference, string guiMessage, string toolTip)
        {
            var prevValue = _projectGeneration.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(preference);
            var newValue = EditorGUILayout.Toggle(new GUIContent(guiMessage, toolTip), prevValue);
            if (newValue != prevValue)
            {
                _projectGeneration.AssemblyNameProvider.ToggleProjectGeneration(preference);
            }
        }

        public void CreateIfDoesntExist()
        {
            if (!_projectGeneration.SolutionExists())
            {
                _projectGeneration.Sync();
            }
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            (_projectGeneration.AssemblyNameProvider as IPackageInfoCache)?.ResetPackageInfoCache();
            _projectGeneration.SyncIfNeeded(addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles).ToList(), importedFiles);
        }

        public void SyncAll()
        {
            (_projectGeneration.AssemblyNameProvider as IPackageInfoCache)?.ResetPackageInfoCache();
            AssetDatabase.Refresh();
            _projectGeneration.Sync();
        }

        public bool OpenProject(string path, int line, int column)
        {
            if (path != "" && (!SupportsExtension(path) || !File.Exists(path)))
            {
                return false;
            }

            if (line == -1)
            {
                line = 1;
            }

            if (column == -1)
            {
                column = 0;
            }

            string arguments;
            if (Arguments != DefaultArgument)
            {
                arguments = _projectGeneration.ProjectDirectory != path
                    ? CodeEditor.ParseArgument(Arguments, path, line, column)
                    : _projectGeneration.ProjectDirectory;
            }
            else
            {
                arguments = $@"""{_projectGeneration.ProjectDirectory}""";
                if (_projectGeneration.ProjectDirectory != path && path.Length != 0)
                {
                    arguments += $@" -g ""{path}"":{line}:{column}";
                }
            }

            if (IsOSX)
            {
                return OpenOSX(arguments);
            }

            var app = DefaultApp;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = app,
                    Arguments = arguments,
                    WindowStyle = app.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        private static bool OpenOSX(string arguments)
        {
            var app = DefaultApp;
            var internalCli = Path.Combine(app, "Contents/Resources/app/bin/antigravity-ide");
            if (File.Exists(internalCli))
            {
                var cliProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = internalCli,
                        Arguments = arguments,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    }
                };
                cliProcess.Start();
                return true;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-a \"{app}\" --args {arguments}",
                    UseShellExecute = true,
                }
            };
            process.Start();
            return true;
        }

        private static bool SupportsExtension(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            return HandledExtensions.Contains(extension.TrimStart('.'));
        }

        public CodeEditor.Installation[] Installations => _discoverability.PathCallback();

        public AntigravityScriptEditor(IDiscovery discovery, IGenerator projectGeneration)
        {
            _discoverability = discovery;
            _projectGeneration = projectGeneration;
        }

        static AntigravityScriptEditor()
        {
            var editor = new AntigravityScriptEditor(new AntigravityDiscovery(), new ProjectGeneration(Directory.GetParent(Application.dataPath).FullName));
            CodeEditor.Register(editor);

            if (IsAntigravityInstallation(CodeEditor.CurrentEditorInstallation))
            {
                editor.CreateIfDoesntExist();
            }
        }

        private static bool IsAntigravityInstallation(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var cleanPath = path.TrimEnd('/', '\\');
            var lowerCasePath = cleanPath.ToLower();
            var filename = Path.GetFileName(lowerCasePath).Replace(" ", "").Replace("-", "");
            return _supportedFileNames.Any(name => name.Replace("-", "") == filename) || lowerCasePath.Contains("antigravity");
        }

        public void Initialize(string editorInstallationPath)
        {
        }
    }
}