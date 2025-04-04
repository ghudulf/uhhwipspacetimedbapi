// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteReducers : RemoteBase
    {
        public delegate void DeactivateBusHandler(ReducerEventContext ctx, uint busId);
        public event DeactivateBusHandler? OnDeactivateBus;

        public void DeactivateBus(uint busId)
        {
            conn.InternalCallReducer(new Reducer.DeactivateBus(busId), this.SetCallReducerFlags.DeactivateBusFlags);
        }

        public bool InvokeDeactivateBus(ReducerEventContext ctx, Reducer.DeactivateBus args)
        {
            if (OnDeactivateBus == null) return false;
            OnDeactivateBus(
                ctx,
                args.BusId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class DeactivateBus : Reducer, IReducerArgs
        {
            [DataMember(Name = "busId")]
            public uint BusId;

            public DeactivateBus(uint BusId)
            {
                this.BusId = BusId;
            }

            public DeactivateBus()
            {
            }

            string IReducerArgs.ReducerName => "DeactivateBus";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags DeactivateBusFlags;
        public void DeactivateBus(CallReducerFlags flags) => DeactivateBusFlags = flags;
    }
}
