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
        public delegate void UpdateUserHandler(ReducerEventContext ctx, SpacetimeDB.Identity userId, string? login, string? passwordHash, int? role, string? phoneNumber, string? email, bool? isActive, SpacetimeDB.Identity? actingUser);
        public event UpdateUserHandler? OnUpdateUser;

        public void UpdateUser(SpacetimeDB.Identity userId, string? login, string? passwordHash, int? role, string? phoneNumber, string? email, bool? isActive, SpacetimeDB.Identity? actingUser)
        {
            conn.InternalCallReducer(new Reducer.UpdateUser(userId, login, passwordHash, role, phoneNumber, email, isActive, actingUser), this.SetCallReducerFlags.UpdateUserFlags);
        }

        public bool InvokeUpdateUser(ReducerEventContext ctx, Reducer.UpdateUser args)
        {
            if (OnUpdateUser == null) return false;
            OnUpdateUser(
                ctx,
                args.UserId,
                args.Login,
                args.PasswordHash,
                args.Role,
                args.PhoneNumber,
                args.Email,
                args.IsActive,
                args.ActingUser
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class UpdateUser : Reducer, IReducerArgs
        {
            [DataMember(Name = "userId")]
            public SpacetimeDB.Identity UserId;
            [DataMember(Name = "login")]
            public string? Login;
            [DataMember(Name = "passwordHash")]
            public string? PasswordHash;
            [DataMember(Name = "role")]
            public int? Role;
            [DataMember(Name = "phoneNumber")]
            public string? PhoneNumber;
            [DataMember(Name = "email")]
            public string? Email;
            [DataMember(Name = "isActive")]
            public bool? IsActive;
            [DataMember(Name = "actingUser")]
            public SpacetimeDB.Identity? ActingUser;

            public UpdateUser(
                SpacetimeDB.Identity UserId,
                string? Login,
                string? PasswordHash,
                int? Role,
                string? PhoneNumber,
                string? Email,
                bool? IsActive,
                SpacetimeDB.Identity? ActingUser
            )
            {
                this.UserId = UserId;
                this.Login = Login;
                this.PasswordHash = PasswordHash;
                this.Role = Role;
                this.PhoneNumber = PhoneNumber;
                this.Email = Email;
                this.IsActive = IsActive;
                this.ActingUser = ActingUser;
            }

            public UpdateUser()
            {
            }

            string IReducerArgs.ReducerName => "UpdateUser";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags UpdateUserFlags;
        public void UpdateUser(CallReducerFlags flags) => UpdateUserFlags = flags;
    }
}
