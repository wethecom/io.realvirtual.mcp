using System;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Variables;
using GameCreator.Runtime.Dialogue;
using UnityEngine;

// A Game Creator 2 variable Value type that stores a Dialogue 'Actor' ScriptableObject.
// GC2 ships a Value* type for every module asset (ValueQuest, ValueItem, ValueShooterWeapon...)
// so those assets can live inside Name/List Variables — but the Dialogue package never shipped
// one for Actor. This adds it, so you can drop Actors into a Global/Local Name Variables
// collection and look them up BY NAME at runtime (see ActorBook).
//
// Placed in its own namespace (not GameCreator.Runtime.*) so the 'Actor' type and the
// 'GameCreator.Runtime.Dialogue' namespace don't collide by simple name.

namespace Game.DialogueActors
{
    [Title("Actor")]
    [Category("Dialogue/Actor")]
    [Description("A Game Creator Dialogue Actor (speaker) asset stored in a variable")]

    [Serializable]
    public class ValueActor : TValue
    {
        public static readonly IdString TYPE_ID = new IdString("actor");

        // EXPOSED MEMBERS: -----------------------------------------------------------------------

        [SerializeField] private Actor m_Value;

        // PROPERTIES: ----------------------------------------------------------------------------

        public override IdString TypeID => TYPE_ID;
        public override Type Type => typeof(Actor);

        public override bool CanSave => false;

        public override TValue Copy => new ValueActor
        {
            m_Value = this.m_Value
        };

        // CONSTRUCTORS: --------------------------------------------------------------------------

        public ValueActor() : base()
        { }

        public ValueActor(Actor value) : this()
        {
            this.m_Value = value;
        }

        // OVERRIDE METHODS: ----------------------------------------------------------------------

        protected override object Get()
        {
            return this.m_Value;
        }

        protected override void Set(object value)
        {
            this.m_Value = value is Actor cast ? cast : null;
        }

        public override string ToString()
        {
            return this.m_Value != null ? this.m_Value.name : "(none)";
        }

        // REGISTRATION METHODS: ------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInit() => RegisterValueType(
            TYPE_ID,
            new TypeData(typeof(ValueActor), CreateValue),
            typeof(Actor)
        );

        #if UNITY_EDITOR

        [UnityEditor.InitializeOnLoadMethod]
        private static void EditorInit() => RegisterValueType(
            TYPE_ID,
            new TypeData(typeof(ValueActor), CreateValue),
            typeof(Actor)
        );

        #endif

        private static ValueActor CreateValue(object value)
        {
            return new ValueActor(value as Actor);
        }
    }
}
