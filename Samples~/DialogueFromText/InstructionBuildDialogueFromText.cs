using System;
using System.Collections.Generic;
using System.Reflection;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.VisualScripting;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Variables;
using Game.DialogueActors;
using UnityEngine;
using UnityEngine.Serialization;
// 'Dialogue' (the type) and 'GameCreator.Runtime.Dialogue' (the namespace) collide by
// simple name from inside GameCreator.Runtime.*, so alias the component type explicitly.
using DialogueBehaviour = GameCreator.Runtime.Dialogue.Dialogue;

// A Game Creator 2 Instruction that turns a plain-text script into a playable,
// BRANCHING GC2 Dialogue at runtime — useful for feeding generated text (e.g. from
// an LLM or a variable) straight into a real dialogue.
//
// Script format (indent = 4 spaces or a tab per level):
//   plain line            -> a spoken line (chains to the previous line)
//   Name: line            -> a spoken line by actor "Name" (looked up in the Actor Book)
//   ? Prompt text         -> a Choice node; the prompt is shown, options follow
//       * Option A        -> a player choice (child of the Choice)
//           Name: reply   -> that branch's content (chains under the option)
//       * Option B
//           reply line
//
// Speakers: a "Cast" is just a Name Variables list whose entries map a speaker name to
// an Actor (see ValueActor / ActorBook). Point "Cast" at a GameObject that has such a
// list (usually Self). "Shared Cast" is an optional Global Name Variables asset used as a
// fallback for speakers not found in Cast. Any line prefixed "Name:" whose Name matches a
// cast entry gets that Actor as its speaker (Cast is checked first, then Shared Cast).
// Lines with no recognised prefix are narrator lines (no actor). If no cast is assigned,
// speaker prefixes are left as plain text and the dialogue still builds.
//
// It builds the tree on a target GameObject's Dialogue component (added if missing)
// and, by default, plays it.

namespace GameCreator.Runtime.VisualScripting
{
    [Title("Build Dialogue From Text")]
    [Description("Parses a plain-text script (lines, 'Name:' speakers, '? choices' and '* options' with indented branches) into a real GC2 Dialogue on the target GameObject, then optionally plays it.")]
    [Category("Dialogue/Build Dialogue From Text")]
    [Parameter("Dialogue Object", "Who plays this dialogue. Leave as Self (this object).")]
    [Parameter("Script", "The dialogue text, or a String variable holding it. A line like 'Thief: hello' makes 'Thief' the speaker; '? ' is a choice prompt, '* ' an option, and indentation makes branches.")]
    [Parameter("Cast", "A GameObject whose Name Variables map speaker names to Actors (usually Self). This is what turns 'Thief:' into an actor. Leave empty if you don't name speakers.")]
    [Parameter("Shared Cast", "Optional. A shared Global Name Variables asset used as a fallback cast for speakers not found in Cast. Most setups leave this empty.")]
    [Parameter("Skin", "How the dialogue looks. Leave empty to auto-pick a skin.")]
    [Parameter("Play", "Start the dialogue immediately after building it.")]
    [Keywords("Dialogue", "Conversation", "Text", "Branch", "Choice", "Actor", "Speaker", "Generate")]
    [Serializable]
    public class InstructionBuildDialogueFromText : Instruction
    {
        private const string DEFAULT_SCRIPT =
            "Thief: So you finally tracked me down.\n" +
            "Thief: I'd ask how you found me, but it hardly matters now.\n" +
            "? What do you do?\n" +
            "    * Demand the cache\n" +
            "        Thief: You really think threats work on me?\n" +
            "        Thief: The cache is long gone. Sold to the highest bidder.\n" +
            "    * Try to reason with him\n" +
            "        Thief: Reason? In this city? You're more naive than you look.\n" +
            "        Thief: But... maybe we can make a deal.\n" +
            "    * Attack\n" +
            "        Thief: Then you've made your last mistake.";

        [Tooltip("Who plays this dialogue. Leave as Self (this object).")]
        [SerializeField, FormerlySerializedAs("m_Target")]
        private PropertyGetGameObject m_DialogueObject = GetGameObjectSelf.Create();

        [Tooltip("The dialogue text — or set this to a String variable that holds it. A line like 'Thief: hello' makes 'Thief' the speaker.")]
        [SerializeField] private PropertyGetString m_Script = new PropertyGetString(DEFAULT_SCRIPT);

        [Tooltip("A GameObject whose Name Variables map speaker names to Actors (usually Self). This is what turns 'Thief:' into an actor. Leave empty if you don't name speakers.")]
        [SerializeField, FormerlySerializedAs("m_LocalActorBook")]
        private PropertyGetGameObject m_Cast = GetGameObjectNone.Create();

        [Tooltip("Optional. A shared Global Name Variables asset used as a fallback cast for speaker names not found in Cast above. Most setups leave this empty.")]
        [SerializeField, FormerlySerializedAs("m_ActorBook")]
        private GlobalNameVariables m_SharedCast;

        [Tooltip("How the dialogue looks. Leave empty to auto-pick a skin.")]
        [SerializeField] private DialogueSkin m_Skin;

        [Tooltip("Start the dialogue immediately after building it.")]
        [SerializeField] private bool m_Play = true;

