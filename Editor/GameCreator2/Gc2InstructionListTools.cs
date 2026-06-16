// Custom UnityMCP tool: append Game Creator 2 Instructions to an InstructionList
// field (e.g. CarEntry.onEnter / onExit) over MCP.
//
// GC2 instruction lists are [SerializeReference] polymorphic collections, which the
// generic component_set tool cannot populate. This writes them the way the Inspector
// does: SerializedProperty.managedReferenceValue.
//
// Registered automatically by realvirtual's McpToolRegistry, which scans ALL non-system
// assemblies for [McpTool] static methods — no edit to the vendor package required.
// Lives in an Editor folder because it uses UnityEditor.SerializedObject.

using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using realvirtual.MCP;               // McpTool / McpParam attributes
using realvirtual.MCP.Tools;         // ToolHelpers
using realvirtual.MCP.Serialization; // McpTypeResolver
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Gc2Mcp
{
    public static class Gc2InstructionListTools
    {
        [McpTool("Append a Game Creator 2 Instruction to an InstructionList field on a component (e.g. CarEntry.onEnter / onExit). Optionally set simple scalar/enum fields of the new instruction from JSON (enums by integer index). Editor only. Saves the scene dirty flag; call editor_save_scene after.")]
        public static string Gc2AddInstruction(
            [McpParam("GameObject hierarchy path (e.g. 'Vesta_1')")] string name,
            [McpParam("Component type that owns the list (e.g. 'CarEntry')")] string componentType,
            [McpParam("Serialized field name of the InstructionList (e.g. 'onEnter' or 'onExit')")] string listField,
            [McpParam("Instruction class name to add (e.g. 'InstructionSetVehicleControlMode')")] string instructionType,
            [McpParam("Optional JSON of simple fields to set on the new instruction, e.g. {\"m_Mode\":1}. Enums use their integer index.")] string fields = null)
        {
#if UNITY_EDITOR
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var compType = McpTypeResolver.Resolve(componentType);
            if (compType == null)
                return ToolHelpers.Error($"Component type '{componentType}' not found");

            var comp = go.GetComponent(compType);
            if (comp == null)
                return ToolHelpers.Error($"Component '{componentType}' not found on '{name}'");

            var instrType = McpTypeResolver.Resolve(instructionType);
            if (instrType == null)
                return ToolHelpers.Error($"Instruction type '{instructionType}' not found");
            if (!IsInstruction(instrType))
                return ToolHelpers.Error($"'{instructionType}' is not a Game Creator 2 Instruction");

            var so = new SerializedObject(comp);
            var listProp = so.FindProperty(listField);
            if (listProp == null)
                return ToolHelpers.Error($"No serialized field '{listField}' on '{componentType}'");

            // Game Creator 2 InstructionList stores its items in 'm_Instructions'.
            var arr = listProp.FindPropertyRelative("m_Instructions");
            if (arr == null || !arr.isArray)
                return ToolHelpers.Error($"Field '{listField}' has no 'm_Instructions' array (not an InstructionList?)");

            object instance;
            try { instance = Activator.CreateInstance(instrType); }
            catch (Exception ex) { return ToolHelpers.Error($"Cannot instantiate '{instructionType}': {ex.Message}"); }

            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            var elem = arr.GetArrayElementAtIndex(idx);
            // Overwrite the freshly-inserted (possibly duplicated) reference with our instance.
            elem.managedReferenceValue = instance;

            var setSummary = new JArray();
            if (!string.IsNullOrEmpty(fields))
            {
                JObject f;
                try { f = JObject.Parse(fields); }
                catch (Exception ex) { return ToolHelpers.Error($"Invalid fields JSON: {ex.Message}"); }

                foreach (var kv in f)
                {
                    var p = elem.FindPropertyRelative(kv.Key);
                    if (p == null) { setSummary.Add($"{kv.Key}: NOT FOUND"); continue; }
                    setSummary.Add(TrySet(p, kv.Value) ? $"{kv.Key}: set" : $"{kv.Key}: unsupported ({p.propertyType})");
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            if (!Application.isPlaying && go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = go.name,
                ["component"] = componentType,
                ["list"] = listField,
                ["added"] = instrType.Name,
                ["index"] = idx,
                ["fields"] = setSummary,
                ["note"] = "Scene marked dirty - call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("gc2_add_instruction is editor-only");
#endif
        }

        [McpTool("List the Game Creator 2 Instructions in an InstructionList field on a component (e.g. CarEntry.onEnter). Read-only — use this to verify a list instead of the crash-prone component_get.")]
        public static string Gc2ListInstructions(
            [McpParam("GameObject hierarchy path (e.g. 'Vesta_1')")] string name,
            [McpParam("Component type that owns the list (e.g. 'CarEntry')")] string componentType,
            [McpParam("Serialized field name of the InstructionList (e.g. 'onEnter')")] string listField)
        {
#if UNITY_EDITOR
            var arr = ResolveList(name, componentType, listField, out var err, out _, out _);
            if (arr == null) return err;

            var items = new JArray();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                string typeName;
                try
                {
                    var val = e.managedReferenceValue;
                    typeName = val != null ? val.GetType().Name : "(null)";
                }
                catch (System.Exception ex)
                {
                    // Fall back to the serialized type id when the managed value can't be read.
                    typeName = !string.IsNullOrEmpty(e.managedReferenceFullTypename)
                        ? e.managedReferenceFullTypename.Split(' ', '.')[^1] + " (unreadable: " + ex.GetType().Name + ")"
                        : "(unreadable: " + ex.GetType().Name + ")";
                }
                items.Add(new JObject { ["index"] = i, ["type"] = typeName });
            }

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["component"] = componentType,
                ["list"] = listField,
                ["count"] = arr.arraySize,
                ["instructions"] = items
            });
#else
            return ToolHelpers.Error("gc2_list_instructions is editor-only");
#endif
        }

        [McpTool("Remove a Game Creator 2 Instruction from an InstructionList field by index, or clear the whole list with index = -1. Saves the scene dirty flag; call editor_save_scene after.")]
        public static string Gc2RemoveInstruction(
            [McpParam("GameObject hierarchy path (e.g. 'Vesta_1')")] string name,
            [McpParam("Component type that owns the list (e.g. 'CarEntry')")] string componentType,
            [McpParam("Serialized field name of the InstructionList (e.g. 'onEnter')")] string listField,
            [McpParam("Index to remove, or -1 to clear the entire list")] int index)
        {
#if UNITY_EDITOR
            var arr = ResolveList(name, componentType, listField, out var err, out var so, out var comp);
            if (arr == null) return err;

            int before = arr.arraySize;
            if (index < 0)
            {
                arr.ClearArray();
            }
            else
            {
                if (index >= arr.arraySize)
                    return ToolHelpers.Error($"Index {index} out of range (count {arr.arraySize})");
                arr.DeleteArrayElementAtIndex(index);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            var go = comp.gameObject;
            if (!Application.isPlaying && go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["component"] = componentType,
                ["list"] = listField,
                ["removed"] = index < 0 ? before : 1,
                ["count"] = arr.arraySize,
                ["note"] = "Scene marked dirty - call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("gc2_remove_instruction is editor-only");
#endif
        }

        [McpTool("Set a PropertyGetGameObject field on a Game Creator 2 instruction inside an InstructionList (e.g. InstructionEnterVehicle's Target/Character). value = 'self', 'player', or a GameObject hierarchy path (direct scene reference). Saves the scene dirty flag; call editor_save_scene after.")]
        public static string Gc2SetInstructionGameObject(
            [McpParam("GameObject hierarchy path that owns the component (e.g. 'Vesta_1')")] string name,
            [McpParam("Component type that owns the list (e.g. 'Trigger')")] string componentType,
            [McpParam("Serialized field name of the InstructionList (e.g. 'm_Instructions' owner field)")] string listField,
            [McpParam("Index of the instruction within the list")] int index,
            [McpParam("PropertyGetGameObject field on the instruction (e.g. 'm_Target' or 'm_Character')")] string field,
            [McpParam("'self', 'player', or a GameObject hierarchy path for a direct scene reference")] string value)
        {
#if UNITY_EDITOR
            var arr = ResolveList(name, componentType, listField, out var err, out var so, out var comp);
            if (arr == null) return err;
            if (index < 0 || index >= arr.arraySize)
                return ToolHelpers.Error($"Index {index} out of range (count {arr.arraySize})");

            var elem = arr.GetArrayElementAtIndex(index);
            var propField = elem.FindPropertyRelative(field);
            if (propField == null)
                return ToolHelpers.Error($"Instruction at [{index}] has no field '{field}'");

            // PropertyGetGameObject stores its polymorphic type in 'm_Property'.
            var inner = propField.FindPropertyRelative("m_Property");
            if (inner == null || inner.propertyType != SerializedPropertyType.ManagedReference)
                return ToolHelpers.Error($"Field '{field}' is not a PropertyGetGameObject (no managed 'm_Property')");

            object propType; string kind;
            var v = (value ?? "").Trim();
            if (string.Equals(v, "self", StringComparison.OrdinalIgnoreCase))
            {
                propType = CreateGameObjectProperty("GetGameObjectSelf", null);
                kind = "self";
            }
            else if (string.Equals(v, "player", StringComparison.OrdinalIgnoreCase))
            {
                propType = CreateGameObjectProperty("GetGameObjectPlayer", null);
                kind = "player";
            }
            else
            {
                var go = ToolHelpers.FindGameObject(v);
                if (go == null) return ToolHelpers.Error($"GameObject '{v}' not found");
                propType = CreateGameObjectProperty("GetGameObjectInstance", go);
                kind = $"instance:{go.name}";
            }

            if (propType == null)
                return ToolHelpers.Error("Could not create the GameObject property type (GC2 type not found)");

            inner.managedReferenceValue = propType;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            var owner = comp.gameObject;
            if (!Application.isPlaying && owner.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(owner.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["component"] = componentType,
                ["list"] = listField,
                ["index"] = index,
                ["field"] = field,
                ["set"] = kind,
                ["note"] = "Scene marked dirty - call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("gc2_set_instruction_gameobject is editor-only");
#endif
        }

        [McpTool("Set ANY [SerializeReference] (managed reference) field on a component to a new instance of a named type — e.g. a GC2 Trigger's 'm_Event' to an Event subclass (EventOnInteract, EventOnTriggerEnter3D, ...), or any polymorphic GC2 field. 'propertyPath' is a SerializedProperty path (e.g. 'm_Event'). Optionally set simple scalar/enum fields on the new instance via JSON. Use fully-qualified component types for ambiguous names (e.g. GameCreator.Runtime.VisualScripting.Trigger). Editor only; call editor_save_scene after.")]
        public static string Gc2SetManagedReference(
            [McpParam("GameObject hierarchy path (e.g. 'Vesta_1/Triggers_Enter_Exit')")] string name,
            [McpParam("Component type (fully-qualified if ambiguous, e.g. 'GameCreator.Runtime.VisualScripting.Trigger')")] string componentType,
            [McpParam("Serialized property path of the managed-reference field (e.g. 'm_Event')")] string propertyPath,
            [McpParam("Type name to instantiate into the field (e.g. 'EventOnInteract')")] string typeName,
            [McpParam("Optional JSON of simple fields to set on the new instance, e.g. {\"m_Foo\":1}")] string fields = null)
        {
#if UNITY_EDITOR
            var go = ToolHelpers.FindGameObject(name);
            if (go == null) return ToolHelpers.Error($"GameObject '{name}' not found");

            var compType = McpTypeResolver.Resolve(componentType);
            if (compType == null) return ToolHelpers.Error($"Component type '{componentType}' not found");

            var comp = go.GetComponent(compType);
            if (comp == null) return ToolHelpers.Error($"Component '{componentType}' not found on '{name}'");

            var newType = McpTypeResolver.Resolve(typeName);
            if (newType == null) return ToolHelpers.Error($"Type '{typeName}' not found");

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return ToolHelpers.Error($"No serialized property '{propertyPath}' on '{componentType}'");
            if (prop.propertyType != SerializedPropertyType.ManagedReference)
                return ToolHelpers.Error($"'{propertyPath}' is not a [SerializeReference] managed-reference field");

            object instance;
            try { instance = System.Activator.CreateInstance(newType); }
            catch (System.Exception ex) { return ToolHelpers.Error($"Cannot instantiate '{typeName}': {ex.Message}"); }

            prop.managedReferenceValue = instance;

            var setSummary = new JArray();
            if (!string.IsNullOrEmpty(fields))
            {
                JObject f;
                try { f = JObject.Parse(fields); }
                catch (System.Exception ex) { return ToolHelpers.Error($"Invalid fields JSON: {ex.Message}"); }
                foreach (var kv in f)
                {
                    var p = prop.FindPropertyRelative(kv.Key);
                    if (p == null) { setSummary.Add($"{kv.Key}: NOT FOUND"); continue; }
                    setSummary.Add(TrySet(p, kv.Value) ? $"{kv.Key}: set" : $"{kv.Key}: unsupported ({p.propertyType})");
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            if (!Application.isPlaying && go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["component"] = componentType,
                ["propertyPath"] = propertyPath,
                ["set"] = newType.Name,
                ["fields"] = setSummary,
                ["note"] = "Scene marked dirty - call editor_save_scene to persist."
            });
#else
            return ToolHelpers.Error("gc2_set_managed_reference is editor-only");
#endif
        }

#if UNITY_EDITOR
        //! Builds a GC2 PropertyTypeGetGameObject instance (GetGameObjectSelf/Player/Instance)
        //! by type name. For the Instance variant, sets its private 'm_GameObject' reference.
        private static object CreateGameObjectProperty(string typeName, GameObject go)
        {
            var t = McpTypeResolver.Resolve(typeName);
            if (t == null) return null;

            var instance = Activator.CreateInstance(t);
            if (go != null)
            {
                var f = t.GetField("m_GameObject",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                f?.SetValue(instance, go);
            }
            return instance;
        }

        //! Resolves the 'm_Instructions' array property of an InstructionList field.
        //! On failure returns null and fills 'error' with a ToolHelpers.Error JSON string.
        private static SerializedProperty ResolveList(
            string name, string componentType, string listField,
            out string error, out SerializedObject so, out Component comp)
        {
            error = null; so = null; comp = null;

            var go = ToolHelpers.FindGameObject(name);
            if (go == null) { error = ToolHelpers.Error($"GameObject '{name}' not found"); return null; }

            var compType = McpTypeResolver.Resolve(componentType);
            if (compType == null) { error = ToolHelpers.Error($"Component type '{componentType}' not found"); return null; }

            comp = go.GetComponent(compType);
            if (comp == null) { error = ToolHelpers.Error($"Component '{componentType}' not found on '{name}'"); return null; }

            so = new SerializedObject(comp);
            var listProp = so.FindProperty(listField);
            if (listProp == null) { error = ToolHelpers.Error($"No serialized field '{listField}' on '{componentType}'"); return null; }

            var arr = listProp.FindPropertyRelative("m_Instructions");
            if (arr == null || !arr.isArray) { error = ToolHelpers.Error($"Field '{listField}' has no 'm_Instructions' array (not an InstructionList?)"); return null; }

            return arr;
        }

        //! True if the type derives from GameCreator's Instruction base (matched by name,
        //! so this file needs no compile-time dependency on the GC2 assembly).
        private static bool IsInstruction(Type t)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (b.FullName == "GameCreator.Runtime.VisualScripting.Instruction")
                    return true;
            return false;
        }

        private static bool TrySet(SerializedProperty p, JToken v)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Enum:    p.enumValueIndex = v.Value<int>();    return true;
                case SerializedPropertyType.Integer: p.intValue = v.Value<int>();          return true;
                case SerializedPropertyType.Boolean: p.boolValue = v.Value<bool>();        return true;
                case SerializedPropertyType.Float:   p.floatValue = v.Value<float>();      return true;
                case SerializedPropertyType.String:  p.stringValue = v.Value<string>();    return true;
                default: return false;
            }
        }
#endif
    }
}
