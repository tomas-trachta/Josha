using Josha.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Josha.Business
{
    internal static class NamespaceComponent
    {
        private static readonly byte[] NamespacesEntropy =
            Encoding.UTF8.GetBytes("Josha/namespaces/v1");

        private static readonly byte[] BindingsEntropy =
            Encoding.UTF8.GetBytes("Josha/bindings/v1");

        public static void CreateNamespace(string name, List<DirOD> childNodesTreeOne, List<DirOD> childNodesTreeTwo, bool defaultNamespace = false)
        {
            if (name == "default" && !defaultNamespace)
                throw new Exception("You cannot override default Namespace, please choose different name.");

            var dirPath = DirectoryAnalyserComponent.WinRoot + "josha_data";
            var filePath = Path.Combine(dirPath, "namespaces.dans");

            string textContents = PersistenceFile.LoadDecrypted(filePath, NamespacesEntropy, "Namespaces");

            var treeOnePaths = string.Join(',', childNodesTreeOne.Select(x => x.Path));
            var treeTwoPaths = string.Join(',', childNodesTreeTwo.Select(x => x.Path));

            var lines = textContents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            bool overwritten = false;

            for(int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(';', 2);
                if (parts.Length == 2 && parts[0] == name)
                {
                    lines[i] = $"{name};{treeOnePaths}|{treeTwoPaths}";

                    overwritten = true;
                    break;
                }
            }

            if(!overwritten)
            {
                textContents += $"{name};{treeOnePaths}|{treeTwoPaths}" + Environment.NewLine;
            }
            else
            {
                textContents = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            }

            if (!DirectoryAnalyserComponent.DirectoryExists(dirPath))
            {
                var created = DirectoryAnalyserComponent.CreateDirectory(dirPath);

                if (!created)
                    return;
            }

            PersistenceFile.SaveEncrypted(filePath, textContents, NamespacesEntropy, "Namespaces");
        }

        internal static List<DANamespace> LoadNamespaces()
        {
            var dirPath = DirectoryAnalyserComponent.WinRoot + "josha_data";
            var filePath = Path.Combine(dirPath, "namespaces.dans");
            var decryptedFileContents = PersistenceFile.LoadDecrypted(filePath, NamespacesEntropy, "Namespaces");
            if (string.IsNullOrEmpty(decryptedFileContents))
                return [];
            var lines = decryptedFileContents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            var namespaces = new List<DANamespace>();
            foreach(var line in lines)
            {
                var parts = line.Split(';', 2);
                if (parts.Length == 2)
                {
                    var name = parts[0];
                    var treesParts = parts[1].Split('|', 2);
                    if (treesParts.Length == 2)
                    {
                        var treeOnePaths = treesParts[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var treeTwoPaths = treesParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var treeOneNodes = treeOnePaths.Select(path => new DirOD(Path.GetFileName(path), path)).ToList();
                        var treeTwoNodes = treeTwoPaths.Select(path => new DirOD(Path.GetFileName(path), path)).ToList();
                        namespaces.Add(new DANamespace(name, treeOneNodes, treeTwoNodes));
                    }
                }
            }
            return namespaces;
        }

        public static void CreateBinding(int keyCode, string namespaceName)
        {
            var dirPath = DirectoryAnalyserComponent.WinRoot + "josha_data";
            var filePath = Path.Combine(dirPath, "bindings.dans");

            string textContents = PersistenceFile.LoadDecrypted(filePath, BindingsEntropy, "Bindings");

            var lines = textContents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            bool overwritten = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('|', 2);
                if (parts.Length == 2 && parts[0] == namespaceName)
                {
                    lines[i] = $"{namespaceName}|{keyCode}";

                    overwritten = true;
                    break;
                }
            }

            if (!overwritten)
            {
                textContents += $"{namespaceName}|{keyCode}" + Environment.NewLine;
            }
            else
            {
                textContents = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            }

            if (!DirectoryAnalyserComponent.DirectoryExists(dirPath))
            {
                var created = DirectoryAnalyserComponent.CreateDirectory(dirPath);

                if (!created)
                    return;
            }

            PersistenceFile.SaveEncrypted(filePath, textContents, BindingsEntropy, "Bindings");
        }

        public static List<DANamespaceBinding> LoadBindings()
        {
            var dirPath = DirectoryAnalyserComponent.WinRoot + "josha_data";
            var filePath = Path.Combine(dirPath, "bindings.dans");
            var decryptedFileContents = PersistenceFile.LoadDecrypted(filePath, BindingsEntropy, "Bindings");
            if (string.IsNullOrEmpty(decryptedFileContents))
                return [];
            var lines = decryptedFileContents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            var bindings = new List<DANamespaceBinding>();
            foreach (var line in lines)
            {
                var parts = line.Split('|', 2);
                if (parts.Length == 2)
                {
                    var namespaceName = parts[0];
                    if (int.TryParse(parts[1], out int keyCode))
                    {
                        bindings.Add(new DANamespaceBinding(namespaceName, keyCode));
                    }
                }
            }
            return bindings;
        }
    }
}
