// Custom UnityMCP tool: create a Game Creator 2 Dialogue on a GameObject.
// A GC2 Dialogue is a MonoBehaviour whose Story.Content is a TSerializableTree<Node>
// (the same tree type as the Quests task tree). This builds a sequential
// conversation by chaining text Nodes (parent -> child). Reflection-based, so no
// compile-time dependency on the GC2 Dialogue assembly.
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
using UnityEditor.SceneManagement;
#endif

namespace Gc2Mcp
{
    public static class Gc2DialogueTools
    {
        [McpTool("Create a Game Creator 2 Dialogue on a target GameObject: adds a Dialogue component (if missing) and builds a SEQUENTIAL conversation from a list of text lines — each line becomes a NodeTypeText node, chained parent->child in Story.Content. 'lines' is a JSON array of strings, or of objects {\"text\":\"...\",\"actor\":\"Assets/Path/Actor.asset\"} to set a speaker Actor per line. Editor only; call editor_save_scene after.")]
        public static string Gc2CreateDialogue(
            [McpParam("Target GameObject path that holds (or will get) the Dialogue component")] string name,
            [McpParam("JSON array of lines: [\"Hi there.\",\"How are you?\"] or [{\"text\":\"Hi\",\"actor\":\"Assets/Actors/Bob.asset\"}]")] string lines)
        {
#if UNITY_EDITOR
            var go = ToolHelpers.FindGameObject(name);
            if (go == null) return ToolHelpers.Error($"GameObject '{name}' not found");

            var dlgType = McpTypeResolver.Resolve("GameCreator.Runtime.Dialogue.Dialogue");
            if (dlgType == null) return ToolHelpers.Error("GC2 Dialogue type not found (is the Dialogue module installed?)");
            var nodeType = McpTypeResolver.Resolve("GameCreator.Runtime.Dialogue.Node");
            if (nodeType == null) return ToolHelpers.Error("GC2 Dialogue Node type not found");

            JArray arr;
            try { arr = JArray.Parse(lines); }
            catch (Exception ex) { return ToolHelpers.Error($"Invalid lines JSON: {ex.Message}"); }

            var comp = go.GetComponent(dlgType);
            if (comp == null) comp = Undo.AddComponent(go, dlgType);

            // Story.Content (the TSerializableTree<Node>)
            var story = dlgType.GetProperty("Story", BindingFlags.Instance | BindingFlags.Public)?.GetValue(comp);
            if (story == null) return ToolHelpers.Error("Dialogue.Story is null");
            var content = story.GetType().GetProperty("Content", BindingFlags.Instance | BindingFlags.Public)?.GetValue(story);
            if (content == null) return ToolHelpers.Error("Story.Content is null");

            var nodeCtor = nodeType.GetConstructor(new[] { typeof(string) });
            if (nodeCtor == null) return ToolHelpers.Error("Node(string) constructor not found");
            var addToRoot = content.GetType().GetMethod("AddToRoot", new[] { nodeType });
            var addChild = content.GetType().GetMethod("AddChild", new[] { nodeType, typeof(int) });
            if (addToRoot == null || addChild == null) return ToolHelpers.Error("Content tree AddToRoot/AddChild not found");

            var actingField = nodeType.GetField("m_Acting", BindingFlags.Instance | BindingFlags.NonPublic);

            int prevId = 0;
            int count = 0;
            var added = new JArray();
            foreach (var item in arr)
            {
                string text = item.Type == JTokenType.Object ? (item["text"]?.ToString() ?? "") : item.ToString();
                var node = nodeCtor.Invoke(new object[] { text });

                // optional speaker Actor
                if (item.Type == JTokenType.Object && item["actor"] != null && actingField != null)
                {
                    var actorPath = ToAssetsRelative(item["actor"].ToString());
                    var actorObj = AssetDatabase.LoadMainAssetAtPath(actorPath);
                    if (actorObj != null)
                    {
                        var acting = actingField.GetValue(node);
                        var actorF = acting?.GetType().GetField("m_Actor", BindingFlags.Instance | BindingFlags.NonPublic);
                        actorF?.SetValue(acting, actorObj);
                    }
                }

                prevId = count == 0
                    ? (int)addToRoot.Invoke(content, new[] { node })
                    : (int)addChild.Invoke(content, new object[] { node, prevId });
                added.Add(text);
                count++;
            }

            // GC2's tree carries an internal m_Dirty counter (workaround for Unity not
            // detecting in-place changes); bump it so the edit is flushed to disk.
            var dirty = GetFieldDeep(content.GetType(), "m_Dirty");
            dirty?.SetValue(content, (int)(dirty.GetValue(content) ?? 0) + 1);

            EditorUtility.SetDirty(comp);
            if (!Application.isPlaying && go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["lines"] = count,
                ["text"] = added,
                ["note"] = "Dialogue built (sequential text nodes). Play it with a 'Dialogue: Play' instruction. Call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("gc2_create_dialogue is editor-only");
#endif
        }

#if UNITY_EDITOR
        private static FieldInfo GetFieldDeep(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f;
            }
            return null;
        }

        private static string ToAssetsRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            int idx = path.IndexOf("/Assets/");
            return idx >= 0 ? path.Substring(idx + 1) : path;
        }
#endif
    }
}
