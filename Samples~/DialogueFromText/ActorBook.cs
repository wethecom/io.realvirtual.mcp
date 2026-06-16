using GameCreator.Runtime.Variables;
using GameCreator.Runtime.Dialogue;
using UnityEngine;

// The "call an actor by name" function the rest of the system uses.
//
// Actors are stored as Name Variables: each variable's NAME is the speaker's key
// (e.g. "Bob", "Thief") and its VALUE is a ValueActor holding the Actor asset. Point
// this at either a Global Name Variables asset (a project-wide cast) or a Local Name
// Variables component (a per-scene / per-object cast) and ask for an actor by name.
//
// Usage:
//   Actor bob = ActorBook.Get(myGlobalCast, "Bob");
//   Actor cop = ActorBook.Get(npc.GetComponent<LocalNameVariables>(), "Officer");

namespace Game.DialogueActors
{
    public static class ActorBook
    {
        //! Resolve an Actor by variable name from a Global Name Variables asset.
        public static Actor Get(GlobalNameVariables book, string name)
        {
            if (book == null || string.IsNullOrEmpty(name)) return null;
            return book.Exists(name) ? book.Get(name) as Actor : null;
        }

        //! Resolve an Actor by variable name from a Local Name Variables component.
        public static Actor Get(LocalNameVariables book, string name)
        {
            if (book == null || string.IsNullOrEmpty(name)) return null;
            return book.Exists(name) ? book.Get(name) as Actor : null;
        }

        //! True if the named actor exists in the given Global cast.
        public static bool Has(GlobalNameVariables book, string name)
        {
            return book != null && !string.IsNullOrEmpty(name) && book.Exists(name);
        }

        //! True if the named actor exists in the given Local cast.
        public static bool Has(LocalNameVariables book, string name)
        {
            return book != null && !string.IsNullOrEmpty(name) && book.Exists(name);
        }
    }
}
