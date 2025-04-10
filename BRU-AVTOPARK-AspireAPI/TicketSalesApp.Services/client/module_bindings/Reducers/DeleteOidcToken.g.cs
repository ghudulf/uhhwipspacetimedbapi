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
        public delegate void DeleteOidcTokenHandler(ReducerEventContext ctx, uint internalId);
        public event DeleteOidcTokenHandler? OnDeleteOidcToken;

        public void DeleteOidcToken(uint internalId)
        {
            conn.InternalCallReducer(new Reducer.DeleteOidcToken(internalId), this.SetCallReducerFlags.DeleteOidcTokenFlags);
        }

        public bool InvokeDeleteOidcToken(ReducerEventContext ctx, Reducer.DeleteOidcToken args)
        {
            if (OnDeleteOidcToken == null) return false;
            OnDeleteOidcToken(
                ctx,
                args.InternalId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class DeleteOidcToken : Reducer, IReducerArgs
        {
            [DataMember(Name = "internalId")]
            public uint InternalId;

            public DeleteOidcToken(uint InternalId)
            {
                this.InternalId = InternalId;
            }

            public DeleteOidcToken()
            {
            }

            string IReducerArgs.ReducerName => "DeleteOidcToken";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags DeleteOidcTokenFlags;
        public void DeleteOidcToken(CallReducerFlags flags) => DeleteOidcTokenFlags = flags;
    }
}