        // Reflection handles for setting a node's speaker (Node.Actor is read-only).
        private static readonly FieldInfo F_ACTING =
            typeof(Node).GetField("m_Acting", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo F_ACTOR =
            typeof(Acting).GetField("m_Actor", BindingFlags.Instance | BindingFlags.NonPublic);

        public override string Title => "Build Dialogue From Text";

        protected override async System.Threading.Tasks.Task Run(Args args)
        {
            GameObject target = this.m_DialogueObject.Get(args);
            if (target == null) return;

            DialogueBehaviour dialogue = target.GetComponent<DialogueBehaviour>();
            if (dialogue == null) dialogue = target.AddComponent<DialogueBehaviour>();

            Content content = dialogue.Story?.Content;
            if (content == null) return;

            // Resolve the cast from a GameObject's LocalNameVariables.
            GameObject castGo = this.m_Cast.Get(args);
            LocalNameVariables localBook = castGo != null
                ? castGo.GetComponent<LocalNameVariables>()
                : null;

            // The script text comes from a string property — a constant, or (the point of
            // this) a String Name Variable the designer fills in.
            string scriptText = this.m_Script.Get(args) ?? string.Empty;

            // Clear any existing nodes so the build is fresh.
            foreach (int root in content.RootIds) content.Remove(root);

            BuildTree(content, scriptText, localBook, this.m_SharedCast);

            // A dialogue needs a Skin to render (it drives the UI). A runtime-built dialogue
            // has none, so ensure one: the assigned skin, else the last-used skin, else
            // (editor only) auto-find any skin in the project.
            if (content.DialogueSkin == null)
            {
                DialogueSkin skin = this.m_Skin != null ? this.m_Skin : Content.LAST_SKIN;
#if UNITY_EDITOR
                if (skin == null)
                {
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:DialogueSkin");
                    if (guids.Length > 0)
                        skin = UnityEditor.AssetDatabase.LoadAssetAtPath<DialogueSkin>(
                            UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                }
#endif
                content.DialogueSkin = skin;
            }

            if (this.m_Play) await dialogue.Play(args);
        }

        // Parses the indentation-based script into the Dialogue's node tree.
        private static void BuildTree(Content content, string script,
            LocalNameVariables localBook, GlobalNameVariables globalBook)
        {
            // chainTail[level] = last node id at that indent level (for chaining siblings).
            // choiceId[level]  = the Choice node whose options live at level+1.
            var chainTail = new Dictionary<int, int>();
            var choiceId = new Dictionary<int, int>();

            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                int level = IndentLevel(raw, out string body);
                if (body.Length == 0) continue;

                bool isChoice = body.StartsWith("? ");
                bool isOption = body.StartsWith("* ");
                string text = isChoice || isOption ? body.Substring(2).Trim() : body.Trim();

                // A "Name:" prefix that matches a variable in either book sets the speaker.
                Actor actor = SplitSpeaker(ref text, localBook, globalBook);

                // Dropping to a shallower line invalidates anything deeper.
                Clear(chainTail, level);
                Clear(choiceId, level);

                int parent;
                if (isOption)
                {
                    // An option is a direct child of the enclosing Choice (parallel branch).
                    parent = choiceId.TryGetValue(level - 1, out int cid) ? cid : Invalid(chainTail, level - 1);
                }
                else
                {
                    // A normal/choice line chains after its previous sibling, else descends.
                    parent = chainTail.TryGetValue(level, out int sib)
                        ? sib
                        : (level == 0 ? -1 : Invalid(chainTail, level - 1));
                }

                var node = new Node(text);
                if (isChoice) node.NodeType = new NodeTypeChoice();
                if (actor != null) SetActor(node, actor);

                int id = parent == -1 ? content.AddToRoot(node) : content.AddChild(node, parent);

                chainTail[level] = id;
                if (isChoice) choiceId[level] = id;
            }
        }

        // If 'text' starts with "Name:" and Name is a known actor in either cast (local
        // checked first, then global), strip the prefix from 'text' and return that Actor;
        // otherwise leave 'text' and return null.
        private static Actor SplitSpeaker(ref string text,
            LocalNameVariables localBook, GlobalNameVariables globalBook)
        {
            if (localBook == null && globalBook == null) return null;

            int colon = text.IndexOf(':');
            if (colon <= 0) return null;

            string name = text.Substring(0, colon).Trim();
            if (name.Length == 0) return null;

            Actor actor = Resolve(name, localBook, globalBook);
            if (actor == null) return null;

            text = text.Substring(colon + 1).Trim();
            return actor;
        }

        // Local cast overrides global cast for the same speaker name.
        private static Actor Resolve(string name, LocalNameVariables localBook, GlobalNameVariables globalBook)
        {
            if (ActorBook.Has(localBook, name)) return ActorBook.Get(localBook, name);
            if (ActorBook.Has(globalBook, name)) return ActorBook.Get(globalBook, name);
            return null;
        }

        private static void SetActor(Node node, Actor actor)
        {
            var acting = F_ACTING?.GetValue(node);
            if (acting != null) F_ACTOR?.SetValue(acting, actor);
        }

        private static int IndentLevel(string line, out string body)
        {
            int spaces = 0, i = 0;
            for (; i < line.Length; i++)
            {
                if (line[i] == ' ') spaces++;
                else if (line[i] == '\t') spaces += 4;
                else break;
            }
            body = line.Substring(i);
            return spaces / 4;
        }

        private static int Invalid(Dictionary<int, int> tail, int level)
        {
            return tail.TryGetValue(level, out int id) ? id : -1;
        }

        private static void Clear(Dictionary<int, int> map, int level)
        {
            var toRemove = new List<int>();
            foreach (var kv in map) if (kv.Key > level) toRemove.Add(kv.Key);
            foreach (int k in toRemove) map.Remove(k);
        }
    }
}
