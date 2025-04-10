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
        public delegate void PruneOidcTokensHandler(ReducerEventContext ctx, ulong thresholdDate);
        public event PruneOidcTokensHandler? OnPruneOidcTokens;

        public void PruneOidcTokens(ulong thresholdDate)
        {
            conn.InternalCallReducer(new Reducer.PruneOidcTokens(thresholdDate), this.SetCallReducerFlags.PruneOidcTokensFlags);
        }

        public bool InvokePruneOidcTokens(ReducerEventContext ctx, Reducer.PruneOidcTokens args)
        {
            if (OnPruneOidcTokens == null) return false;
            OnPruneOidcTokens(
                ctx,
                args.ThresholdDate
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class PruneOidcTokens : Reducer, IReducerArgs
        {
            [DataMember(Name = "thresholdDate")]
            public ulong ThresholdDate;

            public PruneOidcTokens(ulong ThresholdDate)
            {
                this.ThresholdDate = ThresholdDate;
            }

            public PruneOidcTokens()
            {
            }

            string IReducerArgs.ReducerName => "PruneOidcTokens";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags PruneOidcTokensFlags;
        public void PruneOidcTokens(CallReducerFlags flags) => PruneOidcTokensFlags = flags;
    }
}
