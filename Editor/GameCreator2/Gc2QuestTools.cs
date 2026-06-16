// Custom UnityMCP tool: create a Game Creator 2 Quest asset (title, description,
// and a flat list of root tasks). Uses reflection so there's no compile-time
// dependency on the GC2 Quests assembly (matched by type name, like the other tools).
//
// Registered automatically by realvirtual's McpToolRegistry. Editor-only.

using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using realvirtual.MCP;
using realvirtual.MCP.Tools;
using realvirtual.MCP.Serialization;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gc2Mcp
{
    public static class Gc2QuestTools
    {
        [McpTool("Create a Game Creator 2 Quest asset (GameCreator.Runtime.Quests.Quest) with a title, description, and a flat list of root tasks (each task name becomes a Task). Saves the .asset. Reference it directly in a Journal 'Activate Quest' instruction, or add it to the Quests repository. Editor only.")]
        public static string Gc2CreateQuest(
            [McpParam("Asset path to create, e.g. 'Assets/Quests/MyQuest.asset'")] string assetPath,
            [McpParam("Quest title")] string title,
            [McpParam("Quest description")] string description = "",
            [McpParam("JSON array of task names, e.g. [\"Find the key\",\"Open the door\"]")] string tasks = null)
        {
#if UNITY_EDITOR
            assetPath = ToAssetsRelative(assetPath);
            if (!assetPath.EndsWith(".asset")) assetPath += ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                return ToolHelpers.Error($"Asset already exists: '{assetPath}'");

            var questType = McpTypeResolver.Resolve("GameCreator.Runtime.Quests.Quest");
            if (questType == null) return ToolHelpers.Error("GC2 Quest type not found (is the Quests module installed?)");
            var taskType = McpTypeResolver.Resolve("GameCreator.Runtime.Quests.Task");
            if (taskType == null) return ToolHelpers.Error("GC2 Task type not found");
            var pgsType = McpTypeResolver.Resolve("GameCreator.Runtime.Common.PropertyGetString");
            if (pgsType == null) return ToolHelpers.Error("PropertyGetString type not found");

            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy;

            var quest = ScriptableObject.CreateInstance(questType);
            quest.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            SetField(quest, questType, "m_Title", MakePgs(pgsType, title), F);
            SetField(quest, questType, "m_Description", MakePgs(pgsType, description ?? ""), F);

            int taskCount = 0;
            var treeProp = questType.GetProperty("Tasks", BindingFlags.Instance | BindingFlags.Public);
            var tree = treeProp?.GetValue(quest);
            if (tree != null && !string.IsNullOrEmpty(tasks))
            {
                JArray arr;
                try { arr = JArray.Parse(tasks); }
                catch (Exception ex) { return ToolHelpers.Error($"Invalid tasks JSON: {ex.Message}"); }

                var addToRoot = tree.GetType().GetMethod("AddToRoot", new[] { taskType });
                if (addToRoot == null) return ToolHelpers.Error("Could not find TasksTree.AddToRoot(Task)");

                foreach (var item in arr)
                {
                    var t = Activator.CreateInstance(taskType);
                    SetField(t, taskType, "m_Name", MakePgs(pgsType, item.ToString()), F);
                    addToRoot.Invoke(tree, new[] { t });
                    taskCount++;
                }
            }

            AssetDatabase.CreateAsset(quest, assetPath);
            EditorUtility.SetDirty(quest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return ToolHelpers.Ok(new JObject
            {
                ["path"] = assetPath,
                ["title"] = title,
                ["tasks"] = taskCount,
                ["guid"] = AssetDatabase.AssetPathToGUID(assetPath),
                ["note"] = "Quest created. Reference it in a Journal 'Activate Quest' instruction (or add it to the Quests repository under Project Settings)."
            });
#else
            return ToolHelpers.Error("gc2_create_quest is editor-only");
#endif
        }

#if UNITY_EDITOR
        //! Builds a GC2 PropertyGetString holding a constant value via its (string) constructor.
        private static object MakePgs(Type pgsType, string value)
        {
            var ctor = pgsType.GetConstructor(new[] { typeof(string) });
            return ctor != null ? ctor.Invoke(new object[] { value }) : Activator.CreateInstance(pgsType);
        }

        private static void SetField(object obj, Type type, string field, object value, BindingFlags f)
        {
            var fi = type.GetField(field, f);
            fi?.SetValue(obj, value);
        }

        private static string ToAssetsRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            int idx = path.IndexOf("/Assets/");
            if (idx >= 0) return path.Substring(idx + 1);
            return path;
        }
#endif
    }
}
