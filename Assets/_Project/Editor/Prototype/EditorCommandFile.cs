using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // A way to drive this editor from outside when the Unity MCP bridge cannot run
    // commands (17 September 2026: every RunCommand died with "No logs available"
    // through two editor restarts): a text file dropped in Temp is read, deleted
    // and run on the editor's main thread; the outcome goes to a reply file.
    //
    //   Temp/editor-command.txt        one line: build-guest | matrix <job> | stop | refresh | run <Type.Method>
    //   Temp/editor-command.reply.txt  "ok <command>" or "error <command>: <message>"
    //
    // Editor only, polled twice a second, the same entry points the menu items and
    // the bridge call (HQPrototypeBuild, CameraClearanceMatrixDriver). Nothing here
    // runs in a build.
    [InitializeOnLoad]
    public static class EditorCommandFile
    {
        private const string CommandPath = "Temp/editor-command.txt";
        private const string ReplyPath = "Temp/editor-command.reply.txt";
        private static double nextPoll;
        private static string lastResult;

        static EditorCommandFile()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CommandPath)) return;
            string command;
            try { command = File.ReadAllText(CommandPath).Trim(); File.Delete(CommandPath); }
            catch (IOException) { return; } // still being written; next poll
            if (command.Length == 0) return;
            try
            {
                Run(command);
                File.WriteAllText(ReplyPath, "ok " + command + (lastResult != null ? ": " + lastResult : string.Empty) + "\n");
                lastResult = null;
            }
            catch (Exception e)
            {
                File.WriteAllText(ReplyPath, "error " + command + ": " + e.Message + "\n");
                Debug.LogError("[EditorCommandFile] " + command + ": " + e.Message);
            }
        }

        private static void Run(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "build-guest":
                    if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
                    HQPrototypeBuild.BuildWindowsLocalDevelopment();
                    break;
                case "matrix":
                    if (parts.Length < 2) throw new ArgumentException("matrix <job>");
                    CameraClearanceMatrixDriver.Start(parts[1].Trim());
                    break;
                case "stop":
                    CameraClearanceMatrixDriver.StopCleanly();
                    break;
                case "refresh":
                    AssetDatabase.Refresh();
                    break;
                case "run":
                {
                    // A public static parameterless method by full name, e.g.
                    // run SunkCost.Editor.Prototype.PlayerVitalsSetup.Apply
                    if (parts.Length < 2) throw new ArgumentException("run <Namespace.Type.Method>");
                    string full = parts[1].Trim();
                    int dot = full.LastIndexOf('.');
                    if (dot <= 0) throw new ArgumentException("run <Namespace.Type.Method>");
                    string typeName = full.Substring(0, dot), methodName = full.Substring(dot + 1);
                    Type type = null;
                    foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = assembly.GetType(typeName);
                        if (type != null) break;
                    }
                    if (type == null) throw new ArgumentException("No type " + typeName);
                    System.Reflection.MethodInfo method = type.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (method == null) throw new ArgumentException("No public static " + methodName + "() on " + typeName);
                    object result;
                    try { result = method.Invoke(null, null); }
                    catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; } // the method's own error, not the reflection wrapper
                    lastResult = result?.ToString();
                    if (result != null) Debug.Log("[EditorCommandFile] " + full + ": " + result);
                    break;
                }
                default:
                    throw new ArgumentException("Unknown command '" + parts[0] + "' (build-guest | matrix <job> | stop | refresh | run <Type.Method>)");
            }
        }
    }
}
