using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.CodeEditor;

namespace Antigravity.Editor
{
    public interface IDiscovery
    {
        CodeEditor.Installation[] PathCallback();
    }

    public class AntigravityDiscovery : IDiscovery
    {
        private List<CodeEditor.Installation> _installations;

        public CodeEditor.Installation[] PathCallback()
        {
            if (_installations == null)
            {
                _installations = new List<CodeEditor.Installation>();
                FindInstallationPaths();
            }

            return _installations.ToArray();
        }

        private void FindInstallationPaths()
        {
            string[] possiblePaths =
#if UNITY_EDITOR_OSX
            {
                "/Applications/Antigravity IDE.app",
                "/Applications/Antigravity.app",
                $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/Applications/Antigravity IDE.app",
                $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/Applications/Antigravity.app",
                "/usr/local/bin/antigravity-ide",
                "/usr/local/bin/antigravity",
                "/usr/local/bin/agy",
                "/opt/homebrew/bin/antigravity-ide",
                "/opt/homebrew/bin/antigravity",
                "/opt/homebrew/bin/agy"
            };
#elif UNITY_EDITOR_WIN
            {
                $"{GetLocalAppData()}/Programs/Antigravity IDE/Antigravity IDE.exe",
                $"{GetLocalAppData()}/Programs/Antigravity/Antigravity.exe",
                $"{GetProgramFiles()}/Antigravity IDE/Antigravity IDE.exe",
                $"{GetProgramFiles()}/Antigravity/Antigravity.exe"
            };
#else
            {
                "/usr/bin/antigravity-ide",
                "/usr/bin/antigravity",
                "/bin/antigravity-ide",
                "/bin/antigravity",
                "/usr/local/bin/antigravity-ide",
                "/usr/local/bin/antigravity",
                "/snap/bin/antigravity-ide",
                "/snap/bin/antigravity"
            };
#endif
            var existingPaths = possiblePaths.Where(AntigravityExists).Distinct().ToList();

            if (existingPaths.Count > 0)
            {
                _installations = existingPaths.Select(path => new CodeEditor.Installation
                {
                    Name = "Antigravity",
                    Path = path
                }).ToList();
            }
        }

#if UNITY_EDITOR_WIN
        private static string GetProgramFiles()
        {
            return Environment.GetEnvironmentVariable("ProgramFiles")?.Replace("\\", "/");
        }

        private static string GetLocalAppData()
        {
            return Environment.GetEnvironmentVariable("LOCALAPPDATA")?.Replace("\\", "/");
        }
#endif

        private static bool AntigravityExists(string path)
        {
#if UNITY_EDITOR_OSX
            return Directory.Exists(path) || File.Exists(path);
#else
            return File.Exists(path);
#endif
        }
    }
}