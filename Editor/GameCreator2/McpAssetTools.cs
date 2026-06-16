// Custom UnityMCP tool: duplicate a project asset OR folder with fresh GUID(s).
// For folders, Unity's AssetDatabase.CopyAsset copies all contained assets and (in
// the Project-window "Duplicate" code path) remaps references BETWEEN the copied
// assets so the copies reference each other, not the originals. References to assets
// OUTSIDE the copied set are preserved (shared). This tool also runs a GUID-remap
// safety pass to guarantee internal references point at the copies even if CopyAsset
// didn't remap them.
//
// Registered automatically by realvirtual's McpToolRegistry. Editor-only.

using System.Collections.Generic;
using System.IO;
using realvirtual.MCP;
using realvirtual.MCP.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Gc2Mcp
{
    public static class McpAssetTools
    {
        [McpTool("Duplicate a project asset OR folder to a new path with fresh GUID(s) (like Project-window Duplicate). When copying a folder, all contained assets get new GUIDs and references BETWEEN the copied assets are remapped to the copies (a GUID-remap safety pass guarantees this); references to assets OUTSIDE the copied set are preserved (shared). Use for cloning a self-contained asset set (e.g. a self-contained asset set (folder)) so edits to the clone don't affect the original. Paths are Assets-relative or absolute. AssetDatabase is refreshed automatically.")]
        public static string AssetDuplicate(
            [McpParam("Source asset or folder (e.g. 'Assets/.../MyFolder')")] string sourcePath,
            [McpParam("Destination path that must not exist yet (e.g. 'Assets/.../MyFolder_Copy')")] string newPath)
        {
#if UNITY_EDITOR
            sourcePath = ToAssetsRelative(sourcePath);
            newPath = ToAssetsRelative(newPath);

            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sourcePath)))
                return ToolHelpers.Error($"Source asset/folder not found: '{sourcePath}'");
            if (AssetDatabase.LoadMainAssetAtPath(newPath) != null || AssetDatabase.IsValidFolder(newPath))
                return ToolHelpers.Error($"Destination already exists: '{newPath}'");

            bool isFolder = AssetDatabase.IsValidFolder(sourcePath);

            // Map every source asset's GUID -> its path relative to the source root,
            // so we can match it to the corresponding copy afterward.
            var srcGuidToRel = new Dictionary<string, string>();
            if (isFolder)
            {
                foreach (var g in AssetDatabase.FindAssets("", new[] { sourcePath }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (AssetDatabase.IsValidFolder(p)) continue;
                    srcGuidToRel[g] = p.Substring(sourcePath.Length);
                }
            }

            if (!AssetDatabase.CopyAsset(sourcePath, newPath))
                return ToolHelpers.Error($"AssetDatabase.CopyAsset failed ('{sourcePath}' -> '{newPath}')");

            AssetDatabase.Refresh();

            int remapped = 0;
            if (isFolder)
            {
                // Build oldGUID -> newGUID by matching relative paths.
                var oldToNew = new Dictionary<string, string>();
                foreach (var kv in srcGuidToRel)
                {
                    var copyPath = newPath + kv.Value;
                    var newGuid = AssetDatabase.AssetPathToGUID(copyPath);
                    if (!string.IsNullOrEmpty(newGuid) && newGuid != kv.Key)
                        oldToNew[kv.Key] = newGuid;
                }

                // Safety pass: rewrite any internal references in the copied (text) assets.
                if (oldToNew.Count > 0)
                {
                    foreach (var g in AssetDatabase.FindAssets("", new[] { newPath }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g);
                        if (AssetDatabase.IsValidFolder(p)) continue;
                        var full = Path.GetFullPath(p);
                        string text;
                        try { text = File.ReadAllText(full); }
                        catch { continue; }
                        if (!text.StartsWith("%YAML")) continue; // text assets only

                        bool changed = false;
                        foreach (var map in oldToNew)
                        {
                            if (text.Contains(map.Key)) { text = text.Replace(map.Key, map.Value); changed = true; }
                        }
                        if (changed) { File.WriteAllText(full, text); remapped++; }
                    }

                    if (remapped > 0) AssetDatabase.Refresh();
                }
            }

            return ToolHelpers.Ok(new JObject
            {
                ["source"] = sourcePath,
                ["newPath"] = newPath,
                ["isFolder"] = isFolder,
                ["newGuid"] = AssetDatabase.AssetPathToGUID(newPath),
                ["filesRemapped"] = remapped,
                ["note"] = isFolder
                    ? "Folder cloned with fresh GUIDs; internal references repointed to the copies."
                    : "Asset cloned with a fresh GUID."
            });
#else
            return ToolHelpers.Error("asset_duplicate is editor-only");
#endif
        }

        [McpTool("Remove 'missing script' MonoBehaviour slots from prefabs (and optionally the open scene) project-wide - e.g. after deleting a package like FishNet whose components were on many prefabs. Scans prefabs under the given folder (default whole project), removes missing-script components, and saves only the ones that changed. Returns counts. This can be slow on large projects; scope with 'folder' to run in batches.")]
        public static string CleanupMissingScripts(
            [McpParam("Folder to scan (Assets-relative), e.g. 'Assets/MyContent'. Omit for the whole project.")] string folder = null,
            [McpParam("Also clean the currently open scene's objects (default true)")] bool includeOpenScene = true)
        {
#if UNITY_EDITOR
            var searchFolder = string.IsNullOrEmpty(folder) ? "Assets" : ToAssetsRelative(folder);
            if (!AssetDatabase.IsValidFolder(searchFolder))
                return ToolHelpers.Error($"Folder not found: '{searchFolder}'");

            int prefabsScanned = 0, prefabsChanged = 0, removedInPrefabs = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchFolder });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                prefabsScanned++;
                GameObject root;
                try { root = PrefabUtility.LoadPrefabContents(path); }
                catch { continue; }
                if (root == null) continue;

                int removed = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsChanged++;
                    removedInPrefabs += removed;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            int removedInScene = 0;
            if (includeOpenScene && !Application.isPlaying)
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.IsValid() && scene.isLoaded)
                {
                    foreach (var go in scene.GetRootGameObjects())
                        foreach (var t in go.GetComponentsInChildren<Transform>(true))
                            removedInScene += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (removedInScene > 0) EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            AssetDatabase.SaveAssets();

            return ToolHelpers.Ok(new JObject
            {
                ["folder"] = searchFolder,
                ["prefabsScanned"] = prefabsScanned,
                ["prefabsChanged"] = prefabsChanged,
                ["missingRemovedInPrefabs"] = removedInPrefabs,
                ["missingRemovedInScene"] = removedInScene,
                ["note"] = removedInScene > 0 ? "Scene marked dirty - call editor_save_scene." : "Prefabs saved."
            });
#else
            return ToolHelpers.Error("cleanup_missing_scripts is editor-only");
#endif
        }

        [McpTool("Force-reserialize a project asset (re-runs OnBeforeSerialize, recomputing cached hashes/IDs such as GC2 IdPathString's m_ID) - use after editing an asset's YAML directly to fix any name->hash pointers. AssetDatabase is refreshed automatically.")]
        public static string AssetReserialize(
            [McpParam("Asset path to reserialize (Assets-relative or absolute)")] string path)
        {
#if UNITY_EDITOR
            path = ToAssetsRelative(path);
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                return ToolHelpers.Error($"Asset not found: '{path}'");

            AssetDatabase.ForceReserializeAssets(new[] { path });
            AssetDatabase.Refresh();

            return ToolHelpers.Ok(new JObject
            {
                ["path"] = path,
                ["note"] = "Reserialized - cached hashes/IDs recomputed from their source strings."
            });
#else
            return ToolHelpers.Error("asset_reserialize is editor-only");
#endif
        }

#if UNITY_EDITOR
        //! Normalizes an absolute or messy path to a forward-slash 'Assets/...'-relative path.
        private static string ToAssetsRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            int idx = path.IndexOf("/Assets/");
            if (idx >= 0) return path.Substring(idx + 1);
            if (path.StartsWith("Assets/") || path == "Assets") return path;
            return path; // assume already relative
        }
#endif
    }
}
