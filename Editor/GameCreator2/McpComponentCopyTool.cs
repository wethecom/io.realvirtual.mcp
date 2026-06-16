// Custom UnityMCP tool: copy a whole component (all serialized values, including
// object references) from one GameObject to another, using Unity's native
// ComponentUtility.CopyComponent / PasteComponent. This is the reliable way to
// "make a copy of a vital component and add it" — and it never reads the component
// back as JSON, so it avoids the component_get crash on heavy GC2 components.
//
// Registered automatically by realvirtual's McpToolRegistry. Editor-only.

using realvirtual.MCP;               // McpTool / McpParam
using realvirtual.MCP.Tools;         // ToolHelpers
using realvirtual.MCP.Serialization; // McpTypeResolver
using Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
#endif

namespace Gc2Mcp
{
    public static class McpComponentCopyTool
    {
        [McpTool("Copy a component with ALL its serialized values (including object references) from one GameObject to another, using Unity's native copy/paste. If the target already has that component type, pastes values onto the existing one; otherwise adds a new copy. Object references that point at the SOURCE object's own children will still point there after the copy — re-point them afterwards. Saves the scene dirty flag; call editor_save_scene after.")]
        public static string ComponentCopy(
            [McpParam("Source GameObject path (e.g. '/Car' for a root named Car)")] string source,
            [McpParam("Component type to copy (e.g. 'VehicleLights')")] string componentType,
            [McpParam("Target GameObject path (e.g. 'Vesta_1')")] string target,
            [McpParam("If true (default) and the target already has the component, paste values onto it; if false, always add a new copy")] bool pasteValuesIfExists = true)
        {
#if UNITY_EDITOR
            var srcGo = ToolHelpers.FindGameObject(source);
            if (srcGo == null) return ToolHelpers.Error($"Source GameObject '{source}' not found");

            var type = McpTypeResolver.Resolve(componentType);
            if (type == null) return ToolHelpers.Error($"Component type '{componentType}' not found");

            var srcComp = srcGo.GetComponent(type);
            if (srcComp == null) return ToolHelpers.Error($"Source '{source}' has no '{componentType}'");

            var tgtGo = ToolHelpers.FindGameObject(target);
            if (tgtGo == null) return ToolHelpers.Error($"Target GameObject '{target}' not found");

            if (!ComponentUtility.CopyComponent(srcComp))
                return ToolHelpers.Error($"CopyComponent failed for '{componentType}'");

            var existing = tgtGo.GetComponent(type);
            bool ok;
            string mode;
            if (existing != null && pasteValuesIfExists)
            {
                ok = ComponentUtility.PasteComponentValues(existing);
                mode = "values (onto existing)";
            }
            else
            {
                ok = ComponentUtility.PasteComponentAsNew(tgtGo);
                mode = "new component";
            }

            if (!ok) return ToolHelpers.Error($"Paste failed for '{componentType}' onto '{target}'");

            EditorUtility.SetDirty(tgtGo);
            if (!Application.isPlaying && tgtGo.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(tgtGo.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["source"] = source,
                ["target"] = target,
                ["component"] = componentType,
                ["mode"] = mode,
                ["note"] = "Object refs may still point at the source's children - re-point as needed. Call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("component_copy is editor-only");
#endif
        }

        [McpTool("Copy a whole GameObject (with all children and components) into a target parent, preserving its LOCAL transform (so interior parts land in the same spot relative to the new car). Uses Object.Instantiate, so references WITHIN the copied subtree remap to the copies; references to objects OUTSIDE it (e.g. an instruction targeting the original car) are preserved and may need re-pointing. Saves the scene dirty flag; call editor_save_scene after.")]
        public static string GameObjectCopy(
            [McpParam("Source GameObject path (e.g. '/Car/CenterOfGravity')")] string source,
            [McpParam("Target parent GameObject path (e.g. 'Vesta_1')")] string targetParent,
            [McpParam("Optional new name for the copy (defaults to the source name)")] string newName = null)
        {
#if UNITY_EDITOR
            var src = ToolHelpers.FindGameObject(source);
            if (src == null) return ToolHelpers.Error($"Source GameObject '{source}' not found");

            var parent = ToolHelpers.FindGameObject(targetParent);
            if (parent == null) return ToolHelpers.Error($"Target parent '{targetParent}' not found");

            var copy = (GameObject)Object.Instantiate(src);
            copy.transform.SetParent(parent.transform, false);
            // Match the source's local transform under its own parent.
            copy.transform.localPosition = src.transform.localPosition;
            copy.transform.localRotation = src.transform.localRotation;
            copy.transform.localScale = src.transform.localScale;
            copy.name = string.IsNullOrEmpty(newName) ? src.name : newName;

            Undo.RegisterCreatedObjectUndo(copy, "Copy GameObject");
            EditorUtility.SetDirty(parent);
            if (!Application.isPlaying && parent.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(parent.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["source"] = source,
                ["parent"] = targetParent,
                ["name"] = copy.name,
                ["path"] = ToolHelpers.GetGameObjectPath(copy),
                ["note"] = "Refs outside the copied subtree may still point at the source car - re-point as needed. Call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("game_object_copy is editor-only");
#endif
        }
    }
}
