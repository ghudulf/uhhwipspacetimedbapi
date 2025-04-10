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
        public delegate void DeleteQrSessionHandler(ReducerEventContext ctx, string sessionId);
        public event DeleteQrSessionHandler? OnDeleteQrSession;

        public void DeleteQrSession(string sessionId)
        {
            conn.InternalCallReducer(new Reducer.DeleteQrSession(sessionId), this.SetCallReducerFlags.DeleteQrSessionFlags);
        }

        public bool InvokeDeleteQrSession(ReducerEventContext ctx, Reducer.DeleteQrSession args)
        {
            if (OnDeleteQrSession == null) return false;
            OnDeleteQrSession(
                ctx,
                args.SessionId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class DeleteQrSession : Reducer, IReducerArgs
        {
            [DataMember(Name = "sessionId")]
            public string SessionId;

            public DeleteQrSession(string SessionId)
            {
                this.SessionId = SessionId;
            }

            public DeleteQrSession()
            {
                this.SessionId = "";
            }

            string IReducerArgs.ReducerName => "DeleteQRSession";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags DeleteQrSessionFlags;
        public void DeleteQrSession(CallReducerFlags flags) => DeleteQrSessionFlags = flags;
    }
}
