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
        public delegate void DeleteUserHandler(ReducerEventContext ctx, SpacetimeDB.Identity userId);
        public event DeleteUserHandler? OnDeleteUser;

        public void DeleteUser(SpacetimeDB.Identity userId)
        {
            conn.InternalCallReducer(new Reducer.DeleteUser(userId), this.SetCallReducerFlags.DeleteUserFlags);
        }

        public bool InvokeDeleteUser(ReducerEventContext ctx, Reducer.DeleteUser args)
        {
            if (OnDeleteUser == null) return false;
            OnDeleteUser(
                ctx,
                args.UserId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class DeleteUser : Reducer, IReducerArgs
        {
            [DataMember(Name = "userId")]
            public SpacetimeDB.Identity UserId;

            public DeleteUser(SpacetimeDB.Identity UserId)
            {
                this.UserId = UserId;
            }

            public DeleteUser()
            {
            }

            string IReducerArgs.ReducerName => "DeleteUser";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags DeleteUserFlags;
        public void DeleteUser(CallReducerFlags flags) => DeleteUserFlags = flags;
    }
}
