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
        public delegate void RegisterUserHandler(ReducerEventContext ctx, string login, string password, string email, string phoneNumber, uint? roleId, string? roleName, SpacetimeDB.Identity? actingUser, string? newUserIdentity);
        public event RegisterUserHandler? OnRegisterUser;

        public void RegisterUser(string login, string password, string email, string phoneNumber, uint? roleId, string? roleName, SpacetimeDB.Identity? actingUser, string? newUserIdentity)
        {
            conn.InternalCallReducer(new Reducer.RegisterUser(login, password, email, phoneNumber, roleId, roleName, actingUser, newUserIdentity), this.SetCallReducerFlags.RegisterUserFlags);
        }

        public bool InvokeRegisterUser(ReducerEventContext ctx, Reducer.RegisterUser args)
        {
            if (OnRegisterUser == null) return false;
            OnRegisterUser(
                ctx,
                args.Login,
                args.Password,
                args.Email,
                args.PhoneNumber,
                args.RoleId,
                args.RoleName,
                args.ActingUser,
                args.NewUserIdentity
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class RegisterUser : Reducer, IReducerArgs
        {
            [DataMember(Name = "login")]
            public string Login;
            [DataMember(Name = "password")]
            public string Password;
            [DataMember(Name = "email")]
            public string Email;
            [DataMember(Name = "phoneNumber")]
            public string PhoneNumber;
            [DataMember(Name = "roleId")]
            public uint? RoleId;
            [DataMember(Name = "roleName")]
            public string? RoleName;
            [DataMember(Name = "actingUser")]
            public SpacetimeDB.Identity? ActingUser;
            [DataMember(Name = "newUserIdentity")]
            public string? NewUserIdentity;

            public RegisterUser(
                string Login,
                string Password,
                string Email,
                string PhoneNumber,
                uint? RoleId,
                string? RoleName,
                SpacetimeDB.Identity? ActingUser,
                string? NewUserIdentity
            )
            {
                this.Login = Login;
                this.Password = Password;
                this.Email = Email;
                this.PhoneNumber = PhoneNumber;
                this.RoleId = RoleId;
                this.RoleName = RoleName;
                this.ActingUser = ActingUser;
                this.NewUserIdentity = NewUserIdentity;
            }

            public RegisterUser()
            {
                this.Login = "";
                this.Password = "";
                this.Email = "";
                this.PhoneNumber = "";
            }

            string IReducerArgs.ReducerName => "RegisterUser";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags RegisterUserFlags;
        public void RegisterUser(CallReducerFlags flags) => RegisterUserFlags = flags;
    }
}
