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
        public delegate void UseMagicLinkTokenHandler(ReducerEventContext ctx, string token);
        public event UseMagicLinkTokenHandler? OnUseMagicLinkToken;

        public void UseMagicLinkToken(string token)
        {
            conn.InternalCallReducer(new Reducer.UseMagicLinkToken(token), this.SetCallReducerFlags.UseMagicLinkTokenFlags);
        }

        public bool InvokeUseMagicLinkToken(ReducerEventContext ctx, Reducer.UseMagicLinkToken args)
        {
            if (OnUseMagicLinkToken == null) return false;
            OnUseMagicLinkToken(
                ctx,
                args.Token
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class UseMagicLinkToken : Reducer, IReducerArgs
        {
            [DataMember(Name = "token")]
            public string Token;

            public UseMagicLinkToken(string Token)
            {
                this.Token = Token;
            }

            public UseMagicLinkToken()
            {
                this.Token = "";
            }

            string IReducerArgs.ReducerName => "UseMagicLinkToken";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags UseMagicLinkTokenFlags;
        public void UseMagicLinkToken(CallReducerFlags flags) => UseMagicLinkTokenFlags = flags;
    }
}
