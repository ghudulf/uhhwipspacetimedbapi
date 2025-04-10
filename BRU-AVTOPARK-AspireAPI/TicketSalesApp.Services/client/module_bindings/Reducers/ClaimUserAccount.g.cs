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
        public delegate void ClaimUserAccountHandler(ReducerEventContext ctx, string login, string password, string? newUserIdentity);
        public event ClaimUserAccountHandler? OnClaimUserAccount;

        public void ClaimUserAccount(string login, string password, string? newUserIdentity)
        {
            conn.InternalCallReducer(new Reducer.ClaimUserAccount(login, password, newUserIdentity), this.SetCallReducerFlags.ClaimUserAccountFlags);
        }

        public bool InvokeClaimUserAccount(ReducerEventContext ctx, Reducer.ClaimUserAccount args)
        {
            if (OnClaimUserAccount == null) return false;
            OnClaimUserAccount(
                ctx,
                args.Login,
                args.Password,
                args.NewUserIdentity
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class ClaimUserAccount : Reducer, IReducerArgs
        {
            [DataMember(Name = "login")]
            public string Login;
            [DataMember(Name = "password")]
            public string Password;
            [DataMember(Name = "newUserIdentity")]
            public string? NewUserIdentity;

            public ClaimUserAccount(
                string Login,
                string Password,
                string? NewUserIdentity
            )
            {
                this.Login = Login;
                this.Password = Password;
                this.NewUserIdentity = NewUserIdentity;
            }

            public ClaimUserAccount()
            {
                this.Login = "";
                this.Password = "";
            }

            string IReducerArgs.ReducerName => "ClaimUserAccount";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags ClaimUserAccountFlags;
        public void ClaimUserAccount(CallReducerFlags flags) => ClaimUserAccountFlags = flags;
    }
}
